using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink score entry: the grid, validation, who may enter scores, and locking a class's term.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolScoreTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

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

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid ClassSubjectId, Guid TermId, Guid TeacherStaffId, HttpClient Teacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Guid> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code, Guid? teacherStaffId)
    {
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId }, Ct));
        return link.GetProperty("id").GetGuid();
    }

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await StaffIdAsync(school, userId);
        var classSubjectId = await ClassSubjectAsync(school, classId, "Mathematics", "MTH", staffId);
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return new Setup(school, classId, classSubjectId, termId, staffId, teacher);
    }

    private static string GridUrl(Setup s) => $"/api/school/scores?classSubjectId={s.ClassSubjectId}&termId={s.TermId}";

    private static object Put(Setup s, params (Guid Enrolment, Guid Component, decimal? Value)[] entries) =>
        new { classSubjectId = s.ClassSubjectId, termId = s.TermId, entries = entries.Select(e => new { enrolmentId = e.Enrolment, componentId = e.Component, value = e.Value }).ToArray() };

    private static Guid ComponentId(JsonElement grid, string name) =>
        grid.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static Guid EnrolmentOf(JsonElement grid, string firstName) =>
        grid.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName)).GetProperty("enrolmentId").GetGuid();

    private static JsonElement Student(JsonElement grid, string firstName) =>
        grid.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    [Fact]
    public async Task Scores_save_and_reload()
    {
        var s = await SetupAsync("Score Academy");
        await kit.StudentAsync(s.School, s.ClassId, "Tolu", "Adeyemi");
        await kit.StudentAsync(s.School, s.ClassId, "Ada", "Obi");

        var empty = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.False(empty.GetProperty("locked").GetBoolean());
        Assert.Equal(new[] { "CA1", "CA2", "Exam" }, empty.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray());
        Assert.Equal(new[] { 20m, 20m, 60m }, empty.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("maxScore").GetDecimal()).ToArray());
        // Ordered by last name: Adeyemi before Obi.
        Assert.StartsWith("Tolu", empty.GetProperty("students")[0].GetProperty("name").GetString());
        Assert.All(empty.GetProperty("students").EnumerateArray(), st => Assert.All(st.GetProperty("values").EnumerateObject(), v => Assert.Equal(JsonValueKind.Null, v.Value.ValueKind)));

        var ca1 = ComponentId(empty, "CA1");
        var exam = ComponentId(empty, "Exam");
        var tolu = EnrolmentOf(empty, "Tolu");
        var ada = EnrolmentOf(empty, "Ada");
        var saved = await Json(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (tolu, ca1, 18m), (tolu, exam, 55.5m), (ada, ca1, 0m)), Ct));
        Assert.Equal(18m, Student(saved, "Tolu").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());

        var again = await Json(await s.School.Owner.GetAsync(GridUrl(s), Ct));
        Assert.Equal(18m, Student(again, "Tolu").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());
        Assert.Equal(55.5m, Student(again, "Tolu").GetProperty("values").GetProperty(exam.ToString()).GetDecimal());
        Assert.Equal(0m, Student(again, "Ada").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());
        Assert.Equal(JsonValueKind.Null, Student(again, "Ada").GetProperty("values").GetProperty(exam.ToString()).ValueKind);

        // A partial save leaves the other cells alone; null clears a cell.
        await Json(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (tolu, ca1, null)), Ct));
        var cleared = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.Equal(JsonValueKind.Null, Student(cleared, "Tolu").GetProperty("values").GetProperty(ca1.ToString()).ValueKind);
        Assert.Equal(55.5m, Student(cleared, "Tolu").GetProperty("values").GetProperty(exam.ToString()).GetDecimal());
        Assert.Equal(2, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Values_above_the_max_or_negative_are_400()
    {
        var s = await SetupAsync("Max Score School");
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var enrolment = EnrolmentOf(grid, "Ada");

        await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 20.5m)), Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, -1m)), Ct), HttpStatusCode.BadRequest);
        // One bad cell rejects the whole save.
        var exam = ComponentId(grid, "Exam");
        await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, exam, 40m), (enrolment, ca1, 21m)), Ct), HttpStatusCode.BadRequest);
        Assert.Equal(0, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.School.TenantId, Ct)));

        // The maximum itself is allowed, and a component outside the scheme or a stranger's enrolment is refused.
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 20m)), Ct)).EnsureSuccessStatusCode();
        var unknownComponent = await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, Guid.NewGuid(), 5m)), Ct), HttpStatusCode.BadRequest);
        Assert.True(unknownComponent.GetProperty("errors").TryGetProperty("componentId", out _));
        var unknownEnrolment = await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (Guid.NewGuid(), ca1, 5m)), Ct), HttpStatusCode.BadRequest);
        Assert.True(unknownEnrolment.GetProperty("errors").TryGetProperty("entries", out _));
    }

    [Fact]
    public async Task Term_must_belong_to_the_classes_session()
    {
        var s = await SetupAsync("Term Session School");
        var otherTerm = await api.WithDbAsync(async db =>
        {
            var session = new AcademicSession { TenantId = s.School.TenantId, Name = "2020/2021", StartDate = new DateOnly(2020, 9, 1), EndDate = new DateOnly(2021, 7, 30) };
            var term = new Term { TenantId = s.School.TenantId, SessionId = session.Id, Number = 1, Name = "Old term", StartDate = new DateOnly(2020, 9, 1), EndDate = new DateOnly(2020, 12, 15) };
            db.AcademicSessions.Add(session);
            db.Terms.Add(term);
            await db.SaveChangesAsync(Ct);
            return term.Id;
        });
        var problem = await ProblemAsync(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.ClassSubjectId}&termId={otherTerm}", Ct), HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("termId", out _));
    }

    [Fact]
    public async Task Only_the_assigned_subject_teacher_or_leadership_can_enter_scores()
    {
        var s = await SetupAsync("Scoring Roles School");
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var enrolment = EnrolmentOf(grid, "Ada");
        var body = Put(s, (enrolment, ca1, 10m));

        var (otherTeacher, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        await ProblemAsync(await otherTeacher.PutAsJsonAsync("/api/school/scores", body, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await otherTeacher.GetAsync(GridUrl(s), Ct), HttpStatusCode.Forbidden);

        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await ProblemAsync(await bursar.PutAsJsonAsync("/api/school/scores", body, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await bursar.GetAsync(GridUrl(s), Ct), HttpStatusCode.Forbidden);
        Assert.Equal(0, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.School.TenantId, Ct)));

        // The class teacher reads every subject of the class but cannot write another teacher's subject.
        var (classTeacher, classTeacherUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        var classTeacherStaff = await StaffIdAsync(s.School, classTeacherUser);
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId = classTeacherStaff }, Ct)).EnsureSuccessStatusCode();
        (await classTeacher.GetAsync(GridUrl(s), Ct)).EnsureSuccessStatusCode();
        await ProblemAsync(await classTeacher.PutAsJsonAsync("/api/school/scores", body, Ct), HttpStatusCode.Forbidden);

        // Leadership and the assigned teacher can.
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", body, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 12m)), Ct)).EnsureSuccessStatusCode();
        var final = await Json(await s.School.Owner.GetAsync(GridUrl(s), Ct));
        Assert.Equal(12m, Student(final, "Ada").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());
    }

    [Fact]
    public async Task Locked_terms_refuse_score_changes_and_unlock_is_audited()
    {
        var s = await SetupAsync("Lock School");
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var enrolment = EnrolmentOf(grid, "Ada");
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 10m)), Ct)).EnsureSuccessStatusCode();

        var lockUrl = $"/api/school/results/{s.ClassId}/{s.TermId}/lock";
        var unlockUrl = $"/api/school/results/{s.ClassId}/{s.TermId}/unlock";

        // Teachers cannot lock; unlocking something not locked is a conflict.
        await ProblemAsync(await s.Teacher.PostAsync(lockUrl, null, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await s.School.Owner.PostAsync(unlockUrl, null, Ct), HttpStatusCode.Conflict);

        (await s.School.Owner.PostAsync(lockUrl, null, Ct)).EnsureSuccessStatusCode();
        await ProblemAsync(await s.School.Owner.PostAsync(lockUrl, null, Ct), HttpStatusCode.Conflict);
        Assert.True((await Json(await s.Teacher.GetAsync(GridUrl(s), Ct))).GetProperty("locked").GetBoolean());

        await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 11m)), Ct), HttpStatusCode.Conflict);
        await ProblemAsync(await s.School.Owner.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, null)), Ct), HttpStatusCode.Conflict);
        var held = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.Equal(10m, Student(held, "Ada").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());

        await ProblemAsync(await s.Teacher.PostAsync(unlockUrl, null, Ct), HttpStatusCode.Forbidden);
        (await s.School.Owner.PostAsync(unlockUrl, null, Ct)).EnsureSuccessStatusCode();
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 11m)), Ct)).EnsureSuccessStatusCode();

        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == s.School.TenantId && a.EntityType == "TermResultLock").OrderBy(a => a.Id).ToListAsync(Ct));
        Assert.Equal(new[] { "results.lock", "results.unlock" }, audits.Select(a => a.Action).ToArray());
        Assert.All(audits, a =>
        {
            Assert.Contains(s.ClassId.ToString(), a.AfterJson);
            Assert.Contains(s.TermId.ToString(), a.AfterJson);
            Assert.NotNull(a.ActorId);
        });
        // Score changes are audited too.
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.TenantId == s.School.TenantId && a.EntityType == "Score" && a.Action == "modified", Ct)));
    }

    [Fact]
    public async Task Decimals_are_kept_to_one_place()
    {
        var s = await SetupAsync("Decimal School");
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var exam = ComponentId(grid, "Exam");
        var enrolment = EnrolmentOf(grid, "Ada");

        await ProblemAsync(await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, exam, 12.25m)), Ct), HttpStatusCode.BadRequest);
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, exam, 12.5m)), Ct)).EnsureSuccessStatusCode();
        var reread = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.Equal(12.5m, Student(reread, "Ada").GetProperty("values").GetProperty(exam.ToString()).GetDecimal());
    }

    [Fact]
    public async Task Two_teachers_saving_the_same_cell_leave_one_row_with_the_last_write()
    {
        var s = await SetupAsync("Same Cell School");
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var enrolment = EnrolmentOf(grid, "Ada");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            (i % 2 == 0 ? s.Teacher : s.School.Owner).PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 10m + i)), Ct)));
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}"));
        Assert.Equal(1, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Students_with_scores_cannot_be_deleted()
    {
        var s = await SetupAsync("History Scores School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (EnrolmentOf(grid, "Ada"), ComponentId(grid, "CA1"), 10m)), Ct)).EnsureSuccessStatusCode();

        await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/students/{studentId}", Ct), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Editing_the_scheme_keeps_the_scores_and_refuses_to_drop_a_scored_component()
    {
        var s = await SetupAsync("Scheme Keeps Scores School");
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var exam = ComponentId(grid, "Exam");
        var enrolment = EnrolmentOf(grid, "Ada");
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, exam, 50m)), Ct)).EnsureSuccessStatusCode();

        // Renaming and re-weighting in place keeps the column and its scores.
        var ca1 = ComponentId(grid, "CA1"); var ca2 = ComponentId(grid, "CA2");
        (await s.School.Owner.PutAsJsonAsync("/api/school/assessment", Scheme(null, (ca1, "Test 1", 10), (ca2, "Test 2", 10), (exam, "Exam", 80)), Ct)).EnsureSuccessStatusCode();
        var after = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.Equal(exam, ComponentId(after, "Exam"));
        Assert.Equal(50m, Student(after, "Ada").GetProperty("values").GetProperty(exam.ToString()).GetDecimal());

        // Dropping the scored component is refused.
        await ProblemAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment", new { components = new object[]
        {
            new { name = "Test 1", maxScore = 40 }, new { name = "Test 2", maxScore = 60 },
        } }, Ct), HttpStatusCode.Conflict);
    }

    // ---------- Fix round 1: the scheme under existing scores ----------

    private const string SchemeLocked = "Scores have been entered with this assessment";

    private static object Scheme(Guid? levelId, params (Guid? Id, string Name, decimal Max)[] parts) =>
        new { levelId, components = parts.Select(p => new { id = p.Id, name = p.Name, maxScore = p.Max }).ToArray() };

    private async Task<(Setup S, Guid Ca1, Guid Ca2, Guid Exam, Guid Enrolment)> ScoredAsync(string name, decimal ca1Score = 18m)
    {
        var s = await SetupAsync(name);
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var enrolment = EnrolmentOf(grid, "Ada");
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, ca1Score)), Ct)).EnsureSuccessStatusCode();
        return (s, ca1, ComponentId(grid, "CA2"), ComponentId(grid, "Exam"), enrolment);
    }

    private static async Task AssertConflictAsync(HttpResponseMessage r, string contains)
    {
        var problem = await ProblemAsync(r, HttpStatusCode.Conflict);
        Assert.Contains(contains, problem.ToString());
    }

    [Fact]
    public async Task A_level_override_is_refused_once_scores_exist_under_the_default()
    {
        var (s, _, _, _, _) = await ScoredAsync("Override Blocked School");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == s.ClassId).Select(c => c.LevelId).FirstAsync(Ct));
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(levelId, (null, "Test", 40), (null, "Exam", 60)), Ct), SchemeLocked);
    }

    [Fact]
    public async Task The_maximum_cannot_drop_below_a_stored_score()
    {
        var (s, ca1, ca2, exam, _) = await ScoredAsync("Lower Max School", 18m);
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "CA1", 15), (ca2, "CA2", 25), (exam, "Exam", 60)), Ct), "above the new maximum");
        // At the stored score is fine.
        (await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "CA1", 18), (ca2, "CA2", 22), (exam, "Exam", 60)), Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "CA1", 25), (ca2, "CA2", 15), (exam, "Exam", 60)), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Parts_cannot_be_added_removed_or_reordered_once_scores_exist_but_can_be_renamed()
    {
        var (s, ca1, ca2, exam, _) = await ScoredAsync("Reorder School");
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca2, "CA2", 20), (ca1, "CA1", 20), (exam, "Exam", 60)), Ct), SchemeLocked);
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca2, "CA2", 40), (exam, "Exam", 60)), Ct), SchemeLocked);
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "CA1", 20), (ca2, "CA2", 20), (exam, "Exam", 50), (null, "Project", 10)), Ct), SchemeLocked);
        // Without ids the parts look new, which is also a change of structure.
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (null, "CA1", 20), (null, "CA2", 20), (null, "Exam", 60)), Ct), SchemeLocked);

        (await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "Test 1", 20), (ca2, "Test 2", 20), (exam, "Final exam", 60)), Ct)).EnsureSuccessStatusCode();
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.Equal(18m, Student(grid, "Ada").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());
        Assert.Equal("Test 1", grid.GetProperty("components")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Unknown_component_ids_are_a_400()
    {
        var (s, _, ca2, exam, _) = await ScoredAsync("Unknown Id School");
        var problem = await ProblemAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (Guid.NewGuid(), "CA1", 20), (ca2, "CA2", 20), (exam, "Exam", 60)), Ct), HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("components", out _));
    }

    [Fact]
    public async Task The_scheme_cannot_change_while_a_class_term_is_locked()
    {
        var (s, ca1, ca2, exam, _) = await ScoredAsync("Locked Scheme School");
        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();
        await AssertConflictAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "Test 1", 20), (ca2, "CA2", 20), (exam, "Exam", 60)), Ct), "Results are locked");
    }

    [Fact]
    public async Task Resetting_a_level_override_with_scores_is_refused()
    {
        var s = await SetupAsync("Reset Override School");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == s.ClassId).Select(c => c.LevelId).FirstAsync(Ct));
        (await s.School.Owner.PutAsJsonAsync("/api/school/assessment", Scheme(levelId, (null, "CA", 40), (null, "Exam", 60)), Ct)).EnsureSuccessStatusCode();
        await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (EnrolmentOf(grid, "Ada"), ComponentId(grid, "CA"), 30m)), Ct)).EnsureSuccessStatusCode();

        await AssertConflictAsync(await s.School.Owner.DeleteAsync($"/api/school/assessment/{levelId}", Ct), SchemeLocked);
    }

    [Fact]
    public async Task A_class_subject_with_scores_cannot_be_removed()
    {
        var (s, _, _, _, _) = await ScoredAsync("Remove Subject School");
        var subjectId = await api.WithDbAsync(db => db.ClassSubjects.IgnoreQueryFilters().Where(c => c.Id == s.ClassSubjectId).Select(c => c.SubjectId).FirstAsync(Ct));
        await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/classes/{s.ClassId}/subjects/{subjectId}", Ct), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Two_first_requests_create_one_default_scheme()
    {
        var s = await SetupAsync("First Use School");
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? s.Teacher : s.School.Owner).GetAsync(GridUrl(s), Ct)));
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}"));
        Assert.Equal(3, await api.WithDbAsync(db => db.AssessmentComponents.IgnoreQueryFilters().CountAsync(c => c.TenantId == s.School.TenantId && c.LevelId == null, Ct)));
    }

    [Fact]
    public async Task The_same_component_twice_in_one_request_is_a_400()
    {
        var s = await SetupAsync("Duplicate Id School");
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var problem = await ProblemAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment",
            Scheme(null, (ca1, "CA1", 40), (ca1, "CA1 again", 60)), Ct), HttpStatusCode.BadRequest);
        Assert.Contains("Each part can appear once.", problem.ToString());
    }

    [Fact]
    public async Task A_score_save_racing_a_lowered_maximum_never_leaves_a_score_above_the_maximum()
    {
        for (var round = 0; round < 6; round++)
        {
            var s = await SetupAsync($"Race School {round}");
            await kit.StudentAsync(s.School, s.ClassId, "Ada");
            var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
            var ca1 = ComponentId(grid, "CA1");
            var ca2 = ComponentId(grid, "CA2");
            var exam = ComponentId(grid, "Exam");
            var enrolment = EnrolmentOf(grid, "Ada");

            var save = s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, 20m)), Ct);
            var lower = s.School.Owner.PutAsJsonAsync("/api/school/assessment", Scheme(null, (ca1, "CA1", 10), (ca2, "CA2", 30), (exam, "Exam", 60)), Ct);
            var responses = await Task.WhenAll(save, lower);
            Assert.All(responses, r => Assert.True((int)r.StatusCode is 200 or 400 or 409, $"{(int)r.StatusCode}"));

            var (max, stored) = await api.WithDbAsync(async db => (
                await db.AssessmentComponents.IgnoreQueryFilters().Where(c => c.Id == ca1).Select(c => c.MaxScore).FirstAsync(Ct),
                await db.Scores.IgnoreQueryFilters().Where(x => x.ComponentId == ca1).Select(x => (decimal?)x.Value).MaxAsync(Ct)));
            Assert.True(stored is null || stored <= max, $"round {round}: stored {stored} above max {max}");
        }
    }

    [Fact]
    public async Task Clearing_a_score_writes_an_audit_row()
    {
        var (s, ca1, _, _, enrolment) = await ScoredAsync("Clear Audit School");
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", Put(s, (enrolment, ca1, null)), Ct)).EnsureSuccessStatusCode();
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.TenantId == s.School.TenantId && a.EntityType == "Score" && a.Action == "deleted", Ct)));
    }
}
