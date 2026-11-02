using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Bookings;
using Saas.Domain.Bookings;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class BookingFlowTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A Monday-to-Saturday date at least 3 days ahead (beyond minimum notice and the 24h reminder).</summary>
    private static DateOnly OpenDay()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(3);
        while (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task Customer_books_online_and_the_slot_disappears()
    {
        var (owner, slug, _) = await SignupAsync("Kemi");
        var serviceId = await CreateServiceAsync(owner, "Haircut", 60);
        var anon = api.CreateClient();
        var page = await Json(await anon.GetAsync($"/api/public/book/{slug}", Ct));
        Assert.Equal("Haircut", page.GetProperty("services")[0].GetProperty("name").GetString());
        Assert.True(page.GetProperty("showBranding").GetBoolean());

        var day = OpenDay();
        var slots = await SlotsAsync(anon, slug, serviceId, day);
        Assert.Equal("09:00", Local(slots[0]));
        Assert.Equal("17:00", Local(slots[^1])); // 9-6 opening hours, 60-minute service
        var days = await Json(await anon.GetAsync($"/api/public/book/{slug}/days?serviceId={serviceId}&month={day:yyyy-MM}", Ct));
        Assert.True(days.EnumerateArray().Single(d => d.GetProperty("date").GetString() == day.ToString("yyyy-MM-dd")).GetProperty("available").GetBoolean());

        var booked = await BookAsync(anon, slug, serviceId, slots[2], email: "tunde@example.com");
        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);
        var result = await Json(booked);
        Assert.Equal("Confirmed", result.GetProperty("booking").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("authorizationUrl").ValueKind);

        var after = await SlotsAsync(anon, slug, serviceId, day);
        Assert.DoesNotContain(slots[2], after);
        Assert.Equal(HttpStatusCode.Conflict, (await BookAsync(anon, slug, serviceId, slots[2])).StatusCode);

        // Confirmation goes to customer and business; reminders are scheduled 24h and 2h before.
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "booking.confirmed" && n.Recipient == "tunde@example.com", Ct)));
        var token = result.GetProperty("manageUrl").GetString()!.Split("/b/")[1];
        var ics = await anon.GetStringAsync($"/api/public/bookings/{token}/ics", Ct);
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("SUMMARY:Haircut at Kemi Salon", ics);
    }

    [Fact]
    public async Task Two_customers_racing_for_one_slot_get_exactly_one_booking()
    {
        var (owner, slug, _) = await SignupAsync("Race");
        var serviceId = await CreateServiceAsync(owner, "Braids", 120);
        var slot = (await SlotsAsync(api.CreateClient(), slug, serviceId, OpenDay()))[0];

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => BookAsync(api.CreateClient(), slug, serviceId, slot, phone: $"0803000000{i}")));

        Assert.Single(attempts, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(attempts.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Anyone_available_spreads_bookings_across_staff()
    {
        var (owner, slug, tenantId) = await SignupAsync("Duo");
        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.PostAsJsonAsync("/api/staff", Staff("Ada"), Ct)).StatusCode); // Free: 1 staff
        await api.MakeProAsync(tenantId, Products.Bookings, "team");
        var ada = await Json(await owner.PostAsJsonAsync("/api/staff", Staff("Ada"), Ct));
        var serviceId = await CreateServiceAsync(owner, "Nails", 60, extraStaff: ada.GetProperty("id").GetGuid());
        var anon = api.CreateClient();
        var slot = (await SlotsAsync(anon, slug, serviceId, OpenDay()))[0];

        var first = await Json(await BookAsync(anon, slug, serviceId, slot, phone: "08031111111"));
        var second = await Json(await BookAsync(anon, slug, serviceId, slot, phone: "08032222222"));
        var third = await BookAsync(anon, slug, serviceId, slot, phone: "08033333333");

        Assert.NotEqual(first.GetProperty("booking").GetProperty("staff").GetProperty("displayName").GetString(),
            second.GetProperty("booking").GetProperty("staff").GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
    }

    [Fact]
    public async Task Deposit_holds_the_slot_until_paid_or_expired()
    {
        var (owner, slug, tenantId) = await SignupAsync("Deposit");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        (await owner.PutAsJsonAsync("/api/booking-business/paystack", new { secretKey = "sk_test_0123456789abcdef0123" }, Ct)).EnsureSuccessStatusCode();
        var serviceId = await CreateServiceAsync(owner, "Bridal make-up", 90, price: 5_000_000, depositType: "Fixed", depositValue: 1_500_000);
        var anon = api.CreateClient();
        var slots = await SlotsAsync(anon, slug, serviceId, OpenDay());

        var held = await Json(await BookAsync(anon, slug, serviceId, slots[0], email: "bride@example.com"));
        var booking = held.GetProperty("booking");
        Assert.Equal("Pending", booking.GetProperty("status").GetString());
        Assert.Equal(1_500_000, booking.GetProperty("depositMinor").GetInt64());
        var authUrl = held.GetProperty("authorizationUrl").GetString()!;
        var init = api.Merchant.Initialized.Single(i => authUrl.EndsWith(i.Reference));
        Assert.Equal(1_500_000, init.AmountMinor);
        Assert.Equal(HttpStatusCode.Conflict, (await BookAsync(anon, slug, serviceId, slots[0], phone: "08039999999")).StatusCode);

        var token = held.GetProperty("manageUrl").GetString()!.Split("/b/")[1];
        api.Merchant.Charges[init.Reference] = new VerifiedCharge(init.Reference, 1_500_000, "NGN", DateTimeOffset.UtcNow);
        var paid = await Json(await anon.PostAsJsonAsync($"/api/public/bookings/{token}/verify", new { reference = init.Reference }, Ct));
        Assert.Equal("Confirmed", paid.GetProperty("status").GetString());
        Assert.Equal(3_500_000, paid.GetProperty("balanceMinor").GetInt64());
        var reminders = await api.WithDbAsync(db => db.BookingReminders.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId, Ct));
        Assert.Equal(2, reminders);

        // A second hold that is never paid is released by the job and the slot opens again.
        var unpaid = await Json(await BookAsync(anon, slug, serviceId, slots[^1], phone: "08034444444"));
        var unpaidCode = unpaid.GetProperty("booking").GetProperty("code").GetString();
        await api.WithDbAsync(db => db.Bookings.IgnoreQueryFilters().Where(b => b.Code == unpaidCode)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.HoldExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        var (expired, _) = await api.AsTenantAsync<BookingJobs, (int, int)>(tenantId, j => j.RunAsync(Ct));
        Assert.Equal(1, expired);
        Assert.Contains(slots[^1], await SlotsAsync(anon, slug, serviceId, OpenDay()));

        // Refund part of the online deposit from the business side.
        var list = await Json(await owner.GetAsync("/api/bookings?status=Confirmed", Ct));
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        var refunded = await Json(await owner.PostAsJsonAsync($"/api/bookings/{id}/refund", new { amountMinor = 500_000 }, Ct));
        Assert.Equal(1_000_000, refunded.GetProperty("paidMinor").GetInt64());
        Assert.Contains(api.Merchant.Refunds, r => r.Reference == init.Reference && r.AmountMinor == 500_000);
    }

    [Fact]
    public async Task Customer_can_reschedule_and_cancel_before_the_deadline()
    {
        var (owner, slug, _) = await SignupAsync("Manage");
        var serviceId = await CreateServiceAsync(owner, "Massage", 60);
        var anon = api.CreateClient();
        var slots = await SlotsAsync(anon, slug, serviceId, OpenDay());
        var booked = await Json(await BookAsync(anon, slug, serviceId, slots[0]));
        var token = booked.GetProperty("manageUrl").GetString()!.Split("/b/")[1];

        var moved = await Json(await anon.PostAsJsonAsync($"/api/public/bookings/{token}/reschedule", new { startsAt = slots[4] }, Ct));
        Assert.Equal(slots[4], moved.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Contains(slots[0], await SlotsAsync(anon, slug, serviceId, OpenDay())); // old time is free again

        var cancelled = await Json(await anon.PostAsync($"/api/public/bookings/{token}/cancel", null, Ct));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.False(cancelled.GetProperty("canCancel").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync($"/api/public/bookings/{token}/review", new { rating = 9 }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Business_books_walk_ins_tracks_no_shows_and_reports()
    {
        var (owner, slug, _) = await SignupAsync("Walk");
        var serviceId = await CreateServiceAsync(owner, "Shave", 30, price: 300_000);
        var staffId = (await Json(await owner.GetAsync("/api/staff", Ct)))[0].GetProperty("id").GetGuid();
        var day = OpenDay();
        var evening = new DateTimeOffset(day.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromHours(1));

        var outside = await owner.PostAsJsonAsync("/api/bookings", WalkIn(serviceId, staffId, evening, force: false), Ct);
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        var walkIn = await Json(await owner.PostAsJsonAsync("/api/bookings", WalkIn(serviceId, staffId, evening, force: true), Ct));
        Assert.Equal("Confirmed", walkIn.GetProperty("status").GetString());
        Assert.StartsWith("https://wa.me/234", walkIn.GetProperty("whatsappReminderUrl").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/bookings", WalkIn(serviceId, staffId, evening, force: true), Ct)).StatusCode);

        var id = walkIn.GetProperty("id").GetGuid();
        var noShow = await Json(await owner.PostAsJsonAsync($"/api/bookings/{id}/status", new { status = "NoShow" }, Ct));
        Assert.Equal(1, noShow.GetProperty("customer").GetProperty("noShowCount").GetInt32());

        var second = await Json(await owner.PostAsJsonAsync("/api/bookings", WalkIn(serviceId, staffId, evening.AddHours(1), force: true), Ct));
        await owner.PostAsJsonAsync($"/api/bookings/{second.GetProperty("id").GetGuid()}/payments", new { amountMinor = 300_000, type = "Balance", method = "Cash" }, Ct);
        await owner.PostAsJsonAsync($"/api/bookings/{second.GetProperty("id").GetGuid()}/status", new { status = "Completed" }, Ct);

        var report = await Json(await owner.GetAsync($"/api/booking-reports?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(2, report.GetProperty("bookings").GetInt32());
        Assert.Equal(0.5, report.GetProperty("noShowRate").GetDouble());
        var calendar = await Json(await owner.GetAsync($"/api/calendar?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(2, calendar.GetProperty("bookings").GetArrayLength());
        var customers = await Json(await owner.GetAsync("/api/booking-customers?q=Walk", Ct));
        Assert.Equal(1, customers.GetProperty("items")[0].GetProperty("noShowCount").GetInt32());
    }

    [Fact]
    public async Task Reminders_are_sent_once_when_due()
    {
        var (owner, slug, tenantId) = await SignupAsync("Remind");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        var serviceId = await CreateServiceAsync(owner, "Facial", 60);
        var anon = api.CreateClient();
        var slot = (await SlotsAsync(anon, slug, serviceId, OpenDay()))[0];
        await BookAsync(anon, slug, serviceId, slot, email: "remind-me@example.com");
        await api.WithDbAsync(db => db.BookingReminders.IgnoreQueryFilters().Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.SendAt).Take(1).ExecuteUpdateAsync(s => s.SetProperty(r => r.SendAt, DateTimeOffset.UtcNow.AddMinutes(-5)), Ct));

        var (_, first) = await api.AsTenantAsync<BookingJobs, (int, int)>(tenantId, j => j.RunAsync(Ct));
        var (_, second) = await api.AsTenantAsync<BookingJobs, (int, int)>(tenantId, j => j.RunAsync(Ct));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "booking.reminder" && n.Recipient == "remind-me@example.com", Ct)));
    }

    // Helpers

    private async Task<(HttpClient Owner, string Slug, Guid TenantId)> SignupAsync(string name)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Owner", email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "bookings", businessName = $"{name} Salon",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var business = await Json(await client.GetAsync("/api/booking-business", Ct));
        return (client, business.GetProperty("slug").GetString()!, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private async Task<Guid> CreateServiceAsync(HttpClient owner, string name, int minutes, long price = 1_000_000,
        string depositType = "None", long depositValue = 0, Guid? extraStaff = null)
    {
        var staffIds = (await Json(await owner.GetAsync("/api/staff", Ct))).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        if (extraStaff is { } extra && !staffIds.Contains(extra)) staffIds.Add(extra);
        var response = await owner.PostAsJsonAsync("/api/services", new
        {
            name, durationMinutes = minutes, bufferBeforeMinutes = 0, bufferAfterMinutes = 0, priceMinor = price,
            depositType, depositValue, isActive = true, position = 0, staffIds,
        }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static object Staff(string name) => new { displayName = name, isBookable = true, position = 1 };

    private static object WalkIn(Guid serviceId, Guid staffId, DateTimeOffset startsAt, bool force) => new
    {
        serviceId, staffId, startsAt, source = "WalkIn", force,
        customer = new { name = "Walk Customer", phone = "08055555555" },
    };

    private async Task<List<DateTimeOffset>> SlotsAsync(HttpClient client, string slug, Guid serviceId, DateOnly day)
    {
        var availability = await Json(await client.GetAsync($"/api/public/book/{slug}/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}", Ct));
        return availability.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ToList();
    }

    private Task<HttpResponseMessage> BookAsync(HttpClient client, string slug, Guid serviceId, DateTimeOffset startsAt,
        string phone = "08030000000", string? email = null) =>
        client.PostAsJsonAsync($"/api/public/book/{slug}/bookings", new
        {
            serviceId, staffId = (Guid?)null, startsAt, notes = (string?)null,
            customer = new { name = "Tunde", phone, email },
        }, Ct);

    private static string Local(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(1)).ToString("HH:mm");

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
