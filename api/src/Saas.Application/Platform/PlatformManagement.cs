using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;
using Saas.Domain.Payouts;
using Saas.Domain.Property;

namespace Saas.Application.Platform;

// Plans and comps

public sealed record PlanAdminDto(Guid Id, string Product, string Code, string Name, long PriceMinor, string Currency, BillingInterval Interval,
    IReadOnlyDictionary<string, long> Limits, IReadOnlyList<string> Features, bool IsDefault, bool IsHidden, int SortOrder, int ActiveSubscribers,
    int CompSubscribers);
public sealed record SavePlanRequest(string Product, string Code, string Name, long PriceMinor, bool IsHidden, int SortOrder,
    Dictionary<string, long>? Limits, List<string>? Features);
public sealed record CompRequest(string PlanCode, DateTimeOffset Until, string Reason);

/// <summary>
/// Plans are edited here, never deleted. Price, limits and features changes reach a paying subscriber when it next pays (its period
/// keeps what was bought); businesses on the default free plan have no subscription, so edits to it apply to them at once.
/// </summary>
public sealed partial class PlatformPlansService(IAppDbContext db, PlatformAudit audit, TimeProvider clock)
{
    [GeneratedRegex("^[a-z0-9-]{2,30}$")]
    private static partial Regex CodePattern();

    public async Task<IReadOnlyList<PlanAdminDto>> ListAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Paying subscribers and comps are counted apart: a comp pays nothing, so it is neither revenue nor a "subscriber" of the price.
        var counts = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now))
            .GroupBy(s => new { s.PlanId, Comp = s.Provider == CurrentSubscription.CompProvider }).Select(g => new { g.Key.PlanId, g.Key.Comp, Count = g.Count() })
            .ToListAsync(ct);
        int Count(Guid planId, bool comp) => counts.Where(c => c.PlanId == planId && c.Comp == comp).Sum(c => c.Count);
        return (await db.Plans.AsNoTracking().OrderBy(p => p.Product).ThenBy(p => p.SortOrder).ToListAsync(ct))
            .Select(p => ToDto(p, Count(p.Id, false), Count(p.Id, true))).ToList();
    }

    public async Task<PlanAdminDto> CreateAsync(SavePlanRequest r, CancellationToken ct)
    {
        if (!Products.All.Contains(r.Product)) throw BadRequestException.Field("product", "Unknown product.");
        var code = r.Code.Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(code)) throw BadRequestException.Field("code", "Use 2-30 lowercase letters, numbers and dashes.");
        if (await db.Plans.AnyAsync(p => p.Product == r.Product && p.Code == code, ct)) throw BadRequestException.Field("code", "That code is taken for this product.");
        var plan = new Plan { Product = r.Product, Code = code, Name = "" };
        Apply(plan, r);
        db.Plans.Add(plan);
        audit.Log("plan.create", nameof(Plan), plan.Id.ToString(), after: new { plan.Product, plan.Code, plan.Name, plan.PriceMinor, plan.IsHidden });
        await db.SaveChangesAsync(ct);
        return ToDto(plan, 0);
    }

    public async Task<PlanAdminDto> UpdateAsync(Guid id, SavePlanRequest r, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Plan");
        var before = new { plan.Name, plan.PriceMinor, plan.IsHidden, plan.Limits, plan.Features };
        if (r.IsHidden && plan.IsDefault) throw BadRequestException.Field("isHidden", "The free plan new businesses start on can't be hidden.");
        Apply(plan, r);
        audit.Log("plan.update", nameof(Plan), plan.Id.ToString(), before: before, after: new { plan.Name, plan.PriceMinor, plan.IsHidden, plan.Limits, plan.Features });
        await db.SaveChangesAsync(ct);
        return ToDto(plan, 0);
    }

    /// <summary>Gives a business a plan free until a date (a subscription with provider "comp"); afterwards it falls back on its own.</summary>
    public async Task CompAsync(Guid tenantId, CompRequest r, CancellationToken ct)
    {
        var reason = Reasons.Require(r.Reason);
        if (r.Until <= clock.GetUtcNow()) throw BadRequestException.Field("until", "Choose a date in the future.");
        var product = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Product).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business");
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Product == product && p.Code == r.PlanCode, ct) ?? throw BadRequestException.Field("planCode", "Choose a plan of this product.");
        var comp = new Subscription
        {
            TenantId = tenantId, PlanId = plan.Id, Status = SubscriptionStatus.Active, CurrentPeriodStart = clock.GetUtcNow(),
            CurrentPeriodEnd = r.Until.ToUniversalTime(), Provider = "comp",
        };
        comp.Snapshot(plan, plan.PriceMinor, plan.Currency); // list value, used only to rank it against paid periods; comps aren't revenue
        db.Subscriptions.Add(comp);
        audit.Log("business.comp", nameof(Tenant), tenantId.ToString(), tenantId, after: new { plan = plan.Code, until = r.Until, reason });
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Plan plan, SavePlanRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 60) throw BadRequestException.Field("name", "Enter a name of up to 60 characters.");
        if (r.PriceMinor < 0) throw BadRequestException.Field("priceMinor", "The price can't be negative.");
        plan.Name = r.Name.Trim();
        plan.PriceMinor = r.PriceMinor;
        plan.IsHidden = r.IsHidden;
        plan.SortOrder = r.SortOrder;
        if (r.Limits is not null) plan.Limits = r.Limits;
        if (r.Features is not null) plan.Features = r.Features.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Take(20).ToList();
    }

    private static PlanAdminDto ToDto(Plan p, int subscribers, int comps = 0) => new(p.Id, p.Product, p.Code, p.Name, p.PriceMinor, p.Currency, p.Interval, p.Limits, p.Features,
        p.IsDefault, p.IsHidden, p.SortOrder, subscribers, comps);
}

