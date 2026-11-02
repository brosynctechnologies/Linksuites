using Saas.Domain.Core;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

public sealed record ReportRequest(ReportReason Reason, string? Details);

public sealed class ReportRequestValidator : AbstractValidator<ReportRequest>
{
    public ReportRequestValidator() => RuleFor(x => x.Details).MaximumLength(1000);
}

/// <summary>Anyone can report a listing. Three open reports from different people hide it until a moderator decides.</summary>
public sealed class ReportService(IAppDbContext db, ICurrentUser user, PropertyCore core, NotificationQueue notifications, IKeyedHash hasher, IDbErrors errors)
{
    public const int HideAfter = 3;

    public async Task ReportAsync(Guid listingId, ReportRequest r, CancellationToken ct)
    {
        var listing = await db.Listings.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == listingId && l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Listing");
        // Anonymous reporters are told apart by a keyed hash of their IP, so the stored value can't be reversed by hashing every IP.
        var reporter = user.UserId?.ToString() ?? "ip:" + hasher.Hash("listing-report", user.IpAddress ?? "unknown")[..32];
        var already = new ConflictException("You've already reported this listing. A moderator will look at it.");
        if (await db.ListingReports.IgnoreQueryFilters().AnyAsync(x => x.ListingId == listingId && x.ReporterKey == reporter, ct)) throw already;

        core.UseTenantOf(listing.TenantId);
        db.ListingReports.Add(new ListingReport
        {
            ListingId = listingId, ReporterUserId = user.UserId, ReporterKey = reporter, Reason = r.Reason, Details = r.Details?.Trim(),
        });
        var open = await db.ListingReports.CountAsync(x => x.ListingId == listingId && x.Status == ReportStatus.Open, ct) + 1;
        if (open >= HideAfter)
        {
            listing.Status = ListingStatus.Hidden;
            listing.ModerationNote = "Hidden after several reports while a moderator checks it.";
            if (await core.OwnerEmailAsync(listing.TenantId, ct) is { } owner)
                notifications.Email(owner, "listing.hidden", new() { ["listing"] = listing.Title, ["link"] = $"{core.Web}/studio/listings/{listing.Id}" }, listing.TenantId);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (errors.IsUniqueViolation(ex))
        {
            throw already; // the same person reporting twice at the same moment
        }
    }
}

public sealed record VerificationDocumentDto(int Index, string Label, string FileName, string ContentType);
public sealed record VerificationDto(Guid Id, AgencyKind Kind, VerificationStatus Status, string? LicenceNo, string? Note,
    IReadOnlyList<VerificationDocumentDto> Documents, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);

/// <summary>An agency sends ID and business documents; a platform admin approves them and the agency gets the verified badge.</summary>
public sealed class VerificationService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, IFileStorage storage, PropertyCore core)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public async Task<VerificationDto?> LatestAsync(CancellationToken ct)
    {
        var latest = await db.VerificationRequests.AsNoTracking().OrderByDescending(v => v.CreatedAt).FirstOrDefaultAsync(ct);
        return latest is null ? null : ToDto(latest);
    }

    public async Task<VerificationDto> SubmitAsync(Upload idDocument, Upload? businessDocument, string? licenceNo, CancellationToken ct)
    {
        var agency = await core.AgencyAsync(ct);
        if (agency.VerifiedAt is not null) throw new ConflictException("Your agency is already verified.");
        if (await db.VerificationRequests.AnyAsync(v => v.Status == VerificationStatus.Pending, ct))
            throw new ConflictException("Your documents are being checked. We'll email you when it's done.");
        if (licenceNo is { Length: > 40 }) throw BadRequestException.Field("licenceNo", "Enter a licence number of up to 40 characters.");

        var request = new VerificationRequest { Kind = agency.Kind, SubmittedBy = user.UserId ?? throw new ForbiddenException(), LicenceNo = licenceNo?.Trim() };
        request.Documents.Add(await SaveAsync(request, idDocument, "ID document", "idDocument", 0, ct));
        if (businessDocument is not null) request.Documents.Add(await SaveAsync(request, businessDocument, "Business document", "businessDocument", 1, ct));
        db.VerificationRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return ToDto(request);
    }

    private async Task<VerificationDocument> SaveAsync(VerificationRequest request, Upload file, string label, string field, int index, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > MaxBytes) throw BadRequestException.Field(field, "Upload a file of up to 10 MB.");
        var head = new byte[12];
        await using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var read = buffer.Read(head, 0, head.Length);
        buffer.Position = 0;
        var (ext, type) = ListingMediaService.Sniff(head.AsSpan(0, read)) switch
        {
            "jpg" => ("jpg", "image/jpeg"),
            "png" => ("png", "image/png"),
            "webp" => ("webp", "image/webp"),
            _ when head.AsSpan(0, Math.Min(read, 5)).SequenceEqual("%PDF-"u8) => ("pdf", "application/pdf"),
            _ => throw BadRequestException.Field(field, "Upload a photo (JPEG or PNG) or a PDF."),
        };
        // Under "private/": never served from /uploads; only admins read these through the API.
        var key = $"private/verification/{tenant.TenantId}/{request.Id:N}/{index}.{ext}";
        await storage.SaveAsync(key, buffer, type, ct);
        return new VerificationDocument { Label = label, Key = key, ContentType = type, FileName = Path.GetFileName(file.FileName) };
    }

    public static VerificationDto ToDto(VerificationRequest v) => new(v.Id, v.Kind, v.Status, v.LicenceNo, v.Note,
        v.Documents.Select((d, i) => new VerificationDocumentDto(i, d.Label, d.FileName, d.ContentType)).ToList(), v.CreatedAt, v.ReviewedAt);
}
