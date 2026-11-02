using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>Two requests at once can't leave the platform with no super admin, or refund a stay twice.</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerConcurrencyTests(ApiFixture api)
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

    [Fact]
    public async Task Two_super_admins_removing_each_other_at_once_leave_one()
    {
        // Tests in this collection run one at a time, so the other super admins can step aside for this test and come back after.
        var others = await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.Role == PlatformRole.SuperAdmin && a.RemovedAt == null).Select(a => a.Id).ToListAsync(Ct));
        var (a, aUser, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.SuperAdmin);
        var (b, bUser, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.SuperAdmin);
        var aId = await api.WithDbAsync(db => db.PlatformAdmins.Where(x => x.UserId == aUser).Select(x => x.Id).SingleAsync(Ct));
        var bId = await api.WithDbAsync(db => db.PlatformAdmins.Where(x => x.UserId == bUser).Select(x => x.Id).SingleAsync(Ct));
        var now = DateTimeOffset.UtcNow;
        await api.WithDbAsync(db => db.PlatformAdmins.Where(x => others.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAt, now), Ct));
        try
        {
            var results = await Task.WhenAll(
                a.PutAsJsonAsync($"/api/platform/admins/{bId}", new { role = "Finance" }, Ct),
                b.PutAsJsonAsync($"/api/platform/admins/{aId}", new { role = "Finance" }, Ct));

            Assert.Contains(results, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await api.WithDbAsync(db => db.PlatformAdmins.CountAsync(x => (x.Id == aId || x.Id == bId) && x.Role == PlatformRole.SuperAdmin && x.RemovedAt == null, Ct)));
        }
        finally
        {
            await api.WithDbAsync(db => db.PlatformAdmins.Where(x => others.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAt, (DateTimeOffset?)null), Ct));
        }
    }

    [Theory]
    [InlineData("guest")]
    [InlineData("host")]
    public async Task A_double_clicked_stay_cancellation_refunds_once(string who)
    {
        var agency = await kit.AgencyAsync($"Double {who} Homes", verified: true);
        var listingId = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: $"Double {who} apartment", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        var guest = await kit.SeekerAsync($"Double {who}");
        var checkIn = Today.AddDays(14);
        var booking = await Json(await guest.PostAsJsonAsync("/api/stays", new
        {
            listingId, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(3).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666",
        }, Ct));
        var reference = api.Gateway.Initialized.Last(i => booking.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference)).Reference;
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, booking.GetProperty("totalMinor").GetInt64(), "NGN", DateTimeOffset.UtcNow);
        var stayId = booking.GetProperty("stayId").GetGuid();
        await Json(await guest.PostAsJsonAsync($"/api/stays/{stayId}/verify", new { reference }, Ct));
        var (client, path) = who == "guest" ? (guest, $"/api/stays/{stayId}/cancel") : (agency.Client, $"/api/property/stays/{stayId}/cancel");

        api.Gateway.RefundDelay = TimeSpan.FromMilliseconds(300);
        try
        {
            var results = await Task.WhenAll(client.PostAsync(path, null, Ct), client.PostAsync(path, null, Ct));
            Assert.Contains(results, r => r.IsSuccessStatusCode);
        }
        finally
        {
            api.Gateway.RefundDelay = TimeSpan.Zero;
        }

        lock (api.Gateway.Refunds) Assert.Single(api.Gateway.Refunds, r => r.Reference == reference);
    }
}
