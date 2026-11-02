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
/// Score import (kind <c>scores</c>, a roster template): one row per student of the class, one column per assessment component, saved
/// through the score sheet's own save. A blank cell leaves the stored score alone; an unchanged row is not written. Also the two engine
/// fixes carried from Task 2: a CSV round trip keeps formula-safe cells unchanged, and the missing-column check comes before the class is
/// looked at.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportScoreTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string Header = "admission_no,student,CA1,CA2,Exam,sheet_for,row_version";
    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string NotInClass = "This student is not in this class.";
    private const string TermLocked = "This term is locked for the class, so scores can't change. Ask the principal to unlock it.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
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

    /// <summary>
    /// A score file; every line gets <paramref name="sheetFor"/> as the template prefills it, then a blank row_version: right for a student with
    /// no stored scores (a test that changes stored scores takes the version from the real template).
    /// </summary>
    private static string Csv(string sheetFor, params string[] lines) =>
        string.Join("\r\n", new[] { Header }.Concat(lines.Select(l => $"{l},\"{sheetFor}\","))) + "\r\n";

    private static string SheetForMessage(string label) => $"This sheet was made for {label}. Download the template for this class and subject.";

    /// <summary>What the template's sheet_for cell says for this class subject and term: "2026/2027 / JSS 1A / Mathematics / First term".</summary>
    private async Task<string> SheetForAsync(Guid classSubjectId, Guid termId) => await api.WithDbAsync(async db =>
    {
        var cs = await db.ClassSubjects.IgnoreQueryFilters().FirstAsync(c => c.Id == classSubjectId, Ct);
        var cls = await db.SchoolClasses.IgnoreQueryFilters().FirstAsync(c => c.Id == cs.ClassId, Ct);
        var session = await db.AcademicSessions.IgnoreQueryFilters().Where(x => x.Id == cls.SessionId).Select(x => x.Name).FirstAsync(Ct);
        var subject = await db.Subjects.IgnoreQueryFilters().Where(x => x.Id == cs.SubjectId).Select(x => x.Name).FirstAsync(Ct);
        var term = await db.Terms.IgnoreQueryFilters().Where(t => t.Id == termId).Select(t => t.Name).FirstAsync(Ct);
        return $"{session} / {cls.Name} / {subject} / {term}";
    });

    private static byte[] SaveWorkbook(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int ColumnOf(IXLWorksheet sheet, string heading) =>
        Enumerable.Range(1, 20).First(c => sheet.Cell(1, c).GetString() == heading);

    /// <summary>Downloads the real .xlsx template, types <paramref name="ca1"/> into every CA1 cell and, when given, retypes every sheet_for cell.</summary>
    private static async Task<byte[]> FilledTemplateAsync(HttpClient client, Guid classSubjectId, Guid termId, string ca1, string? sheetFor = null)
    {
        var response = await client.GetAsync("/api/school/imports/scores/template.xlsx" + Scope(classSubjectId, termId), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        var sheet = workbook.Worksheet("Scores");
        var (ca1Column, forColumn) = (ColumnOf(sheet, "CA1"), ColumnOf(sheet, "sheet_for"));
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            row.Cell(ca1Column).Value = ca1;
            if (sheetFor is not null) row.Cell(forColumn).Value = sheetFor;
        }
        return SaveWorkbook(workbook);
    }

    private static string Scope(Guid classSubjectId, Guid termId) => $"?classSubjectId={classSubjectId}&termId={termId}";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Guid classSubjectId, Guid termId, string csv) =>
        client.PostAsync("/api/school/imports/scores/preview" + Scope(classSubjectId, termId), Form(Encoding.UTF8.GetBytes(csv), "scores.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewFileAsync(HttpClient client, Guid classSubjectId, Guid termId, byte[] bytes, string fileName) =>
        client.PostAsync("/api/school/imports/scores/preview" + Scope(classSubjectId, termId), Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private static void AssertCounts(JsonElement preview, int @new, int update, int unchanged, int error)
    {
        var c = preview.GetProperty("counts");
        Assert.Equal((@new, update, unchanged, error),
            (c.GetProperty("new").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("error").GetInt32()));
    }

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid MathsId, Guid EnglishId, Guid TermId, Guid TeacherStaffId, HttpClient Teacher,
        string Ada, string Tolu, string MathsFor, string EnglishFor);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Guid> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code, Guid? teacherStaffId)
    {
        var subjects = (await Json(await school.Owner.GetAsync("/api/school/subjects", Ct))).EnumerateArray().ToList();
        var found = subjects.FirstOrDefault(x => x.GetProperty("code").GetString() == code);
        var subjectId = found.ValueKind != JsonValueKind.Undefined
            ? found.GetProperty("id").GetGuid()
            : (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct))).GetProperty("id").GetGuid();
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId, teacherStaffId }, Ct));
        return link.GetProperty("id").GetGuid();
    }

    private async Task<string> AdmissionNoAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.Id == studentId).Select(x => x.AdmissionNo).FirstAsync(Ct));

    /// <summary>JSS 1A with Ada Zed and Tolu Obi; Mathematics taught by the teacher, English by nobody.</summary>
    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var staffId = await StaffIdAsync(school, userId);
        var maths = await ClassSubjectAsync(school, classId, "Mathematics", "MTH", staffId);
        var english = await ClassSubjectAsync(school, classId, "English", "ENG", null);
        var ada = await AdmissionNoAsync(await kit.StudentAsync(school, classId, "Ada", "Zed"));
        var tolu = await AdmissionNoAsync(await kit.StudentAsync(school, classId, "Tolu", "Obi"));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var mathsFor = await SheetForAsync(maths, termId);
        Assert.Matches(@"^\d{4}/\d{4} / JSS 1A / Mathematics / ", mathsFor);
        return new Setup(school, classId, maths, english, termId, staffId, teacher, ada, tolu, mathsFor, await SheetForAsync(english, termId));
    }

    private static async Task<JsonElement> GridAsync(HttpClient client, Guid classSubjectId, Guid termId) =>
        await Json(await client.GetAsync($"/api/school/scores?classSubjectId={classSubjectId}&termId={termId}", Ct));

    private static Guid ComponentId(JsonElement grid, string name) =>
        grid.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static Guid EnrolmentOf(JsonElement grid, string firstName) =>
        grid.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName)).GetProperty("enrolmentId").GetGuid();

    private static async Task TypedSaveAsync(HttpClient client, Guid classSubjectId, Guid termId, params (Guid Enrolment, Guid Component, decimal? Value)[] entries) =>
        (await client.PutAsJsonAsync("/api/school/scores", new
        {
            classSubjectId, termId,
            entries = entries.Select(e => new { enrolmentId = e.Enrolment, componentId = e.Component, value = e.Value }).ToArray(),
        }, Ct)).EnsureSuccessStatusCode();

    private Task<List<Score>> ScoresAsync(Guid tenantId, Guid classSubjectId) =>
        api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.ClassSubjectId == classSubjectId)
            .OrderBy(x => x.EnrolmentId).ThenBy(x => x.ComponentId).ToListAsync(Ct));

    [Fact]
    public async Task Scores_import_matches_typed_save()
    {
        var s = await SetupAsync("Score Import School");
        var grid = await GridAsync(s.Teacher, s.MathsId, s.TermId);
        var ca1 = ComponentId(grid, "CA1");
        var exam = ComponentId(grid, "Exam");

        var preview = await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50", $"{s.Tolu},Tolu Obi,,,")));
        Assert.Equal("scores", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal([2, 3], rows.Select(r => r.GetProperty("row").GetInt32()));
        Assert.Equal("New", rows[0].GetProperty("status").GetString());
        Assert.Contains(s.Ada, rows[0].GetProperty("label").GetString());
        Assert.Contains("Ada Zed", rows[0].GetProperty("label").GetString());
        Assert.Equal("Unchanged", rows[1].GetProperty("status").GetString());
        AssertCounts(preview, 1, 0, 1, 0);
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.MathsId));

        var done = await Json(await ConfirmAsync(s.Teacher, preview.GetProperty("token").GetString()!));
        Assert.Equal(1, done.GetProperty("created").GetInt32());
        Assert.Equal(0, done.GetProperty("updated").GetInt32());

        // The same cells typed on the sheet for Tolu write the same rows as the import wrote for Ada.
        await TypedSaveAsync(s.Teacher, s.MathsId, s.TermId, (EnrolmentOf(grid, "Tolu"), ca1, 15m), (EnrolmentOf(grid, "Tolu"), exam, 50m));
        var stored = await ScoresAsync(s.School.TenantId, s.MathsId);
        var imported = stored.Where(x => x.EnrolmentId == EnrolmentOf(grid, "Ada")).OrderBy(x => x.ComponentId).ToList();
        var typed = stored.Where(x => x.EnrolmentId == EnrolmentOf(grid, "Tolu")).OrderBy(x => x.ComponentId).ToList();
        Assert.Equal(2, imported.Count);
        Assert.Equal(typed.Select(x => (x.ComponentId, x.Value, x.EnteredByStaffId, x.TermId)), imported.Select(x => (x.ComponentId, x.Value, x.EnteredByStaffId, x.TermId)));
        Assert.Equal(s.TeacherStaffId, imported[0].EnteredByStaffId);
        Assert.Equal(15m, imported.Single(x => x.ComponentId == ca1).Value);
        Assert.Equal(50m, imported.Single(x => x.ComponentId == exam).Value);

        var audit = await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == s.School.TenantId && a.Action == "imports.scores").SingleAsync(Ct));
        Assert.Equal("ImportBatch", audit.EntityType);
    }

    [Fact]
    public async Task Blank_cell_keeps_stored_score()
    {
        var s = await SetupAsync("Score Blank School");
        var grid = await GridAsync(s.Teacher, s.MathsId, s.TermId);
        var (ada, ca1, ca2, exam) = (EnrolmentOf(grid, "Ada"), ComponentId(grid, "CA1"), ComponentId(grid, "CA2"), ComponentId(grid, "Exam"));
        await TypedSaveAsync(s.Teacher, s.MathsId, s.TermId, (ada, ca1, 18m), (ada, ca2, 12m));
        // Ada has stored scores, so the row carries the version from the real template.
        var version = (await ImportTemplateKit.VersionsAsync(s.Teacher, "/api/school/imports/scores/template.csv" + Scope(s.MathsId, s.TermId)))[s.Ada];
        Assert.NotEmpty(version);

        var preview = await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, $"{Header}\r\n{s.Ada},Ada Zed,,14,,\"{s.MathsFor}\",{version}\r\n"));
        Assert.Equal("Update", Rows(preview)[0].GetProperty("status").GetString());
        AssertCounts(preview, 0, 1, 0, 0);
        var done = await Json(await ConfirmAsync(s.Teacher, preview.GetProperty("token").GetString()!));
        Assert.Equal(0, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());

        var stored = await ScoresAsync(s.School.TenantId, s.MathsId);
        Assert.Equal(18m, stored.Single(x => x.EnrolmentId == ada && x.ComponentId == ca1).Value);
        Assert.Equal(14m, stored.Single(x => x.EnrolmentId == ada && x.ComponentId == ca2).Value);
        Assert.DoesNotContain(stored, x => x.ComponentId == exam);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task Unchanged_round_trip_writes_nothing()
    {
        var s = await SetupAsync("Score Round Trip School");
        var grid = await GridAsync(s.Teacher, s.MathsId, s.TermId);
        var (ada, ca1, exam) = (EnrolmentOf(grid, "Ada"), ComponentId(grid, "CA1"), ComponentId(grid, "Exam"));
        await TypedSaveAsync(s.Teacher, s.MathsId, s.TermId, (ada, ca1, 18m), (ada, exam, 55.5m));
        var before = (await ScoresAsync(s.School.TenantId, s.MathsId)).Select(x => (x.Id, x.Value, x.EnteredByStaffId, x.UpdatedAt)).ToList();
        Assert.Equal(2, before.Count);

        // The owner downloads and uploads: a written cell would carry the owner's staff id, so "nothing written" is visible.
        var xlsx = await s.School.Owner.GetAsync("/api/school/imports/scores/template.xlsx" + Scope(s.MathsId, s.TermId), Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var xlsxBytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(xlsxBytes)))
        {
            var sheet = workbook.Worksheet(1);
            Assert.Equal(["admission_no", "student", "CA1", "CA2", "Exam", "sheet_for"], Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()));
            var adaRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.Ada);
            Assert.Equal(s.MathsFor, sheet.Cell(adaRow, 6).GetString());
            Assert.Equal("Ada Zed", sheet.Cell(adaRow, 2).GetString());
            Assert.Equal("18", sheet.Cell(adaRow, 3).GetString());
            Assert.Equal("", sheet.Cell(adaRow, 4).GetString());
            Assert.Equal("55.5", sheet.Cell(adaRow, 5).GetString());
            Assert.Contains(workbook.Worksheets, w => w.Name == "How to fill in");
        }

        var preview = await Json(await PreviewFileAsync(s.School.Owner, s.MathsId, s.TermId, xlsxBytes, "scores.xlsx"));
        Assert.All(Rows(preview), r => Assert.Equal("Unchanged", r.GetProperty("status").GetString()));
        AssertCounts(preview, 0, 0, 2, 0);
        Assert.Equal(0, preview.GetProperty("warnings").GetArrayLength());
        var done = await Json(await ConfirmAsync(s.School.Owner, preview.GetProperty("token").GetString()!));
        Assert.Equal(0, done.GetProperty("created").GetInt32());
        Assert.Equal(0, done.GetProperty("updated").GetInt32());

        var csv = await s.School.Owner.GetAsync("/api/school/imports/scores/template.csv" + Scope(s.MathsId, s.TermId), Ct);
        var csvBytes = await csv.Content.ReadAsByteArrayAsync(Ct);
        Assert.Contains($"\"{s.MathsFor}\"", Encoding.UTF8.GetString(csvBytes));
        var csvPreview = await Json(await PreviewFileAsync(s.School.Owner, s.MathsId, s.TermId, csvBytes, "scores.csv"));
        AssertCounts(csvPreview, 0, 0, 2, 0);
        Assert.Equal(0, csvPreview.GetProperty("warnings").GetArrayLength());
        await Json(await ConfirmAsync(s.School.Owner, csvPreview.GetProperty("token").GetString()!));

        var after = (await ScoresAsync(s.School.TenantId, s.MathsId)).Select(x => (x.Id, x.Value, x.EnteredByStaffId, x.UpdatedAt)).ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Over_maximum_is_error()
    {
        var s = await SetupAsync("Score Maximum School");
        var grid = await GridAsync(s.Teacher, s.MathsId, s.TermId);

        var preview = await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,25,,", $"{s.Tolu},Tolu Obi,10,,40")));
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal("CA1 scores must be between 0 and 20.", rows[0].GetProperty("message").GetString());
        Assert.Equal("New", rows[1].GetProperty("status").GetString());
        AssertCounts(preview, 1, 0, 0, 1);

        var problem = await ProblemAsync(await ConfirmAsync(s.Teacher, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal(ErrorsMessage, problem.GetProperty("detail").GetString());
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.MathsId));

        // Not a number, a negative score, two decimals and a thousands comma are errors too, with the sheet's sentences.
        var more = Rows(await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor,
            $"{s.Ada},Ada Zed,ten,,", $"{s.Tolu},Tolu Obi,-1,,")))).Concat(Rows(await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor,
            $"{s.Ada},Ada Zed,,,12.25", $"{s.Tolu},Tolu Obi,,,\"1,5\""))))).ToList();
        Assert.All(more, r => Assert.Equal("Error", r.GetProperty("status").GetString()));
        Assert.Equal("Enter a number for CA1, such as 12 or 12.5.", more[0].GetProperty("message").GetString());
        Assert.Equal("CA1 scores must be between 0 and 20.", more[1].GetProperty("message").GetString());
        Assert.Equal("Scores can have at most one decimal place.", more[2].GetProperty("message").GetString());
        Assert.Equal("Enter a number for Exam, such as 12 or 12.5.", more[3].GetProperty("message").GetString());
        Assert.Equal(2, grid.GetProperty("students").GetArrayLength());
    }

    [Fact]
    public async Task Template_for_another_class_is_refused()
    {
        var s = await SetupAsync("Score Other Class School");
        var classB = await kit.ClassAsync(s.School, "B");
        var mathsB = await ClassSubjectAsync(s.School, classB, "Mathematics", "MTH", s.TeacherStaffId);
        await kit.StudentAsync(s.School, classB, "Bayo", "Ade");

        // Class A's real template, filled in, uploaded with class B's scope: its sheet_for names class A.
        var filled = await FilledTemplateAsync(s.Teacher, s.MathsId, s.TermId, "10");
        var preview = await Json(await PreviewFileAsync(s.Teacher, mathsB, s.TermId, filled, "scores.xlsx"));
        var rows = Rows(preview);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Error", r.GetProperty("status").GetString());
            Assert.Equal(SheetForMessage(s.MathsFor), r.GetProperty("message").GetString());
        });
        await ProblemAsync(await ConfirmAsync(s.Teacher, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);

        // Someone retypes sheet_for to class B: the rows still name class A's students, so they are refused, never written onto class B.
        var retyped = await FilledTemplateAsync(s.Teacher, s.MathsId, s.TermId, "10", await SheetForAsync(mathsB, s.TermId));
        var again = Rows(await Json(await PreviewFileAsync(s.Teacher, mathsB, s.TermId, retyped, "scores.xlsx")));
        Assert.All(again, r =>
        {
            Assert.Equal("Error", r.GetProperty("status").GetString());
            Assert.Equal(NotInClass, r.GetProperty("message").GetString());
        });
        Assert.Empty(await ScoresAsync(s.School.TenantId, mathsB));
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.MathsId));

        // The same student twice in one file is an error on both rows.
        var twice = Rows(await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,10,,", $"{s.Ada},Ada Zed,11,,"))));
        Assert.All(twice, r => Assert.Equal("This student appears more than once in the file.", r.GetProperty("message").GetString()));
    }

    /// <summary>Review fix: the same class and term but another subject matches the roster, so only sheet_for stops Maths scores landing in English.</summary>
    [Fact]
    public async Task Template_for_another_subject_is_refused()
    {
        var s = await SetupAsync("Score Other Subject Sheet School");
        var filled = await FilledTemplateAsync(s.School.Owner, s.MathsId, s.TermId, "12");

        var preview = await Json(await PreviewFileAsync(s.School.Owner, s.EnglishId, s.TermId, filled, "scores.xlsx"));
        var rows = Rows(preview);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Error", r.GetProperty("status").GetString());
            Assert.Equal(SheetForMessage(s.MathsFor), r.GetProperty("message").GetString());
        });
        var problem = await ProblemAsync(await ConfirmAsync(s.School.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.EnglishId));
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.MathsId));

        // Case and extra spaces don't matter; the same file on its own subject is fine.
        var loose = Csv("  " + s.MathsFor.ToUpperInvariant().Replace(" / ", "  /   ") + " ", $"{s.Ada},Ada Zed,12,,");
        AssertCounts(await Json(await PreviewAsync(s.School.Owner, s.MathsId, s.TermId, loose)), 1, 0, 0, 0);
        AssertCounts(await Json(await PreviewFileAsync(s.School.Owner, s.MathsId, s.TermId, filled, "scores.xlsx")), 2, 0, 0, 0);

        // A blank sheet_for cell is refused too.
        var blank = Rows(await Json(await PreviewAsync(s.School.Owner, s.MathsId, s.TermId, Csv("", $"{s.Ada},Ada Zed,12,,"))));
        Assert.Equal("Error", blank[0].GetProperty("status").GetString());
        Assert.Equal("Keep the sheet_for cell as the template filled it. Download the template for this class and subject.", blank[0].GetProperty("message").GetString());
    }

    /// <summary>
    /// Task 3 carry: sheet_for names the session. Last session's JSS 1A has the same class, subject and term names and a repeating student is on
    /// both rosters, so without the session last year's Maths template would pass for this year's.
    /// </summary>
    [Fact]
    public async Task Template_from_last_session_is_refused()
    {
        var s = await SetupAsync("Score Last Session School");
        var adaId = await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.TenantId == s.School.TenantId && x.AdmissionNo == s.Ada).Select(x => x.Id).FirstAsync(Ct));
        var (oldMaths, oldTerm) = await api.WithDbAsync(async db =>
        {
            var cls = await db.SchoolClasses.IgnoreQueryFilters().FirstAsync(c => c.Id == s.ClassId, Ct);
            var term = await db.Terms.IgnoreQueryFilters().FirstAsync(t => t.Id == s.TermId, Ct);
            var current = await db.AcademicSessions.IgnoreQueryFilters().FirstAsync(x => x.Id == cls.SessionId, Ct);
            var mathsSubject = await db.ClassSubjects.IgnoreQueryFilters().Where(c => c.Id == s.MathsId).Select(c => c.SubjectId).FirstAsync(Ct);
            var start = current.StartDate.Year - 1;
            var session = new AcademicSession { TenantId = s.School.TenantId, Name = $"{start}/{start + 1}", StartDate = new DateOnly(start, 9, 1), EndDate = new DateOnly(start + 1, 8, 31) };
            var oldTermRow = new Term { TenantId = s.School.TenantId, SessionId = session.Id, Number = term.Number, Name = term.Name, StartDate = term.StartDate.AddYears(-1), EndDate = term.EndDate.AddYears(-1) };
            var oldClass = new SchoolClass { TenantId = s.School.TenantId, SessionId = session.Id, LevelId = cls.LevelId, Arm = cls.Arm, Name = cls.Name };
            var oldLink = new ClassSubject { TenantId = s.School.TenantId, ClassId = oldClass.Id, SubjectId = mathsSubject, TeacherStaffId = s.TeacherStaffId };
            db.AddRange(session, oldTermRow, oldClass, oldLink, new Enrolment { TenantId = s.School.TenantId, StudentId = adaId, SessionId = session.Id, ClassId = oldClass.Id });
            await db.SaveChangesAsync(Ct);
            return (oldLink.Id, oldTermRow.Id);
        });
        var oldFor = await SheetForAsync(oldMaths, oldTerm);
        Assert.NotEqual(s.MathsFor, oldFor);
        Assert.EndsWith(s.MathsFor[s.MathsFor.IndexOf(" / JSS 1A", StringComparison.Ordinal)..], oldFor);

        // Last session's real template, filled in, uploaded on this session's Maths.
        var filled = await FilledTemplateAsync(s.School.Owner, oldMaths, oldTerm, "10");
        var preview = await Json(await PreviewFileAsync(s.School.Owner, s.MathsId, s.TermId, filled, "scores.xlsx"));
        var row = Assert.Single(Rows(preview));
        Assert.Equal("Error", row.GetProperty("status").GetString());
        Assert.Equal(SheetForMessage(oldFor), row.GetProperty("message").GetString());
        await ProblemAsync(await ConfirmAsync(s.School.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.MathsId));
    }

    [Fact]
    public async Task File_without_sheet_for_is_refused()
    {
        var s = await SetupAsync("Score No Sheet For School");
        var csv = $"admission_no,student,CA1,CA2,Exam,row_version\r\n{s.Ada},Ada Zed,12,,,\r\n";
        var problem = await ProblemAsync(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, csv), HttpStatusCode.BadRequest);
        Assert.Equal("The file is missing the column sheet_for. Download the template to see every column.", problem.GetProperty("detail").GetString());
    }

    /// <summary>Review minor 4: headings that match no column are named in a preview warning, so a misspelt "Exams" is not silently skipped.</summary>
    [Fact]
    public async Task Unknown_headings_are_named_in_a_warning()
    {
        var s = await SetupAsync("Score Unknown Column School");
        var csv = $"admission_no,student,CA1,Exams,Total,sheet_for,row_version\r\n{s.Ada},Ada Zed,12,55,67,\"{s.MathsFor}\",\r\n";
        var preview = await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, csv));
        Assert.Equal(["These columns were ignored: exams and total."], preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
        AssertCounts(preview, 1, 0, 0, 0);
    }

    /// <summary>Review minor 9: the reader also takes off the apostrophe Csv.Cell puts before a leading tab or carriage return.</summary>
    [Fact]
    public void Csv_reader_strips_the_escape_before_tab_and_return()
    {
        var reader = new Saas.Infrastructure.Schools.SpreadsheetReader();
        var csv = "a,b,c,d\r\n\"'\tA01\",\"'\rB02\",\"'=C\",\"'plain\"\r\n";
        var row = Assert.Single(reader.Read(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "x.csv"));
        Assert.Equal(("A01", "B02", "=C", "'plain"), (row["a"], row["b"], row["c"], row["d"]));
    }

    [Fact]
    public async Task Lock_between_preview_and_confirm_refuses()
    {
        var s = await SetupAsync("Score Lock School");
        var preview = await Json(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50")));
        AssertCounts(preview, 1, 0, 0, 0);

        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();

        var problem = await ProblemAsync(await ConfirmAsync(s.Teacher, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal("term_locked", problem.GetProperty("code").GetString());
        Assert.Equal(TermLocked, problem.GetProperty("detail").GetString());
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.MathsId));

        // A preview of a locked term is refused the same way.
        var again = await ProblemAsync(await PreviewAsync(s.Teacher, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50")), HttpStatusCode.Conflict);
        Assert.Equal("term_locked", again.GetProperty("code").GetString());
        Assert.Equal(TermLocked, again.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Teacher_of_other_subject_404()
    {
        var s = await SetupAsync("Score Other Subject School");
        const string NotFound = "Class subject was not found.";

        var template = await ProblemAsync(await s.Teacher.GetAsync("/api/school/imports/scores/template.xlsx" + Scope(s.EnglishId, s.TermId), Ct), HttpStatusCode.NotFound);
        Assert.Equal(NotFound, template.GetProperty("detail").GetString());
        var preview = await ProblemAsync(await PreviewAsync(s.Teacher, s.EnglishId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50")), HttpStatusCode.NotFound);
        Assert.Equal(NotFound, preview.GetProperty("detail").GetString());
        var madeUp = await ProblemAsync(await PreviewAsync(s.Teacher, Guid.NewGuid(), s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50")), HttpStatusCode.NotFound);
        Assert.Equal(NotFound, madeUp.GetProperty("detail").GetString());

        // The class teacher reads every subject's scores but enters only their own, so the import is 404 for them as well.
        var (classTeacher, classTeacherUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId = await StaffIdAsync(s.School, classTeacherUser) }, Ct)).EnsureSuccessStatusCode();
        await ProblemAsync(await PreviewAsync(classTeacher, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50")), HttpStatusCode.NotFound);

        // The bursar is refused before anything is looked up, even for a made-up class subject.
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await ProblemAsync(await bursar.GetAsync("/api/school/imports/scores/template.csv" + Scope(Guid.NewGuid(), s.TermId), Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await PreviewAsync(bursar, s.MathsId, s.TermId, Csv(s.MathsFor, $"{s.Ada},Ada Zed,15,,50")), HttpStatusCode.Forbidden);

        // Leadership reaches English, which nobody teaches.
        var owner = await Json(await PreviewAsync(s.School.Owner, s.EnglishId, s.TermId, Csv(s.EnglishFor, $"{s.Ada},Ada Zed,15,,50")));
        AssertCounts(owner, 1, 0, 0, 0);
        Assert.Empty(await ScoresAsync(s.School.TenantId, s.EnglishId));
    }

    private async Task<(SchoolKit.School School, Guid ClassSubjectId, Guid TermId, string AdmissionNo)> NurseryAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var admissionNo = await AdmissionNoAsync(await kit.StudentAsync(school, classId, "Ada", "Zed"));
        var classSubjectId = await ClassSubjectAsync(school, classId, "Play", "PLY", null);
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return (school, classSubjectId, termId, admissionNo);
    }

    [Fact]
    public async Task Developmental_class_400()
    {
        var n = await NurseryAsync("Score Nursery School");
        const string Developmental = "This class uses developmental assessment.";

        var template = await ProblemAsync(await n.School.Owner.GetAsync("/api/school/imports/scores/template.xlsx" + Scope(n.ClassSubjectId, n.TermId), Ct), HttpStatusCode.BadRequest);
        Assert.Equal(Developmental, template.GetProperty("detail").GetString());
        var preview = await ProblemAsync(await PreviewAsync(n.School.Owner, n.ClassSubjectId, n.TermId, Csv("Nursery", $"{n.AdmissionNo},Ada Zed,15,,50")), HttpStatusCode.BadRequest);
        Assert.Equal(Developmental, preview.GetProperty("detail").GetString());
        Assert.Empty(await ScoresAsync(n.School.TenantId, n.ClassSubjectId));
    }

    /// <summary>Carried from Task 2 (b): the sheet name comes from the importer, not from building its template, so a file missing a column is told so first.</summary>
    [Fact]
    public async Task Missing_column_is_named_before_the_class_is_checked()
    {
        var n = await NurseryAsync("Score Column Order School");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Scores");
        sheet.Cell(1, 1).Value = "student";
        sheet.Cell(1, 2).Value = "CA1";
        sheet.Cell(2, 1).Value = "Ada Zed";
        sheet.Cell(2, 2).Value = "15";
        sheet.Cell(1, 3).Value = "sheet_for";
        sheet.Cell(2, 3).Value = "Nursery 1A / Play / First term";
        sheet.Cell(1, 4).Value = "row_version";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var problem = await ProblemAsync(await PreviewFileAsync(n.School.Owner, n.ClassSubjectId, n.TermId, stream.ToArray(), "scores.xlsx"), HttpStatusCode.BadRequest);
        Assert.Equal("The file is missing the column admission_no. Download the template to see every column.",
            problem.GetProperty("detail").GetString());
    }

    /// <summary>Carried from Task 2 (a): the CSV template escapes cells starting with = + - @ with an apostrophe, and the reader takes it off again.</summary>
    [Fact]
    public async Task Csv_round_trip_with_formula_safe_cells_is_unchanged()
    {
        var s = await SetupAsync("Score Apostrophe School");
        var student = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = "-A01", firstName = "@Bisi", lastName = "=Ola", gender = "Female", classId = s.ClassId }, Ct));
        var grid = await GridAsync(s.Teacher, s.MathsId, s.TermId);
        var bisi = grid.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("admissionNo").GetString() == "-A01").GetProperty("enrolmentId").GetGuid();
        Assert.NotEqual(Guid.Empty, student.GetProperty("id").GetGuid());
        await TypedSaveAsync(s.Teacher, s.MathsId, s.TermId, (bisi, ComponentId(grid, "CA1"), 9m));

        var csv = await (await s.Teacher.GetAsync("/api/school/imports/scores/template.csv" + Scope(s.MathsId, s.TermId), Ct)).Content.ReadAsByteArrayAsync(Ct);
        Assert.Contains("\"'-A01\"", Encoding.UTF8.GetString(csv));

        var preview = await Json(await PreviewFileAsync(s.Teacher, s.MathsId, s.TermId, csv, "scores.csv"));
        AssertCounts(preview, 0, 0, 3, 0);
    }
}
