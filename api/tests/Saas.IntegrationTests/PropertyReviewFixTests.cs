using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Property;
using Saas.Domain.Core;
using Saas.Domain.Property;
using Saas.Infrastructure.Property;
using SkiaSharp;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

/// <summary>Regression tests for the findings of the final review of the property product.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PropertyReviewFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Lagos).DateTime);
    private readonly PropertyKit kit = new(api);

    private async Task<(Agency Agency, Guid ListingId)> ShortLetAsync(string name)
    {
        var agency = await kit.AgencyAsync(name, verified: true);
        return (agency, await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"{name} flat", purpose: ListingPurpose.ShortLet, price: 5_000_000)));
    }

    private static object Dates(Guid listingId, DateOnly checkIn, int nights = 2) =>
        new { listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(nights).ToString("yyyy-MM-dd"), guests = 2 };

    private async Task<(Guid StayId, string Reference, long Total)> BookAsync(HttpClient guest, Guid listingId, DateOnly checkIn)
    {
        var booking = await Json(await guest.PostAsJsonAsync("/api/stays", Dates(listingId, checkIn), Ct));
        var reference = api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        return (booking.GetProperty("stayId").GetGuid(), reference, booking.GetProperty("totalMinor").GetInt64());
    }

    private async Task PayAsync(HttpClient guest, (Guid StayId, string Reference, long Total) stay)
    {
        api.Gateway.Charges[stay.Reference] = new VerifiedCharge(stay.Reference, stay.Total, "NGN", DateTimeOffset.UtcNow);
        (await guest.PostAsJsonAsync($"/api/stays/{stay.StayId}/verify", new { reference = stay.Reference }, Ct)).EnsureSuccessStatusCode();
    }

    // C1
    [Fact]
    public async Task Listing_with_upcoming_stays_cannot_be_deleted_and_trips_survive_a_missing_listing()
    {
        var (agency, id) = await ShortLetAsync("Keeper Homes");
        var guest = await kit.SeekerAsync("Booked Guest");
        await PayAsync(guest, await BookAsync(guest, id, Today.AddDays(9)));

        Assert.Equal(HttpStatusCode.Conflict, (await agency.Client.DeleteAsync($"/api/property/listings/{id}", Ct)).StatusCode);

        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).ExecuteDeleteAsync(Ct)); // e.g. tenant removal
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/api/me/stays", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agency.Client.GetAsync("/api/property/stays", Ct)).StatusCode);
    }

    // C2
    [Fact]
    public async Task Reminders_skip_viewings_whose_listing_is_gone()
    {
        var agency = await kit.AgencyAsync("Vanishing Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var slots = Enum.GetValues<DayOfWeek>().Select(d => new { weekday = d.ToString(), start = "00:00", end = "23:00", slotMinutes = 60 });
        (await agency.Client.PutAsJsonAsync("/api/property/inspection-slots", new { slots }, Ct)).EnsureSuccessStatusCode();
        var at = (await Json(await api.CreateClient().GetAsync($"/api/listings/{id}/inspection-times", Ct)))[0].GetDateTimeOffset();
        var booked = await Json(await (await kit.SeekerAsync("Orphan Viewer")).PostAsJsonAsync($"/api/listings/{id}/inspections", new { at }, Ct));
        (await agency.Client.PostAsJsonAsync($"/api/property/inspections/{booked.GetProperty("id").GetGuid()}/status", new { status = "Confirmed" }, Ct)).EnsureSuccessStatusCode();
        await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).ExecuteDeleteAsync(Ct));

        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PropertyJobs>().RemindInspectionsAsync(DateTimeOffset.UtcNow, Ct);
    }

    // I1
    [Fact]
    public async Task Payment_for_a_stay_cancelled_while_pending_is_refunded()
    {
        var (_, id) = await ShortLetAsync("Lagging Homes");
        var guest = await kit.SeekerAsync("Impatient");
        var stay = await BookAsync(guest, id, Today.AddDays(11));
        (await guest.PostAsync($"/api/stays/{stay.StayId}/cancel", null, Ct)).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<StayService>().ConfirmAsync(stay.Reference, stay.Total, "NGN", Ct); // webhook arrives late

        Assert.Contains(api.Gateway.Refunds, r => r.Reference == stay.Reference && r.AmountMinor == stay.Total);
        Assert.Equal(stay.Total, await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().Where(s => s.Id == stay.StayId).Select(s => s.RefundMinor).SingleAsync(Ct)));
    }

    // I2
    [Fact]
    public async Task Renewing_a_live_listing_while_over_the_plan_limit_is_refused()
    {
        var agency = await kit.AgencyAsync("Downgraded Agency", verified: true);
        await api.MakeProAsync(agency.TenantId, "property");
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++) ids.Add(await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"Live {i}")));
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == agency.TenantId).ExecuteDeleteAsync(Ct)); // back to Free (3)

        Assert.Equal(HttpStatusCode.PaymentRequired, (await agency.Client.PostAsync($"/api/property/listings/{ids[0]}/renew", null, Ct)).StatusCode);
    }

    // I3
    [Fact]
    public async Task Dismissing_a_report_does_not_undo_an_upheld_one()
    {
        var agency = await kit.AgencyAsync("Scammy Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        foreach (var name in new[] { "Victim1", "Victim2" })
            (await (await kit.SeekerAsync(name)).PostAsJsonAsync($"/api/listings/{id}/report", new { reason = "Scam" }, Ct)).EnsureSuccessStatusCode();
        var admin = await kit.AdminAsync();
        var reports = (await Json(await admin.GetAsync("/api/admin/property/reports", Ct))).EnumerateArray().Where(r => r.GetProperty("listingId").GetGuid() == id).ToList();

        await admin.PostAsJsonAsync($"/api/admin/property/reports/{reports[0].GetProperty("id").GetGuid()}/decision", new { uphold = true }, Ct);
        await admin.PostAsJsonAsync($"/api/admin/property/reports/{reports[1].GetProperty("id").GetGuid()}/decision", new { uphold = false }, Ct);

        Assert.Equal(ListingStatus.Hidden, await api.WithDbAsync(db => db.Listings.IgnoreQueryFilters().Where(l => l.Id == id).Select(l => l.Status).SingleAsync(Ct)));
    }

    // I4
    [Fact]
    public async Task Agencies_cannot_hide_their_reviews_and_restored_reviews_stay_visible()
    {
        var agency = await kit.AgencyAsync("Touchy Agency", verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync());
        var slug = (await Json(await agency.Client.GetAsync("/api/property/profile/agent", Ct))).GetProperty("slug").GetString();
        var critic = await kit.SeekerAsync("Critic");
        await critic.PostAsJsonAsync($"/api/listings/{id}/enquiries", new { name = "Critic", phone = "+2348077777777", message = "Hi" }, Ct);
        await api.WithDbAsync(db => db.Enquiries.IgnoreQueryFilters().Where(e => e.ListingId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Ct));
        var review = await Json(await critic.PostAsJsonAsync($"/api/agents/{slug}/reviews", new { rating = 1, comment = "Never showed up." }, Ct));
        var reviewId = review.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await agency.Client.PostAsync($"/api/reviews/{reviewId}/report", null, Ct)).StatusCode);

        (await (await kit.SeekerAsync("Sockpuppet")).PostAsync($"/api/reviews/{reviewId}/report", null, Ct)).EnsureSuccessStatusCode();
        (await (await kit.AdminAsync()).PostAsJsonAsync($"/api/admin/property/reviews/{reviewId}/decision", new { restore = true }, Ct)).EnsureSuccessStatusCode();
        (await (await kit.SeekerAsync("Sockpuppet2")).PostAsync($"/api/reviews/{reviewId}/report", null, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(1, (await Json(await api.CreateClient().GetAsync($"/api/agents/{slug}", Ct))).GetProperty("reviews").GetArrayLength());
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient agency, Guid listingId, byte[] bytes) =>
        agency.PostAsync($"/api/property/listings/{listingId}/media", new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "photo.png" } }, Ct);

    private async Task ProcessAllAsync()
    {
        using var scope = api.Services.CreateScope();
        var processor = ActivatorUtilities.CreateInstance<PhotoProcessor>(scope.ServiceProvider);
        while (await processor.ProcessNextAsync(Ct)) { }
    }

    private static byte[] Plain(SKColor color)
    {
        using var bitmap = new SKBitmap(800, 600);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    // I5
    [Fact]
    public async Task Featureless_photos_are_not_treated_as_duplicates()
    {
        var first = await kit.AgencyAsync("Plain Wall One", verified: true);
        var a = await kit.LiveListingAsync(first.Client, await kit.ListingBodyAsync());
        await UploadAsync(first.Client, a, Plain(new SKColor(240, 240, 235)));
        await ProcessAllAsync();
        var second = await kit.AgencyAsync("Plain Wall Two", verified: true);
        var b = await kit.LiveListingAsync(second.Client, await kit.ListingBodyAsync());
        await UploadAsync(second.Client, b, Plain(new SKColor(250, 250, 250)));
        await ProcessAllAsync();

        var listing = await Json(await second.Client.GetAsync($"/api/property/listings/{b}", Ct));
        Assert.Equal("Active", listing.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("media")[0].GetProperty("duplicateOfListingId").ValueKind);
    }

    /// <summary>A real 1x1 PNG whose header claims 30000x30000 pixels (about 3.6 GB decoded).</summary>
    private static byte[] HugeHeaderPng()
    {
        using var tiny = new SKBitmap(1, 1);
        using var image = SKImage.FromBitmap(tiny);
        var png = image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), 30_000);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 30_000);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29, 4), Crc32(png.AsSpan(12, 17)));
        return png;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    // I6
    [Fact]
    public async Task Photos_with_absurd_dimensions_fail_without_decoding()
    {
        var agency = await kit.AgencyAsync("Bomb Agency");
        var id = (await kit.CreateListingAsync(agency.Client, await kit.ListingBodyAsync())).GetProperty("id").GetGuid();
        (await UploadAsync(agency.Client, id, HugeHeaderPng())).EnsureSuccessStatusCode();
        await ProcessAllAsync();

        var media = (await Json(await agency.Client.GetAsync($"/api/property/listings/{id}", Ct))).GetProperty("media")[0];
        Assert.Equal("Failed", media.GetProperty("status").GetString());
        Assert.Contains("too large", media.GetProperty("error").GetString());
    }

    // I9
    [Fact]
    public async Task Host_cannot_cancel_a_stay_that_has_started()
    {
        var (agency, id) = await ShortLetAsync("Midstay Homes");
        var guest = await kit.SeekerAsync("Settled");
        var stay = await BookAsync(guest, id, Today);
        await PayAsync(guest, stay);

        Assert.Equal(HttpStatusCode.Conflict, (await agency.Client.PostAsync($"/api/property/stays/{stay.StayId}/cancel", null, Ct)).StatusCode);
    }
}
