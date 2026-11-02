using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class PlatformAuthTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An admin with the given role signed in to the admin panel (password + emailed code).</summary>
    public static async Task<(HttpClient Client, Guid UserId, string Token)> PanelAdminAsync(ApiFixture api, PlatformRole role)
    {
        var (seeded, userId) = await PlatformRoleTests.AdminWithRoleAsync(api, role);
        var email = await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync(Ct));
        var client = api.CreateClient();
        var challenge = await Json(await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password = "password123" }, Ct));
        var code = await CodeAsync(api, email);
        var auth = await Json(await client.PostAsJsonAsync("/api/platform/auth/verify",
            new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code }, Ct));
        var token = auth.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId, token);
    }

    /// <summary>Signs in to the admin panel with an email and password (reads the emailed code from the outbox).</summary>
    public static async Task<(HttpClient Client, string Token)> PanelLoginAsync(ApiFixture api, string email, string password)
    {
        // Admin sessions last 8 hours and only 3 codes may be sent per 10 minutes, so tests reuse one session per admin.
        var key = $"{api.GetHashCode()}:{email}";
        if (Sessions.TryGetValue(key, out var cached))
        {
            var reused = api.CreateClient();
            reused.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cached);
            return (reused, cached);
        }
        var client = api.CreateClient();
        var challenge = await Json(await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password }, Ct));
        var auth = await Json(await client.PostAsJsonAsync("/api/platform/auth/verify",
            new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code = await CodeAsync(api, email) }, Ct));
        var token = auth.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Sessions[key] = token;
        return (client, token);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Sessions = new();

    private static async Task<string> CodeAsync(ApiFixture api, string email) =>
        (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "admin.code" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["code"];

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync(Ct));
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    [Fact]
    public async Task Admin_signs_in_with_password_and_emailed_code()
    {
        var (client, _, token) = await PanelAdminAsync(api, PlatformRole.Support);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/health", Ct)).StatusCode);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(TimeSpan.FromHours(8), jwt.ValidTo - jwt.IssuedAt);
        Assert.Equal("otp", jwt.Claims.Single(c => c.Type == "amr").Value);
        Assert.Equal("Support", jwt.Claims.Single(c => c.Type == "padm").Value);
    }

    [Fact]
    public async Task Wrong_password_and_non_admin_look_the_same()
    {
        var (_, userId) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Finance);
        var email = await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync(Ct));
        var customer = $"customer-{Guid.NewGuid():N}@example.com";
        await api.CreateClient().PostAsJsonAsync("/api/auth/seeker/signup", new { fullName = "Customer", email = customer, password = "password123" }, Ct);

        var wrongPassword = await api.CreateClient().PostAsJsonAsync("/api/platform/auth/login", new { email, password = "not-the-password" }, Ct);
        var notAdmin = await api.CreateClient().PostAsJsonAsync("/api/platform/auth/login", new { email = customer, password = "password123" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, notAdmin.StatusCode);
        var a = (await wrongPassword.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("detail").GetString();
        var b = (await notAdmin.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("detail").GetString();
        Assert.Equal(a, b);
        Assert.False(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "admin.code" && n.Recipient == customer, Ct)));
    }

    [Fact]
    public async Task Customer_app_token_is_refused_on_platform_routes()
    {
        var (seekerToken, _) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.SuperAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await seekerToken.GetAsync("/api/platform/health", Ct)).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_codes_burn_the_challenge()
    {
        var (_, userId) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Moderator);
        var email = await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync(Ct));
        var client = api.CreateClient();
        var challengeId = (await Json(await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password = "password123" }, Ct)))
            .GetProperty("challengeId").GetGuid();
        var code = await CodeAsync(api, email);
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/platform/auth/verify", new { challengeId, code = wrong }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/platform/auth/verify", new { challengeId, code }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Health_shows_jobs_and_failed_emails()
    {
        var (client, _, _) = await PanelAdminAsync(api, PlatformRole.Moderator);
        await api.WithDbAsync(async db =>
        {
            db.JobRuns.Add(new JobRun { Name = $"test-job-{Guid.NewGuid():N}"[..20], LastStartedAt = DateTimeOffset.UtcNow, LastSucceededAt = DateTimeOffset.UtcNow });
            db.Notifications.Add(new Notification { Recipient = "bounce@example.com", TemplateKey = "otp", Status = NotificationStatus.Failed, Error = "Mailbox full" });
            return await db.SaveChangesAsync(Ct);
        });

        var health = await Json(await client.GetAsync("/api/platform/health", Ct));
        Assert.Contains(health.GetProperty("jobs").EnumerateArray(), j => j.GetProperty("name").GetString()!.StartsWith("test-job-"));
        Assert.Contains(health.GetProperty("failures").EnumerateArray(), f => f.GetProperty("error").GetString() == "Mailbox full");
        Assert.True(health.GetProperty("outbox").GetProperty("failed").GetInt32() >= 1);
    }
}
