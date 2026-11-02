using Saas.Domain.Core;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Saas.Application.Common;
using Saas.Domain.Property;
using Location = Saas.Domain.Property.Location;

namespace Saas.Application.Property;

public sealed record LocationDto(Guid Id, string Name, string Slug, LocationKind Kind, string? ParentSlug, double? Lat, double? Lng);
public sealed record ListingCardDto(Guid Id, string Title, string Slug, string AgencySlug, string AgencyName, bool Verified, ListingPurpose Purpose,
    string TypeName, long PriceMinor, string Currency, PricePeriod PricePeriod, int Bedrooms, int Bathrooms, int? SizeSqm, string AreaName,
    string? CityName, string? CoverUrl, int PhotoCount, bool Featured, bool Top, double Lat, double Lng, DateTimeOffset? PublishedAt);
public sealed record MapPinDto(Guid Id, double Lat, double Lng, long PriceMinor, string Slug, string AgencySlug);
public sealed record SearchResultDto(Paged<ListingCardDto> Items, IReadOnlyList<MapPinDto> Pins, LocationDto? Location);

public sealed record PhotoDto(string? ThumbUrl, string? CardUrl, string? FullUrl, int Width, int Height);
public sealed record NamedDto(string Name, string Slug);
public sealed record AgentCardDto(Guid Id, string DisplayName, string Slug, string? PhotoUrl, string? Phone, string? WhatsApp, bool Verified,
    double RatingAverage, int RatingCount);
public sealed record AgencyCardDto(Guid Id, string Name, string Slug, AgencyKind Kind, string? LogoUrl, bool Verified, string? Phone);
public sealed record ListingDetailDto(Guid Id, string Title, string Slug, string Description, ListingPurpose Purpose, NamedDto Type,
    IReadOnlyList<LocationDto> Breadcrumb, string Address, double Lat, double Lng, long PriceMinor, string Currency, PricePeriod PricePeriod,
    int Bedrooms, int Bathrooms, int Toilets, int? SizeSqm, bool Furnished, string? TourUrl, string? VideoUrl, IReadOnlyList<PhotoDto> Photos,
    IReadOnlyList<CostDto> Costs, long MoveInTotalMinor, IReadOnlyList<NamedDto> Amenities, ShortLetDto? ShortLet, AgentCardDto Agent,
    AgencyCardDto Agency, DateTimeOffset? PublishedAt, DateTimeOffset? LastConfirmedAt, IReadOnlyList<ListingCardDto> Similar);

public sealed record AgentReviewDto(Guid Id, string UserName, int Rating, string? Comment, ReviewBasis Basis, DateTimeOffset CreatedAt);
public sealed record AgentPageDto(AgentCardDto Agent, string? Bio, string? LicenceNo, AgencyCardDto Agency, IReadOnlyList<ListingCardDto> Listings,
    IReadOnlyList<AgentReviewDto> Reviews);
public sealed record AgencyPageDto(AgencyCardDto Agency, string? Bio, string? Address, IReadOnlyList<AgentCardDto> Agents,
    IReadOnlyList<ListingCardDto> Listings, double RatingAverage, int RatingCount);

public sealed record MedianDto(int Bedrooms, long MedianMinor, int Count);
public sealed record AreaGuideDto(LocationDto Location, IReadOnlyList<LocationDto> Breadcrumb, IReadOnlyList<LocationDto> Areas, int ActiveCount,
    IReadOnlyList<MedianDto> RentByBedrooms, long? MedianSaleMinor, long? MedianNightlyMinor, IReadOnlyList<ListingCardDto> Latest);

public sealed record SearchQuery(ListingPurpose? Purpose, string? Location, string? Type, long? MinPrice, long? MaxPrice, int? Beds,
    string? Amenities, bool? Verified, string? Bbox, string? Sort, int Page = 1, int PageSize = 24)
{
    public ListingFilter ToFilter() => new(Purpose, Location, Type, MinPrice, MaxPrice, Beds,
        string.IsNullOrWhiteSpace(Amenities) ? null : Amenities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        Verified ?? false, PropertySearchService.ParseBbox(Bbox), Sort);
}

/// <summary>Public, cross-tenant reads: search, listing pages, agents, agencies, locations and area guides. Active listings only.</summary>
public sealed class PropertySearchService(IAppDbContext db, LocationTree locations, TimeProvider clock)
{
    private static readonly GeometryFactory Geo = new(new PrecisionModel(), 4326);
    private const int MaxPins = 500;

