using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink events reach the bell: the right people get exactly one in-app notification, with a link into school-web.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolNotificationEventsTests(ApiFixture api)
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

    private Task<List<UserNotification>> NotesAsync(Guid userId, string kind) =>
        api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == userId && n.Kind == kind).ToListAsync(Ct));

    /// <summary>Exactly one notification of the kind for the user, from this school, linking to the path.</summary>
    private async Task<UserNotification> OneAsync(Guid userId, string kind, SchoolKit.School school, string link)
    {
        var note = Assert.Single(await NotesAsync(userId, kind));
        Assert.Equal("school", note.Product);
        Assert.Equal(school.TenantId, note.TenantId);
        Assert.StartsWith(link, note.Link);
        Assert.False(string.IsNullOrWhiteSpace(note.Title));
        return note;
    }

    private async Task NoneAsync(Guid userId, string kind) => Assert.Empty(await NotesAsync(userId, kind));

    private Task<Guid> TermIdAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

    private Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>A current term around today, one class with Mathematics, and Ada scored in it.</summary>
    private async Task<(SchoolKit.School School, Guid ClassId, Guid TermId, Guid StudentId)> ScoredAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct));
        var classId = await kit.ClassAsync(school);
        var termId = await TermIdAsync(school);
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var math = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid() }, Ct))).GetProperty("id").GetGuid();
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var enrolmentId = await EnrolmentAsync(school, studentId);
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={math}&termId={termId}", Ct));
        var entries = grid.GetProperty("components").EnumerateArray()
            .Select(c => new { enrolmentId, componentId = c.GetProperty("id").GetGuid(), value = (decimal?)10 }).ToArray();
        (await school.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = math, termId, entries }, Ct)).EnsureSuccessStatusCode();
        return (school, classId, termId, studentId);
    }

    /// <summary>A school with one student whose term invoice is ₦100,000 of tuition, and the student's guardian linked to a parent account.</summary>
    private async Task<(SchoolKit.School School, Guid TermId, Guid StudentId, Guid ParentId, HttpClient Parent)> FeesAsync(string name, bool online = false)
    {
        var school = await kit.SchoolAsync(name);
        if (online)
        {
            await api.MakeProAsync(school.TenantId, "school", "standard");
            await kit.SetPaystackKeyAsync(school);
        }
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", "08031234567", $"mum-{Guid.NewGuid():N}@example.com");
        var (parent, parentId) = await kit.ParentAsync(school, guardianId);
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await TermIdAsync(school);
        var item = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = item, amountMinor = 10_000_000L } } }, Ct));
        return (school, termId, studentId, parentId, parent);
    }

    private static async Task GenerateAsync(SchoolKit.School school, Guid termId) =>
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));

    private static async Task<Guid> InvoiceIdAsync(SchoolKit.School school, Guid termId) =>
        (await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}", Ct))).EnumerateArray().Single().GetProperty("id").GetGuid();

    [Fact]
    public async Task Results_published_notify_linked_parents_and_students_once()
    {
        var (school, classId, termId, studentId) = await ScoredAsync("Published Notes School");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");
        var (_, parentId) = await kit.ParentAsync(school, guardianId);
        await kit.GuardianAsync(school, studentId, "Emeka Obi", "08030000000", null); // no account: nothing in-app
        await kit.StudentLoginAsync(school, studentId);
        var studentUserId = await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId).Select(s => s.UserId!.Value).FirstAsync(Ct));
        var ownerId = await kit.OwnerIdAsync(school);

        var results = $"/api/school/results/{classId}/{termId}";
        (await school.Owner.PostAsync($"{results}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsync($"{results}/publish", null, Ct)).EnsureSuccessStatusCode();
        // Unpublishing and publishing again tells nobody twice.
        (await school.Owner.PostAsync($"{results}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsync($"{results}/publish", null, Ct)).EnsureSuccessStatusCode();

        var parentNote = await OneAsync(parentId, "school.results_published", school, "/parent/results");
        Assert.Contains("Ada Obi", parentNote.Title);
        await OneAsync(studentUserId, "school.results_published", school, "/student/results");
        await NoneAsync(ownerId, "school.results_published");
    }

    [Fact]
    public async Task Report_card_bundles_notify_the_member_who_asked_when_ready_or_failed()
    {
        var (school, classId, termId, _) = await ScoredAsync("Bundle Notes School");
        var (teacher, teacherId) = await kit.StaffAsync(school, MemberRole.Admin, "Vice Principal");
        var ownerId = await kit.OwnerIdAsync(school);

        var id = (await Json(await teacher.PostAsync($"/api/school/report-cards/{classId}/{termId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(school.TenantId, async svc => { await svc.BuildBundleAsync(id, Ct); return true; });
        await OneAsync(teacherId, "school.report_cards_ready", school, "/app/results");
        await NoneAsync(ownerId, "school.report_cards_ready");

        // A class with nothing to print fails, and the member who asked hears about that too.
        var empty = await kit.ClassAsync(school, "B");
        var failed = (await Json(await school.Owner.PostAsync($"/api/school/report-cards/{empty}/{termId}/bundle", null, Ct))).GetProperty("id").GetGuid();
        await api.AsTenantAsync<ReportCardService, bool>(school.TenantId, async svc => { await svc.BuildBundleAsync(failed, Ct); return true; });
        await OneAsync(ownerId, "school.report_cards_failed", school, "/app/results");
        await NoneAsync(teacherId, "school.report_cards_failed");
    }

    [Fact]
    public async Task New_invoices_notify_linked_guardians_once()
    {
        var (school, termId, _, parentId, _) = await FeesAsync("Invoice Notes School");
        await GenerateAsync(school, termId);
        await GenerateAsync(school, termId); // refreshing an unpaid invoice is not a new one
        var note = await OneAsync(parentId, "school.invoice_issued", school, "/parent/fees");
        Assert.Contains("Ada Obi", note.Title);
        await NoneAsync(await kit.OwnerIdAsync(school), "school.invoice_issued");
    }

    [Fact]
    public async Task Fee_reminders_reach_linked_guardians_on_the_email_schedule()
    {
        var (school, termId, _, parentId, _) = await FeesAsync("Reminder Notes School");
        await GenerateAsync(school, termId);
        var invoiceId = await InvoiceIdAsync(school, termId);
        var due = Today.AddDays(20);
        await api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId).ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, due), Ct));

        Task<int> Send(DateOnly day) => api.AsTenantAsync<FinanceService, int>(school.TenantId, s => s.SendDueRemindersAsync(day, Ct));
        Assert.Equal(0, await Send(due.AddDays(-4)));
        await NoneAsync(parentId, "school.fee_reminder");
        Assert.Equal(1, await Send(due.AddDays(-3)));
        Assert.Equal(0, await Send(due.AddDays(-3)));
        await OneAsync(parentId, "school.fee_reminder", school, "/parent/fees");
    }

    [Fact]
    public async Task Payments_notify_guardians_and_online_ones_also_reach_the_bursars()
    {
        var (school, termId, studentId, parentId, parent) = await FeesAsync("Payment Notes School", online: true);
        var (_, bursarId) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        var ownerId = await kit.OwnerIdAsync(school);
        await GenerateAsync(school, termId);
        var invoiceId = await InvoiceIdAsync(school, termId);

        var reference = (await Json(await parent.PostAsJsonAsync($"/api/parent/children/{studentId}/invoices/{invoiceId}/pay",
            new { amountMinor = 3_000_000L }, Ct))).GetProperty("reference").GetString()!;
        kit.Charge(reference, 3_000_000);
        await Json(await parent.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct));
        // The payer is also the guardian: one notification, not two.
        await OneAsync(parentId, "school.payment_received", school, "/parent/fees");
        await OneAsync(bursarId, "school.payment_received", school, "/app/fees");
        await NoneAsync(ownerId, "school.payment_received");

        // A payment the bursar records reaches the guardians, not the bursars.
        var manual = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/invoices/{invoiceId}/payments",
            new { amountMinor = 1_000_000L, method = "Cash", paidOn = Today }, Ct))).GetProperty("id").GetGuid();
        Assert.Equal(2, (await NotesAsync(parentId, "school.payment_received")).Count);
        Assert.Single(await NotesAsync(bursarId, "school.payment_received"));

        await Json(await school.Owner.PostAsJsonAsync($"/api/school/payments/{manual}/reverse", new { reason = "Counted twice" }, Ct));
        await OneAsync(parentId, "school.payment_reversed", school, "/parent/fees");
        await NoneAsync(bursarId, "school.payment_reversed");
    }

    [Fact]
    public async Task Payment_mismatches_notify_the_bursars_and_the_proprietor()
    {
        var (school, termId, studentId, parentId, parent) = await FeesAsync("Mismatch Notes School", online: true);
        var (_, bursarId) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        var ownerId = await kit.OwnerIdAsync(school);
        await GenerateAsync(school, termId);
        var invoiceId = await InvoiceIdAsync(school, termId);

        var reference = (await Json(await parent.PostAsJsonAsync($"/api/parent/children/{studentId}/invoices/{invoiceId}/pay",
            new { amountMinor = 3_000_000L }, Ct))).GetProperty("reference").GetString()!;
        var body = SchoolKit.ChargeSuccessBody(reference, 300_000);
        (await kit.WebhookAsync(school.TenantId, body, SchoolKit.Sign(body))).EnsureSuccessStatusCode();

        await OneAsync(bursarId, "school.payment_mismatch", school, "/app/fees");
        await OneAsync(ownerId, "school.payment_mismatch", school, "/app/fees");
        await NoneAsync(parentId, "school.payment_mismatch");
        await NoneAsync(parentId, "school.payment_received");
    }

    [Fact]
    public async Task An_absence_notifies_the_primary_guardian_once()
    {
        var school = await kit.SchoolAsync("Absence Notes School");
        await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct));
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var primary = await kit.GuardianAsync(school, studentId, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");
        var second = await kit.GuardianAsync(school, studentId, "Emeka Obi", null, $"dad-{Guid.NewGuid():N}@example.com");
        var (_, mumId) = await kit.ParentAsync(school, primary);
        var (_, dadId) = await kit.ParentAsync(school, second);
        var enrolmentId = await EnrolmentAsync(school, studentId);

        var url = $"/api/school/attendance/{classId}/{Today:yyyy-MM-dd}";
        for (var i = 0; i < 2; i++)
            (await school.Owner.PutAsJsonAsync(url, new { clientId = "phone-1", marks = new[] { new { enrolmentId, status = "Absent" } } }, Ct)).EnsureSuccessStatusCode();

        var note = await OneAsync(mumId, "school.absent", school, "/parent/attendance");
        Assert.Contains("Ada Obi", note.Title);
        await NoneAsync(dadId, "school.absent");
    }

    [Fact]
    public async Task An_accepted_invite_notifies_the_member_who_sent_it()
    {
        var school = await kit.SchoolAsync("Invite Notes School");
        var (admin, adminId) = await kit.StaffAsync(school, MemberRole.Admin, "Registrar");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", null, $"mum-{Guid.NewGuid():N}@example.com");

        var link = (await Json(await admin.PostAsync($"/api/school/guardians/{guardianId}/invite", null, Ct))).GetProperty("link").GetString()!;
        var (parent, _, _) = await kit.ParentAccountAsync();
        await Json(await parent.PostAsync($"/api/school-invites/{SchoolKit.TokenOf(link)}/accept", null, Ct));

        var note = await OneAsync(adminId, "school.guardian_joined", school, "/app/students");
        Assert.Contains("Ngozi Obi", note.Title);
        await NoneAsync(await kit.OwnerIdAsync(school), "school.guardian_joined");
    }

    [Fact]
    public async Task A_finished_import_notifies_the_member_who_ran_it()
    {
        var school = await kit.SchoolAsync("Import Notes School");
        await kit.ClassAsync(school);
        const string header = "admission_no,first_name,middle_name,last_name,gender,date_of_birth,class,guardian1_name,guardian1_phone,guardian1_email,guardian1_relationship,guardian2_name,guardian2_phone,guardian2_email,guardian2_relationship";
        var csv = $"{header}\r\nADM-1,Ada,,Obi,F,,JSS 1A,,,,,,,,\r\nADM-2,Bayo,,Eze,M,,JSS 1A,,,,,,,,\r\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var preview = await Json(await school.Owner.PostAsync("/api/school/students/import/preview",
            new MultipartFormDataContent { { file, "file", "students.csv" } }, Ct));
        await Json(await school.Owner.PostAsync($"/api/school/students/import/{preview.GetProperty("token").GetString()}/confirm", null, Ct));

        var note = await OneAsync(await kit.OwnerIdAsync(school), "school.import_finished", school, "/app/students");
        Assert.Contains("2", note.Body);
    }
}
