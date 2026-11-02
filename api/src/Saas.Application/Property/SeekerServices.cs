using Saas.Domain.Core;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

// Saved listings and saved searches

public sealed record SaveSearchRequest(string Name, ListingFilter Filter, AlertFrequency Frequency);
public sealed record SavedSearchDto(Guid Id, string Name, ListingFilter Filter, AlertFrequency Frequency, DateTimeOffset? LastSentAt, DateTimeOffset CreatedAt);

public sealed class SaveSearchRequestValidator : AbstractValidator<SaveSearchRequest>
{
    public SaveSearchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Filter).NotNull();
        RuleFor(x => x.Filter.Bbox).Must(b => b is null || b.Length == 4).WithName("filter.bbox").WithMessage("A map area has four numbers.")
            .When(x => x.Filter is not null);
    }
}

/// <summary>A seeker's own lists: saved listings and saved searches with email alerts.</summary>
public sealed class SeekerService(IAppDbContext db, ICurrentUser user, LocationTree locations, PropertySearchService search)
{
    public const int MaxSearches = 20;

    private Guid Me => user.UserId ?? throw new ForbiddenException();

    public async Task SaveListingAsync(Guid listingId, CancellationToken ct)
    {
        var me = Me;
        if (!await db.Listings.IgnoreQueryFilters().AnyAsync(l => l.Id == listingId && l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active), ct)) throw new NotFoundException("Listing");
        if (await db.SavedListings.AnyAsync(s => s.UserId == me && s.ListingId == listingId, ct)) return;
        db.SavedListings.Add(new SavedListing { UserId = me, ListingId = listingId });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsaveListingAsync(Guid listingId, CancellationToken ct)
    {
        var me = Me;
        await db.SavedListings.Where(s => s.UserId == me && s.ListingId == listingId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Saved listings that are still live, newest save first.</summary>
    public async Task<IReadOnlyList<ListingCardDto>> SavedListingsAsync(CancellationToken ct)
    {
        var me = Me;
        var ids = await db.SavedListings.Where(s => s.UserId == me).OrderByDescending(s => s.CreatedAt).Select(s => s.ListingId).Take(200).ToListAsync(ct);
        return await search.CardsForAsync(ids, ct);
    }

    public async Task<IReadOnlyList<SavedSearchDto>> SearchesAsync(CancellationToken ct)
    {
        var me = Me;
        return (await db.SavedSearches.AsNoTracking().Where(s => s.UserId == me).OrderByDescending(s => s.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<SavedSearchDto> SaveSearchAsync(Guid? id, SaveSearchRequest r, CancellationToken ct)
    {
        var me = Me;
        var filter = await CleanAsync(r.Filter, ct);
        SavedSearch saved;
        if (id is null)
        {
            if (await db.SavedSearches.CountAsync(s => s.UserId == me, ct) >= MaxSearches)
                throw BadRequestException.Field("name", $"You can save up to {MaxSearches} searches. Delete one first.");
            saved = new SavedSearch { UserId = me, Name = r.Name.Trim(), Filter = filter, LastSentAt = DateTimeOffset.UtcNow };
            db.SavedSearches.Add(saved);
        }
        else saved = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct) ?? throw new NotFoundException("Saved search");
        saved.Name = r.Name.Trim();
        saved.Filter = filter;
        saved.Frequency = r.Frequency;
        await db.SaveChangesAsync(ct);
        return ToDto(saved);
    }

    public async Task DeleteSearchAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        if (await db.SavedSearches.Where(s => s.Id == id && s.UserId == me).ExecuteDeleteAsync(ct) == 0) throw new NotFoundException("Saved search");
    }

    /// <summary>Checks slugs and the map area so an alert can't silently match nothing (or everything).</summary>
    private async Task<ListingFilter> CleanAsync(ListingFilter f, CancellationToken ct)
    {
        var tree = await locations.GetAsync(ct);
        if (!string.IsNullOrWhiteSpace(f.Location) && tree.Find(f.Location) is null) throw BadRequestException.Field("filter.location", "Unknown location.");
        if (!string.IsNullOrWhiteSpace(f.Type) && !await db.PropertyTypes.AnyAsync(t => t.Slug == f.Type, ct)) throw BadRequestException.Field("filter.type", "Unknown property type.");
        if (f.Amenities is { Length: > 0 } a && await db.Amenities.CountAsync(x => a.Contains(x.Slug), ct) != a.Distinct().Count())
            throw BadRequestException.Field("filter.amenities", "Unknown amenity.");
        if (f.MinPrice is { } min && f.MaxPrice is { } max && min > max) throw BadRequestException.Field("filter.minPrice", "The minimum price is above the maximum.");
        if (f.Bbox is { } bbox) PropertySearchService.ParseBbox(string.Join(',', bbox.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        return f with { Location = f.Location?.Trim().ToLowerInvariant(), Sort = null };
    }

    private static SavedSearchDto ToDto(SavedSearch s) => new(s.Id, s.Name, s.Filter, s.Frequency, s.LastSentAt, s.CreatedAt);
}

/// <summary>Emails seekers whose saved searches match a listing. Instant alerts go out when a listing first goes live.</summary>
public sealed class SearchAlerts(IAppDbContext db, LocationTree locations, NotificationQueue notifications, AppUrls urls)
{
    public async Task<ListingMatchInput> MatchInputAsync(Listing l, CancellationToken ct)
    {
        var type = await db.PropertyTypes.Where(t => t.Id == l.PropertyTypeId).Select(t => t.Slug).FirstAsync(ct);
        var amenityIds = await db.ListingAmenities.IgnoreQueryFilters().Where(a => a.ListingId == l.Id).Select(a => a.AmenityId).ToListAsync(ct);
        var amenities = await db.Amenities.Where(a => amenityIds.Contains(a.Id)).Select(a => a.Slug).ToListAsync(ct);
        var verified = await db.AgencyProfiles.IgnoreQueryFilters().AnyAsync(a => a.TenantId == l.TenantId && a.VerifiedAt != null, ct);
        return new ListingMatchInput(l.Purpose, l.LocationId, type, l.PriceMinor, l.Bedrooms, amenities.ToHashSet(), verified, l.Point.Y, l.Point.X);
    }

    public async Task<bool> MatchesAsync(SavedSearch s, ListingMatchInput input, CancellationToken ct)
    {
        var tree = await locations.GetAsync(ct);
        HashSet<Guid>? subtree = null;
        if (!string.IsNullOrWhiteSpace(s.Filter.Location))
        {
            if (tree.Find(s.Filter.Location) is not { } node) return false;
            subtree = tree.Subtree(node.Id);
        }
        return PropertyRules.Matches(s.Filter, input, subtree);
    }

    public async Task InstantAsync(Listing l, CancellationToken ct)
    {
        var searches = await db.SavedSearches.AsNoTracking().Where(s => s.Frequency == AlertFrequency.Instant).ToListAsync(ct);
        if (searches.Count == 0) return;
        var input = await MatchInputAsync(l, ct);
        var agencySlug = await db.AgencyProfiles.IgnoreQueryFilters().Where(a => a.TenantId == l.TenantId).Select(a => a.Slug).FirstAsync(ct);
        var link = $"{urls.Web(Products.Property)}/listings/{agencySlug}/{l.Slug}";
        var matched = new List<SavedSearch>();
        foreach (var s in searches)
            if (await MatchesAsync(s, input, ct)) matched.Add(s);
        var userIds = matched.Select(s => s.UserId).Distinct().ToList();
        var emails = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        foreach (var s in matched.DistinctBy(s => s.UserId))
            if (emails.GetValueOrDefault(s.UserId) is { } email)
                notifications.Email(email, "search.alert", new()
                {
                    ["search"] = s.Name, ["listing"] = l.Title, ["price"] = Money.Format(l.PriceMinor, l.Currency), ["link"] = link,
                }, userId: s.UserId);
    }
}

// Agent reviews

public sealed record ReviewRequest(int Rating, string? Comment);
public sealed record ReviewDecisionRequest(bool Restore);
public sealed record AdminReviewDto(Guid Id, string AgentName, string AgentSlug, string UserName, int Rating, string? Comment, int ReportCount,
    ReviewStatus Status, DateTimeOffset CreatedAt);

public sealed class ReviewRequestValidator : AbstractValidator<ReviewRequest>
{
    public ReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

/// <summary>Reviews only from seekers who really dealt with the agent: an enquiry a day old, a done viewing, or a completed stay.</summary>
public sealed class AgentReviewService(IAppDbContext db, ICurrentUser user, PropertyCore core)
{
    public static readonly TimeSpan EnquiryAge = TimeSpan.FromHours(24);

    public async Task<AgentReviewDto> ReviewAsync(string agentSlug, ReviewRequest r, CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var agent = await db.AgentProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Slug == agentSlug, ct) ?? throw new NotFoundException("Agent");
        if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == agent.TenantId && m.UserId == me, ct))
            throw new ForbiddenException("You can't review your own agency.");
        var basis = await BasisAsync(agent, me, ct)
            ?? throw new ForbiddenException("You can review an agent after you've enquired, inspected or stayed.");
        if (await db.AgentReviews.IgnoreQueryFilters().AnyAsync(x => x.AgentProfileId == agent.Id && x.UserId == me, ct))
            throw new ConflictException("You've already reviewed this agent.");

        core.UseTenantOf(agent.TenantId);
        var review = new AgentReview
        {
            AgentProfileId = agent.Id, UserId = me, UserName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync(ct),
            Rating = r.Rating, Comment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim(), Basis = basis,
        };
        db.AgentReviews.Add(review);
        await db.SaveChangesAsync(ct);
        await RecomputeAsync(agent.Id, ct);
        return new AgentReviewDto(review.Id, review.UserName, review.Rating, review.Comment, review.Basis, review.CreatedAt);
    }

    private async Task<ReviewBasis?> BasisAsync(AgentProfile agent, Guid me, CancellationToken ct)
    {
        var stayed = await (from s in db.Stays.IgnoreQueryFilters() join l in db.Listings.IgnoreQueryFilters() on s.ListingId equals l.Id
                            where s.GuestUserId == me && l.AgentProfileId == agent.Id && s.Status == StayStatus.Completed select s.Id).AnyAsync(ct);
        if (stayed) return ReviewBasis.Stay;
        if (await db.Inspections.IgnoreQueryFilters().AnyAsync(i => i.SeekerUserId == me && i.AgentProfileId == agent.Id && i.Status == InspectionStatus.Done, ct))
            return ReviewBasis.Inspection;
        var cutoff = core.Now - EnquiryAge;
        if (await db.Enquiries.IgnoreQueryFilters().AnyAsync(e => e.UserId == me && e.AgentProfileId == agent.Id && e.CreatedAt <= cutoff, ct))
            return ReviewBasis.Enquiry;
        return null;
    }

    /// <summary>Anyone signed in can report a review; it's hidden until a moderator restores it.</summary>
    public async Task ReportAsync(Guid reviewId, CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var review = await db.AgentReviews.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == reviewId, ct) ?? throw new NotFoundException("Review");
        if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == review.TenantId && m.UserId == me, ct))
            throw new ForbiddenException("You can't report reviews of your own agency. Contact support if a review breaks the rules.");
        core.UseTenantOf(review.TenantId);
        // A published review that already has reports was restored by a moderator: further reports are counted, not acted on.
        var restored = review.Status == ReviewStatus.Published && review.ReportCount > 0;
        review.ReportCount++;
        if (!restored) review.Status = ReviewStatus.Hidden;
        await db.SaveChangesAsync(ct);
        await RecomputeAsync(review.AgentProfileId, ct);
    }

