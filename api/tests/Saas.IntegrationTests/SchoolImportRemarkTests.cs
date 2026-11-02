using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Subject comments and overall remarks (kind <c>remarks</c>), a roster template for one class and term, saved through the results screens'
/// own saves. Subject teachers get only the subjects they teach; leadership and the class teacher see every subject, but a comment is written
/// only by that subject's teacher or leadership, as on the child page. The class teacher's remark is the class teacher's or leadership's; the
/// principal's remark is leadership's only, and its column is absent from anyone else's template. Published results refuse the import.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportRemarkTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string Published = "Unpublish the results before changing remarks.";
    private const string PrincipalOnly = "Only the principal writes the principal's remark.";
    private const string ClassTeacherOnly = "Only the class teacher writes the class teacher's remark.";

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

    /// <summary>A CSV cell as a spreadsheet saves one: always quoted, quotes doubled, line breaks kept.</summary>
    private static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// A file with the heading line <paramref name="header"/> (sheet_for and row_version added last) and every line ending in
    /// <paramref name="sheetFor"/> and a blank row_version: right for a student with nothing stored that the caller can change.
    /// </summary>
    private static string Csv(string header, string sheetFor, params string[] lines) => CsvAt(header, sheetFor, null, lines);

    /// <summary>As <see cref="Csv"/>, with each line's row_version from <paramref name="versions"/> (the caller's real template) by its admission number.</summary>
    private static string CsvAt(string header, string sheetFor, IReadOnlyDictionary<string, string>? versions, params string[] lines) =>
        string.Join("\r\n", new[] { header + ",sheet_for,row_version" }
            .Concat(lines.Select(l => $"{l},{Q(sheetFor)},{versions?.GetValueOrDefault(l.Split(',')[0].Trim('"')) ?? ""}"))) + "\r\n";

    /// <summary>The row_version of each student in <paramref name="client"/>'s own template (it covers what that person can change).</summary>
    private static Task<Dictionary<string, string>> VersionsAsync(HttpClient client, Setup s) =>
        ImportTemplateKit.VersionsAsync(client, $"/api/school/imports/remarks/template.csv{Scope(s.ClassId, s.TermId)}");

    private static string Scope(Guid classId, Guid termId) => $"?classId={classId}&termId={termId}";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, Setup s, string csv) =>
        client.PostAsync($"/api/school/imports/remarks/preview{Scope(s.ClassId, s.TermId)}", Form(Encoding.UTF8.GetBytes(csv), "remarks.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewFileAsync(HttpClient client, Setup s, byte[] bytes, string fileName) =>
        client.PostAsync($"/api/school/imports/remarks/preview{Scope(s.ClassId, s.TermId)}", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static Task<HttpResponseMessage> TemplateAsync(HttpClient client, Setup s, string ext = "xlsx") =>
        client.GetAsync($"/api/school/imports/remarks/template.{ext}{Scope(s.ClassId, s.TermId)}", Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private static void AssertCounts(JsonElement preview, int @new, int update, int unchanged, int error)
    {
        var c = preview.GetProperty("counts");
        Assert.Equal((@new, update, unchanged, error),
            (c.GetProperty("new").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("error").GetInt32()));
    }

    private static string Token(JsonElement preview) => preview.GetProperty("token").GetString()!;

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<string> AdmissionNoAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.Id == studentId).Select(x => x.AdmissionNo).FirstAsync(Ct));

    private async Task<Guid> EnrolmentAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>"2026/2027 / JSS 1A / Comments and remarks / First term".</summary>
    private async Task<string> SheetForAsync(Setup s) => await api.WithDbAsync(async db =>
    {
        var cls = await db.SchoolClasses.IgnoreQueryFilters().FirstAsync(c => c.Id == s.ClassId, Ct);
        var session = await db.AcademicSessions.IgnoreQueryFilters().Where(x => x.Id == cls.SessionId).Select(x => x.Name).FirstAsync(Ct);
        var term = await db.Terms.IgnoreQueryFilters().Where(t => t.Id == s.TermId).Select(t => t.Name).FirstAsync(Ct);
        return $"{session} / {cls.Name} / Comments and remarks / {term}";
    });

    private sealed record Setup(
        SchoolKit.School School, Guid ClassId, Guid TermId, Guid MathId, Guid EnglishId,
        HttpClient ClassTeacher, HttpClient MathTeacher, HttpClient OtherTeacher, HttpClient Bursar,
        Guid AdaId, Guid AdaEnrolment, string Ada, Guid ToluId, Guid ToluEnrolment, string Tolu);

    /// <summary>
    /// JSS 1A with Ada Zed and Tolu Obi, a class teacher (who teaches no subject), Mathematics taught by the maths teacher and English taught by
    /// another teacher, plus a teacher with nothing in the class and a bursar.
    /// </summary>
    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (classTeacher, classUser) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        var (mathTeacher, mathUser) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var (_, englishUser) = await kit.StaffAsync(school, MemberRole.Staff, "English teacher");
        var (otherTeacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Other teacher");
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = await StaffIdAsync(school, classUser) }, Ct)).EnsureSuccessStatusCode();

        async Task<Guid> LinkAsync(string subjectName, string code, Guid teacherUser)
        {
            var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = subjectName, code }, Ct));
            var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
                new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = await StaffIdAsync(school, teacherUser) }, Ct));
            return link.GetProperty("id").GetGuid();
        }
        var mathId = await LinkAsync("Mathematics", "MTH", mathUser);
        var englishId = await LinkAsync("English", "ENG", englishUser);

        var adaId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var toluId = await kit.StudentAsync(school, classId, "Tolu", "Obi");
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

        // A subject comment needs the student's result in that subject, so both students are scored in both subjects.
        var enrolments = new[] { await EnrolmentAsync(adaId), await EnrolmentAsync(toluId) };
        foreach (var classSubjectId in new[] { mathId, englishId })
        {
            var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={classSubjectId}&termId={termId}", Ct));
            var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
            var entries = enrolments.Select(enrolmentId => new { enrolmentId, componentId = components[0], value = 10m }).ToArray();
            (await school.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId, termId, entries }, Ct)).EnsureSuccessStatusCode();
        }
        return new Setup(school, classId, termId, mathId, englishId, classTeacher, mathTeacher, otherTeacher, bursar,
            adaId, await EnrolmentAsync(adaId), await AdmissionNoAsync(adaId), toluId, await EnrolmentAsync(toluId), await AdmissionNoAsync(toluId));
    }

    private static string ResultsUrl(Setup s) => $"/api/school/results/{s.ClassId}/{s.TermId}";

    private sealed record Saved(string? Maths, string? English, string? Teacher, string? Principal);

    /// <summary>What the results screen shows for one student.</summary>
    private static async Task<Saved> SavedAsync(Setup s, Guid enrolmentId)
    {
        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var row = results.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == enrolmentId);
        string? Subject(string name) => row.GetProperty("subjects").EnumerateArray()
            .Single(x => x.GetProperty("subject").GetString() == name).GetProperty("remark").GetString();
        return new Saved(Subject("Mathematics"), Subject("English"), row.GetProperty("teacherRemark").GetString(), row.GetProperty("principalRemark").GetString());
    }

    /// <summary>Typed on the screens by the owner: subject comments, then both remarks.</summary>
    private static async Task TypedAsync(Setup s, Guid enrolmentId, Saved values)
    {
        // The results must exist before a comment can be written; the screen opens them first.
        (await s.School.Owner.GetAsync(ResultsUrl(s), Ct)).EnsureSuccessStatusCode();
        foreach (var (classSubjectId, comment) in new[] { (s.MathId, values.Maths), (s.EnglishId, values.English) }.Where(x => x.Item2 is not null))
            (await s.School.Owner.PutAsJsonAsync($"{ResultsUrl(s)}/subject-comments", new { enrolmentId, classSubjectId, comment }, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PutAsJsonAsync($"{ResultsUrl(s)}/remarks",
            new { enrolmentId, teacherRemark = values.Teacher, principalRemark = values.Principal }, Ct)).EnsureSuccessStatusCode();
    }

    private static List<string> Headings(byte[] xlsx)
    {
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var sheet = workbook.Worksheet("Remarks");
        return Enumerable.Range(1, sheet.LastColumnUsed()!.ColumnNumber()).Select(c => sheet.Cell(1, c).GetString()).ToList();
    }

    [Fact]
    public async Task Subject_teacher_template_has_only_own_subjects()
    {
        var s = await SetupAsync("Remark Template School");
        await TypedAsync(s, s.AdaEnrolment, new Saved("Good effort.", "Reads well.", "A calm term.", "Well done."));

        // The maths teacher: Mathematics only, no remarks.
        var math = await TemplateAsync(s.MathTeacher, s);
        Assert.Equal(HttpStatusCode.OK, math.StatusCode);
        var mathBytes = await math.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(["admission_no", "student", "Mathematics", "sheet_for", "row_version"], Headings(mathBytes));
        using (var workbook = new XLWorkbook(new MemoryStream(mathBytes)))
        {
            var sheet = workbook.Worksheet("Remarks");
            var adaRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.Ada);
            Assert.Equal(("Ada Zed", "Good effort.", await SheetForAsync(s)), (sheet.Cell(adaRow, 2).GetString(), sheet.Cell(adaRow, 3).GetString(), sheet.Cell(adaRow, 4).GetString()));
        }

        // The class teacher sees every subject and writes the class teacher's remark; leadership also writes the principal's.
        Assert.Equal(["admission_no", "student", "English", "Mathematics", "class_teacher_remark", "sheet_for", "row_version"],
            Headings(await (await TemplateAsync(s.ClassTeacher, s)).Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal(["admission_no", "student", "English", "Mathematics", "class_teacher_remark", "principal_remark", "sheet_for", "row_version"],
            Headings(await (await TemplateAsync(s.School.Owner, s)).Content.ReadAsByteArrayAsync(Ct)));

        // Unchanged, the maths teacher's own template is all Unchanged with no warnings.
        var roundTrip = await Json(await PreviewFileAsync(s.MathTeacher, s, mathBytes, "remarks.xlsx"));
        AssertCounts(roundTrip, 0, 0, 2, 0);
        Assert.Equal(0, roundTrip.GetProperty("warnings").GetArrayLength());

        var sheetFor = await SheetForAsync(s);
        // A maths teacher's file with the class teacher's remark filled in is an error row, as the screen keeps that box closed to them.
        var row = Assert.Single(Rows(await Json(await PreviewAsync(s.MathTeacher, s,
            Csv("admission_no,student,Mathematics,class_teacher_remark", sheetFor, $"{s.Ada},Ada Zed,Better now.,Kind and calm.")))));
        Assert.Equal(("Error", ClassTeacherOnly), (row.GetProperty("status").GetString(), row.GetProperty("message").GetString()));

        // The class teacher sees English but does not teach it: an unchanged cell is fine, a changed one is an error, as on the child page.
        var ct = Rows(await Json(await PreviewAsync(s.ClassTeacher, s,
            CsvAt("admission_no,student,English,class_teacher_remark", sheetFor, await VersionsAsync(s.ClassTeacher, s),
                $"{s.Ada},Ada Zed,Reads well.,A calmer term.", $"{s.Tolu},Tolu Obi,Speaks up more.,"))));
        Assert.Equal("Update", ct[0].GetProperty("status").GetString());
        Assert.Equal(("Error", "The subject teacher writes the English comment."), (ct[1].GetProperty("status").GetString(), ct[1].GetProperty("message").GetString()));

        // The bursar is refused before anything is looked up; a teacher with nothing in the class gets the class's 404.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Bursar.GetAsync($"/api/school/imports/remarks/template.xlsx{Scope(Guid.NewGuid(), Guid.NewGuid())}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(s.Bursar, s, Csv("admission_no,Mathematics", sheetFor, $"{s.Ada},x"))).StatusCode);
        var other = await ProblemAsync(await TemplateAsync(s.OtherTeacher, s), HttpStatusCode.NotFound);
        Assert.Equal("Class was not found.", other.GetProperty("detail").GetString());
        await ProblemAsync(await PreviewAsync(s.OtherTeacher, s, Csv("admission_no,Mathematics", sheetFor, $"{s.Ada},x")), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Remarks_import_saves()
    {
        var s = await SetupAsync("Remark Import School");
        var sheetFor = await SheetForAsync(s);

        var preview = await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,Mathematics,English,class_teacher_remark,principal_remark", sheetFor,
                $"{s.Ada},Ada Zed,Strong in algebra.,  Reads widely.  ,A focused term.,Keep it up.", $"{s.Tolu},Tolu Obi,,,,")));
        Assert.Equal("remarks", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal("New", rows[0].GetProperty("status").GetString());
        Assert.Contains("Ada Zed", rows[0].GetProperty("label").GetString());
        Assert.Equal("Unchanged", rows[1].GetProperty("status").GetString());
        AssertCounts(preview, 1, 0, 1, 0);
        Assert.Equal(new Saved(null, null, null, null), await SavedAsync(s, s.AdaEnrolment));

        var done = await Json(await ConfirmAsync(s.School.Owner, Token(preview)));
        Assert.Equal((1, 0), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        // Trimmed as the screen trims it.
        Assert.Equal(new Saved("Strong in algebra.", "Reads widely.", "A focused term.", "Keep it up."), await SavedAsync(s, s.AdaEnrolment));
        Assert.Equal(new Saved(null, null, null, null), await SavedAsync(s, s.ToluEnrolment));

        // The same file again is Unchanged; blank cells keep what is saved.
        AssertCounts(await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,Mathematics,English,class_teacher_remark,principal_remark", sheetFor,
                $"{s.Ada},Ada Zed,Strong in algebra.,Reads widely.,A focused term.,Keep it up."))), 0, 0, 1, 0);
        AssertCounts(await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,Mathematics,English,class_teacher_remark,principal_remark", sheetFor, $"{s.Ada},Ada Zed,,,,"))), 0, 0, 1, 0);

        // The maths teacher changes Ada's maths comment only; the class teacher changes the class teacher's remark only.
        var math = await Json(await PreviewAsync(s.MathTeacher, s,
            CsvAt("admission_no,student,Mathematics", sheetFor, await VersionsAsync(s.MathTeacher, s), $"{s.Ada},Ada Zed,Much improved.")));
        Assert.Equal("Update", Assert.Single(Rows(math)).GetProperty("status").GetString());
        var mathDone = await Json(await ConfirmAsync(s.MathTeacher, Token(math)));
        Assert.Equal((0, 1), (mathDone.GetProperty("created").GetInt32(), mathDone.GetProperty("updated").GetInt32()));
        var teacher = await Json(await PreviewAsync(s.ClassTeacher, s,
            CsvAt("admission_no,student,class_teacher_remark", sheetFor, await VersionsAsync(s.ClassTeacher, s),
                $"{s.Ada},Ada Zed,Settled well.", $"{s.Tolu},Tolu Obi,Helpful in class.")));
        AssertCounts(teacher, 1, 1, 0, 0);
        await Json(await ConfirmAsync(s.ClassTeacher, Token(teacher)));
        Assert.Equal(new Saved("Much improved.", "Reads widely.", "Settled well.", "Keep it up."), await SavedAsync(s, s.AdaEnrolment));
        Assert.Equal(new Saved(null, null, "Helpful in class.", null), await SavedAsync(s, s.ToluEnrolment));

        // A student who is not in the class is an error row.
        var stranger = Assert.Single(Rows(await Json(await PreviewAsync(s.School.Owner, s, Csv("admission_no,student,Mathematics", sheetFor, "NOPE-1,Someone,Hello")))));
        Assert.Equal("This student is not in this class.", stranger.GetProperty("message").GetString());

        // A student with no maths result yet (no scores) can't get a maths comment, as the child page shows no maths box; a remark is fine.
        var bolaId = await kit.StudentAsync(s.School, s.ClassId, "Bola", "Ade");
        var bola = await AdmissionNoAsync(bolaId);
        var noResult = Rows(await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,Mathematics,class_teacher_remark", sheetFor, $"{bola},Bola Ade,Hello.,"))));
        Assert.Equal(("Error", "This student has no Mathematics result yet. Enter their scores first."),
            (noResult[0].GetProperty("status").GetString(), noResult[0].GetProperty("message").GetString()));
        var remarkOnly = Assert.Single(Rows(await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,Mathematics,class_teacher_remark", sheetFor, $"{bola},Bola Ade,,New this term.")))));
        Assert.Equal("New", remarkOnly.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Comment_over_500_error()
    {
        var s = await SetupAsync("Remark Length School");
        var sheetFor = await SheetForAsync(s);
        var exactly500 = new string('a', 500);
        var preview = await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,Mathematics,class_teacher_remark", sheetFor,
                $"{s.Ada},Ada Zed,{new string('b', 501)},", $"{s.Tolu},Tolu Obi,  {exactly500}  ,{new string('c', 501)}")));
        var rows = Rows(preview);
        Assert.Equal(("Error", "The Mathematics comment can have at most 500 characters."), (rows[0].GetProperty("status").GetString(), rows[0].GetProperty("message").GetString()));
        // 500 characters inside spaces is within the limit once trimmed, as on the screen; the remark next to it is not.
        Assert.Equal(("Error", "The class teacher's remark can have at most 500 characters."), (rows[1].GetProperty("status").GetString(), rows[1].GetProperty("message").GetString()));
        AssertCounts(preview, 0, 0, 0, 2);

        var problem = await ProblemAsync(await ConfirmAsync(s.School.Owner, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal(ErrorsMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(new Saved(null, null, null, null), await SavedAsync(s, s.ToluEnrolment));

        // Exactly 500 after trimming saves.
        var ok = await Json(await PreviewAsync(s.School.Owner, s, Csv("admission_no,student,Mathematics", sheetFor, $"{s.Tolu},Tolu Obi,  {exactly500}  ")));
        AssertCounts(ok, 1, 0, 0, 0);
        await Json(await ConfirmAsync(s.School.Owner, Token(ok)));
        Assert.Equal(exactly500, (await SavedAsync(s, s.ToluEnrolment)).Maths);
    }

    [Fact]
    public async Task Published_results_refused()
    {
        var s = await SetupAsync("Remark Publish School");
        var sheetFor = await SheetForAsync(s);
        var csv = Csv("admission_no,student,Mathematics", sheetFor, $"{s.Ada},Ada Zed,Steady.");

        // Previewed while still a draft, then locked and published before the confirm: the confirm is refused and nothing is written.
        var preview = await Json(await PreviewAsync(s.School.Owner, s, csv));
        AssertCounts(preview, 1, 0, 0, 0);
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();

        var confirm = await ProblemAsync(await ConfirmAsync(s.School.Owner, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(Published, confirm.GetProperty("detail").GetString());
        Assert.Equal("results_published", confirm.GetProperty("code").GetString());
        Assert.Null((await SavedAsync(s, s.AdaEnrolment)).Maths);

        // A new preview is refused the same way, for the maths teacher too.
        var again = await ProblemAsync(await PreviewAsync(s.School.Owner, s, csv), HttpStatusCode.Conflict);
        Assert.Equal(Published, again.GetProperty("detail").GetString());
        Assert.Equal(Published, (await ProblemAsync(await PreviewAsync(s.MathTeacher, s, csv), HttpStatusCode.Conflict)).GetProperty("detail").GetString());

        // Unpublished, the same file imports.
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        var after = await Json(await PreviewAsync(s.School.Owner, s, csv));
        await Json(await ConfirmAsync(s.School.Owner, Token(after)));
        Assert.Equal("Steady.", (await SavedAsync(s, s.AdaEnrolment)).Maths);
    }

    [Fact]
    public async Task Principal_column_rejected_for_teacher()
    {
        var s = await SetupAsync("Remark Principal School");
        var sheetFor = await SheetForAsync(s);

        // The class teacher's template has no principal_remark column; one added by hand and filled in is an error row.
        var preview = await Json(await PreviewAsync(s.ClassTeacher, s,
            Csv("admission_no,student,class_teacher_remark,principal_remark", sheetFor,
                $"{s.Ada},Ada Zed,A good term.,Excellent work.", $"{s.Tolu},Tolu Obi,Kind to others.,")));
        var rows = Rows(preview);
        Assert.Equal(("Error", PrincipalOnly), (rows[0].GetProperty("status").GetString(), rows[0].GetProperty("message").GetString()));
        // Left blank, the column does no harm.
        Assert.Equal("New", rows[1].GetProperty("status").GetString());
        Assert.Equal(0, preview.GetProperty("warnings").GetArrayLength());
        var problem = await ProblemAsync(await ConfirmAsync(s.ClassTeacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal(new Saved(null, null, null, null), await SavedAsync(s, s.AdaEnrolment));

        // The maths teacher gets the same row error.
        var math = Assert.Single(Rows(await Json(await PreviewAsync(s.MathTeacher, s,
            Csv("admission_no,student,Mathematics,principal_remark", sheetFor, $"{s.Ada},Ada Zed,Fine.,Excellent work.")))));
        Assert.Equal(PrincipalOnly, math.GetProperty("message").GetString());

        // Leadership writes it.
        var owner = await Json(await PreviewAsync(s.School.Owner, s, Csv("admission_no,student,principal_remark", sheetFor, $"{s.Ada},Ada Zed,Excellent work.")));
        await Json(await ConfirmAsync(s.School.Owner, Token(owner)));
        Assert.Equal("Excellent work.", (await SavedAsync(s, s.AdaEnrolment)).Principal);
    }

    [Fact]
    public async Task Free_text_csv_round_trip_is_lossless()
    {
        var s = await SetupAsync("Remark Round Trip School");
        // Commas, quotes, line breaks, a tab inside, and values that start with = + - @ or with an apostrophe before one of them.
        var ada = new Saved("Good, \"steady\" work,\nkeeps going.", "=SUM(A1:A3)", "-5 marks lost\tthen +3 gained.", "@home and at school.");
        var tolu = new Saved("+ve attitude, always.", "'=quoted on purpose", "Line one\nLine two, with \"quotes\"", "'@ the start");
        await TypedAsync(s, s.AdaEnrolment, ada);
        await TypedAsync(s, s.ToluEnrolment, tolu);
        Assert.Equal(ada, await SavedAsync(s, s.AdaEnrolment));
        Assert.Equal(tolu, await SavedAsync(s, s.ToluEnrolment));

        // The CSV template, uploaded unchanged, reads back exactly as stored: every row is Unchanged.
        var csv = await TemplateAsync(s.School.Owner, s, "csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var csvBytes = await csv.Content.ReadAsByteArrayAsync(Ct);
        var roundTrip = await Json(await PreviewFileAsync(s.School.Owner, s, csvBytes, "remarks.csv"));
        AssertCounts(roundTrip, 0, 0, 2, 0);
        Assert.Equal(0, roundTrip.GetProperty("warnings").GetArrayLength());
        // So does the .xlsx template.
        var xlsxBytes = await (await TemplateAsync(s.School.Owner, s)).Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(xlsxBytes)))
        {
            var sheet = workbook.Worksheet("Remarks");
            var toluRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.Tolu);
            Assert.Equal(("'=quoted on purpose", "'@ the start"), (sheet.Cell(toluRow, 3).GetString(), sheet.Cell(toluRow, 6).GetString()));
        }
        AssertCounts(await Json(await PreviewFileAsync(s.School.Owner, s, xlsxBytes, "remarks.xlsx")), 0, 0, 2, 0);

        // New text with the same characters, typed into a spreadsheet and saved as CSV, is stored exactly.
        var sheetFor = await SheetForAsync(s);
        var maths = "Line \"one\", =3+4\nLine two\tend";
        var english = "-minus first, then @at";
        var preview = await Json(await PreviewAsync(s.School.Owner, s,
            CsvAt("admission_no,student,Mathematics,English", sheetFor, await VersionsAsync(s.School.Owner, s), $"{Q(s.Tolu)},{Q("Tolu Obi")},{Q(maths)},{Q(english)}")));
        AssertCounts(preview, 0, 1, 0, 0);
        await Json(await ConfirmAsync(s.School.Owner, Token(preview)));
        Assert.Equal(tolu with { Maths = maths, English = english }, await SavedAsync(s, s.ToluEnrolment));
        Assert.Equal(ada, await SavedAsync(s, s.AdaEnrolment));
    }

    /// <summary>
    /// Carried to Task 11 from Task 5: an apostrophe that is not before = + - @ (such as 'abc) is plain text and reads as typed from a hand-made
    /// CSV and from an .xlsx. A hand-made CSV cell ''=x loses exactly one apostrophe (the template escape rule), so it is stored as '=x.
    /// </summary>
    [Fact]
    public async Task Leading_apostrophe_reads_as_typed()
    {
        var s = await SetupAsync("Remark Apostrophe School");
        var sheetFor = await SheetForAsync(s);

        // A hand-made CSV.
        var csv = await Json(await PreviewAsync(s.School.Owner, s,
            Csv("admission_no,student,class_teacher_remark,principal_remark", sheetFor, $"{Q(s.Tolu)},{Q("Tolu Obi")},{Q("'abc")},{Q("''=x")}")));
        AssertCounts(csv, 1, 0, 0, 0);
        await Json(await ConfirmAsync(s.School.Owner, Token(csv)));
        Assert.Equal(new Saved(null, null, "'abc", "'=x"), await SavedAsync(s, s.ToluEnrolment));

        // An .xlsx typed by hand, with the cell holding the text 'abc.
        byte[] xlsx;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Remarks");
            // row_version left blank: Ada has nothing saved yet.
            string[] headings = ["admission_no", "student", "class_teacher_remark", "sheet_for", "row_version"];
            for (var c = 0; c < headings.Length; c++) sheet.Cell(1, c + 1).SetValue(headings[c]);
            sheet.Cell(2, 1).SetValue(s.Ada);
            sheet.Cell(2, 2).SetValue("Ada Zed");
            // ClosedXML reads one leading apostrophe as Excel's "show as text" prefix and drops it (as the template writer notes), so the
            // text 'abc is set with a second one.
            sheet.Cell(2, 3).Value = "''abc";
            Assert.Equal("'abc", sheet.Cell(2, 3).GetString());
            sheet.Cell(2, 4).SetValue(sheetFor);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            xlsx = stream.ToArray();
        }
        var fromXlsx = await Json(await PreviewFileAsync(s.School.Owner, s, xlsx, "remarks.xlsx"));
        AssertCounts(fromXlsx, 1, 0, 0, 0);
        await Json(await ConfirmAsync(s.School.Owner, Token(fromXlsx)));
        Assert.Equal("'abc", (await SavedAsync(s, s.AdaEnrolment)).Teacher);

        // Both templates, uploaded unchanged, read back as stored.
        var csvTemplate = await (await TemplateAsync(s.School.Owner, s, "csv")).Content.ReadAsByteArrayAsync(Ct);
        AssertCounts(await Json(await PreviewFileAsync(s.School.Owner, s, csvTemplate, "remarks.csv")), 0, 0, 2, 0);
        var xlsxTemplate = await (await TemplateAsync(s.School.Owner, s)).Content.ReadAsByteArrayAsync(Ct);
        AssertCounts(await Json(await PreviewFileAsync(s.School.Owner, s, xlsxTemplate, "remarks.xlsx")), 0, 0, 2, 0);
    }
}
