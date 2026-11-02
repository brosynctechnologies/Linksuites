using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink finance: debtors, reminders, the results hold, invoice cancel and review, expenses and the finance reports.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolFinanceTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);

    private sealed record Fees(SchoolKit.School School, Guid TermId, Guid LevelId, Guid ClassA, Guid ClassB);

    /// <summary>A school with two JSS 1 classes and a fee structure for the current term. Add students, then call <see cref="GenerateAsync"/>.</summary>
    private async Task<Fees> FeesAsync(string name, params (string Item, long Amount)[] items)
    {
        var school = await kit.SchoolAsync(name);
        var classA = await kit.ClassAsync(school, "A");
        var classB = await kit.ClassAsync(school, "B");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classA).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var lines = new List<object>();
        foreach (var (item, amount) in items.Length == 0 ? [("Tuition", 10_000_000L)] : items)
        {
            var id = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = item, isOptional = false }, Ct))).GetProperty("id").GetGuid();
            lines.Add(new { feeItemId = id, amountMinor = amount });
        }
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}", new { lines }, Ct));
        return new Fees(school, termId, levelId, classA, classB);
    }

    private static async Task GenerateAsync(Fees f) =>
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = f.TermId }, Ct));

    private static async Task<Guid> InvoiceIdAsync(Fees f, Guid studentId) =>
        (await Json(await f.School.Owner.GetAsync($"/api/school/invoices?termId={f.TermId}", Ct))).EnumerateArray()
            .Single(i => i.GetProperty("studentId").GetGuid() == studentId).GetProperty("id").GetGuid();

    private Task SetDueAsync(Guid invoiceId, DateOnly due) =>
        api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId).ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, due), Ct));

    private static Task<HttpResponseMessage> PayAsync(HttpClient staff, Guid invoiceId, long amountMinor, string method = "Cash") =>
        staff.PostAsJsonAsync($"/api/school/invoices/{invoiceId}/payments", new { amountMinor, method, paidOn = Today }, Ct);

    private static MultipartFormDataContent PhotoForm(byte[] bytes, string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "receipt" } };
    }

    // ---------- Debtors ----------

    [Fact]
    public async Task Debtors_list_and_csv_match_balances()
    {
        var f = await FeesAsync("Debtor Academy");
        var s1 = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        var s2 = await kit.StudentAsync(f.School, f.ClassA, "Bola");
        var s3 = await kit.StudentAsync(f.School, f.ClassA, "Chike");
        var s4 = await kit.StudentAsync(f.School, f.ClassB, "Dayo");
        var s5 = await kit.StudentAsync(f.School, f.ClassA, "Emeka");
        await kit.GuardianAsync(f.School, s1, "@Boss Obi", "0803 123 4567", "boss@example.com");
        await kit.GuardianAsync(f.School, s4, "=HYPERLINK(\"http://x.example\")", "+44 20 7946 0958", null);
        await GenerateAsync(f);
        var i1 = await InvoiceIdAsync(f, s1); var i2 = await InvoiceIdAsync(f, s2); var i3 = await InvoiceIdAsync(f, s3);
        var i4 = await InvoiceIdAsync(f, s4); var i5 = await InvoiceIdAsync(f, s5);
        await SetDueAsync(i1, Today.AddDays(-30));
        await SetDueAsync(i2, Today.AddDays(-10));
        await SetDueAsync(i3, Today.AddDays(-40));
        await SetDueAsync(i4, Today.AddDays(5));
        await SetDueAsync(i5, Today.AddDays(-50));
        await Json(await PayAsync(f.School.Owner, i2, 2_500_000));
        await Json(await PayAsync(f.School.Owner, i3, 10_000_000)); // paid in full: not a debtor
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{i5}/cancel", new { reason = "Withdrawn before the term started" }, Ct)); // cancelled: not a debtor

        var all = await Json(await f.School.Owner.GetAsync("/api/school/debtors", Ct));
        var rows = all.GetProperty("debtors").EnumerateArray().ToList();
        Assert.Equal([i1, i2, i4], rows.Select(r => r.GetProperty("invoiceId").GetGuid()).ToArray());
        Assert.Equal([30, 10, 0], rows.Select(r => r.GetProperty("daysOverdue").GetInt32()).ToArray());
        Assert.Equal([10_000_000L, 7_500_000L, 10_000_000L], rows.Select(r => r.GetProperty("balance").GetInt64()).ToArray());
        Assert.Equal(27_500_000L, all.GetProperty("totalBalance").GetInt64());
        Assert.Equal("@Boss Obi", rows[0].GetProperty("primaryGuardian").GetString());
        Assert.Equal("+2348031234567", rows[0].GetProperty("guardianPhone").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("primaryGuardian").ValueKind);

        Assert.Equal(2, (await Json(await f.School.Owner.GetAsync("/api/school/debtors?minDaysOverdue=10", Ct))).GetProperty("debtors").GetArrayLength());
        var late = (await Json(await f.School.Owner.GetAsync("/api/school/debtors?minDaysOverdue=11", Ct))).GetProperty("debtors").EnumerateArray().Single();
        Assert.Equal(i1, late.GetProperty("invoiceId").GetGuid());
        var inB = (await Json(await f.School.Owner.GetAsync($"/api/school/debtors?classId={f.ClassB}", Ct))).GetProperty("debtors").EnumerateArray().Single();
        Assert.Equal(i4, inB.GetProperty("invoiceId").GetGuid());
        await ProblemAsync(await f.School.Owner.GetAsync($"/api/school/debtors?termId={Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);

        // The CSV has the same rows and amounts, and text typed by a family can't run as a formula.
        var response = await f.School.Owner.GetAsync("/api/school/debtors.csv", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // UTF-8 BOM so Excel reads diacritics
        Assert.Contains("\r\n", System.Text.Encoding.UTF8.GetString(bytes));
        var lines = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("admission number,student,class,invoice,total,paid,balance,days overdue,primary guardian,guardian phone", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains("\"'@Boss Obi\"", lines[1]);
        Assert.EndsWith(",\"0803 123 4567\"", lines[1]); // local spaced form: no leading +, so no apostrophe
        Assert.DoesNotContain("+234", lines[1]);
        Assert.Contains("\"'=HYPERLINK(", lines[3]); // text typed by a family is still escaped
        Assert.EndsWith(",\"442 079 460 958\"", lines[3]); // not a Nigerian number: digits in groups, never a leading +
        Assert.Contains(",100000.00,0.00,100000.00,30,", lines[1]);
        Assert.Contains(",100000.00,25000.00,75000.00,10,", lines[2]);
        Assert.Contains("\"JSS 1B\"", lines[3]);
        Assert.Equal(27_500_000L, lines.Skip(1).Sum(l => (long)(decimal.Parse(l.Split(',')[6], System.Globalization.CultureInfo.InvariantCulture) * 100)));

        // Leadership and the bursar read debtors; a teacher does not.
        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.OK, (await bursar.GetAsync("/api/school/debtors", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bursar.GetAsync("/api/school/debtors.csv", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/debtors", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/debtors.csv", Ct)).StatusCode);
    }

    [Fact]
    public async Task Overdue_days_count_from_the_first_instalment_the_payments_do_not_cover()
    {
        var f = await FeesAsync("Instalment Debtors");
        var s = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        await GenerateAsync(f);
        var invoice = await InvoiceIdAsync(f, s);
        var session = await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(x => x.TenantId == f.School.TenantId && x.IsCurrent).Select(x => new { x.StartDate, x.EndDate }).FirstAsync(Ct));
        var d1 = Today.AddDays(-20); var d2 = Today.AddDays(-5); var d3 = Today.AddDays(30);
        Assert.True(d1 >= session.StartDate && d3 <= session.EndDate, "the current session covers the instalment dates");
        await Json(await f.School.Owner.PutAsJsonAsync($"/api/school/invoices/{invoice}/instalments", new[]
        {
            new { dueDate = d1, amountMinor = 4_000_000L }, new { dueDate = d2, amountMinor = 3_000_000L }, new { dueDate = d3, amountMinor = 3_000_000L },
        }, Ct));

        int Days(JsonElement list) => list.GetProperty("debtors").EnumerateArray().Single().GetProperty("daysOverdue").GetInt32();
        Assert.Equal(20, Days(await Json(await f.School.Owner.GetAsync("/api/school/debtors", Ct))));
        await Json(await PayAsync(f.School.Owner, invoice, 4_000_000));
        Assert.Equal(5, Days(await Json(await f.School.Owner.GetAsync("/api/school/debtors", Ct))));
        await Json(await PayAsync(f.School.Owner, invoice, 3_000_000));
        Assert.Equal(0, Days(await Json(await f.School.Owner.GetAsync("/api/school/debtors", Ct))));
    }

    // ---------- Cancel and review ----------

    [Fact]
    public async Task An_invoice_can_be_cancelled_with_a_reason_unless_it_has_payments()
    {
        var f = await FeesAsync("Cancel Academy");
        var s = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        await GenerateAsync(f);
        var invoice = await InvoiceIdAsync(f, s);
        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/school/invoices/{invoice}/cancel", new { reason = "Not allowed here" }, Ct)).StatusCode);

        var payment = (await Json(await PayAsync(f.School.Owner, invoice, 1_000_000))).GetProperty("id").GetGuid();
        var refused = await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{invoice}/cancel", new { reason = "Child left the school" }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("This invoice has payments. Reverse them before cancelling.", refused.GetProperty("detail").GetString());

        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{payment}/reverse", new { reason = "Paid into the wrong account" }, Ct));
        await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{invoice}/cancel", new { reason = "no" }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{invoice}/cancel", new { reason = new string('x', 301) }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{Guid.NewGuid()}/cancel", new { reason = "Child left the school" }, Ct), HttpStatusCode.NotFound);

        var done = await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{invoice}/cancel", new { reason = "Child left the school" }, Ct));
        Assert.Equal("Cancelled", done.GetProperty("status").GetString());
        await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{invoice}/cancel", new { reason = "Child left the school" }, Ct), HttpStatusCode.Conflict);

        var audit = await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == f.School.TenantId && a.Action == "invoice.cancelled" && a.EntityId == invoice.ToString()).ToListAsync(Ct));
        Assert.Contains("Child left the school", Assert.Single(audit).AfterJson);
    }

    [Fact]
    public async Task Marking_an_invoice_reviewed_clears_the_review_flag()
    {
        var f = await FeesAsync("Review Academy");
        var s = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        await GenerateAsync(f);
        var invoice = await InvoiceIdAsync(f, s);
        await api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.Id == invoice).ExecuteUpdateAsync(x => x.SetProperty(i => i.NeedsReview, true), Ct));
        Assert.True((await Json(await f.School.Owner.GetAsync($"/api/school/invoices/{invoice}", Ct))).GetProperty("needsReview").GetBoolean());

        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"/api/school/invoices/{invoice}/reviewed", null, Ct)).StatusCode);
        var after = await Json(await f.School.Owner.PostAsync($"/api/school/invoices/{invoice}/reviewed", null, Ct));
        Assert.False(after.GetProperty("needsReview").GetBoolean());
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == f.School.TenantId && a.Action == "invoice.reviewed" && a.EntityId == invoice.ToString(), Ct)));
        await ProblemAsync(await f.School.Owner.PostAsync($"/api/school/invoices/{Guid.NewGuid()}/reviewed", null, Ct), HttpStatusCode.NotFound);
    }

    // ---------- Settings and the results hold ----------

    [Fact]
    public async Task Results_hold_setting_is_off_by_default()
    {
        var f = await FeesAsync("Hold Academy");
        var owing = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        var cancelled = await kit.StudentAsync(f.School, f.ClassA, "Bola");
        var noInvoice = await kit.StudentAsync(f.School, f.ClassA, "Chike");
        await GenerateAsync(f);
        var invoice = await InvoiceIdAsync(f, owing);
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{await InvoiceIdAsync(f, cancelled)}/cancel", new { reason = "Left the school" }, Ct));
        var noInvoiceEnrolment = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.StudentId == noInvoice).Select(e => e.Id).FirstAsync(Ct));
        await api.WithDbAsync(db => db.StudentInvoices.IgnoreQueryFilters().Where(i => i.EnrolmentId == noInvoiceEnrolment).ExecuteDeleteAsync(Ct));

        var settings = await Json(await f.School.Owner.GetAsync("/api/school/settings/finance", Ct));
        Assert.False(settings.GetProperty("resultsHoldForUnpaidFees").GetBoolean());
        Assert.Equal(3, settings.GetProperty("reminderDaysBefore").GetInt32());
        Assert.Equal(7, settings.GetProperty("reminderEveryDays").GetInt32());
        Task<bool> Held(Guid student, Guid? term = null) => api.AsTenantAsync<FinanceService, bool>(f.School.TenantId, s => s.IsHeldAsync(student, term ?? f.TermId, Ct));
        Assert.False(await Held(owing)); // a balance, but the school has not turned the hold on

        var on = await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { resultsHoldForUnpaidFees = true }, Ct));
        Assert.True(on.GetProperty("resultsHoldForUnpaidFees").GetBoolean());
        Assert.Equal(3, on.GetProperty("reminderDaysBefore").GetInt32()); // left out: kept
        Assert.True(await Held(owing));
        Assert.False(await Held(cancelled));
        Assert.False(await Held(noInvoice));
        Assert.False(await Held(owing, Guid.NewGuid()));

        var paymentId = (await Json(await PayAsync(f.School.Owner, invoice, 10_000_000))).GetProperty("id").GetGuid();
        Assert.False(await Held(owing));
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{paymentId}/reverse", new { reason = "Cheque bounced" }, Ct));
        Assert.True(await Held(owing));

        var changed = await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderDaysBefore = 5, reminderEveryDays = 10, resultsHoldForUnpaidFees = false }, Ct));
        Assert.Equal(5, changed.GetProperty("reminderDaysBefore").GetInt32());
        Assert.Equal(10, changed.GetProperty("reminderEveryDays").GetInt32());
        Assert.False(await Held(owing));
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == f.School.TenantId && a.Action == "school.finance_settings", Ct)));

        await ProblemAsync(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderDaysBefore = 31 }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderDaysBefore = -1 }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderEveryDays = 0 }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderEveryDays = 31 }, Ct), HttpStatusCode.BadRequest);

        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.OK, (await bursar.GetAsync("/api/school/settings/finance", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PutAsJsonAsync("/api/school/settings/finance", new { resultsHoldForUnpaidFees = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync("/api/school/settings/finance", new { resultsHoldForUnpaidFees = true }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/settings/finance", Ct)).StatusCode);
    }

    // ---------- Reminders ----------

    private Task<int> SendRemindersAsync(Guid tenantId, DateOnly today) =>
        api.AsTenantAsync<FinanceService, int>(tenantId, s => s.SendDueRemindersAsync(today, Ct));

    private Task<List<Notification>> ReminderEmailsAsync(Guid tenantId, string? to = null) =>
        api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().Where(n => n.TenantId == tenantId && n.TemplateKey == "school.fee_reminder" && (to == null || n.Recipient == to)).ToListAsync(Ct));

    [Fact]
    public async Task Reminders_go_to_the_primary_guardian_on_the_schedule_and_stop_at_six()
    {
        var f = await FeesAsync("Reminder Academy");
        var s = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        var email = $"mum-{Guid.NewGuid():N}@example.com";
        await kit.GuardianAsync(f.School, s, "Ngozi Obi", "08031234567", email);
        await GenerateAsync(f);
        var invoice = await InvoiceIdAsync(f, s);
        var due = Today.AddDays(20);
        await SetDueAsync(invoice, due);

        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, due.AddDays(-4)));
        Assert.Equal(1, await SendRemindersAsync(f.School.TenantId, due.AddDays(-3)));
        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, due.AddDays(-3))); // the same day again: nothing
        var first = Assert.Single(await ReminderEmailsAsync(f.School.TenantId, email));
        Assert.Equal("Ada Obi", first.Payload["child"]);
        Assert.Equal("Reminder Academy", first.Payload["school"]);
        Assert.Contains("100,000", first.Payload["balance"]);
        Assert.Contains("/parent", first.Payload["link"]);
        Assert.Equal(email, first.Recipient);

        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, due.AddDays(1)));
        var sent = new List<DateOnly> { due.AddDays(-3) };
        for (var day = due.AddDays(4); sent.Count < 6; day = day.AddDays(7))
        {
            Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, day.AddDays(-1)));
            Assert.Equal(1, await SendRemindersAsync(f.School.TenantId, day));
            sent.Add(day);
        }
        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, sent[^1].AddDays(7)));
        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, sent[^1].AddDays(100)));
        var rows = await api.WithDbAsync(db => db.FeeReminders.IgnoreQueryFilters().Where(r => r.InvoiceId == invoice).OrderBy(r => r.SentOn).ToListAsync(Ct));
        Assert.Equal(6, rows.Count);
        Assert.Equal(FeeReminderKind.BeforeDue, rows[0].Kind);
        Assert.All(rows.Skip(1), r => Assert.Equal(FeeReminderKind.Overdue, r.Kind));
        Assert.Equal(6, (await ReminderEmailsAsync(f.School.TenantId, email)).Count);
    }

    [Fact]
    public async Task Reminders_skip_paid_cancelled_and_unreachable_invoices_and_follow_the_school_setting()
    {
        var f = await FeesAsync("Reminder Rules Academy");
        var paid = await kit.StudentAsync(f.School, f.ClassA, "Paid");
        var cancelled = await kit.StudentAsync(f.School, f.ClassA, "Cancelled");
        var noEmail = await kit.StudentAsync(f.School, f.ClassA, "NoEmail");
        var noGuardian = await kit.StudentAsync(f.School, f.ClassA, "NoGuardian");
        var backup = await kit.StudentAsync(f.School, f.ClassA, "Backup");
        var emails = new Dictionary<string, string>();
        foreach (var (key, student) in new[] { ("paid", paid), ("cancelled", cancelled) })
        {
            emails[key] = $"{key}-{Guid.NewGuid():N}@example.com";
            await kit.GuardianAsync(f.School, student, $"Parent {key}", null, emails[key]);
        }
        await kit.GuardianAsync(f.School, noEmail, "Phone Only", "08099990000", null);
        // The primary guardian has no email; the second guardian does and is told instead.
        emails["backup"] = $"backup-{Guid.NewGuid():N}@example.com";
        await kit.GuardianAsync(f.School, backup, "Phone Primary", "08088880000", null);
        await kit.GuardianAsync(f.School, backup, "Email Second", null, emails["backup"]);
        await GenerateAsync(f);
        var due = Today.AddDays(40);
        foreach (var student in new[] { paid, cancelled, noEmail, noGuardian, backup }) await SetDueAsync(await InvoiceIdAsync(f, student), due);
        await Json(await PayAsync(f.School.Owner, await InvoiceIdAsync(f, paid), 10_000_000));
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{await InvoiceIdAsync(f, cancelled)}/cancel", new { reason = "Left the school" }, Ct));

        // The default is three days before, so five days before sends nothing...
        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, due.AddDays(-5)));
        // ...until the school asks for five. Paid and cancelled invoices, and a family with no email address, are left out.
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderDaysBefore = 5 }, Ct));
        Assert.Equal(1, await SendRemindersAsync(f.School.TenantId, due.AddDays(-5)));
        Assert.Single(await ReminderEmailsAsync(f.School.TenantId, emails["backup"]));
        Assert.Empty(await ReminderEmailsAsync(f.School.TenantId, emails["paid"]));
        Assert.Empty(await ReminderEmailsAsync(f.School.TenantId, emails["cancelled"]));
        Assert.Single(await ReminderEmailsAsync(f.School.TenantId));
        // A family that couldn't be reached has no reminder recorded, so one goes out once they add an email.
        Assert.Equal(1, await api.WithDbAsync(db => db.FeeReminders.IgnoreQueryFilters().CountAsync(r => r.TenantId == f.School.TenantId, Ct)));

        // The gap between overdue reminders is the school's too.
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/finance", new { reminderEveryDays = 2 }, Ct));
        Assert.Equal(1, await SendRemindersAsync(f.School.TenantId, due.AddDays(1)));
        Assert.Equal(0, await SendRemindersAsync(f.School.TenantId, due.AddDays(2)));
        Assert.Equal(1, await SendRemindersAsync(f.School.TenantId, due.AddDays(3)));
    }

    [Fact]
    public async Task Reminders_only_cover_the_school_they_run_for()
    {
        var a = await FeesAsync("Reminder School A");
        var b = await FeesAsync("Reminder School B");
        var emails = new List<string>();
        foreach (var f in new[] { a, b })
        {
            var s = await kit.StudentAsync(f.School, f.ClassA, "Ada");
            var email = $"parent-{Guid.NewGuid():N}@example.com";
            emails.Add(email);
            await kit.GuardianAsync(f.School, s, "Ngozi Obi", null, email);
            await GenerateAsync(f);
            await SetDueAsync(await InvoiceIdAsync(f, s), Today.AddDays(10));
        }
        Assert.Equal(1, await SendRemindersAsync(a.School.TenantId, Today.AddDays(7)));
        Assert.Single(await ReminderEmailsAsync(a.School.TenantId));
        Assert.Empty(await ReminderEmailsAsync(b.School.TenantId));
    }

    // ---------- Expenses and the finance reports ----------

    private static object Expense(string category = "Transport", long amountMinor = 1_250_000, DateOnly? date = null, string? note = null) =>
        new { category, amountMinor, date = date ?? Today, note };

    [Fact]
    public async Task Expenses_show_in_the_term_summary()
    {
        var f = await FeesAsync("Summary Academy");
        var s1 = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        var s2 = await kit.StudentAsync(f.School, f.ClassB, "Bola");
        await GenerateAsync(f);
        var i1 = await InvoiceIdAsync(f, s1);
        await Json(await PayAsync(f.School.Owner, i1, 4_000_000));
        // A pending online checkout and a reversed payment don't count as collected.
        var reversed = (await Json(await PayAsync(f.School.Owner, i1, 500_000))).GetProperty("id").GetGuid();
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{reversed}/reverse", new { reason = "Entered twice" }, Ct));
        var term = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.Id == f.TermId).Select(t => new { t.StartDate, t.EndDate }).FirstAsync(Ct));

        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        var created = await Json(await bursar.PostAsJsonAsync("/api/school/expenses", Expense(" Transport ", 1_250_000, term.StartDate, "Bus repairs"), Ct));
        Assert.Equal("Transport", created.GetProperty("category").GetString());
        Assert.Equal(1_250_000L, created.GetProperty("amountMinor").GetInt64());
        Assert.False(created.GetProperty("hasReceipt").GetBoolean());
        // The same category in another case reuses the spelling already in use.
        var second = await Json(await f.School.Owner.PostAsJsonAsync("/api/school/expenses", Expense("transport", 300_000, term.StartDate), Ct));
        Assert.Equal("Transport", second.GetProperty("category").GetString());
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/expenses", Expense("Stationery", 999_999, term.StartDate.AddDays(-1)), Ct)); // before the term

        var summary = await Json(await f.School.Owner.GetAsync($"/api/school/finance/summary?termId={f.TermId}", Ct));
        Assert.Equal(20_000_000L, summary.GetProperty("expected").GetInt64());
        Assert.Equal(4_000_000L, summary.GetProperty("collected").GetInt64());
        Assert.Equal(16_000_000L, summary.GetProperty("outstanding").GetInt64());
        Assert.Equal(1_550_000L, summary.GetProperty("expenses").GetInt64());
        var current = await Json(await f.School.Owner.GetAsync("/api/school/finance/summary", Ct));
        Assert.Equal(1_550_000L, current.GetProperty("expenses").GetInt64());
        Assert.Equal(summary.GetProperty("termId").GetGuid(), current.GetProperty("termId").GetGuid());
        await ProblemAsync(await f.School.Owner.GetAsync($"/api/school/finance/summary?termId={Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);

        // A cancelled invoice leaves expected and outstanding.
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{await InvoiceIdAsync(f, s2)}/cancel", new { reason = "Left the school" }, Ct));
        var after = await Json(await f.School.Owner.GetAsync($"/api/school/finance/summary?termId={f.TermId}", Ct));
        Assert.Equal(10_000_000L, after.GetProperty("expected").GetInt64());
        Assert.Equal(6_000_000L, after.GetProperty("outstanding").GetInt64());

        // Categories are the distinct ones in use.
        Assert.Equal(["Stationery", "Transport"], (await Json(await bursar.GetAsync("/api/school/expenses/categories", Ct))).EnumerateArray().Select(c => c.GetString()).ToArray());
        var list = (await Json(await bursar.GetAsync("/api/school/expenses?category=Transport", Ct))).EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal(3, (await Json(await bursar.GetAsync("/api/school/expenses", Ct))).GetArrayLength());
        Assert.Equal(2, (await Json(await bursar.GetAsync($"/api/school/expenses?termId={f.TermId}", Ct))).GetArrayLength());

        // Update and delete.
        var id = created.GetProperty("id").GetGuid();
        var updated = await Json(await bursar.PutAsJsonAsync($"/api/school/expenses/{id}", Expense("Transport", 2_000_000, term.StartDate, "Bus repairs and tyres"), Ct));
        Assert.Equal(2_000_000L, updated.GetProperty("amountMinor").GetInt64());
        Assert.Equal(2_300_000L, (await Json(await f.School.Owner.GetAsync($"/api/school/finance/summary?termId={f.TermId}", Ct))).GetProperty("expenses").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, (await bursar.DeleteAsync($"/api/school/expenses/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bursar.GetAsync($"/api/school/expenses/{id}", Ct)).StatusCode);
        Assert.Equal(300_000L, (await Json(await f.School.Owner.GetAsync($"/api/school/finance/summary?termId={f.TermId}", Ct))).GetProperty("expenses").GetInt64());

        // Teachers have no finance access.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/expenses", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync("/api/school/expenses", Expense(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/finance/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/finance/collections?by=class", Ct)).StatusCode);
    }

    [Fact]
    public async Task Expenses_are_validated()
    {
        var school = await kit.SchoolAsync("Expense Rules Academy");
        async Task Bad(object body, string field)
        {
            var problem = await ProblemAsync(await school.Owner.PostAsJsonAsync("/api/school/expenses", body, Ct), HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), $"expected an error on {field}: {problem}");
        }
        await Bad(Expense(amountMinor: 0), "amountMinor");
        await Bad(Expense(amountMinor: -5), "amountMinor");
        await Bad(Expense(category: "   "), "category");
        await Bad(Expense(category: new string('c', 61)), "category");
        await Bad(Expense(date: Today.AddDays(1)), "date");
        await Bad(Expense(note: new string('n', 301)), "note");
        await Json(await school.Owner.PostAsJsonAsync("/api/school/expenses", Expense(category: new string('c', 60), date: Today), Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.DeleteAsync($"/api/school/expenses/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Expense_receipts_are_private_and_signed()
    {
        var school = await kit.SchoolAsync("Receipt Photo Academy");
        var other = await kit.SchoolAsync("Other Receipt Academy");
        var expense = await Json(await school.Owner.PostAsJsonAsync("/api/school/expenses", Expense(), Ct));
        var id = expense.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, expense.GetProperty("receiptUrl").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.PostAsync($"/api/school/expenses/{id}/receipt", PhotoForm("not an image"u8.ToArray()), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.PostAsync($"/api/school/expenses/{id}/receipt", PhotoForm(Png), Ct)).StatusCode);
        var withReceipt = await Json(await school.Owner.PostAsync($"/api/school/expenses/{id}/receipt", PhotoForm(Png), Ct));
        Assert.True(withReceipt.GetProperty("hasReceipt").GetBoolean());
        var link = withReceipt.GetProperty("receiptUrl").GetString()!;
        Assert.Contains("/api/school/expense-receipts/", link);

        var anonymous = api.CreateClient();
        var path = new Uri(link).PathAndQuery;
        var ok = await anonymous.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/webp", ok.Content.Headers.ContentType?.MediaType);

        var parts = path.Split('/');
        parts[^1] = new string(parts[^1].Reverse().ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);
        parts = path.Split('/');
        parts[^2] = "1000000000";
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);

        // A student photo link for the same id can't open a receipt: each kind of link has its own purpose.
        var photoLink = api.Services.GetRequiredService<SchoolPhotoSigner>().Url(id);
        var photoParts = new Uri(photoLink).PathAndQuery.Split('/');
        var swapped = $"/api/school/expense-receipts/{id}/{photoParts[^2]}/{photoParts[^1]}";
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(swapped, Ct)).StatusCode);
        var receiptParts = path.Split('/');
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/school/photos/{id}/{receiptParts[^2]}/{receiptParts[^1]}", Ct)).StatusCode);

        var key = await api.WithDbAsync(db => db.SchoolExpenses.IgnoreQueryFilters().Where(e => e.Id == id).Select(e => e.ReceiptKey).SingleAsync(Ct));
        Assert.StartsWith("private/", key);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/uploads/{key}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/expenses/{id}/receipt", Ct)).StatusCode);
        Assert.False((await Json(await school.Owner.GetAsync($"/api/school/expenses/{id}", Ct))).GetProperty("hasReceipt").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path, Ct)).StatusCode);
    }

    [Fact]
    public async Task Collections_are_reported_by_class_method_and_fee_item()
    {
        var f = await FeesAsync("Collections Academy", ("Tuition", 8_000_000), ("Books", 2_000_000));
        var s1 = await kit.StudentAsync(f.School, f.ClassA, "Ada");
        var s2 = await kit.StudentAsync(f.School, f.ClassB, "Bola");
        await GenerateAsync(f);
        var i1 = await InvoiceIdAsync(f, s1); var i2 = await InvoiceIdAsync(f, s2);
        // A discount lowers the invoice but isn't a fee item: the payment is shared across the items only.
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/invoices/{i1}/discount", new { kind = "Amount", value = 1_000_000, reason = "Staff child" }, Ct));
        await Json(await PayAsync(f.School.Owner, i1, 4_500_000, "Cash"));
        await Json(await PayAsync(f.School.Owner, i2, 3_000_000, "Transfer"));
        await Json(await PayAsync(f.School.Owner, i2, 1_000_000, "Pos"));
        var mistake = (await Json(await PayAsync(f.School.Owner, i2, 500_000, "Cash"))).GetProperty("id").GetGuid();
        await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{mistake}/reverse", new { reason = "Entered twice" }, Ct));

        async Task<Dictionary<string, long>> By(string by)
        {
            var report = await Json(await f.School.Owner.GetAsync($"/api/school/finance/collections?termId={f.TermId}&by={by}", Ct));
            Assert.Equal(8_500_000L, report.GetProperty("total").GetInt64());
            var rows = report.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("label").GetString()!, r => r.GetProperty("amountMinor").GetInt64());
            Assert.Equal(8_500_000L, rows.Values.Sum());
            return rows;
        }
        Assert.Equal(new Dictionary<string, long> { ["JSS 1A"] = 4_500_000, ["JSS 1B"] = 4_000_000 }, await By("class"));
        Assert.Equal(new Dictionary<string, long> { ["Cash"] = 4_500_000, ["Bank transfer"] = 3_000_000, ["POS"] = 1_000_000 }, await By("method"));
        // Ada: 4,500,000 split 80/20 = 3,600,000 and 900,000. Bola: 4,000,000 split = 3,200,000 and 800,000.
        Assert.Equal(new Dictionary<string, long> { ["Tuition"] = 6_800_000, ["Books"] = 1_700_000 }, await By("item"));

        await ProblemAsync(await f.School.Owner.GetAsync($"/api/school/finance/collections?termId={f.TermId}&by=colour", Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.GetAsync($"/api/school/finance/collections?termId={f.TermId}", Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await f.School.Owner.GetAsync($"/api/school/finance/collections?termId={Guid.NewGuid()}&by=class", Ct), HttpStatusCode.NotFound);
    }
}
