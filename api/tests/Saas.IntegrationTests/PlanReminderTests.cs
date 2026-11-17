using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1: a business's owner is emailed 7 days and 1 day before its paid plan ends (plans don't renew by themselves), once per
/// reminder per end date; extending the plan arms the reminders again.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlanReminderTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid TenantId, string OwnerEmail)> BusinessOnProAsync(string name)
    {
        var (_, auth) = await api.SignupAsync(name);
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.WhatsAppOrders);
        var email = await api.WithDbAsync(db => (from m in db.Memberships.IgnoreQueryFilters()
                                                 join u in db.Users on m.UserId equals u.Id
                                                 where m.TenantId == tenantId && m.Role == MemberRole.Owner
                                                 select u.Email!).SingleAsync(Ct));
        return (tenantId, email);
    }

    private Task SetEndAsync(Guid tenantId, DateTimeOffset end) => api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters()
        .Where(s => s.TenantId == tenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriodEnd, end), Ct));

    private Task<List<Notification>> RemindersAsync(Guid tenantId) => api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().AsNoTracking()
        .Where(n => n.TenantId == tenantId && n.TemplateKey == "plan.ending").OrderBy(n => n.CreatedAt).ToListAsync(Ct));

    private Task RunAsync(DateTimeOffset now) => api.Services.GetRequiredService<PlanReminderJobs>().RunOnceAsync(now, Ct);

    [Fact]
    public async Task Owner_is_reminded_a_week_and_a_day_before_the_plan_ends_once_each()
    {
        var (tenantId, email) = await BusinessOnProAsync("RemindWeek");
        var now = DateTimeOffset.UtcNow;
        await SetEndAsync(tenantId, now.AddDays(6.5));

        await RunAsync(now);
        await RunAsync(now.AddHours(1)); // the hourly run again: no second email
        var reminders = await RemindersAsync(tenantId);
        var week = Assert.Single(reminders);
        Assert.Equal(email, week.Recipient);
        Assert.Equal("Pro", week.Payload!["plan"]);
        Assert.Contains("/dashboard/billing", week.Payload["link"]);

        await RunAsync(now.AddDays(6)); // half a day left
        await RunAsync(now.AddDays(6).AddHours(1));
        reminders = await RemindersAsync(tenantId);
        Assert.Equal(2, reminders.Count);
        Assert.Equal("1", reminders[1].Payload!["days"]);
    }

    [Fact]
    public async Task A_plan_far_from_its_end_gets_no_reminder()
    {
        var (tenantId, _) = await BusinessOnProAsync("RemindFar");
        await SetEndAsync(tenantId, DateTimeOffset.UtcNow.AddDays(20));
        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Empty(await RemindersAsync(tenantId));
    }

    [Fact]
    public async Task Extending_the_plan_arms_the_reminders_again()
    {
        var (tenantId, _) = await BusinessOnProAsync("RemindExtend");
        var now = DateTimeOffset.UtcNow;
        await SetEndAsync(tenantId, now.AddDays(5));
        await RunAsync(now);
        Assert.Single(await RemindersAsync(tenantId));

        await SetEndAsync(tenantId, now.AddDays(35)); // paid another month
        await RunAsync(now.AddDays(29));
        Assert.Equal(2, (await RemindersAsync(tenantId)).Count);
    }

    [Fact]
    public async Task No_reminder_when_another_plan_runs_on_after_this_one()
    {
        var (tenantId, _) = await BusinessOnProAsync("RemindOverlap");
        var now = DateTimeOffset.UtcNow;
        await SetEndAsync(tenantId, now.AddDays(3));
        await api.WithDbAsync(async db =>
        {
            var pro = await db.Plans.FirstAsync(p => p.Product == Products.WhatsAppOrders && p.Code == "pro", Ct);
            db.Subscriptions.Add(new Subscription
            {
                TenantId = tenantId, PlanId = pro.Id, Status = SubscriptionStatus.Active,
                CurrentPeriodStart = now.AddDays(3), CurrentPeriodEnd = now.AddDays(60),
            });
            return await db.SaveChangesAsync(Ct);
        });
        await RunAsync(now);
        Assert.Empty(await RemindersAsync(tenantId));
    }

    [Fact]
    public async Task A_cancelled_plan_gets_no_reminder()
    {
        var (tenantId, _) = await BusinessOnProAsync("RemindCancelled");
        await SetEndAsync(tenantId, DateTimeOffset.UtcNow.AddDays(2));
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Cancelled), Ct));
        await RunAsync(DateTimeOffset.UtcNow);
        Assert.Empty(await RemindersAsync(tenantId));
    }
}
