using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PlatformManagementTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private static object PlanBody(string code, long price, bool hidden = false) => new
    {
        product = "property", code, name = $"Test {code}", priceMinor = price, isHidden = hidden, sortOrder = 9,
        limits = new Dictionary<string, long> { ["listings"] = 10, ["listing_expiry_days"] = 45, ["listing_stats"] = 1, ["team_seats"] = 2 },
        features = new[] { "10 listings" },
    };

    private Task Subscribe(Guid tenantId, Guid planId, DateTimeOffset until) => api.WithDbAsync(async db =>
    {
        db.Subscriptions.Add(new Subscription
        {
            TenantId = tenantId, PlanId = planId, Status = SubscriptionStatus.Active, CurrentPeriodStart = DateTimeOffset.UtcNow, CurrentPeriodEnd = until,
            Provider = "paystack",
        });
        return await db.SaveChangesAsync(Ct);
    });

    [Fact]
    public async Task Plan_price_edit_keeps_current_periods()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"t{Guid.NewGuid().ToString("N")[..8]}";
        var plan = await Json(await finance.PostAsJsonAsync("/api/platform/plans", PlanBody(code, 700_000), Ct));
        var planId = plan.GetProperty("id").GetGuid();
        var agency = await kit.AgencyAsync("Price Lock Realty");
        var paidUntil = DateTimeOffset.UtcNow.AddDays(20);
        await Subscribe(agency.TenantId, planId, paidUntil);

        (await finance.PutAsJsonAsync($"/api/platform/plans/{planId}", PlanBody(code, 900_000), Ct)).EnsureSuccessStatusCode();

        var sub = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == agency.TenantId, Ct));
        Assert.Equal((planId, SubscriptionStatus.Active), (sub.PlanId, sub.Status));
        Assert.Equal(paidUntil.ToUnixTimeSeconds(), sub.CurrentPeriodEnd!.Value.ToUnixTimeSeconds());
        var plans = await Json(await finance.GetAsync("/api/platform/plans", Ct));
        Assert.Equal(900_000, plans.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == planId).GetProperty("priceMinor").GetInt64());
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "plan.update" && a.EntityId == planId.ToString(), Ct)));
    }

    [Fact]
    public async Task Hidden_plan_is_not_offered_but_keeps_subscribers()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var code = $"h{Guid.NewGuid().ToString("N")[..8]}";
        var plan = await Json(await finance.PostAsJsonAsync("/api/platform/plans", PlanBody(code, 500_000, hidden: true), Ct));
        var subscriber = await kit.AgencyAsync("Legacy Plan Realty");
        await Subscribe(subscriber.TenantId, plan.GetProperty("id").GetGuid(), DateTimeOffset.UtcNow.AddDays(10));
        var other = await kit.AgencyAsync("New Customer Realty");

        var offered = await Json(await other.Client.GetAsync("/api/billing", Ct));
        Assert.DoesNotContain(offered.GetProperty("plans").EnumerateArray(), p => p.GetProperty("code").GetString() == code);
        var mine = await Json(await subscriber.Client.GetAsync("/api/billing", Ct));
        Assert.Equal(code, mine.GetProperty("currentPlan").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync("/api/billing/checkout", new { planCode = code }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Comp_gives_a_plan_until_its_date_then_falls_back()
    {
        var agency = await kit.AgencyAsync("Launch Partner Realty");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(-1), reason = "Launch partner, three months free." }, Ct)).StatusCode);

        (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(90), reason = "Launch partner, three months free." }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("pro", (await Json(await agency.Client.GetAsync("/api/billing", Ct))).GetProperty("currentPlan").GetProperty("code").GetString());

        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == agency.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentPeriodEnd, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        Assert.Equal("free", (await Json(await agency.Client.GetAsync("/api/billing", Ct))).GetProperty("currentPlan").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Payments_list_spans_products()
    {
        var agency = await kit.AgencyAsync("Paying Realty", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var purchase = await Json(await agency.Client.PostAsJsonAsync($"/api/property/listings/{id}/boosts", new { code = "top-7" }, Ct));
        var reference = api.Gateway.Initialized.Last(i => purchase.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 500_000, "NGN", DateTimeOffset.UtcNow);
        await agency.Client.PostAsJsonAsync($"/api/property/boosts/{purchase.GetProperty("boostId").GetGuid()}/verify", new { reference }, Ct);
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);

        var payments = await Json(await finance.GetAsync("/api/platform/payments?product=property&pageSize=100", Ct));
        var row = payments.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("reference").GetString() == reference);
        Assert.Equal(("Boost", 500_000L, "Paying Realty"), (row.GetProperty("kind").GetString(), row.GetProperty("amountMinor").GetInt64(),
            row.GetProperty("businessName").GetString()));
        var csv = await finance.GetAsync("/api/platform/payments.csv?product=property", Ct);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains(reference, await csv.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Overview_counts_businesses_fees_and_queues()
    {
        await kit.AgencyAsync("Counted Realty");
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var overview = await Json(await support.GetAsync("/api/platform/overview", Ct));
        var property = overview.GetProperty("products").EnumerateArray().Single(p => p.GetProperty("product").GetString() == "property");
        Assert.True(property.GetProperty("businesses").GetInt32() >= 1);
        Assert.True(overview.TryGetProperty("mrrMinor", out _));
        Assert.True(overview.GetProperty("queues").TryGetProperty("listings", out _));
        Assert.True(overview.GetProperty("fees").TryGetProperty("boostsMinor", out _));
    }

    [Fact]
    public async Task Audit_filters_by_business()
    {
        var agency = await kit.AgencyAsync("Audited Realty");
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/suspend", new { reason = "Testing the audit trail here." }, Ct);

        var audit = await Json(await support.GetAsync($"/api/platform/audit?tenantId={agency.TenantId}", Ct));
        var entry = audit.GetProperty("items").EnumerateArray().First(a => a.GetProperty("action").GetString() == "business.suspend");
        Assert.Equal("Support Admin", entry.GetProperty("actorName").GetString());
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/platform/audit", Ct)).StatusCode);
    }

    [Fact]
    public async Task Last_super_admin_cannot_be_removed_or_demoted()
    {
        var (super, userId, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.SuperAdmin);
        var others = await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.Role == PlatformRole.SuperAdmin && a.RemovedAt == null && a.UserId != userId)
            .Select(a => a.Id).ToListAsync(Ct));
        await api.WithDbAsync(db => db.PlatformAdmins.Where(a => others.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedAt, DateTimeOffset.UtcNow), Ct));
        try
        {
            var me = (await Json(await super.GetAsync("/api/platform/admins", Ct))).EnumerateArray().Single(a => a.GetProperty("userId").GetGuid() == userId);
            var adminId = me.GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.Conflict, (await super.DeleteAsync($"/api/platform/admins/{adminId}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await super.PutAsJsonAsync($"/api/platform/admins/{adminId}", new { role = "Moderator" }, Ct)).StatusCode);
        }
        finally
        {
            await api.WithDbAsync(db => db.PlatformAdmins.Where(a => others.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedAt, (DateTimeOffset?)null), Ct));
        }
    }

    [Fact]
    public async Task Super_admin_adds_a_moderator_who_can_then_moderate_and_removes_them()
    {
        var (super, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.SuperAdmin);
        var email = $"new-mod-{Guid.NewGuid():N}@example.com";
        await api.SignUpAsync(api.CreateClient(), "/api/auth/seeker/signup", new { fullName = "New Moderator", email, password = "password123" }, Ct);

        var added = await Json(await super.PostAsJsonAsync("/api/platform/admins", new { email, role = "Moderator" }, Ct));
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "admin.added" && n.Recipient == email, Ct)));
        Assert.Equal(HttpStatusCode.Conflict, (await super.PostAsJsonAsync("/api/platform/admins", new { email, role = "Finance" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await super.PostAsJsonAsync("/api/platform/admins", new { email = "nobody-here@example.com", role = "Finance" }, Ct)).StatusCode);

        var client = api.CreateClient();
        var challenge = await Json(await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password = "password123" }, Ct));
        var code = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "admin.code" && n.Recipient == email).FirstAsync(Ct))).Payload["code"];
        var token = (await Json(await client.PostAsJsonAsync("/api/platform/auth/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code }, Ct)))
            .GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);

        (await super.DeleteAsync($"/api/platform/admins/{added.GetProperty("id").GetGuid()}", Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/property/moderation", Ct)).StatusCode); // their session ended
    }

    [Fact]
    public async Task Only_super_admins_manage_admins_and_plans_need_finance()
    {
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/api/platform/admins", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/api/platform/admins", new { email = "x@example.com", role = "Finance" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsJsonAsync("/api/platform/plans", PlanBody("nope", 1), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/api/platform/payments", Ct)).StatusCode);
    }
}
