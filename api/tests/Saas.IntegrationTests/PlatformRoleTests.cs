using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PlatformRoleTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A signed-in account (no business) whose user has the given admin role.</summary>
    public static async Task<(HttpClient Client, Guid UserId)> AdminWithRoleAsync(ApiFixture api, PlatformRole role)
    {
        var client = api.CreateClient();
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var signup = await api.SignUpAsync(client, "/api/auth/seeker/signup", new { fullName = $"{role} Admin", email, password = "password123" }, Ct);
        signup.EnsureSuccessStatusCode();
        var userId = (await signup.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("user").GetProperty("id").GetGuid();
        await api.WithDbAsync(async db =>
        {
            db.PlatformAdmins.Add(new PlatformAdmin { UserId = userId, Role = role });
            return await db.SaveChangesAsync(Ct);
        });
        var login = await client.PostAsJsonAsync("/api/auth/seeker/login", new { email, password = "password123" }, Ct);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }

    [Fact]
    public async Task Config_admin_is_bootstrapped_as_super_admin()
    {
        await api.SignUpAsync(api.CreateClient(), "/api/auth/seeker/signup", new { fullName = "Admin", email = ApiFixture.AdminEmail, password = "password123" }, Ct);
        var (panel, _) = await PlatformAuthTests.PanelLoginAsync(api, ApiFixture.AdminEmail, "password123");
        var user = (await (await panel.GetAsync("/api/me", Ct)).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("user");

        Assert.Equal("SuperAdmin", user.GetProperty("platformRole").GetString());
        Assert.True(user.GetProperty("isPlatformAdmin").GetBoolean());
        var userId = user.GetProperty("id").GetGuid();
        Assert.Equal(PlatformRole.SuperAdmin, await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.UserId == userId).Select(a => a.Role).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Moderator_cannot_reach_finance_routes()
    {
        var (moderator, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync("/api/admin/payouts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync("/api/admin/course-reviews", Ct)).StatusCode);
    }

    [Fact]
    public async Task Finance_cannot_reach_moderation()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/admin/course-reviews", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/admin/payouts", Ct)).StatusCode);
    }

    [Fact]
    public async Task Removed_admin_loses_access_on_the_next_request()
    {
        var (moderator, userId, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);

        await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedAt, DateTimeOffset.UtcNow), Ct));

        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync("/api/admin/property/moderation", Ct)).StatusCode); // same token
    }

    [Fact]
    public async Task Customers_are_not_admins()
    {
        var client = api.CreateClient();
        var signup = await api.SignUpAsync(client, "/api/auth/seeker/signup",
            new { fullName = "Plain User", email = $"plain-{Guid.NewGuid():N}@example.com", password = "password123" }, Ct);
        var body = await signup.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.False(body.GetProperty("user").GetProperty("isPlatformAdmin").GetBoolean());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/overview", Ct)).StatusCode);
    }
}
