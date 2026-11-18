using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Saas.IntegrationTests;

/// <summary>Wave 2 (roadmap 2026-11-17) OrderLink fixes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class OrderWave2Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Quote_ReturnsCurrentProductSlug()
    {
        var (seller, auth) = await api.SignupAsync("Rename");
        var slug = auth.GetProperty("tenant").GetProperty("slug").GetString();
        var dress = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Ankara Dress", priceMinor = 800_000, inStock = true, isActive = true }, Ct));
        var id = dress.GetProperty("id").GetGuid();

        var renamed = await seller.PutAsJsonAsync($"/api/products/{id}", new { name = "Ankara Maxi Dress", priceMinor = 800_000, inStock = true, isActive = true }, Ct);
        renamed.EnsureSuccessStatusCode();
        var newSlug = (await Json(renamed)).GetProperty("slug").GetString();
        Assert.NotEqual(dress.GetProperty("slug").GetString(), newSlug);

        var quote = await Json(await api.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/quote", new
        {
            items = new[] { new { productId = id, quantity = 1 } },
            fulfilment = "Pickup",
        }, Ct));

        Assert.Equal(newSlug, quote.GetProperty("lines")[0].GetProperty("productSlug").GetString());
    }

    [Fact]
    public async Task PublicStoreList_ListsActiveStoresWithProducts_AndPages()
    {
        var (seller, auth) = await api.SignupAsync("Listed");
        var slug = auth.GetProperty("tenant").GetProperty("slug").GetString()!;
        (await seller.PostAsJsonAsync("/api/products", new { name = "Zobo", priceMinor = 50_000, inStock = true, isActive = true }, Ct)).EnsureSuccessStatusCode();
        var (_, emptyAuth) = await api.SignupAsync("Unlisted");
        var emptySlug = emptyAuth.GetProperty("tenant").GetProperty("slug").GetString()!;
        var (suspended, suspendedAuth) = await api.SignupAsync("Suspended");
        (await suspended.PostAsJsonAsync("/api/products", new { name = "Kunu", priceMinor = 50_000, inStock = true, isActive = true }, Ct)).EnsureSuccessStatusCode();
        var suspendedId = suspendedAuth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.WithDbAsync(async db =>
        {
            var t = await db.Tenants.FindAsync([suspendedId], Ct);
            t!.Status = Saas.Domain.Core.TenantStatus.Suspended;
            return await db.SaveChangesAsync(Ct);
        });

        var anon = api.CreateClient();
        var slugs = new List<string>();
        for (var page = 1; ; page++)
        {
            var response = await anon.GetAsync($"/api/public/stores?page={page}&pageSize=500", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Json(response);
            var items = body.GetProperty("items").EnumerateArray().ToList();
            slugs.AddRange(items.Select(i => i.GetProperty("slug").GetString()!));
            if (page * 500 >= body.GetProperty("total").GetInt32()) break;
        }

        Assert.Contains(slug, slugs);
        Assert.DoesNotContain(emptySlug, slugs);
        Assert.DoesNotContain(suspendedAuth.GetProperty("tenant").GetProperty("slug").GetString()!, slugs);

        var capped = await Json(await anon.GetAsync("/api/public/stores?pageSize=100000", Ct));
        Assert.True(capped.GetProperty("pageSize").GetInt32() <= 1000);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
