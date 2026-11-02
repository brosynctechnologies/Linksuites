using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Fixes from the SchoolLink client/server recheck (.superpowers/recheck/school.md). Each test is named after its finding.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolRecheckFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private readonly SchoolKit kit = new(api);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed record Fees(SchoolKit.School School, Guid ClassId, Guid LevelId, Guid TermId, Guid Tuition, Guid Uniform);

    /// <summary>A school with one JSS 1 class and a current-term structure of ₦100,000 tuition and ₦20,000 uniform.</summary>
    private async Task<Fees> FeesAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await TermIdAsync(school);
        var tuition = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        var uniform = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Uniform", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        var f = new Fees(school, classId, levelId, termId, tuition, uniform);
        await StructureAsync(f, 10_000_000, 2_000_000);
        return f;
    }

    private static async Task StructureAsync(Fees f, long tuition, long uniform)
    {
        var lines = new List<object>();
        if (tuition > 0) lines.Add(new { feeItemId = f.Tuition, amountMinor = tuition });
        if (uniform > 0) lines.Add(new { feeItemId = f.Uniform, amountMinor = uniform });
        await Json(await f.School.Owner.PutAsJsonAsync($"/api/school/fee-structure/{f.TermId}/{f.LevelId}", new { lines }, Ct));
    }

    private static async Task<JsonElement> GenerateAsync(Fees f) =>
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = f.TermId }, Ct));

    private static async Task<Guid> InvoiceOfAsync(Fees f, Guid studentId) =>
        (await Json(await f.School.Owner.GetAsync($"/api/school/invoices?termId={f.TermId}", Ct))).EnumerateArray()
            .Single(i => i.GetProperty("studentId").GetGuid() == studentId).GetProperty("id").GetGuid();

    private static async Task<JsonElement> InvoiceAsync(Fees f, Guid id) => await Json(await f.School.Owner.GetAsync($"/api/school/invoices/{id}", Ct));

    private static Guid[] Ids(JsonElement result, string property) =>
        result.GetProperty(property).EnumerateArray().Select(e => e.GetGuid()).ToArray();

    private Task<Guid> TermIdAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

    private Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    private Task SetTermAroundTodayAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct));

    /// <summary>Gives every listed student 10 in each component of a new Mathematics class subject for the current term.</summary>
    private async Task ScoreAsync(SchoolKit.School school, Guid classId, Guid termId, params Guid[] studentIds)
    {
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var math = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid() }, Ct))).GetProperty("id").GetGuid();
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={math}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        var entries = new List<object>();
        foreach (var studentId in studentIds)
        {
            var enrolmentId = await EnrolmentAsync(school, studentId);
            entries.AddRange(components.Select(componentId => (object)new { enrolmentId, componentId, value = (decimal?)10 }));
        }
        (await school.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = math, termId, entries }, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await Json(await client.GetAsync("/api/me/notifications/unread-count", Ct))).GetProperty("count").GetInt32();

    // ---------- 3. Generate flags the invoices it asks the bursar to check ----------

    [Fact]
    public async Task Sc3_Generate_flags_an_invoice_whose_discount_no_longer_fits_until_it_is_reviewed()
    {
        var f = await FeesAsync("Sc3 Review Flag School");
        var ada = await kit.StudentAsync(f.School, f.ClassId, "Ada");
        await GenerateAsync(f);
        var id = await InvoiceOfAsync(f, ada);
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Amount", value = 11_000_000, reason = "Scholarship" }, Ct));

        // The structure drops tuition: the ₦110,000 discount no longer fits and is left out.
        await StructureAsync(f, 0, 500_000);
        var result = await GenerateAsync(f);
        Assert.Equal([id], Ids(result, "needsReview"));
        // The invoice page shows the warning and the Mark reviewed button.
        Assert.True((await InvoiceAsync(f, id)).GetProperty("needsReview").GetBoolean(), "the invoice Generate listed for checking is flagged");
        // The notice links come from the result itself, with the number and the student's name.
        var listed = Assert.Single(result.GetProperty("invoices").EnumerateArray());
        Assert.Equal(id, listed.GetProperty("id").GetGuid());
        Assert.StartsWith("INV-", listed.GetProperty("number").GetString());
        Assert.Equal("Ada Obi", listed.GetProperty("studentName").GetString());
        var reviewed = await Json(await f.School.Owner.PostAsync($"/api/school/invoices/{id}/reviewed", null, Ct));
        Assert.False(reviewed.GetProperty("needsReview").GetBoolean());

        // Generating again with no change neither lists nor flags it.
        var again = await GenerateAsync(f);
        Assert.Empty(Ids(again, "needsReview"));
        Assert.False((await InvoiceAsync(f, id)).GetProperty("needsReview").GetBoolean());
    }

    [Fact]
    public async Task Sc3_Generate_lists_invoices_with_payments_apart_and_only_when_the_structure_differs()
    {
        var f = await FeesAsync("Sc3 Paid Invoices School");
        var ada = await kit.StudentAsync(f.School, f.ClassId, "Ada");
        await GenerateAsync(f);
        var id = await InvoiceOfAsync(f, ada);
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/payments", new { amountMinor = 1_000_000L, method = "Cash", paidOn = Today }, Ct));

        // Nothing changed: an invoice with payments is not mentioned at all.
        var same = await GenerateAsync(f);
        Assert.Empty(Ids(same, "needsReview"));
        Assert.Empty(Ids(same, "withPayments"));

        // The structure changed: it is left alone and listed as such, but not flagged for review.
        await StructureAsync(f, 11_000_000, 2_000_000);
        var changed = await GenerateAsync(f);
        Assert.Empty(Ids(changed, "needsReview"));
        Assert.Equal([id], Ids(changed, "withPayments"));
        Assert.Contains(changed.GetProperty("invoices").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
        var invoice = await InvoiceAsync(f, id);
        Assert.False(invoice.GetProperty("needsReview").GetBoolean());
        Assert.Equal(12_000_000L, invoice.GetProperty("total").GetInt64());
    }

    // ---------- 4. The reminder email and the bell give the same amount ----------

    [Fact]
    public async Task Sc4_Reminder_email_and_bell_name_the_same_instalment_and_the_full_balance()
    {
        var f = await FeesAsync("Sc4 Reminder School");
        await StructureAsync(f, 10_000_000, 0);
        var ada = await kit.StudentAsync(f.School, f.ClassId, "Ada");
        var guardianId = await kit.GuardianAsync(f.School, ada, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");
        var (_, parentId) = await kit.ParentAsync(f.School, guardianId);
        await GenerateAsync(f);
        var id = await InvoiceOfAsync(f, ada);
        var first = Today.AddDays(-10);
        await Json(await f.School.Owner.PutAsJsonAsync($"/api/school/invoices/{id}/instalments", new[]
        {
            new { dueDate = first, amountMinor = 4_000_000L },
            new { dueDate = Today.AddDays(10), amountMinor = 3_000_000L },
            new { dueDate = Today.AddDays(20), amountMinor = 3_000_000L },
        }, Ct));

        Assert.Equal(1, await api.AsTenantAsync<FinanceService, int>(f.School.TenantId, s => s.SendDueRemindersAsync(Today, Ct)));
        var email = await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters()
            .SingleAsync(n => n.TenantId == f.School.TenantId && n.TemplateKey == "school.fee_reminder", Ct));
        var bell = await api.WithDbAsync(db => db.UserNotifications.SingleAsync(n => n.UserId == parentId && n.Kind == "school.fee_reminder", Ct));

        var dateText = first.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(email.Payload.ContainsKey("dueAmount"), "the reminder email names the instalment that is due");
        Assert.Contains("40,000", email.Payload["dueAmount"]);
        Assert.Contains("100,000", email.Payload["balance"]);
        Assert.Equal(dateText, email.Payload["dueDate"]);

        var text = Saas.Infrastructure.Notifications.EmailTemplates.Render(email, "SchoolLink").Text;
        Assert.Contains($"{email.Payload["dueAmount"]} was due on {dateText}", text);
        Assert.Contains(email.Payload["balance"], text);

        // The bell says the same: the same amount and date, and the full balance second.
        Assert.Contains(email.Payload["dueAmount"], bell.Body);
        Assert.Contains(dateText, bell.Body);
        Assert.Contains(email.Payload["balance"], bell.Body);
        Assert.True(bell.Body.IndexOf(email.Payload["dueAmount"], StringComparison.Ordinal) < bell.Body.IndexOf(email.Payload["balance"], StringComparison.Ordinal));
    }

    // ---------- 5. Parent notifications open the child they are about ----------

    [Fact]
    public async Task Sc5_Parent_notification_links_carry_the_child()
    {
        var f = await FeesAsync("Sc5 Child Links School");
        await SetTermAroundTodayAsync(f.School);
        var ada = await kit.StudentAsync(f.School, f.ClassId, "Ada");
        var bola = await kit.StudentAsync(f.School, f.ClassId, "Bola");
        var mumForAda = await kit.GuardianAsync(f.School, ada, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");
        var mumForBola = await kit.GuardianAsync(f.School, bola, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");
        var (parent, parentId) = await kit.ParentAsync(f.School, mumForAda);
        await Json(await parent.PostAsync($"/api/school-invites/{await kit.InviteAsync(f.School, mumForBola)}/accept", null, Ct));

        // Invoices for both, a payment and its reversal for Ada, a reminder, an absence, results and an announcement.
        await GenerateAsync(f);
        var adaInvoice = await InvoiceOfAsync(f, ada);
        var payment = (await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{adaInvoice}/payments",
            new { amountMinor = 1_000_000L, method = "Cash", paidOn = Today }, Ct))).GetProperty("id").GetGuid();
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{payment}/reverse", new { reason = "Counted twice" }, Ct));
        await api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == adaInvoice).ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, Today.AddDays(-1)), Ct));
        await api.AsTenantAsync<FinanceService, int>(f.School.TenantId, s => s.SendDueRemindersAsync(Today, Ct));
        (await f.School.Owner.PutAsJsonAsync($"/api/school/attendance/{f.ClassId}/{Today:yyyy-MM-dd}",
            new { clientId = "phone-1", marks = new[] { new { enrolmentId = await EnrolmentAsync(f.School, ada), status = "Absent" } } }, Ct)).EnsureSuccessStatusCode();
        await ScoreAsync(f.School, f.ClassId, f.TermId, ada, bola);
        var results = $"/api/school/results/{f.ClassId}/{f.TermId}";
        (await f.School.Owner.PostAsync($"{results}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await f.School.Owner.PostAsync($"{results}/publish", null, Ct)).EnsureSuccessStatusCode();
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements", new
        {
            title = "JSS 1A trip", body = "The trip is on Friday.", audience = "Classes", classIds = new[] { f.ClassId }, levelIds = Array.Empty<Guid>(), emailGuardians = false,
        }, Ct));

        var notes = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == parentId).ToListAsync(Ct));
        string Link(string kind, Guid child) => Assert.Single(notes, n => n.Kind == kind && n.Title.Contains(child == ada ? "Ada" : "Bola")).Link;
        Assert.Equal($"/parent/fees?child={ada}", Link("school.invoice_issued", ada));
        Assert.Equal($"/parent/fees?child={bola}", Link("school.invoice_issued", bola));
        Assert.Equal($"/parent/fees?child={ada}", Link("school.payment_received", ada));
        Assert.Equal($"/parent/fees?child={ada}", Link("school.payment_reversed", ada));
        Assert.Equal($"/parent/fees?child={ada}", Link("school.fee_reminder", ada));
        Assert.Equal($"/parent/attendance?child={ada}", Link("school.absent", ada));
        Assert.Equal($"/parent/results?child={ada}", Link("school.results_published", ada));
        var announcement = Assert.Single(notes, n => n.Kind == "school.announcement");
        Assert.Matches($"^/parent/announcements\\?child=({ada}|{bola})$", announcement.Link);
        // Every parent link that is not a message thread names a child.
        Assert.All(notes.Where(n => !n.Link.StartsWith("/parent/messages/", StringComparison.Ordinal)), n => Assert.Contains("?child=", n.Link));
    }

    // ---------- 6. Reading a thread clears its bell notifications ----------

    [Fact]
    public async Task Sc6_Opening_a_thread_marks_its_bell_notifications_read()
    {
        var school = await kit.SchoolAsync("Sc6 Thread Read School");
        var classId = await kit.ClassAsync(school);
        var ada = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, ada, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);
        var (teacher, teacherId) = await kit.StaffAsync(school, MemberRole.Staff, "Form Teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == teacherId).Select(p => p.Id).FirstAsync(Ct));
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct));

        Guid threadId = default;
        foreach (var body in new[] { "Ada will be late.", "Sorry, Tuesday.", "Thank you." })
            threadId = (await Json(await parent.PostAsJsonAsync($"/api/parent/children/{ada}/threads", new { to = "class-teacher", body }, Ct))).GetProperty("id").GetGuid();
        // An unrelated notification for the teacher stays unread.
        await api.WithDbAsync(async db =>
        {
            db.UserNotifications.Add(new UserNotification { UserId = teacherId, TenantId = school.TenantId, Product = "school", Kind = "school.announcement", Title = "Other", Body = "Other", Link = "/app/announcements" });
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        Assert.Equal(4, await UnreadAsync(teacher));

        // The teacher opens the thread from Messages, not from the bell.
        await Json(await teacher.GetAsync($"/api/school/threads/{threadId}/messages", Ct));
        Assert.Equal(1, await UnreadAsync(teacher));

        // The same on the parent's side.
        await Json(await teacher.PostAsJsonAsync($"/api/school/threads/{threadId}/messages", new { body = "Noted." }, Ct));
        Assert.Equal(1, await UnreadAsync(parent));
        await Json(await parent.GetAsync($"/api/parent/threads/{threadId}/messages", Ct));
        Assert.Equal(0, await UnreadAsync(parent));
    }

    // ---------- 7. The school settings page reads the profile ----------

    [Fact]
    public async Task Sc7_Leadership_reads_the_school_profile_for_the_settings_page()
    {
        var school = await kit.SchoolAsync("Sc7 Profile School");
        await Json(await school.Owner.PutAsJsonAsync("/api/school/settings/profile", new { motto = "Knowledge is light", address = "1 School Road, Ibadan" }, Ct));

        var empty = await Json(await school.Owner.GetAsync("/api/school/settings/profile", Ct));
        Assert.Equal("Sc7 Profile School", empty.GetProperty("name").GetString());
        Assert.Equal("Knowledge is light", empty.GetProperty("motto").GetString());
        Assert.Equal("1 School Road, Ibadan", empty.GetProperty("address").GetString());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("logoUrl").ValueKind);
        Assert.False(empty.GetProperty("hasSignature").GetBoolean());

        static MultipartFormDataContent Form()
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new MultipartFormDataContent { { file, "file", "image.png" } };
        }
        await Json(await school.Owner.PutAsync("/api/school/settings/logo", Form(), Ct));
        await Json(await school.Owner.PutAsync("/api/school/settings/signature", Form(), Ct));
        var full = await Json(await school.Owner.GetAsync("/api/school/settings/profile", Ct));
        Assert.Equal(JsonValueKind.String, full.GetProperty("logoUrl").ValueKind);
        Assert.True(full.GetProperty("hasSignature").GetBoolean());

        var (admin, _) = await kit.StaffAsync(school, MemberRole.Admin, "Principal");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/school/settings/profile", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/settings/profile", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/school/settings/profile", Ct)).StatusCode);
    }

    // ---------- 8. A cancelled invoice has no balance ----------

    [Fact]
    public async Task Sc8_A_cancelled_invoice_has_no_balance()
    {
        var f = await FeesAsync("Sc8 Cancelled Balance School");
        var ada = await kit.StudentAsync(f.School, f.ClassId, "Ada");
        await GenerateAsync(f);
        var id = await InvoiceOfAsync(f, ada);

        var cancelled = await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/cancel", new { reason = "Child left the school" }, Ct));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(0L, cancelled.GetProperty("balance").GetInt64());
        Assert.Equal(12_000_000L, cancelled.GetProperty("total").GetInt64());
        var row = (await Json(await f.School.Owner.GetAsync($"/api/school/invoices?termId={f.TermId}&status=Cancelled", Ct))).EnumerateArray().Single();
        Assert.Equal(0L, row.GetProperty("balance").GetInt64());

        // Recalculating (a late online payment, say) keeps it at zero.
        await api.AsTenantAsync<FeeService, bool>(f.School.TenantId, async s => { await s.RecalculateAsync(id, Ct); return true; });
        Assert.Equal(0L, (await InvoiceAsync(f, id)).GetProperty("balance").GetInt64());
    }

    // ---------- 9. A ₦0 invoice is not "Unpaid" ----------

    [Fact]
    public async Task Sc9_An_invoice_with_nothing_to_pay_is_paid()
    {
        var f = await FeesAsync("Sc9 Zero Invoice School");
        await StructureAsync(f, 10_000_000, 0);
        var ada = await kit.StudentAsync(f.School, f.ClassId, "Ada");
        await GenerateAsync(f);
        var id = await InvoiceOfAsync(f, ada);

        var free = await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{id}/discount", new { kind = "Percent", value = 100, reason = "Full scholarship" }, Ct));
        Assert.Equal(0L, free.GetProperty("total").GetInt64());
        Assert.Equal("Paid", free.GetProperty("status").GetString());
        var unpaid = (await Json(await f.School.Owner.GetAsync($"/api/school/invoices?termId={f.TermId}&status=Unpaid", Ct))).EnumerateArray().ToList();
        Assert.DoesNotContain(unpaid, i => i.GetProperty("id").GetGuid() == id);
    }

    // ---------- 11. "Report cards ready" opens that bundle ----------

    [Fact]
    public async Task Sc11_Report_card_notifications_link_to_their_bundle()
    {
        var school = await kit.SchoolAsync("Sc11 Bundle Link School");
        await SetTermAroundTodayAsync(school);
        var classId = await kit.ClassAsync(school);
        var termId = await TermIdAsync(school);
        var ada = await kit.StudentAsync(school, classId, "Ada");
        await ScoreAsync(school, classId, termId, ada);
        var ownerId = await kit.OwnerIdAsync(school);

        var id = (await Json(await school.Owner.PostAsync($"/api/school/report-cards/{classId}/{termId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(school.TenantId, async svc => { await svc.BuildBundleAsync(id, Ct); return true; });
        var ready = await api.WithDbAsync(db => db.UserNotifications.SingleAsync(n => n.UserId == ownerId && n.Kind == "school.report_cards_ready", Ct));
        Assert.Equal($"/app/results?bundle={id}", ready.Link);
        var bundle = await Json(await school.Owner.GetAsync($"/api/school/report-cards/bundles/{id}", Ct));
        Assert.Equal("Ready", bundle.GetProperty("status").GetString());
        Assert.Equal(classId, bundle.GetProperty("classId").GetGuid());
        Assert.Equal(termId, bundle.GetProperty("termId").GetGuid());
        Assert.False(string.IsNullOrEmpty(bundle.GetProperty("className").GetString()));

        // Bundles are built from drafts too, so the failure copy doesn't mention publishing.
        var empty = await kit.ClassAsync(school, "B");
        var failedId = (await Json(await school.Owner.PostAsync($"/api/school/report-cards/{empty}/{termId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(school.TenantId, async svc => { await svc.BuildBundleAsync(failedId, Ct); return true; });
        var failed = await api.WithDbAsync(db => db.UserNotifications.SingleAsync(n => n.UserId == ownerId && n.Kind == "school.report_cards_failed", Ct));
        Assert.Equal($"/app/results?bundle={failedId}", failed.Link);
        Assert.DoesNotContain("published", failed.Body);
    }
}
