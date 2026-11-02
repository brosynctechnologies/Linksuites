using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Property;
using Point = NetTopologySuite.Geometries.Point;

namespace Saas.Application.Property;

/// <summary>An agency's own listings and profiles.</summary>
public sealed class ListingStudioService(IAppDbContext db, PropertyCore core, IFileStorage storage)
{
    public async Task<IReadOnlyList<StudioListingSummaryDto>> ListAsync(ListingStatus? status, CancellationToken ct)
    {
        var query = db.Listings.AsNoTracking();
        if (status is { } s) query = query.Where(l => l.Status == s);
        var listings = await query.OrderByDescending(l => l.UpdatedAt ?? l.CreatedAt).Take(500).ToListAsync(ct);
        var ids = listings.Select(l => l.Id).ToList();
        var areaIds = listings.Select(l => l.LocationId).Distinct().ToList();
        var areas = await db.Locations.Where(a => areaIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var covers = (await db.ListingMedia.Where(m => ids.Contains(m.ListingId) && m.Status == MediaStatus.Ready)
                .Select(m => new { m.ListingId, m.Position, m.ThumbUrl }).ToListAsync(ct))
            .GroupBy(m => m.ListingId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Position).First().ThumbUrl);
        return listings.Select(l => new StudioListingSummaryDto(l.Id, l.Title, l.Slug, l.Purpose, l.Status, l.PriceMinor, l.Currency, l.PricePeriod,
            areas.GetValueOrDefault(l.LocationId, ""), covers.GetValueOrDefault(l.Id), l.ViewsCount, l.ExpiresAt, l.FeaturedUntil, l.TopUntil,
            l.UpdatedAt ?? l.CreatedAt)).ToList();
    }

