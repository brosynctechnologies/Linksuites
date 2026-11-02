using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// SchoolLink on the platform: the demo request, the admin console (usage, suspension, support view), reserved school links, and the
/// role table for every <c>/api/school</c> route.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class SchoolPlatformTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private HttpClient As(string token)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> StatusAsync(HttpClient admin, Guid tenantId, string action) =>
        admin.PostAsJsonAsync($"/api/platform/businesses/{tenantId}/{action}", new { reason = "Checking a complaint about this school." }, Ct);

    private static DateTimeOffset Lagos(DateOnly day, int hour, int minute = 0) =>
        new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(1));

    private async Task RunJobsAsync(DateTimeOffset now)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SchoolJobs>().RunOnceAsync(now, Ct);
    }

    // ---------- Demo requests ----------

    [Fact]
    public async Task Demo_request_emails_the_platform_team()
    {
        var (_, activeSuper) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.SuperAdmin);
        var (_, removedSuper) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.SuperAdmin);
        var (_, finance) = await PlatformRoleTests.AdminWithRoleAsync(api, PlatformRole.Finance);
        await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.UserId == removedSuper).ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedAt, DateTimeOffset.UtcNow), Ct));
        var anon = api.CreateClient();
        var school = $"Bright Future Academy {Guid.NewGuid().ToString("N")[..6]}";

        // Validated: every field, a real email, a sensible student count.
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/school-demo-requests",
            new { schoolName = "", contactName = "Mrs Ade", email = "ade@example.com", phone = "08031234567", students = 300 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/school-demo-requests",
            new { schoolName = school, contactName = "Mrs Ade", email = "not-an-email", phone = "08031234567", students = 300 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/school-demo-requests",
            new { schoolName = school, contactName = "Mrs Ade", email = "ade@example.com", phone = "08031234567", students = 0 }, Ct)).StatusCode);
        var noCount = await anon.PostAsJsonAsync("/api/school-demo-requests",
            new { schoolName = school, contactName = "Mrs Ade", email = "ade@example.com", phone = "08031234567", students = (int?)null }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noCount.StatusCode);
        Assert.True((await noCount.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("errors").TryGetProperty("students", out _));
        async Task<List<Notification>> SentAsync() =>
            (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.demo_request").ToListAsync(Ct)))
            .Where(n => n.Payload.GetValueOrDefault("school") == school).ToList();
        Assert.Empty(await SentAsync());

        var ok = await anon.PostAsJsonAsync("/api/school-demo-requests",
            new { schoolName = school, contactName = "Mrs Ade", email = "ade@example.com", phone = "08031234567", students = 450 }, Ct);
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync(Ct));

        var sent = await SentAsync();
        var superAdmins = await api.WithDbAsync(db => (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                                                       where a.RemovedAt == null && a.Role == PlatformRole.SuperAdmin select u.Email!).ToListAsync(Ct));
        Assert.Equal(superAdmins.Order(), sent.Select(n => n.Recipient).Order());
        var emailOf = (Guid id) => api.WithDbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.Email!).SingleAsync(Ct));
        Assert.Contains(await emailOf(activeSuper), sent.Select(n => n.Recipient));
        Assert.DoesNotContain(await emailOf(removedSuper), sent.Select(n => n.Recipient));
        Assert.DoesNotContain(await emailOf(finance), sent.Select(n => n.Recipient));

        // A real template, not the key-value fallback.
        var email = Saas.Infrastructure.Notifications.EmailTemplates.Render(sent[0], "SchoolLink");
        Assert.Contains(school, email.Subject);
        Assert.Contains("Mrs Ade", email.Text);
        Assert.Contains("ade@example.com", email.Text);
        Assert.Contains("450", email.Text);

        // Public and rate limited as a public write.
        var endpoint = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/api/school-demo-requests");
        Assert.Equal(Saas.Api.Endpoints.RateLimits.PublicWrite,endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public async Task Demo_request_without_a_super_admin_goes_to_the_platform_address()
    {
        var school = $"Orphan Demo School {Guid.NewGuid().ToString("N")[..6]}";
        var supers = await api.WithDbAsync(db => db.PlatformAdmins.Where(a => a.RemovedAt == null && a.Role == PlatformRole.SuperAdmin).Select(a => a.Id).ToListAsync(Ct));
        await api.WithDbAsync(db => db.PlatformAdmins.Where(a => supers.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedAt, DateTimeOffset.UtcNow), Ct));
        try
        {
            var ok = await api.CreateClient().PostAsJsonAsync("/api/school-demo-requests",
                new { schoolName = school, contactName = "Mrs Ade", email = "ade@example.com", phone = "08031234567", students = 300 }, Ct);
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
            var sent = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.demo_request").ToListAsync(Ct)))
                .Where(n => n.Payload.GetValueOrDefault("school") == school).Select(n => n.Recipient).Order().ToList();
            Assert.Equal(new[] { ApiFixture.AdminEmail, ApiFixture.UnclaimedAdminEmail }.Order(), sent); // Platform:AdminEmails

            // Logged as an error naming the school and contact email (never the phone), whether or not there is an address to fall back on.
            foreach (var configured in new[] { true, false })
            {
                var name = $"Logged Demo School {Guid.NewGuid().ToString("N")[..6]}";
                var log = new ListLogger<SchoolDemoService>();
                using (var scope = api.Services.CreateScope())
                {
                    var sp = scope.ServiceProvider;
                    var service = new SchoolDemoService(sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<NotificationQueue>(),
                        configured ? sp.GetRequiredService<Saas.Application.Auth.PlatformOptions>() : new Saas.Application.Auth.PlatformOptions(), log);
                    await service.RequestAsync(new SchoolDemoRequest(name, "Mrs Ade", "ade@example.com", "08031234567", 300), Ct);
                }
                var entry = Assert.Single(log.Entries);
                Assert.Equal(LogLevel.Error, entry.Level);
                Assert.Contains(name, entry.Message);
                Assert.Contains("ade@example.com", entry.Message);
                Assert.DoesNotContain("08031234567", entry.Message);
                var count = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.demo_request").ToListAsync(Ct)))
                    .Count(n => n.Payload.GetValueOrDefault("school") == name);
                Assert.Equal(configured ? 2 : 0, count);
            }
        }
        finally
        {
            await api.WithDbAsync(db => db.PlatformAdmins.Where(a => supers.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedAt, (DateTimeOffset?)null), Ct));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    // ---------- Admin console ----------

    private string SchoolWebUrl
    {
        get
        {
            var url = api.Services.GetRequiredService<IConfiguration>()["App:ProductWebUrls:school"];
            Assert.False(string.IsNullOrEmpty(url), "App:ProductWebUrls:school is not configured.");
            return url;
        }
    }

    [Fact]
    public async Task School_appears_in_the_console_with_student_and_staff_usage()
    {
        var name = $"Console School {Guid.NewGuid().ToString("N")[..6]}";
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        var leaver = await kit.StudentAsync(school, classId, "Bola");
        await kit.StudentAsync(school, classId, "Chidi");
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{leaver}/status", new { status = "Withdrawn", reason = "Moved away" }, Ct));
        await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        var found = await Json(await support.GetAsync($"/api/platform/businesses?product=school&q={Uri.EscapeDataString(name)}", Ct));
        var row = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal((school.TenantId, "school", "Active"), (row.GetProperty("id").GetGuid(), row.GetProperty("product").GetString(), row.GetProperty("status").GetString()));

        var detail = await Json(await support.GetAsync($"/api/platform/businesses/{school.TenantId}", Ct));
        var usage = detail.GetProperty("usage").EnumerateArray().ToDictionary(u => u.GetProperty("key").GetString()!, u => u.GetProperty("used").GetInt64());
        Assert.Equal(2, usage["students"]); // active students only
        Assert.Equal(2, usage["team_seats"]); // the proprietor and the teacher
        Assert.Equal(SchoolWebUrl, detail.GetProperty("webUrl").GetString());
        Assert.Equal(2, detail.GetProperty("members").GetArrayLength());
    }

    [Fact]
    public async Task School_fee_payments_are_not_platform_payments()
    {
        var started = DateTimeOffset.UtcNow;
        var school = await kit.SchoolAsync($"Fee Money School {Guid.NewGuid().ToString("N")[..6]}");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var item = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}", new { lines = new[] { new { feeItemId = item, amountMinor = 5_000_000 } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));
        var invoiceId = (await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}", Ct))).EnumerateArray().Single().GetProperty("id").GetGuid();
        var reference = $"TRF-{Guid.NewGuid().ToString("N")[..8]}";
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/invoices/{invoiceId}/payments", new { amountMinor = 1_000_000, method = "Transfer", reference, paidOn = Today }, Ct));
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);

        // The control: a plan subscription payment by the same school is a platform payment.
        var subscriptionRef = $"sub_school_{Guid.NewGuid():N}";
        await api.WithDbAsync(async db =>
        {
            var standard = await db.Plans.FirstAsync(p => p.Product == "school" && p.Code == "standard", Ct);
            db.Payments.Add(new Payment
            {
                TenantId = school.TenantId, AmountMinor = standard.PriceMinor, Currency = "NGN", Provider = "paystack", ProviderRef = subscriptionRef,
                Purpose = "subscription", Status = PaymentStatus.Succeeded, PaidAt = DateTimeOffset.UtcNow, Metadata = new() { ["planId"] = standard.Id.ToString() },
            });
            return await db.SaveChangesAsync(Ct);
        });
        Assert.False(await api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().AnyAsync(p => p.ProviderRef == reference, Ct)));

        // Narrowed to this test's minutes (tests run one at a time), so the page size can't hide a row either way.
        var range = $"from={Uri.EscapeDataString(started.AddSeconds(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"))}";
        var payments = (await Json(await finance.GetAsync($"/api/platform/payments?product=school&pageSize=100&{range}", Ct))).GetProperty("items").EnumerateArray().ToList();
        Assert.True(payments.Count < 100, $"{payments.Count} rows: the window is too wide to prove absence.");
        var row = Assert.Single(payments, p => p.GetProperty("reference").GetString() == subscriptionRef);
        Assert.Equal(("Subscription", school.TenantId), (row.GetProperty("kind").GetString(), row.GetProperty("businessId").GetGuid()));
        Assert.DoesNotContain(payments, p => p.GetProperty("reference").GetString() == reference);
        var detail = await Json(await finance.GetAsync($"/api/platform/businesses/{school.TenantId}", Ct));
        var detailPayment = Assert.Single(detail.GetProperty("payments").EnumerateArray());
        Assert.Equal(subscriptionRef, detailPayment.GetProperty("reference").GetString());
    }

    /// <summary>A subject on the class, the current term around today, and full scores for the student: enough for a printable report card.</summary>
    private async Task ScoresAsync(SchoolKit.School s, Guid classId, Guid studentId)
    {
        var subject = await Json(await s.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var classSubject = (await Json(await s.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid() }, Ct))).GetProperty("id").GetGuid();
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == s.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == s.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        var components = (await Json(await s.Owner.GetAsync($"/api/school/scores?classSubjectId={classSubject}&termId={termId}", Ct)))
            .GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        var enrolmentId = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == s.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));
        var entries = components.Zip(new[] { 18m, 17m, 50m }, (c, v) => new { enrolmentId, componentId = c, value = (decimal?)v }).ToArray();
        (await s.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = classSubject, termId, entries }, Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Suspended_school_blocks_staff_and_portals_and_stops_reminders()
    {
        var school = await kit.SchoolAsync($"Suspended School {Guid.NewGuid().ToString("N")[..6]}");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", "08031234567", $"guardian-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);
        var student = await kit.StudentLoginAsync(school, studentId);
        var admissionNo = (await Json(await school.Owner.GetAsync($"/api/school/students/{studentId}", Ct))).GetProperty("admissionNo").GetString();
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");

        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var item = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}", new { lines = new[] { new { feeItemId = item, amountMinor = 5_000_000 } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));
        await api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.TenantId == school.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, Today.AddDays(3)), Ct));
        Task<int> RemindersAsync() => api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.TenantId == school.TenantId && n.TemplateKey == "school.fee_reminder", Ct));
        Task<int> BroadcastsAsync(Guid tenantId) => api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.TenantId == tenantId && n.TemplateKey == "school.announcement", Ct));
        Task<Saas.Domain.School.ReportCardBundle> BundleAsync(Guid id) => api.WithDbAsync(db => db.ReportCardBundles.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == id, Ct));
        async Task<Guid> QueueWorkAsync(SchoolKit.School s, Guid sClassId)
        {
            await Json(await s.Owner.PostAsJsonAsync("/api/school/announcements", new
            {
                title = "Closing early", body = "School closes at noon.", audience = "School", classIds = Array.Empty<Guid>(), levelIds = Array.Empty<Guid>(), emailGuardians = true,
            }, Ct));
            var sTermId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == s.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
            return (await Json(await s.Owner.PostAsync($"/api/school/report-cards/{sClassId}/{sTermId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        }
        var bundleId = await QueueWorkAsync(school, classId);

        // Another school, still active, with the same work waiting: the control for the same job run.
        var active = await kit.SchoolAsync($"Active Neighbour School {Guid.NewGuid().ToString("N")[..6]}");
        var activeClass = await kit.ClassAsync(active);
        var activeStudent = await kit.StudentAsync(active, activeClass, "Bisi");
        await kit.GuardianAsync(active, activeStudent, "Kemi Ade", "08031234568", $"guardian-{Guid.NewGuid():N}@example.com");
        await ScoresAsync(active, activeClass, activeStudent); // so its bundle has a report card to print
        var activeBundleId = await QueueWorkAsync(active, activeClass);
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);

        (await StatusAsync(support, school.TenantId, "suspend")).EnsureSuccessStatusCode();

        // Staff are blocked as in every product.
        foreach (var staff in new[] { school.Owner, teacher })
        {
            var blocked = await staff.GetAsync("/api/school/students", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Contains("suspended", await blocked.Content.ReadAsStringAsync(Ct));
        }

        // Parents and students see the portal message; the school drops out of the parent's list and the public sign-in.
        var child = await parent.GetAsync($"/api/parent/children/{studentId}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, child.StatusCode);
        Assert.Contains(ParentAccess.PortalUnavailable, await child.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync($"/api/parent/children/{studentId}/invoices", Ct)).StatusCode);
        Assert.Equal(0, (await Json(await parent.GetAsync("/api/parent/children", Ct))).GetArrayLength());
        var me = await student.GetAsync("/api/student/me", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Contains(ParentAccess.PortalUnavailable, await me.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/student/overview", Ct)).StatusCode);
        var login = await api.CreateClient().PostAsJsonAsync($"/api/school/{school.Slug}/student-login", new { admissionNo, password = SchoolKit.StudentPassword }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains(ParentAccess.PortalUnavailable, await login.Content.ReadAsStringAsync(Ct));

        // The school job leaves it alone: no reminders, no announcement emails, no report card bundles. The active school's work is done
        // in the same run.
        await RunJobsAsync(Lagos(Today, 9));
        Assert.Equal(0, await RemindersAsync());
        Assert.Equal(0, await BroadcastsAsync(school.TenantId));
        var waiting = await BundleAsync(bundleId);
        Assert.Equal((Saas.Domain.School.ReportCardBundleStatus.Queued, (DateTimeOffset?)null), (waiting.Status, waiting.CompletedAt));
        Assert.Equal(1, await BroadcastsAsync(active.TenantId));
        var built = await BundleAsync(activeBundleId);
        Assert.Equal(Saas.Domain.School.ReportCardBundleStatus.Ready, built.Status);
        Assert.NotNull(built.FileKey);
        Assert.NotNull(built.CompletedAt);

        // Reactivated: everything works again, including the requests refused above, and the waiting work goes out.
        (await StatusAsync(support, school.TenantId, "reactivate")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.GetAsync("/api/school/students", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync("/api/school/students", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await parent.GetAsync($"/api/parent/children/{studentId}", Ct)).StatusCode);
        Assert.Equal(1, (await Json(await parent.GetAsync($"/api/parent/children/{studentId}/invoices", Ct))).GetArrayLength());
        Assert.Equal(1, (await Json(await parent.GetAsync("/api/parent/children", Ct))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/api/student/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/api/student/overview", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().PostAsJsonAsync($"/api/school/{school.Slug}/student-login", new { admissionNo, password = SchoolKit.StudentPassword }, Ct)).StatusCode);
        await RunJobsAsync(Lagos(Today, 9, 5));
        Assert.Equal(1, await RemindersAsync());
        Assert.Equal(1, await BroadcastsAsync(school.TenantId));
        Assert.NotEqual(Saas.Domain.School.ReportCardBundleStatus.Queued, (await BundleAsync(bundleId)).Status);
    }

    [Fact]
    public async Task Support_view_of_a_school_is_read_only()
    {
        var school = await kit.SchoolAsync($"Viewed School {Guid.NewGuid().ToString("N")[..6]}");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        const string reason = "The proprietor says report cards won't download.";

        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{school.TenantId}/view-as", new { reason }, Ct));
        Assert.Equal("school", viewAs.GetProperty("product").GetString());
        Assert.StartsWith($"{SchoolWebUrl}/support#token=", viewAs.GetProperty("webUrl").GetString());
        var view = As(viewAs.GetProperty("token").GetString()!);

        // Reads like the proprietor.
        var students = await Json(await view.GetAsync("/api/school/students", Ct));
        Assert.Contains(students.EnumerateArray(), s => s.GetProperty("id").GetGuid() == studentId);
        Assert.Equal(HttpStatusCode.OK, (await view.GetAsync($"/api/school/students/{studentId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await view.GetAsync("/api/school/settings/paystack", Ct)).StatusCode);

        // Writes nothing.
        foreach (var write in new Func<Task<HttpResponseMessage>>[]
        {
            () => view.PostAsJsonAsync("/api/school/students", new { admissionNo = "SV-1", firstName = "Support", lastName = "View", gender = "Female", classId }, Ct),
            () => view.PutAsJsonAsync("/api/school/settings/profile", new { name = "Renamed by support" }, Ct),
            () => view.DeleteAsync($"/api/school/students/{studentId}", Ct),
            () => view.PostAsJsonAsync($"/api/school/students/{studentId}/login", new { password = "support-pass-1" }, Ct),
            () => view.PostAsync("/api/school/guardians/invite-all", null, Ct),
        })
        {
            var response = await write();
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("read only", await response.Content.ReadAsStringAsync(Ct));
        }
        Assert.Equal(1, await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().CountAsync(s => s.TenantId == school.TenantId, Ct)));
        Assert.Equal(HttpStatusCode.Forbidden, (await view.GetAsync("/api/parent/children", Ct)).StatusCode);

        // The school sees the visit in its account activity.
        var activity = await Json(await school.Owner.GetAsync("/api/account/activity", Ct));
        var entry = Assert.Single(activity.EnumerateArray());
        Assert.Equal(reason, entry.GetProperty("reason").GetString());
    }

    // ---------- Reserved school links ----------

    /// <summary>school-web's own top-level pages; a school with one of these links could never reach its sign-in page at /{slug}.</summary>
    private static readonly string[] SchoolWebRoutes =
        ["login", "signup", "app", "parent", "student", "invite", "pricing", "demo", "forgot-password", "reset-password", "support", "account", "api"];

    private static string? SchoolWebAppFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var app = Path.Combine(dir.FullName, "school-web", "src", "app");
            if (Directory.Exists(app)) return app;
        }
        return null;
    }

    [Fact]
    public async Task Reserved_school_slugs_are_rejected()
    {
        // Every static top-level page of school-web is reserved, so a new page can't silently shadow a school.
        var app = SchoolWebAppFolder();
        Assert.NotNull(app);
        var pages = Directory.GetDirectories(app).Select(Path.GetFileName).Where(n => n is not null && !n.StartsWith('[') && !n.StartsWith('(') && !n.StartsWith('_')).ToList();
        Assert.NotEmpty(pages);
        Assert.All(pages.Concat(SchoolWebRoutes), word => Assert.False(Slugs.IsValid(word!), $"\"{word}\" must be a reserved school link."));

        var client = api.CreateClient();
        Task<HttpResponseMessage> SignupAsync(string businessName, string? slug) => client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Reserved Proprietor", email = $"reserved-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            product = "school", businessName, slug,
        }, Ct);

        // Asking for a reserved link is a validation error on the slug field.
        foreach (var word in SchoolWebRoutes)
        {
            var refused = await SignupAsync("Reserved Word School", word);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
            Assert.True(problem.GetProperty("errors").TryGetProperty("slug", out _), $"{word}: {problem}");
        }

        // A school named after a page gets a different link.
        foreach (var name in new[] { "Parent", "Student", "Demo", "Account" })
        {
            var tenant = (await Json(await SignupAsync(name, null))).GetProperty("tenant");
            var slug = tenant.GetProperty("slug").GetString()!;
            Assert.True(Slugs.IsValid(slug), slug);
            Assert.NotEqual(name.ToLowerInvariant(), slug);
            var profileSlug = await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == tenant.GetProperty("id").GetGuid()).Select(p => p.Slug).SingleAsync(Ct));
            Assert.Equal(slug, profileSlug);
        }

        // A free link that was asked for is used, and the next school can't take it.
        var wanted = $"st-annes-{Guid.NewGuid().ToString("N")[..6]}";
        Assert.Equal(wanted, (await Json(await SignupAsync("St Anne's", wanted))).GetProperty("tenant").GetProperty("slug").GetString());
        var taken = await SignupAsync("St Anne's Two", wanted);
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.True((await taken.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("errors").TryGetProperty("slug", out _));
    }

    /// <summary>Signs up a business in the given product and returns the owner's signed-in client and the tenant id.</summary>
    private async Task<(HttpClient Owner, Guid TenantId)> BusinessAsync(string product, string name)
    {
        var client = api.CreateClient();
        var auth = await Json(await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Grandfathered Owner", email = $"gf-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            product, businessName = name, storeName = name, whatsappNumber = "+2348099999999",
        }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    /// <summary>The settings as read, with only the slug changed: what the settings page sends on save.</summary>
    private static async Task<Dictionary<string, JsonElement>> SettingsWithSlugAsync(HttpClient owner, string path, string slug)
    {
        var current = (await Json(await owner.GetAsync(path, Ct))).Deserialize<Dictionary<string, JsonElement>>()!;
        current["slug"] = JsonSerializer.SerializeToElement(slug);
        return current;
    }

    [Fact]
    public async Task Existing_reserved_slugs_are_grandfathered_on_settings_saves()
    {
        // A store, a booking business, an academy and an agency that already had "demo" before it was reserved.
        var (store, storeTenant) = await BusinessAsync("whatsapp-orders", $"Grandfathered Shop {Guid.NewGuid().ToString("N")[..6]}");
        var (salon, salonTenant) = await BusinessAsync("bookings", $"Grandfathered Salon {Guid.NewGuid().ToString("N")[..6]}");
        var (tutor, academyTenant) = await BusinessAsync("courses", $"Grandfathered Academy {Guid.NewGuid().ToString("N")[..6]}");
        var (agent, agencyTenant) = await BusinessAsync("property", $"Grandfathered Agency {Guid.NewGuid().ToString("N")[..6]}");
        await api.WithDbAsync(db => db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == storeTenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.Slug, "demo"), Ct));
        await api.WithDbAsync(db => db.BookingBusinesses.IgnoreQueryFilters().Where(b => b.TenantId == salonTenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.Slug, "demo"), Ct));
        await api.WithDbAsync(db => db.Academies.IgnoreQueryFilters().Where(a => a.TenantId == academyTenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.Slug, "demo"), Ct));
        await api.WithDbAsync(db => db.AgencyProfiles.IgnoreQueryFilters().Where(a => a.TenantId == agencyTenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.Slug, "demo"), Ct));

        foreach (var (owner, path) in new[] { (store, "/api/store"), (salon, "/api/booking-business"), (tutor, "/api/academy"), (agent, "/api/property/profile/agency") })
        {
            // Saving with the unchanged link works.
            var same = await owner.PutAsJsonAsync(path, await SettingsWithSlugAsync(owner, path, "demo"), Ct);
            Assert.True(same.IsSuccessStatusCode, $"{path}: {(int)same.StatusCode} {await same.Content.ReadAsStringAsync(Ct)}");

            // Moving to another reserved word is refused on the slug field.
            var moved = await owner.PutAsJsonAsync(path, await SettingsWithSlugAsync(owner, path, "parent"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
            Assert.True((await moved.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("errors").TryGetProperty("slug", out _), path);

            // Format rules still apply to an unchanged link.
            var badFormat = await owner.PutAsJsonAsync(path, await SettingsWithSlugAsync(owner, path, "Demo Shop!"), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, badFormat.StatusCode);
        }
        Assert.Equal("demo", await api.WithDbAsync(db => db.Stores.IgnoreQueryFilters().Where(s => s.TenantId == storeTenant).Select(s => s.Slug).SingleAsync(Ct)));
        Assert.Equal("demo", await api.WithDbAsync(db => db.BookingBusinesses.IgnoreQueryFilters().Where(b => b.TenantId == salonTenant).Select(b => b.Slug).SingleAsync(Ct)));
        Assert.Equal("demo", await api.WithDbAsync(db => db.Academies.IgnoreQueryFilters().Where(a => a.TenantId == academyTenant).Select(a => a.Slug).SingleAsync(Ct)));
        Assert.Equal("demo", await api.WithDbAsync(db => db.AgencyProfiles.IgnoreQueryFilters().Where(a => a.TenantId == agencyTenant).Select(a => a.Slug).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Signup_slug_is_only_for_schools()
    {
        var wanted = $"my-invoices-{Guid.NewGuid().ToString("N")[..6]}";
        var refused = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Invoice Owner", email = $"inv-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
            product = "invoices", businessName = "My Invoices", slug = wanted,
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.True((await refused.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("errors").TryGetProperty("slug", out _));
        Assert.False(await api.WithDbAsync(db => db.Tenants.AnyAsync(t => t.Slug == wanted, Ct)));
    }

    [Fact]
    public async Task Signup_slug_held_by_a_school_profile_is_a_field_error()
    {
        // A school profile whose link no tenant holds: the link check covers product profiles too, so the asked-for link is refused on the
        // slug field with nothing saved (the insert-time catch is now only a race backstop; see Signup_link_race_names_the_field_the_signup_has).
        var other = await kit.SchoolAsync($"Stale Link School {Guid.NewGuid().ToString("N")[..6]}");
        var stale = $"stale-link-{Guid.NewGuid().ToString("N")[..6]}";
        await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == other.TenantId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, stale), Ct));
        var email = $"race-{Guid.NewGuid():N}@example.com";

        var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = "Race Proprietor", email, password = "password123", country = "NG", product = "school", businessName = "Race School", slug = stale,
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.Equal("That link is taken. Choose another.", problem.GetProperty("errors").GetProperty("slug")[0].GetString());
        Assert.False(await api.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email, Ct))); // nothing half-saved
    }

    [Fact]
    public async Task Signup_skips_links_that_a_product_changed_to()
    {
        // A booking business, an academy and an agency move their own link to X through settings (the tenant keeps its old one); a
        // school profile holds X directly (schools have no link change yet). A new signup whose name makes X gets another free link.
        foreach (var product in new[] { "bookings", "courses", "property", "school" })
        {
            var suffix = Guid.NewGuid().ToString("N")[..6];
            var taken = $"moved-link-{suffix}";
            var (holder, holderTenant) = await BusinessAsync(product, $"Link Holder {suffix}");
            switch (product)
            {
                case "bookings": await MoveLinkAsync(holder, "/api/booking-business", taken); break;
                case "courses": await MoveLinkAsync(holder, "/api/academy", taken); break;
                case "property": await MoveLinkAsync(holder, "/api/property/profile/agency", taken); break;
                default:
                    await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == holderTenant).ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, taken), Ct));
                    break;
            }
            Assert.False(await api.WithDbAsync(db => db.Tenants.AnyAsync(t => t.Slug == taken, Ct)), product); // only the product holds it

            var response = await api.CreateClient().PostAsJsonAsync("/api/auth/signup", new
            {
                fullName = "Second Owner", email = $"second-{Guid.NewGuid():N}@example.com", password = "password123", country = "NG",
                product, businessName = $"Moved Link {suffix}",
            }, Ct);

            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(response.IsSuccessStatusCode, $"{product}: {(int)response.StatusCode} {body}");
            var slug = JsonDocument.Parse(body).RootElement.GetProperty("tenant").GetProperty("slug").GetString()!;
            Assert.StartsWith($"{taken}-", slug);
            Assert.True(Slugs.IsValid(slug), slug);
        }

        async Task MoveLinkAsync(HttpClient owner, string path, string slug)
        {
            var moved = await owner.PutAsJsonAsync(path, await SettingsWithSlugAsync(owner, path, slug), Ct);
            Assert.True(moved.IsSuccessStatusCode, $"{path}: {(int)moved.StatusCode} {await moved.Content.ReadAsStringAsync(Ct)}");
        }
    }

    [Fact]
    public void Signup_link_race_names_the_field_the_signup_has()
    {
        // The unique-violation backstop can only be reached by two signups racing, so its message choice is checked directly.
        static (string Field, string Message) Of(Saas.Application.Auth.SignupRequest r)
        {
            var errors = Saas.Infrastructure.Auth.AuthService.SlugTaken(r).Errors!;
            var (field, messages) = Assert.Single(errors);
            return (field, Assert.Single(messages));
        }
        const string rename = "We couldn't set up your account with that business name. Try a slightly different name.";
        Assert.Equal(("slug", "That link is taken. Choose another."),
            Of(new("Ada", "a@example.com", "password123", "NG", Product: "school", BusinessName: "St Anne's", Slug: "st-annes")));
        Assert.Equal(("businessName", rename), Of(new("Ada", "a@example.com", "password123", "NG", Product: "school", BusinessName: "St Anne's")));
        Assert.Equal(("businessName", rename), Of(new("Ada", "a@example.com", "password123", "NG", Product: "bookings", BusinessName: "Ada's Salon")));
        Assert.Equal(("storeName", rename), Of(new("Ada", "a@example.com", "password123", "NG", StoreName: "Ada's Shop", WhatsappNumber: "08031234567")));
    }

    [Fact]
    public async Task Demo_request_refuses_control_characters()
    {
        var anon = api.CreateClient();
        foreach (var body in new object[]
        {
            new { schoolName = "Bright Future\nBcc: someone@example.com", contactName = "Mrs Ade", email = "ade@example.com", phone = "08031234567", students = 300 },
            new { schoolName = "Bright Future", contactName = "Mrs Ade\r", email = "ade@example.com", phone = "08031234567", students = 300 },
            new { schoolName = "Bright Future", contactName = "Mrs Ade", email = "ade@example.com", phone = "0803\t123 4567", students = 300 },
            new { schoolName = "Bright Future", contactName = "Mrs Ade", email = "ade@example.com", phone = "0803 123 4567 ext. 2", students = 300 },
        })
        {
            var response = await anon.PostAsJsonAsync("/api/school-demo-requests", body, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var email in new[] { "ade\n@example.com", "ade@example.com\r", "ade@exam\u0000ple.com", "ade\u007f@example.com" })
        {
            var response = await anon.PostAsJsonAsync("/api/school-demo-requests",
                new { schoolName = "Bright Future", contactName = "Mrs Ade", email, phone = "08031234567", students = 300 }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("email", out var messages), $"{JsonSerializer.Serialize(email)}: {errors}");
            Assert.Contains("Write your email address on one line.", messages.EnumerateArray().Select(m => m.GetString()));
        }
        var ok = await anon.PostAsJsonAsync("/api/school-demo-requests",
            new { schoolName = "Bright Future", contactName = "Mrs Ade", email = "ade@example.com", phone = "+234 (803) 123-4567", students = 300 }, Ct);
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Bursar_cannot_read_a_students_attendance()
    {
        var school = await kit.SchoolAsync($"Bursar Attendance School {Guid.NewGuid().ToString("N")[..6]}");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/students/{studentId}/attendance", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.GetAsync($"/api/school/students/{studentId}/attendance", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bursar.GetAsync($"/api/school/students/{studentId}", Ct)).StatusCode); // students stay readable
    }

    // ---------- Role table ----------

    // Owner, Admin, Accountant (bursar), Staff (teacher). Y: the route serves the role (2xx, or 400/402/404/409/422 for made-up input). N: 403.
    // S: the route lets the role through and the service limits it to its own classes, threads or posts (per-area tests cover the
    // record checks); with made-up ids the test asserts what Y allows, or a 403 from the service's record check (not the route's role
    // check). Never 401, 3xx, 405, 415, 429 or 5xx.
    private const string Admin = "YYNN", Reports = "YYYN", OwnerOnly = "YNNN", Everyone = "YYYY";

    /// <summary>"METHOD pattern" → expected outcome for Owner, Admin, Accountant and Staff. Parent and student tokens: always 403.</summary>
    private static readonly Dictionary<string, string> Table = new()
    {
        ["GET /api/school/setup"] = Everyone,
        ["POST /api/school/setup/apply-template"] = Admin,
        ["GET /api/school/levels"] = Everyone,
        ["POST /api/school/levels"] = Admin,
        ["PUT /api/school/levels/{id:guid}"] = Admin,
        ["DELETE /api/school/levels/{id:guid}"] = Admin,
        ["GET /api/school/classes"] = Everyone,
        ["POST /api/school/classes"] = Admin,
        ["PUT /api/school/classes/{id:guid}"] = Admin,
        ["DELETE /api/school/classes/{id:guid}"] = Admin,
        ["GET /api/school/subjects"] = Everyone,
        ["POST /api/school/subjects"] = Admin,
        ["PUT /api/school/subjects/{id:guid}"] = Admin,
        ["DELETE /api/school/subjects/{id:guid}"] = Admin,
        ["GET /api/school/classes/{id:guid}/subjects"] = Everyone,
        ["POST /api/school/classes/{id:guid}/subjects"] = Admin,
        ["PUT /api/school/classes/{id:guid}/subjects/{subjectId:guid}"] = Admin,
        ["DELETE /api/school/classes/{id:guid}/subjects/{subjectId:guid}"] = Admin,
        ["GET /api/school/assessment"] = Everyone,
        ["PUT /api/school/assessment"] = Admin,
        ["DELETE /api/school/assessment/{levelId:guid}"] = Admin,
        ["GET /api/school/traits"] = Everyone,
        ["PUT /api/school/traits"] = Admin,
        ["GET /api/school/developmental/areas"] = Everyone,
        ["PUT /api/school/developmental/areas"] = Admin,
        ["GET /api/school/developmental/scale"] = Everyone,
        ["PUT /api/school/developmental/scale"] = Admin,
        ["GET /api/school/grading"] = Everyone,
        ["PUT /api/school/grading"] = Admin,
        ["DELETE /api/school/grading/{levelId:guid}"] = Admin,

        ["GET /api/school/staff"] = Admin,
        ["PUT /api/school/staff/{membershipId:guid}"] = Admin,
        ["PUT /api/school/classes/{id:guid}/class-teacher"] = Admin,
        ["PUT /api/school/class-subjects/{id:guid}/teacher"] = Admin,
        ["GET /api/school/my-classes"] = Everyone,
        ["GET /api/school/staff/me"] = Everyone,
        ["GET /api/school/timetable/{classId:guid}"] = "YYNS",
        ["PUT /api/school/timetable/{classId:guid}"] = Admin,

        ["GET /api/school/students"] = "YYYS",
        ["POST /api/school/students"] = Admin,
        ["GET /api/school/students/{id:guid}"] = "YYYS",
        ["PUT /api/school/students/{id:guid}"] = Admin,
        ["DELETE /api/school/students/{id:guid}"] = Admin,
        ["POST /api/school/students/{id:guid}/photo"] = Admin,
        ["POST /api/school/students/{id:guid}/transfer"] = Admin,
        ["POST /api/school/students/{id:guid}/status"] = Admin,
        ["POST /api/school/students/{id:guid}/guardians"] = Admin,
        ["PUT /api/school/students/{id:guid}/guardians/{guardianId:guid}"] = Admin,
        ["POST /api/school/students/{id:guid}/guardians/{guardianId:guid}/primary"] = Admin,
        ["DELETE /api/school/students/{id:guid}/guardians/{guardianId:guid}"] = Admin,
        ["POST /api/school/students/{id:guid}/login"] = Admin,
        ["POST /api/school/guardians/{id:guid}/invite"] = Admin,
        ["POST /api/school/guardians/invite-all"] = Admin,
        ["GET /api/school/guardians"] = Reports,
        ["GET /api/school/students/import/template.xlsx"] = Admin,
        ["GET /api/school/students/import/template.csv"] = Admin,
        ["POST /api/school/students/import/preview"] = Admin,
        ["POST /api/school/students/import/{token}/confirm"] = Admin,
        // Generic imports, checked with the students kind (leadership only). Each kind's own access check refuses before anything is looked up.
        ["GET /api/school/imports/{kind}/template.xlsx"] = Admin,
        ["GET /api/school/imports/{kind}/template.csv"] = Admin,
        ["POST /api/school/imports/{kind}/preview"] = Admin,
        // A batch belongs to the user who previewed it (who passed the kind's check), so a made-up token is 404 for every role.
        ["POST /api/school/imports/{token}/confirm"] = "YYSS",
        ["GET /api/school/students/id-cards.pdf"] = Reports,
        ["GET /api/school/promotion/preview"] = Admin,
        ["POST /api/school/promotion"] = Admin,

        ["GET /api/school/attendance/summary"] = Admin,
        ["GET /api/school/attendance/{classId:guid}/{date}"] = "YYNS",
        ["PUT /api/school/attendance/{classId:guid}/{date}"] = "YYNS",
        ["GET /api/school/students/{id:guid}/attendance"] = "YYNS", // the spec gives the bursar no attendance access
        ["GET /api/school/students/{id:guid}/traits"] = "YYNY", // class teacher or leadership; a subject teacher gets 404
        ["PUT /api/school/students/{id:guid}/traits"] = "YYNY",
        ["GET /api/school/students/{id:guid}/developmental"] = "YYNY", // class teacher or leadership; the bursar is refused before the student is looked up
        ["PUT /api/school/students/{id:guid}/developmental"] = "YYNY",


        ["GET /api/school/assignments"] = "YYNS", // subject teacher or leadership; the bursar is refused before the class subject is looked up
        ["POST /api/school/assignments"] = "YYNS",
        ["PUT /api/school/assignments/{id:guid}/submissions/{enrolmentId:guid}"] = "YYNS",
        ["GET /api/school/lesson-plans"] = "YYNY", // subject teacher or leadership; a teacher who does not teach it gets 404; the bursar is refused before lookup
        ["POST /api/school/lesson-plans"] = "YYNY",
        ["GET /api/school/lesson-plans/{id:guid}"] = "YYNY",
        ["PUT /api/school/lesson-plans/{id:guid}"] = "YYNY",
        ["POST /api/school/lesson-plans/{id:guid}/submit"] = "YYNY",
        ["POST /api/school/lesson-plans/{id:guid}/accept"] = "YYNY",
        ["POST /api/school/lesson-plans/{id:guid}/return"] = "YYNY",
        ["GET /api/school/exam-periods"] = "YYNY", // subject teacher or leadership; unpublished periods stay off the teacher's list; the bursar is refused before lookup
        ["POST /api/school/exam-periods"] = Admin,
        ["GET /api/school/exam-periods/{id:guid}"] = "YYNY", // a teacher gets 404 until the period is published, and then only sittings for subjects they teach
        ["POST /api/school/exam-periods/{id:guid}/sittings"] = Admin,
        ["POST /api/school/exam-periods/{id:guid}/publish"] = Admin,
        ["GET /api/school/exam-sittings/{id:guid}"] = "YYNY",
        ["PUT /api/school/exam-sittings/{id:guid}"] = Admin,
        ["DELETE /api/school/exam-sittings/{id:guid}"] = Admin,
        ["GET /api/school/questions"] = "YYNY", // subject teacher or leadership; a teacher of another subject gets 404; the bursar is refused before lookup
        ["POST /api/school/questions"] = "YYNY",
        ["POST /api/school/questions/{id:guid}/archive"] = "YYNY", // a teacher of another subject gets 404; the bursar is refused before lookup
        ["POST /api/school/questions/{id:guid}/unarchive"] = "YYNY",
        ["GET /api/school/cbt/papers"] = "YYNY", // subject teacher of that class subject or leadership; another teacher gets 404; the bursar is refused before lookup
        ["GET /api/school/cbt/papers/{id:guid}/attempts"] = "YYNY", // same access as release and reopen
        ["POST /api/school/cbt/papers"] = "YYNY", // subject teacher of that class subject or leadership; a developmental class is refused
        ["POST /api/school/cbt/papers/{id:guid}/publish"] = "YYNY",
        ["POST /api/school/cbt/papers/{id:guid}/release"] = "YYNY", // subject teacher of that class subject or leadership; the bursar is refused before lookup
        ["POST /api/school/cbt/attempts/{id:guid}/reopen"] = "YYNY",
        ["GET /api/school/play/words"] = "YYNY", // a teacher sees school and level lists and those of class subjects they teach; the bursar is refused
        ["POST /api/school/play/words"] = "YYNY", // a teacher or leadership; a class-subject list needs its teacher; the bursar is refused before lookup
        ["GET /api/school/play/teasers"] = "YYNY", // staff read the answer too; the bursar is refused
        ["POST /api/school/play/teasers"] = "YYNY",
        ["GET /api/school/scores"] = "YYNS",
        ["PUT /api/school/scores"] = "YYNS",
        ["POST /api/school/results/{classId:guid}/{termId:guid}/lock"] = Admin,
        ["POST /api/school/results/{classId:guid}/{termId:guid}/unlock"] = Admin,
        ["GET /api/school/comment-bank"] = Everyone,
        ["PUT /api/school/comment-bank"] = Admin,
        ["GET /api/school/results/{classId:guid}/{termId:guid}"] = "YYNS",
        ["GET /api/school/results/{classId:guid}/{termId:guid}/broadsheet.pdf"] = Admin,
        ["GET /api/school/results/{classId:guid}/{termId:guid}/broadsheet.csv"] = Admin,
        ["GET /api/school/results/students/{studentId:guid}"] = "YYNY", // a teacher who does not teach the child gets 404; the bursar is refused first
        ["GET /api/school/results/{classId:guid}/{termId:guid}/suggestions"] = "YYNY", // a teacher who does not teach the subject gets 404
        ["PUT /api/school/results/{classId:guid}/{termId:guid}/remarks"] = "YYNS",
        ["PUT /api/school/results/{classId:guid}/{termId:guid}/subject-comments"] = "YYNY",
        ["POST /api/school/results/{classId:guid}/{termId:guid}/compute"] = Admin,
        ["POST /api/school/results/{classId:guid}/{termId:guid}/publish"] = Admin,
        ["POST /api/school/results/{classId:guid}/{termId:guid}/unpublish"] = Admin,
        ["GET /api/school/report-cards/{enrolmentId:guid}/{termId:guid}.pdf"] = "YYNS",
        ["POST /api/school/report-cards/{classId:guid}/{termId:guid}/bundle"] = "YYNS",
        ["GET /api/school/report-cards/bundles/{id:guid}"] = "YYNS",

        ["GET /api/school/settings/lesson-plans"] = Admin,
        ["PUT /api/school/settings/lesson-plans"] = Admin,
        ["GET /api/school/settings/play"] = Admin,
        ["PUT /api/school/settings/play"] = Admin,
        ["GET /api/school/settings/profile"] = Admin,
        ["PUT /api/school/settings/profile"] = Admin,
        ["PUT /api/school/settings/logo"] = Admin,
        ["DELETE /api/school/settings/logo"] = Admin,
        ["PUT /api/school/settings/signature"] = Admin,
        ["DELETE /api/school/settings/signature"] = Admin,
        ["GET /api/school/settings/paystack"] = OwnerOnly,
        ["PUT /api/school/settings/paystack"] = OwnerOnly,
        ["DELETE /api/school/settings/paystack"] = OwnerOnly,

        ["GET /api/school/fee-items"] = Reports,
        ["POST /api/school/fee-items"] = Reports,
        ["PUT /api/school/fee-items/{id:guid}"] = Reports,
        ["DELETE /api/school/fee-items/{id:guid}"] = Reports,
        ["GET /api/school/fee-structure/{termId:guid}"] = Reports,
        ["PUT /api/school/fee-structure/{termId:guid}/{levelId:guid}"] = Reports,
        ["POST /api/school/fee-structure/{termId:guid}/copy-from/{fromTermId:guid}"] = Reports,
        ["POST /api/school/invoices/generate"] = Reports,
        ["GET /api/school/invoices"] = Reports,
        ["GET /api/school/invoices/{id:guid}"] = Reports,
        ["PUT /api/school/invoices/{id:guid}/optional-items"] = Reports,
        ["POST /api/school/invoices/{id:guid}/discount"] = Reports,
        ["PUT /api/school/invoices/{id:guid}/instalments"] = Reports,
        ["POST /api/school/invoices/{id:guid}/payments"] = Reports,
        ["POST /api/school/payments/{id:guid}/reverse"] = Reports,
        ["GET /api/school/payments/{id:guid}/receipt.pdf"] = Reports,
        ["POST /api/school/invoices/{id:guid}/cancel"] = Reports,
        ["POST /api/school/invoices/{id:guid}/reviewed"] = Reports,
        ["GET /api/school/debtors"] = Reports,
        ["GET /api/school/debtors/whatsapp"] = Reports,
        ["GET /api/school/debtors.csv"] = Reports,
        ["GET /api/school/finance/summary"] = Reports,
        ["GET /api/school/finance/collections"] = Reports,
        ["GET /api/school/settings/finance"] = Reports,
        ["PUT /api/school/settings/finance"] = Admin,
        ["GET /api/school/expenses"] = Reports,
        ["GET /api/school/expenses/categories"] = Reports,
        ["GET /api/school/expenses/{id:guid}"] = Reports,
        ["POST /api/school/expenses"] = Reports,
        ["PUT /api/school/expenses/{id:guid}"] = Reports,
        ["DELETE /api/school/expenses/{id:guid}"] = Reports,
        ["POST /api/school/expenses/{id:guid}/receipt"] = Reports,
        ["DELETE /api/school/expenses/{id:guid}/receipt"] = Reports,

        ["GET /api/school/announcements"] = Everyone,
        ["POST /api/school/announcements"] = "YYYS",
        ["DELETE /api/school/announcements/{id:guid}"] = "YYYS",
        ["POST /api/school/announcements/{id:guid}/attachment"] = "YYYS",
        ["GET /api/school/threads"] = "YYYS",
        ["GET /api/school/threads/{id:guid}/messages"] = "YYYS",
        ["POST /api/school/threads/{id:guid}/messages"] = "YYYS",
        ["POST /api/school/threads/{id:guid}/messages/image"] = "YYYS",
    };

    /// <summary>Public routes (the school's sign-in page and signed file links): no token at all, so no role decides.</summary>
    private static readonly HashSet<string> Exempt =
    [
        "POST /api/school/{slug}/student-login",
        "GET /api/school/{slug}/public",
        "GET /api/school/photos/{id:guid}/{expires:long}/{signature}",
        "GET /api/school/report-cards/bundles/{id:guid}/{expires:long}/{signature}",
        "GET /api/school/logos/{tenantId:guid}/{expires:long}/{signature}",
        "GET /api/school/expense-receipts/{id:guid}/{expires:long}/{signature}",
        "GET /api/school/announcement-files/{id:guid}/{expires:long}/{signature}",
        "GET /api/school/assignment-files/{id:guid}/{expires:long}/{signature}",
        "GET /api/school/message-images/{id:guid}/{expires:long}/{signature}",
    ];

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    /// <summary>Every signed-in school route (and the public ones too when asked); <c>Form</c> marks file uploads (they only match a
    /// multipart request).</summary>
    private List<(string Method, string Pattern, bool Form)> SchoolRoutes(bool withExempt = false)
    {
        _ = api.CreateClient();
        return api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && p.StartsWith("/api/school/"))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, e.RoutePattern.RawText!,
                e.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true)))
            .Where(r => withExempt || !Exempt.Contains($"{r.m} {r.Item2}")).Distinct().ToList();
    }

    private static HttpRequestMessage Request(string method, string path, bool form, object? body = null) => new(new HttpMethod(method), path)
    {
        Content = form ? new MultipartFormDataContent { { new StringContent("x"), "body" } }
            : method is "POST" or "PUT" ? JsonContent.Create(body ?? new { }) : null,
    };

    [Fact]
    public async Task School_routes_need_a_token_except_the_public_ones()
    {
        var routes = SchoolRoutes(withExempt: true);
        Assert.All(Exempt, e => Assert.Contains(e, routes.Select(r => $"{r.Method} {r.Pattern}"))); // the exempt list names real routes
        var anon = api.CreateClient();
        var failures = new List<string>();

        foreach (var (method, pattern, form) in routes)
        {
            var path = Parameter().Replace(pattern, m => m.Value switch
            {
                var v when v.Contains(":guid") => Guid.NewGuid().ToString(),
                var v when v.Contains(":int") || v.Contains(":long") => "0",
                "{date}" => Today.ToString("yyyy-MM-dd"),
                _ => "x",
            });
            using var request = Request(method, path, form);
            var status = (int)(await anon.SendAsync(request, Ct)).StatusCode;
            var exempt = Exempt.Contains($"{method} {pattern}");
            if (exempt ? status is 401 or >= 500 : status != 401)
                failures.Add($"{method} {pattern} → {status}, expected {(exempt ? "no 401 (a public route)" : "401")}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task School_role_table()
    {
        var routes = SchoolRoutes();
        var missing = routes.Select(r => $"{r.Method} {r.Pattern}").Where(k => !Table.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Decide who may use: " + string.Join(", ", missing));
        var stale = Table.Keys.Except(routes.Select(r => $"{r.Method} {r.Pattern}")).ToList();
        Assert.True(stale.Count == 0, "No longer routes: " + string.Join(", ", stale));

        var school = await kit.SchoolAsync($"Role Table School {Guid.NewGuid().ToString("N")[..6]}");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", "08031234567", $"guardian-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);
        var student = await kit.StudentLoginAsync(school, studentId);
        var subjectId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var classSubjectId = await api.WithDbAsync(db => db.ClassSubjects.IgnoreQueryFilters().Where(c => c.ClassId == classId).Select(c => c.Id).SingleAsync(Ct));
        var enrolmentId = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.StudentId == studentId).Select(e => e.Id).SingleAsync(Ct));
        var bundleId = (await Json(await school.Owner.PostAsync($"/api/school/report-cards/{classId}/{termId}/bundle", null, Ct))).GetProperty("id").GetGuid();

        // Routes whose service checks the record before the role get real records, so a refusal is a real 403 and not a 404. Deletes
        // and other writes by {id} keep made-up ids, so the school's records survive the run.
        string Fill(string method, string pattern) => Parameter().Replace(pattern, m =>
        {
            var before = pattern[..m.Index];
            return m.Value switch
            {
                var v when v.Contains(":int") || v.Contains(":long") => "0",
                "{classId:guid}" => classId.ToString(),
                "{termId:guid}" => termId.ToString(),
                "{enrolmentId:guid}" => enrolmentId.ToString(),
                "{kind}" => "students",
                "{id:guid}" when method == "GET" && before == "/api/school/students/" => studentId.ToString(),
                "{id:guid}" when method == "GET" && before == "/api/school/classes/" => classId.ToString(),
                "{id:guid}" when method == "GET" && before == "/api/school/report-cards/bundles/" => bundleId.ToString(),
                var v when v.Contains(":guid") => Guid.NewGuid().ToString(),
                "{date}" => Today.ToString("yyyy-MM-dd"),
                _ => "x",
            };
        });
        var queries = new Dictionary<string, string>
        {
            ["GET /api/school/scores"] = $"?classSubjectId={classSubjectId}&termId={termId}",
            ["GET /api/school/assignments"] = $"?classSubjectId={classSubjectId}&termId={termId}",
            ["GET /api/school/lesson-plans"] = $"?classSubjectId={classSubjectId}&termId={termId}",
            ["GET /api/school/exam-periods"] = $"?termId={termId}",
            ["GET /api/school/cbt/papers"] = $"?classSubjectId={classSubjectId}&termId={termId}",
            ["GET /api/school/results/{classId:guid}/{termId:guid}/suggestions"] = $"?enrolmentId={enrolmentId}&classSubjectId={classSubjectId}",
            ["GET /api/school/results/students/{studentId:guid}"] = $"?termId={termId}",
        };
        var bodies = new Dictionary<string, object>
        {
            ["PUT /api/school/scores"] = new { classSubjectId, termId, entries = Array.Empty<object>() },
            ["PUT /api/school/results/{classId:guid}/{termId:guid}/subject-comments"] = new { enrolmentId, classSubjectId, comment = "A trial comment." },
        };
        var roles = new (string Name, HttpClient Client)[]
        {
            ("Owner", school.Owner),
            ("Admin", (await kit.StaffAsync(school, MemberRole.Admin, "Principal")).Client),
            ("Accountant", (await kit.StaffAsync(school, MemberRole.Accountant, "Bursar")).Client),
            ("Staff", (await kit.StaffAsync(school, MemberRole.Staff, "Teacher")).Client),
            ("parent token", parent),
            ("student token", student),
        };
        var failures = new List<string>();

        foreach (var (method, pattern, form) in routes)
        {
            var key = $"{method} {pattern}";
            var expected = Table[key];
            var path = Fill(method, pattern) + queries.GetValueOrDefault(key, "");
            for (var i = 0; i < roles.Length; i++)
            {
                var (role, client) = roles[i];
                var want = i < 4 ? expected[i] : 'N';
                using var request = Request(method, path, form, bodies.GetValueOrDefault(key));
                var response = await client.SendAsync(request, Ct);
                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(Ct);
                var routeRefused = response.StatusCode == HttpStatusCode.Forbidden && body.Contains("You need the");
                var served = status is (>= 200 and < 300) or 400 or 402 or 404 or 409 or 422;
                var problem = want switch
                {
                    'Y' when !served => "expected access (2xx, or 400/402/404/409/422 for the made-up input)",
                    'S' when !(served || (status == 403 && !routeRefused)) => "expected the route to let it through (a record check may still refuse)",
                    'N' when status != 403 => "expected 403",
                    _ => null,
                };
                if (problem is not null) failures.Add($"{role} {method} {pattern} → {status}, {problem} {(status >= 400 ? body[..Math.Min(body.Length, 160)] : "")}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
