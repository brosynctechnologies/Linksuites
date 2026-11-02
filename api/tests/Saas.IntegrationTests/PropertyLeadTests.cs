using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyLeadTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private async Task<string> AgentSlugAsync(HttpClient agency) =>
        (await Json(await agency.GetAsync("/api/property/profile/agent", Ct))).GetProperty("slug").GetString()!;

    [Fact]
    public async Task Enquiry_reaches_the_agent_inbox_and_email_outbox()
    {
        var agency = await kit.AgencyAsync("Lead Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());

        var sent = await api.CreateClient().PostAsJsonAsync($"/api/listings/{id}/enquiries",
            new { name = "Chidi Okafor", phone = "+2348011111111", email = "chidi@example.com", message = "Is it still available? Can I view on Saturday?" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);

        var inbox = await Json(await agency.Client.GetAsync("/api/property/enquiries", Ct));
        var enquiry = inbox.EnumerateArray().Single();
        Assert.Equal("Chidi Okafor", enquiry.GetProperty("name").GetString());
        Assert.Equal(id, enquiry.GetProperty("listingId").GetGuid());
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "enquiry.new" && n.Recipient == agency.Email, Ct)));
        Assert.Equal(1, await api.WithDbAsync(db => db.ListingStats.IgnoreQueryFilters().Where(s => s.ListingId == id).SumAsync(s => s.Enquiries, Ct)));

        (await agency.Client.PostAsJsonAsync($"/api/property/enquiries/{enquiry.GetProperty("id").GetGuid()}/status", new { status = "Replied" }, Ct))
            .EnsureSuccessStatusCode();
        Assert.Equal("Replied", (await Json(await agency.Client.GetAsync("/api/property/enquiries", Ct)))[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Inspection_slot_cannot_be_double_booked()
    {
        var agency = await kit.AgencyAsync("Viewing Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var slots = Enum.GetValues<DayOfWeek>().Select(d => new { weekday = d.ToString(), start = "09:00", end = "17:00", slotMinutes = 60 });
        (await agency.Client.PutAsJsonAsync("/api/property/inspection-slots", new { slots }, Ct)).EnsureSuccessStatusCode();

        var times = await Json(await api.CreateClient().GetAsync($"/api/listings/{id}/inspection-times", Ct));
        Assert.True(times.GetArrayLength() >= 8 * 13);
        var first = times[0].GetDateTimeOffset();

        var amaka = await kit.SeekerAsync("Amaka");
        var booked = await amaka.PostAsJsonAsync($"/api/listings/{id}/inspections", new { at = first, phone = "+2348022222222" }, Ct);
        booked.EnsureSuccessStatusCode();
        var bayo = await kit.SeekerAsync("Bayo");
        Assert.Equal(HttpStatusCode.Conflict, (await bayo.PostAsJsonAsync($"/api/listings/{id}/inspections", new { at = first }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bayo.PostAsJsonAsync($"/api/listings/{id}/inspections", new { at = first.AddMinutes(17) }, Ct)).StatusCode);
        Assert.DoesNotContain((await Json(await api.CreateClient().GetAsync($"/api/listings/{id}/inspection-times", Ct))).EnumerateArray(),
            t => t.GetDateTimeOffset() == first);

        var studio = await Json(await agency.Client.GetAsync("/api/property/inspections", Ct));
        var inspection = studio.EnumerateArray().Single();
        Assert.Equal("Amaka Seeker", inspection.GetProperty("seekerName").GetString());
        (await agency.Client.PostAsJsonAsync($"/api/property/inspections/{inspection.GetProperty("id").GetGuid()}/status", new { status = "Confirmed" }, Ct))
            .EnsureSuccessStatusCode();
        var mine = await Json(await amaka.GetAsync("/api/me/inspections", Ct));
        Assert.Equal("Confirmed", mine[0].GetProperty("status").GetString());
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "inspection.confirmed", Ct)));
    }

    [Fact]
    public async Task Instant_saved_search_is_emailed_when_a_match_goes_live()
    {
        var band = Random.Shared.NextInt64(100_000, 900_000) * 10_000_00;
        var wantsLekki = await kit.SeekerAsync("Lekki Hunter");
        var wantsYaba = await kit.SeekerAsync("Yaba Hunter");
        (await wantsLekki.PostAsJsonAsync("/api/me/saved-searches", new
        {
            name = "Lekki 3 beds", frequency = "Instant",
            filter = new { purpose = "Rent", location = "lekki-lagos", minPrice = band, maxPrice = band + 9_999_00, minBeds = 3 },
        }, Ct)).EnsureSuccessStatusCode();
        (await wantsYaba.PostAsJsonAsync("/api/me/saved-searches", new
        {
            name = "Yaba", frequency = "Instant", filter = new { purpose = "Rent", location = "yaba-lagos", minPrice = band, maxPrice = band + 9_999_00 },
        }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await wantsYaba.PostAsJsonAsync("/api/me/saved-searches", new
        {
            name = "Nowhere", frequency = "Instant", filter = new { location = "atlantis" },
        }, Ct)).StatusCode);

        var agency = await kit.AgencyAsync("Alert Agency", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Fresh Lekki flat", price: band + 5));

        var alerts = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "search.alert").ToListAsync(Ct)))
            .Where(n => n.Payload.GetValueOrDefault("listing") == "Fresh Lekki flat").Select(n => n.Recipient).ToList();
        var lekkiEmail = (await Json(await wantsLekki.GetAsync("/api/me", Ct))).GetProperty("user").GetProperty("email").GetString();
        Assert.Equal([lekkiEmail], alerts);
        Assert.Single((await Json(await wantsLekki.GetAsync("/api/me/saved-searches", Ct))).EnumerateArray());
    }

    [Fact]
    public async Task Review_requires_an_earlier_enquiry()
    {
        var agency = await kit.AgencyAsync("Reviewed Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var slug = await AgentSlugAsync(agency.Client);
        var seeker = await kit.SeekerAsync("Reviewer");
        var review = new { rating = 4, comment = "Showed up on time and the flat matched the photos." };

        Assert.Equal(HttpStatusCode.Forbidden, (await seeker.PostAsJsonAsync($"/api/agents/{slug}/reviews", review, Ct)).StatusCode);
        (await seeker.PostAsJsonAsync($"/api/listings/{id}/enquiries", new { name = "Reviewer", phone = "+2348033333333", message = "Hello" }, Ct))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await seeker.PostAsJsonAsync($"/api/agents/{slug}/reviews", review, Ct)).StatusCode); // too soon
        await api.WithDbAsync(db => db.Enquiries.IgnoreQueryFilters().Where(e => e.ListingId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Ct));

        (await seeker.PostAsJsonAsync($"/api/agents/{slug}/reviews", review, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await seeker.PostAsJsonAsync($"/api/agents/{slug}/reviews", review, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agency.Client.PostAsJsonAsync($"/api/agents/{slug}/reviews", review, Ct)).StatusCode);
    }

    [Fact]
    public async Task Rating_updates_the_agent_profile_and_reported_reviews_are_hidden()
    {
        var agency = await kit.AgencyAsync("Rated Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var slug = await AgentSlugAsync(agency.Client);
        foreach (var (name, rating) in new[] { ("Ngozi", 5), ("Emeka", 3) })
        {
            var seeker = await kit.SeekerAsync(name);
            await seeker.PostAsJsonAsync($"/api/listings/{id}/enquiries", new { name, phone = "+2348044444444", message = "Hi" }, Ct);
            await api.WithDbAsync(db => db.Enquiries.IgnoreQueryFilters().Where(e => e.ListingId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Ct));
            (await seeker.PostAsJsonAsync($"/api/agents/{slug}/reviews", new { rating, comment = "Fine" }, Ct)).EnsureSuccessStatusCode();
        }

        var page = await Json(await api.CreateClient().GetAsync($"/api/agents/{slug}", Ct));
        Assert.Equal(4.0, page.GetProperty("agent").GetProperty("ratingAverage").GetDouble());
        Assert.Equal(2, page.GetProperty("reviews").GetArrayLength());

        var low = page.GetProperty("reviews").EnumerateArray().Single(r => r.GetProperty("rating").GetInt32() == 3).GetProperty("id").GetGuid();
        (await (await kit.SeekerAsync("Reporter")).PostAsync($"/api/reviews/{low}/report", null, Ct)).EnsureSuccessStatusCode(); // agencies may not report their own reviews
        page = await Json(await api.CreateClient().GetAsync($"/api/agents/{slug}", Ct));
        Assert.Equal((5.0, 1), (page.GetProperty("agent").GetProperty("ratingAverage").GetDouble(), page.GetProperty("reviews").GetArrayLength()));

        var admin = await kit.AdminAsync();
        (await admin.PostAsJsonAsync($"/api/admin/property/reviews/{low}/decision", new { restore = true }, Ct)).EnsureSuccessStatusCode();
        page = await Json(await api.CreateClient().GetAsync($"/api/agents/{slug}", Ct));
        Assert.Equal(4.0, page.GetProperty("agent").GetProperty("ratingAverage").GetDouble());
    }

    [Fact]
    public async Task Seekers_save_listings_and_see_their_enquiries()
    {
        var agency = await kit.AgencyAsync("Saved Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Saveable flat"));
        var seeker = await kit.SeekerAsync("Saver");

        Assert.Equal(HttpStatusCode.NoContent, (await seeker.PostAsync($"/api/me/saved-listings/{id}", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await seeker.PostAsync($"/api/me/saved-listings/{id}", null, Ct)).StatusCode); // idempotent
        var saved = await Json(await seeker.GetAsync("/api/me/saved-listings", Ct));
        Assert.Equal(["Saveable flat"], saved.EnumerateArray().Select(s => s.GetProperty("title").GetString()!));

        await seeker.PostAsJsonAsync($"/api/listings/{id}/enquiries", new { name = "Saver", phone = "+2348055555555", message = "Hi" }, Ct);
        var enquiries = await Json(await seeker.GetAsync("/api/me/enquiries", Ct));
        Assert.Equal("Saveable flat", enquiries[0].GetProperty("listingTitle").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await seeker.DeleteAsync($"/api/me/saved-listings/{id}", Ct)).StatusCode);
        Assert.Equal(0, (await Json(await seeker.GetAsync("/api/me/saved-listings", Ct))).GetArrayLength());
    }
}
