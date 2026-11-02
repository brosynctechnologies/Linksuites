using Saas.Domain.Core;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

public enum ListingEventKind { View, WhatsApp, Call }
public sealed record ListingEventRequest(ListingEventKind Kind);

public sealed record ListingStatsRowDto(Guid ListingId, string Title, ListingStatus Status, int Views, int WhatsAppClicks, int CallClicks, int Enquiries);
public sealed record ListingStatsDto(int Days, int Views, int WhatsAppClicks, int CallClicks, int Enquiries, IReadOnlyList<ListingStatsRowDto> Listings);

/// <summary>Daily counters for views and contact clicks. Views count once per visitor per listing per day.</summary>
public sealed class ListingEventsService(IAppDbContext db, ICurrentUser user, IMemoryCache cache, PlanService plans, TimeProvider clock)
{
    public async Task RecordAsync(Guid listingId, ListingEventKind kind, CancellationToken ct)
    {
        var listing = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId && l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active))
            .Select(l => new { l.Id, l.TenantId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Listing");
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (kind == ListingEventKind.View)
        {
            var visitor = user.UserId?.ToString() ?? user.IpAddress ?? "unknown";
            var key = $"view:{listingId:N}:{day:yyyyMMdd}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(visitor)))[..16]}";
            if (cache.TryGetValue(key, out _)) return;
            cache.Set(key, true, TimeSpan.FromHours(24));
        }
        await AddAsync(db, listing.TenantId, listing.Id, day, kind switch
        {
            ListingEventKind.View => "views",
            ListingEventKind.WhatsApp => "whats_app_clicks",
            _ => "call_clicks",
        }, ct);
        if (kind == ListingEventKind.View)
            await db.Listings.IgnoreQueryFilters().Where(l => l.Id == listing.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ViewsCount, l => l.ViewsCount + 1), ct);
    }

    public static Task AddAsync(IAppDbContext db, Guid tenantId, Guid listingId, DateOnly day, string column, CancellationToken ct) =>
        db.AddListingStatAsync(tenantId, listingId, day, column, ct);

    public async Task<ListingStatsDto> StatsAsync(int days, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.ListingStats, ct))
            throw new PlanLimitException("Views and lead stats come with the Pro and Agency plans. Upgrade to see them.");
        days = Math.Clamp(days, 1, 365);
        var since = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-days + 1);
        var totals = await db.ListingStats.Where(s => s.Day >= since).GroupBy(s => s.ListingId)
            .Select(g => new { ListingId = g.Key, Views = g.Sum(s => s.Views), WhatsApp = g.Sum(s => s.WhatsAppClicks), Calls = g.Sum(s => s.CallClicks), Enquiries = g.Sum(s => s.Enquiries) })
            .ToListAsync(ct);
        var listings = await db.Listings.AsNoTracking().Select(l => new { l.Id, l.Title, l.Status }).ToListAsync(ct);
        var rows = listings.Select(l =>
        {
            var t = totals.FirstOrDefault(x => x.ListingId == l.Id);
            return new ListingStatsRowDto(l.Id, l.Title, l.Status, t?.Views ?? 0, t?.WhatsApp ?? 0, t?.Calls ?? 0, t?.Enquiries ?? 0);
        }).OrderByDescending(r => r.Views).ToList();
        return new ListingStatsDto(days, rows.Sum(r => r.Views), rows.Sum(r => r.WhatsAppClicks), rows.Sum(r => r.CallClicks), rows.Sum(r => r.Enquiries), rows);
    }
}
