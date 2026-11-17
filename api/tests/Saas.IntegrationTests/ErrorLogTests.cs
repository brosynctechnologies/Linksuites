using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Platform;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 0 error tracking: API crashes and web app crash reports are grouped by kind in <c>error_events</c>, a new API error emails
/// the support team once, browser reports never email, and support lists and resolves them in the admin console.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class ErrorLogTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    /// <summary>A real unhandled error: a patient whose stored allergy text can't be decrypted (see CareAccessTests).</summary>
    private async Task<HttpStatusCode> CrashAsync(CareKit.Clinic clinic, string firstName)
    {
        var patientId = (await kit.PatientAsync(clinic, firstName, "Bello")).GetProperty("id").GetGuid();
        await api.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE patients SET allergies = '[\"CfDJ8-not-a-real-ciphertext\"]'::jsonb WHERE id = {patientId}", Ct));
        return (await clinic.Owner.GetAsync($"/api/care/patients/{patientId}", Ct)).StatusCode;
    }

    private Task<List<ErrorEvent>> ApiCrashRowsAsync() => api.WithDbAsync(db =>
        db.ErrorEvents.AsNoTracking().Where(e => e.Source == "api" && e.Path!.StartsWith("GET /api/care/patients/")).ToListAsync(Ct));

    private Task<int> AlertsAsync(DateTimeOffset since) => api.WithDbAsync(db =>
        db.Notifications.CountAsync(n => n.TemplateKey == "platform.error_new" && n.CreatedAt >= since, Ct));

    [Fact]
    public async Task Api_crashes_are_grouped_by_kind_and_email_the_team_once()
    {
        await api.WithDbAsync(db => db.ErrorEvents.Where(e => e.Source == "api" && e.Path!.StartsWith("GET /api/care/patients/")).ExecuteDeleteAsync(Ct));
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var clinic = await kit.ClinicAsync("Error Log Clinic");

        Assert.Equal(HttpStatusCode.InternalServerError, await CrashAsync(clinic, "Kemi"));
        var row = Assert.Single(await ApiCrashRowsAsync());
        Assert.Equal(1, row.Count);
        Assert.Null(row.ResolvedAt);
        Assert.False(string.IsNullOrEmpty(row.Stack));
        var alerts = await AlertsAsync(since);
        Assert.True(alerts >= 1, "a new API error emails the team");

        // Another patient (another id in the path): the same kind of error, so one row, counted twice, and no second email.
        Assert.Equal(HttpStatusCode.InternalServerError, await CrashAsync(clinic, "Tunde"));
        row = Assert.Single(await ApiCrashRowsAsync());
        Assert.Equal(2, row.Count);
        Assert.Equal(alerts, await AlertsAsync(since));
    }

    [Fact]
    public async Task Support_lists_and_resolves_errors_and_a_repeat_reopens_it()
    {
        var clinic = await kit.ClinicAsync("Error Reopen Clinic");
        Assert.Equal(HttpStatusCode.InternalServerError, await CrashAsync(clinic, "Ada"));
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        var open = await support.GetFromJsonAsync<List<JsonElement>>("/api/platform/errors", Ct);
        var item = Assert.Single(open!, e => e.GetProperty("path").GetString()?.StartsWith("GET /api/care/patients/") == true);
        var id = item.GetProperty("id").GetGuid();

        (await support.PostAsync($"/api/platform/errors/{id}/resolve", null, Ct)).EnsureSuccessStatusCode();
        open = await support.GetFromJsonAsync<List<JsonElement>>("/api/platform/errors", Ct);
        Assert.DoesNotContain(open!, e => e.GetProperty("id").GetGuid() == id);
        var resolved = await support.GetFromJsonAsync<List<JsonElement>>("/api/platform/errors?status=resolved", Ct);
        Assert.Contains(resolved!, e => e.GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.InternalServerError, await CrashAsync(clinic, "Bisi"));
        open = await support.GetFromJsonAsync<List<JsonElement>>("/api/platform/errors", Ct);
        Assert.Contains(open!, e => e.GetProperty("id").GetGuid() == id); // it came back, so it is open again
    }

    [Fact]
    public async Task Only_support_and_super_admins_see_errors()
    {
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.GetAsync("/api/platform/errors", Ct)).StatusCode);
        var (owner, _) = await api.SignupAsync("ErrorsNosy");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/platform/errors", Ct)).StatusCode);
        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/platform/errors", Ct)).StatusCode);
    }

    [Fact]
    public async Task Browser_reports_are_grouped_and_never_email()
    {
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var client = api.CreateClient();
        var first = await client.PostAsJsonAsync("/api/client-errors", new
        {
            app = "school", message = $"TypeError: x{marker} is undefined at row 12", path = "/app/results/123?term=2", stack = "at Results (page.js:1:1)",
        }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var second = await client.PostAsJsonAsync("/api/client-errors", new
        {
            app = "school", message = $"TypeError: x{marker} is undefined at row 40", path = "/app/results/987",
        }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var rows = await api.WithDbAsync(db => db.ErrorEvents.AsNoTracking().Where(e => e.Message.Contains(marker)).ToListAsync(Ct));
        var row = Assert.Single(rows);
        Assert.Equal("web:school", row.Source);
        Assert.Equal("client", row.Kind);
        Assert.Equal("/app/results/123", row.Path); // the query string is never stored
        Assert.Equal(2, row.Count);
        var alerts = await api.WithDbAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.TemplateKey == "platform.error_new" && n.CreatedAt >= since).ToListAsync(Ct));
        Assert.DoesNotContain(alerts, n => n.Payload is { } p && p.TryGetValue("message", out var m) && m.Contains(marker));
    }

    [Fact]
    public async Task Api_errors_are_grouped_by_route_template_not_the_real_path()
    {
        var clinic = await kit.ClinicAsync("Error Route Clinic");
        Assert.Equal(HttpStatusCode.InternalServerError, await CrashAsync(clinic, "Route"));
        var row = Assert.Single(await ApiCrashRowsAsync());
        Assert.Equal("GET /api/care/patients/{id:guid}", row.Path); // no real ids (or tokens) in the stored path
    }

    [Fact]
    public async Task One_reporter_can_add_only_a_few_new_browser_errors_an_hour()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var client = api.CreateClient();
        for (var i = 0; i < 8; i++)
        {
            var r = await client.PostAsJsonAsync("/api/client-errors", new { app = "hub", message = $"Flood {marker} kind {(char)('a' + i)}" }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        }
        var stored = await api.WithDbAsync(db => db.ErrorEvents.CountAsync(e => e.Message.Contains(marker), Ct));
        Assert.Equal(ErrorRules.NewBrowserKindsPerReporterPerHour, stored);
    }

    [Fact]
    public async Task A_browser_report_does_not_reopen_an_error_marked_fixed()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var client = api.CreateClient();
        var body = new { app = "care", message = $"ReferenceError: f{marker} is not defined" };
        (await client.PostAsJsonAsync("/api/client-errors", body, Ct)).EnsureSuccessStatusCode();
        await api.WithDbAsync(db => db.ErrorEvents.Where(e => e.Message.Contains(marker))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResolvedAt, DateTimeOffset.UtcNow), Ct));

        (await client.PostAsJsonAsync("/api/client-errors", body, Ct)).EnsureSuccessStatusCode();
        var row = await api.WithDbAsync(db => db.ErrorEvents.AsNoTracking().SingleAsync(e => e.Message.Contains(marker), Ct));
        Assert.NotNull(row.ResolvedAt);
        Assert.Equal(2, row.Count);
    }

    // Wave 2

    private Task<int> AlertsForKindAsync(string kind) => api.WithDbAsync(async db =>
        (await db.Notifications.AsNoTracking().Where(n => n.TemplateKey == "platform.error_new").Select(n => n.Payload).ToListAsync(Ct))
            .Count(p => p is not null && p.TryGetValue("kind", out var k) && k == kind));

    [Fact]
    public async Task An_api_error_that_comes_back_after_being_fixed_emails_again()
    {
        var kind = $"ReopenTest{Guid.NewGuid():N}"[..20];
        var log = api.Services.GetRequiredService<IErrorLog>();
        await log.RecordAsync("api", kind, "Object reference not set", "GET /api/orders/{id}", "at Orders.Get()", Ct);
        var first = await AlertsForKindAsync(kind);
        Assert.True(first >= 1, "a new API error emails the team");

        await log.RecordAsync("api", kind, "Object reference not set", "GET /api/orders/{id}", null, Ct);
        Assert.Equal(first, await AlertsForKindAsync(kind)); // a repeat while open stays quiet

        await api.WithDbAsync(db => db.ErrorEvents.Where(e => e.Kind == kind)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResolvedAt, DateTimeOffset.UtcNow), Ct));
        await log.RecordAsync("api", kind, "Object reference not set", "GET /api/orders/{id}", null, Ct);

        var row = await api.WithDbAsync(db => db.ErrorEvents.AsNoTracking().SingleAsync(e => e.Kind == kind, Ct));
        Assert.Null(row.ResolvedAt);
        Assert.Equal(3, row.Count);
        Assert.Equal(2 * first, await AlertsForKindAsync(kind)); // it came back after being fixed: the team hears about it like a new one
    }

    [Fact]
    public async Task Secrets_are_taken_out_of_stored_messages_and_stacks()
    {
        var kind = $"SecretTest{Guid.NewGuid():N}"[..20];
        var message = "Call failed: Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl with sk_live_abcDEF1234567890 "
            + "at https://api.example.com/v1/charge?key=AIzaSyA-secret-value&token=tok_987654&page=2 password=hunter22";
        var stack = "System.Exception: Host=db;Username=app;Password=SuperSecret99;Database=saas\n   at Pay(String sk_test_zyx987654321)\n"
            + "   body {\"password\":\"opensesame\",\"secret\":\"s3cr3t\"} client_secret=cs_abc";
        await api.Services.GetRequiredService<IErrorLog>().RecordAsync("api", kind, message, "POST /api/pay?token=abc", stack, Ct);

        var row = await api.WithDbAsync(db => db.ErrorEvents.AsNoTracking().SingleAsync(e => e.Kind == kind, Ct));
        foreach (var secret in new[] { "eyJhbGciOiJIUzI1NiJ9", "abcDEF1234567890", "AIzaSyA-secret-value", "tok_987654", "hunter22", "SuperSecret99",
                     "zyx987654321", "opensesame", "s3cr3t", "cs_abc" })
        {
            Assert.DoesNotContain(secret, row.Message);
            Assert.DoesNotContain(secret, row.Stack!);
        }
        Assert.Contains("Bearer [redacted]", row.Message);
        Assert.Contains("page=2", row.Message); // ordinary query values stay
        Assert.Contains("Username=app", row.Stack!);
    }

    [Fact]
    public void Redaction_leaves_ordinary_words_alone()
    {
        Assert.Equal("monkey=banana keyboard=1", ErrorRules.Redact("monkey=banana keyboard=1"));
        Assert.Equal("api_key=[redacted]&id=4", ErrorRules.Redact("api_key=abc123&id=4"));
    }

    [Fact]
    public async Task Oversized_browser_reports_are_refused()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/client-errors",
            new { app = "school", message = "big", stack = new string('x', 64 * 1024) }, Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Text_with_a_null_character_or_a_cut_emoji_is_still_recorded()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        // 999 letters then an emoji: capping at 1000 characters would split its surrogate pair.
        var message = $"Bad\0input {marker} " + new string('y', 999 - $"Bad\0input {marker} ".Length) + "😀";
        (await api.CreateClient().PostAsJsonAsync("/api/client-errors", new { app = "invoices", message }, Ct)).EnsureSuccessStatusCode();
        var row = await api.WithDbAsync(db => db.ErrorEvents.AsNoTracking().SingleAsync(e => e.Message.Contains(marker), Ct));
        Assert.DoesNotContain('\0', row.Message);
        Assert.False(char.IsHighSurrogate(row.Message[^1]));
    }

    [Theory]
    [InlineData("not-an-app", "boom")]
    [InlineData(null, "boom")]
    [InlineData("school", "")]
    public async Task Browser_reports_need_a_known_app_and_a_message(string? app, string message)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/client-errors", new { app, message }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Ids_and_numbers_do_not_split_an_error()
    {
        Assert.Equal(
            ErrorRules.Fingerprint("api", "X", "Order 12 not found", "GET /api/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            ErrorRules.Fingerprint("api", "X", "Order 9873 not found", "GET /api/orders/0e1d2c3b-4a59-6877-8695-a4b3c2d1e0f9?x=1"));
        Assert.NotEqual(
            ErrorRules.Fingerprint("api", "X", "Order 12 not found", "GET /api/orders/1"),
            ErrorRules.Fingerprint("api", "X", "Invoice 12 not found", "GET /api/orders/1"));
    }
}
