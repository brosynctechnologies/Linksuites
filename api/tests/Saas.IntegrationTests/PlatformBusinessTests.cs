using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PlatformBusinessTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private async Task<string> SlugAsync(HttpClient agency) =>
        (await Json(await agency.GetAsync("/api/property/profile/agency", Ct))).GetProperty("slug").GetString()!;

    private static Task<HttpResponseMessage> Suspend(HttpClient admin, Guid tenantId, string action = "suspend") =>
        admin.PostAsJsonAsync($"/api/platform/businesses/{tenantId}/{action}", new { reason = "Reported scam listings, checking documents." }, Ct);

    [Fact]
    public async Task Support_finds_a_business_by_owner_email_and_sees_its_detail()
    {
        var agency = await kit.AgencyAsync("Findable Realty", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        var found = await Json(await support.GetAsync($"/api/platform/businesses?q={Uri.EscapeDataString(agency.Email)}", Ct));
        var row = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(("Findable Realty", "property", "Active", "Free"), (row.GetProperty("name").GetString(), row.GetProperty("product").GetString(),
            row.GetProperty("status").GetString(), row.GetProperty("planName").GetString()));

        var detail = await Json(await support.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct));
        Assert.Equal(agency.Email, detail.GetProperty("members")[0].GetProperty("email").GetString());
        Assert.Contains(detail.GetProperty("usage").EnumerateArray(), u => u.GetProperty("key").GetString() == "listings" && u.GetProperty("used").GetInt64() == 1);
    }

    [Fact]
    public async Task Suspended_business_disappears_from_public_pages_and_comes_back()
    {
        var agency = await kit.AgencyAsync("Suspicious Realty", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Too good to be true"));
        var listingSlug = (await Json(await agency.Client.GetAsync($"/api/property/listings/{id}", Ct))).GetProperty("slug").GetString();
        var agencySlug = await SlugAsync(agency.Client);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var anon = api.CreateClient();

        (await Suspend(support, agency.TenantId)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/listings/{agencySlug}/{listingSlug}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/agencies/{agencySlug}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.PostAsJsonAsync($"/api/listings/{id}/enquiries", new { name = "A", phone = "+2348011111111" }, Ct)).StatusCode);
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "business.suspended" && n.Recipient == agency.Email, Ct)));

        (await Suspend(support, agency.TenantId, "reactivate")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/listings/{agencySlug}/{listingSlug}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Suspended_order_store_is_hidden()
    {
        var owner = api.CreateClient();
        var slugName = $"Corner Shop {Guid.NewGuid().ToString("N")[..6]}";
        var signup = await Json(await owner.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Shop Owner", email = $"shop-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            businessName = slugName, storeName = slugName, whatsappNumber = "+2348099999999",
        }, Ct));
        var tenantId = signup.GetProperty("tenant").GetProperty("id").GetGuid();
        var slug = signup.GetProperty("tenant").GetProperty("slug").GetString();
        var storeSlug = await api.WithDbAsync(db => db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).Select(s => s.Slug).SingleAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync($"/api/public/stores/{storeSlug}", Ct)).StatusCode);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        (await Suspend(support, tenantId)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/api/public/stores/{storeSlug}", Ct)).StatusCode);
        _ = slug;
    }

    [Fact]
    public async Task Suspended_member_is_blocked_on_the_next_request()
    {
        var agency = await kit.AgencyAsync("Blocked Realty");
        Assert.Equal(HttpStatusCode.OK, (await agency.Client.GetAsync("/api/property/listings", Ct)).StatusCode);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        (await Suspend(support, agency.TenantId)).EnsureSuccessStatusCode();

        var blocked = await agency.Client.GetAsync("/api/property/listings", Ct); // token issued before the suspension
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("suspended", await blocked.Content.ReadAsStringAsync(Ct));
        var login = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = agency.Email, password = "password123", product = "property" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains("suspended", await login.Content.ReadAsStringAsync(Ct));

        (await Suspend(support, agency.TenantId, "reactivate")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await agency.Client.GetAsync("/api/property/listings", Ct)).StatusCode);
    }

    [Fact]
    public async Task Finance_and_moderators_cannot_suspend()
    {
        var agency = await kit.AgencyAsync("Safe Realty");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var (moderator, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        Assert.Equal(HttpStatusCode.Forbidden, (await Suspend(finance, agency.TenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Suspend(moderator, agency.TenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync($"/api/platform/businesses/{agency.TenantId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await (await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support)).Client
            .PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/suspend", new { reason = "x" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task User_page_lists_businesses_and_activity_and_support_can_unlock()
    {
        var agency = await kit.AgencyAsync("Busy Owner Realty", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        await api.CreateClient().PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Busy Owner", email = agency.Email, password = "password123", country = "NG", product = "courses", businessName = "Busy Owner Academy",
        }, Ct);
        var userId = await api.WithDbAsync(db => db.Users.Where(u => u.Email == agency.Email).Select(u => u.Id).SingleAsync(Ct));
        await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10)), Ct));
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        var search = await Json(await support.GetAsync($"/api/platform/users?q={Uri.EscapeDataString(agency.Email)}", Ct));
        Assert.Equal(userId, Assert.Single(search.EnumerateArray()).GetProperty("id").GetGuid());
        var user = await Json(await support.GetAsync($"/api/platform/users/{userId}", Ct));
        Assert.Equal(["courses", "property"], user.GetProperty("memberships").EnumerateArray().Select(m => m.GetProperty("product").GetString()!).Order());
        Assert.True(user.GetProperty("lockedOut").GetBoolean());

        (await support.PostAsync($"/api/platform/users/{userId}/unlock", null, Ct)).EnsureSuccessStatusCode();
        Assert.False((await Json(await support.GetAsync($"/api/platform/users/{userId}", Ct))).GetProperty("lockedOut").GetBoolean());
        (await support.PostAsync($"/api/platform/users/{userId}/send-login-code", null, Ct)).EnsureSuccessStatusCode();
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "otp" && n.Recipient == agency.Email, Ct)));
        (await support.PostAsync($"/api/platform/users/{userId}/sign-out-everywhere", null, Ct)).EnsureSuccessStatusCode();
        Assert.False(await api.WithDbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == userId && t.RevokedAt == null, Ct)));
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "user.unlock" && a.EntityId == userId.ToString(), Ct)));
    }
}