// Payments

/// <summary>
/// One payment. <see cref="AmountMinor"/> is what was charged or asked for; <see cref="RefundedMinor"/> what went back; and
/// <see cref="CollectedMinor"/> the one "collected" rule for every source: money actually received, less refunds (0 while unpaid).
/// </summary>
public sealed record PaymentRowDto(Guid Id, string Kind, string Product, Guid BusinessId, string BusinessName, long AmountMinor, long RefundedMinor,
    long CollectedMinor, string Currency, string Status, string Reference, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt);

/// <summary>Every payment through the platform: plan subscriptions, course sales, short-let stays and boosts, with refunds.</summary>
public sealed class PlatformMoneyService(IAppDbContext db)
{
    /// <summary>One row per payment, all sources in one UNION ALL so the database filters, sorts and pages the whole set.</summary>
    private sealed record Row(Guid Id, string Kind, string Product, Guid BusinessId, string BusinessName, long AmountMinor, long RefundedMinor,
        long CollectedMinor, string Currency, string Status, string Reference, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt);

    // Static SQL: every filter is applied by LINQ on top, as parameters. Collected = received - refunded, per source:
    //  - course orders: received once paid (Paid or Refunded); a refund returns the whole order.
    //  - stays: every row listed was paid (a stay cancelled before its payment arrived, or paid after its hold ran out, is refunded
    //    in full but still received the money); refund_minor is what went back.
    //  - boosts: received once paid; boosts aren't refunded.
    //  - subscriptions: received when Succeeded or Refunded; a refund returns the whole payment.
    private IQueryable<Row> Rows() => db.SqlQuery<Row>($"""
        SELECT o.id, 'Course' AS kind, 'courses' AS product, o.tenant_id AS business_id, COALESCE(t.name, '') AS business_name,
               o.total_minor AS amount_minor,
               CASE WHEN o.status = 'Refunded' THEN o.total_minor ELSE 0::bigint END AS refunded_minor,
               CASE WHEN o.status = 'Paid' THEN o.total_minor ELSE 0::bigint END AS collected_minor,
               o.currency, o.status, COALESCE(o.reference, '') AS reference, o.created_at, o.paid_at
        FROM course_orders o LEFT JOIN tenants t ON t.id = o.tenant_id WHERE o.total_minor > 0
        UNION ALL
        SELECT s.id, 'Stay', 'property', s.tenant_id, COALESCE(t.name, ''), s.total_minor, s.refund_minor, GREATEST(s.total_minor - s.refund_minor, 0),
               s.currency, s.status, COALESCE(s.reference, ''), s.created_at, s.paid_at
        FROM stays s LEFT JOIN tenants t ON t.id = s.tenant_id WHERE s.paid_at IS NOT NULL OR s.refund_minor > 0
        UNION ALL
        SELECT b.id, 'Boost', 'property', b.tenant_id, COALESCE(t.name, ''), b.amount_minor, 0::bigint, b.amount_minor,
               b.currency, b.status, COALESCE(b.reference, ''), b.created_at, b.paid_at
        FROM boosts b LEFT JOIN tenants t ON t.id = b.tenant_id WHERE b.paid_at IS NOT NULL
        UNION ALL
        SELECT p.id, 'Subscription', COALESCE(t.product, ''), p.tenant_id, COALESCE(t.name, ''), p.amount_minor,
               CASE WHEN p.status = 'Refunded' THEN p.amount_minor ELSE 0::bigint END,
               CASE WHEN p.status = 'Succeeded' THEN p.amount_minor ELSE 0::bigint END,
               p.currency, p.status, COALESCE(p.provider_ref, ''), p.created_at,
               CASE WHEN p.status IN ('Succeeded', 'Refunded') THEN COALESCE(p.paid_at, p.updated_at, p.created_at) END
        FROM payments p LEFT JOIN tenants t ON t.id = p.tenant_id
        """);

