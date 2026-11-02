using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

/// <summary>Signed, expiring links that let an agent renew a listing from an email without signing in.</summary>
public interface IListingTokens
{
    string Renew(Guid listingId);
    /// <summary>The listing id, or null when the token is invalid or expired.</summary>
    Guid? ReadRenew(string token);
}

public sealed record RenewResultDto(string Title, ListingStatus Status, DateTimeOffset? ExpiresAt, string? Note);

/// <summary>
/// Scheduled work across every agency: expiry warnings and expiry, daily saved-search digests, viewing reminders,
/// unpaid short-let holds, finished stays and ended boosts. Each step is idempotent, so running it often is safe.
/// </summary>
public sealed class PropertyJobs(IAppDbContext db, SearchAlerts alerts, NotificationQueue notifications,
    IListingTokens tokens, AppUrls urls, ILogger<PropertyJobs> logger)
{
    public const int DigestSize = 10;
    private string Web => urls.Web(Products.Property);

    public async Task<int> WarnExpiringAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Listings.IgnoreQueryFilters()
            .Where(l => l.Status == ListingStatus.Active && l.ExpiryWarnedAt == null && l.ExpiresAt != null && l.ExpiresAt > now
                && l.ExpiresAt <= now + PropertyRules.ExpiryWarning)
            .Take(500).ToListAsync(ct);
        foreach (var listing in due)
        {
            listing.ExpiryWarnedAt = now;
            if (await AgentEmailAsync(listing, ct) is { } email)
                notifications.Email(email, "listing.expiring", new()
                {
                    ["listing"] = listing.Title, ["expires"] = InspectionService.When(listing.ExpiresAt!.Value),
                    ["renewLink"] = $"{Web}/renew/{tokens.Renew(listing.Id)}", ["link"] = $"{Web}/studio/listings/{listing.Id}",
                }, listing.TenantId);
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    public async Task<int> ExpireAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Listings.IgnoreQueryFilters().Where(l => l.Status == ListingStatus.Active && l.ExpiresAt != null && l.ExpiresAt <= now)
            .Take(500).ToListAsync(ct);
        foreach (var listing in due)
        {
            listing.Status = ListingStatus.Expired;
            if (await AgentEmailAsync(listing, ct) is { } email)
                notifications.Email(email, "listing.expired", new()
                {
                    ["listing"] = listing.Title, ["renewLink"] = $"{Web}/renew/{tokens.Renew(listing.Id)}", ["link"] = $"{Web}/studio/listings/{listing.Id}",
                }, listing.TenantId);
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    /// <summary>One email per daily saved search with listings first published since the last one; nothing when there's nothing new.</summary>
    public async Task<int> SendDailyDigestsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var dueBefore = now.AddHours(-23);
        var searches = await db.SavedSearches.Where(s => s.Frequency == AlertFrequency.Daily && (s.LastSentAt == null || s.LastSentAt <= dueBefore))
            .Take(1000).ToListAsync(ct);
        if (searches.Count == 0) return 0;
        var since = searches.Min(s => s.LastSentAt ?? now.AddDays(-1));
        var fresh = await (from l in db.Listings.IgnoreQueryFilters()
                           join a in db.AgencyProfiles.IgnoreQueryFilters() on l.TenantId equals a.TenantId
                           where l.Status == ListingStatus.Active && l.FirstPublishedAt > since
                           orderby l.FirstPublishedAt descending
                           select new { L = l, AgencySlug = a.Slug }).Take(2000).ToListAsync(ct);
        var inputs = new List<(Listing Listing, string AgencySlug, ListingMatchInput Input)>();
        foreach (var f in fresh) inputs.Add((f.L, f.AgencySlug, await alerts.MatchInputAsync(f.L, ct)));
        var userIds = searches.Select(s => s.UserId).Distinct().ToList();
        var emails = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        var sent = 0;
        foreach (var search in searches)
        {
            var since1 = search.LastSentAt ?? now.AddDays(-1);
            var matches = new List<(Listing Listing, string AgencySlug)>();
            foreach (var x in inputs.Where(x => x.Listing.FirstPublishedAt > since1))
                if (matches.Count < DigestSize && await alerts.MatchesAsync(search, x.Input, ct)) matches.Add((x.Listing, x.AgencySlug));
            search.LastSentAt = now;
            if (matches.Count == 0 || emails.GetValueOrDefault(search.UserId) is not { } email) continue;
            notifications.Email(email, "search.digest", new()
            {
                ["search"] = search.Name, ["count"] = matches.Count.ToString(),
                ["listings"] = string.Join("\n", matches.Select(m =>
                    $"- {m.Listing.Title}, {Money.Format(m.Listing.PriceMinor, m.Listing.Currency)}: {Web}/listings/{m.AgencySlug}/{m.Listing.Slug}")),
                ["link"] = $"{Web}/me/searches",
            }, userId: search.UserId);
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }

    public async Task<int> RemindInspectionsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.Inspections.IgnoreQueryFilters()
            .Where(i => i.Status == InspectionStatus.Confirmed && !i.ReminderSent && i.ScheduledAt > now && i.ScheduledAt <= now.AddHours(24))
            .Take(500).ToListAsync(ct);
        foreach (var inspection in due)
        {
            inspection.ReminderSent = true;
            var title = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == inspection.ListingId).Select(l => l.Title).FirstOrDefaultAsync(ct);
            var agent = await db.AgentProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == inspection.AgentProfileId, ct);
            if (title is null || agent is null) continue; // the listing or agent is gone; nothing to remind about
            var email = await db.Users.Where(u => u.Id == inspection.SeekerUserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            if (email is not null)
                notifications.Email(email, "inspection.reminder", new()
                {
                    ["listing"] = title, ["when"] = InspectionService.When(inspection.ScheduledAt), ["agent"] = agent.DisplayName,
                    ["phone"] = agent.Phone ?? "", ["link"] = $"{Web}/me/viewings",
                }, inspection.TenantId);
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    /// <summary>Unpaid short-let holds free their dates after 15 minutes.</summary>
    public Task<int> ExpireStayHoldsAsync(DateTimeOffset now, CancellationToken ct) =>
        db.Stays.IgnoreQueryFilters().Where(s => s.Status == StayStatus.Pending && s.HoldExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Expired), ct);

    /// <summary>Stays whose check-out day has come (Lagos time) are completed, which lets guests review the agent.</summary>
    public Task<int> CompleteStaysAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, InspectionService.Zone).DateTime);
        return db.Stays.IgnoreQueryFilters().Where(s => s.Status == StayStatus.Confirmed && s.CheckOut <= today)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Completed), ct);
    }

    public Task<int> ExpireBoostsAsync(DateTimeOffset now, CancellationToken ct) =>
        db.Boosts.IgnoreQueryFilters().Where(b => b.Status == BoostStatus.Active && b.EndsAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, BoostStatus.Expired), ct);

    /// <summary>Runs every step; one failing step is logged and the rest still run.</summary>
    public async Task RunAllAsync(DateTimeOffset now, CancellationToken ct)
    {
        (string Name, Func<Task<int>> Run)[] steps =
        [
            ("expire stay holds", () => ExpireStayHoldsAsync(now, ct)), ("complete stays", () => CompleteStaysAsync(now, ct)),
            ("expire boosts", () => ExpireBoostsAsync(now, ct)), ("warn expiring listings", () => WarnExpiringAsync(now, ct)),
            ("expire listings", () => ExpireAsync(now, ct)), ("remind viewings", () => RemindInspectionsAsync(now, ct)),
            ("daily digests", () => SendDailyDigestsAsync(now, ct)),
        ];
        foreach (var (name, run) in steps)
        {
            try
            {
                await run();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Property job step {Step} failed", name);
                db.ChangeTracker.Clear();
            }
        }
    }

    private Task<string?> AgentEmailAsync(Listing listing, CancellationToken ct) =>
        (from a in db.AgentProfiles.IgnoreQueryFilters() join u in db.Users on a.UserId equals u.Id where a.Id == listing.AgentProfileId select u.Email)
            .FirstOrDefaultAsync(ct);
}

/// <summary>Renewing from the link in the expiry email, without signing in.</summary>
public sealed class ListingRenewService(IAppDbContext db, IListingTokens tokens, PropertyCore core, ListingStudioService studio)
{
    /// <summary>What the renew link would renew, without renewing it, so the page can ask first.</summary>
    public async Task<RenewResultDto> PreviewAsync(string token, CancellationToken ct)
    {
        var id = tokens.ReadRenew(token) ?? throw BadRequestException.Field("token", "This renew link is invalid or has expired. Sign in to renew instead.");
        var listing = await db.Listings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Listing");
        return new RenewResultDto(listing.Title, listing.Status, listing.ExpiresAt, listing.ModerationNote);
    }

    public async Task<RenewResultDto> RenewAsync(string token, CancellationToken ct)
    {
        var id = tokens.ReadRenew(token) ?? throw BadRequestException.Field("token", "This renew link is invalid or has expired. Sign in to renew instead.");
        var tenantId = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).Select(l => (Guid?)l.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Listing");
        core.UseTenantOf(tenantId);
        var listing = await studio.RenewAsync(id, ct);
        return new RenewResultDto(listing.Title, listing.Status, listing.ExpiresAt, listing.ModerationNote);
    }
}
