using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Saas.Application.Billing;

namespace Saas.IntegrationTests;

/// <summary>Wave 2 (roadmap 2026-11-17) BookLink fixes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BookingWave2Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly OpenDay()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(3);
        while (d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    [Fact]
    public async Task B6_Availability_can_leave_out_the_booking_being_moved()
    {
        var (owner, slug, _) = await SignupAsync("Mover");
        var serviceId = await CreateServiceAsync(owner, "Cut", 60, price: 500_000, depositType: "None", depositValue: 0);
        var anon = api.CreateClient();
        var day = OpenDay();
        var first = (await SlotsAsync(anon, $"/api/public/book/{slug}/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}"))[0];
        var held = await Json(await BookAsync(anon, slug, serviceId, first));
        var token = Token(held);
        var id = await BookingIdAsync(owner, held);

        var publicSlots = await SlotsAsync(anon, $"/api/public/book/{slug}/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");
        Assert.DoesNotContain(first, publicSlots);

        var publicExcluding = await SlotsAsync(anon, $"/api/public/book/{slug}/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}&excludeToken={token}");
        Assert.Contains(first, publicExcluding);
        Assert.Contains(first.AddMinutes(30), publicExcluding);

        var appExcluding = await SlotsAsync(owner, $"/api/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}&excludeBookingId={id}");
        Assert.Contains(first, appExcluding);
        var appSlots = await SlotsAsync(owner, $"/api/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");
        Assert.DoesNotContain(first, appSlots);
    }

    [Fact]
    public async Task Pending_booking_is_confirmed_when_a_balance_payment_covers_the_deposit()
    {
        var (owner, slug, tenantId) = await SignupAsync("Covered");
        await api.MakeProAsync(tenantId, Products.Bookings, "solo");
        await ConnectPaystackAsync(owner);
        var serviceId = await CreateServiceAsync(owner, "Braids", 60, price: 1_000_000, depositType: "Fixed", depositValue: 500_000);
        var anon = api.CreateClient();
        var slot = (await SlotsAsync(anon, $"/api/public/book/{slug}/availability?serviceId={serviceId}&date={OpenDay():yyyy-MM-dd}"))[0];
        var held = await Json(await BookAsync(anon, slug, serviceId, slot));
        var id = await BookingIdAsync(owner, held);

        var part = await Json(await owner.PostAsJsonAsync($"/api/bookings/{id}/payments", new { amountMinor = 200_000, type = "Balance", method = "Cash" }, Ct));
        Assert.Equal("Pending", part.GetProperty("status").GetString());
        var rest = await Json(await owner.PostAsJsonAsync($"/api/bookings/{id}/payments", new { amountMinor = 300_000, type = "Balance", method = "Transfer" }, Ct));
        Assert.Equal("Confirmed", rest.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Public_booking_page_list_has_only_active_businesses_taking_bookings()
    {
        var (_, listed, _) = await SignupAsync("ListedSalon");
        var (closedOwner, closed, _) = await SignupAsync("ClosedSalon");
        var business = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Json(await closedOwner.GetAsync("/api/booking-business", Ct))).GetRawText())!;
        business["bookingEnabled"] = false;
        (await closedOwner.PutAsJsonAsync("/api/booking-business", business, Ct)).EnsureSuccessStatusCode();

        var anon = api.CreateClient();
        var slugs = new List<string>();
        for (var page = 1; ; page++)
        {
            var response = await anon.GetAsync($"/api/public/book?page={page}&pageSize=500", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await Json(response);
            slugs.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()!));
            if (page * 500 >= body.GetProperty("total").GetInt32()) break;
        }
        Assert.Contains(listed, slugs);
        Assert.DoesNotContain(closed, slugs);
    }

    // Helpers

    private async Task<(HttpClient Owner, string Slug, Guid TenantId)> SignupAsync(string name)
    {
        var client = api.CreateClient();
        var response = await api.SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = $"{name} Owner", email = $"bk2-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
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

    private async Task<List<DateTimeOffset>> SlotsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await Json(response)).GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ToList();
    }

    private Task<HttpResponseMessage> BookAsync(HttpClient client, string slug, Guid serviceId, DateTimeOffset startsAt) =>
        client.PostAsJsonAsync($"/api/public/book/{slug}/bookings", new
        {
            serviceId, staffId = (Guid?)null, startsAt, notes = (string?)null,
            customer = new { name = "Tunde", phone = "08030000000", email = (string?)null },
        }, Ct);

    private async Task<Guid> BookingIdAsync(HttpClient owner, JsonElement held)
    {
        var code = held.GetProperty("booking").GetProperty("code").GetString();
        var list = await Json(await owner.GetAsync($"/api/bookings?q={code}", Ct));
        return list.GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private static string Token(JsonElement held) => held.GetProperty("manageUrl").GetString()!.Split("/b/")[1];

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