    private IQueryable<Row> Filtered(string? product, string? status, DateTimeOffset? from, DateTimeOffset? to)
    {
        var q = Rows();
        if (!string.IsNullOrWhiteSpace(product)) q = q.Where(r => r.Product == product);
        if (!string.IsNullOrWhiteSpace(status)) { var st = status.ToLower(); q = q.Where(r => r.Status.ToLower() == st); }
        if (from is { } f) { var start = f.ToUniversalTime(); q = q.Where(r => r.CreatedAt >= start); }
        if (to is { } e) { var end = e.ToUniversalTime(); q = q.Where(r => r.CreatedAt <= end); }
        return q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);
    }

    private static PaymentRowDto Dto(Row r) => new(r.Id, r.Kind, r.Product, r.BusinessId, r.BusinessName, r.AmountMinor, r.RefundedMinor, r.CollectedMinor,
        r.Currency, r.Status, r.Reference, r.CreatedAt, r.PaidAt);

    public async Task<Paged<PaymentRowDto>> PaymentsAsync(string? product, string? status, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = Filtered(product, status, from, to);
        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<PaymentRowDto>(items.Select(Dto).ToList(), page, pageSize, total);
    }

    /// <summary>The whole filtered range, read row by row and built in memory. Text cells are quoted and can't start a formula.</summary>
    public async Task<string> CsvAsync(string? product, string? status, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var sb = new StringBuilder("date,kind,product,business,amount,refunded,collected,currency,status,reference\n");
        await foreach (var p in Filtered(product, status, from, to).AsAsyncEnumerable().WithCancellation(ct))
            sb.Append(CultureInfo.InvariantCulture, $"{p.CreatedAt:yyyy-MM-dd HH:mm},{Csv.Cell(p.Kind)},{Csv.Cell(p.Product)},{Csv.Cell(p.BusinessName)},{p.AmountMinor / 100m:0.00},{p.RefundedMinor / 100m:0.00},{p.CollectedMinor / 100m:0.00},{Csv.Cell(p.Currency)},{Csv.Cell(p.Status)},{Csv.Cell(p.Reference)}\n");
        return sb.ToString();
    }
}

public static class Csv
{
    /// <summary>
    /// A quoted CSV cell. Values a spreadsheet would run as a formula (starting with = + - @, tab or carriage return) get a leading
    /// apostrophe, so business names and references typed by customers open as plain text.
    /// </summary>
    public static string Cell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or (char)9 or (char)13) v = "'" + v; // 9 tab, 13 carriage return
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }
}

// Overview

public sealed record ProductFiguresDto(string Product, int Businesses, int NewThisMonth, int Paying, int Suspended);
public sealed record FeesDto(long CourseFeesMinor, long ShortLetCommissionMinor, long BoostsMinor, long TotalMinor);
public sealed record QueuesDto(int Listings, int Courses, int Verifications, int Reports, int Reviews, int Payouts, long PayoutsMinor);
public sealed record MrrDto(string Currency, long MonthlyMinor);
public sealed record OverviewDto(IReadOnlyList<ProductFiguresDto> Products, long MrrMinor, IReadOnlyList<MrrDto> MrrByCurrency, FeesDto Fees, QueuesDto Queues, int FailedEmails24h,
    IReadOnlyList<JobRunDto> Jobs);

