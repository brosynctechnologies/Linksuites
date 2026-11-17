using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Billing;

/// <summary>
/// Paid plans don't renew by themselves, so the owner is emailed <see cref="FirstNoticeDays"/> days and <see cref="LastNoticeDays"/>
/// day before the business's paid time runs out. "Runs out" is the end of the last active period of that product: a plan that another
/// one takes over from gets no email. Each reminder is sent once per end date (<see cref="Subscription.EndingNoticeFor"/>), so extending
/// the plan arms them again.
/// </summary>
public sealed class PlanReminderJobs(IAppDbContext db, NotificationQueue notifications, AppUrls urls, ILogger<PlanReminderJobs> logger)
{
    public const int FirstNoticeDays = 7, LastNoticeDays = 1;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

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
            var end = last.CurrentPeriodEnd!.Value;
            if (end > horizon || last.PlanPrice <= 0) continue;
            var days = end - now <= TimeSpan.FromDays(LastNoticeDays) ? LastNoticeDays : FirstNoticeDays;

            var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == last.Id, ct);
            if (sub.EndingNoticeFor == end && sub.EndingNoticeDays <= days) continue; // this reminder (or a later one) already went

            var owner = await (from m in db.Memberships.IgnoreQueryFilters()
                               join u in db.Users on m.UserId equals u.Id
                               where m.TenantId == business.Key.TenantId && m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active && u.Email != null
                               select new { u.Id, u.Email, u.FullName }).FirstOrDefaultAsync(ct);
            var tenantName = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == business.Key.TenantId).Select(t => t.Name).FirstAsync(ct);
            if (owner is null)
            {
                logger.LogWarning("Plan ending for business {TenantId}, but it has no active owner to email", business.Key.TenantId);
            }
            else
            {
                notifications.Email(owner.Email!, "plan.ending", new Dictionary<string, string>
                {
                    ["name"] = owner.FullName,
                    ["business"] = tenantName,
                    ["plan"] = last.PlanName,
                    ["days"] = days.ToString(),
                    ["date"] = TimeZoneInfo.ConvertTime(end, Lagos).ToString("d MMMM yyyy"),
                    ["productName"] = Products.Brand(business.Key.Product),
                    ["link"] = $"{urls.Web(business.Key.Product)}{BillingService.BillingPath(business.Key.Product)}",
                }, business.Key.TenantId, owner.Id);
                sent++;
            }
            sub.EndingNoticeFor = end;
            sub.EndingNoticeDays = days;
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }
}
