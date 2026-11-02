using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyListingTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    [Fact]
    public async Task Unverified_agency_listing_goes_to_moderation()
    {
        var agency = await kit.AgencyAsync("New Homes");
        var listing = await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync());
        Assert.Equal("Draft", listing.GetProperty("status").GetString());

        var submitted = await Json(await kit.SubmitAsync(agency.Client, listing.GetProperty("id").GetGuid()));
        Assert.Equal("Pending", submitted.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Verified_agency_listing_goes_live_with_plan_expiry()
    {
        var agency = await kit.AgencyAsync("Trusted Homes", verified: true);
        var listing = await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync());
        var live = await Json(await kit.SubmitAsync(agency.Client, listing.GetProperty("id").GetGuid()));

        Assert.Equal("Active", live.GetProperty("status").GetString());
        var expires = live.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(5));
        Assert.Equal(4_150_000_00, live.GetProperty("moveInTotalMinor").GetInt64()); // ₦3.5m rent + ₦350k agency fee + ₦300k caution
    }

    [Fact]
    public async Task Free_plan_allows_three_listings()
    {
        var agency = await kit.AgencyAsync("Busy Agency", verified: true);
        for (var i = 0; i < 3; i++) await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"Flat {i}"));
        var fourth = await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Flat 4"));

        var response = await kit.SubmitAsync(agency.Client, fourth.GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task Renewing_at_the_plan_limit_is_refused()
    {
        var agency = await kit.AgencyAsync("Full Agency", verified: true);
        var old = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Old flat"));
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == old)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ListingStatus.Expired), Ct));
        for (var i = 0; i < 3; i++) await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"New flat {i}"));

        var renew = await agency.Client.PostAsync($"/api/property/listings/{old}/renew", null, Ct);

        Assert.Equal(HttpStatusCode.PaymentRequired, renew.StatusCode);
    }

    [Fact]
    public async Task Inspection_fee_cost_is_rejected()
    {
        var agency = await kit.AgencyAsync("Sneaky Agency");
        var body = await kit.ListingBodyAsync(costs: [new { kind = "Other", label = "Inspection fee", amountMinor = 10_000_00 }]);

        var response = await agency.Client.PostAsJsonAsync("/api/property/listings", body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Json(response)).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals("costs[0].label", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Another_agency_cannot_read_my_listing()
    {
        var mine = await kit.AgencyAsync("Mine Agency");
        var theirs = await kit.AgencyAsync("Their Agency");
        var listing = await kit.CreateListingAsync(mine.Client, await kit.ListingBodyAsync());

        var response = await theirs.Client.GetAsync($"/api/property/listings/{listing.GetProperty("id").GetGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Seeker_token_cannot_use_the_studio()
    {
        var seeker = await kit.SeekerAsync("Curious");
        Assert.Equal(HttpStatusCode.Forbidden, (await seeker.GetAsync("/api/property/listings", Ct)).StatusCode);
    }

    [Fact]
    public async Task Short_let_needs_its_details_and_a_nightly_price()
    {
        var agency = await kit.AgencyAsync("Stays Agency");
        var body = await kit.ListingBodyAsync(purpose: ListingPurpose.ShortLet, price: 5_000_000);
        body.Remove("shortLet");

        var response = await agency.Client.PostAsJsonAsync("/api/property/listings", body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
