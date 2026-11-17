using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>Whether the caller has a signature on file (the answer to a signature upload).</summary>
public sealed record CareSignatureDto(bool HasSignature);

/// <summary>A stored branding image, read on the server and streamed to a signed-in member.</summary>
public sealed record CareImage(byte[] Content, string ContentType);

/// <summary>
/// Wave 1: the clinic's logo (the medical director and admins) and each staff member's own signature (doctors' print on prescriptions and
/// discharge summaries, the cashier's on receipts). Both are private files, as SchoolLink's branding: the real type is checked from the bytes,
/// the image is shrunk to fit its box and re-encoded as WebP (dropping its metadata), saved under a new key starting <c>private/care/</c>
/// (never a public link), and the file it replaces is deleted. Members read them through the API only.
/// </summary>
public sealed class CareBrandingService(IAppDbContext db, ITenantContext tenant, CareAccess access, IFileStorage storage, Saas.Application.School.IStudentPhotoProcessor images)
{
    public const int LogoMaxSide = 600;
    public const int SignatureMaxWidth = 600;
    public const int SignatureMaxHeight = 200;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Logo (leadership) ----------

    /// <summary>Saves a new logo (the medical director or an admin; 403 otherwise). 400 <c>file</c> for anything but a JPEG, PNG or WebP image of at most 5 MB.</summary>
    public async Task<ClinicDto> UploadLogoAsync(Upload upload, CancellationToken ct)
    {
        await EnsureLeadershipAsync(ct);
        var key = await StoreAsync(upload, $"branding/logo-{Guid.NewGuid():N}", LogoMaxSide, LogoMaxSide, ct);
        var profile = await db.ClinicProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");
        var old = profile.LogoKey;
        profile.LogoKey = key;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return CareSetupService.ToDto(profile);
    }

    /// <summary>Removes the logo and deletes its file (leadership). Removing none is not an error.</summary>
    public async Task DeleteLogoAsync(CancellationToken ct)
    {
        await EnsureLeadershipAsync(ct);
        var profile = await db.ClinicProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");
        var old = profile.LogoKey;
        profile.LogoKey = null;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
    }

    /// <summary>The logo image for any member (the app's header and settings); 404 when there is none.</summary>
    public async Task<CareImage> LogoAsync(CancellationToken ct)
    {
        await access.CallerAsync(ct);
        var key = await db.ClinicProfiles.AsNoTracking().Select(c => c.LogoKey).FirstOrDefaultAsync(ct);
        return await ReadAsync(key, "Logo", ct);
    }

    // ---------- Signature (each member, their own) ----------

    /// <summary>Saves the caller's own signature (any member; the support view can't write). 400 <c>file</c> as for the logo.</summary>
    public async Task<CareSignatureDto> UploadSignatureAsync(Upload upload, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (caller.IsSupportView) throw new ForbiddenException();
        var key = await StoreAsync(upload, $"signatures/{caller.UserId:N}-{Guid.NewGuid():N}", SignatureMaxWidth, SignatureMaxHeight, ct);
        var profile = await OwnProfileAsync(caller, ct);
        var old = profile.SignatureKey;
        profile.SignatureKey = key;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
        return new CareSignatureDto(true);
    }

    /// <summary>Removes the caller's own signature and deletes its file.</summary>
    public async Task DeleteSignatureAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (caller.IsSupportView) throw new ForbiddenException();
        var profile = await db.CareStaffProfiles.FirstOrDefaultAsync(p => p.UserId == caller.UserId, ct);
        if (profile?.SignatureKey is not { } old) return;
        profile.SignatureKey = null;
        await db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(old, ct);
    }

    /// <summary>The caller's own signature image; 404 when they have none. Nobody reads another member's signature.</summary>
    public async Task<CareImage> SignatureAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var key = await db.CareStaffProfiles.AsNoTracking().Where(p => p.UserId == caller.UserId).Select(p => p.SignatureKey).FirstOrDefaultAsync(ct);
        return await ReadAsync(key, "Signature", ct);
    }

    /// <summary>A staff member's signature bytes for a printed document, or null when they have none (or the file is gone).</summary>
    internal static async Task<byte[]?> SignatureBytesAsync(IAppDbContext db, IFileStorage storage, Guid? userId, CancellationToken ct)
    {
        if (userId is not { } id) return null;
        var key = await db.CareStaffProfiles.AsNoTracking().Where(p => p.UserId == id).Select(p => p.SignatureKey).FirstOrDefaultAsync(ct);
        return await PrescriptionService.LogoAsync(storage, key, ct);
    }

    // ---------- Internals ----------

    private async Task EnsureLeadershipAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureLeadership(caller);
        if (caller.IsSupportView) throw new ForbiddenException();
    }

    /// <summary>The caller's clinic profile, tracked; created for an active member who has none yet (as leadership's staff edit does).</summary>
    private async Task<CareStaffProfile> OwnProfileAsync(CareCaller caller, CancellationToken ct)
    {
        var profile = await db.CareStaffProfiles.FirstOrDefaultAsync(p => p.UserId == caller.UserId, ct);
        if (profile is not null) return profile;
        var member = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == caller.UserId && m.Status == MembershipStatus.Active, ct)
            ?? throw new ForbiddenException();
        profile = new CareStaffProfile
        {
            TenantId = TenantId, MembershipId = member.Id, UserId = member.UserId, ClinicalRole = caller.ClinicalRole,
        };
        db.CareStaffProfiles.Add(profile);
        return profile;
    }

    private async Task<CareImage> ReadAsync(string? key, string what, CancellationToken ct)
    {
        var bytes = await PrescriptionService.LogoAsync(storage, key, ct) ?? throw new NotFoundException(what);
        return new CareImage(bytes, "image/webp");
    }

    /// <summary>Checks the real type, shrinks it to fit the box and saves it privately under a new key (so a changed image is never served stale).</summary>
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
        var key = $"private/care/{TenantId}/{name}.webp";
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
            // The old file is orphaned, not harmful; nothing points at it any more.
        }
    }
}
