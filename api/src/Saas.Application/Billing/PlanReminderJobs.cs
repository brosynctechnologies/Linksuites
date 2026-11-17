using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Billing;

/// <summary>
/// Paid plans don't renew by themselves, so the owner is emailed <see cref="FirstNoticeDays"/> days and <see cref="LastNoticeDays"/>
/// day before the business's paid time runs out. "Runs out" is the end of the last active period of that product: a plan that another
/// one takes over from gets no email. Each reminder is sent once per end date (<see cref="Subscription.EndingNoticeFor"/>), so extending
/// the plan arms them again. One business failing never stops the others.
/// </summary>
public sealed class PlanReminderJobs(IAppDbContext db, NotificationQueue notifications, AppUrls urls, ILogger<PlanReminderJobs> logger)
{
    public const int FirstNoticeDays = 7, LastNoticeDays = 1;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

    private sealed record Ending(Guid TenantId, string Product, Guid SubscriptionId, string PlanName, DateTimeOffset End);

    /// <summary>Returns how many reminders were queued.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var horizon = now.AddDays(FirstNoticeDays);
        // Every active, running subscription of every business, with its plan's product; grouped per business below.
        var running = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now))
            .Select(s => new { s.Id, s.TenantId, s.CurrentPeriodEnd, PlanName = s.Plan!.Name, PlanPrice = s.Plan.PriceMinor, s.Plan.Product })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var business in running.GroupBy(s => (s.TenantId, s.Product)))
        {
            // Paid time runs out at the latest end; an open-ended (or free) plan never runs out.
            if (business.Any(s => s.CurrentPeriodEnd is null)) continue;
            var last = business.OrderByDescending(s => s.CurrentPeriodEnd).First();
            if (last.CurrentPeriodEnd!.Value > horizon || last.PlanPrice <= 0) continue;
            try
            {
                if (await RemindAsync(new Ending(last.TenantId, last.Product, last.Id, last.PlanName, last.CurrentPeriodEnd.Value), now, ct)) sent++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Plan ending reminder for business {TenantId} failed", last.TenantId);
                db.ChangeTracker.Clear();
            }
        }
        return sent;
    }

    private async Task<bool> RemindAsync(Ending e, DateTimeOffset now, CancellationToken ct)
    {
        var stage = e.End - now <= TimeSpan.FromDays(LastNoticeDays) ? LastNoticeDays : FirstNoticeDays;
        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == e.SubscriptionId, ct);
        if (sub.EndingNoticeFor == e.End && sub.EndingNoticeDays <= stage) return false; // this reminder (or a later one) already went

        var owner = await (from m in db.Memberships.IgnoreQueryFilters()
                           join u in db.Users on m.UserId equals u.Id
                           where m.TenantId == e.TenantId && m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active && u.Email != null
                           select new { u.Id, u.Email, u.FullName }).FirstOrDefaultAsync(ct);
        var sent = false;
        if (owner is null)
        {
            logger.LogWarning("Plan ending for business {TenantId}, but it has no active owner to email", e.TenantId);
        }
        else
        {
            var tenantName = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == e.TenantId).Select(t => t.Name).FirstAsync(ct);
            // The email says how many days are really left: a plan bought with 3 days to go doesn't say "7 days".
            var daysLeft = Math.Max(1, (int)Math.Ceiling((e.End - now).TotalDays));
            notifications.Email(owner.Email!, "plan.ending", new Dictionary<string, string>
            {
                ["name"] = owner.FullName,
                ["business"] = tenantName,
                ["plan"] = e.PlanName,
                ["days"] = daysLeft.ToString(),
                ["date"] = TimeZoneInfo.ConvertTime(e.End, Lagos).ToString("d MMMM yyyy"),
                ["productName"] = Products.Brand(e.Product),
                ["link"] = $"{urls.Web(e.Product)}{BillingService.BillingPath(e.Product)}",
            }, e.TenantId, owner.Id);
            sent = true;
        }
        sub.EndingNoticeFor = e.End;
        sub.EndingNoticeDays = stage;
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
