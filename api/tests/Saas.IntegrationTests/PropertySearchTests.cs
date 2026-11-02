using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertySearchTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    /// <summary>Tests share one database, so each gets its own price band to search within.</summary>
    private static long Band() => Random.Shared.NextInt64(100_000, 900_000) * 10_000_00;

    private static string Range(long band) => $"minPrice={band}&maxPrice={band + 9_999_00}";

    private async Task<JsonElement> SearchAsync(string query) =>
        await Json(await api.CreateClient().GetAsync($"/api/listings?{query}", Ct));

    private static List<string> Titles(JsonElement result) =>
        result.GetProperty("items").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList();

    [Fact]
    public async Task Search_by_city_includes_its_areas()
    {
        var band = Band();
        var agency = await kit.AgencyAsync("City Agency", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Lekki flat", price: band + 1));
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Yaba flat", area: "yaba-lagos", lat: 6.5095, lng: 3.3711, price: band + 2));

        Assert.Equal(["Lekki flat", "Yaba flat"], Titles(await SearchAsync($"location=lagos&{Range(band)}")).Order());
        Assert.Equal(["Lekki flat"], Titles(await SearchAsync($"location=lekki-lagos&{Range(band)}")));
        var state = await SearchAsync($"location=lagos-state&{Range(band)}");
        Assert.Equal(2, state.GetProperty("items").GetProperty("total").GetInt32());
        Assert.Equal("Lagos State", state.GetProperty("location").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Bbox_limits_results_to_the_map()
    {
        var band = Band();
        var agency = await kit.AgencyAsync("Map Agency", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Inside", price: band + 1));
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Outside", area: "yaba-lagos", lat: 6.5095, lng: 3.3711, price: band + 2));

        var result = await SearchAsync($"bbox=3.44,6.43,3.50,6.46&{Range(band)}");

        Assert.Equal(["Inside"], Titles(result));
        var pin = result.GetProperty("pins")[0];
        Assert.Equal(6.4474, pin.GetProperty("lat").GetDouble(), 4);
        Assert.Equal(3.4720, pin.GetProperty("lng").GetDouble(), 4);
    }

    [Fact]
    public async Task Unknown_location_is_404_and_bad_bbox_is_400()
    {
        var anon = api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/listings?location=atlantis", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/listings?bbox=5,6,3,4", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/listings?bbox=1,2,3", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/listings?bbox=1,2,3,abc", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/listings?bbox=1,-95,3,4", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/listings?minPrice=500&maxPrice=100", Ct)).StatusCode);
    }

    [Fact]
    public async Task Only_active_listings_are_public()
    {
        var band = Band();
        var unverified = await kit.AgencyAsync("Unverified Agency");
        var pending = await kit.LiveListingAsync(unverified.Client, await kit.ListingBodyAsync(title: "Pending flat", price: band + 1));
        var verified = await kit.AgencyAsync("Live Agency", verified: true);
        var expired = await kit.LiveListingAsync(verified.Client, await kit.ListingBodyAsync(title: "Expired flat", price: band + 2));
        await kit.LiveListingAsync(verified.Client, await kit.ListingBodyAsync(title: "Live flat", price: band + 3));
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == expired)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ListingStatus.Expired), Ct));

        Assert.Equal(["Live flat"], Titles(await SearchAsync(Range(band))));
        var pendingSlug = (await Json(await unverified.Client.GetAsync($"/api/property/listings/{pending}", Ct))).GetProperty("slug").GetString();
        var agencySlug = (await Json(await unverified.Client.GetAsync("/api/property/profile/agency", Ct))).GetProperty("slug").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/api/listings/{agencySlug}/{pendingSlug}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Filters_combine()
    {
        var band = Band();
        var pool = await kit.AmenityAsync("pool");
        var agency = await kit.AgencyAsync("Filter Agency", verified: true);
        await api.MakeProAsync(agency.TenantId, "property"); // four live listings: over the Free limit
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Big with pool", price: band + 5, beds: 4, amenityIds: [pool]));
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Big no pool", price: band + 6, beds: 4));
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Small with pool", price: band + 7, beds: 1, amenityIds: [pool]));
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "For sale", price: band + 8, beds: 4, purpose: ListingPurpose.Sale, amenityIds: [pool]));

        Assert.Equal(["Big with pool"], Titles(await SearchAsync($"purpose=Rent&beds=3&amenities=pool&type=apartment&{Range(band)}")));
        Assert.Equal(["For sale"], Titles(await SearchAsync($"purpose=Sale&{Range(band)}")));
        Assert.Equal(["Big no pool", "Big with pool", "Small with pool"], Titles(await SearchAsync($"purpose=Rent&sort=price_asc&{Range(band)}")).Order());
        Assert.Equal("Small with pool", Titles(await SearchAsync($"purpose=Rent&sort=price_desc&{Range(band)}"))[0]);
    }

    [Fact]
    public async Task Listing_page_shows_costs_agent_and_location()
    {
        var agency = await kit.AgencyAsync("Detail Agency", verified: true);
        (await agency.Client.PutAsJsonAsync("/api/property/profile/agent", new { displayName = "Tunde Bakare", phone = "+2348030000000", whatsApp = "+2348030000000" }, Ct))
            .EnsureSuccessStatusCode();
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Detail flat"));
        var slugs = await api.WithDbAsync(async db => await (
            from l in db.Listings.IgnoreQueryFilters()
            join a in db.AgencyProfiles.IgnoreQueryFilters() on l.TenantId equals a.TenantId
            where l.Id == id
            select new { Listing = l.Slug, Agency = a.Slug }).SingleAsync(Ct));

        var detail = await Json(await api.CreateClient().GetAsync($"/api/listings/{slugs.Agency}/{slugs.Listing}", Ct));

        Assert.Equal(4_150_000_00, detail.GetProperty("moveInTotalMinor").GetInt64());
        Assert.Equal(2, detail.GetProperty("costs").GetArrayLength());
        Assert.Equal("Tunde Bakare", detail.GetProperty("agent").GetProperty("displayName").GetString());
        Assert.True(detail.GetProperty("agent").GetProperty("verified").GetBoolean());
        Assert.Equal(["Lagos State", "Lagos", "Lekki"], detail.GetProperty("breadcrumb").EnumerateArray().Select(b => b.GetProperty("name").GetString()!));
        Assert.Equal("Apartment", detail.GetProperty("type").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Views_are_counted_once_per_visitor_per_day()
    {
        var agency = await kit.AgencyAsync("Views Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var visitor = api.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsJsonAsync($"/api/listings/{id}/events", new { kind = "View" }, Ct)).StatusCode);
        await visitor.PostAsJsonAsync($"/api/listings/{id}/events", new { kind = "WhatsApp" }, Ct);
        await visitor.PostAsJsonAsync($"/api/listings/{id}/events", new { kind = "WhatsApp" }, Ct);

        var stat = await api.WithDbAsync(db => db.ListingStats.IgnoreQueryFilters().SingleAsync(s => s.ListingId == id, Ct));
        Assert.Equal((1, 2), (stat.Views, stat.WhatsAppClicks));
        Assert.Equal(1, (await Json(await agency.Client.GetAsync($"/api/property/listings/{id}", Ct))).GetProperty("viewsCount").GetInt32());
    }

    [Fact]
    public async Task Stats_are_a_paid_feature()
    {
        var agency = await kit.AgencyAsync("Stats Agency", verified: true);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await agency.Client.GetAsync("/api/property/stats", Ct)).StatusCode);
        await api.MakeProAsync(agency.TenantId, "property");
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        await api.CreateClient().PostAsJsonAsync($"/api/listings/{id}/events", new { kind = "Call" }, Ct);

        var stats = await Json(await agency.Client.GetAsync("/api/property/stats?days=30", Ct));
        var row = stats.GetProperty("listings").EnumerateArray().Single(r => r.GetProperty("listingId").GetGuid() == id);
        Assert.Equal(1, row.GetProperty("callClicks").GetInt32());
    }

    [Fact]
    public async Task Agent_and_agency_pages_list_live_listings()
    {
        var agency = await kit.AgencyAsync("Pages Agency", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Page flat"));
        var agent = await Json(await agency.Client.GetAsync("/api/property/profile/agent", Ct));
        var agencySlug = (await Json(await agency.Client.GetAsync("/api/property/profile/agency", Ct))).GetProperty("slug").GetString();

        var agentPage = await Json(await api.CreateClient().GetAsync($"/api/agents/{agent.GetProperty("slug").GetString()}", Ct));
        Assert.Equal(["Page flat"], agentPage.GetProperty("listings").EnumerateArray().Select(l => l.GetProperty("title").GetString()!));
        var agencyPage = await Json(await api.CreateClient().GetAsync($"/api/agencies/{agencySlug}", Ct));
        Assert.Equal("Pages Agency", agencyPage.GetProperty("agency").GetProperty("name").GetString());
        Assert.Equal(1, agencyPage.GetProperty("listings").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/agents/nobody-at-all", Ct)).StatusCode);
    }

    [Fact]
    public async Task Property_types_and_amenities_for_the_wizard()
    {
        var types = await Json(await api.CreateClient().GetAsync("/api/property-types", Ct));
        Assert.Equal("Apartment", types[0].GetProperty("name").GetString());
        var amenities = await Json(await api.CreateClient().GetAsync("/api/amenities", Ct));
        Assert.Contains(amenities.EnumerateArray(), a => a.GetProperty("slug").GetString() == "pool");
    }

    [Fact]
    public async Task Locations_and_area_guides()
    {
        var states = await Json(await api.CreateClient().GetAsync("/api/locations", Ct));
        Assert.Equal(37, states.GetArrayLength());
        var lagosAreas = await Json(await api.CreateClient().GetAsync("/api/locations?parent=lagos", Ct));
        Assert.Contains(lagosAreas.EnumerateArray(), a => a.GetProperty("slug").GetString() == "lekki-lagos");

        var guide = await Json(await api.CreateClient().GetAsync("/api/areas/lekki-lagos", Ct));
        Assert.Equal("Lekki", guide.GetProperty("location").GetProperty("name").GetString());
        Assert.True(guide.TryGetProperty("rentByBedrooms", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/areas/atlantis", Ct)).StatusCode);
    }
}
