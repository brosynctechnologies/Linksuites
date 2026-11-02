using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Password reset/change and server-side sessions (admin console and support view).</summary>
[Collection(nameof(ApiCollection))]
public sealed class BlockerAuthTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private async Task<(string Email, JsonElement Auth)> SeekerAsync()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        var auth = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/seeker/signup", new { fullName = "Forgetful Person", email, password = "password123" }, Ct));
        return (email, auth);
    }

    /// <summary>The reset link from the latest "password.reset" email, split into email and token.</summary>
    private async Task<(string Link, string Email, string Token)> ResetLinkAsync(string email)
    {
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "password.reset" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["link"];
        var fragment = System.Web.HttpUtility.ParseQueryString(link[(link.IndexOf('#') + 1)..]);
        return (link, fragment["email"]!, fragment["token"]!);
    }

    [Fact]
    public async Task Forgot_password_sends_a_link_and_reveals_nothing()
    {
        var (email, _) = await SeekerAsync();
        var anon = api.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/password/forgot", new { email, product = "property" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/password/forgot", new { email = "nobody-at-all@example.com", product = "property" }, Ct)).StatusCode);

        var (link, _, _) = await ResetLinkAsync(email);
        Assert.StartsWith("http://localhost:4100/reset-password#", link);
        Assert.False(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "password.reset" && n.Recipient == "nobody-at-all@example.com", Ct)));
        for (var i = 0; i < 4; i++) await anon.PostAsJsonAsync("/api/auth/password/forgot", new { email }, Ct);
        Assert.Equal(3, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "password.reset" && n.Recipient == email, Ct)));
    }

    [Fact]
    public async Task Reset_works_once_and_ends_every_session()
    {
        var (email, auth) = await SeekerAsync();
        var anon = api.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/password/forgot", new { email }, Ct);
        var (_, linkEmail, token) = await ResetLinkAsync(email);

        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/auth/password/reset", new { email = linkEmail, token = token + "x", newPassword = "brand-new-pass" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/password/reset", new { email = linkEmail, token, newPassword = "brand-new-pass" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/auth/password/reset", new { email = linkEmail, token, newPassword = "another-pass-1" }, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.GetProperty("refreshToken").GetString() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/seeker/login", new { email, password = "brand-new-pass" }, Ct)).StatusCode);
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "password.changed" && n.Recipient == email, Ct)));
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "user.password_reset" && a.EntityType == "User", Ct)));
    }

    [Fact]
    public async Task Reset_and_sign_out_everywhere_end_admin_console_sessions()
    {
        var (panel, userId, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.OK, (await panel.GetAsync("/api/platform/me", Ct)).StatusCode);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        (await support.PostAsync($"/api/platform/users/{userId}/sign-out-everywhere", null, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await panel.GetAsync("/api/platform/me", Ct)).StatusCode);
    }

    [Fact]
    public async Task Change_password_needs_the_current_one()
    {
        var (email, auth) = await SeekerAsync();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var other = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/seeker/login", new { email, password = "password123" }, Ct));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/password/change",
            new { currentPassword = "wrong-password", newPassword = "changed-pass-1" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/password/change",
            new { currentPassword = "password123", newPassword = "changed-pass-1", refreshToken = auth.GetProperty("refreshToken").GetString() }, Ct)).StatusCode);

        // The kept session works; the other one was ended. (Order matters: reusing a revoked token is treated as theft and
        // ends every session, by design.)
        var anon = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = auth.GetProperty("refreshToken").GetString() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = other.GetProperty("refreshToken").GetString() }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_sign_out_ends_the_session()
    {
        var (panel, _, token) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        var me = await Json(await panel.GetAsync("/api/platform/me", Ct));
        Assert.Equal("Moderator", me.GetProperty("platformRole").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await panel.PostAsync("/api/platform/auth/logout", null, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await panel.GetAsync("/api/platform/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await panel.GetAsync("/api/admin/property/moderation", Ct)).StatusCode);
    }

    [Fact]
    public async Task Platform_me_follows_role_changes()
    {
        var (panel, userId, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Role, PlatformRole.Finance), Ct));
        Assert.Equal("Finance", (await Json(await panel.GetAsync("/api/platform/me", Ct))).GetProperty("platformRole").GetString());
    }

    [Fact]
    public async Task Ending_a_support_view_stops_the_token_and_shows_in_activity()
    {
        var kit = new PropertyKit(api);
        var agency = await kit.AgencyAsync("Ended View Realty");
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = "Checking a reported layout problem." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/property/listings", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsync("/api/support-view/end", null, Ct)).StatusCode); // the banner's End view

        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/property/listings", Ct)).StatusCode);
        var activity = await Json(await agency.Client.GetAsync("/api/account/activity", Ct));
        Assert.NotEqual(JsonValueKind.Null, activity[0].GetProperty("endedAt").ValueKind);

        var second = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = "Second look at the same problem." }, Ct));
        (await support.PostAsync($"/api/platform/view-as/{second.GetProperty("sessionId").GetGuid()}/end", null, Ct)).EnsureSuccessStatusCode();
        var viewer2 = api.CreateClient();
        viewer2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer2.GetAsync("/api/property/listings", Ct)).StatusCode);
    }

    [Fact]
    public async Task Support_sends_a_real_reset_email()
    {
        var (email, auth) = await SeekerAsync();
        var userId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        (await support.PostAsync($"/api/platform/users/{userId}/send-password-reset", null, Ct)).EnsureSuccessStatusCode();
        await ResetLinkAsync(email);
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "user.send_password_reset" && a.EntityId == userId.ToString(), Ct)));
    }
}
