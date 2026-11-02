using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;
using Point = NetTopologySuite.Geometries.Point;

namespace Saas.Application.Property;

public sealed record DuplicatePairDto(string? MyPhotoUrl, string? TheirPhotoUrl, Guid TheirListingId, string TheirListingTitle, string TheirAgency);
public sealed record ModerationItemDto(Guid ListingId, string Title, ListingStatus Status, string? Note, string AgencyName, string AgencySlug,
    bool AgencyVerified, int HostCancellations, int OpenReports, string? CoverUrl, long PriceMinor, PricePeriod PricePeriod, string AreaName,
    IReadOnlyList<DuplicatePairDto> Duplicates, DateTimeOffset SubmittedAt, IReadOnlyList<string> Photos, string PublicUrl);
public sealed record ListingDecisionRequest(bool Approve, string? Note);

public sealed record AdminVerificationDto(Guid Id, Guid TenantId, string AgencyName, string AgencySlug, AgencyKind Kind, VerificationStatus Status,
    string? LicenceNo, string? Note, string SubmittedByName, string SubmittedByEmail, IReadOnlyList<VerificationDocumentDto> Documents,
    DateTimeOffset CreatedAt);
public sealed record VerificationDecisionRequest(bool Approve, string? Note);
public sealed record VerificationFile(Stream? Content, string? RedirectUrl, string ContentType, string FileName);

public sealed record AdminReportDto(Guid Id, Guid ListingId, string ListingTitle, ListingStatus ListingStatus, string AgencyName, ReportReason Reason,
    string? Details, ReportStatus Status, DateTimeOffset CreatedAt);
public sealed record ReportDecisionRequest(bool Uphold);

public sealed record AdminLocationDto(Guid Id, Guid? ParentId, LocationKind Kind, string Name, string Slug, double? Lat, double? Lng, int Position);
public sealed record SaveLocationRequest(string Name, string? Slug, LocationKind Kind, Guid? ParentId, double? Lat, double? Lng, int? Position);

