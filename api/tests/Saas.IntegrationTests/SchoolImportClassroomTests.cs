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
/// Classroom imports. Assignment marks (kind <c>assignment-marks</c>, roster, scope <c>assignmentId</c>) go through the marking path, so a
/// counted mark writes the score through the single writer and a locked term refuses it. Exam sittings (kind <c>exam-sittings</c>, list,
/// scope <c>periodId</c>) go through the add-sitting path with its class and venue clash sentences, also between rows of the same file; an
/// invigilator clash is a warning. The timetable (kind <c>timetable</c>, roster of the current slots, scope <c>classId</c>) replaces the
/// class's week as the timetable screen's save does; an unchanged template is a no-op and a teacher clash is a warning.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportClassroomTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static readonly DateOnly ExamDay = new(2026, 11, 16);
    private static readonly DateOnly NextDay = new(2026, 11, 17);
    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string TermLocked = "This term is locked for the class, so scores can't change. Ask the principal to unlock it.";
    private const string ClassOverlap = "This sitting overlaps another exam for the class.";
    private const string VenueOverlap = "This venue is already booked at that time.";
    private const string InvigilatorWarning = "This teacher is already invigilating another exam at that time.";

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

    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string kind, string scope, string csv) =>
        client.PostAsync($"/api/school/imports/{kind}/preview{scope}", Form(Encoding.UTF8.GetBytes(csv), $"{kind}.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewFileAsync(HttpClient client, string kind, string scope, byte[] bytes, string fileName) =>
        client.PostAsync($"/api/school/imports/{kind}/preview{scope}", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();
    private static List<string> Warnings(JsonElement preview) => preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();
    private static string Token(JsonElement preview) => preview.GetProperty("token").GetString()!;

    private static void AssertCounts(JsonElement preview, int @new, int update, int unchanged, int error)
    {
        var c = preview.GetProperty("counts");
        Assert.Equal((@new, update, unchanged, error),
            (c.GetProperty("new").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("error").GetInt32()));
    }

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<string> EmailAsync(Guid userId) => await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstAsync(Ct));

    private async Task<string> AdmissionNoAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.Id == studentId).Select(x => x.AdmissionNo).FirstAsync(Ct));

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    private Task<Guid> CurrentTermAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

    private async Task<string> SessionNameAsync(Guid classId) => await api.WithDbAsync(async db =>
    {
        var sessionId = await db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.SessionId).FirstAsync(Ct);
        return await db.AcademicSessions.IgnoreQueryFilters().Where(x => x.Id == sessionId).Select(x => x.Name).FirstAsync(Ct);
    });

    private async Task<string> TermNameAsync(Guid termId) =>
        await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.Id == termId).Select(t => t.Name).FirstAsync(Ct));

    private async Task<Guid> SubjectLinkAsync(SchoolKit.School school, Guid classId, Guid subjectId, Guid? teacherStaffId) =>
        (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId, teacherStaffId }, Ct))).GetProperty("id").GetGuid();

    private async Task<Guid> SubjectAsync(SchoolKit.School school, string name, string code) =>
        (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct))).GetProperty("id").GetGuid();

    // ---------- Assignment marks ----------

    private sealed record MarkSetup(SchoolKit.School School, Guid ClassId, Guid ClassSubjectId, Guid TermId, HttpClient Teacher,
        Guid AdaId, Guid Ada, string AdaNo, Guid ToluId, Guid Tolu, string ToluNo);

    private async Task<MarkSetup> MarkSetupAsync(string name, bool nursery = false)
    {
        var school = await kit.SchoolAsync(name);
        Guid classId;
        if (nursery)
        {
            var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
                new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
            classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "A" }, Ct))).GetProperty("id").GetGuid();
        }
        else classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Subject teacher");
        var subjectId = nursery ? await SubjectAsync(school, "Numeracy", "NUM") : await SubjectAsync(school, "Mathematics", "MTH");
        var classSubjectId = await SubjectLinkAsync(school, classId, subjectId, await StaffIdAsync(school, userId));
        var adaId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var toluId = await kit.StudentAsync(school, classId, "Tolu", "Obi");
        return new MarkSetup(school, classId, classSubjectId, await CurrentTermAsync(school), teacher,
            adaId, await EnrolmentAsync(school, adaId), await AdmissionNoAsync(adaId), toluId, await EnrolmentAsync(school, toluId), await AdmissionNoAsync(toluId));
    }

    private static async Task<Guid> AssignmentAsync(MarkSetup s, string title, Guid? componentId, decimal? maxMarks) =>
        (await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, title, instructions = "Show the working.",
            dueAt = DateTimeOffset.UtcNow.AddDays(3), maxMarks, assessmentComponentId = componentId,
        }, Ct))).GetProperty("id").GetGuid();

    private static async Task<Guid> ComponentAsync(MarkSetup s, string name) =>
        (await Json(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.ClassSubjectId}&termId={s.TermId}", Ct)))
        .GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static async Task<JsonElement> SubmissionAsync(MarkSetup s, Guid assignmentId, Guid enrolmentId) =>
        (await Json(await s.Teacher.GetAsync($"/api/school/assignments?classSubjectId={s.ClassSubjectId}&termId={s.TermId}", Ct)))
        .EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == assignmentId)
        .GetProperty("submissions").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == enrolmentId);

    /// <summary>
    /// "2026/2027 / JSS 1A / Mathematics / First term / CA1 homework / due 2026-10-25 / #1a2b3c4d": the title, the due date (Lagos) and the
    /// last 8 characters of the assignment's id, so two assignments with the same title have different sheets.
    /// </summary>
    private async Task<string> MarkSheetForAsync(MarkSetup s, string subject, Guid assignmentId, string className = "JSS 1A")
    {
        var a = await api.WithDbAsync(db => db.Assignments.IgnoreQueryFilters().Where(x => x.Id == assignmentId).Select(x => new { x.Title, x.DueAt }).FirstAsync(Ct));
        var due = DateOnly.FromDateTime(a.DueAt.ToOffset(TimeSpan.FromHours(1)).DateTime).ToString("yyyy-MM-dd");
        return $"{await SessionNameAsync(s.ClassId)} / {className} / {subject} / {await TermNameAsync(s.TermId)} / {a.Title} / due {due} / #{assignmentId.ToString("N")[^8..]}";
    }

    private Task<List<(Guid Enrolment, decimal Value)>> ScoresAsync(Guid tenantId, Guid componentId) =>
        api.WithDbAsync(async db => (await db.Scores.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.ComponentId == componentId)
            .Select(x => new { x.EnrolmentId, x.Value }).ToListAsync(Ct)).Select(x => (x.EnrolmentId, x.Value)).ToList());

    [Fact]
    public async Task Counted_mark_import_writes_score()
    {
        var s = await MarkSetupAsync("Mark Import School");
        var ca1 = await ComponentAsync(s, "CA1"); // out of 20
        var id = await AssignmentAsync(s, "CA1 homework", ca1, 10m);
        var scope = $"?assignmentId={id}";
        var sheetFor = await MarkSheetForAsync(s, "Mathematics", id);

        // Tolu is marked on the screen: 5 of 10 is 10 of 20.
        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{s.Tolu}", new { mark = 5m }, Ct));

        var preview = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,mark,feedback,sheet_for,row_version",
            $"{s.AdaNo},Ada Zed,10,Well done,\"{sheetFor}\",",
            $"{s.ToluNo},Tolu Obi,,,\"{sheetFor}\",")));
        Assert.Equal("assignment-marks", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal("New", rows[0].GetProperty("status").GetString());
        Assert.Contains("Ada Zed", rows[0].GetProperty("label").GetString());
        Assert.Equal("Unchanged", rows[1].GetProperty("status").GetString());
        AssertCounts(preview, 1, 0, 1, 0);
        Assert.Single(await ScoresAsync(s.School.TenantId, ca1));

        var done = await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        Assert.Equal((1, 0), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        var ada = await SubmissionAsync(s, id, s.Ada);
        Assert.Equal(10m, ada.GetProperty("mark").GetDecimal());
        Assert.Equal("Well done", ada.GetProperty("feedback").GetString());
        Assert.Equal("Returned", ada.GetProperty("status").GetString());
        // The same score the screen writes: scaled onto the component, one row per student and component.
        var scores = await ScoresAsync(s.School.TenantId, ca1);
        Assert.Equal(2, scores.Count);
        Assert.Equal(20m, scores.Single(x => x.Enrolment == s.Ada).Value);
        Assert.Equal(10m, scores.Single(x => x.Enrolment == s.Tolu).Value);
        Assert.Equal(5m, (await SubmissionAsync(s, id, s.Tolu)).GetProperty("mark").GetDecimal());

        // Updating a mark, a mark out of range and a mark that is not a number.
        var update = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,mark,feedback,sheet_for,row_version",
            $"{s.AdaNo},Ada Zed,11,,\"{sheetFor}\",",
            $"{s.ToluNo},Tolu Obi,lots,,\"{sheetFor}\",")));
        var bad = Rows(update);
        Assert.Equal("The mark must be between 0 and 10.", bad[0].GetProperty("message").GetString());
        Assert.Equal("Enter a mark such as 8 or 8.5.", bad[1].GetProperty("message").GetString());
        var refused = await ProblemAsync(await ConfirmAsync(s.Teacher, Token(update)), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        // Tolu has a saved mark, so the row carries the version from the real template.
        var toluVersion = (await ImportTemplateKit.VersionsAsync(s.Teacher, "/api/school/imports/assignment-marks/template.csv" + scope))[s.ToluNo];
        var changed = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,mark,feedback,sheet_for,row_version", $"{s.ToluNo},Tolu Obi,7.5,,\"{sheetFor}\",{toluVersion}")));
        Assert.Equal("Update", Assert.Single(Rows(changed)).GetProperty("status").GetString());
        await Json(await ConfirmAsync(s.Teacher, Token(changed)));
        Assert.Equal(15m, (await ScoresAsync(s.School.TenantId, ca1)).Single(x => x.Enrolment == s.Tolu).Value);

        // The template carries the stored marks and feedback; uploaded unchanged, every row is Unchanged and nothing is written.
        var xlsx = await s.Teacher.GetAsync("/api/school/imports/assignment-marks/template.xlsx" + scope, Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Marks");
            Assert.Equal(["admission_no", "student", "mark", "feedback", "sheet_for"], Enumerable.Range(1, 5).Select(c => sheet.Cell(1, c).GetString()).ToList());
            var adaRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.AdaNo);
            Assert.Equal(("10", "Well done", sheetFor), (sheet.Cell(adaRow, 3).GetString(), sheet.Cell(adaRow, 4).GetString(), sheet.Cell(adaRow, 5).GetString()));
        }
        var roundTrip = await Json(await PreviewFileAsync(s.Teacher, "assignment-marks", scope, bytes, "marks.xlsx"));
        AssertCounts(roundTrip, 0, 0, 2, 0);
        Assert.Empty(Warnings(roundTrip));

        // Access: the bursar first, then a teacher of another subject gets the screen's 404.
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var bursarProblem = await ProblemAsync(await bursar.GetAsync($"/api/school/imports/assignment-marks/template.xlsx?assignmentId={Guid.NewGuid()}", Ct), HttpStatusCode.Forbidden);
        Assert.Equal("The bursar does not set assignments.", bursarProblem.GetProperty("detail").GetString());
        var (other, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        var hidden = await ProblemAsync(await other.GetAsync("/api/school/imports/assignment-marks/template.xlsx" + scope, Ct), HttpStatusCode.NotFound);
        Assert.Equal("This assignment was not found.", hidden.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Nursery_received_import()
    {
        var s = await MarkSetupAsync("Nursery Mark Import School", nursery: true);
        var id = await AssignmentAsync(s, "Bring a leaf", null, null);
        var scope = $"?assignmentId={id}";
        var sheetFor = await MarkSheetForAsync(s, "Numeracy", id, "Nursery 1A");

        var xlsx = await s.Teacher.GetAsync("/api/school/imports/assignment-marks/template.xlsx" + scope, Ct);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Marks");
            Assert.Equal(["admission_no", "student", "received", "feedback", "sheet_for"], Enumerable.Range(1, 5).Select(c => sheet.Cell(1, c).GetString()).ToList());
            Assert.Equal("no", sheet.Cell(2, 3).GetString());
            Assert.Equal(sheetFor, sheet.Cell(2, 5).GetString());
        }
        AssertCounts(await Json(await PreviewFileAsync(s.Teacher, "assignment-marks", scope, bytes, "marks.xlsx")), 0, 0, 2, 0);

        var preview = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,received,feedback,sheet_for,row_version",
            $"{s.AdaNo},Ada Zed,Yes,Brought a green leaf.,\"{sheetFor}\",",
            $"{s.ToluNo},Tolu Obi,no,,\"{sheetFor}\",")));
        AssertCounts(preview, 1, 0, 1, 0);
        await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        var ada = await SubmissionAsync(s, id, s.Ada);
        Assert.Equal("Received", ada.GetProperty("status").GetString());
        Assert.Equal("Brought a green leaf.", ada.GetProperty("feedback").GetString());
        Assert.Equal("None", (await SubmissionAsync(s, id, s.Tolu)).GetProperty("status").GetString());

        // A mark column is not read for a nursery class: it is named in a warning.
        var marked = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,mark,received,sheet_for,row_version", $"{s.AdaNo},Ada Zed,5,yes,\"{sheetFor}\",")));
        AssertCounts(marked, 0, 0, 1, 0);
        Assert.Equal(["This column was ignored: mark."], Warnings(marked));

        // A value that is neither yes nor no.
        var maybe = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,received,feedback,sheet_for,row_version", $"{s.ToluNo},Tolu Obi,maybe,,\"{sheetFor}\",")));
        Assert.Equal("Enter yes or no for received.", Assert.Single(Rows(maybe)).GetProperty("message").GetString());

        // Back to not received, through confirm: the status returns to None, as the screen's "Not received" does.
        var adaVersion = (await ImportTemplateKit.VersionsAsync(s.Teacher, "/api/school/imports/assignment-marks/template.csv" + scope))[s.AdaNo];
        var back = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,received,feedback,sheet_for,row_version", $"{s.AdaNo},Ada Zed,no,,\"{sheetFor}\",{adaVersion}")));
        Assert.Equal("Update", Assert.Single(Rows(back)).GetProperty("status").GetString());
        var undone = await Json(await ConfirmAsync(s.Teacher, Token(back)));
        Assert.Equal((0, 1), (undone.GetProperty("created").GetInt32(), undone.GetProperty("updated").GetInt32()));
        Assert.Equal("None", (await SubmissionAsync(s, id, s.Ada)).GetProperty("status").GetString());

        // Work the student sent stays received: "no" on it is an error, and nothing changes.
        var sentAt = DateTimeOffset.UtcNow.AddHours(-1);
        await api.WithDbAsync(async db =>
        {
            db.AssignmentSubmissions.Add(new AssignmentSubmission
            {
                TenantId = s.School.TenantId, AssignmentId = id, EnrolmentId = s.Tolu, Body = "A leaf from the garden.", SubmittedAt = sentAt,
                Status = AssignmentSubmissionStatus.Received,
            });
            return await db.SaveChangesAsync(Ct);
        });
        var sent = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,received,feedback,sheet_for,row_version", $"{s.ToluNo},Tolu Obi,no,,\"{sheetFor}\",")));
        var refused = Assert.Single(Rows(sent));
        Assert.Equal(("Error", "The student sent this work, so it stays received."), (refused.GetProperty("status").GetString(), refused.GetProperty("message").GetString()));
        await ProblemAsync(await ConfirmAsync(s.Teacher, Token(sent)), HttpStatusCode.Conflict);
        Assert.Equal("Received", (await SubmissionAsync(s, id, s.Tolu)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Same_title_assignment_template_is_refused()
    {
        var s = await MarkSetupAsync("Same Title Mark Import School");
        var ca1 = await ComponentAsync(s, "CA1");
        var counted = await AssignmentAsync(s, "Classwork", ca1, 10m);
        var practice = await AssignmentAsync(s, "Classwork", null, null);

        // The counted Classwork's real template, filled in, uploaded on the practice Classwork of the same class subject and term.
        var xlsx = await s.Teacher.GetAsync($"/api/school/imports/assignment-marks/template.xlsx?assignmentId={counted}", Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        byte[] filled;
        string countedFor = await MarkSheetForAsync(s, "Mathematics", counted);
        Assert.NotEqual(countedFor, await MarkSheetForAsync(s, "Mathematics", practice));
        using (var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync(Ct))))
        {
            var sheet = workbook.Worksheet("Marks");
            Assert.Equal(countedFor, sheet.Cell(2, 5).GetString());
            Assert.Matches(@" / Classwork / due \d{4}-\d{2}-\d{2} / #[0-9a-f]{8}$", countedFor);
            sheet.Cell(2, 3).Value = "8";
            sheet.Cell(3, 3).Value = "9";
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            filled = stream.ToArray();
        }
        var preview = await Json(await PreviewFileAsync(s.Teacher, "assignment-marks", $"?assignmentId={practice}", filled, "marks.xlsx"));
        Assert.All(Rows(preview), r =>
        {
            Assert.Equal("Error", r.GetProperty("status").GetString());
            Assert.Equal($"This sheet was made for {countedFor}. Download the template for this assignment.", r.GetProperty("message").GetString());
        });
        await ProblemAsync(await ConfirmAsync(s.Teacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(JsonValueKind.Null, (await SubmissionAsync(s, practice, s.Ada)).GetProperty("mark").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await SubmissionAsync(s, counted, s.Ada)).GetProperty("mark").ValueKind);
        Assert.Empty(await ScoresAsync(s.School.TenantId, ca1));

        // On its own assignment the same file is accepted.
        AssertCounts(await Json(await PreviewFileAsync(s.Teacher, "assignment-marks", $"?assignmentId={counted}", filled, "marks.xlsx")), 2, 0, 0, 0);
    }

    [Fact]
    public async Task Mark_import_locked_term_409()
    {
        var s = await MarkSetupAsync("Locked Mark Import School");
        var ca1 = await ComponentAsync(s, "CA1");
        var id = await AssignmentAsync(s, "Locked CA1", ca1, 10m);
        var practice = await AssignmentAsync(s, "Practice", null, null);
        var scope = $"?assignmentId={id}";
        var sheetFor = await MarkSheetForAsync(s, "Mathematics", id);
        var csv = Lines("admission_no,student,mark,feedback,sheet_for,row_version", $"{s.AdaNo},Ada Zed,8,,\"{sheetFor}\",");

        var preview = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, csv));
        AssertCounts(preview, 1, 0, 0, 0);
        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();

        var confirm = await ProblemAsync(await ConfirmAsync(s.Teacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(TermLocked, confirm.GetProperty("detail").GetString());
        Assert.Equal("term_locked", confirm.GetProperty("code").GetString());
        var again = await ProblemAsync(await PreviewAsync(s.Teacher, "assignment-marks", scope, csv), HttpStatusCode.Conflict);
        Assert.Equal("term_locked", again.GetProperty("code").GetString());
        Assert.Empty(await ScoresAsync(s.School.TenantId, ca1));
        Assert.Equal(JsonValueKind.Null, (await SubmissionAsync(s, id, s.Ada)).GetProperty("mark").ValueKind);

        // Feedback alone writes no score, and a practice mark writes none either, so the lock does not stop them (as on the screen).
        var feedback = await Json(await PreviewAsync(s.Teacher, "assignment-marks", scope, Lines(
            "admission_no,student,mark,feedback,sheet_for,row_version", $"{s.AdaNo},Ada Zed,,Good effort,\"{sheetFor}\",")));
        await Json(await ConfirmAsync(s.Teacher, Token(feedback)));
        Assert.Equal("Good effort", (await SubmissionAsync(s, id, s.Ada)).GetProperty("feedback").GetString());
        var practiceFor = await MarkSheetForAsync(s, "Mathematics", practice);
        var practiceMark = await Json(await PreviewAsync(s.Teacher, "assignment-marks", $"?assignmentId={practice}", Lines(
            "admission_no,student,mark,feedback,sheet_for,row_version", $"{s.AdaNo},Ada Zed,4,,\"{practiceFor}\",")));
        await Json(await ConfirmAsync(s.Teacher, Token(practiceMark)));
        Assert.Equal(4m, (await SubmissionAsync(s, practice, s.Ada)).GetProperty("mark").GetDecimal());
        Assert.Empty(await ScoresAsync(s.School.TenantId, ca1));
    }

    // ---------- Exam sittings ----------

    private sealed record ExamSetup(SchoolKit.School School, Guid ClassA, Guid ClassB, Guid MathsA, Guid EnglishA, Guid MathsB, Guid TermId, Guid PeriodId,
        HttpClient Teacher, Guid TeacherStaffId, string TeacherEmail, string TeacherName);

    private async Task<ExamSetup> ExamSetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classA = await kit.ClassAsync(school);
        var classB = await kit.ClassAsync(school, "B");
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var staffId = await StaffIdAsync(school, userId);
        var maths = await SubjectAsync(school, "Mathematics", "MTH");
        var english = await SubjectAsync(school, "English", "ENG");
        var mathsA = await SubjectLinkAsync(school, classA, maths, staffId);
        var englishA = await SubjectLinkAsync(school, classA, english, null);
        var mathsB = await SubjectLinkAsync(school, classB, maths, staffId);
        var termId = await CurrentTermAsync(school);
        var periodId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/exam-periods",
            new { termId, name = "First term examination", kind = "Examination", start = ExamDay, end = NextDay }, Ct))).GetProperty("id").GetGuid();
        return new ExamSetup(school, classA, classB, mathsA, englishA, mathsB, termId, periodId, teacher, staffId, await EmailAsync(userId), "Maths teacher Staff");
    }

    private const string SittingHeader = "class,subject,date,start,end,venue,instructions,invigilator";

    private static async Task<List<JsonElement>> SittingsAsync(ExamSetup s) =>
        (await Json(await s.School.Owner.GetAsync($"/api/school/exam-periods/{s.PeriodId}", Ct))).GetProperty("sittings").EnumerateArray().ToList();

    [Fact]
    public async Task Sitting_import_creates_sittings()
    {
        var s = await ExamSetupAsync("Sitting Import School");
        var scope = $"?periodId={s.PeriodId}";

        var template = await s.School.Owner.GetAsync("/api/school/imports/exam-sittings/template.xlsx" + scope, Ct);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        using (var workbook = new XLWorkbook(new MemoryStream(await template.Content.ReadAsByteArrayAsync(Ct))))
            Assert.Equal(["class", "subject", "date", "start", "end", "venue", "instructions", "invigilator"],
                Enumerable.Range(1, 8).Select(c => workbook.Worksheet("Sittings").Cell(1, c).GetString()).ToList());

        var preview = await Json(await PreviewAsync(s.School.Owner, "exam-sittings", scope, Lines(SittingHeader,
            "jss 1a,Mathematics,2026-11-16,09:00,10:00,Hall A,Answer every question.,",
            $"JSS 1B,mth,17/11/2026,9:00,11:00,Lab 1,Show your working.,{s.TeacherEmail.ToUpperInvariant()}",
            "JSS 9Z,Mathematics,2026-11-16,12:00,13:00,Hall A,Answer every question.,",
            "JSS 1A,Physics,2026-11-16,12:00,13:00,Hall A,Answer every question.,",
            "JSS 1A,English,2026-11-20,12:00,13:00,Hall A,Answer every question.,",
            "JSS 1A,English,2026-11-16,13:00,12:00,Hall A,Answer every question.,",
            "JSS 1A,English,2026-11-16,12:00,13:00,Hall A,Answer every question.,nobody@example.com")));
        Assert.Equal("exam-sittings", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal(["New", "New", "Error", "Error", "Error", "Error", "Error"], rows.Select(r => r.GetProperty("status").GetString()).ToList());
        Assert.Equal("Mathematics, JSS 1A", rows[0].GetProperty("label").GetString());
        Assert.Equal("There is no class called JSS 9Z this session.", rows[2].GetProperty("message").GetString());
        Assert.Equal("Physics is not a subject of JSS 1A.", rows[3].GetProperty("message").GetString());
        Assert.Equal("Choose a date inside the exam period.", rows[4].GetProperty("message").GetString());
        Assert.Equal("The exam must end after it starts.", rows[5].GetProperty("message").GetString());
        Assert.Equal("Choose an active staff member from this school.", rows[6].GetProperty("message").GetString());
        var refused = await ProblemAsync(await ConfirmAsync(s.School.Owner, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Empty(await SittingsAsync(s));

        var good = await Json(await PreviewAsync(s.School.Owner, "exam-sittings", scope, Lines(SittingHeader,
            "jss 1a,Mathematics,2026-11-16,09:00,10:00,Hall A,Answer every question.,",
            $"JSS 1B,mth,17/11/2026,9:00,11:00,Lab 1,Show your working.,{s.TeacherEmail.ToUpperInvariant()}")));
        AssertCounts(good, 2, 0, 0, 0);
        var done = await Json(await ConfirmAsync(s.School.Owner, Token(good)));
        Assert.Equal((2, 0), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        var sittings = await SittingsAsync(s);
        Assert.Equal(2, sittings.Count);
        var first = sittings.Single(x => x.GetProperty("classSubjectId").GetGuid() == s.MathsA);
        Assert.Equal(("2026-11-16", "09:00", "10:00", 60, "Hall A", "Answer every question."),
            (first.GetProperty("date").GetString(), first.GetProperty("start").GetString(), first.GetProperty("end").GetString(),
             first.GetProperty("durationMinutes").GetInt32(), first.GetProperty("venue").GetString(), first.GetProperty("instructions").GetString()));
        Assert.Equal(JsonValueKind.Null, first.GetProperty("invigilatorStaffId").ValueKind);
        var second = sittings.Single(x => x.GetProperty("classSubjectId").GetGuid() == s.MathsB);
        Assert.Equal(("2026-11-17", 120, s.TeacherStaffId), (second.GetProperty("date").GetString(), second.GetProperty("durationMinutes").GetInt32(), second.GetProperty("invigilatorStaffId").GetGuid()));

        // The same file uploaded again adds nothing: every row clashes with the sitting it already made.
        var again = await Json(await PreviewAsync(s.School.Owner, "exam-sittings", scope, Lines(SittingHeader,
            "jss 1a,Mathematics,2026-11-16,09:00,10:00,Hall A,Answer every question.,",
            $"JSS 1B,mth,17/11/2026,9:00,11:00,Lab 1,Show your working.,{s.TeacherEmail.ToUpperInvariant()}")));
        Assert.All(Rows(again), r => Assert.Equal(("Error", ClassOverlap), (r.GetProperty("status").GetString(), r.GetProperty("message").GetString())));
        await ProblemAsync(await ConfirmAsync(s.School.Owner, Token(again)), HttpStatusCode.Conflict);
        Assert.Equal(2, (await SittingsAsync(s)).Count);

        // Excel times and dates (real time and date cells, not text) read as the person typed them.
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Sittings");
        string[] headings = ["class", "subject", "date", "start", "end", "venue", "instructions"];
        for (var c = 0; c < headings.Length; c++) ws.Cell(1, c + 1).Value = headings[c];
        ws.Cell(2, 1).Value = "JSS 1A";
        ws.Cell(2, 2).Value = "English";
        ws.Cell(2, 3).Value = new DateTime(2026, 11, 17);
        ws.Cell(2, 4).Value = new TimeSpan(13, 0, 0); // an Excel time cell (0.5417 formatted as a time)
        ws.Cell(2, 4).Style.NumberFormat.Format = "hh:mm";
        ws.Cell(2, 5).Value = 0.625; // 15:00 as a plain Excel serial
        ws.Cell(2, 6).Value = "Hall C";
        ws.Cell(2, 7).Value = "Answer every question.";
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        var timed = await Json(await PreviewFileAsync(s.School.Owner, "exam-sittings", scope, stream.ToArray(), "sittings.xlsx"));
        AssertCounts(timed, 1, 0, 0, 0);
        await Json(await ConfirmAsync(s.School.Owner, Token(timed)));
        var english = (await SittingsAsync(s)).Single(x => x.GetProperty("classSubjectId").GetGuid() == s.EnglishA);
        Assert.Equal(("2026-11-17", "13:00", "15:00"), (english.GetProperty("date").GetString(), english.GetProperty("start").GetString(), english.GetProperty("end").GetString()));
    }

    [Fact]
    public async Task Clash_within_file_error()
    {
        var s = await ExamSetupAsync("Sitting Clash Import School");
        var scope = $"?periodId={s.PeriodId}";
        // Already booked: JSS 1B Mathematics in Hall Z on the second day.
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/exam-periods/{s.PeriodId}/sittings", new
        {
            classSubjectId = s.MathsB, date = NextDay, start = "09:00", end = "10:00", durationMinutes = 60, venue = "Hall Z",
            instructions = "Answer every question.", invigilatorStaffId = (Guid?)null,
        }, Ct));

        var preview = await Json(await PreviewAsync(s.School.Owner, "exam-sittings", scope, Lines(SittingHeader,
            "JSS 1A,Mathematics,2026-11-16,09:00,10:00,Hall A,Answer every question.,",
            "JSS 1A,English,2026-11-16,09:30,10:30,Hall B,Answer every question.,",
            "JSS 1B,Mathematics,2026-11-16,09:00,10:00,hall a ,Answer every question.,",
            "JSS 1A,English,2026-11-17,09:30,10:30,Hall Z,Answer every question.,",
            "JSS 1A,English,2026-11-16,10:00,11:00,Hall A,Answer every question.,")));
        var rows = Rows(preview);
        Assert.Equal(["New", "Error", "Error", "Error", "New"], rows.Select(r => r.GetProperty("status").GetString()).ToList());
        Assert.Equal(ClassOverlap, rows[1].GetProperty("message").GetString());
        Assert.Equal(VenueOverlap, rows[2].GetProperty("message").GetString());
        Assert.Equal(VenueOverlap, rows[3].GetProperty("message").GetString());

        var refused = await ProblemAsync(await ConfirmAsync(s.School.Owner, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", refused.GetProperty("code").GetString());
        Assert.Single(await SittingsAsync(s));
    }

    [Fact]
    public async Task Invigilator_clash_warning_not_error()
    {
        var s = await ExamSetupAsync("Invigilator Import School");
        var scope = $"?periodId={s.PeriodId}";
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/exam-periods/{s.PeriodId}/sittings", new
        {
            classSubjectId = s.MathsA, date = ExamDay, start = "09:00", end = "10:00", durationMinutes = 60, venue = "Hall A",
            instructions = "Answer every question.", invigilatorStaffId = s.TeacherStaffId,
        }, Ct));

        var preview = await Json(await PreviewAsync(s.School.Owner, "exam-sittings", scope, Lines(SittingHeader,
            $"JSS 1B,Mathematics,2026-11-16,09:30,10:30,Lab 1,Answer every question.,{s.TeacherEmail}")));
        var row = Assert.Single(Rows(preview));
        Assert.Equal("New", row.GetProperty("status").GetString());
        Assert.Equal(InvigilatorWarning, row.GetProperty("message").GetString());
        Assert.Equal([$"Row 2: {InvigilatorWarning}"], Warnings(preview));

        await Json(await ConfirmAsync(s.School.Owner, Token(preview)));
        Assert.Equal(2, (await SittingsAsync(s)).Count);
    }

    // ---------- Timetable ----------

    private static object Slot(string day, int period, string start, string end, Guid classSubjectId) => new { day, period, start, end, classSubjectId };

    private static async Task<List<JsonElement>> SlotsAsync(SchoolKit.School school, Guid classId) =>
        (await Json(await school.Owner.GetAsync($"/api/school/timetable/{classId}", Ct))).GetProperty("slots").EnumerateArray().ToList();

    [Fact]
    public async Task Timetable_import_replaces_week()
    {
        var s = await ExamSetupAsync("Timetable Import School");
        var scope = $"?classId={s.ClassA}";
        (await s.School.Owner.PutAsJsonAsync($"/api/school/timetable/{s.ClassA}", new[]
        {
            Slot("Mon", 1, "08:00", "08:40", s.MathsA), Slot("Mon", 2, "08:40", "09:20", s.EnglishA), Slot("Tue", 1, "08:00", "08:40", s.MathsA),
        }, Ct)).EnsureSuccessStatusCode();
        // JSS 1B has the same Maths teacher on Wednesday, period 3.
        (await s.School.Owner.PutAsJsonAsync($"/api/school/timetable/{s.ClassB}", new[] { Slot("Wed", 3, "10:00", "10:40", s.MathsB) }, Ct)).EnsureSuccessStatusCode();
        var before = (await SlotsAsync(s.School, s.ClassA)).Select(x => x.GetProperty("id").GetGuid()).ToHashSet();
        var sheetFor = $"{await SessionNameAsync(s.ClassA)} / JSS 1A";

        // The template is the current week; uploaded unchanged it is a no-op.
        var xlsx = await s.School.Owner.GetAsync("/api/school/imports/timetable/template.xlsx" + scope, Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Timetable");
            Assert.Equal(["day", "period", "start", "end", "subject", "sheet_for"], Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()).ToList());
            Assert.Equal(["Monday", "1", "08:00", "08:40", "Mathematics", sheetFor], Enumerable.Range(1, 6).Select(c => sheet.Cell(2, c).GetString()).ToList());
            Assert.Equal("Tuesday", sheet.Cell(4, 1).GetString());
        }
        var same = await Json(await PreviewFileAsync(s.School.Owner, "timetable", scope, bytes, "timetable.xlsx"));
        AssertCounts(same, 0, 0, 3, 0);
        Assert.Empty(Warnings(same));
        var noop = await Json(await ConfirmAsync(s.School.Owner, Token(same)));
        Assert.Equal((0, 0), (noop.GetProperty("created").GetInt32(), noop.GetProperty("updated").GetInt32()));
        Assert.Equal(before, (await SlotsAsync(s.School, s.ClassA)).Select(x => x.GetProperty("id").GetGuid()).ToHashSet());

        // A week with one lesson changed, one kept, one new and Tuesday's lesson left out: the week is replaced. The changed lesson carries
        // its version from the real template; the new one's is blank.
        var versions = await ImportTemplateKit.VersionsAsync(s.School.Owner, "/api/school/imports/timetable/template.csv" + scope, r => $"{r["day"]} {r["period"]}");
        var preview = await Json(await PreviewAsync(s.School.Owner, "timetable", scope, Lines("day,period,start,end,subject,sheet_for,row_version",
            $"Mon,1,08:00,08:40,ENG,\"{sheetFor}\",{versions["Monday 1"]}",
            $"monday,2,,,,\"{sheetFor}\",",
            $"Wednesday,3,10:00,10:40,Mathematics,\"{sheetFor}\",")));
        Assert.Equal(["Update", "Unchanged", "New"], Rows(preview).Select(r => r.GetProperty("status").GetString()).ToList());
        Assert.Equal("Monday, period 1", Rows(preview)[0].GetProperty("label").GetString());
        var warnings = Warnings(preview);
        Assert.Contains("This lesson is not in the file and will be removed: Tuesday, period 1 (Mathematics).", warnings);
        Assert.Contains($"Row 4: {s.TeacherName} also teaches JSS 1B on Wednesday, period 3.", warnings);
        var done = await Json(await ConfirmAsync(s.School.Owner, Token(preview)));
        Assert.Equal((1, 1, 1), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32(), done.GetProperty("removed").GetInt32()));
        var slots = await SlotsAsync(s.School, s.ClassA);
        Assert.Equal(["Mon 1 English 08:00", "Mon 2 English 08:40", "Wed 3 Mathematics 10:00"],
            slots.Select(x => $"{x.GetProperty("day").GetString()} {x.GetProperty("period").GetInt32()} {x.GetProperty("subject").GetString()} {x.GetProperty("start").GetString()}").ToList());

        // Errors: the same day and period twice (both rows), a subject the class doesn't take, a bad day, a new lesson with no times.
        var bad = await Json(await PreviewAsync(s.School.Owner, "timetable", scope, Lines("day,period,start,end,subject,sheet_for,row_version",
            $"Mon,1,08:00,08:40,English,\"{sheetFor}\",",
            $"Mon,1,08:00,08:40,Mathematics,\"{sheetFor}\",",
            $"Thu,4,11:00,11:40,Physics,\"{sheetFor}\",",
            $"Sunday,1,08:00,08:40,English,\"{sheetFor}\",",
            $"Fri,5,,,English,\"{sheetFor}\",",
            $"Fri,6,08:00,08:40,English,\"2020/2021 / JSS 1A\",")));
        var messages = Rows(bad).Select(r => r.GetProperty("message").GetString()).ToList();
        Assert.Equal("The class already has a lesson at this day and period. Choose another period.", messages[0]);
        Assert.Equal(messages[0], messages[1]);
        Assert.Equal("Choose a subject taught in this class.", messages[2]);
        Assert.Equal("Choose a day from Monday to Saturday.", messages[3]);
        Assert.Equal("Enter the start time as HH:mm, such as 08:00.", messages[4]);
        Assert.Equal("This sheet was made for 2020/2021 / JSS 1A. Download the template for this class.", messages[5]);
        // Monday, period 1 has rows in the file (both errors), so it is not listed as removed; the other two saved lessons are.
        Assert.Equal(["These lessons are not in the file and will be removed: Monday, period 2 (English) and Wednesday, period 3 (Mathematics)."], Warnings(bad));
        await ProblemAsync(await ConfirmAsync(s.School.Owner, Token(bad)), HttpStatusCode.Conflict);
        Assert.Equal(3, (await SlotsAsync(s.School, s.ClassA)).Count);

        // A file that only drops lessons: the result, the notice and the audit entry count the removals.
        var drop = await Json(await PreviewAsync(s.School.Owner, "timetable", scope, Lines("day,period,start,end,subject,sheet_for,row_version",
            $"Mon,1,08:00,08:40,English,\"{sheetFor}\",")));
        AssertCounts(drop, 0, 0, 1, 0);
        Assert.Equal(["These lessons are not in the file and will be removed: Monday, period 2 (English) and Wednesday, period 3 (Mathematics)."], Warnings(drop));
        var dropped = await Json(await ConfirmAsync(s.School.Owner, Token(drop)));
        Assert.Equal((0, 0, 2), (dropped.GetProperty("created").GetInt32(), dropped.GetProperty("updated").GetInt32(), dropped.GetProperty("removed").GetInt32()));
        Assert.Equal("Mon", Assert.Single(await SlotsAsync(s.School, s.ClassA)).GetProperty("day").GetString());
        var ownerId = await kit.OwnerIdAsync(s.School);
        var notes = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == ownerId && n.Kind == "school.import_finished")
            .OrderByDescending(n => n.CreatedAt).Select(n => n.Body).ToListAsync(Ct));
        Assert.Equal("0 added, 0 updated and 2 removed from timetable.csv.", notes[0]);
        Assert.Contains("1 added, 1 updated and 1 removed from timetable.csv.", notes);
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == s.School.TenantId && a.Action == "imports.timetable")
            .OrderByDescending(a => a.CreatedAt).Select(a => a.AfterJson).ToListAsync(Ct));
        Assert.Equal(2, JsonDocument.Parse(audits[0]!).RootElement.GetProperty("removed").GetInt32());
    }

    [Fact]
    public async Task Teacher_refused_sittings_and_timetable()
    {
        var s = await ExamSetupAsync("Classroom Refusal School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        foreach (var client in new[] { s.Teacher, bursar })
        {
            var sittings = await ProblemAsync(await client.GetAsync($"/api/school/imports/exam-sittings/template.xlsx?periodId={s.PeriodId}", Ct), HttpStatusCode.Forbidden);
            Assert.Equal("Only the principal can set the exam timetable.", sittings.GetProperty("detail").GetString());
            await ProblemAsync(await PreviewAsync(client, "exam-sittings", $"?periodId={Guid.NewGuid()}", Lines(SittingHeader, "JSS 1A,Mathematics,2026-11-16,09:00,10:00,Hall A,Go.,")), HttpStatusCode.Forbidden);

            var timetable = await ProblemAsync(await client.GetAsync($"/api/school/imports/timetable/template.xlsx?classId={s.ClassA}", Ct), HttpStatusCode.Forbidden);
            Assert.Equal("Only the principal can change the timetable.", timetable.GetProperty("detail").GetString());
            await ProblemAsync(await PreviewAsync(client, "timetable", $"?classId={Guid.NewGuid()}", Lines("day,period,start,end,subject,sheet_for", "Mon,1,08:00,08:40,Mathematics,x")), HttpStatusCode.Forbidden);
        }
        // Leadership reaches them; a period or class that doesn't exist is the screen's 404.
        Assert.Equal("This timetable was not found.",
            (await ProblemAsync(await s.School.Owner.GetAsync($"/api/school/imports/exam-sittings/template.xlsx?periodId={Guid.NewGuid()}", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal("Class was not found.",
            (await ProblemAsync(await s.School.Owner.GetAsync($"/api/school/imports/timetable/template.xlsx?classId={Guid.NewGuid()}", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
    }
}
