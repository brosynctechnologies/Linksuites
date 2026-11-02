using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Assignments for one class subject and term. A counted mark upserts the same score cell a teacher types.
/// A practice mark stays on the submission. Nursery work is recorded by the teacher, because those children do not sign in.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolAssignmentTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

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

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid ClassSubjectId, Guid TermId, HttpClient Teacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await StaffIdAsync(school, userId);
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = staffId }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return new Setup(school, classId, link.GetProperty("id").GetGuid(), termId, teacher);
    }

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    private static string GridUrl(Setup s) => $"/api/school/scores?classSubjectId={s.ClassSubjectId}&termId={s.TermId}";

    private static Guid ComponentId(JsonElement grid, string name) =>
        grid.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static JsonElement Student(JsonElement grid, string firstName) =>
        grid.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    private async Task<Guid> ScoreIdAsync(Guid tenantId, Guid enrolmentId, Guid componentId) =>
        await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.EnrolmentId == enrolmentId && s.ComponentId == componentId)
            .Select(s => s.Id).SingleAsync(Ct));

    private Task<int> ScoreCountAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct));

    private static object Assignment(Setup s, string title, Guid? componentId, decimal? maxMarks, DateTimeOffset? dueAt = null) => new
    {
        classSubjectId = s.ClassSubjectId,
        termId = s.TermId,
        title,
        instructions = "Show the working.",
        dueAt = dueAt ?? DateTimeOffset.UtcNow.AddDays(3),
        maxMarks,
        assessmentComponentId = componentId,
    };

    [Fact]
    public async Task Counted_assignment_matches_typed_score()
    {
        var s = await SetupAsync("Counted Assignment School");
        var adaId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var toluId = await kit.StudentAsync(s.School, s.ClassId, "Tolu", "Adeyemi");
        var ada = await EnrolmentAsync(s.School, adaId);
        var tolu = await EnrolmentAsync(s.School, toluId);
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        Assert.Equal(20m, grid.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == ca1).GetProperty("maxScore").GetDecimal());

        // Typing 20 stores one cell. The assignment mark must update that same row, not a second one.
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId,
            entries = new[] { new { enrolmentId = ada, componentId = ca1, value = 20m } },
        }, Ct)).EnsureSuccessStatusCode();
        var typedId = await ScoreIdAsync(s.School.TenantId, ada, ca1);

        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", Assignment(s, "CA1 homework", ca1, 10m), Ct));
        var id = created.GetProperty("id").GetGuid();
        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{ada}", new { mark = 10m }, Ct));
        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{tolu}", new { mark = 10m }, Ct));

        Assert.Equal(typedId, await ScoreIdAsync(s.School.TenantId, ada, ca1));
        var rows = await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters()
            .Where(x => x.TenantId == s.School.TenantId && x.ComponentId == ca1)
            .Select(x => new { x.EnrolmentId, x.Value }).ToListAsync(Ct));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(20m, row.Value));
        Assert.Contains(rows, row => row.EnrolmentId == ada);
        Assert.Contains(rows, row => row.EnrolmentId == tolu);

        var sheet = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        Assert.Equal(20m, Student(sheet, "Ada").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());
        Assert.Equal(20m, Student(sheet, "Tolu").GetProperty("values").GetProperty(ca1.ToString()).GetDecimal());
    }

    [Fact]
    public async Task Practice_assignment_does_not_write_score()
    {
        var s = await SetupAsync("Practice Assignment School");
        var adaId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var ada = await EnrolmentAsync(s.School, adaId);
        var guardianId = await kit.GuardianAsync(s.School, adaId, "Ngozi Obi", "08031112233", $"ada-practice-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(s.School, guardianId);
        var student = await kit.StudentLoginAsync(s.School, adaId);

        var due = DateTimeOffset.UtcNow.AddHours(-2);
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments",
            Assignment(s, "Practice fractions", null, 10m, due), Ct));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, created.GetProperty("assessmentComponentId").ValueKind);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("I got one half."), "body");
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "working.png");
        var submitted = await Json(await student.PostAsync($"/api/student/assignments/{id}/submit", form, Ct));
        Assert.True(submitted.GetProperty("late").GetBoolean());
        Assert.True(submitted.GetProperty("submitted").GetBoolean());
        var link = submitted.GetProperty("fileUrl").GetString()!;
        Assert.Contains("/api/school/assignment-files/", link);
        var anonymous = api.CreateClient();
        var path = new Uri(link).PathAndQuery;
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path, Ct)).StatusCode);
        var parts = path.Split('/');
        parts[^1] = new string(parts[^1].Reverse().ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);
        var key = await api.WithDbAsync(db => db.AssignmentSubmissions.IgnoreQueryFilters().Where(x => x.AssignmentId == id).Select(x => x.FileKey).SingleAsync(Ct));
        Assert.StartsWith("private/", key);

        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{ada}", new { mark = 8m, feedback = "Show one more step." }, Ct));
        Assert.Equal(0, await ScoreCountAsync(s.School.TenantId));

        var locked = await ProblemAsync(await student.PostAsync($"/api/student/assignments/{id}/submit", new MultipartFormDataContent
        {
            { new StringContent("A replacement."), "body" },
        }, Ct), HttpStatusCode.Conflict);
        Assert.Contains("reopen", locked.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{ada}", new { reopen = true }, Ct));
        var replaced = await Json(await student.PostAsync($"/api/student/assignments/{id}/submit", new MultipartFormDataContent
        {
            { new StringContent("The replacement."), "body" },
        }, Ct));
        Assert.Equal("The replacement.", replaced.GetProperty("body").GetString());
        Assert.Equal(0, await ScoreCountAsync(s.School.TenantId));

        var parentView = await Json(await parent.GetAsync($"/api/parent/children/{adaId}/assignments", Ct));
        var item = Assert.Single(parentView.EnumerateArray());
        Assert.Equal("Practice fractions", item.GetProperty("title").GetString());
        Assert.True(item.GetProperty("submitted").GetBoolean());
        Assert.Equal(8m, item.GetProperty("mark").GetDecimal());
        Assert.False(item.TryGetProperty("feedback", out _));
        Assert.False(item.TryGetProperty("instructions", out _));
        Assert.False(item.TryGetProperty("fileUrl", out _));
        Assert.DoesNotContain("Show the working", parentView.GetRawText());
        Assert.DoesNotContain("Show one more step", parentView.GetRawText());
    }

    [Fact]
    public async Task Locked_term_refuses_counted_mark()
    {
        var s = await SetupAsync("Locked Assignment School");
        var adaId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var ada = await EnrolmentAsync(s.School, adaId);
        var grid = await Json(await s.Teacher.GetAsync(GridUrl(s), Ct));
        var ca1 = ComponentId(grid, "CA1");
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", Assignment(s, "Locked CA1", ca1, 10m), Ct));
        var id = created.GetProperty("id").GetGuid();
        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();

        var problem = await ProblemAsync(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{ada}", new { mark = 10m }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("This term is locked for the class, so scores can't change. Ask the principal to unlock it.", problem.GetProperty("detail").GetString());
        Assert.Equal("term_locked", problem.GetProperty("code").GetString());
        Assert.Equal(0, await ScoreCountAsync(s.School.TenantId));

        // A practice mark does not write a score, so the lock does not apply to it.
        var practice = await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", Assignment(s, "Locked practice", null, null), Ct));
        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{practice.GetProperty("id").GetGuid()}/submissions/{ada}", new { mark = 4m, feedback = "Noted." }, Ct));
        Assert.Equal(0, await ScoreCountAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Foreign_id_is_404()
    {
        var s = await SetupAsync("Foreign Assignment School");
        var adaId = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var toluId = await kit.StudentAsync(s.School, s.ClassId, "Tolu", "Adeyemi");
        var ada = await EnrolmentAsync(s.School, adaId);
        var guardianId = await kit.GuardianAsync(s.School, adaId, "Ngozi Obi", "08032223344", $"ada-foreign-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(s.School, guardianId);
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", Assignment(s, "Week 3", null, 10m), Ct));
        var id = created.GetProperty("id").GetGuid();

        var missing = await ProblemAsync(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{Guid.NewGuid()}/submissions/{ada}", new { mark = 1m }, Ct), HttpStatusCode.NotFound);
        Assert.Equal("This assignment was not found.", missing.GetProperty("detail").GetString());

        var (otherTeacher, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        var hidden = await ProblemAsync(await otherTeacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{ada}", new { mark = 1m }, Ct), HttpStatusCode.NotFound);
        Assert.Equal("This assignment was not found.", hidden.GetProperty("detail").GetString());

        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/assignments", Assignment(s, "Bursar homework", null, 5m), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/assignments?classSubjectId={s.ClassSubjectId}&termId={s.TermId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{ada}", new { mark = 1m }, Ct)).StatusCode);

        var child = await ProblemAsync(await parent.GetAsync($"/api/parent/children/{toluId}/assignments", Ct), HttpStatusCode.NotFound);
        Assert.Equal("Child was not found.", child.GetProperty("detail").GetString());

        var own = await Json(await parent.GetAsync($"/api/parent/children/{adaId}/assignments", Ct));
        Assert.Equal("Week 3", Assert.Single(own.EnumerateArray()).GetProperty("title").GetString());
        Assert.False(Assert.Single(own.EnumerateArray()).GetProperty("submitted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Assert.Single(own.EnumerateArray()).GetProperty("mark").ValueKind);
    }

    [Fact]
    public async Task Nursery_teacher_records_received_without_student_login()
    {
        var school = await kit.SchoolAsync("Nursery Assignment School");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        Assert.Equal("Developmental", level.GetProperty("assessmentMode").GetString());
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Nursery teacher");
        var staffId = await StaffIdAsync(school, userId);
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Numeracy", code = "NUM" }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = staffId }, Ct));
        var classSubjectId = link.GetProperty("id").GetGuid();
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(school, studentId);

        var login = await ProblemAsync(await school.Owner.PostAsJsonAsync($"/api/school/students/{studentId}/login", new { password = "student-pass-1" }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("don't sign in", login.GetProperty("detail").GetString());
        Assert.Null(await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.Id == studentId).Select(s => s.UserId).SingleAsync(Ct)));

        var created = await Json(await teacher.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId, termId, title = "Bring a leaf", instructions = "Any green leaf.", dueAt = DateTimeOffset.UtcNow.AddDays(1),
        }, Ct));
        var id = created.GetProperty("id").GetGuid();
        var saved = await Json(await teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{enrolmentId}",
            new { received = true, feedback = "Brought a green leaf." }, Ct));
        var row = saved.GetProperty("submissions").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == enrolmentId);
        Assert.Equal("Received", row.GetProperty("status").GetString());
        Assert.Equal("Brought a green leaf.", row.GetProperty("feedback").GetString());
        Assert.True(row.GetProperty("submitted").GetBoolean());

        var notReceived = await Json(await teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{enrolmentId}", new { received = false }, Ct));
        var cleared = notReceived.GetProperty("submissions").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == enrolmentId);
        Assert.Equal("None", cleared.GetProperty("status").GetString());
        Assert.False(cleared.GetProperty("submitted").GetBoolean());

        await Json(await teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{enrolmentId}",
            new { received = true, feedback = "Brought a green leaf." }, Ct));
        Assert.Equal(0, await ScoreCountAsync(school.TenantId));
    }
}
