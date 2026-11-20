using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Wave 3 item 5: a signed-in owner adds another business (same product, or another one) without a new account.</summary>
[Collection(nameof(ApiCollection))]
public sealed class MultiBusinessTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {text}");
        return JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
    }

    private HttpClient As(JsonElement auth)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    private static Guid TenantId(JsonElement auth) => auth.GetProperty("tenant").GetProperty("id").GetGuid();

    [Fact]
    public async Task Owner_adds_a_second_store_and_is_switched_to_it()
    {
        var (owner, first) = await api.SignupAsync("TwoShops");

        var added = await Json(await owner.PostAsJsonAsync("/api/me/businesses",
            new { name = "TwoShops Second Store", whatsappNumber = "08031112222" }, Ct));

        var tenant = added.GetProperty("tenant");
        Assert.Equal("TwoShops Second Store", tenant.GetProperty("name").GetString());
        Assert.Equal("Owner", tenant.GetProperty("role").GetString());
        Assert.Equal(Products.WhatsAppOrders, tenant.GetProperty("product").GetString());
        Assert.NotEqual(TenantId(first), TenantId(added));
        Assert.NotEqual(first.GetProperty("tenant").GetProperty("slug").GetString(), tenant.GetProperty("slug").GetString());
        Assert.Equal(first.GetProperty("user").GetProperty("id").GetGuid(), added.GetProperty("user").GetProperty("id").GetGuid());

        // The product's signup setup ran: the new business has its own store with the WhatsApp number.
        var second = As(added);
        var store = await Json(await second.GetAsync("/api/store", Ct));
        Assert.Equal("TwoShops Second Store", store.GetProperty("name").GetString());
        Assert.Equal("2348031112222", store.GetProperty("whatsappNumber").GetString());

        // Both show in the switcher; the new one is current, and switching back works.
        var list = (await Json(await second.GetAsync("/api/me/businesses", Ct))).EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.True(list.Single(b => b.GetProperty("tenantId").GetGuid() == TenantId(added)).GetProperty("current").GetBoolean());
        var back = await Json(await second.PostAsJsonAsync("/api/auth/switch", new { tenantId = TenantId(first) }, Ct));
        Assert.Equal(TenantId(first), TenantId(back));
    }

    [Fact]
    public async Task Plan_limits_apply_per_business()
    {
        var (owner, first) = await api.SignupAsync("PerBizPlan");
        await api.MakeProAsync(TenantId(first), Products.WhatsAppOrders);
        var added = await Json(await owner.PostAsJsonAsync("/api/me/businesses",
            new { name = "PerBizPlan Free Store", whatsappNumber = "08031112223" }, Ct));

        var firstPlan = await api.AsTenantAsync<PlanService, Plan>(TenantId(first), s => s.GetCurrentPlanAsync(Ct));
        var secondPlan = await api.AsTenantAsync<PlanService, Plan>(TenantId(added), s => s.GetCurrentPlanAsync(Ct));
        Assert.Equal("pro", firstPlan.Code);
        Assert.Equal("free", secondPlan.Code);

        // The new store is held to the free plan's limits, whatever the first one pays for.
        var billing = await Json(await As(added).GetAsync("/api/billing", Ct));
        Assert.Equal("free", billing.GetProperty("currentPlan").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Owner_adds_a_business_of_another_product_and_can_sign_in_to_it()
    {
        var email = $"crossprod-{Guid.NewGuid():N}@example.com";
        var client = api.CreateClient();
        var signup = await Json(await api.SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = "Cross Product", email, password = "password123", country = "NG",
            storeName = "Cross Store", whatsappNumber = "08031234567",
        }, Ct));

        var added = await Json(await As(signup).PostAsJsonAsync("/api/me/businesses",
            new { product = "invoices", name = "Cross Consulting" }, Ct));
        Assert.Equal("invoices", added.GetProperty("tenant").GetProperty("product").GetString());

        var login = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email, password = "password123", product = "invoices" }, Ct));
        Assert.Equal("Cross Consulting", login.GetProperty("tenant").GetProperty("name").GetString());
        Assert.True(await api.WithDbAsync(db => db.BusinessSettings.IgnoreQueryFilters().AnyAsync(p => p.TenantId == TenantId(added), Ct)));
    }

    [Fact]
    public async Task Adding_a_business_checks_the_products_signup_fields()
    {
        var (owner, _) = await api.SignupAsync("AddBizRules");

        var noNumber = await owner.PostAsJsonAsync("/api/me/businesses", new { name = "No Number Store" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noNumber.StatusCode);
        var noName = await owner.PostAsJsonAsync("/api/me/businesses", new { name = " ", whatsappNumber = "08031234567" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        var unknown = await owner.PostAsJsonAsync("/api/me/businesses", new { product = "rockets", name = "Moon Co" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var slug = await owner.PostAsJsonAsync("/api/me/businesses", new { name = "Slug Store", whatsappNumber = "08031234567", slug = "my-own-link" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, slug.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.CreateClient().PostAsJsonAsync("/api/me/businesses", new { name = "Anon Store", whatsappNumber = "08031234567" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_an_owner_adds_businesses()
    {
        var (owner, auth) = await api.SignupAsync("AddBizStaff");
        await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == TenantId(auth))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, MemberRole.Admin), Ct));
        var refreshed = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = auth.GetProperty("refreshToken").GetString() }, Ct));

        var r = await As(refreshed).PostAsJsonAsync("/api/me/businesses", new { name = "Admin Store", whatsappNumber = "08031234567" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task An_account_owns_at_most_ten_businesses_of_one_product()
    {
        var (owner, _) = await api.SignupAsync("ManyShops");
        for (var i = 2; i <= 10; i++)
            await Json(await owner.PostAsJsonAsync("/api/me/businesses", new { name = $"ManyShops {i}", whatsappNumber = "08031234567" }, Ct));

        var eleventh = await owner.PostAsJsonAsync("/api/me/businesses", new { name = "ManyShops 11", whatsappNumber = "08031234567" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
        Assert.Contains("10", await eleventh.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Adding_businesses_at_once_never_passes_the_limit()
    {
        var (owner, auth) = await api.SignupAsync("RaceShops");
        for (var i = 2; i <= 9; i++)
            await Json(await owner.PostAsJsonAsync("/api/me/businesses", new { name = $"RaceShops {i}", whatsappNumber = "08031234567" }, Ct));

        // Five tabs add the tenth at the same moment: one gets it.
        var answers = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            owner.PostAsJsonAsync("/api/me/businesses", new { name = $"RaceShops last {i}", whatsappNumber = "08031234567" }, Ct)));
        Assert.Single(answers, a => a.IsSuccessStatusCode);
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(10, await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().CountAsync(m => m.UserId == userId && m.Role == MemberRole.Owner, Ct)));

        var badNumber = await owner.PostAsJsonAsync("/api/me/businesses", new { name = "Bad Number", whatsappNumber = "call me" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badNumber.StatusCode);
    }
}
