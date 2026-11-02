using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Property;
using Saas.Domain.Core;
using Saas.Domain.Property;
using Saas.Infrastructure.Property;

namespace Saas.IntegrationTests;

/// <summary>Property items moved forward from "next release".</summary>
[Collection(nameof(ApiCollection))]
public sealed class NextReleasePropertyTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Lagos).DateTime);
    private readonly PropertyKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private async Task<(PropertyKit.Agency Agency, Guid ListingId)> ShortLetAsync(string name)
    {
        var agency = await kit.AgencyAsync(name, verified: true);
        return (agency, await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"{name} apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000)));
    }

    private async Task<(JsonElement Booking, string Reference)> BookAsync(HttpClient guest, Guid listingId, DateOnly checkIn)
    {
        var booking = await Json(await guest.PostAsJsonAsync("/api/stays", new
        {
            listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666",
        }, Ct));
        return (booking, api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference);
    }

    private Task<bool> EmailedAsync(string recipient, string template) =>
        api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.Recipient == recipient && n.TemplateKey == template, Ct));

    [Fact]
    public async Task Map_area_with_non_finite_numbers_is_a_400()
    {
        foreach (var bbox in new[] { "NaN,6,4,7", "3,6,Infinity,7", "-Infinity,6,4,7" })
            Assert.Equal(HttpStatusCode.BadRequest, (await api.CreateClient().GetAsync($"/api/listings?bbox={bbox}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_amount_stay_payment_alerts_finance_and_emails_the_guest()
    {
        var (finance, financeUser, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var financeEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == financeUser).Select(u => u.Email!).SingleAsync(Ct));
        var (_, listingId) = await ShortLetAsync("Short Paid Homes");
        var guestEmail = $"underpaid-{Guid.NewGuid():N}@example.com";
        var guest = await kit.SeekerAsync("Underpaid", guestEmail);
        var (booking, reference) = await BookAsync(guest, listingId, Today.AddDays(30));
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 100_000, "NGN", DateTimeOffset.UtcNow);
        await guest.PostAsJsonAsync($"/api/stays/{booking.GetProperty("stayId").GetGuid()}/verify", new { reference }, Ct);

        Assert.True(await EmailedAsync(guestEmail, "stay.payment_mismatch"));
        Assert.True(await EmailedAsync(financeEmail, "payments.mismatch"));
    }

    [Fact]
    public async Task A_payment_refunded_because_the_stay_was_cancelled_is_emailed_to_the_guest()
    {
        var (_, listingId) = await ShortLetAsync("Late Pay Homes");
        var guestEmail = $"latepay-{Guid.NewGuid():N}@example.com";
        var guest = await kit.SeekerAsync("Late payer", guestEmail);
        var (booking, reference) = await BookAsync(guest, listingId, Today.AddDays(40));
        var stayId = booking.GetProperty("stayId").GetGuid();
        (await guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct)).EnsureSuccessStatusCode(); // cancelled while the payment was on its way
        using (var scope = api.Services.CreateScope()) // the payment lands through the Paystack webhook
            await scope.ServiceProvider.GetRequiredService<StayService>().ConfirmAsync(reference, booking.GetProperty("totalMinor").GetInt64(), "NGN", Ct);

        Assert.True(await EmailedAsync(guestEmail, "stay.refunded"));
    }

    [Fact]
    public async Task Anonymous_reports_use_a_keyed_hash_and_a_simultaneous_duplicate_is_a_409()
    {
        var agency = await kit.AgencyAsync("Reported Realty", verified: true);
        var listingId = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Reported flat"));
        var anon = api.CreateClient();

        var results = await Task.WhenAll(
            anon.PostAsJsonAsync($"/api/listings/{listingId}/report", new { reason = "Fake" }, Ct),
            anon.PostAsJsonAsync($"/api/listings/{listingId}/report", new { reason = "Fake" }, Ct));

        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var key = await api.WithDbAsync(db => db.ListingReports.IgnoreQueryFilters().Where(r => r.ListingId == listingId).Select(r => r.ReporterKey).SingleAsync(Ct));
        foreach (var ip in new[] { "unknown", "127.0.0.1", "::1" })
            Assert.NotEqual("ip:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip)))[..24], key);
    }

    [Fact]
    public async Task Stay_and_viewing_phone_and_notes_have_length_limits()
    {
        var (_, listingId) = await ShortLetAsync("Long Text Homes");
        var guest = await kit.SeekerAsync("Verbose");
        var checkIn = Today.AddDays(50);
        var stay = await guest.PostAsJsonAsync("/api/stays", new
        {
            listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = new string('8', 200),
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, stay.StatusCode);

        var agency = await kit.AgencyAsync("Viewing Realty", verified: true);
        var rental = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Viewing flat"));
        var viewing = await guest.PostAsJsonAsync($"/api/listings/{rental}/inspections",
            new { at = DateTimeOffset.UtcNow.AddDays(3), phone = "+2348066666666", notes = new string('x', 5000) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, viewing.StatusCode);
    }

    [Fact]
    public async Task Two_boosts_confirming_at_once_both_add_their_days()
    {
        var agency = await kit.AgencyAsync("Double Boost Realty", verified: true);
        var listingId = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Double boosted flat"));
        var references = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var purchase = await Json(await agency.Client.PostAsJsonAsync($"/api/property/listings/{listingId}/boosts", new { code = "top-7" }, Ct));
            references.Add(api.Gateway.Initialized.Last(x => purchase.GetProperty("authorizationUrl").GetString()!.EndsWith(x.Reference)).Reference);
        }

        await Task.WhenAll(references.Select(async reference =>
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BoostService>().ConfirmAsync(reference, 500_000, "NGN", Ct);
        }));

        var topUntil = await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId).Select(l => l.TopUntil).SingleAsync(Ct));
        Assert.InRange(topUntil!.Value, DateTimeOffset.UtcNow.AddDays(14).AddHours(-1), DateTimeOffset.UtcNow.AddDays(14).AddHours(1));
    }

    [Fact]
    public async Task Hidden_listings_count_toward_the_plan_and_approval_checks_the_limit()
    {
        var agency = await kit.AgencyAsync("Hidden Count Realty", verified: true); // Free: 3 listings
        var live = new List<Guid>();
        for (var i = 0; i < 3; i++) live.Add(await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"Counted flat {i}")));
        var draft = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Fourth flat"))).GetProperty("id").GetGuid();
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == live[2]).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ListingStatus.Hidden), Ct));

        Assert.Equal(HttpStatusCode.PaymentRequired, (await kit.SubmitAsync(agency.Client, draft)).StatusCode);

        // A hidden listing that would push the agency over its plan can't simply be approved back to live.
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == draft).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ListingStatus.Hidden), Ct));
        var moderator = await kit.AdminAsync();
        var decision = await moderator.PostAsJsonAsync($"/api/admin/property/listings/{draft}/decision", new { approve = true }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, decision.StatusCode);
    }

    [Fact]
    public void The_reset_email_says_the_link_lasts_ten_minutes()
    {
        var email = Saas.Infrastructure.Notifications.EmailTemplates.Render(
            new Notification { Recipient = "a@example.com", TemplateKey = "password.reset", Payload = new() { ["name"] = "Ada", ["link"] = "https://x" } }, "PropertyLink");
        Assert.Contains("10 minutes", email.Text);
        Assert.DoesNotContain("hour", email.Text);
    }

    [Fact]
    public async Task Plan_usage_counts_hidden_listings()
    {
        var agency = await kit.AgencyAsync("Usage Hidden Realty", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Usage hidden flat"));
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ListingStatus.Hidden), Ct));

        var usage = (await Json(await agency.Client.GetAsync("/api/billing", Ct))).GetProperty("usage").GetProperty("items").EnumerateArray().Single(u => u.GetProperty("key").GetString() == "listings");
        Assert.Equal(1, usage.GetProperty("used").GetInt32());
    }

    [Fact]
    public async Task The_renew_link_can_be_previewed_without_renewing()
    {
        var agency = await kit.AgencyAsync("Preview Renew Realty", verified: true);
        var listingId = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Preview renew flat"));
        var before = await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId).Select(l => l.ExpiresAt).SingleAsync(Ct));
        var token = api.Services.GetRequiredService<IListingTokens>().Renew(listingId);

        var preview = await Json(await api.CreateClient().GetAsync($"/api/listings/renew/{Uri.EscapeDataString(token)}", Ct));
        Assert.Equal("Preview renew flat", preview.GetProperty("title").GetString());
        Assert.Equal(before, await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId).Select(l => l.ExpiresAt).SingleAsync(Ct)));
        Assert.Equal(HttpStatusCode.BadRequest, (await api.CreateClient().GetAsync("/api/listings/renew/not-a-token", Ct)).StatusCode);
    }

    [Fact]
    public async Task Map_pins_carry_the_listing_link()
    {
        var agency = await kit.AgencyAsync("Pinned Realty", verified: true);
        await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Pinned flat"));
        var result = await Json(await api.CreateClient().GetAsync("/api/listings?pageSize=48", Ct));
        var pin = result.GetProperty("pins")[0];
        Assert.False(string.IsNullOrEmpty(pin.GetProperty("slug").GetString()));
        Assert.False(string.IsNullOrEmpty(pin.GetProperty("agencySlug").GetString()));
    }

    [Fact]
    public async Task Moderation_items_show_every_photo_and_the_public_link()
    {
        var agency = await kit.AgencyAsync("Unverified Photos Realty"); // unverified: goes to the queue
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Queued photo flat"))).GetProperty("id").GetGuid();
        for (var i = 0; i < 2; i++)
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(PropertyMediaTests.Photo(20 + i)), "file", "photo.png" } };
            (await agency.Client.PostAsync($"/api/property/listings/{listingId}/media", form, Ct)).EnsureSuccessStatusCode();
        }
        using (var scope = api.Services.CreateScope())
        {
            var processor = ActivatorUtilities.CreateInstance<PhotoProcessor>(scope.ServiceProvider);
            while (await processor.ProcessNextAsync(Ct)) { }
        }
        (await kit.SubmitAsync(agency.Client, listingId)).EnsureSuccessStatusCode();

        var item = (await Json(await (await kit.AdminAsync()).GetAsync("/api/admin/property/moderation", Ct))).EnumerateArray()
            .Single(m => m.GetProperty("listingId").GetGuid() == listingId);
        Assert.Equal(2, item.GetProperty("photos").GetArrayLength());
        Assert.Contains("/listings/", item.GetProperty("publicUrl").GetString());
    }

    private sealed class FailingDeleteStorage(IFileStorage inner) : IFileStorage
    {
        public Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => inner.SaveAsync(key, content, contentType, ct);
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new IOException("Storage is unavailable.");
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => inner.OpenReadAsync(key, ct);
        public string? TemporaryUrl(string key, TimeSpan lifetime) => inner.TemporaryUrl(key, lifetime);
    }

    [Fact]
    public async Task A_photo_is_ready_only_once_its_original_is_deleted()
    {
        var agency = await kit.AgencyAsync("Sticky Original Realty");
        var listingId = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Sticky original flat"))).GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent { { new ByteArrayContent(PropertyMediaTests.Photo(31)), "file", "photo.png" } };
        var mediaId = (await Json(await agency.Client.PostAsync($"/api/property/listings/{listingId}/media", form, Ct))).GetProperty("id").GetGuid();

        await using (var failing = api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            var existing = s.Last(d => d.ServiceType == typeof(IFileStorage));
            s.Add(new ServiceDescriptor(typeof(IFileStorage), sp => new FailingDeleteStorage((IFileStorage)(existing.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, existing.ImplementationType!))), existing.Lifetime));
        })))
        {
            using var scope = failing.Services.CreateScope();
            var processor = ActivatorUtilities.CreateInstance<PhotoProcessor>(scope.ServiceProvider);
            await processor.ProcessNextAsync(Ct);
        }
        var stuck = await api.WithDbAsync(db => db.ListingMedia.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == mediaId, Ct));
        Assert.Equal(MediaStatus.Processing, stuck.Status); // not Ready (original still there), not Failed (the photo is fine)

        await api.WithDbAsync(db => db.ListingMedia.IgnoreQueryFilters().Where(m => m.Id == mediaId).ExecuteUpdateAsync(s => s.SetProperty(m => m.ClaimedAt, (DateTimeOffset?)null), Ct));
        // The app's own background processor may pick it up first, so wait for the outcome rather than assume who ran it.
        ListingMedia media;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        do
        {
            using (var scope = api.Services.CreateScope())
            {
                var processor = ActivatorUtilities.CreateInstance<PhotoProcessor>(scope.ServiceProvider);
                while (await processor.ProcessNextAsync(Ct)) { }
            }
            media = await api.WithDbAsync(db => db.ListingMedia.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == mediaId, Ct));
            if (media.Status != MediaStatus.Processing) break;
            await Task.Delay(250, Ct);
        } while (DateTimeOffset.UtcNow < deadline);
        Assert.Equal(MediaStatus.Ready, media.Status);
        using var check = api.Services.CreateScope();
        Assert.Null(await check.ServiceProvider.GetRequiredService<IFileStorage>().OpenReadAsync(media.OriginalKey, Ct));
    }
}
