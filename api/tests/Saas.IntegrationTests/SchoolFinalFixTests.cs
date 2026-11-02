using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;
using UglyToad.PdfPig;

namespace Saas.IntegrationTests;

/// <summary>
/// Cross-task fixes from the final review: frozen nursery ratings, the mode a card was computed in, old published attendance,
/// archiving bank questions, removing a class subject that has work, and marking a timed-out CBT attempt.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolFinalFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private readonly SchoolKit kit = new(api);

    private const string TermLocked = "This term is locked for the class, so scores can't change. Ask the principal to unlock it.";
    private const string QuestionMissing = "This question was not found.";
    private const string HasWork = "This subject has homework, tests, lesson plans or exam sittings. Remove those first.";

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

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>The current term, widened around today so a register taken yesterday counts.</summary>
    private async Task<Guid> TermAsync(SchoolKit.School school) =>
        await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });

    private async Task<(Guid ClassSubjectId, Guid SubjectId)> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code, Guid? teacherStaffId = null)
    {
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct));
        var subjectId = subject.GetProperty("id").GetGuid();
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId, teacherStaffId }, Ct));
        return (link.GetProperty("id").GetGuid(), subjectId);
    }

    private async Task ScoreAllAsync(SchoolKit.School school, Guid classSubjectId, Guid termId, Guid enrolmentId, params decimal[] values)
    {
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={classSubjectId}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        var entries = components.Zip(values).Select(c => new { enrolmentId, componentId = c.First, value = (decimal?)c.Second }).ToArray();
        (await school.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId, termId, entries }, Ct)).EnsureSuccessStatusCode();
    }

    private static string PdfText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static async Task<string> CardTextAsync(HttpClient client, Guid enrolmentId, Guid termId)
    {
        var response = await client.GetAsync($"/api/school/report-cards/{enrolmentId}/{termId}.pdf", Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return PdfText(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    // ---------- I1: developmental ratings freeze with the term ----------

    [Fact]
    public async Task Developmental_put_after_lock_is_409()
    {
        var school = await kit.SchoolAsync("Locked Nursery School");
        var levelId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct))).GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var (classTeacher, classUser) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = await StaffIdAsync(school, classUser) }, Ct)).EnsureSuccessStatusCode();
        var termId = await TermAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");

        var areas = await Json(await classTeacher.GetAsync("/api/school/developmental/areas", Ct));
        var scale = await Json(await classTeacher.GetAsync("/api/school/developmental/scale", Ct));
        object[] Rate(string label) => areas.GetProperty("areas").EnumerateArray().Select(a => new
        {
            areaId = a.GetProperty("id").GetGuid(),
            scaleId = scale.GetProperty("scale").EnumerateArray().Single(s => s.GetProperty("label").GetString() == label).GetProperty("id").GetGuid(),
            comment = (string?)null,
        }).ToArray();
        var url = $"/api/school/students/{studentId}/developmental?termId={termId}";
        await Json(await classTeacher.PutAsJsonAsync(url, new { ratings = Rate("Good") }, Ct));

        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/lock", null, Ct)).EnsureSuccessStatusCode();
        foreach (var client in new[] { classTeacher, school.Owner })
        {
            var problem = await ProblemAsync(await client.PutAsJsonAsync(url, new { ratings = Rate("Needs Support") }, Ct), HttpStatusCode.Conflict);
            Assert.Equal(TermLocked, problem.GetProperty("detail").GetString());
            Assert.Equal("term_locked", problem.GetProperty("code").GetString());
        }

        var stored = await Json(await classTeacher.GetAsync(url, Ct));
        Assert.All(stored.GetProperty("ratings").EnumerateArray(), r => Assert.Equal("Good", r.GetProperty("scale").GetString()));

        // Unlocking opens them again, like scores.
        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/unlock", null, Ct)).EnsureSuccessStatusCode();
        await Json(await classTeacher.PutAsJsonAsync(url, new { ratings = Rate("Very Good") }, Ct));
    }

    // ---------- I2: each card keeps the mode it was computed in ----------

    private sealed record NurseryNumeric(SchoolKit.School School, Guid LevelId, Guid ClassId, Guid TermId, Guid StudentId, Guid EnrolmentId);

    /// <summary>A Nursery-stage level kept numeric (as existing schools migrate), with Ada scored in Mathematics and the results computed.</summary>
    private async Task<NurseryNumeric> NurseryNumericAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 2", stage = "Nursery", order = 3, hasStudentLogins = false, assessmentMode = "Numeric" }, Ct));
        Assert.Equal("Numeric", level.GetProperty("assessmentMode").GetString());
        var levelId = level.GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var termId = await TermAsync(school);
        var (maths, _) = await ClassSubjectAsync(school, classId, "Mathematics", "MTH");
        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(school, studentId);
        await ScoreAllAsync(school, maths, termId, enrolmentId, 15, 15, 50);
        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/compute", null, Ct)).EnsureSuccessStatusCode();
        return new NurseryNumeric(school, levelId, classId, termId, studentId, enrolmentId);
    }

    private static object Level(string? mode) => mode is null
        ? new { name = "Nursery 2", stage = "Nursery", order = 3, hasStudentLogins = false }
        : new { name = "Nursery 2", stage = "Nursery", order = 3, hasStudentLogins = false, assessmentMode = mode };

    [Fact]
    public async Task Published_numeric_card_is_unchanged_after_switching_to_developmental()
    {
        var n = await NurseryNumericAsync("Mode History School");
        (await n.School.Owner.PostAsync($"/api/school/results/{n.ClassId}/{n.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await n.School.Owner.PostAsync($"/api/school/results/{n.ClassId}/{n.TermId}/publish", null, Ct)).EnsureSuccessStatusCode();
        var before = await CardTextAsync(n.School.Owner, n.EnrolmentId, n.TermId);
        Assert.Contains("Mathematics", before);

        var switched = await Json(await n.School.Owner.PutAsJsonAsync($"/api/school/levels/{n.LevelId}", Level("Developmental"), Ct));
        Assert.Equal("Developmental", switched.GetProperty("assessmentMode").GetString());

        var results = await Json(await n.School.Owner.GetAsync($"/api/school/results/{n.ClassId}/{n.TermId}", Ct));
        Assert.Equal("Numeric", results.GetProperty("mode").GetString());
        var ada = Assert.Single(results.GetProperty("students").EnumerateArray());
        Assert.Equal("Numeric", ada.GetProperty("mode").GetString());
        Assert.Equal("Mathematics", Assert.Single(ada.GetProperty("subjects").EnumerateArray()).GetProperty("subject").GetString());
        Assert.Equal(0, ada.GetProperty("developmental").GetArrayLength());

        var child = await Json(await n.School.Owner.GetAsync($"/api/school/results/students/{n.StudentId}?termId={n.TermId}", Ct));
        Assert.Equal("Numeric", child.GetProperty("mode").GetString());
        Assert.Single(child.GetProperty("subjects").EnumerateArray());
        Assert.Equal(0, child.GetProperty("developmental").GetArrayLength());

        var after = await CardTextAsync(n.School.Owner, n.EnrolmentId, n.TermId);
        Assert.Equal(before, after);
        Assert.DoesNotContain("Not yet rated", after);

        var csv = Encoding.UTF8.GetString(await (await n.School.Owner.GetAsync($"/api/school/results/{n.ClassId}/{n.TermId}/broadsheet.csv", Ct)).Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("Mathematics", csv);
        Assert.Contains("Position", csv);
    }

    [Fact]
    public async Task Level_put_without_mode_keeps_it()
    {
        var n = await NurseryNumericAsync("Mode Keep School");

        var kept = await Json(await n.School.Owner.PutAsJsonAsync($"/api/school/levels/{n.LevelId}", Level(null), Ct));
        Assert.Equal("Numeric", kept.GetProperty("assessmentMode").GetString());
        var stored = await api.WithDbAsync(db => db.Levels.IgnoreQueryFilters().Where(l => l.Id == n.LevelId).Select(l => l.AssessmentMode).SingleAsync(Ct));
        Assert.Equal(Saas.Domain.School.AssessmentMode.Numeric, stored);
        var numeric = await Json(await n.School.Owner.GetAsync($"/api/school/results/{n.ClassId}/{n.TermId}", Ct));
        Assert.Single(Assert.Single(numeric.GetProperty("students").EnumerateArray()).GetProperty("subjects").EnumerateArray());

        // An explicit change marks the level's unpublished results stale, so the next view is in the new mode.
        await Json(await n.School.Owner.PutAsJsonAsync($"/api/school/levels/{n.LevelId}", Level("Developmental"), Ct));
        var developmental = await Json(await n.School.Owner.GetAsync($"/api/school/results/{n.ClassId}/{n.TermId}", Ct));
        Assert.Equal("Developmental", developmental.GetProperty("mode").GetString());
        var ada = Assert.Single(developmental.GetProperty("students").EnumerateArray());
        Assert.Equal("Developmental", ada.GetProperty("mode").GetString());
        Assert.Equal(0, ada.GetProperty("subjects").GetArrayLength());
        Assert.Equal(8, ada.GetProperty("developmental").GetArrayLength());
    }

    // ---------- I3: old published rows print no false zeros ----------

    [Fact]
    public async Task Published_row_without_counts_prints_present_of_open()
    {
        var school = await kit.SchoolAsync("Old Attendance School");
        var classId = await kit.ClassAsync(school);
        var termId = await TermAsync(school);
        var (maths, _) = await ClassSubjectAsync(school, classId, "Mathematics", "MTH");
        var enrolmentId = await EnrolmentAsync(school, await kit.StudentAsync(school, classId, "Ada", "Zed"));
        await ScoreAllAsync(school, maths, termId, enrolmentId, 15, 15, 50);
        foreach (var (day, status) in new[] { (-3, "Present"), (-2, "Late"), (-1, "Absent") })
            (await school.Owner.PutAsJsonAsync($"/api/school/attendance/{classId}/{Today.AddDays(day):yyyy-MM-dd}",
                new { clientId = Guid.NewGuid().ToString(), marks = new[] { new { enrolmentId, status } } }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/publish", null, Ct)).EnsureSuccessStatusCode();

        Assert.Contains("Present 1, late 1, absent 1, excused 0 of 3 days", await CardTextAsync(school.Owner, enrolmentId, termId));

        // What School26 does to a row published before the counts existed (it stored late inside present).
        await api.WithDbAsync(db => db.TermResults.IgnoreQueryFilters().Where(r => r.EnrolmentId == enrolmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DaysPresent, 2).SetProperty(r => r.DaysLate, (int?)null)
                .SetProperty(r => r.DaysAbsent, (int?)null).SetProperty(r => r.DaysExcused, (int?)null), Ct));

        var card = await CardTextAsync(school.Owner, enrolmentId, termId);
        Assert.Contains("Present 2 of 3 days", card);
        Assert.DoesNotContain("absent 0", card);
        Assert.DoesNotContain("excused", card);

        var results = await Json(await school.Owner.GetAsync($"/api/school/results/{classId}/{termId}", Ct));
        var ada = Assert.Single(results.GetProperty("students").EnumerateArray());
        Assert.Equal(2, ada.GetProperty("daysPresent").GetInt32());
        Assert.Equal(JsonValueKind.Null, ada.GetProperty("daysLate").ValueKind);
        Assert.Equal(JsonValueKind.Null, ada.GetProperty("daysAbsent").ValueKind);
        Assert.Equal(JsonValueKind.Null, ada.GetProperty("daysExcused").ValueKind);

        var csv = Encoding.UTF8.GetString(await (await school.Owner.GetAsync($"/api/school/results/{classId}/{termId}/broadsheet.csv", Ct)).Content.ReadAsByteArrayAsync(Ct));
        var lines = csv.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.EndsWith("\"Present\",\"Late\",\"Absent\",\"Excused\",\"Open days\"", lines[0]);
        Assert.EndsWith(",2,,,,3", lines[1]);

        // Unpublishing and computing again fills all four.
        (await school.Owner.PostAsync($"/api/school/results/{classId}/{termId}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        Assert.Contains("Present 1, late 1, absent 1, excused 0 of 3 days", await CardTextAsync(school.Owner, enrolmentId, termId));
    }

    [Fact]
    public void Attendance_text_without_counts_is_the_old_line()
    {
        Assert.Equal("Present 4 of 5 days", ReportCardText.Attendance(4, null, null, null, 5));
        Assert.Equal("Present 4, late 1, absent 0, excused 0 of 5 days", ReportCardText.Attendance(4, (int?)1, 0, 0, 5));
    }

    // ---------- CBT setup ----------

    private sealed record Cbt(SchoolKit.School School, Guid ClassId, Guid TermId, Guid MathsId, Guid MathsSubjectId, Guid EnglishSubjectId, HttpClient Teacher, HttpClient OtherTeacher);

    private async Task<Cbt> CbtAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var (other, otherUser) = await kit.StaffAsync(school, MemberRole.Staff, "English teacher");
        var (maths, mathsSubject) = await ClassSubjectAsync(school, classId, "Mathematics", "MTH", await StaffIdAsync(school, teacherUser));
        var (_, englishSubject) = await ClassSubjectAsync(school, classId, "English", "ENG", await StaffIdAsync(school, otherUser));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return new Cbt(school, classId, termId, maths, mathsSubject, englishSubject, teacher, other);
    }

    private async Task<Guid> QuestionAsync(Cbt s, string stem, decimal marks = 2) =>
        (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            new { subjectId = s.MathsSubjectId, stem, options = new[] { "w", "x", "y", "z" }, correctIndex = 1, marks }, Ct))).GetProperty("id").GetGuid();

    private async Task<Guid> PaperAsync(Cbt s, int drawCount, Guid[] picks, Guid? componentId = null)
    {
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId = s.MathsId, termId = s.TermId, title = "Mathematics test", durationMinutes = 40,
            openAt = DateTimeOffset.UtcNow.AddHours(-1), closeAt = DateTimeOffset.UtcNow.AddHours(3),
            assessmentComponentId = componentId, drawCount, pickQuestionIds = picks,
        }, Ct));
        var id = created.GetProperty("id").GetGuid();
        await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{id}/publish", null, Ct));
        return id;
    }

    private async Task<(HttpClient Client, Guid EnrolmentId)> PupilAsync(Cbt s, string name)
    {
        var id = await kit.StudentAsync(s.School, s.ClassId, name);
        return (await kit.StudentLoginAsync(s.School, id), await EnrolmentAsync(s.School, id));
    }

    private static List<string> Stems(JsonElement attempt) =>
        attempt.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("stem").GetString()!).OrderBy(x => x).ToList();

    // ---------- I4: archiving bank questions ----------

    [Fact]
    public async Task Archived_question_is_not_drawn()
    {
        var s = await CbtAsync("Archive Draw School");
        var wrong = await QuestionAsync(s, "Wrong key question");
        await QuestionAsync(s, "Good question one");
        await QuestionAsync(s, "Good question two");

        var archived = await Json(await s.Teacher.PostAsync($"/api/school/questions/{wrong}/archive", null, Ct));
        Assert.True(archived.GetProperty("archived").GetBoolean());
        var listed = await Json(await s.Teacher.GetAsync($"/api/school/questions?subjectId={s.MathsSubjectId}", Ct));
        Assert.True(listed.EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == wrong).GetProperty("archived").GetBoolean());

        // Two unarchived questions remain, so a draw of three is refused and a draw of two never includes the archived one.
        var tooMany = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId = s.MathsId, termId = s.TermId, title = "Too many", durationMinutes = 40,
            openAt = DateTimeOffset.UtcNow.AddHours(-1), closeAt = DateTimeOffset.UtcNow.AddHours(3), drawCount = 3, pickQuestionIds = Array.Empty<Guid>(),
        }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal("There are not enough questions in the bank for this test.", tooMany.GetProperty("detail").GetString());
        var paperId = await PaperAsync(s, 2, []);
        var (pupil, _) = await PupilAsync(s, "Ada");
        var started = await Json(await pupil.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        Assert.Equal(["Good question one", "Good question two"], Stems(started));

        var restored = await Json(await s.Teacher.PostAsync($"/api/school/questions/{wrong}/unarchive", null, Ct));
        Assert.False(restored.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Archived_pick_is_left_out_of_new_attempts_only()
    {
        var s = await CbtAsync("Archive Pick School");
        var wrong = await QuestionAsync(s, "Wrong key question");
        var good = await QuestionAsync(s, "Good question");
        var paperId = await PaperAsync(s, 0, [wrong, good]);

        var (first, _) = await PupilAsync(s, "Ada");
        var firstStart = await Json(await first.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var firstAttempt = firstStart.GetProperty("attemptId").GetGuid();
        Assert.Equal(["Good question", "Wrong key question"], Stems(firstStart));

        await Json(await s.School.Owner.PostAsync($"/api/school/questions/{wrong}/archive", null, Ct));

        var (second, _) = await PupilAsync(s, "Bola");
        var secondStart = await Json(await second.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        Assert.Equal(["Good question"], Stems(secondStart));

        // The attempt already running keeps its snapshot.
        var firstView = await Json(await first.GetAsync($"/api/student/cbt/attempts/{firstAttempt}", Ct));
        Assert.Equal(["Good question", "Wrong key question"], Stems(firstView));
    }

    [Fact]
    public async Task Archive_follows_the_question_rules()
    {
        var s = await CbtAsync("Archive Access School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var questionId = await QuestionAsync(s, "Who may archive me?");

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/questions/{questionId}/archive", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/questions/{Guid.NewGuid()}/archive", null, Ct)).StatusCode);
        foreach (var path in new[] { "archive", "unarchive" })
        {
            var other = await ProblemAsync(await s.OtherTeacher.PostAsync($"/api/school/questions/{questionId}/{path}", null, Ct), HttpStatusCode.NotFound);
            Assert.Equal(QuestionMissing, other.GetProperty("detail").GetString());
            var missing = await ProblemAsync(await s.Teacher.PostAsync($"/api/school/questions/{Guid.NewGuid()}/{path}", null, Ct), HttpStatusCode.NotFound);
            Assert.Equal(QuestionMissing, missing.GetProperty("detail").GetString());
        }

        var elsewhere = await kit.SchoolAsync("Archive Access Other School");
        var foreign = await ProblemAsync(await elsewhere.Owner.PostAsync($"/api/school/questions/{questionId}/archive", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(QuestionMissing, foreign.GetProperty("detail").GetString());

        Assert.True((await Json(await s.Teacher.PostAsync($"/api/school/questions/{questionId}/archive", null, Ct))).GetProperty("archived").GetBoolean());
        Assert.True(await api.WithDbAsync(db => db.BankQuestions.IgnoreQueryFilters().Where(q => q.Id == questionId).Select(q => q.Archived).SingleAsync(Ct)));
    }

    // ---------- I5: removing a class subject that has work ----------

    [Fact]
    public async Task Class_subject_with_work_cannot_be_removed()
    {
        var s = await CbtAsync("Remove Subject School");
        await QuestionAsync(s, "Kept question");
        var paperId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId = s.MathsId, termId = s.TermId, title = "Practice", durationMinutes = 40,
            openAt = DateTimeOffset.UtcNow.AddHours(1), closeAt = DateTimeOffset.UtcNow.AddHours(3), drawCount = 1, pickQuestionIds = Array.Empty<Guid>(),
        }, Ct))).GetProperty("id").GetGuid();

        var problem = await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/classes/{s.ClassId}/subjects/{s.MathsSubjectId}", Ct), HttpStatusCode.Conflict);
        Assert.Equal(HasWork, problem.GetProperty("detail").GetString());
        Assert.True(await api.WithDbAsync(db => db.CbtPapers.IgnoreQueryFilters().AnyAsync(p => p.Id == paperId, Ct)));

        // An exam sitting blocks it too.
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", new
        {
            termId = s.TermId, name = "First term examination", kind = "Examination", start = new DateOnly(2026, 11, 16), end = new DateOnly(2026, 11, 17),
        }, Ct))).GetProperty("id").GetGuid();
        var englishId = await api.WithDbAsync(db => db.ClassSubjects.IgnoreQueryFilters().Where(c => c.ClassId == s.ClassId && c.SubjectId == s.EnglishSubjectId).Select(c => c.Id).SingleAsync(Ct));
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/exam-periods/{periodId}/sittings", new
        {
            classSubjectId = englishId, date = new DateOnly(2026, 11, 16), start = "09:00", end = "10:00", durationMinutes = 60,
            venue = "Hall A", instructions = "Answer every question.", invigilatorStaffId = (Guid?)null,
        }, Ct));
        var sitting = await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/classes/{s.ClassId}/subjects/{s.EnglishSubjectId}", Ct), HttpStatusCode.Conflict);
        Assert.Equal(HasWork, sitting.GetProperty("detail").GetString());

        // A subject with nothing attached still goes.
        var spare = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Music", code = "MUS" }, Ct));
        var spareId = spare.GetProperty("id").GetGuid();
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/classes/{s.ClassId}/subjects", new { subjectId = spareId }, Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync($"/api/school/classes/{s.ClassId}/subjects/{spareId}", Ct)).StatusCode);
    }

    // ---------- I7: a timed-out attempt is marked when it is next read ----------

    private async Task<Guid> ExamComponentAsync(Cbt s)
    {
        var assessment = await Json(await s.School.Owner.GetAsync("/api/school/assessment", Ct));
        return assessment.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Exam").GetProperty("id").GetGuid();
    }

    /// <summary>Starts the counted paper, answers its one question correctly, and moves the deadline into the past without handing in.</summary>
    private async Task<Guid> RunOutOfTimeAsync(Cbt s, HttpClient pupil, Guid paperId, bool lockFirst = false)
    {
        var started = await Json(await pupil.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        var item = Assert.Single(started.GetProperty("items").EnumerateArray());
        var right = item.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList().IndexOf("x");
        await Json(await pupil.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers", new { itemId = item.GetProperty("id").GetGuid(), chosenIndex = right }, Ct));
        if (lockFirst) (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();
        var shift = TimeSpan.FromMinutes(45);
        await api.WithDbAsync(db => db.CbtAttempts.IgnoreQueryFilters().Where(a => a.Id == attemptId).ExecuteUpdateAsync(x => x
            .SetProperty(a => a.StartedAt, a => a.StartedAt - shift).SetProperty(a => a.DeadlineAt, a => a.DeadlineAt - shift), Ct));
        return attemptId;
    }

    private Task<List<decimal>> ScoresAsync(Cbt s, Guid enrolmentId, Guid componentId) =>
        api.WithDbAsync(db => db.Scores.IgnoreQueryFilters()
            .Where(x => x.TenantId == s.School.TenantId && x.EnrolmentId == enrolmentId && x.ComponentId == componentId).Select(x => x.Value).ToListAsync(Ct));

    [Fact]
    public async Task Timed_out_counted_attempt_is_marked_when_read()
    {
        var s = await CbtAsync("Timed Out School");
        var examId = await ExamComponentAsync(s);
        var questionId = await QuestionAsync(s, "Which letter is right?", marks: 60);
        var paperId = await PaperAsync(s, 0, [questionId], examId);

        var (ada, adaEnrolment) = await PupilAsync(s, "Ada");
        var adaAttempt = await RunOutOfTimeAsync(s, ada, paperId);
        Assert.Empty(await ScoresAsync(s, adaEnrolment, examId));

        var view = await Json(await ada.GetAsync($"/api/student/cbt/attempts/{adaAttempt}", Ct));
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("submittedAt").ValueKind);
        Assert.Equal(60m, view.GetProperty("totalMarks").GetDecimal());
        Assert.Equal([60m], await ScoresAsync(s, adaEnrolment, examId));

        // Reading again does not mark twice, and handing in now is refused as already handed in.
        await Json(await ada.GetAsync($"/api/student/cbt/attempts/{adaAttempt}", Ct));
        Assert.Equal([60m], await ScoresAsync(s, adaEnrolment, examId));
        var handIn = await ProblemAsync(await ada.PostAsync($"/api/student/cbt/attempts/{adaAttempt}/submit", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal("This test has already been handed in.", handIn.GetProperty("detail").GetString());

        // The student's list and the teacher's attempts list mark theirs too.
        var (bola, bolaEnrolment) = await PupilAsync(s, "Bola");
        await RunOutOfTimeAsync(s, bola, paperId);
        var list = await Json(await bola.GetAsync("/api/student/cbt/papers", Ct));
        Assert.Equal("Submitted", list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == paperId).GetProperty("status").GetString());
        Assert.Equal([60m], await ScoresAsync(s, bolaEnrolment, examId));

        var (chi, chiEnrolment) = await PupilAsync(s, "Chi");
        var chiAttempt = await RunOutOfTimeAsync(s, chi, paperId);
        var attempts = await Json(await s.Teacher.GetAsync($"/api/school/cbt/papers/{paperId}/attempts", Ct));
        var chiRow = attempts.GetProperty("students").EnumerateArray().Single(r => r.GetProperty("enrolmentId").GetGuid() == chiEnrolment);
        var marked = Assert.Single(chiRow.GetProperty("attempts").EnumerateArray());
        Assert.Equal(chiAttempt, marked.GetProperty("attemptId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, marked.GetProperty("submittedAt").ValueKind);
        Assert.Equal(60m, Assert.Single(marked.GetProperty("items").EnumerateArray()).GetProperty("marksAwarded").GetDecimal());
        Assert.Equal([60m], await ScoresAsync(s, chiEnrolment, examId));
    }

    [Fact]
    public async Task Timed_out_attempt_on_a_locked_term_is_marked_without_a_score()
    {
        var s = await CbtAsync("Timed Out Locked School");
        var examId = await ExamComponentAsync(s);
        var questionId = await QuestionAsync(s, "Which letter is right?", marks: 60);
        var paperId = await PaperAsync(s, 0, [questionId], examId);
        var (ada, adaEnrolment) = await PupilAsync(s, "Ada");
        var attemptId = await RunOutOfTimeAsync(s, ada, paperId, lockFirst: true);

        var view = await Json(await ada.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct));
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("submittedAt").ValueKind);
        Assert.Equal(60m, view.GetProperty("totalMarks").GetDecimal());
        Assert.Empty(await ScoresAsync(s, adaEnrolment, examId));
    }
}
