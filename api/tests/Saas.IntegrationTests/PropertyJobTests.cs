using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Property;
using Saas.Domain.Core;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyJobTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private async Task RunAsync(Func<PropertyJobs, Task> job)
    {
        using var scope = api.Services.CreateScope();
        await job(scope.ServiceProvider.GetRequiredService<PropertyJobs>());
    }

    private Task<List<Notification>> NotificationsAsync(string template) =>
        api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == template).ToListAsync(Ct));

    private Task SetAsync(Guid listingId, DateTimeOffset expiresAt) =>
        api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, expiresAt), Ct));

    [Fact]
    public async Task Listing_is_warned_once_then_expires()
    {
        var agency = await kit.AgencyAsync("Expiring Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Expiring flat"));
        await SetAsync(id, DateTimeOffset.UtcNow.AddDays(3));

        await RunAsync(j => j.WarnExpiringAsync(DateTimeOffset.UtcNow, Ct));
        await RunAsync(j => j.WarnExpiringAsync(DateTimeOffset.UtcNow, Ct));
        var warnings = (await NotificationsAsync("listing.expiring")).Where(n => n.Payload["listing"] == "Expiring flat").ToList();
        Assert.Single(warnings);
        Assert.Equal(agency.Email, warnings[0].Recipient);

        await SetAsync(id, DateTimeOffset.UtcNow.AddMinutes(-1));
        await RunAsync(j => j.ExpireAsync(DateTimeOffset.UtcNow, Ct));
        Assert.Equal(ListingStatus.Expired, await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).Select(l => l.Status).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Renew_link_restores_an_expired_listing()
    {
        var agency = await kit.AgencyAsync("Renewing Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Renewable flat"));
        await SetAsync(id, DateTimeOffset.UtcNow.AddDays(2));
        await RunAsync(j => j.WarnExpiringAsync(DateTimeOffset.UtcNow, Ct));
        var link = (await NotificationsAsync("listing.expiring")).Single(n => n.Payload["listing"] == "Renewable flat").Payload["renewLink"];
        var token = link[(link.LastIndexOf('/') + 1)..];
        await SetAsync(id, DateTimeOffset.UtcNow.AddMinutes(-1));
        await RunAsync(j => j.ExpireAsync(DateTimeOffset.UtcNow, Ct));

        var anon = api.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsync($"/api/listings/renew/{token}x", null, Ct)).StatusCode);
        var renewed = await Json(await anon.PostAsync($"/api/listings/renew/{token}", null, Ct));

        Assert.Equal("Active", renewed.GetProperty("status").GetString());
        Assert.True(renewed.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task Daily_digest_sends_only_new_matches()
    {
        var band = Random.Shared.NextInt64(100_000, 900_000) * 10_000_00;
        var seeker = await kit.SeekerAsync("Digest Reader");
        var email = (await Json(await seeker.GetAsync("/api/me", Ct))).GetProperty("user").GetProperty("email").GetString();
        var agency = await kit.AgencyAsync("Digest Agency", verified: true);
        await api.MakeProAsync(agency.TenantId, "property");
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Older flat", price: band + 1));
        (await seeker.PostAsJsonAsync("/api/me/saved-searches", new
        {
            name = "Daily Lekki", frequency = "Daily", filter = new { location = "lekki-lagos", minPrice = band, maxPrice = band + 9_999_00 },
        }, Ct)).EnsureSuccessStatusCode();
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Newer flat", price: band + 2));
        await api.WithDbAsync(db => db.SavedSearches.Where(s => s.Name == "Daily Lekki")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSentAt, x => x.LastSentAt!.Value.AddHours(-30)), Ct));
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Title == "Older flat" && l.PriceMinor == band + 1)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.FirstPublishedAt, DateTimeOffset.UtcNow.AddDays(-3)), Ct));

        await RunAsync(j => j.SendDailyDigestsAsync(DateTimeOffset.UtcNow, Ct));
        await RunAsync(j => j.SendDailyDigestsAsync(DateTimeOffset.UtcNow, Ct));

        var digests = (await NotificationsAsync("search.digest")).Where(n => n.Recipient == email).ToList();
        Assert.Single(digests);
        Assert.Contains("Newer flat", digests[0].Payload["listings"]);
        Assert.DoesNotContain("Older flat", digests[0].Payload["listings"]);
    }

    [Fact]
    public async Task Viewing_reminder_is_sent_once()
    {
        var agency = await kit.AgencyAsync("Reminder Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Reminder flat"));
        var slots = Enum.GetValues<DayOfWeek>().Select(d => new { weekday = d.ToString(), start = "00:00", end = "23:00", slotMinutes = 60 });
        (await agency.Client.PutAsJsonAsync("/api/property/inspection-slots", new { slots }, Ct)).EnsureSuccessStatusCode();
        var at = (await Json(await api.CreateClient().GetAsync($"/api/listings/{id}/inspection-times", Ct)))[0].GetDateTimeOffset();
        var seeker = await kit.SeekerAsync("Forgetful");
        var booked = await Json(await seeker.PostAsJsonAsync($"/api/listings/{id}/inspections", new { at }, Ct));
        (await agency.Client.PostAsJsonAsync($"/api/property/inspections/{booked.GetProperty("id").GetGuid()}/status", new { status = "Confirmed" }, Ct))
            .EnsureSuccessStatusCode();

        await RunAsync(j => j.RemindInspectionsAsync(DateTimeOffset.UtcNow, Ct));
        await RunAsync(j => j.RemindInspectionsAsync(DateTimeOffset.UtcNow, Ct));

        Assert.Single((await NotificationsAsync("inspection.reminder")).Where(n => n.Payload["listing"] == "Reminder flat"));
    }
}
