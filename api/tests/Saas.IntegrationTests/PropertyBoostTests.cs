using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Property;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyBoostTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private async Task<JsonElement> BuyAsync(HttpClient agency, Guid listingId, string code)
    {
        var response = await agency.PostAsJsonAsync($"/api/property/listings/{listingId}/boosts", new { code }, Ct);
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    private async Task<JsonElement> PayAsync(HttpClient agency, JsonElement purchase, long amount)
    {
        var reference = api.Gateway.Initialized.Last(i => purchase.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, amount, "NGN", DateTimeOffset.UtcNow);
        return await Json(await agency.PostAsJsonAsync($"/api/property/boosts/{purchase.GetProperty("boostId").GetGuid()}/verify", new { reference }, Ct));
    }

    private Task<DateTimeOffset?> TopUntilAsync(Guid listingId) =>
        api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId).Select(l => l.TopUntil).SingleAsync(Ct));

    [Fact]
    public async Task Boost_options_show_the_prices()
    {
        var agency = await kit.AgencyAsync("Options Agency");
        var options = await Json(await agency.Client.GetAsync("/api/property/boost-options", Ct));
        Assert.Equal([("featured-7", 300_000L), ("featured-30", 1_000_000L), ("top-7", 500_000L)],
            options.EnumerateArray().Select(o => (o.GetProperty("code").GetString()!, o.GetProperty("priceMinor").GetInt64())));
    }

    [Fact]
    public async Task Top_boost_moves_listing_to_the_top_of_search()
    {
        var band = Random.Shared.NextInt64(100_000, 900_000) * 10_000_00;
        var agency = await kit.AgencyAsync("Boosting Agency", verified: true);
        var older = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Older listing", price: band + 1));
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Newer listing", price: band + 2));
        var search = $"/api/listings?minPrice={band}&maxPrice={band + 9_999_00}";
        Assert.Equal("Newer listing", (await Json(await api.CreateClient().GetAsync(search, Ct))).GetProperty("items").GetProperty("items")[0].GetProperty("title").GetString());

        var boost = await PayAsync(agency.Client, await BuyAsync(agency.Client, older, "top-7"), 500_000);

        Assert.Equal("Active", boost.GetProperty("status").GetString());
        var first = (await Json(await api.CreateClient().GetAsync(search, Ct))).GetProperty("items").GetProperty("items")[0];
        Assert.Equal("Older listing", first.GetProperty("title").GetString());
        Assert.True(first.GetProperty("top").GetBoolean());
    }

    [Fact]
    public async Task Boost_confirm_is_idempotent_and_checks_amount()
    {
        var agency = await kit.AgencyAsync("Careful Booster", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var purchase = await BuyAsync(agency.Client, id, "top-7");

        Assert.Equal("Pending", (await PayAsync(agency.Client, purchase, 1_000)).GetProperty("status").GetString());
        Assert.Null(await TopUntilAsync(id));

        await PayAsync(agency.Client, purchase, 500_000);
        await PayAsync(agency.Client, purchase, 500_000);
        using (var scope = api.Services.CreateScope())
        {
            var reference = await api.WithDbAsync(db => db.Boosts.IgnoreQueryFilters().Where(b => b.ListingId == id).Select(b => b.Reference).SingleAsync(Ct));
            await scope.ServiceProvider.GetRequiredService<BoostService>().ConfirmAsync(reference, 500_000, "NGN", Ct); // the webhook
        }
        Assert.InRange((await TopUntilAsync(id))!.Value, DateTimeOffset.UtcNow.AddDays(6.9), DateTimeOffset.UtcNow.AddDays(7.1));
    }

    [Fact]
    public async Task Only_live_listings_can_be_boosted_and_only_by_owners()
    {
        var agency = await kit.AgencyAsync("Draft Booster");
        var draft = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await agency.Client.PostAsJsonAsync($"/api/property/listings/{draft}/boosts", new { code = "top-7" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await agency.Client.PostAsJsonAsync($"/api/property/listings/{draft}/boosts", new { code = "gold-99" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Expired_boost_stops_ranking()
    {
        var agency = await kit.AgencyAsync("Fading Booster", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        await PayAsync(agency.Client, await BuyAsync(agency.Client, id, "featured-7"), 300_000);
        await api.WithDbAsync(async db =>
        {
            await db.Boosts.IgnoreQueryFilters().Where(b => b.ListingId == id).ExecuteUpdateAsync(s => s.SetProperty(b => b.EndsAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
            return await db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.FeaturedUntil, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);
        });

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PropertyJobs>().ExpireBoostsAsync(DateTimeOffset.UtcNow, Ct);

        var boosts = await Json(await agency.Client.GetAsync("/api/property/boosts", Ct));
        Assert.Equal("Expired", boosts[0].GetProperty("status").GetString());
        var listing = await Json(await agency.Client.GetAsync($"/api/property/listings/{id}", Ct));
        Assert.True(listing.GetProperty("featuredUntil").GetDateTimeOffset() < DateTimeOffset.UtcNow);
    }
}
