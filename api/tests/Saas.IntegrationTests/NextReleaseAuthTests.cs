using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Saas.Api.Infrastructure;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Items moved forward from "next release": lockout, break-glass admin, role-naming 403s, masking, limits, validation.</summary>
[Collection(nameof(ApiCollection))]
public sealed class NextReleaseAuthTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly PropertyKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<string> DetailAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("detail").GetString()!;

    [Fact]
    public async Task Forgot_and_reset_without_an_email_are_a_clear_400()
    {
        var client = api.CreateClient();
        var forgot = await client.PostAsJsonAsync("/api/auth/password/forgot", new { product = "property" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, forgot.StatusCode);
        var reset = await client.PostAsJsonAsync("/api/auth/password/reset", new { token = "x", newPassword = "password123" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public void Reset_links_last_ten_minutes() =>
        Assert.Equal(TimeSpan.FromMinutes(10), api.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);

    [Fact]
    public async Task Bad_customer_logins_do_not_lock_an_admin_out_of_the_console_and_vice_versa()
    {
        var (_, userId) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Support);
        var email = await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).SingleAsync(Ct));
        var client = api.CreateClient();
        for (var i = 0; i < 6; i++) await client.PostAsJsonAsync("/api/auth/seeker/login", new { email, password = "wrong-password" }, Ct);

        Assert.True((await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode,
            "customer-side failures locked the console");

        var (_, other) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Support);
        var otherEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == other).Select(u => u.Email!).SingleAsync(Ct));
        for (var i = 0; i < 6; i++) await client.PostAsJsonAsync("/api/platform/auth/login", new { email = otherEmail, password = "wrong-password" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/platform/auth/login", new { email = otherEmail, password = "password123" }, Ct)).StatusCode);
        Assert.True((await client.PostAsJsonAsync("/api/auth/seeker/login", new { email = otherEmail, password = "password123" }, Ct)).IsSuccessStatusCode,
            "console failures locked the customer account");
    }

    [Fact]
    public async Task Break_glass_command_grants_super_admin_and_is_audited()
    {
        var email = $"rescuer-{Guid.NewGuid():N}@example.com";
        await kit.SeekerAsync("Rescuer", email);
        var userId = await api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(Ct));
        var output = new StringWriter();

        Assert.Equal(0, await AdminCli.RunAsync(api.Services, ["admin", "grant", email, "SuperAdmin"], output, Ct));

        var row = await api.WithDbAsync(db => db.PlatformAdmins.SingleAsync(a => a.UserId == userId, Ct));
        Assert.Equal((PlatformRole.SuperAdmin, (DateTimeOffset?)null), (row.Role, row.RemovedAt));
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "admin.grant_cli" && a.EntityId == row.UserId.ToString(), Ct)));
        Assert.Equal(1, await AdminCli.RunAsync(api.Services, ["admin", "grant", $"nobody-{Guid.NewGuid():N}@example.com", "SuperAdmin"], new StringWriter(), Ct));
        Assert.Equal(2, await AdminCli.RunAsync(api.Services, ["admin", "grant", email, "Janitor"], new StringWriter(), Ct));
    }

    [Fact]
    public async Task Forbidden_responses_name_the_role_that_is_needed()
    {
        var (moderator, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Moderator);
        var payments = await moderator.GetAsync("/api/platform/payments", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, payments.StatusCode);
        Assert.Contains("Finance", await DetailAsync(payments));

        var agency = await kit.AgencyAsync("Role Message Realty");
        await api.MakeProAsync(agency.TenantId, "property");
        var email = $"agent-{Guid.NewGuid():N}@example.com";
        (await agency.Client.PostAsJsonAsync("/api/team/invites", new { email, role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
        var accepted = await Json(await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = "Agency Staff", password = "password123" }, Ct));
        var staff = api.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accepted.GetProperty("accessToken").GetString());
        var earnings = await staff.GetAsync("/api/property/earnings", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, earnings.StatusCode);
        Assert.Contains("Owner or Admin", await DetailAsync(earnings));
    }

    [Fact]
    public async Task Support_views_show_only_the_last_four_digits_of_a_bank_account()
    {
        var agency = await kit.AgencyAsync("Masked Bank Realty");
        (await agency.Client.PutAsJsonAsync("/api/property/payout-account", new { bankName = "GTBank", accountNumber = "0123456789", accountName = "Masked Owner" }, Ct))
            .EnsureSuccessStatusCode();
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{agency.TenantId}/view-as", new { reason = "Checking the payout settings screen." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());

        var account = await Json(await viewer.GetAsync("/api/property/payout-account", Ct));
        Assert.Equal("••••6789", account.GetProperty("accountNumber").GetString());
        Assert.Equal("0123456789", (await Json(await agency.Client.GetAsync("/api/property/payout-account", Ct))).GetProperty("accountNumber").GetString());
    }

    [Fact]
    public async Task Admin_routes_are_rate_limited_per_admin()
    {
        var (_, _, token) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        await using var limited = api.WithWebHostBuilder(b => b.UseSetting("RateLimits:AdminPerMinute", "3"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/platform/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/platform/me", Ct)).StatusCode);
    }
}
