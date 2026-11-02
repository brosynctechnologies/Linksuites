using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Payouts;

namespace Saas.Application.Platform;

public sealed record BusinessRowDto(Guid Id, string Name, string Slug, string Product, TenantStatus Status, string PlanName, string? OwnerEmail,
    DateTimeOffset CreatedAt);
public sealed record MemberDto(Guid UserId, string Name, string? Email, MemberRole Role, MembershipStatus Status, DateTimeOffset? LastLoginAt);
public sealed record SubscriptionSummaryDto(Guid Id, string PlanCode, string PlanName, long PriceMinor, SubscriptionStatus Status,
    DateTimeOffset CurrentPeriodStart, DateTimeOffset? CurrentPeriodEnd, string? Provider);
public sealed record AuditRowDto(long Id, Guid? TenantId, Guid? ActorId, string? ActorName, string Action, string EntityType, string EntityId,
    string? BeforeJson, string? AfterJson, string? Ip, DateTimeOffset CreatedAt, string? BusinessName = null, string? Product = null);
public sealed record BusinessPaymentDto(Guid Id, long AmountMinor, string Currency, string Status, string Reference, DateTimeOffset CreatedAt);
public sealed record BusinessPayoutDto(Guid Id, long AmountMinor, PayoutStatus Status, string? Reference, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt);
public sealed record BusinessDetailDto(Guid Id, string Name, string Slug, string Product, TenantStatus Status, string Country, string Currency,
    DateTimeOffset CreatedAt, string PlanName, SubscriptionSummaryDto? Subscription, IReadOnlyList<UsageItemDto> Usage, IReadOnlyList<MemberDto> Members,
    string WebUrl, IReadOnlyList<AuditRowDto> RecentAudit, IReadOnlyList<BusinessPaymentDto> Payments, IReadOnlyList<BusinessPayoutDto> Payouts);

