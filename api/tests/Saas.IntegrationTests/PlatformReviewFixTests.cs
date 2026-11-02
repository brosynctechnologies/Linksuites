using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

/// <summary>Regression tests for the findings of the final review of the admin console.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PlatformReviewFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private static string PathOf(RoutePattern pattern) => "/" + string.Join("/", pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternParameterPart p when p.ParameterPolicies.Any(x => x.Content == "guid") => Guid.Empty.ToString(),
        RoutePatternParameterPart p when p.ParameterPolicies.Any(x => x.Content == "int") => "0",
        RoutePatternParameterPart => "x",
        _ => "",
    }))));

    // C1 + I4: every admin and platform route refuses a customer token, even an admin's own customer sign-in.
    [Fact]
    public async Task Customer_tokens_never_reach_admin_routes()
    {
        var (customerToken, _) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.SuperAdmin);
        var routes = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && (raw.StartsWith("/api/admin") || (raw.StartsWith("/api/platform") && !raw.StartsWith("/api/platform/auth"))))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (Method: m, Path: PathOf(e.RoutePattern))))
            .ToList();
        Assert.True(routes.Count > 40);

        var reached = new List<string>();
        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json") };
            var response = await customerToken.SendAsync(request, Ct);
            if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)) reached.Add($"{method} {path} -> {(int)response.StatusCode}");
        }
        Assert.Empty(reached);
    }

    // C1: an address listed in config gets nothing from a customer sign-up; only the emailed code (proof of the mailbox) makes it an admin.
    [Fact]
    public async Task Config_email_becomes_admin_only_after_the_emailed_code()
    {
        var email = ApiFixture.UnclaimedAdminEmail;
        var squatter = api.CreateClient();
        var signup = await squatter.PostAsJsonAsync("/api/auth/seeker/signup", new { fullName = "Squatter", email, password = "password123" }, Ct);
        signup.EnsureSuccessStatusCode();
        var auth = await signup.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.False(auth.GetProperty("user").GetProperty("isPlatformAdmin").GetBoolean());
        squatter.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await squatter.GetAsync("/api/admin/payouts", Ct)).StatusCode);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        Assert.False(await api.WithDbAsync(db => db.PlatformAdmins.AnyAsync(a => a.UserId == userId, Ct)));

        var (panel, _) = await PlatformAuthTests.PanelLoginAsync(api, email, "password123"); // needs the code sent to the mailbox
        Assert.Equal(HttpStatusCode.OK, (await panel.GetAsync("/api/platform/health", Ct)).StatusCode);
        Assert.Equal(PlatformRole.SuperAdmin, await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.UserId == userId).Select(a => a.Role).SingleAsync(Ct)));
    }

    // C2: a suspended course business stops selling through direct links.
    [Fact]
    public async Task Suspended_course_business_stops_selling()
    {
        var tutor = api.CreateClient();
        var signup = await (await tutor.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Fraud Tutor", email = $"fraud-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG", product = "courses",
            businessName = $"Fraud Academy {Guid.NewGuid().ToString("N")[..6]}",
        }, Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        tutor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signup.GetProperty("accessToken").GetString());
        var tenantId = signup.GetProperty("tenant").GetProperty("id").GetGuid();
        var course = await (await tutor.PostAsJsonAsync("/api/studio/courses", new { title = "Get rich quick", priceMinor = 1_000_000, isFree = false, level = "AllLevels", listOnMarketplace = false }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var courseId = course.GetProperty("id").GetGuid();
        var detail = await (await tutor.GetAsync($"/api/studio/courses/{courseId}", Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var section = detail.GetProperty("sections")[0].GetProperty("id").GetGuid();
        var built = await (await tutor.PostAsJsonAsync($"/api/studio/sections/{section}/lessons", new { title = "Intro", type = "Text" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var lessonId = built.GetProperty("sections")[0].GetProperty("lessons")[0].GetProperty("id").GetGuid();
        (await tutor.PutAsJsonAsync($"/api/studio/lessons/{lessonId}", new { title = "Intro", type = "Text", isPreview = true, content = "# Hi" }, Ct)).EnsureSuccessStatusCode();
        (await tutor.PostAsync($"/api/studio/courses/{courseId}/publish", null, Ct)).EnsureSuccessStatusCode();
        var academySlug = signup.GetProperty("tenant").GetProperty("slug").GetString();
        var courseSlug = course.GetProperty("slug").GetString();
        var anon = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/catalog/courses/{academySlug}/{courseSlug}", Ct)).StatusCode);

        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        (await support.PostAsJsonAsync($"/api/platform/businesses/{tenantId}/suspend", new { reason = "Selling a fraudulent course." }, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/catalog/courses/{academySlug}/{courseSlug}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/catalog/academies/{academySlug}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/catalog/lessons/{lessonId}/preview", Ct)).StatusCode);
        var learner = await kit.SeekerAsync("Would-be learner");
        Assert.Equal(HttpStatusCode.NotFound, (await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId }, Ct)).StatusCode);
    }

    private async Task<Guid> PlanIdAsync(string product, string code) =>
        await api.WithDbAsync(db => db.Plans.Where(p => p.Product == product && p.Code == code).Select(p => p.Id).SingleAsync(Ct));

    // One current-plan rule everywhere (CurrentSubscription.Current): the highest plan in force wins, so a comp of a lower plan
    // never takes away a plan the business paid for, however long the comp runs.
    [Fact]
    public async Task Comp_of_a_lower_plan_keeps_the_paid_plan()
    {
        var agency = await kit.AgencyAsync("Paid Agency Realty");
        await api.MakeProAsync(agency.TenantId, "property", "agency"); // paid for one month
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        async Task<string?> CurrentAsync() =>
            (await (await agency.Client.GetAsync("/api/billing", Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct))
                .GetProperty("currentPlan").GetProperty("code").GetString();

        (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(10), reason = "Goodwill comp after an outage." }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("agency", await CurrentAsync());

        (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(400), reason = "Long goodwill comp." }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("agency", await CurrentAsync());
    }

    // I1: paying for a plan the business is comped on starts a real paid subscription instead of extending the comp.
    [Fact]
    public async Task Paying_for_a_comped_plan_creates_a_paid_subscription()
    {
        var agency = await kit.AgencyAsync("Converting Realty");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        (await finance.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/comp",
            new { planCode = "pro", until = DateTimeOffset.UtcNow.AddDays(10), reason = "Trial for a launch partner." }, Ct)).EnsureSuccessStatusCode();

        var checkout = await (await agency.Client.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var reference = api.Gateway.Initialized.Last(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BillingService>().ConfirmPaymentAsync(reference, 1_000_000, "NGN", Ct);

        var subs = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == agency.TenantId && s.Status == SubscriptionStatus.Active).ToListAsync(Ct));
        Assert.Contains(subs, s => s.Provider == "paystack" && s.PlanId != Guid.Empty);
    }

    // I2: view-as is limited to the business: not the owner's own learner/seeker data, and no lasting secrets.
    [Fact]
    public async Task View_as_stays_inside_the_business()
    {
        var owner = api.CreateClient();
        var email = $"salon-{Guid.NewGuid():N}@example.com";
        var signup = await (await owner.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Salon Owner", email, password = "password123", country = "NG", product = "bookings", businessName = $"Salon {Guid.NewGuid().ToString("N")[..6]}",
        }, Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var tenantId = signup.GetProperty("tenant").GetProperty("id").GetGuid();
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await (await support.PostAsJsonAsync($"/api/platform/businesses/{tenantId}/view-as", new { reason = "Customer can't see their calendar." }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/learn/enrollments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me/saved-listings", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/me/businesses", Ct)).StatusCode);
        var staff = await (await client.GetAsync("/api/staff", Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.All(staff.EnumerateArray(), s => Assert.Equal("", s.GetProperty("calendarFeedUrl").GetString()));
    }
}