    private sealed class Row
    {
        public required Listing L { get; init; }
        public required AgencyProfile A { get; init; }
    }

    private IQueryable<Row> Live() =>
        from l in db.Listings.IgnoreQueryFilters().AsNoTracking()
        join a in db.AgencyProfiles.IgnoreQueryFilters().AsNoTracking() on l.TenantId equals a.TenantId
        where l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active)
        select new Row { L = l, A = a };

    /// <summary>"w,s,e,n" in degrees. Null when absent; a 400 when malformed, so a bad map never returns the whole country.</summary>
    public static double[]? ParseBbox(string? bbox)
    {
        if (string.IsNullOrWhiteSpace(bbox)) return null;
        var parts = bbox.Split(',');
        var values = new double[4];
        if (parts.Length != 4 || parts.Select((p, i) => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])).Any(ok => !ok))
            throw BadRequestException.Field("bbox", "Send the map area as west,south,east,north.");
        var (w, s, e, n) = (values[0], values[1], values[2], values[3]);
        if (values.Any(v => !double.IsFinite(v)) || w < -180 || e > 180 || s < -90 || n > 90 || w >= e || s >= n)
            throw BadRequestException.Field("bbox", "Send the map area as west,south,east,north.");
        return values;
    }

    public async Task<SearchResultDto> SearchAsync(SearchQuery q, CancellationToken ct)
    {
        var f = q.ToFilter();
        if (f.MinPrice is { } min && f.MaxPrice is { } max && min > max) throw BadRequestException.Field("minPrice", "The minimum price is above the maximum.");
        var tree = await locations.GetAsync(ct);
        LocationNode? location = null;
        if (!string.IsNullOrWhiteSpace(f.Location)) location = tree.Find(f.Location) ?? throw new NotFoundException("Location");

        var rows = await FilterAsync(Live(), f, location is null ? null : tree.Subtree(location.Id), ct);
        var now = clock.GetUtcNow();
        var ordered = f.Sort switch
        {
            "newest" => rows.OrderByDescending(x => x.L.PublishedAt),
            "price_asc" => rows.OrderBy(x => x.L.PriceMinor).ThenByDescending(x => x.L.PublishedAt),
            "price_desc" => rows.OrderByDescending(x => x.L.PriceMinor).ThenByDescending(x => x.L.PublishedAt),
            _ => Recommended(rows, now),
        };
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 48);
        var total = await rows.CountAsync(ct);
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var pins = await rows.OrderByDescending(x => x.L.PublishedAt).Take(MaxPins).Select(x => new { x.L.Id, x.L.Point, x.L.PriceMinor, x.L.Slug, AgencySlug = x.A.Slug }).ToListAsync(ct);
        return new SearchResultDto(new Paged<ListingCardDto>(await CardsAsync(items, tree, ct), page, pageSize, total),
            pins.Select(p => new MapPinDto(p.Id, p.Point.Y, p.Point.X, p.PriceMinor, p.Slug, p.AgencySlug)).ToList(), location is null ? null : ToDto(location, tree));
    }

    /// <summary>How many featured listings the home page shows at once.</summary>
    public const int HomeFeaturedLimit = 24;

    /// <summary>
    /// The home page's featured row: every listing with a running Featured boost, from any city and of any purpose. When more are
    /// featured than fit, a fresh random order on each request lets them take turns.
    /// </summary>
    public async Task<IReadOnlyList<ListingCardDto>> FeaturedAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await Live().Where(x => x.L.FeaturedUntil != null && x.L.FeaturedUntil > now)
            .OrderBy(x => EF.Functions.Random()).Take(HomeFeaturedLimit).ToListAsync(ct);
        return await CardsAsync(rows, await locations.GetAsync(ct), ct);
    }

    private static IOrderedQueryable<Row> Recommended(IQueryable<Row> rows, DateTimeOffset now) =>
        rows.OrderByDescending(x => x.L.TopUntil != null && x.L.TopUntil > now)
            .ThenByDescending(x => x.L.FeaturedUntil != null && x.L.FeaturedUntil > now)
            .ThenByDescending(x => x.A.VerifiedAt != null)
            .ThenByDescending(x => x.L.PublishedAt);

    private async Task<IQueryable<Row>> FilterAsync(IQueryable<Row> rows, ListingFilter f, HashSet<Guid>? subtree, CancellationToken ct)
    {
        if (f.Purpose is { } purpose) rows = rows.Where(x => x.L.Purpose == purpose);
        if (subtree is not null) rows = rows.Where(x => subtree.Contains(x.L.LocationId));
        if (!string.IsNullOrWhiteSpace(f.Type))
        {
            var typeId = await db.PropertyTypes.Where(t => t.Slug == f.Type).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct)
                ?? throw BadRequestException.Field("type", "Unknown property type.");
            rows = rows.Where(x => x.L.PropertyTypeId == typeId);
        }
        if (f.MinPrice is { } min) rows = rows.Where(x => x.L.PriceMinor >= min);
        if (f.MaxPrice is { } max) rows = rows.Where(x => x.L.PriceMinor <= max);
        if (f.MinBeds is { } beds) rows = rows.Where(x => x.L.Bedrooms >= beds);
        if (f.Amenities is { Length: > 0 } slugs)
        {
            var ids = await db.Amenities.Where(a => slugs.Contains(a.Slug)).Select(a => a.Id).ToListAsync(ct);
            if (ids.Count != slugs.Distinct().Count()) throw BadRequestException.Field("amenities", "Unknown amenity.");
            var needed = ids.Count;
            rows = rows.Where(x => db.ListingAmenities.IgnoreQueryFilters().Count(a => a.ListingId == x.L.Id && ids.Contains(a.AmenityId)) == needed);
        }
        if (f.VerifiedOnly) rows = rows.Where(x => x.A.VerifiedAt != null);
        if (f.Bbox is [var w, var s, var e, var n])
        {
            var area = Geo.ToGeometry(new Envelope(w, e, s, n));
            rows = rows.Where(x => x.L.Point.Intersects(area));
        }
        return rows;
    }

    private async Task<List<ListingCardDto>> CardsAsync(IReadOnlyList<Row> rows, LocationTree.Snapshot tree, CancellationToken ct)
    {
        var ids = rows.Select(r => r.L.Id).ToList();
        var photos = (await db.ListingMedia.IgnoreQueryFilters().AsNoTracking().Where(m => ids.Contains(m.ListingId) && m.Status == MediaStatus.Ready)
                .Select(m => new { m.ListingId, m.Position, m.CardUrl }).ToListAsync(ct))
            .GroupBy(m => m.ListingId).ToDictionary(g => g.Key, g => (Cover: g.OrderBy(m => m.Position).First().CardUrl, Count: g.Count()));
        var types = await db.PropertyTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var now = clock.GetUtcNow();
        return rows.Select(r =>
        {
            var area = tree.Find(r.L.LocationId);
            var city = area?.ParentId is { } p ? tree.Find(p) : null;
            var photo = photos.GetValueOrDefault(r.L.Id);
            return new ListingCardDto(r.L.Id, r.L.Title, r.L.Slug, r.A.Slug, r.A.Name, r.A.VerifiedAt is not null, r.L.Purpose,
                types.GetValueOrDefault(r.L.PropertyTypeId, ""), r.L.PriceMinor, r.L.Currency, r.L.PricePeriod, r.L.Bedrooms, r.L.Bathrooms,
                r.L.SizeSqm, area?.Name ?? "", city?.Name, photo.Cover, photo.Count, r.L.FeaturedUntil > now, r.L.TopUntil > now,
                r.L.Point.Y, r.L.Point.X, r.L.PublishedAt);
        }).ToList();
    }

    /// <summary>Cards for the given listings that are still live, in the given order.</summary>
    public async Task<IReadOnlyList<ListingCardDto>> CardsForAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var rows = await Live().Where(x => ids.Contains(x.L.Id)).ToListAsync(ct);
        var cards = await CardsAsync(rows, await locations.GetAsync(ct), ct);
        return cards.OrderBy(c => ids.ToList().IndexOf(c.Id)).ToList();
    }

    public async Task<ListingDetailDto> ListingAsync(string agencySlug, string listingSlug, CancellationToken ct)
    {
        var row = await Live().FirstOrDefaultAsync(x => x.A.Slug == agencySlug && x.L.Slug == listingSlug, ct) ?? throw new NotFoundException("Listing");
        var l = row.L;
        var tree = await locations.GetAsync(ct);
        var type = await db.PropertyTypes.AsNoTracking().FirstAsync(t => t.Id == l.PropertyTypeId, ct);
        var costs = await db.ListingCosts.IgnoreQueryFilters().AsNoTracking().Where(c => c.ListingId == l.Id).OrderBy(c => c.Position)
            .Select(c => new CostDto(c.Kind, c.Label, c.AmountMinor)).ToListAsync(ct);
        var amenityIds = await db.ListingAmenities.IgnoreQueryFilters().Where(a => a.ListingId == l.Id).Select(a => a.AmenityId).ToListAsync(ct);
        var amenities = await db.Amenities.AsNoTracking().Where(a => amenityIds.Contains(a.Id)).OrderBy(a => a.Position)
            .Select(a => new NamedDto(a.Name, a.Slug)).ToListAsync(ct);
        var photos = await db.ListingMedia.IgnoreQueryFilters().AsNoTracking().Where(m => m.ListingId == l.Id && m.Status == MediaStatus.Ready)
            .OrderBy(m => m.Position).Select(m => new PhotoDto(m.ThumbUrl, m.CardUrl, m.FullUrl, m.Width, m.Height)).ToListAsync(ct);
        var agent = await db.AgentProfiles.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == l.AgentProfileId, ct);

        var area = tree.Find(l.LocationId);
        var siblings = area?.ParentId is { } cityId ? tree.Subtree(cityId) : [l.LocationId];
        var similar = await Recommended(Live().Where(x => x.L.Id != l.Id && x.L.Purpose == l.Purpose && siblings.Contains(x.L.LocationId)), clock.GetUtcNow())
            .Take(4).ToListAsync(ct);

        return new ListingDetailDto(l.Id, l.Title, l.Slug, l.Description, l.Purpose, new NamedDto(type.Name, type.Slug),
            tree.Breadcrumb(l.LocationId).Select(n => ToDto(n, tree)).ToList(), l.Address, l.Point.Y, l.Point.X, l.PriceMinor, l.Currency,
            l.PricePeriod, l.Bedrooms, l.Bathrooms, l.Toilets, l.SizeSqm, l.Furnished, l.TourUrl, l.VideoUrl, photos, costs,
            PropertyRules.MoveInTotal(l.PriceMinor, costs.Select(c => c.AmountMinor)), amenities,
            l.Purpose == ListingPurpose.ShortLet ? new ShortLetDto(l.CleaningFeeMinor, l.MinNights, l.MaxGuests, l.CheckInTime, l.CheckOutTime, l.HouseRules) : null,
            ToCard(agent), ToCard(row.A), l.PublishedAt, l.LastConfirmedAt, await CardsAsync(similar, tree, ct));
    }

    public sealed record ReferenceDto(Guid Id, string Name, string Slug);

    public async Task<IReadOnlyList<ReferenceDto>> TypesAsync(CancellationToken ct) =>
        await db.PropertyTypes.AsNoTracking().OrderBy(t => t.Position).Select(t => new ReferenceDto(t.Id, t.Name, t.Slug)).ToListAsync(ct);

    public async Task<IReadOnlyList<ReferenceDto>> AmenitiesAsync(CancellationToken ct) =>
        await db.Amenities.AsNoTracking().OrderBy(a => a.Position).Select(a => new ReferenceDto(a.Id, a.Name, a.Slug)).ToListAsync(ct);

    public async Task<IReadOnlyList<LocationDto>> LocationsAsync(string? parent, CancellationToken ct)
    {
        var tree = await locations.GetAsync(ct);
        Guid? parentId = null;
        if (!string.IsNullOrWhiteSpace(parent)) parentId = (tree.Find(parent) ?? throw new NotFoundException("Location")).Id;
        return tree.Children(parentId).Select(n => ToDto(n, tree)).ToList();
    }

    public async Task<AreaGuideDto> AreaAsync(string slug, CancellationToken ct)
    {
        var tree = await locations.GetAsync(ct);
        var node = tree.Find(slug) ?? throw new NotFoundException("Area");
        var subtree = tree.Subtree(node.Id);
        var live = Live().Where(x => subtree.Contains(x.L.LocationId));
        var prices = await live.Select(x => new { x.L.Purpose, x.L.PricePeriod, x.L.Bedrooms, x.L.PriceMinor }).Take(5000).ToListAsync(ct);
        var rent = prices.Where(p => p.Purpose == ListingPurpose.Rent && p.PricePeriod == PricePeriod.Year).GroupBy(p => Math.Min(p.Bedrooms, 5))
            .OrderBy(g => g.Key).Select(g => new MedianDto(g.Key, Median(g.Select(p => p.PriceMinor)), g.Count())).ToList();
        var sales = prices.Where(p => p.Purpose == ListingPurpose.Sale).Select(p => p.PriceMinor).ToList();
        var nightly = prices.Where(p => p.Purpose == ListingPurpose.ShortLet).Select(p => p.PriceMinor).ToList();
        var latest = await live.OrderByDescending(x => x.L.PublishedAt).Take(8).ToListAsync(ct);
        return new AreaGuideDto(ToDto(node, tree), tree.Breadcrumb(node.Id).Select(n => ToDto(n, tree)).ToList(),
            tree.Children(node.Id).Select(n => ToDto(n, tree)).ToList(), prices.Count, rent,
            sales.Count > 0 ? Median(sales) : null, nightly.Count > 0 ? Median(nightly) : null, await CardsAsync(latest, tree, ct));
    }

    public async Task<AgentPageDto> AgentAsync(string slug, CancellationToken ct)
    {
        var agent = await db.AgentProfiles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug && db.Tenants.Any(t => t.Id == a.TenantId && t.Status == TenantStatus.Active), ct) ?? throw new NotFoundException("Agent");
        var agency = await db.AgencyProfiles.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.TenantId == agent.TenantId, ct);
        var tree = await locations.GetAsync(ct);
        var listings = await Recommended(Live().Where(x => x.L.AgentProfileId == agent.Id), clock.GetUtcNow()).Take(60).ToListAsync(ct);
        var reviews = await db.AgentReviews.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.AgentProfileId == agent.Id && r.Status == ReviewStatus.Published).OrderByDescending(r => r.CreatedAt).Take(50)
            .Select(r => new AgentReviewDto(r.Id, r.UserName, r.Rating, r.Comment, r.Basis, r.CreatedAt)).ToListAsync(ct);
        return new AgentPageDto(ToCard(agent), agent.Bio, agent.LicenceNo, ToCard(agency), await CardsAsync(listings, tree, ct), reviews);
    }

    public async Task<AgencyPageDto> AgencyAsync(string slug, CancellationToken ct)
    {
        var agency = await db.AgencyProfiles.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug && db.Tenants.Any(t => t.Id == a.TenantId && t.Status == TenantStatus.Active), ct) ?? throw new NotFoundException("Agency");
        var agents = await db.AgentProfiles.IgnoreQueryFilters().AsNoTracking().Where(a => a.TenantId == agency.TenantId).OrderBy(a => a.DisplayName).ToListAsync(ct);
        var tree = await locations.GetAsync(ct);
        var listings = await Recommended(Live().Where(x => x.L.TenantId == agency.TenantId), clock.GetUtcNow()).Take(60).ToListAsync(ct);
        var rated = agents.Where(a => a.RatingCount > 0).ToList();
        var count = rated.Sum(a => a.RatingCount);
        var average = count == 0 ? 0 : Math.Round(rated.Sum(a => a.RatingAverage * a.RatingCount) / count, 1);
        return new AgencyPageDto(ToCard(agency), agency.Bio, agency.Address, agents.Select(ToCard).ToList(), await CardsAsync(listings, tree, ct), average, count);
    }

    private static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    public static LocationDto ToDto(LocationNode n, LocationTree.Snapshot tree) =>
        new(n.Id, n.Name, n.Slug, n.Kind, n.ParentId is { } p ? tree.Find(p)?.Slug : null, n.Lat, n.Lng);

    public static AgentCardDto ToCard(AgentProfile a) =>
        new(a.Id, a.DisplayName, a.Slug, a.PhotoUrl, a.Phone, a.WhatsApp, a.VerifiedAt is not null, a.RatingAverage, a.RatingCount);

    public static AgencyCardDto ToCard(AgencyProfile a) => new(a.Id, a.Name, a.Slug, a.Kind, a.LogoUrl, a.VerifiedAt is not null, a.Phone);
}
