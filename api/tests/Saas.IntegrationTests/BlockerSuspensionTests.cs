using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Suspension holds on every server at once and covers invitations, staff calendar feeds and customer changes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerSuspensionTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private async Task<(HttpClient Owner, string Slug, Guid TenantId)> SalonAsync(string name)
    {
        var client = api.CreateClient();
        var auth = await Json(await api.SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = $"{name} Owner", email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "bookings", businessName = $"{name} Salon {Guid.NewGuid().ToString("N")[..4]}",
        }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var business = await Json(await client.GetAsync("/api/booking-business", Ct));
        return (client, business.GetProperty("slug").GetString()!, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private Task SetStatusDirectlyAsync(Guid tenantId, TenantStatus status) =>
        api.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status), Ct));

    [Fact]
    public async Task Suspension_by_another_server_applies_on_the_next_request()
    {
        var (owner, _, tenantId) = await SalonAsync("Elsewhere");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/staff", Ct)).StatusCode); // status now known to this server

        await SetStatusDirectlyAsync(tenantId, TenantStatus.Suspended); // as another API server would, without touching this one's memory

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/staff", Ct)).StatusCode);
        await SetStatusDirectlyAsync(tenantId, TenantStatus.Active);
    }

    [Fact]
    public async Task Suspended_business_invitations_cannot_be_accepted()
    {
        var (owner, _, tenantId) = await SalonAsync("Inviting");
        await api.MakeProAsync(tenantId, "bookings", "team"); // Free has one seat
        var email = $"new-staff-{Guid.NewGuid():N}@example.com";
        (await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
        var token = link[(link.LastIndexOf('/') + 1)..];
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        (await support.PostAsJsonAsync($"/api/platform/businesses/{tenantId}/suspend", new { reason = "Investigating fake bookings." }, Ct)).EnsureSuccessStatusCode();

        var accept = await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{token}/accept", new { fullName = "New Staff", password = "password123" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, accept.StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == tenantId && m.User!.Email == email, Ct)));
    }

    [Fact]
    public async Task Staff_calendar_feed_stops_while_suspended()
    {
        var (owner, _, tenantId) = await SalonAsync("Feed");
        var feed = (await Json(await owner.GetAsync("/api/staff", Ct)))[0].GetProperty("calendarFeedUrl").GetString()!;
        var path = new Uri(feed).PathAndQuery;
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync(path, Ct)).StatusCode);

        await SetStatusDirectlyAsync(tenantId, TenantStatus.Suspended);

        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync(path, Ct)).StatusCode);
        await SetStatusDirectlyAsync(tenantId, TenantStatus.Active);
    }

    [Fact]
    public async Task Customers_can_view_but_not_change_bookings_with_a_suspended_business()
    {
        var (owner, slug, tenantId) = await SalonAsync("Frozen");
        var staffIds = (await Json(await owner.GetAsync("/api/staff", Ct))).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        var serviceId = (await Json(await owner.PostAsJsonAsync("/api/services", new
        {
            name = "Haircut", durationMinutes = 60, bufferBeforeMinutes = 0, bufferAfterMinutes = 0, priceMinor = 500_000,
            depositType = "None", depositValue = 0, isActive = true, position = 0, staffIds,
        }, Ct))).GetProperty("id").GetGuid();
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)).AddDays(3);
        while (day.DayOfWeek == DayOfWeek.Sunday) day = day.AddDays(1);
        var anon = api.CreateClient();
        var slots = (await Json(await anon.GetAsync($"/api/public/book/{slug}/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}", Ct)))
            .GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("startsAt").GetDateTimeOffset()).ToList();
        var booked = await Json(await anon.PostAsJsonAsync($"/api/public/book/{slug}/bookings", new
        {
            serviceId, staffId = (Guid?)null, startsAt = slots[0], notes = (string?)null, customer = new { name = "Bola", phone = "08030000000", email = (string?)null },
        }, Ct));
        var token = booked.GetProperty("manageUrl").GetString()!.Split("/b/")[1];

        await SetStatusDirectlyAsync(tenantId, TenantStatus.Suspended);

        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/api/public/bookings/{token}", Ct)).StatusCode);
        var cancel = await anon.PostAsync($"/api/public/bookings/{token}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Contains("currently unavailable", await cancel.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsJsonAsync($"/api/public/bookings/{token}/reschedule", new { startsAt = slots[1] }, Ct)).StatusCode);
        await SetStatusDirectlyAsync(tenantId, TenantStatus.Active);
    }
}
