using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink signup: the school profile, the current session with three terms, the school plans and the setup checklist.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolSetupTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    [Fact]
    public async Task Signup_creates_the_school_session_and_three_terms()
    {
        var school = await kit.SchoolAsync("Greenfield Academy");

        var setup = await Json(await school.Owner.GetAsync("/api/school/setup", Ct));
        Assert.Equal("Greenfield Academy", setup.GetProperty("school").GetProperty("name").GetString());
        Assert.Equal(school.Slug, setup.GetProperty("school").GetProperty("slug").GetString());

        var sessions = setup.GetProperty("sessions");
        Assert.Equal(1, sessions.GetArrayLength());
        var session = sessions[0];
        Assert.True(session.GetProperty("isCurrent").GetBoolean());
        var terms = session.GetProperty("terms").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], terms.Select(t => t.GetProperty("number").GetInt32()));
        Assert.Equal(1, terms.Count(t => t.GetProperty("isCurrent").GetBoolean()));

        var checklist = setup.GetProperty("checklist").EnumerateArray().ToList();
        Assert.Equal(["levels-and-classes", "subjects", "students", "staff"], checklist.Select(c => c.GetProperty("key").GetString()));
        Assert.All(checklist, c => Assert.False(c.GetProperty("done").GetBoolean()));

        var billing = await Json(await school.Owner.GetAsync("/api/billing", Ct));
        var plan = billing.GetProperty("currentPlan");
        Assert.Equal("free", plan.GetProperty("code").GetString());
        Assert.Equal(50, plan.GetProperty("limits").GetProperty("students").GetInt64());
        Assert.Contains(billing.GetProperty("usage").GetProperty("items").EnumerateArray(), i => i.GetProperty("key").GetString() == "students");
    }

    [Fact]
    public async Task The_session_runs_september_to_august_and_names_both_years()
    {
        var school = await kit.SchoolAsync("Session Naming School");
        var session = (await Json(await school.Owner.GetAsync("/api/school/setup", Ct))).GetProperty("sessions")[0];

        var start = DateOnly.Parse(session.GetProperty("startDate").GetString()!);
        var end = DateOnly.Parse(session.GetProperty("endDate").GetString()!);
        Assert.Equal(9, start.Month);
        Assert.Equal(1, start.Day);
        Assert.Equal(new DateOnly(start.Year + 1, 8, 31), end);
        Assert.Equal($"{start.Year}/{start.Year + 1}", session.GetProperty("name").GetString());

        var terms = session.GetProperty("terms").EnumerateArray().ToList();
        Assert.Equal([9, 1, 4], terms.Select(t => DateOnly.Parse(t.GetProperty("startDate").GetString()!).Month));
        Assert.Equal(["First term", "Second term", "Third term"], terms.Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_school_plans_are_seeded_with_the_agreed_limits()
    {
        var plans = await api.WithDbAsync(db => db.Plans.Where(p => p.Product == Products.School && p.SortOrder < 9).OrderBy(p => p.SortOrder).ToListAsync(Ct));

        Assert.Equal(["free", "standard", "premium"], plans.Select(p => p.Code));
        Assert.Equal([0L, 6_000_000L, 15_000_000L], plans.Select(p => p.PriceMinor));
        Assert.All(plans, p => Assert.Equal(BillingInterval.Term, p.Interval));
        Assert.Equal([50L, 300L, 1000L], plans.Select(p => p.Limit(LimitKeys.Students)));
        Assert.Equal([3L, 15L, 50L], plans.Select(p => p.Limit(LimitKeys.TeamSeats)));
        Assert.Equal([0L, 0L, 1L], plans.Select(p => p.Limit(LimitKeys.RemoveBranding)));
        Assert.Equal([0L, 1L, 1L], plans.Select(p => p.Limit(LimitKeys.OnlineFees)));
        Assert.True(plans[0].IsDefault);
    }

    [Fact]
    public async Task A_term_subscription_renews_four_months_on()
    {
        var school = await kit.SchoolAsync("Renewal School");
        var checkout = await Json(await school.Owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "standard" }, Ct));
        var reference = api.Gateway.Initialized.Last(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;

        async Task ConfirmAsync()
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BillingService>().ConfirmPaymentAsync(reference, 6_000_000, "NGN", Ct);
        }
        await ConfirmAsync();
        var first = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == school.TenantId).SingleAsync(Ct));
        Assert.Equal(first.CurrentPeriodStart.AddMonths(4), first.CurrentPeriodEnd);

        // A second payment while the term is running extends it by another four months.
        var again = await Json(await school.Owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "standard" }, Ct));
        reference = api.Gateway.Initialized.Last(i => again.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        await ConfirmAsync();
        var renewed = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == school.TenantId).SingleAsync(Ct));
        Assert.Equal(first.CurrentPeriodEnd!.Value.AddMonths(4), renewed.CurrentPeriodEnd);
    }

    [Fact]
    public async Task Only_school_accounts_can_open_the_school_setup()
    {
        var school = await kit.SchoolAsync("Locked Door School");
        var (invoices, _) = await api.SignupInvoicesAsync("Invoicer");

        Assert.Equal(HttpStatusCode.Forbidden, (await invoices.GetAsync("/api/school/setup", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/school/setup", Ct)).StatusCode);
        Assert.True((await school.Owner.GetAsync("/api/school/setup", Ct)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Staff_can_read_the_setup()
    {
        var school = await kit.SchoolAsync("Staff Setup School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        Assert.True((await teacher.GetAsync("/api/school/setup", Ct)).IsSuccessStatusCode);
    }
}
