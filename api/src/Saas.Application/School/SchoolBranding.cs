using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School;

/// <summary>Leave a field out (null) to keep it. An empty motto or address clears it; the name cannot be empty.</summary>
public sealed record SaveSchoolProfileRequest(string? Name, string? Motto, string? Address);
public sealed record SchoolBrandingDto(string Name, string? Motto, string? Address, string? LogoUrl, bool HasSignature);

/// <summary>
/// The school's name, motto, address, logo and the principal's signature. Owner and Admin change them. The logo and the signature are
/// private files: resized, re-encoded as WebP and shown through short-lived links (logo) or read on the server (report cards).
/// </summary>
public sealed class SchoolBrandingService(IAppDbContext db, ITenantContext tenant, IFileStorage storage, IStudentPhotoProcessor images, SchoolPhotoSigner signer)
{
    public const int LogoMaxSide = 600;
    public const int SignatureMaxWidth = 600;
    public const int SignatureMaxHeight = 200;
    public const int MaxNameLength = 120;
    public const int MaxMottoLength = 200;
    public const int MaxAddressLength = 300;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>The current name, motto, address, logo link and whether a signature is on file: what the school settings page edits.</summary>
    public async Task<SchoolBrandingDto> GetProfileAsync(CancellationToken ct) =>
        Dto(await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School"));

    public async Task<SchoolBrandingDto> UpdateProfileAsync(SaveSchoolProfileRequest r, CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        if (r.Name is not null)
        {
            var name = r.Name.Trim();
            if (name.Length == 0) throw BadRequestException.Field("name", "Enter the school's name.");
            if (name.Length > MaxNameLength) throw BadRequestException.Field("name", $"The name can have at most {MaxNameLength} characters.");
            profile.Name = name;
        }
        if (r.Motto is not null) profile.Motto = Clean(r.Motto, MaxMottoLength, "motto");
        if (r.Address is not null) profile.Address = Clean(r.Address, MaxAddressLength, "address");
        await db.SaveChangesAsync(ct);
        return Dto(profile);
    }

    public async Task<SchoolBrandingDto> UploadLogoAsync(Upload upload, CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var key = await StoreAsync(upload, "logo", LogoMaxSide, LogoMaxSide, ct);
        var old = profile.LogoKey;
        profile.LogoKey = key;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return Dto(profile);
    }

    public async Task<SchoolBrandingDto> UploadSignatureAsync(Upload upload, CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var key = await StoreAsync(upload, "signature", SignatureMaxWidth, SignatureMaxHeight, ct);
        var old = profile.SignatureKey;
        profile.SignatureKey = key;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return Dto(profile);
    }

    public async Task DeleteLogoAsync(CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var old = profile.LogoKey;
        profile.LogoKey = null;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
    }

    public async Task DeleteSignatureAsync(CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var old = profile.SignatureKey;
        profile.SignatureKey = null;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
    }

    private SchoolBrandingDto Dto(Domain.School.SchoolProfile p) =>
        new(p.Name, p.Motto, p.Address, p.LogoKey is null ? null : signer.LogoUrl(p.TenantId), p.SignatureKey is not null);

    private static string? Clean(string value, int max, string field)
    {
        var text = value.Trim();
        if (text.Length > max) throw BadRequestException.Field(field, $"The {field} can have at most {max} characters.");
        return text.Length == 0 ? null : text;
    }

    /// <summary>Checks the real type, resizes to fit the box and saves privately under a new key (so a changed image is never served from a stale link).</summary>
    private async Task<string> StoreAsync(Upload upload, string name, int maxWidth, int maxHeight, CancellationToken ct)
    {
        var (buffer, _, _) = await ImageUploads.ReadAsync(upload, ct);
        byte[] webp;
        using (buffer)
        {
            try
            {
                webp = images.ToWebp(buffer, maxWidth, maxHeight);
            }
            catch (InvalidDataException)
            {
                throw BadRequestException.Field("file", "We couldn't read this image. Upload a JPEG, PNG or WebP image.");
            }
        }
        var key = $"private/school/{TenantId}/branding/{name}-{Guid.NewGuid():N}.webp";
        await using var stream = new MemoryStream(webp);
        await storage.SaveAsync(key, stream, "image/webp", ct);
        return key;
    }

    private async Task DeleteQuietlyAsync(string? key, CancellationToken ct)
    {
        if (key is null) return;
        try
        {
            await storage.DeleteAsync(key, ct);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            // The old file is orphaned, not harmful; the profile already points elsewhere.
        }
    }
}