public sealed class PlatformOverviewService(IAppDbContext db, TimeProvider clock)
{
    public async Task<OverviewDto> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var tenants = await db.Tenants.AsNoTracking().Select(t => new { t.Id, t.Product, t.Status, t.CreatedAt }).ToListAsync(ct);
        var paid = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now) && s.Provider != "comp"
                && (s.PriceMinor ?? s.Plan!.PriceMinor) > 0)
            // MRR counts what each business actually pays (the price frozen on its subscription), not today's list price.
            .Select(s => new { s.TenantId, s.Plan!.Product, PriceMinor = s.PriceMinor ?? s.Plan.PriceMinor, Currency = s.Currency ?? s.Plan.Currency, s.Plan.Interval })
            .ToListAsync(ct);
        var payingTenants = paid.Select(p => p.TenantId).ToHashSet();
        var products = Saas.Application.Billing.Products.All.Order().Select(p => new ProductFiguresDto(p,
            tenants.Count(t => t.Product == p), tenants.Count(t => t.Product == p && t.CreatedAt >= monthStart),
            tenants.Count(t => t.Product == p && payingTenants.Contains(t.Id)), tenants.Count(t => t.Product == p && t.Status == TenantStatus.Suspended))).ToList();
        long Monthly(long price, BillingInterval interval) => interval switch
        {
            BillingInterval.Year => price / 12,
            BillingInterval.Term => price / 4,
            _ => price,
        };
        var mrrByCurrency = paid.GroupBy(p => p.Currency).OrderBy(g => g.Key)
            .Select(g => new MrrDto(g.Key, g.Sum(p => Monthly(p.PriceMinor, p.Interval)))).ToList();
        var mrr = mrrByCurrency.FirstOrDefault(m => m.Currency == "NGN")?.MonthlyMinor ?? 0; // the platform's reporting currency

        var earnings = await db.Earnings.IgnoreQueryFilters().Where(e => !e.Reversed && e.CreatedAt >= monthStart)
            .GroupBy(e => e.Source).Select(g => new { g.Key, Fees = g.Sum(e => e.FeeMinor) }).ToListAsync(ct);
        var courseFees = earnings.FirstOrDefault(e => e.Key == EarningSource.CourseOrder)?.Fees ?? 0;
        var stayFees = earnings.FirstOrDefault(e => e.Key == EarningSource.Stay)?.Fees ?? 0;
        var boosts = await db.Boosts.IgnoreQueryFilters().Where(b => b.PaidAt >= monthStart).SumAsync(b => (long?)b.AmountMinor, ct) ?? 0;

        var payouts = await db.Payouts.IgnoreQueryFilters().Where(p => p.Status == PayoutStatus.Requested).Select(p => p.AmountMinor).ToListAsync(ct);
        var queues = new QueuesDto(
            await db.Listings.IgnoreQueryFilters().CountAsync(l => l.Status == ListingStatus.Pending || l.Status == ListingStatus.Hidden, ct),
            await db.Courses.IgnoreQueryFilters().CountAsync(c => c.MarketplaceStatus == MarketplaceStatus.InReview, ct),
            await db.VerificationRequests.IgnoreQueryFilters().CountAsync(v => v.Status == VerificationStatus.Pending, ct),
            await db.ListingReports.IgnoreQueryFilters().CountAsync(r => r.Status == ReportStatus.Open, ct),
            await db.AgentReviews.IgnoreQueryFilters().CountAsync(r => r.Status == ReviewStatus.Hidden, ct),
            payouts.Count, payouts.Sum());
        var failed = await db.Notifications.CountAsync(n => n.Status == NotificationStatus.Failed && n.CreatedAt >= now.AddHours(-24), ct);
        var jobs = await db.JobRuns.AsNoTracking().OrderBy(j => j.Name).Select(j => new JobRunDto(j.Name, j.LastStartedAt, j.LastSucceededAt, j.LastError)).ToListAsync(ct);
        return new OverviewDto(products, mrr, mrrByCurrency, new FeesDto(courseFees, stayFees, boosts, courseFees + stayFees + boosts), queues, failed, jobs);
    }
}

// Audit

public sealed class PlatformAuditService(IAppDbContext db, PlatformBusinessService businesses)
{
    public async Task<Paged<AuditRowDto>> QueryAsync(Guid? tenantId, Guid? actorId, string? entityType, string? action, DateTimeOffset? from,
        DateTimeOffset? to, int page, int pageSize, CancellationToken ct, string? product = null)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(product)) q = q.Where(a => db.Tenants.Any(t => t.Id == a.TenantId && t.Product == product));
        if (tenantId is { } t) q = q.Where(a => a.TenantId == t);
        if (actorId is { } a1) q = q.Where(a => a.ActorId == a1);
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        if (from is { } f) q = q.Where(a => a.CreatedAt >= f.ToUniversalTime());
        if (to is { } e) q = q.Where(a => a.CreatedAt <= e.ToUniversalTime());
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await q.CountAsync(ct);
        var rows = await businesses.AuditRowsAsync(q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new Paged<AuditRowDto>(rows, page, pageSize, total);
    }
}