    public async Task<IReadOnlyList<AdminReviewDto>> HiddenAsync(CancellationToken ct) =>
        await (from r in db.AgentReviews.IgnoreQueryFilters().AsNoTracking()
               join a in db.AgentProfiles.IgnoreQueryFilters() on r.AgentProfileId equals a.Id
               where r.Status == ReviewStatus.Hidden
               orderby r.UpdatedAt ?? r.CreatedAt
               select new AdminReviewDto(r.Id, a.DisplayName, a.Slug, r.UserName, r.Rating, r.Comment, r.ReportCount, r.Status, r.CreatedAt)).Take(200).ToListAsync(ct);

    public async Task DecideAsync(Guid reviewId, ReviewDecisionRequest r, CancellationToken ct)
    {
        var review = await db.AgentReviews.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == reviewId, ct) ?? throw new NotFoundException("Review");
        core.UseTenantOf(review.TenantId);
        review.Status = r.Restore ? ReviewStatus.Published : ReviewStatus.Hidden;
        await db.SaveChangesAsync(ct);
        await RecomputeAsync(review.AgentProfileId, ct);
    }

    private async Task RecomputeAsync(Guid agentId, CancellationToken ct)
    {
        var ratings = await db.AgentReviews.IgnoreQueryFilters().Where(x => x.AgentProfileId == agentId && x.Status == ReviewStatus.Published)
            .Select(x => x.Rating).ToListAsync(ct);
        var average = ratings.Count == 0 ? 0 : Math.Round(ratings.Average(), 1);
        await db.AgentProfiles.IgnoreQueryFilters().Where(a => a.Id == agentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RatingAverage, average).SetProperty(a => a.RatingCount, ratings.Count), ct);
    }
}
