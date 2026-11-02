using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>
/// The final review's fixes to the import engine.
/// I1: every roster template (except attendance, which has its register_version row) carries a hidden <c>row_version</c> column; a row
/// that would change something whose stored values changed after the download is an error, on preview and again on confirm.
/// I2: a confirmed batch is kept with its result, so a repeat confirm by the same user returns that result with <c>alreadyImported</c>.
/// M-1: the timetable preview counts removals. M-2: concurrent previews clear expired batches without failing. M-3: CSV template headings
/// are formula-escaped and still read back.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportFinalFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string Stale = "Someone changed this row after you downloaded the template. Download it again.";
    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string Expired = "This import has expired. Upload the file again.";

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

    private static MultipartFormDataContent Form(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string kind, string scope, byte[] bytes, string fileName) =>
        client.PostAsync($"/api/school/imports/{kind}/preview{scope}", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> PreviewCsvAsync(HttpClient client, string kind, string scope, string csv) =>
        PreviewAsync(client, kind, scope, Encoding.UTF8.GetBytes(csv), $"{kind}.csv");

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, JsonElement preview) =>
        client.PostAsync($"/api/school/imports/{preview.GetProperty("token").GetString()}/confirm", null, Ct);

    private static async Task<byte[]> TemplateAsync(HttpClient client, string kind, string scope, string extension)
    {
        var response = await client.GetAsync($"/api/school/imports/{kind}/template.{extension}{scope}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();
    private static string Status(JsonElement row) => row.GetProperty("status").GetString()!;
    private static string? Message(JsonElement row) => row.GetProperty("message").GetString();
    private static int Count(JsonElement preview, string name) => preview.GetProperty("counts").GetProperty(name).GetInt32();

    private static int ColumnOf(IXLWorksheet sheet, string heading) =>
        Enumerable.Range(1, 30).First(c => sheet.Cell(1, c).GetString() == heading);

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<string> AdmissionNoAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.Id == studentId).Select(x => x.AdmissionNo).FirstAsync(Ct));

    private Task<Guid> CurrentTermAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

    // ---------- Scores ----------

    private sealed record ScoreSetup(SchoolKit.School School, Guid ClassId, Guid MathsId, Guid TermId, HttpClient Teacher, string Ada, string Tolu, Guid AdaEnrolment, Guid Ca1, Guid Exam)
    {
        public string Scope => $"?classSubjectId={MathsId}&termId={TermId}";
    }

    private async Task<ScoreSetup> ScoreSetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var subjectId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct))).GetProperty("id").GetGuid();
        var maths = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId, teacherStaffId = await StaffIdAsync(school, userId) }, Ct))).GetProperty("id").GetGuid();
        var ada = await AdmissionNoAsync(await kit.StudentAsync(school, classId, "Ada", "Zed"));
        var tolu = await AdmissionNoAsync(await kit.StudentAsync(school, classId, "Tolu", "Obi"));
        var termId = await CurrentTermAsync(school);
        var grid = await Json(await teacher.GetAsync($"/api/school/scores?classSubjectId={maths}&termId={termId}", Ct));
        Guid Component(string n) => grid.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == n).GetProperty("id").GetGuid();
        var adaEnrolment = grid.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith("Ada")).GetProperty("enrolmentId").GetGuid();
        return new ScoreSetup(school, classId, maths, termId, teacher, ada, tolu, adaEnrolment, Component("CA1"), Component("Exam"));
    }

    private static async Task SaveScoreAsync(HttpClient client, ScoreSetup s, Guid componentId, decimal value) =>
        (await client.PutAsJsonAsync("/api/school/scores", new
        {
            classSubjectId = s.MathsId, termId = s.TermId,
            entries = new[] { new { enrolmentId = s.AdaEnrolment, componentId, value = (decimal?)value } },
        }, Ct)).EnsureSuccessStatusCode();

    private Task<List<Score>> ScoresAsync(ScoreSetup s) =>
        api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == s.School.TenantId && x.ClassSubjectId == s.MathsId)
            .OrderBy(x => x.Id).ToListAsync(Ct));

    /// <summary>
    /// I1, the review's scenario: the teacher downloads the template, the principal corrects Ada's CA1 on the score sheet, the teacher fills
    /// in the Exam column of the old file and uploads it. Ada's row would put CA1 back, so it is an error and nothing is written.
    /// </summary>
    [Fact]
    public async Task Stale_score_template_is_refused_and_writes_nothing()
    {
        var s = await ScoreSetupAsync("Stale Score Template School");
        await SaveScoreAsync(s.Teacher, s, s.Ca1, 10m);

        var xlsx = await TemplateAsync(s.Teacher, "scores", s.Scope, "xlsx");
        var csv = await TemplateAsync(s.Teacher, "scores", s.Scope, "csv");
        await SaveScoreAsync(s.School.Owner, s, s.Ca1, 12m); // the principal's correction, after the download
        var before = (await ScoresAsync(s)).Select(x => (x.Id, x.ComponentId, x.Value, x.UpdatedAt)).ToList();

        byte[] filled;
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx)))
        {
            var sheet = workbook.Worksheet("Scores");
            var version = ColumnOf(sheet, "row_version");
            Assert.True(sheet.Column(version).IsHidden, "row_version is a hidden column");
            Assert.Equal(version, sheet.LastColumnUsed()!.ColumnNumber());
            var exam = ColumnOf(sheet, "Exam");
            foreach (var row in sheet.RowsUsed().Skip(1)) row.Cell(exam).Value = "50";
            filled = Save(workbook);
        }

        var preview = await Json(await PreviewAsync(s.Teacher, "scores", s.Scope, filled, "scores.xlsx"));
        var rows = Rows(preview);
        var ada = rows.Single(r => r.GetProperty("label").GetString()!.StartsWith(s.Ada));
        Assert.Equal(("Error", Stale), (Status(ada), Message(ada)));
        Assert.Equal("New", Status(rows.Single(r => r.GetProperty("label").GetString()!.StartsWith(s.Tolu))));
        var refused = await ProblemAsync(await ConfirmAsync(s.Teacher, preview), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Equal(before, (await ScoresAsync(s)).Select(x => (x.Id, x.ComponentId, x.Value, x.UpdatedAt)).ToList());

        // A stale row that changes nothing is never refused: the old CSV with Ada's CA1 typed as the new 12 is Unchanged.
        var text = Encoding.UTF8.GetString(csv).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        Assert.EndsWith(",row_version", text[0]);
        var adaLine = text.FindIndex(l => l.Contains(s.Ada, StringComparison.Ordinal));
        text[adaLine] = text[adaLine].Replace("\"10\"", "\"12\"");
        var same = await Json(await PreviewCsvAsync(s.Teacher, "scores", s.Scope, string.Join("\r\n", text) + "\r\n"));
        Assert.All(Rows(same), r => Assert.Equal("Unchanged", Status(r)));

        // A file without the column is the engine's missing-column 400.
        var noColumn = await ProblemAsync(await PreviewCsvAsync(s.Teacher, "scores", s.Scope,
            $"admission_no,student,CA1,CA2,Exam,sheet_for\r\n{s.Ada},Ada Zed,,,50,x\r\n"), HttpStatusCode.BadRequest);
        Assert.Equal("The file is missing the column row_version. Download the template to see every column.", noColumn.GetProperty("detail").GetString());
    }

    /// <summary>I1: a preview that was fine, then a change on the score sheet, then the confirm: 409 import_errors with the stale row, nothing written.</summary>
    [Fact]
    public async Task Score_changed_on_screen_between_preview_and_confirm_is_409()
    {
        var s = await ScoreSetupAsync("Stale Score Confirm School");
        await SaveScoreAsync(s.Teacher, s, s.Ca1, 10m);
        var xlsx = await TemplateAsync(s.Teacher, "scores", s.Scope, "xlsx");
        byte[] filled;
        using (var workbook = new XLWorkbook(new MemoryStream(xlsx)))
        {
            var sheet = workbook.Worksheet("Scores");
            var adaRow = sheet.RowsUsed().Skip(1).Single(r => r.Cell(1).GetString() == s.Ada);
            adaRow.Cell(ColumnOf(sheet, "CA1")).Value = "15";
            filled = Save(workbook);
        }
        var preview = await Json(await PreviewAsync(s.Teacher, "scores", s.Scope, filled, "scores.xlsx"));
        Assert.Equal((0, 1, 1, 0), (Count(preview, "new"), Count(preview, "update"), Count(preview, "unchanged"), Count(preview, "error")));

        await SaveScoreAsync(s.School.Owner, s, s.Ca1, 12m);
        var problem = await ProblemAsync(await ConfirmAsync(s.Teacher, preview), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        var row = Assert.Single(problem.GetProperty("rows").EnumerateArray());
        Assert.Equal(Stale, Message(row));
        Assert.Equal(12m, (await ScoresAsync(s)).Single(x => x.ComponentId == s.Ca1).Value);
    }

    // ---------- Fee structure ----------

    /// <summary>
    /// I1: the bursar downloads the fee structure, the principal raises tuition on the screen, and the bursar uploads the old file with a
    /// new Lab amount filled in. The tuition row would put the old amount back, so it is an error and nothing is written.
    /// </summary>
    [Fact]
    public async Task Stale_fee_structure_template_is_refused_and_writes_nothing()
    {
        var school = await kit.SchoolAsync("Stale Fee Structure School");
        var classId = await kit.ClassAsync(school);
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await CurrentTermAsync(school);
        var tuition = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Lab", isOptional = false }, Ct));
        async Task SetTuitionAsync(long amount) => await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = tuition, amountMinor = amount } } }, Ct));
        await SetTuitionAsync(5_000_000L);
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        var scope = $"?termId={termId}&levelId={levelId}";

        var csv = Encoding.UTF8.GetString(await TemplateAsync(bursar, "fee-structure", scope, "csv")).TrimStart('﻿');
        await SetTuitionAsync(5_500_000L); // the principal's change, after the download
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        Assert.Equal("fee_item,amount,sheet_for,row_version", lines[0]);
        var lab = lines.FindIndex(l => l.StartsWith("\"Lab\"", StringComparison.Ordinal));
        lines[lab] = lines[lab].Replace("\"Lab\",\"\"", "\"Lab\",\"2000\"");

        var preview = await Json(await PreviewCsvAsync(bursar, "fee-structure", scope, string.Join("\r\n", lines) + "\r\n"));
        var rows = Rows(preview);
        Assert.Equal("New", Status(rows.Single(r => r.GetProperty("label").GetString() == "Lab")));
        var stale = rows.Single(r => r.GetProperty("label").GetString() == "Tuition");
        Assert.Equal(("Error", Stale), (Status(stale), Message(stale)));
        await ProblemAsync(await ConfirmAsync(bursar, preview), HttpStatusCode.Conflict);

        var stored = await api.WithDbAsync(db => db.FeeStructureLines.IgnoreQueryFilters().Where(l => l.TermId == termId && l.LevelId == levelId)
            .Select(l => l.AmountMinor).ToListAsync(Ct));
        Assert.Equal([5_500_000L], stored);
    }

    // ---------- Timetable ----------

    private sealed record TimetableSetup(SchoolKit.School School, Guid ClassId, Guid Maths, Guid English)
    {
        public string Scope => $"?classId={ClassId}";
    }

    private async Task<TimetableSetup> TimetableSetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        async Task<Guid> LinkAsync(string subject, string code)
        {
            var subjectId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = subject, code }, Ct))).GetProperty("id").GetGuid();
            return (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId, teacherStaffId = (Guid?)null }, Ct))).GetProperty("id").GetGuid();
        }
        var s = new TimetableSetup(school, classId, await LinkAsync("Mathematics", "MTH"), await LinkAsync("English", "ENG"));
        await SaveWeekAsync(s, ("Mon", 1, "08:00", "08:40", s.Maths), ("Mon", 2, "08:40", "09:20", s.English), ("Tue", 1, "08:00", "08:40", s.Maths));
        return s;
    }

    private static async Task SaveWeekAsync(TimetableSetup s, params (string Day, int Period, string Start, string End, Guid ClassSubjectId)[] slots) =>
        (await s.School.Owner.PutAsJsonAsync($"/api/school/timetable/{s.ClassId}",
            slots.Select(x => new { day = x.Day, period = x.Period, start = x.Start, end = x.End, classSubjectId = x.ClassSubjectId }).ToArray(), Ct)).EnsureSuccessStatusCode();

    /// <summary>I1 for the timetable: the version is per lesson. A new lesson row with a blank version is fine; a lesson the file leaves out is not checked.</summary>
    [Fact]
    public async Task Timetable_row_version_is_per_lesson()
    {
        var s = await TimetableSetupAsync("Stale Timetable School");
        var csv = Encoding.UTF8.GetString(await TemplateAsync(s.School.Owner, "timetable", s.Scope, "csv")).TrimStart('﻿');
        // After the download, Monday period 1 moves to 08:05 on the screen (Tuesday is unchanged).
        await SaveWeekAsync(s, ("Mon", 1, "08:05", "08:40", s.Maths), ("Mon", 2, "08:40", "09:20", s.English), ("Tue", 1, "08:00", "08:40", s.Maths));

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        Assert.Equal("day,period,start,end,subject,sheet_for,row_version", lines[0]);
        var sheetFor = lines[1].Split(',')[5];
        string Change(string prefix, string from, string to)
        {
            var i = lines.FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));
            return lines[i].Replace(from, to);
        }
        var file = string.Join("\r\n",
            lines[0],
            Change("\"Monday\",\"1\"", "\"Mathematics\"", "\"English\""),   // stale: changed on the screen since the download
            Change("\"Tuesday\",\"1\"", "\"Mathematics\"", "\"English\""),  // fresh: its own version still matches
            $"Friday,1,08:00,08:40,Mathematics,{sheetFor},") + "\r\n";      // a new lesson with a blank version; Monday period 2 is left out
        var preview = await Json(await PreviewCsvAsync(s.School.Owner, "timetable", s.Scope, file));
        Assert.Equal(["Error", "Update", "New"], Rows(preview).Select(Status));
        Assert.Equal(Stale, Message(Rows(preview)[0]));
        Assert.Equal(1, Count(preview, "remove"));
    }

    /// <summary>M-1: the preview counts the lessons a timetable file removes, so a file that only removes lessons can be confirmed from the dialog.</summary>
    [Fact]
    public async Task Timetable_preview_counts_removals()
    {
        var s = await TimetableSetupAsync("Timetable Removal Count School");
        var lines = Encoding.UTF8.GetString(await TemplateAsync(s.School.Owner, "timetable", s.Scope, "csv")).TrimStart('﻿')
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        var onlyMonday = string.Join("\r\n", lines.Take(2)) + "\r\n";

        var preview = await Json(await PreviewCsvAsync(s.School.Owner, "timetable", s.Scope, onlyMonday));
        Assert.Equal((0, 0, 1, 0, 2), (Count(preview, "new"), Count(preview, "update"), Count(preview, "unchanged"), Count(preview, "error"), Count(preview, "remove")));
        var done = await Json(await ConfirmAsync(s.School.Owner, preview));
        Assert.Equal(2, done.GetProperty("removed").GetInt32());

        // Every other kind reports remove as 0.
        var subjects = await Json(await PreviewCsvAsync(s.School.Owner, "subjects", "", "name,code\r\nBiology,BIO\r\n"));
        Assert.Equal(0, Count(subjects, "remove"));
    }

    // ---------- Idempotent confirm (I2) ----------

    private sealed record PaymentSetup(SchoolKit.School School, string InvoiceNo, Guid TermId);

    private async Task<PaymentSetup> PaymentSetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await CurrentTermAsync(school);
        var tuition = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = tuition, amountMinor = 10_000_000L } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));
        var invoice = (await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}", Ct))).EnumerateArray().Single();
        return new PaymentSetup(school, invoice.GetProperty("number").GetString()!, termId);
    }

    private static string CashFile(PaymentSetup p) =>
        $"invoice_no,admission_no,amount,method,reference,paid_on\r\n{p.InvoiceNo},,5000,Cash,,{DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1).AddDays(-1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\r\n";

    private Task<int> PaymentCountAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == school.TenantId, Ct));

    /// <summary>I2: a confirm whose response was lost is confirmed again. The second call returns the first result and records nothing more.</summary>
    [Fact]
    public async Task Confirm_twice_returns_the_same_result_and_records_once()
    {
        var p = await PaymentSetupAsync("Confirm Twice School");
        var preview = await Json(await PreviewCsvAsync(p.School.Owner, "payments", $"?termId={p.TermId}", CashFile(p)));
        Assert.Equal(1, Count(preview, "new"));

        var first = await Json(await ConfirmAsync(p.School.Owner, preview));
        Assert.Equal((1, 0), (first.GetProperty("created").GetInt32(), first.GetProperty("updated").GetInt32()));
        Assert.False(first.TryGetProperty("alreadyImported", out var flag) && flag.GetBoolean());
        Assert.Equal(1, await PaymentCountAsync(p.School));

        var second = await Json(await ConfirmAsync(p.School.Owner, preview));
        Assert.Equal((1, 0, true), (second.GetProperty("created").GetInt32(), second.GetProperty("updated").GetInt32(), second.GetProperty("alreadyImported").GetBoolean()));
        Assert.Equal(1, await PaymentCountAsync(p.School));
        // One audit entry and one notice: the repeat writes nothing.
        Assert.Equal(1, await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.TenantId == p.School.TenantId && a.Action == "imports.payments", Ct)));

        // Once its life is over, the token is "expired" like any other.
        await api.WithDbAsync(async db =>
        {
            var batch = await db.ImportBatches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == p.School.TenantId, Ct);
            batch.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync(Ct);
        });
        var expired = await ProblemAsync(await ConfirmAsync(p.School.Owner, preview), HttpStatusCode.NotFound);
        Assert.Equal(Expired, expired.GetProperty("detail").GetString());
        Assert.Equal(1, await PaymentCountAsync(p.School));
    }

    /// <summary>I2: another user's token is still 404 "expired", before and after it is confirmed.</summary>
    [Fact]
    public async Task Another_users_token_is_404()
    {
        var p = await PaymentSetupAsync("Other User Token School");
        var (bursar, _) = await kit.StaffAsync(p.School, MemberRole.Accountant, "Bursar");
        var preview = await Json(await PreviewCsvAsync(p.School.Owner, "payments", $"?termId={p.TermId}", CashFile(p)));

        var before = await ProblemAsync(await ConfirmAsync(bursar, preview), HttpStatusCode.NotFound);
        Assert.Equal(("import_expired", Expired), (before.GetProperty("code").GetString(), before.GetProperty("detail").GetString()));
        await Json(await ConfirmAsync(p.School.Owner, preview));
        var after = await ProblemAsync(await ConfirmAsync(bursar, preview), HttpStatusCode.NotFound);
        Assert.Equal(Expired, after.GetProperty("detail").GetString());
        Assert.Equal(1, await PaymentCountAsync(p.School));
    }

    // ---------- Expired batches (M-2) ----------

    /// <summary>M-2: previews at the same moment each clear the school's expired batches; none of them fails while doing it.</summary>
    [Fact]
    public async Task Concurrent_previews_clear_expired_batches()
    {
        var school = await kit.SchoolAsync("Concurrent Preview School");
        for (var round = 0; round < 4; round++)
        {
            await api.WithDbAsync(async db =>
            {
                for (var i = 0; i < 5; i++)
                    db.ImportBatches.Add(new ImportBatch
                    {
                        TenantId = school.TenantId, Token = Guid.NewGuid().ToString("N"), Kind = "subjects", Payload = "{}",
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), CreatedBy = Guid.Empty,
                    });
                return await db.SaveChangesAsync(Ct);
            });
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                PreviewCsvAsync(school.Owner, "subjects", "", $"name,code\r\nSubject {round}{i},S{round}{i}\r\n")));
            foreach (var response in responses) await Json(response);
        }
        Assert.Equal(0, await api.WithDbAsync(db => db.ImportBatches.IgnoreQueryFilters()
            .CountAsync(b => b.TenantId == school.TenantId && b.ExpiresAt < DateTimeOffset.UtcNow, Ct)));
    }

    // ---------- CSV headings (M-3) ----------

    /// <summary>M-3: a component named "=CA1" is written as a formula-safe heading in the CSV template, and the file still reads back unchanged.</summary>
    [Fact]
    public async Task Csv_template_heading_is_formula_escaped_and_round_trips()
    {
        var s = await ScoreSetupAsync("Formula Heading School");
        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/assessment", new
        {
            components = new object[] { new { name = "=CA1", maxScore = 20 }, new { name = "CA2", maxScore = 20 }, new { name = "Exam", maxScore = 60 } },
        }, Ct));
        var grid = await Json(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.MathsId}&termId={s.TermId}", Ct));
        var ca1 = grid.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "=CA1").GetProperty("id").GetGuid();
        await SaveScoreAsync(s.Teacher, s, ca1, 9m);

        var csv = Encoding.UTF8.GetString(await TemplateAsync(s.Teacher, "scores", s.Scope, "csv")).TrimStart('﻿');
        Assert.StartsWith("admission_no,student,'=CA1,CA2,Exam,sheet_for,row_version\r\n", csv);

        var preview = await Json(await PreviewCsvAsync(s.Teacher, "scores", s.Scope, csv));
        Assert.All(Rows(preview), r => Assert.Equal("Unchanged", Status(r)));
        Assert.Empty(preview.GetProperty("warnings").EnumerateArray());

        // Typed over, the escaped heading still reads as the =CA1 column.
        var changed = Rows(await Json(await PreviewCsvAsync(s.Teacher, "scores", s.Scope, csv.Replace("\"9\"", "\"11\""))));
        Assert.Equal("Update", Status(changed.Single(r => r.GetProperty("label").GetString()!.StartsWith(s.Ada))));
    }
}