/// <summary>Every business in every product: search, detail, suspension.</summary>
public sealed class PlatformBusinessService(IAppDbContext db, ITenantContext tenant, PlanService plans, IEnumerable<IUsageProvider> usage,
    PlatformAudit audit, TenantStatusCache statuses, NotificationQueue notifications, AppUrls urls, TimeProvider clock, PlatformViewer viewer)
{
    public async Task<Paged<BusinessRowDto>> SearchAsync(string? q, string? product, TenantStatus? status, string? plan, int page, int pageSize, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // The same current-subscription rule as billing and the detail page (CurrentSubscription), so list, filter and header agree.
        var current = db.Subscriptions.IgnoreQueryFilters().Current(now);
        var rows = from t in db.Tenants.AsNoTracking()
                   let owner = db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == t.Id && m.Role == MemberRole.Owner).Select(m => m.User).FirstOrDefault()
                   let sub = current.Where(s => s.TenantId == t.Id && s.Plan!.Product == t.Product).Select(s => s.Plan).FirstOrDefault()
                   select new { T = t, OwnerEmail = owner!.Email, OwnerPhone = owner.PhoneNumber, OwnerName = owner.FullName, PlanCode = sub!.Code, PlanName = sub.Name };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            // Searching by email or phone is itself a lookup of personal data, so only roles that may see contact details can do it.
            rows = await viewer.SeesContactDetailsAsync(ct)
                ? rows.Where(x => x.T.Name.ToLower().Contains(term) || x.T.Slug.Contains(term) || x.OwnerEmail!.ToLower().Contains(term)
                    || x.OwnerName!.ToLower().Contains(term) || (x.OwnerPhone != null && x.OwnerPhone.Contains(term)))
                : rows.Where(x => x.T.Name.ToLower().Contains(term) || x.T.Slug.Contains(term) || x.OwnerName!.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(product)) rows = rows.Where(x => x.T.Product == product);
        if (status is { } s1) rows = rows.Where(x => x.T.Status == s1);
        if (!string.IsNullOrWhiteSpace(plan)) rows = plan == "free" ? rows.Where(x => x.PlanCode == null || x.PlanCode == "free") : rows.Where(x => x.PlanCode == plan);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.T.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var defaults = await db.Plans.AsNoTracking().Where(p => p.IsDefault).ToDictionaryAsync(p => p.Product, p => p.Name, ct);
        var contacts = await viewer.SeesContactDetailsAsync(ct);
        return new Paged<BusinessRowDto>(items.Select(x => new BusinessRowDto(x.T.Id, x.T.Name, x.T.Slug, x.T.Product, x.T.Status,
            x.PlanName ?? defaults.GetValueOrDefault(x.T.Product, "Free"), contacts ? x.OwnerEmail : null, x.T.CreatedAt)).ToList(), page, pageSize, total);
    }

    public async Task<BusinessDetailDto> DetailAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Business");
        if (tenant.TenantId != id) tenant.SetTenant(id);
        // Header, Comped badge and Subscription panel all read the one current subscription; none running means the free plan.
        var plan = await plans.GetCurrentPlanAsync(ct);
        var sub = await plans.GetCurrentSubscriptionAsync(ct);
        var items = new List<UsageItemDto>();
        foreach (var provider in usage.Where(u => u.Product == t.Product)) items.AddRange(await provider.GetAsync(plan, ct));
        var members = await (from m in db.Memberships.AsNoTracking() join u in db.Users on m.UserId equals u.Id
                             orderby m.Role, u.FullName
                             select new MemberDto(u.Id, u.FullName, u.Email!, m.Role, m.Status, u.LastLoginAt)).ToListAsync(ct);
        if (!await viewer.SeesContactDetailsAsync(ct)) members = members.Select(m => m with { Email = null }).ToList();
        var recent = await AuditRowsAsync(db.AuditLogs.AsNoTracking().Where(a => a.TenantId == id).OrderByDescending(a => a.Id).Take(20), ct);
        // Automatic audit rows hold whole records (customer names, phones, addresses), so only roles that may see contact details get them.
        if (!await viewer.SeesContactDetailsAsync(ct)) recent = recent.Select(a => a with { BeforeJson = null, AfterJson = null }).ToList();
        var payments = await db.Payments.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(20)
            .Select(p => new BusinessPaymentDto(p.Id, p.AmountMinor, p.Currency, p.Status.ToString(), p.ProviderRef, p.CreatedAt)).ToListAsync(ct);
        var payouts = await db.Payouts.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(20)
            .Select(p => new BusinessPayoutDto(p.Id, p.AmountMinor, p.Status, p.Reference, p.CreatedAt, p.PaidAt)).ToListAsync(ct);
        return new BusinessDetailDto(t.Id, t.Name, t.Slug, t.Product, t.Status, t.Country, t.Currency, t.CreatedAt, plan.Name,
            sub?.Plan is null ? null : new SubscriptionSummaryDto(sub.Id, sub.Plan.Code, sub.Plan.Name, sub.PriceMinor ?? sub.Plan.PriceMinor, sub.Status, sub.CurrentPeriodStart,
                sub.CurrentPeriodEnd, sub.Provider),
            items, members, urls.Web(t.Product), recent, payments, payouts);
    }

    public async Task SuspendAsync(Guid id, ReasonRequest r, CancellationToken ct) => await SetStatusAsync(id, TenantStatus.Suspended, r, ct);
    public async Task ReactivateAsync(Guid id, ReasonRequest r, CancellationToken ct) => await SetStatusAsync(id, TenantStatus.Active, r, ct);

    /// <summary>
    /// Suspending hides the business's public pages in every product and blocks its members at their next request; their refresh
    /// tokens are revoked so they can't sign back in. Payments still record. The owner is emailed the reason either way.
    /// </summary>
    private async Task SetStatusAsync(Guid id, TenantStatus to, ReasonRequest r, CancellationToken ct)
    {
        var reason = Reasons.Require(r.Reason);
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Business");
        if (t.Status == TenantStatus.Closed) throw new ConflictException("This business is closed.");
        if (t.Status == to) throw new ConflictException(to == TenantStatus.Suspended ? "This business is already suspended." : "This business is already active.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var before = t.Status;
        t.Status = to;
        audit.Log(to == TenantStatus.Suspended ? "business.suspend" : "business.reactivate", nameof(Tenant), id.ToString(), id,
            new { status = before.ToString() }, new { status = to.ToString(), reason });
        var owner = await db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == id && m.Role == MemberRole.Owner).Select(m => m.User!.Email).FirstOrDefaultAsync(ct);
        if (owner is not null)
            notifications.Email(owner, to == TenantStatus.Suspended ? "business.suspended" : "business.reactivated",
                new() { ["business"] = t.Name, ["reason"] = reason }, id);
        await db.SaveChangesAsync(ct);
        if (to == TenantStatus.Suspended)
            await db.RefreshTokens.Where(x => x.TenantId == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);
        await tx.CommitAsync(ct);
        statuses.Invalidate(id);
    }

    public async Task<List<AuditRowDto>> AuditRowsAsync(IQueryable<AuditLog> query, CancellationToken ct)
    {
        var rows = await query.ToListAsync(ct);
        var actorIds = rows.Where(a => a.ActorId != null).Select(a => a.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var tenantIds = rows.Where(a => a.TenantId != null).Select(a => a.TenantId!.Value).Distinct().ToList();
        var businesses = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => new { t.Name, t.Product }, ct);
        return rows.Select(a =>
        {
            var b = a.TenantId is { } t ? businesses.GetValueOrDefault(t) : null;
            return new AuditRowDto(a.Id, a.TenantId, a.ActorId, a.ActorId is { } x ? names.GetValueOrDefault(x) : null, a.Action, a.EntityType,
                a.EntityId, a.BeforeJson, a.AfterJson, a.Ip, a.CreatedAt, b?.Name, b?.Product);
        }).ToList();
    }
}
