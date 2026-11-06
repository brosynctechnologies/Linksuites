using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Courses;
using Saas.Domain.Payouts;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PayoutAdminTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_queue_shows_the_product_of_each_payout()
    {
        var tutor = api.CreateClient();
        var signup = await api.SignUpAsync(tutor, "/api/auth/signup", new
        {
            fullName = "Payout Owner", email = $"payout-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "courses", businessName = "Payout Academy",
        }, Ct);
        var auth = await signup.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        tutor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();

        await api.WithDbAsync(async db =>
        {
            db.Earnings.Add(new Earning
            {
                TenantId = tenantId, Source = EarningSource.CourseOrder, SourceId = Guid.NewGuid(), Description = "Seeded sale",
                UserId = Guid.NewGuid(), Channel = SaleChannel.Academy, GrossMinor = 800_000, NetMinor = 800_000,
                AvailableAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            return await db.SaveChangesAsync(Ct);
        });
        (await tutor.PutAsJsonAsync("/api/academy/payout-account", new { bankName = "GTBank", accountNumber = "0123456789", accountName = "Payout Owner" }, Ct))
            .EnsureSuccessStatusCode();
        var payout = await (await tutor.PostAsJsonAsync("/api/studio/payouts", new { amountMinor = 600_000 }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);

        var admin = api.CreateClient();
        var login = await api.SignUpAsync(admin, "/api/auth/learner/signup", new { fullName = "Admin", email = ApiFixture.AdminEmail, password = "password123" }, Ct);
        if (!login.IsSuccessStatusCode)
            login = await admin.PostAsJsonAsync("/api/auth/learner/login", new { email = ApiFixture.AdminEmail, password = "password123" }, Ct);
        login.EnsureSuccessStatusCode();
        admin = (await PlatformAuthTests.PanelLoginAsync(api, ApiFixture.AdminEmail, "password123")).Client;

        var queue = await (await admin.GetAsync("/api/admin/payouts?status=Requested", Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var row = queue.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == payout.GetProperty("id").GetGuid());
        Assert.Equal("courses", row.GetProperty("product").GetString());
        Assert.Equal("Payout Academy", row.GetProperty("businessName").GetString());
    }
}