/// <summary>The platform's moderators: listing queue, verification, reports and the location tree. Works across tenants.</summary>
public sealed class PropertyAdminService(IAppDbContext db, PropertyCore core, LocationTree locations, IFileStorage storage,
    NotificationQueue notifications, ICurrentUser user)
{
    // Listings

    public async Task<IReadOnlyList<ModerationItemDto>> ModerationAsync(CancellationToken ct)
    {
        var rows = await (
            from l in db.Listings.IgnoreQueryFilters().AsNoTracking()
            join a in db.AgencyProfiles.IgnoreQueryFilters().AsNoTracking() on l.TenantId equals a.TenantId
            where l.Status == ListingStatus.Pending || l.Status == ListingStatus.Hidden
            orderby l.UpdatedAt ?? l.CreatedAt
            select new { L = l, A = a }).Take(200).ToListAsync(ct);
        var ids = rows.Select(r => r.L.Id).ToList();
        var media = await db.ListingMedia.IgnoreQueryFilters().AsNoTracking().Where(m => ids.Contains(m.ListingId) && m.Status == MediaStatus.Ready).ToListAsync(ct);
        var dupIds = media.Where(m => m.DuplicateOfMediaId != null).Select(m => m.DuplicateOfMediaId!.Value).ToList();
        var theirs = await (
            from m in db.ListingMedia.IgnoreQueryFilters()
            join l in db.Listings.IgnoreQueryFilters() on m.ListingId equals l.Id
            join a in db.AgencyProfiles.IgnoreQueryFilters() on m.TenantId equals a.TenantId
            where dupIds.Contains(m.Id)
            select new { m.Id, m.CardUrl, ListingId = l.Id, l.Title, Agency = a.Name }).ToListAsync(ct);
        var reports = await db.ListingReports.IgnoreQueryFilters().Where(r => ids.Contains(r.ListingId) && r.Status == ReportStatus.Open)
            .GroupBy(r => r.ListingId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var tree = await locations.GetAsync(ct);
        return rows.Select(r =>
        {
            var mine = media.Where(m => m.ListingId == r.L.Id).OrderBy(m => m.Position).ToList();
            var pairs = mine.Where(m => m.DuplicateOfMediaId != null).Select(m => (Mine: m, Theirs: theirs.FirstOrDefault(t => t.Id == m.DuplicateOfMediaId)))
                .Where(p => p.Theirs is not null)
                .Select(p => new DuplicatePairDto(p.Mine.CardUrl, p.Theirs!.CardUrl, p.Theirs.ListingId, p.Theirs.Title, p.Theirs.Agency)).ToList();
            return new ModerationItemDto(r.L.Id, r.L.Title, r.L.Status, r.L.ModerationNote, r.A.Name, r.A.Slug, r.A.VerifiedAt is not null,
                r.A.HostCancellations, reports.GetValueOrDefault(r.L.Id), mine.FirstOrDefault()?.CardUrl, r.L.PriceMinor, r.L.PricePeriod,
                tree.Find(r.L.LocationId)?.Name ?? "", pairs, r.L.UpdatedAt ?? r.L.CreatedAt,
                mine.Select(m => m.FullUrl ?? m.CardUrl).OfType<string>().ToList(), $"{core.Web}/listings/{r.A.Slug}/{r.L.Slug}");
        }).ToList();
    }

    /// <summary>Approving makes the listing live and dismisses its open reports; rejecting needs a note and upholds them.</summary>
    public async Task DecideListingAsync(Guid id, ListingDecisionRequest r, CancellationToken ct)
    {
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) throw BadRequestException.Field("note", "Tell the agency what to change.");
        var tenantId = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).Select(l => (Guid?)l.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Listing");
        core.UseTenantOf(tenantId);
        var listing = await db.Listings.FirstAsync(l => l.Id == id, ct);
        if (listing.Status is not (ListingStatus.Pending or ListingStatus.Hidden)) throw new ConflictException("This listing isn't waiting for a decision.");

        var reports = await db.ListingReports.Where(x => x.ListingId == id && x.Status == ReportStatus.Open).ToListAsync(ct);
        foreach (var report in reports)
        {
            report.Status = r.Approve ? ReportStatus.Dismissed : ReportStatus.Upheld;
            report.ResolvedAt = core.Now;
        }
        if (r.Approve)
        {
            // A hidden listing that came back over the plan (e.g. after a downgrade) can't simply go live again.
            try { await core.EnsureWithinPlanAsync(listing, ct); }
            catch (PlanLimitException)
            {
                throw new ConflictException("This agency is at its plan's listing limit, so approving would put it over. Reject with a note, or ask them to end another listing or upgrade.");
            }
            await core.ActivateAsync(listing, ct);
        }
        else
        {
            listing.Status = ListingStatus.Rejected;
            listing.ModerationNote = r.Note!.Trim();
        }
        if (await core.OwnerEmailAsync(tenantId, ct) is { } owner)
            notifications.Email(owner, r.Approve ? "listing.approved" : "listing.rejected", new()
            {
                ["listing"] = listing.Title, ["note"] = listing.ModerationNote ?? "", ["link"] = $"{core.Web}/studio/listings/{listing.Id}",
            }, tenantId);
        await db.SaveChangesAsync(ct);
    }

    // Verification

    public async Task<IReadOnlyList<AdminVerificationDto>> VerificationsAsync(VerificationStatus? status, CancellationToken ct)
    {
        var rows = await (
            from v in db.VerificationRequests.IgnoreQueryFilters().AsNoTracking()
            join a in db.AgencyProfiles.IgnoreQueryFilters().AsNoTracking() on v.TenantId equals a.TenantId
            join u in db.Users.AsNoTracking() on v.SubmittedBy equals u.Id
            where status == null || v.Status == status
            orderby v.CreatedAt
            select new { V = v, AgencyName = a.Name, AgencySlug = a.Slug, u.FullName, u.Email }).Take(200).ToListAsync(ct);
        return rows.Select(x => new AdminVerificationDto(x.V.Id, x.V.TenantId, x.AgencyName, x.AgencySlug, x.V.Kind, x.V.Status, x.V.LicenceNo,
            x.V.Note, x.FullName, x.Email ?? "", VerificationService.ToDto(x.V).Documents, x.V.CreatedAt)).ToList();
    }

    public async Task<VerificationFile> DocumentAsync(Guid id, int index, CancellationToken ct)
    {
        var request = await db.VerificationRequests.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundException("Verification");
        var doc = request.Documents.ElementAtOrDefault(index) ?? throw new NotFoundException("Document");
        if (storage.TemporaryUrl(doc.Key, TimeSpan.FromMinutes(10)) is { } url) return new VerificationFile(null, url, doc.ContentType, doc.FileName);
        var stream = await storage.OpenReadAsync(doc.Key, ct) ?? throw new NotFoundException("Document");
        return new VerificationFile(stream, null, doc.ContentType, doc.FileName);
    }

    /// <summary>Approving gives the agency and the submitter the badge and releases listings that were only waiting on it.</summary>
    public async Task DecideVerificationAsync(Guid id, VerificationDecisionRequest r, CancellationToken ct)
    {
        if (!r.Approve && string.IsNullOrWhiteSpace(r.Note)) throw BadRequestException.Field("note", "Tell the agency what's missing.");
        var tenantId = await db.VerificationRequests.IgnoreQueryFilters().Where(v => v.Id == id).Select(v => (Guid?)v.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Verification");
        core.UseTenantOf(tenantId);
        var request = await db.VerificationRequests.FirstAsync(v => v.Id == id, ct);
        if (request.Status != VerificationStatus.Pending) throw new ConflictException("This request was already handled.");
        request.Status = r.Approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        request.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        request.ReviewedBy = user.UserId;
        request.ReviewedAt = core.Now;

        if (r.Approve)
        {
            var agency = await db.AgencyProfiles.FirstAsync(ct);
            agency.VerifiedAt = core.Now;
            var agent = await db.AgentProfiles.FirstOrDefaultAsync(a => a.UserId == request.SubmittedBy, ct);
            if (agent is not null)
            {
                agent.VerifiedAt = core.Now;
                if (!string.IsNullOrWhiteSpace(request.LicenceNo)) agent.LicenceNo = request.LicenceNo;
            }
            var waiting = await db.Listings.Where(l => l.Status == ListingStatus.Pending).ToListAsync(ct);
            var flagged = await db.ListingMedia.Where(m => m.DuplicateOfMediaId != null).Select(m => m.ListingId).Distinct().ToListAsync(ct);
            var reported = await db.ListingReports.Where(x => x.Status == ReportStatus.Open).Select(x => x.ListingId).Distinct().ToListAsync(ct);
            foreach (var listing in waiting.Where(l => !flagged.Contains(l.Id) && !reported.Contains(l.Id)))
                await core.ActivateAsync(listing, ct);
        }
        if (await core.OwnerEmailAsync(tenantId, ct) is { } owner)
            notifications.Email(owner, r.Approve ? "verification.approved" : "verification.rejected", new()
            {
                ["note"] = request.Note ?? "", ["link"] = $"{core.Web}/studio/verification",
            }, tenantId);
        await db.SaveChangesAsync(ct);
    }

    // Reports

    public async Task<IReadOnlyList<AdminReportDto>> ReportsAsync(ReportStatus? status, CancellationToken ct) =>
        await (
            from r in db.ListingReports.IgnoreQueryFilters().AsNoTracking()
            join l in db.Listings.IgnoreQueryFilters().AsNoTracking() on r.ListingId equals l.Id
            join a in db.AgencyProfiles.IgnoreQueryFilters().AsNoTracking() on r.TenantId equals a.TenantId
            where r.Status == (status ?? ReportStatus.Open)
            orderby r.CreatedAt
            select new AdminReportDto(r.Id, l.Id, l.Title, l.Status, a.Name, r.Reason, r.Details, r.Status, r.CreatedAt)).Take(500).ToListAsync(ct);

    /// <summary>Upholding hides the listing; dismissing the last open report brings a hidden listing back.</summary>
    public async Task DecideReportAsync(Guid id, ReportDecisionRequest r, CancellationToken ct)
    {
        var tenantId = await db.ListingReports.IgnoreQueryFilters().Where(x => x.Id == id).Select(x => (Guid?)x.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Report");
        core.UseTenantOf(tenantId);
        var report = await db.ListingReports.FirstAsync(x => x.Id == id, ct);
        if (report.Status != ReportStatus.Open) throw new ConflictException("This report was already handled.");
        report.Status = r.Uphold ? ReportStatus.Upheld : ReportStatus.Dismissed;
        report.ResolvedAt = core.Now;
        var listing = await db.Listings.FirstAsync(l => l.Id == report.ListingId, ct);
        if (r.Uphold && listing.Status == ListingStatus.Active)
        {
            listing.Status = ListingStatus.Hidden;
            listing.ModerationNote = "Hidden after a report was upheld. Fix the listing and contact support.";
        }
        else if (!r.Uphold && listing.Status == ListingStatus.Hidden
            && !await db.ListingReports.AnyAsync(x => x.ListingId == listing.Id && x.Id != id
                && (x.Status == ReportStatus.Open || x.Status == ReportStatus.Upheld), ct))
            await core.ActivateAsync(listing, ct);
        await db.SaveChangesAsync(ct);
    }

    // Locations

    public async Task<IReadOnlyList<AdminLocationDto>> LocationsAsync(CancellationToken ct) =>
        (await locations.GetAsync(ct)).All.OrderBy(n => n.Kind).ThenBy(n => n.Position).ThenBy(n => n.Name)
            .Select(n => new AdminLocationDto(n.Id, n.ParentId, n.Kind, n.Name, n.Slug, n.Lat, n.Lng, n.Position)).ToList();

    public async Task<AdminLocationDto> SaveLocationAsync(Guid? id, SaveLocationRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80) throw BadRequestException.Field("name", "Enter a name of up to 80 characters.");
        Location? parent = null;
        if (r.ParentId is { } parentId)
            parent = await db.Locations.FirstOrDefaultAsync(l => l.Id == parentId, ct) ?? throw BadRequestException.Field("parentId", "Choose the parent location.");
        var expectedParent = r.Kind switch { LocationKind.State => (LocationKind?)null, LocationKind.City => LocationKind.State, _ => LocationKind.City };
        if (parent?.Kind != expectedParent) throw BadRequestException.Field("parentId", r.Kind switch
        {
            LocationKind.State => "A state has no parent.",
            LocationKind.City => "A city belongs to a state.",
            _ => "An area belongs to a city.",
        });
        var slug = Slugs.From(string.IsNullOrWhiteSpace(r.Slug) ? (r.Kind == LocationKind.Area && parent is not null ? $"{r.Name} {parent.Slug}" : r.Name) : r.Slug);
        if (await db.Locations.AnyAsync(l => l.Slug == slug && l.Id != id, ct)) throw BadRequestException.Field("slug", "That link is taken.");

        Location location;
        if (id is null)
        {
            location = new Location { Name = r.Name.Trim(), Slug = slug, Kind = r.Kind };
            db.Locations.Add(location);
        }
        else location = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Location");
        location.Name = r.Name.Trim();
        location.Slug = slug;
        location.Kind = r.Kind;
        location.ParentId = parent?.Id;
        location.Position = r.Position ?? location.Position;
        if (r.Lat is { } lat && r.Lng is { } lng) location.Centre = new Point(lng, lat) { SRID = 4326 };
        await db.SaveChangesAsync(ct);
        locations.Invalidate();
        return new AdminLocationDto(location.Id, location.ParentId, location.Kind, location.Name, location.Slug, location.Centre?.Y, location.Centre?.X, location.Position);
    }
}