    public async Task<StudioListingDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadAsync(id, ct), ct);

    public async Task<StudioListingDto> CreateAsync(SaveListingRequest r, CancellationToken ct)
    {
        await CheckReferencesAsync(r, ct);
        var agent = r.AgentProfileId is { } agentId
            ? await db.AgentProfiles.FirstOrDefaultAsync(a => a.Id == agentId, ct) ?? throw BadRequestException.Field("agentProfileId", "Choose an agent from your team.")
            : await core.MyAgentAsync(ct);
        var listing = new Listing { Title = r.Title.Trim(), Slug = await UniqueSlugAsync(r.Title, null, ct), Point = PointAt(r.Lat, r.Lng), AgentProfileId = agent.Id };
        Apply(listing, r);
        db.Listings.Add(listing);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(listing, ct);
    }

    public async Task<StudioListingDto> UpdateAsync(Guid id, SaveListingRequest r, CancellationToken ct)
    {
        await CheckReferencesAsync(r, ct);
        var listing = await LoadAsync(id, ct);
        if (r.AgentProfileId is { } agentId && agentId != listing.AgentProfileId)
        {
            if (!await db.AgentProfiles.AnyAsync(a => a.Id == agentId, ct)) throw BadRequestException.Field("agentProfileId", "Choose an agent from your team.");
            listing.AgentProfileId = agentId;
        }
        listing.Title = r.Title.Trim();
        listing.Point = PointAt(r.Lat, r.Lng);
        Apply(listing, r);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(listing, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        var today = StayService.Today(core.Now);
        if (await db.Stays.AnyAsync(x => x.ListingId == id && x.CheckOut > today
                && (x.Status == StayStatus.Confirmed || (x.Status == StayStatus.Pending && x.HoldExpiresAt > core.Now)), ct))
            throw new ConflictException("Guests have upcoming stays here. Cancel them first, or mark the listing as rented or sold instead.");
        if (await db.Inspections.AnyAsync(x => x.ListingId == id && x.ScheduledAt > core.Now
                && (x.Status == InspectionStatus.Requested || x.Status == InspectionStatus.Confirmed), ct))
            throw new ConflictException("People have viewings booked here. Cancel them first, or mark the listing as rented or sold instead.");
        var media = await db.ListingMedia.Where(m => m.ListingId == id).ToListAsync(ct);
        db.Listings.Remove(listing);
        await db.SaveChangesAsync(ct);
        // Photo files go too; an unprocessed original can still carry the uploader's GPS location.
        foreach (var key in media.SelectMany(ListingMediaService.Keys)) await storage.DeleteAsync(key, ct);
    }

    public async Task<StudioListingDto> SubmitAsync(Guid id, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        if (listing.Status is not (ListingStatus.Draft or ListingStatus.Rejected))
            throw new ConflictException(listing.Status == ListingStatus.Expired ? "This listing has expired. Renew it instead." : "This listing was already submitted.");
        await core.PublishAsync(listing, ct);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(listing, ct);
    }

    /// <summary>Confirms a live listing is still available, or relists an expired, rented or sold one.</summary>
    public async Task<StudioListingDto> RenewAsync(Guid id, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        switch (listing.Status)
        {
            case ListingStatus.Active:
                await core.EnsureWithinPlanAsync(listing, ct);
                listing.LastConfirmedAt = core.Now;
                listing.ExpiryWarnedAt = null;
                listing.ExpiresAt = PropertyRules.ExpiresAt(core.Now, await core.ExpiryDaysAsync(ct));
                break;
            case ListingStatus.Expired or ListingStatus.Rented or ListingStatus.Sold:
                await core.PublishAsync(listing, ct);
                break;
            default:
                throw new ConflictException("Only live, expired, rented or sold listings can be renewed.");
        }
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(listing, ct);
    }

    public async Task<StudioListingDto> SetStatusAsync(Guid id, SetListingStatusRequest r, CancellationToken ct)
    {
        var listing = await LoadAsync(id, ct);
        if (r.Status is not (ListingStatus.Rented or ListingStatus.Sold or ListingStatus.Draft))
            throw BadRequestException.Field("status", "Mark the listing as rented, sold or back to draft.");
        if (r.Status == ListingStatus.Rented && listing.Purpose != ListingPurpose.Rent) throw BadRequestException.Field("status", "Only rentals can be marked rented.");
        if (r.Status == ListingStatus.Sold && listing.Purpose != ListingPurpose.Sale) throw BadRequestException.Field("status", "Only sales can be marked sold.");
        if (listing.Status is ListingStatus.Hidden) throw new ConflictException("A moderator is reviewing this listing.");
        listing.Status = r.Status;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(listing, ct);
    }

    // Profiles

    public async Task<AgencyProfileDto> GetAgencyAsync(CancellationToken ct) => ToDto(await core.AgencyAsync(ct));

    public async Task<AgencyProfileDto> UpdateAgencyAsync(UpdateAgencyRequest r, CancellationToken ct)
    {
        var agency = await core.AgencyAsync(ct);
        var slug = r.Slug.Trim().ToLowerInvariant();
        Slugs.EnsureNotNewlyReserved(slug, agency.Slug);
        if (slug != agency.Slug && await db.AgencyProfiles.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug, ct))
            throw BadRequestException.Field("slug", "That link is taken.");
        agency.Name = r.Name.Trim();
        agency.Slug = slug;
        agency.Kind = r.Kind;
        agency.Phone = Clean(r.Phone);
        agency.Email = Clean(r.Email)?.ToLowerInvariant();
        agency.Address = Clean(r.Address);
        agency.Bio = Clean(r.Bio);
        await db.SaveChangesAsync(ct);
        return ToDto(agency);
    }

    public async Task<IReadOnlyList<AgentProfileDto>> AgentsAsync(CancellationToken ct) =>
        (await db.AgentProfiles.AsNoTracking().OrderBy(a => a.DisplayName).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<AgentProfileDto> MyAgentAsync(CancellationToken ct) => ToDto(await core.MyAgentAsync(ct));

    public async Task<AgentProfileDto> UpdateMyAgentAsync(UpdateAgentRequest r, CancellationToken ct)
    {
        var agent = await core.MyAgentAsync(ct);
        agent.DisplayName = r.DisplayName.Trim();
        agent.Bio = Clean(r.Bio);
        agent.Phone = Clean(r.Phone);
        agent.WhatsApp = Clean(r.WhatsApp);
        agent.LicenceNo = Clean(r.LicenceNo);
        await db.SaveChangesAsync(ct);
        return ToDto(agent);
    }

    // Helpers

    public static Point PointAt(double lat, double lng) => new(lng, lat) { SRID = 4326 };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<Listing> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Listings.Include(l => l.Costs).Include(l => l.Amenities).FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Listing");

    private async Task CheckReferencesAsync(SaveListingRequest r, CancellationToken ct)
    {
        if (!await db.PropertyTypes.AnyAsync(t => t.Id == r.PropertyTypeId, ct)) throw BadRequestException.Field("propertyTypeId", "Choose a property type.");
        if (!await db.Locations.AnyAsync(l => l.Id == r.LocationId && l.Kind == LocationKind.Area, ct))
            throw BadRequestException.Field("locationId", "Choose the area the property is in.");
        var amenities = (r.AmenityIds ?? []).Distinct().ToList();
        if (amenities.Count > 0 && await db.Amenities.CountAsync(a => amenities.Contains(a.Id), ct) != amenities.Count)
            throw BadRequestException.Field("amenityIds", "Choose amenities from the list.");
    }

    private static void Apply(Listing l, SaveListingRequest r)
    {
        l.Description = r.Description?.Trim() ?? "";
        l.Purpose = r.Purpose;
        l.PropertyTypeId = r.PropertyTypeId;
        l.LocationId = r.LocationId;
        l.Address = r.Address?.Trim() ?? "";
        l.PriceMinor = r.PriceMinor;
        l.PricePeriod = r.PricePeriod;
        l.Bedrooms = r.Bedrooms;
        l.Bathrooms = r.Bathrooms;
        l.Toilets = r.Toilets;
        l.SizeSqm = r.SizeSqm;
        l.Furnished = r.Furnished;
        l.TourUrl = Clean(r.TourUrl);
        l.VideoUrl = Clean(r.VideoUrl);
        if (r.ShortLet is { } s && r.Purpose == ListingPurpose.ShortLet)
        {
            l.CleaningFeeMinor = s.CleaningFeeMinor;
            l.MinNights = s.MinNights;
            l.MaxGuests = s.MaxGuests;
            l.CheckInTime = Clean(s.CheckInTime);
            l.CheckOutTime = Clean(s.CheckOutTime);
            l.HouseRules = Clean(s.HouseRules);
        }
        l.Costs.Clear();
        var position = 0;
        foreach (var c in r.Costs ?? [])
            l.Costs.Add(new ListingCost { ListingId = l.Id, Kind = c.Kind, Label = c.Label.Trim(), AmountMinor = c.AmountMinor, Position = position++ });
        l.Amenities.Clear();
        foreach (var a in (r.AmenityIds ?? []).Distinct()) l.Amenities.Add(new ListingAmenity { ListingId = l.Id, AmenityId = a });
    }

    private async Task<string> UniqueSlugAsync(string title, Guid? except, CancellationToken ct)
    {
        var baseSlug = Slugs.From(title);
        if (baseSlug.Length < 3) baseSlug = "listing";
        if (baseSlug.Length > 70) baseSlug = baseSlug[..70].TrimEnd('-');
        var slug = baseSlug;
        for (var i = 2; await db.Listings.AnyAsync(l => l.Slug == slug && l.Id != except, ct); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }

    private async Task<StudioListingDto> ToDtoAsync(Listing l, CancellationToken ct)
    {
        var agency = await core.AgencyAsync(ct);
        var media = await MediaDtosAsync(db, l.Id, ct);
        var costs = l.Costs.OrderBy(c => c.Position).Select(c => new CostDto(c.Kind, c.Label, c.AmountMinor)).ToList();
        return new StudioListingDto(l.Id, l.Title, l.Slug, l.Description, l.Purpose, l.PropertyTypeId, l.LocationId, l.Address, l.Point.Y, l.Point.X,
            l.PriceMinor, l.Currency, l.PricePeriod, l.Bedrooms, l.Bathrooms, l.Toilets, l.SizeSqm, l.Furnished, l.TourUrl, l.VideoUrl, l.Status,
            l.ModerationNote, l.PublishedAt, l.ExpiresAt, l.FeaturedUntil, l.TopUntil, l.ViewsCount, costs, l.Amenities.Select(a => a.AmenityId).ToList(),
            l.Purpose == ListingPurpose.ShortLet ? new ShortLetDto(l.CleaningFeeMinor, l.MinNights, l.MaxGuests, l.CheckInTime, l.CheckOutTime, l.HouseRules) : null,
            PropertyRules.MoveInTotal(l.PriceMinor, costs.Select(c => c.AmountMinor)), l.AgentProfileId, core.ListingUrl(agency.Slug, l.Slug),
            media, l.CreatedAt, l.UpdatedAt);
    }

    /// <summary>A listing's photos, with the other agency's listing named when a photo is a duplicate.</summary>
    public static async Task<IReadOnlyList<ListingMediaDto>> MediaDtosAsync(IAppDbContext db, Guid listingId, CancellationToken ct)
    {
        var media = await db.ListingMedia.AsNoTracking().Where(m => m.ListingId == listingId).OrderBy(m => m.Position).ToListAsync(ct);
        var dupIds = media.Where(m => m.DuplicateOfMediaId is not null).Select(m => m.DuplicateOfMediaId!.Value).ToList();
        var dups = dupIds.Count == 0 ? [] : await (
            from m in db.ListingMedia.IgnoreQueryFilters()
            join a in db.AgencyProfiles.IgnoreQueryFilters() on m.TenantId equals a.TenantId
            where dupIds.Contains(m.Id)
            select new { m.Id, m.ListingId, a.Name }).ToListAsync(ct);
        return media.Select(m =>
        {
            var dup = dups.FirstOrDefault(d => d.Id == m.DuplicateOfMediaId);
            return new ListingMediaDto(m.Id, m.FileName, m.ThumbUrl, m.CardUrl, m.FullUrl, m.Status, m.Position, m.Error, dup?.ListingId, dup?.Name);
        }).ToList();
    }

    private AgencyProfileDto ToDto(AgencyProfile a) =>
        new(a.Id, a.Name, a.Slug, a.Kind, a.LogoUrl, a.Phone, a.Email, a.Address, a.Bio, a.VerifiedAt is not null, a.VerifiedAt, core.AgencyUrl(a.Slug));

    private static AgentProfileDto ToDto(AgentProfile a) =>
        new(a.Id, a.UserId, a.DisplayName, a.Slug, a.Bio, a.Phone, a.WhatsApp, a.LicenceNo, a.PhotoUrl, a.VerifiedAt is not null, a.RatingAverage, a.RatingCount);
}
