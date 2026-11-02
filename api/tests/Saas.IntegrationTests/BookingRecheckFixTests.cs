using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Saas.Application.Billing;

namespace Saas.IntegrationTests;

/// <summary>Fixes from the BookLink recheck (bookings-courses.md, B1-B5).</summary>
[Collection(nameof(ApiCollection))]
public sealed class BookingRecheckFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly OpenDay()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(3);
        while (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task B2_Refund_covers_every_online_payment_and_the_dto_says_how_much()
    {
        var (owner, slug, tenantId) = await SignupAsync("Twice");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        await ConnectPaystackAsync(owner);
        var serviceId = await CreateServiceAsync(owner, "Locs", 60, price: 1_000_000, depositType: "Fixed", depositValue: 500_000);
        var anon = api.CreateClient();
        var slot = (await SlotsAsync(anon, slug, serviceId, OpenDay()))[0];

        // The customer opens Paystack from the booking flow, and again from the manage page, and pays both.
        var held = await Json(await BookAsync(anon, slug, serviceId, slot));
        var token = Token(held);
        var first = Reference(held.GetProperty("authorizationUrl").GetString()!);
        var pay = await Json(await anon.PostAsJsonAsync($"/api/public/bookings/{token}/pay", new { email = (string?)null }, Ct));
        var second = Reference(pay.GetProperty("authorizationUrl").GetString()!);
        Assert.NotEqual(first, second);
        foreach (var reference in new[] { first, second })
        {
            api.Merchant.Charges[reference] = new VerifiedCharge(reference, 500_000, "NGN", DateTimeOffset.UtcNow);
            (await anon.PostAsJsonAsync($"/api/public/bookings/{token}/verify", new { reference }, Ct)).EnsureSuccessStatusCode();
        }

        var id = await BookingIdAsync(owner, held);
        var booking = await Json(await owner.GetAsync($"/api/bookings/{id}", Ct));
        Assert.Equal(1_000_000, booking.GetProperty("paidMinor").GetInt64());

        var refund = await owner.PostAsJsonAsync($"/api/bookings/{id}/refund", new { amountMinor = 1_000_000 }, Ct);
        Assert.True(refund.StatusCode == HttpStatusCode.OK, $"refunding both online deposits: {(int)refund.StatusCode} {await refund.Content.ReadAsStringAsync(Ct)}");
        var refunded = await Json(refund);
        Assert.Equal(0, refunded.GetProperty("paidMinor").GetInt64());
        Assert.Equal(0, refunded.GetProperty("refundableMinor").GetInt64());
        Assert.Contains(api.Merchant.Refunds, r => r.Reference == first && r.AmountMinor == 500_000);
        Assert.Contains(api.Merchant.Refunds, r => r.Reference == second && r.AmountMinor == 500_000);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/bookings/{id}/refund", new { amountMinor = 1 }, Ct)).StatusCode);
    }

    [Fact]
    public async Task B2_Refundable_is_what_the_server_will_refund_after_a_partial_refund()
    {
        var (owner, slug, tenantId) = await SignupAsync("Partial");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        await ConnectPaystackAsync(owner);
        var serviceId = await CreateServiceAsync(owner, "Twists", 60, price: 1_000_000, depositType: "Fixed", depositValue: 400_000);
        var anon = api.CreateClient();
        var slot = (await SlotsAsync(anon, slug, serviceId, OpenDay()))[0];
        var held = await Json(await BookAsync(anon, slug, serviceId, slot));
        var reference = Reference(held.GetProperty("authorizationUrl").GetString()!);
        api.Merchant.Charges[reference] = new VerifiedCharge(reference, 400_000, "NGN", DateTimeOffset.UtcNow);
        (await anon.PostAsJsonAsync($"/api/public/bookings/{Token(held)}/verify", new { reference }, Ct)).EnsureSuccessStatusCode();
        var id = await BookingIdAsync(owner, held);
        // A cash balance is not refundable through Paystack.
        (await owner.PostAsJsonAsync($"/api/bookings/{id}/payments", new { amountMinor = 600_000, type = "Balance", method = "Cash" }, Ct)).EnsureSuccessStatusCode();

        var before = await Json(await owner.GetAsync($"/api/bookings/{id}", Ct));
        Assert.Equal(400_000, before.GetProperty("refundableMinor").GetInt64());
        var after = await Json(await owner.PostAsJsonAsync($"/api/bookings/{id}/refund", new { amountMinor = 150_000 }, Ct));
        Assert.Equal(250_000, after.GetProperty("refundableMinor").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/bookings/{id}/refund", new { amountMinor = 250_001 }, Ct)).StatusCode);
    }

    [Fact]
    public async Task B2_Pay_is_refused_once_confirmed_or_once_the_deposit_is_covered()
    {
        var (owner, slug, tenantId) = await SignupAsync("Block");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        await ConnectPaystackAsync(owner);
        var serviceId = await CreateServiceAsync(owner, "Weave", 60, price: 1_000_000, depositType: "Fixed", depositValue: 500_000);
        var anon = api.CreateClient();
        var slots = await SlotsAsync(anon, slug, serviceId, OpenDay());

        // Confirmed by its deposit.
        var paid = await Json(await BookAsync(anon, slug, serviceId, slots[0]));
        var reference = Reference(paid.GetProperty("authorizationUrl").GetString()!);
        api.Merchant.Charges[reference] = new VerifiedCharge(reference, 500_000, "NGN", DateTimeOffset.UtcNow);
        (await anon.PostAsJsonAsync($"/api/public/bookings/{Token(paid)}/verify", new { reference }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync($"/api/public/bookings/{Token(paid)}/pay", new { email = (string?)null }, Ct)).StatusCode);

        // Still Pending, but the business took the whole deposit amount in cash.
        var pending = await Json(await BookAsync(anon, slug, serviceId, slots[^1], phone: "08037777777"));
        var id = await BookingIdAsync(owner, pending);
        (await owner.PostAsJsonAsync($"/api/bookings/{id}/payments", new { amountMinor = 500_000, type = "Balance", method = "Cash" }, Ct)).EnsureSuccessStatusCode();
        var before = api.Merchant.Initialized.Count;
        var pay = await anon.PostAsJsonAsync($"/api/public/bookings/{Token(pending)}/pay", new { email = (string?)null }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, pay.StatusCode);
        Assert.Equal(before, api.Merchant.Initialized.Count);
    }

    [Fact]
    public async Task B3_Pay_charges_the_deposit_due_that_the_manage_page_shows()
    {
        var (owner, slug, tenantId) = await SignupAsync("Due");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        await ConnectPaystackAsync(owner);
        var serviceId = await CreateServiceAsync(owner, "Cornrows", 60, price: 1_200_000, depositType: "Fixed", depositValue: 500_000);
        var anon = api.CreateClient();
        var slot = (await SlotsAsync(anon, slug, serviceId, OpenDay()))[0];
        var held = await Json(await BookAsync(anon, slug, serviceId, slot));
        Assert.Equal(500_000, held.GetProperty("booking").GetProperty("depositDueMinor").GetInt64());
        var id = await BookingIdAsync(owner, held);

        (await owner.PostAsJsonAsync($"/api/bookings/{id}/payments", new { amountMinor = 200_000, type = "Balance", method = "Cash" }, Ct)).EnsureSuccessStatusCode();
        var view = await Json(await anon.GetAsync($"/api/public/bookings/{Token(held)}", Ct));
        Assert.Equal("Pending", view.GetProperty("status").GetString());
        var due = view.GetProperty("depositDueMinor").GetInt64();
        Assert.Equal(300_000, due);

        var pay = await Json(await anon.PostAsJsonAsync($"/api/public/bookings/{Token(held)}/pay", new { email = (string?)null }, Ct));
        var init = api.Merchant.Initialized.Single(i => i.Reference == Reference(pay.GetProperty("authorizationUrl").GetString()!));
        Assert.Equal(due, init.AmountMinor);
    }

    [Fact]
    public async Task B4_Report_breakdowns_come_from_payments_and_sum_to_money_received()
    {
        var (owner, slug, tenantId) = await SignupAsync("Report");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        await ConnectPaystackAsync(owner);
        var serviceId = await CreateServiceAsync(owner, "Bob", 60, price: 1_000_000, depositType: "Fixed", depositValue: 500_000);
        var anon = api.CreateClient();
        var day = OpenDay();
        var slot = (await SlotsAsync(anon, slug, serviceId, day))[0];
        var held = await Json(await BookAsync(anon, slug, serviceId, slot));
        var reference = Reference(held.GetProperty("authorizationUrl").GetString()!);
        api.Merchant.Charges[reference] = new VerifiedCharge(reference, 500_000, "NGN", DateTimeOffset.UtcNow);
        (await anon.PostAsJsonAsync($"/api/public/bookings/{Token(held)}/verify", new { reference }, Ct)).EnsureSuccessStatusCode();
        var id = await BookingIdAsync(owner, held);
        (await owner.PostAsJsonAsync($"/api/bookings/{id}/refund", new { amountMinor = 200_000 }, Ct)).EnsureSuccessStatusCode();

        // The deposit and the refund were paid today, ahead of the appointment.
        var appointmentOnly = await Json(await owner.GetAsync($"/api/booking-reports?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(1, appointmentOnly.GetProperty("bookings").GetInt32());
        AssertBreakdownsSum(appointmentOnly);
        Assert.Equal(0, appointmentOnly.GetProperty("revenueMinor").GetInt64());

        var lagosToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));
        var withPayments = await Json(await owner.GetAsync($"/api/booking-reports?from={lagosToday:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(300_000, withPayments.GetProperty("revenueMinor").GetInt64());
        Assert.Equal(300_000, withPayments.GetProperty("depositsMinor").GetInt64()); // net of the refund
        AssertBreakdownsSum(withPayments);

        var paymentsOnly = await Json(await owner.GetAsync($"/api/booking-reports?from={lagosToday:yyyy-MM-dd}&to={lagosToday:yyyy-MM-dd}", Ct));
        Assert.Equal(300_000, paymentsOnly.GetProperty("revenueMinor").GetInt64());
        AssertBreakdownsSum(paymentsOnly);
        Assert.Contains(paymentsOnly.GetProperty("byService").EnumerateArray(), s => s.GetProperty("name").GetString() == "Bob");
    }

    private static void AssertBreakdownsSum(JsonElement report)
    {
        var revenue = report.GetProperty("revenueMinor").GetInt64();
        Assert.Equal(revenue, report.GetProperty("byService").EnumerateArray().Sum(s => s.GetProperty("revenueMinor").GetInt64()));
        Assert.Equal(revenue, report.GetProperty("byStaff").EnumerateArray().Sum(s => s.GetProperty("revenueMinor").GetInt64()));
    }

    [Fact]
    public async Task B5_Business_dto_says_whether_deposits_are_actually_taken_and_why_not()
    {
        var (owner, _, tenantId) = await SignupAsync("Plan");
        var fresh = await Json(await owner.GetAsync("/api/booking-business", Ct));
        Assert.False(fresh.GetProperty("depositsEnabled").GetBoolean());

        await ConnectPaystackAsync(owner);
        var free = await Json(await owner.GetAsync("/api/booking-business", Ct));
        Assert.True(free.GetProperty("paystackConnected").GetBoolean());
        Assert.False(free.GetProperty("depositsEnabled").GetBoolean());
        Assert.False(free.GetProperty("depositsInPlan").GetBoolean());

        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        var pro = await Json(await owner.GetAsync("/api/booking-business", Ct));
        Assert.True(pro.GetProperty("depositsEnabled").GetBoolean());
        Assert.True(pro.GetProperty("depositsInPlan").GetBoolean());
        var removed = await Json(await owner.DeleteAsync("/api/booking-business/paystack", Ct));
        Assert.False(removed.GetProperty("depositsEnabled").GetBoolean());
        Assert.True(removed.GetProperty("depositsInPlan").GetBoolean());
    }

    // Helpers

    private async Task<(HttpClient Owner, string Slug, Guid TenantId)> SignupAsync(string name)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Owner", email = $"bk-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "bookings", businessName = $"{name} Salon",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var business = await Json(await client.GetAsync("/api/booking-business", Ct));
        return (client, business.GetProperty("slug").GetString()!, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private async Task ConnectPaystackAsync(HttpClient owner) =>
        (await owner.PutAsJsonAsync("/api/booking-business/paystack", new { secretKey = "sk_test_0123456789abcdef0123" }, Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> CreateServiceAsync(HttpClient owner, string name, int minutes, long price, string depositType, long depositValue)
    {
        var staffIds = (await Json(await owner.GetAsync("/api/staff", Ct))).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        var response = await owner.PostAsJsonAsync("/api/services", new
        {
            name, durationMinutes = minutes, bufferBeforeMinutes = 0, bufferAfterMinutes = 0, priceMinor = price,
            depositType, depositValue, isActive = true, position = 0, staffIds,
        }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<List<DateTimeOffset>> SlotsAsync(HttpClient client, string slug, Guid serviceId, DateOnly day)
    {
        var availability = await Json(await client.GetAsync($"/api/public/book/{slug}/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}", Ct));
        return availability.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ToList();
    }

    private Task<HttpResponseMessage> BookAsync(HttpClient client, string slug, Guid serviceId, DateTimeOffset startsAt, string phone = "08030000000") =>
        client.PostAsJsonAsync($"/api/public/book/{slug}/bookings", new
        {
            serviceId, staffId = (Guid?)null, startsAt, notes = (string?)null,
            customer = new { name = "Tunde", phone, email = (string?)null },
        }, Ct);

    private async Task<Guid> BookingIdAsync(HttpClient owner, JsonElement held)
    {
        var code = held.GetProperty("booking").GetProperty("code").GetString();
        var list = await Json(await owner.GetAsync($"/api/bookings?q={code}", Ct));
        return list.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private static string Token(JsonElement held) => held.GetProperty("manageUrl").GetString()!.Split("/b/")[1];
    private static string Reference(string authorizationUrl) => authorizationUrl.Split('/')[^1];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
