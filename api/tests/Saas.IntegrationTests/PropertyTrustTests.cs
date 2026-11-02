using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyTrustTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private Task<string> StatusAsync(Guid listingId) =>
        api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId).Select(l => l.Status.ToString()).SingleAsync(Ct));

    private static Task<HttpResponseMessage> ReportAsync(HttpClient client, Guid listingId, string reason = "Fake") =>
        client.PostAsJsonAsync($"/api/listings/{listingId}/report", new { reason, details = "The photos are from another listing." }, Ct);

    [Fact]
    public async Task Three_reports_hide_a_listing()
    {
        var agency = await kit.AgencyAsync("Reported Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());

        for (var i = 0; i < 2; i++) (await ReportAsync(await kit.SeekerAsync($"Reporter{i}"), id)).EnsureSuccessStatusCode();
        Assert.Equal("Active", await StatusAsync(id));
        (await ReportAsync(await kit.SeekerAsync("Reporter3"), id, "Scam")).EnsureSuccessStatusCode();

        Assert.Equal("Hidden", await StatusAsync(id));
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "listing.hidden" && n.Recipient == agency.Email, Ct)));
    }

    [Fact]
    public async Task Same_user_cannot_report_twice()
    {
        var agency = await kit.AgencyAsync("Twice Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var seeker = await kit.SeekerAsync("Repeat");

        (await ReportAsync(seeker, id)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await ReportAsync(seeker, id)).StatusCode);
    }

    [Fact]
    public async Task Admin_approves_pending_listing()
    {
        var agency = await kit.AgencyAsync("Waiting Agency");
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var admin = await kit.AdminAsync();

        var queue = await Json(await admin.GetAsync("/api/admin/property/moderation", Ct));
        var item = queue.EnumerateArray().Single(i => i.GetProperty("listingId").GetGuid() == id);
        Assert.Equal("Waiting Agency", item.GetProperty("agencyName").GetString());
        Assert.False(item.GetProperty("agencyVerified").GetBoolean());

        (await admin.PostAsJsonAsync($"/api/admin/property/listings/{id}/decision", new { approve = true }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("Active", await StatusAsync(id));
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "listing.approved" && n.Recipient == agency.Email, Ct)));
    }

    [Fact]
    public async Task Reject_needs_a_note()
    {
        var agency = await kit.AgencyAsync("Rejected Agency");
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var admin = await kit.AdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/property/listings/{id}/decision", new { approve = false }, Ct)).StatusCode);
        (await admin.PostAsJsonAsync($"/api/admin/property/listings/{id}/decision", new { approve = false, note = "Add real photos of the flat." }, Ct))
            .EnsureSuccessStatusCode();

        var mine = await Json(await agency.Client.GetAsync($"/api/property/listings/{id}", Ct));
        Assert.Equal("Rejected", mine.GetProperty("status").GetString());
        Assert.Equal("Add real photos of the flat.", mine.GetProperty("moderationNote").GetString());
    }

    [Fact]
    public async Task Moderation_is_for_platform_admins_only()
    {
        var agency = await kit.AgencyAsync("Nosy Agency");
        var seeker = await kit.SeekerAsync("Nosy");
        Assert.Equal(HttpStatusCode.Forbidden, (await agency.Client.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seeker.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);
    }

    private static MultipartFormDataContent Documents(string licence = "LASRERA-1234") => new()
    {
        { new ByteArrayContent(PropertyMediaTests.Photo(9, 300, 200)), "idDocument", "id-card.png" },
        { new ByteArrayContent("%PDF-1.7\n%business registration\n"u8.ToArray()), "businessDocument", "cac.pdf" },
        { new StringContent(licence), "licenceNo" },
    };

    [Fact]
    public async Task Verification_documents_are_admin_only()
    {
        var agency = await kit.AgencyAsync("Docs Agency");
        var request = await Json(await agency.Client.PostAsync("/api/property/verification", Documents(), Ct));
        Assert.Equal("Pending", request.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await agency.Client.PostAsync("/api/property/verification", Documents(), Ct)).StatusCode);

        var admin = await kit.AdminAsync();
        var queue = await Json(await admin.GetAsync("/api/admin/property/verifications", Ct));
        var item = queue.EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == request.GetProperty("id").GetGuid());
        Assert.Equal(2, item.GetProperty("documents").GetArrayLength());
        var docUrl = $"/api/admin/property/verifications/{item.GetProperty("id").GetGuid()}/documents/1";
        var doc = await admin.GetAsync(docUrl, Ct);
        Assert.Equal(HttpStatusCode.OK, doc.StatusCode);
        Assert.Equal("application/pdf", doc.Content.Headers.ContentType?.MediaType);

        var other = await kit.AgencyAsync("Other Docs Agency");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Client.GetAsync(docUrl, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await kit.SeekerAsync("Snoop")).GetAsync(docUrl, Ct)).StatusCode);
        var key = await api.WithDbAsync(db => db.VerificationRequests.IgnoreQueryFilters()
            .Where(v => v.Id == request.GetProperty("id").GetGuid()).Select(v => v.Documents).SingleAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/uploads/{key[1].Key}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Approved_verification_adds_the_badge_and_releases_waiting_listings()
    {
        var agency = await kit.AgencyAsync("Badge Agency");
        var waiting = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var request = await Json(await agency.Client.PostAsync("/api/property/verification", Documents(), Ct));
        var admin = await kit.AdminAsync();

        (await admin.PostAsJsonAsync($"/api/admin/property/verifications/{request.GetProperty("id").GetGuid()}/decision", new { approve = true }, Ct))
            .EnsureSuccessStatusCode();

        Assert.True((await Json(await agency.Client.GetAsync("/api/property/profile/agency", Ct))).GetProperty("verified").GetBoolean());
        var agent = await Json(await agency.Client.GetAsync("/api/property/profile/agent", Ct));
        Assert.True(agent.GetProperty("verified").GetBoolean());
        Assert.Equal("LASRERA-1234", agent.GetProperty("licenceNo").GetString());
        Assert.Equal("Active", await StatusAsync(waiting));
        Assert.Equal("Approved", (await Json(await agency.Client.GetAsync("/api/property/verification", Ct))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dismissing_reports_restores_a_hidden_listing()
    {
        var agency = await kit.AgencyAsync("Restored Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        for (var i = 0; i < 3; i++) await ReportAsync(await kit.SeekerAsync($"Rival{i}"), id, "AlreadyTaken");
        Assert.Equal("Hidden", await StatusAsync(id));
        var admin = await kit.AdminAsync();

        var reports = await Json(await admin.GetAsync("/api/admin/property/reports", Ct));
        var mine = reports.EnumerateArray().Where(r => r.GetProperty("listingId").GetGuid() == id).ToList();
        Assert.Equal(3, mine.Count);
        foreach (var r in mine)
            (await admin.PostAsJsonAsync($"/api/admin/property/reports/{r.GetProperty("id").GetGuid()}/decision", new { uphold = false }, Ct)).EnsureSuccessStatusCode();

        Assert.Equal("Active", await StatusAsync(id));
    }

    [Fact]
    public async Task Admin_adds_an_area_that_appears_at_once()
    {
        var admin = await kit.AdminAsync();
        var lagos = await kit.LocationAsync("lagos");
        var name = $"Test Estate {Guid.NewGuid().ToString("N")[..6]}";

        var created = await admin.PostAsJsonAsync("/api/admin/property/locations", new { name, kind = "Area", parentId = lagos, lat = 6.45, lng = 3.5 }, Ct);
        created.EnsureSuccessStatusCode();

        var areas = await Json(await api.CreateClient().GetAsync("/api/locations?parent=lagos", Ct));
        Assert.Contains(areas.EnumerateArray(), a => a.GetProperty("name").GetString() == name);
    }
}