// Admins

public sealed record AdminDto(Guid Id, Guid UserId, string Name, string Email, PlatformRole Role, DateTimeOffset AddedAt, string? AddedByName);
public sealed record AddAdminRequest(string Email, PlatformRole Role);
public sealed record ChangeRoleRequest(PlatformRole Role);

/// <summary>Super admins add, re-role and remove admins. There is always at least one active SuperAdmin.</summary>
public sealed class PlatformAdminsService(IAppDbContext db, PlatformAudit audit, AuthSessions sessions, NotificationQueue notifications, AppUrls urls, TimeProvider clock)
{
    public async Task<IReadOnlyList<AdminDto>> ListAsync(CancellationToken ct)
    {
        var rows = await (from a in db.PlatformAdmins.AsNoTracking() join u in db.Users on a.UserId equals u.Id
                          where a.RemovedAt == null orderby a.Role, u.FullName
                          select new { A = a, u.FullName, u.Email }).ToListAsync(ct);
        var adderIds = rows.Where(r => r.A.AddedBy != null).Select(r => r.A.AddedBy!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => adderIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(r => new AdminDto(r.A.Id, r.A.UserId, r.FullName, r.Email!, r.A.Role, r.A.CreatedAt,
            r.A.AddedBy is { } id ? names.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<AdminDto> AddAsync(AddAdminRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Email)) throw BadRequestException.Field("email", "Enter the email of the account to add.");
        var email = r.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new NotFoundException("An account with that email (they need to sign up first)");
        var row = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (row is { RemovedAt: null }) throw new ConflictException("They're already an admin. Change their role instead.");
        if (row is null)
        {
            row = new PlatformAdmin { UserId = user.Id };
            db.PlatformAdmins.Add(row);
        }
        row.Role = r.Role;
        row.AddedBy = audit.ActorId;
        row.RemovedAt = null;
        row.RemovedBy = null;
        audit.Log("admin.add", nameof(PlatformAdmin), user.Id.ToString(), after: new { role = r.Role.ToString(), email });
        notifications.Email(email, "admin.added", new() { ["name"] = user.FullName, ["role"] = r.Role.ToString(), ["link"] = urls.Web("admin") }, userId: user.Id);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(a => a.UserId == user.Id);
    }

    /// <summary>Serialises changes that could remove the last super admin, so two at once can't both pass the check.</summary>
    private const long SuperAdminLock = 0x5A_AD_01;

    public async Task<AdminDto> ChangeRoleAsync(Guid id, ChangeRoleRequest r, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(SuperAdminLock, ct);
        var row = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == id && a.RemovedAt == null, ct) ?? throw new NotFoundException("Admin");
        if (row.Role == PlatformRole.SuperAdmin && r.Role != PlatformRole.SuperAdmin) await EnsureAnotherSuperAdminAsync(row, ct);
        var before = row.Role;
        row.Role = r.Role;
        audit.Log("admin.role", nameof(PlatformAdmin), row.UserId.ToString(), before: new { role = before.ToString() }, after: new { role = r.Role.ToString() });
        await db.SaveChangesAsync(ct);
        if (r.Role is not (PlatformRole.Support or PlatformRole.SuperAdmin)) await db.EndSupportViewsForUserAsync(row.UserId, audit.ActorId, clock.GetUtcNow(), ct);
        await tx.CommitAsync(ct);
        return (await ListAsync(ct)).First(a => a.Id == id);
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(SuperAdminLock, ct);
        var row = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Id == id && a.RemovedAt == null, ct) ?? throw new NotFoundException("Admin");
        if (row.Role == PlatformRole.SuperAdmin) await EnsureAnotherSuperAdminAsync(row, ct);
        row.RemovedAt = clock.GetUtcNow();
        row.RemovedBy = audit.ActorId;
        audit.Log("admin.remove", nameof(PlatformAdmin), row.UserId.ToString(), before: new { role = row.Role.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await sessions.EndAllForUserAsync(row.UserId, audit.ActorId, ct);
    }

    private async Task EnsureAnotherSuperAdminAsync(PlatformAdmin row, CancellationToken ct)
    {
        if (!await db.PlatformAdmins.AnyAsync(a => a.Id != row.Id && a.Role == PlatformRole.SuperAdmin && a.RemovedAt == null, ct))
            throw new ConflictException("There must always be at least one super admin. Add another before removing or demoting this one.");
    }
}
