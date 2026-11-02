using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Property;

namespace Saas.Application.Property;

/// <summary>Shared property logic: the current agency, agent profiles, publishing rules and public URLs.</summary>
public sealed class PropertyCore(IAppDbContext db, ITenantContext tenant, ICurrentUser user, PlanService plans, AgentSlugs slugs, SearchAlerts alerts, AppUrls urls, TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();

    public string Web => urls.Web(Products.Property);
    public string ListingUrl(string agencySlug, string listingSlug) => $"{Web}/listings/{agencySlug}/{listingSlug}";
    public string AgencyUrl(string slug) => $"{Web}/agencies/{slug}";
    public string AgentUrl(string slug) => $"{Web}/agents/{slug}";

    public async Task<AgencyProfile> AgencyAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        return await db.AgencyProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Agency");
    }

    /// <summary>The signed-in member's agent profile, created the first time they need one (e.g. after joining a team).</summary>
    public async Task<AgentProfile> MyAgentAsync(CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenException();
        var profile = await db.AgentProfiles.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (profile is not null) return profile;
        var name = await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstAsync(ct);
        profile = new AgentProfile { UserId = userId, DisplayName = name, Slug = await slugs.UniqueAsync(name, ct) };
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>
    /// The one way a listing goes live (auto-publish, moderator approval, verification). The term, the "Listed" date and the
    /// last-confirmed date start now when the listing comes from the moderation queue (time waiting for review never counts against
    /// its term) or when its old term has passed. A hidden listing that comes back keeps its term. Saved by the caller.
    /// </summary>
    public async Task ActivateAsync(Listing listing, CancellationToken ct)
    {
        var now = Now;
        var first = listing.FirstPublishedAt is null;
        var fromQueue = listing.Status is not (ListingStatus.Active or ListingStatus.Hidden);
        listing.Status = ListingStatus.Active;
        listing.ModerationNote = null;
        listing.PublishedAt ??= now;
        if (fromQueue || listing.ExpiresAt is null || listing.ExpiresAt <= now)
        {
            listing.PublishedAt = now;
            listing.LastConfirmedAt = now;
            listing.ExpiryWarnedAt = null;
            listing.ExpiresAt = PropertyRules.ExpiresAt(now, await ExpiryDaysAsync(ct));
        }
        listing.FirstPublishedAt ??= now;
        if (first) await alerts.InstantAsync(listing, ct);
    }

    /// <summary>For admin work that crosses tenants: act as the tenant that owns a record.</summary>
    public void UseTenantOf(Guid tenantId)
    {
        if (tenant.TenantId != tenantId) tenant.SetTenant(tenantId);
    }

    /// <summary>The email of the business owner, for notifications.</summary>
    public Task<string?> OwnerEmailAsync(Guid tenantId, CancellationToken ct) =>
        db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == tenantId && m.Role == MemberRole.Owner)
            .Select(m => m.User!.Email).FirstOrDefaultAsync(ct);

    public async Task<int> ExpiryDaysAsync(CancellationToken ct)
    {
        var days = (await plans.GetCurrentPlanAsync(ct)).Limit(LimitKeys.ListingExpiryDays);
        return days > 0 ? (int)days : 30;
    }

    /// <summary>A 402 when the agency already has more live listings than its plan allows (e.g. after a downgrade). Live, waiting and hidden listings count.</summary>
    public async Task EnsureWithinPlanAsync(Listing listing, CancellationToken ct)
    {
        var others = await db.Listings.CountAsync(l => l.Id != listing.Id && (l.Status == ListingStatus.Active || l.Status == ListingStatus.Pending || l.Status == ListingStatus.Hidden), ct);
        await plans.EnsureCanAddAsync(LimitKeys.Listings, others, "Listings", ct);
    }

    /// <summary>
    /// Puts a listing up for publishing. It goes live at once when the agency is verified, has no open reports and no photo
    /// matches another agency's; otherwise it waits in the moderation queue. Live, waiting and hidden listings count toward the plan.
    /// </summary>
    public async Task PublishAsync(Listing listing, CancellationToken ct)
    {
        var counted = await db.Listings.CountAsync(l => l.Id != listing.Id && (l.Status == ListingStatus.Active || l.Status == ListingStatus.Pending || l.Status == ListingStatus.Hidden), ct);
        await plans.EnsureCanAddAsync(LimitKeys.Listings, counted, "Listings", ct);

        var media = await db.ListingMedia.Where(m => m.ListingId == listing.Id).ToListAsync(ct);
        if (media.FirstOrDefault(m => m.Status != MediaStatus.Ready) is { } notReady)
            throw BadRequestException.Field("media", notReady.Status == MediaStatus.Failed
                ? $"Photo \"{notReady.FileName}\" couldn't be processed. Remove it or upload it again."
                : $"Photo \"{notReady.FileName}\" is still processing. Try again in a moment.");

        var agency = await AgencyAsync(ct);
        var openReports = await db.ListingReports.AnyAsync(r => r.ListingId == listing.Id && r.Status == ReportStatus.Open, ct);
        var duplicate = media.Any(m => m.DuplicateOfMediaId is not null);

        if (agency.VerifiedAt is not null && !openReports && !duplicate)
        {
            await ActivateAsync(listing, ct); // starts a fresh term: the listing comes from Draft, Rejected, Expired, Rented or Sold
        }
        else
        {
            // No term yet: it starts when a moderator (or verification) puts the listing live.
            listing.ExpiresAt = null;
            listing.ExpiryWarnedAt = null;
            listing.Status = ListingStatus.Pending;
            listing.ModerationNote = duplicate ? "A photo matches another agency's listing. A moderator will check it." : null;
        }
    }
}

public sealed class PropertyProvisioner(IAppDbContext db, AgentSlugs slugs) : Auth.IProductProvisioner
{
    public string Product => Products.Property;

    public async Task ProvisionAsync(Tenant tenant, Auth.SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        var kind = Enum.TryParse<AgencyKind>(request.AccountKind, true, out var k) ? k : AgencyKind.Agency;
        db.AgencyProfiles.Add(new AgencyProfile
        {
            TenantId = tenant.Id, Name = tenant.Name, Slug = tenant.Slug, Kind = kind, Currency = tenant.Currency, Email = request.Email.Trim().ToLowerInvariant(),
        });
        db.AgentProfiles.Add(new AgentProfile
        {
            TenantId = tenant.Id, UserId = ownerUserId, DisplayName = request.FullName.Trim(), Slug = await slugs.UniqueAsync(request.FullName, ct),
        });
    }
}

/// <summary>Agent slugs are global, so they are checked across tenants (signup runs before a tenant is set).</summary>
public sealed class AgentSlugs(IAppDbContext db)
{
    public async Task<string> UniqueAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slugs.From(name);
        if (baseSlug.Length < 3) baseSlug = "agent";
        var slug = baseSlug;
        for (var i = 2; await db.AgentProfiles.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug, ct); i++) slug = $"{baseSlug}-{i}";
        return slug;
    }
}

public sealed class PropertyUsage(IAppDbContext db) : IUsageProvider
{
    public string Product => Products.Property;

    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        var live = await db.Listings.CountAsync(l => l.Status == ListingStatus.Active || l.Status == ListingStatus.Pending || l.Status == ListingStatus.Hidden, ct);
        return [new(LimitKeys.Listings, "Listings (live, in review or hidden)", live, plan.Limit(LimitKeys.Listings))];
    }
}
