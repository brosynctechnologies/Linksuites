using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Homework submissions (wave 4, 4b item 2): a parent sends the work for a child whose level has no student logins (an upload, typed
/// text, or both); late work is flagged by the due date; the teacher sees who has and hasn't sent it and marks it with a score and a
/// comment; a counted score lands on the score sheet as CBT scores do; parents see the mark and the comment for their own children only.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolHomeworkSubmissionTests(ApiFixture api)
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

    private sealed record Child(Guid StudentId, Guid EnrolmentId, HttpClient Parent);
    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid ClassSubjectId, Guid TermId, HttpClient Teacher, Child Ada, Child Tolu);

    /// <summary>A class of the given level with Mathematics taught by its teacher, and two children each with their own parent account.</summary>
    private async Task<Setup> SetupAsync(string name, bool studentLogins)
    {
        var school = await kit.SchoolAsync(name);
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = studentLogins ? "JSS 1" : "Primary 1", stage = studentLogins ? "Jss" : "Primary", order = 5, hasStudentLogins = studentLogins }, Ct));
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = staffId }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return new Setup(school, classId, link.GetProperty("id").GetGuid(), termId, teacher, await ChildAsync(school, classId, "Ada"), await ChildAsync(school, classId, "Tolu"));
    }

    private async Task<Child> ChildAsync(SchoolKit.School school, Guid classId, string firstName)
    {
        var studentId = await kit.StudentAsync(school, classId, firstName);
        var guardianId = await kit.GuardianAsync(school, studentId, $"{firstName} Parent", null, $"{firstName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);
        var enrolmentId = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.StudentId == studentId).Select(e => e.Id).SingleAsync(Ct));
        return new Child(studentId, enrolmentId, parent);
    }

    private async Task<Guid> CreateAsync(Setup s, string title, Guid? componentId, decimal? maxMarks, DateTimeOffset dueAt) =>
        (await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, title, instructions = "Do exercise 4.", dueAt, maxMarks, assessmentComponentId = componentId,
        }, Ct))).GetProperty("id").GetGuid();

    private static MultipartFormDataContent Work(string? text, bool withFile)
    {
        var form = new MultipartFormDataContent();
        if (text is not null) form.Add(new StringContent(text), "body");
        if (withFile)
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "homework.png");
        }
        return form;
    }

    private async Task<Guid> Ca1Async(Setup s) =>
        (await Json(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.ClassSubjectId}&termId={s.TermId}", Ct)))
            .GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "CA1").GetProperty("id").GetGuid();

    [Fact]
    public async Task Parent_sends_work_teacher_marks_it_and_parent_sees_mark_and_comment()
    {
        var s = await SetupAsync("Homework Parent School", studentLogins: false);
        var ca1 = await Ca1Async(s);
        var id = await CreateAsync(s, "Fractions", ca1, 10m, DateTimeOffset.UtcNow.AddHours(-1));

        // Before anything is sent: the parent can send it, and the teacher sees both children as not submitted.
        var before = Assert.Single((await Json(await s.Ada.Parent.GetAsync($"/api/parent/children/{s.Ada.StudentId}/assignments", Ct))).EnumerateArray());
        Assert.True(before.GetProperty("canSubmit").GetBoolean());
        Assert.Equal("Do exercise 4.", before.GetProperty("instructions").GetString());
        Assert.Equal("Mathematics", before.GetProperty("subject").GetString());

        var sent = await Json(await s.Ada.Parent.PostAsync($"/api/parent/children/{s.Ada.StudentId}/assignments/{id}/submit", Work("Ada's answers: 1/2, 3/4.", withFile: true), Ct));
        Assert.True(sent.GetProperty("submitted").GetBoolean());
        Assert.True(sent.GetProperty("late").GetBoolean());
        Assert.Equal("Ada's answers: 1/2, 3/4.", sent.GetProperty("body").GetString());
        var link = sent.GetProperty("fileUrl").GetString()!;
        Assert.Contains("/api/school/assignment-files/", link);
        var key = await api.WithDbAsync(db => db.AssignmentSubmissions.IgnoreQueryFilters().Where(x => x.AssignmentId == id).Select(x => x.FileKey).SingleAsync(Ct));
        Assert.StartsWith($"private/school/{s.School.TenantId}/assignments/", key);
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync(new Uri(link).PathAndQuery, Ct)).StatusCode);

        // The teacher's list for the assignment: who has sent it, who hasn't, and what is late.
        var one = await Json(await s.Teacher.GetAsync($"/api/school/assignments/{id}", Ct));
        Assert.Equal(1, one.GetProperty("submittedCount").GetInt32());
        Assert.Equal(1, one.GetProperty("missingCount").GetInt32());
        Assert.Equal(1, one.GetProperty("lateCount").GetInt32());
        Assert.Equal(0, one.GetProperty("markedCount").GetInt32());
        var adaRow = one.GetProperty("submissions").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == s.Ada.EnrolmentId);
        Assert.True(adaRow.GetProperty("sentByParent").GetBoolean());
        Assert.True(adaRow.GetProperty("late").GetBoolean());
        var toluRow = one.GetProperty("submissions").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == s.Tolu.EnrolmentId);
        Assert.False(toluRow.GetProperty("submitted").GetBoolean());

        // Marked 8 out of 10 with a comment: CA1 (out of 20) becomes 16, as a CBT score would.
        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{s.Ada.EnrolmentId}", new { mark = 8m, feedback = "Good work. Check question 3." }, Ct));
        var score = await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().Where(x => x.EnrolmentId == s.Ada.EnrolmentId && x.ComponentId == ca1).Select(x => x.Value).SingleAsync(Ct));
        Assert.Equal(16m, score);
        Assert.Equal(1, (await Json(await s.Teacher.GetAsync($"/api/school/assignments/{id}", Ct))).GetProperty("markedCount").GetInt32());

        var after = Assert.Single((await Json(await s.Ada.Parent.GetAsync($"/api/parent/children/{s.Ada.StudentId}/assignments", Ct))).EnumerateArray());
        Assert.Equal(8m, after.GetProperty("mark").GetDecimal());
        Assert.Equal("Good work. Check question 3.", after.GetProperty("feedback").GetString());
        Assert.True(after.GetProperty("late").GetBoolean());
        Assert.False(after.GetProperty("canSubmit").GetBoolean());

        // Marked work is not replaced until the teacher reopens it.
        var marked = await ProblemAsync(await s.Ada.Parent.PostAsync($"/api/parent/children/{s.Ada.StudentId}/assignments/{id}/submit", Work("Again.", false), Ct), HttpStatusCode.Conflict);
        Assert.Equal("submission_marked", marked.GetProperty("code").GetString());

        // Tolu's parent sees Tolu's (nothing sent, no mark), never Ada's mark or comment, and cannot reach Ada.
        var tolu = await Json(await s.Tolu.Parent.GetAsync($"/api/parent/children/{s.Tolu.StudentId}/assignments", Ct));
        Assert.Equal(JsonValueKind.Null, Assert.Single(tolu.EnumerateArray()).GetProperty("mark").ValueKind);
        Assert.DoesNotContain("Check question 3", tolu.GetRawText());
        await ProblemAsync(await s.Tolu.Parent.GetAsync($"/api/parent/children/{s.Ada.StudentId}/assignments", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await s.Tolu.Parent.PostAsync($"/api/parent/children/{s.Ada.StudentId}/assignments/{id}/submit", Work("Not mine.", false), Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Text_only_on_time_work_is_not_late_and_empty_work_is_refused()
    {
        var s = await SetupAsync("Homework Text School", studentLogins: false);
        var id = await CreateAsync(s, "Spelling", null, 10m, DateTimeOffset.UtcNow.AddDays(2));
        var empty = await ProblemAsync(await s.Tolu.Parent.PostAsync($"/api/parent/children/{s.Tolu.StudentId}/assignments/{id}/submit", Work("   ", false), Ct), HttpStatusCode.BadRequest);
        Assert.Contains("Write your answer or attach a file.", empty.GetRawText());
        var sent = await Json(await s.Tolu.Parent.PostAsync($"/api/parent/children/{s.Tolu.StudentId}/assignments/{id}/submit", Work("cat, dog, sun", false), Ct));
        Assert.False(sent.GetProperty("late").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("fileUrl").ValueKind);
        // A practice mark stays off the score sheet.
        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/assignments/{id}/submissions/{s.Tolu.EnrolmentId}", new { mark = 9m, feedback = "Lovely." }, Ct));
        Assert.Equal(0, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Parent_cannot_send_work_for_a_child_who_signs_in()
    {
        var s = await SetupAsync("Homework Login School", studentLogins: true);
        var id = await CreateAsync(s, "Essay", null, null, DateTimeOffset.UtcNow.AddDays(1));
        var view = Assert.Single((await Json(await s.Ada.Parent.GetAsync($"/api/parent/children/{s.Ada.StudentId}/assignments", Ct))).EnumerateArray());
        Assert.False(view.GetProperty("canSubmit").GetBoolean());
        var refused = await ProblemAsync(await s.Ada.Parent.PostAsync($"/api/parent/children/{s.Ada.StudentId}/assignments/{id}/submit", Work("Parent's try.", false), Ct), HttpStatusCode.Forbidden);
        Assert.Equal("Your child sends this work from their own student account.", refused.GetProperty("detail").GetString());
        Assert.Equal(0, await api.WithDbAsync(db => db.AssignmentSubmissions.IgnoreQueryFilters().CountAsync(x => x.AssignmentId == id, Ct)));
    }

    [Fact]
    public async Task Another_schools_assignment_is_unknown()
    {
        var a = await SetupAsync("Homework Tenant A", studentLogins: false);
        var b = await SetupAsync("Homework Tenant B", studentLogins: false);
        var id = await CreateAsync(a, "A's homework", null, 10m, DateTimeOffset.UtcNow.AddDays(1));

        await ProblemAsync(await b.Teacher.GetAsync($"/api/school/assignments/{id}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await b.School.Owner.GetAsync($"/api/school/assignments/{id}", Ct), HttpStatusCode.NotFound);
        // B's parent, with B's own child, cannot send work to A's assignment.
        await ProblemAsync(await b.Ada.Parent.PostAsync($"/api/parent/children/{b.Ada.StudentId}/assignments/{id}/submit", Work("Wrong school.", false), Ct), HttpStatusCode.NotFound);
        // Another teacher of A who does not teach the subject gets 404; the bursar is refused.
        var (other, _) = await kit.StaffAsync(a.School, MemberRole.Staff, "Other teacher");
        await ProblemAsync(await other.GetAsync($"/api/school/assignments/{id}", Ct), HttpStatusCode.NotFound);
        var (bursar, _) = await kit.StaffAsync(a.School, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/assignments/{id}", Ct)).StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.AssignmentSubmissions.IgnoreQueryFilters().CountAsync(x => x.AssignmentId == id, Ct)));
    }
}
