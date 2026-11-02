using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Property;
using Saas.Domain.Payouts;
using Saas.Domain.Property;
using static Saas.IntegrationTests.PropertyKit;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PropertyStayTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private readonly PropertyKit kit = new(api);

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Lagos).DateTime);

    /// <summary>A live short-let: ₦50,000 a night, ₦10,000 cleaning, 1-4 guests.</summary>
    private async Task<(Agency Agency, Guid ListingId)> ShortLetAsync(string name)
    {
        var agency = await kit.AgencyAsync(name, verified: true);
        var id = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"{name} apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        return (agency, id);
    }

    private static object Dates(Guid listingId, DateOnly checkIn, int nights = 3, int guests = 2) =>
        new { listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(nights).ToString("yyyy-MM-dd"), guests, phone = "+2348066666666" };

    private async Task<JsonElement> BookAsync(HttpClient guest, Guid listingId, DateOnly checkIn, int nights = 3)
    {
        var response = await guest.PostAsJsonAsync("/api/stays", Dates(listingId, checkIn, nights), Ct);
        response.EnsureSuccessStatusCode();
        return await Json(response);
    }

    private async Task<JsonElement> PayAsync(HttpClient guest, JsonElement booking, long? amount = null)
    {
        var reference = api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, amount ?? booking.GetProperty("totalMinor").GetInt64(), "NGN", DateTimeOffset.UtcNow);
        var verified = await guest.PostAsJsonAsync($"/api/stays/{booking.GetProperty("stayId").GetGuid()}/verify", new { reference }, Ct);
        verified.EnsureSuccessStatusCode();
        return await Json(verified);
    }

    private Task<List<Earning>> EarningsAsync(Guid stayId) =>
        api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.Source == EarningSource.Stay && e.SourceId == stayId).ToListAsync(Ct));

    [Fact]
    public async Task Quote_and_availability()
    {
        var (_, id) = await ShortLetAsync("Quote Homes");
        var quote = await Json(await api.CreateClient().PostAsJsonAsync($"/api/listings/{id}/stay-quote", Dates(id, Today.AddDays(5)), Ct));
        Assert.Equal((3, 15_000_000L, 1_000_000L, 16_000_000L), (quote.GetProperty("nights").GetInt32(), quote.GetProperty("subtotalMinor").GetInt64(),
            quote.GetProperty("cleaningMinor").GetInt64(), quote.GetProperty("totalMinor").GetInt64()));

        await BookAsync(await kit.SeekerAsync("Holder"), id, Today.AddDays(5));
        var availability = await Json(await api.CreateClient().GetAsync($"/api/listings/{id}/availability", Ct));
        Assert.Equal(Today.AddDays(5).ToString("yyyy-MM-dd"), availability.GetProperty("booked")[0].GetProperty("start").GetString());
    }

    [Fact]
    public async Task Six_guests_racing_for_the_same_dates_get_one_stay()
    {
        var (_, id) = await ShortLetAsync("Race Homes");
        var guests = new List<HttpClient>();
        for (var i = 0; i < 6; i++) guests.Add(await kit.SeekerAsync($"Racer{i}"));

        var responses = await Task.WhenAll(guests.Select(g => g.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(20)), Ct)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Contains("just booked", await responses.First(r => r.StatusCode == HttpStatusCode.Conflict).Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Invalid_stay_dates_are_refused()
    {
        var (agency, id) = await ShortLetAsync("Strict Homes");
        var guest = await kit.SeekerAsync("Careless");
        async Task<HttpStatusCode> TryAsync(object body) => (await guest.PostAsJsonAsync("/api/stays", body, Ct)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(Dates(id, Today.AddDays(5), nights: 0)));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(Dates(id, Today.AddDays(-1))));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(Dates(id, Today.AddDays(5), guests: 5)));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(Dates(id, Today.AddDays(5), nights: 120)));

        var body = await kit.ListingBodyAsync(title: "Strict Homes apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000);
        body["shortLet"] = new { cleaningFeeMinor = 1_000_000, minNights = 2, maxGuests = 4, checkInTime = "14:00", checkOutTime = "12:00" };
        (await agency.Client.PutAsJsonAsync($"/api/property/listings/{id}", body, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(Dates(id, Today.AddDays(5), nights: 1)));

        (await agency.Client.PostAsJsonAsync($"/api/property/listings/{id}/blocks",
            new { start = Today.AddDays(30).ToString("yyyy-MM-dd"), end = Today.AddDays(33).ToString("yyyy-MM-dd"), note = "Repairs" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, await TryAsync(Dates(id, Today.AddDays(31))));

        var sale = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Not a short-let", purpose: ListingPurpose.Sale));
        Assert.Equal(HttpStatusCode.BadRequest, await TryAsync(Dates(sale, Today.AddDays(5))));
        Assert.Equal(HttpStatusCode.Forbidden, (await agency.Client.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(40)), Ct)).StatusCode);
    }

    [Fact]
    public async Task Paid_stay_confirms_and_records_host_earning()
    {
        var (agency, id) = await ShortLetAsync("Paid Homes");
        var guest = await kit.SeekerAsync("Payer");
        var checkIn = Today.AddDays(10);
        var booking = await BookAsync(guest, id, checkIn);
        Assert.Equal(16_000_000, booking.GetProperty("totalMinor").GetInt64());

        var stay = await PayAsync(guest, booking);

        Assert.Equal("Confirmed", stay.GetProperty("status").GetString());
        var earning = Assert.Single(await EarningsAsync(booking.GetProperty("stayId").GetGuid()));
        Assert.Equal((10, 1_600_000L, 14_400_000L), (earning.FeePercent, earning.FeeMinor, earning.NetMinor));
        var midnight = checkIn.ToDateTime(TimeOnly.MinValue);
        Assert.Equal(new DateTimeOffset(midnight, Lagos.GetUtcOffset(midnight)).AddHours(24), earning.AvailableAt);
        var studio = await Json(await agency.Client.GetAsync("/api/property/stays", Ct));
        Assert.Equal("Payer Seeker", studio[0].GetProperty("guestName").GetString());
        Assert.Equal(14_400_000, (await Json(await agency.Client.GetAsync("/api/property/earnings", Ct))).GetProperty("pendingMinor").GetInt64());
    }

    [Fact]
    public async Task Confirming_twice_or_with_wrong_amount_is_safe()
    {
        var (_, id) = await ShortLetAsync("Careful Homes");
        var guest = await kit.SeekerAsync("Twice");
        var booking = await BookAsync(guest, id, Today.AddDays(12));
        var stayId = booking.GetProperty("stayId").GetGuid();

        var underpaid = await PayAsync(guest, booking, amount: 100);
        Assert.Equal("Pending", underpaid.GetProperty("status").GetString());
        Assert.Empty(await EarningsAsync(stayId));

        await PayAsync(guest, booking);
        await PayAsync(guest, booking);
        using (var scope = api.Services.CreateScope())
        {
            var reference = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().Where(s => s.Id == stayId).Select(s => s.Reference).SingleAsync(Ct));
            await scope.ServiceProvider.GetRequiredService<StayService>().ConfirmAsync(reference, 16_000_000, "NGN", Ct); // the webhook, late
        }
        Assert.Single(await EarningsAsync(stayId));
    }

    [Fact]
    public async Task Guest_cancel_inside_48_hours_keeps_one_night()
    {
        var (_, id) = await ShortLetAsync("Late Homes");
        var guest = await kit.SeekerAsync("Late");
        var booking = await BookAsync(guest, id, Today.AddDays(1));
        await PayAsync(guest, booking);
        var stayId = booking.GetProperty("stayId").GetGuid();

        (await guest.PostAsync($"/api/stays/{stayId}/cancel", null, Ct)).EnsureSuccessStatusCode();

        var reference = await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().Where(s => s.Id == stayId).Select(s => s.Reference).SingleAsync(Ct));
        Assert.Contains(api.Gateway.Refunds, r => r.Reference == reference && r.AmountMinor == 11_000_000);
        var earning = Assert.Single(await EarningsAsync(stayId));
        Assert.False(earning.Reversed);
        Assert.Equal((5_000_000L, 500_000L, 4_500_000L), (earning.GrossMinor, earning.FeeMinor, earning.NetMinor));
        Assert.Equal("Cancelled", (await Json(await guest.GetAsync("/api/me/stays", Ct)))[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Host_cancel_refunds_in_full()
    {
        var (agency, id) = await ShortLetAsync("Flaky Homes");
        var guest = await kit.SeekerAsync("Stranded");
        var booking = await BookAsync(guest, id, Today.AddDays(14));
        await PayAsync(guest, booking);
        var stayId = booking.GetProperty("stayId").GetGuid();

        (await agency.Client.PostAsync($"/api/property/stays/{stayId}/cancel", null, Ct)).EnsureSuccessStatusCode();

        Assert.Contains(api.Gateway.Refunds, r => r.AmountMinor == 16_000_000);
        Assert.True(Assert.Single(await EarningsAsync(stayId)).Reversed);
        Assert.Equal(1, await api.WithDbAsync(db => db.AgencyProfiles.IgnoreQueryFilters().Where(a => a.TenantId == agency.TenantId)
            .Select(a => a.HostCancellations).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Unpaid_hold_expires_and_frees_the_dates()
    {
        var (_, id) = await ShortLetAsync("Hold Homes");
        var first = await BookAsync(await kit.SeekerAsync("Slow"), id, Today.AddDays(25));
        var second = await kit.SeekerAsync("Quick");
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(25)), Ct)).StatusCode);

        await api.WithDbAsync(db => db.Stays.IgnoreQueryFilters().Where(s => s.Id == first.GetProperty("stayId").GetGuid())
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.HoldExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PropertyJobs>().ExpireStayHoldsAsync(DateTimeOffset.UtcNow, Ct);

        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/api/stays", Dates(id, Today.AddDays(25)), Ct)).StatusCode);
    }

    [Fact]
    public async Task Host_payout_appears_in_shared_admin_queue_as_property()
    {
        var (agency, id) = await ShortLetAsync("Payout Homes");
        var guest = await kit.SeekerAsync("Payout Guest");
        var booking = await BookAsync(guest, id, Today.AddDays(3));
        await PayAsync(guest, booking);
        await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.SourceId == booking.GetProperty("stayId").GetGuid())
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AvailableAt, DateTimeOffset.UtcNow.AddDays(-1)), Ct));

        Assert.Equal(HttpStatusCode.Conflict, (await agency.Client.PostAsJsonAsync("/api/property/payouts", new { amountMinor = 1_000_000 }, Ct)).StatusCode);
        (await agency.Client.PutAsJsonAsync("/api/property/payout-account", new { bankName = "Access", accountNumber = "0987654321", accountName = "Payout Homes" }, Ct))
            .EnsureSuccessStatusCode();
        var payout = await Json(await agency.Client.PostAsJsonAsync("/api/property/payouts", new { amountMinor = 10_000_000 }, Ct));

        var queue = await Json(await (await kit.AdminAsync()).GetAsync("/api/admin/payouts?status=Requested", Ct));
        var row = queue.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == payout.GetProperty("id").GetGuid());
        Assert.Equal(("property", "Payout Homes"), (row.GetProperty("product").GetString(), row.GetProperty("businessName").GetString()));
    }
}
