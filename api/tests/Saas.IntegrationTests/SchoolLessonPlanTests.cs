using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Weekly lesson plans. A draft is visible only to the person who wrote it. Submit asks leadership to review, unless the school
/// has turned review off, in which case submit accepts the plan at once. The bursar is refused before any plan is looked up.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolLessonPlanTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private static readonly DateOnly LessonDate = new(2026, 10, 19);

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

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid ClassSubjectId, Guid TermId, HttpClient Teacher, Guid TeacherId);

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
        return new Setup(school, classId, link.GetProperty("id").GetGuid(), termId, teacher, userId);
    }

    private static object Plan(Setup s, string topic = "Fractions of a whole", Guid? assignmentId = null) => new
    {
        classSubjectId = s.ClassSubjectId,
        termId = s.TermId,
        week = 3,
        date = LessonDate,
        topic,
        objectives = "Learners can name a half and a quarter.",
        materials = "Paper circles, scissors.",
        methods = "Demonstration, then pair work.",
        activities = "Fold a circle into halves and quarters.",
        previousKnowledge = "Learners can count to 20.",
        introduction = "Show a whole orange.",
        presentation = "Fold and name the parts.",
        conclusion = "Learners say what a quarter is.",
        evaluation = "Ask three learners to fold a quarter.",
        assignmentId,
        teacherRemarks = "Watch the pairs who rush.",
    };

    private static string ListUrl(Setup s) => $"/api/school/lesson-plans?classSubjectId={s.ClassSubjectId}&termId={s.TermId}";

    private Task<int> SlotCountAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.TimetableSlots.IgnoreQueryFilters().CountAsync(t => t.TenantId == tenantId, Ct));

    private Task<bool> NeedsReviewAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).Select(p => p.LessonPlansNeedReview).SingleAsync(Ct));

    private Task<List<UserNotification>> NotesAsync(Guid userId) =>
        api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == userId && n.Kind == "school.lesson_plan_submitted").ToListAsync(Ct));

    [Fact]
    public async Task Draft_is_hidden_from_principal()
    {
        var s = await SetupAsync("Draft Lesson Plan School");
        var (principal, _) = await kit.StaffAsync(s.School, MemberRole.Admin, "Principal");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var slots = await SlotCountAsync(s.School.TenantId);
        Assert.True(await NeedsReviewAsync(s.School.TenantId));

        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s), Ct));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.Equal(s.TeacherId, created.GetProperty("createdBy").GetGuid());
        Assert.Equal(3, created.GetProperty("week").GetInt32());
        Assert.Equal("2026-10-19", created.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("assignmentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("submittedAt").ValueKind);
        Assert.Equal(slots, await SlotCountAsync(s.School.TenantId));

        var hidden = await ProblemAsync(await principal.GetAsync($"/api/school/lesson-plans/{id}", Ct), HttpStatusCode.NotFound);
        Assert.Equal("This lesson plan was not found.", hidden.GetProperty("detail").GetString());
        Assert.Empty((await Json(await principal.GetAsync(ListUrl(s), Ct))).EnumerateArray());

        var mine = await Json(await s.Teacher.GetAsync($"/api/school/lesson-plans/{id}", Ct));
        Assert.Equal("Fractions of a whole", mine.GetProperty("topic").GetString());
        Assert.Equal(id, Assert.Single((await Json(await s.Teacher.GetAsync(ListUrl(s), Ct))).EnumerateArray()).GetProperty("id").GetGuid());

        var ownerId = await kit.OwnerIdAsync(s.School);
        var owners = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/lesson-plans", Plan(s, "Proprietor draft"), Ct));
        Assert.Equal("Draft", owners.GetProperty("status").GetString());
        Assert.Equal(ownerId, owners.GetProperty("createdBy").GetGuid());
        var ownerDraft = await ProblemAsync(await principal.GetAsync($"/api/school/lesson-plans/{owners.GetProperty("id").GetGuid()}", Ct), HttpStatusCode.NotFound);
        Assert.Equal("This lesson plan was not found.", ownerDraft.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/lesson-plans/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/lesson-plans/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync(ListUrl(s), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/lesson-plans", Plan(s, "Bursar plan"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/lesson-plans/{Guid.NewGuid()}/submit", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Submit_then_accept()
    {
        var s = await SetupAsync("Review Lesson Plan School");
        var (principal, principalId) = await kit.StaffAsync(s.School, MemberRole.Admin, "Principal");
        var draft = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s, "Still a draft"), Ct));
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s), Ct));
        var id = created.GetProperty("id").GetGuid();

        var submitted = await Json(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct));
        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, submitted.GetProperty("submittedAt").ValueKind);

        var cannotEdit = await ProblemAsync(await s.Teacher.PutAsJsonAsync($"/api/school/lesson-plans/{id}", Plan(s, "Changed too late"), Ct), HttpStatusCode.Conflict);
        Assert.Contains("returned", cannotEdit.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        var seen = await Json(await principal.GetAsync($"/api/school/lesson-plans/{id}", Ct));
        Assert.Equal("Submitted", seen.GetProperty("status").GetString());
        var listed = (await Json(await principal.GetAsync(ListUrl(s), Ct))).EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(id, listed);
        Assert.DoesNotContain(draft.GetProperty("id").GetGuid(), listed);

        var teacherAccept = await ProblemAsync(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/accept", null, Ct), HttpStatusCode.Forbidden);
        Assert.Contains("principal", teacherAccept.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        var accepted = await Json(await principal.PostAsync($"/api/school/lesson-plans/{id}/accept", null, Ct));
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.Equal(principalId, accepted.GetProperty("reviewedBy").GetGuid());
        var after = await ProblemAsync(await s.Teacher.PutAsJsonAsync($"/api/school/lesson-plans/{id}", Plan(s, "After accept"), Ct), HttpStatusCode.Conflict);
        Assert.Contains("accepted", after.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        var ownerId = await kit.OwnerIdAsync(s.School);
        var ownerNotes = await NotesAsync(ownerId);
        var principalNotes = await NotesAsync(principalId);
        Assert.Single(ownerNotes);
        Assert.Single(principalNotes);
        Assert.Empty(await NotesAsync(s.TeacherId));
        Assert.Equal("school", ownerNotes[0].Product);
        Assert.Contains(id.ToString(), ownerNotes[0].Link);
        Assert.Contains("Fractions of a whole", ownerNotes[0].Body);
        Assert.DoesNotContain('!', ownerNotes[0].Title + ownerNotes[0].Body);
        Assert.Equal(ownerNotes[0].Title, principalNotes[0].Title);
    }

    [Fact]
    public async Task Need_review_false_submit_is_accepted()
    {
        var s = await SetupAsync("No Review Lesson Plan School");
        var (principal, principalId) = await kit.StaffAsync(s.School, MemberRole.Admin, "Principal");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        Assert.True(await NeedsReviewAsync(s.School.TenantId));

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Teacher.PutAsJsonAsync("/api/school/settings/lesson-plans", new { needReview = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PutAsJsonAsync("/api/school/settings/lesson-plans", new { needReview = false }, Ct)).StatusCode);

        var saved = await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/lesson-plans", new { needReview = false }, Ct));
        Assert.False(saved.GetProperty("needReview").GetBoolean());
        Assert.False(await NeedsReviewAsync(s.School.TenantId));

        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s), Ct));
        var id = created.GetProperty("id").GetGuid();
        var submitted = await Json(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct));
        Assert.Equal("Accepted", submitted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("reviewedBy").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, submitted.GetProperty("submittedAt").ValueKind);

        var seen = await Json(await principal.GetAsync($"/api/school/lesson-plans/{id}", Ct));
        Assert.Equal("Accepted", seen.GetProperty("status").GetString());
        Assert.Empty(await NotesAsync(await kit.OwnerIdAsync(s.School)));
        Assert.Empty(await NotesAsync(principalId));
    }

    [Fact]
    public async Task Teacher_cannot_open_another_teachers_draft()
    {
        var s = await SetupAsync("Other Teacher Lesson Plan School");
        var (other, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s), Ct));
        var id = created.GetProperty("id").GetGuid();

        var hidden = await ProblemAsync(await other.GetAsync($"/api/school/lesson-plans/{id}", Ct), HttpStatusCode.NotFound);
        Assert.Equal("This lesson plan was not found.", hidden.GetProperty("detail").GetString());
        var edit = await ProblemAsync(await other.PutAsJsonAsync($"/api/school/lesson-plans/{id}", Plan(s, "Not yours"), Ct), HttpStatusCode.NotFound);
        Assert.Equal("This lesson plan was not found.", edit.GetProperty("detail").GetString());
        var submit = await ProblemAsync(await other.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal("This lesson plan was not found.", submit.GetProperty("detail").GetString());
        var list = await ProblemAsync(await other.GetAsync(ListUrl(s), Ct), HttpStatusCode.NotFound);
        Assert.Equal("Class subject was not found.", list.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Return_requires_a_note()
    {
        var s = await SetupAsync("Return Lesson Plan School");
        var (principal, principalId) = await kit.StaffAsync(s.School, MemberRole.Admin, "Principal");
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s), Ct));
        var id = created.GetProperty("id").GetGuid();
        await Json(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct));

        var missing = await ProblemAsync(await principal.PostAsJsonAsync($"/api/school/lesson-plans/{id}/return", new { note = "  " }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("note", missing.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Submitted", (await Json(await s.Teacher.GetAsync($"/api/school/lesson-plans/{id}", Ct))).GetProperty("status").GetString());

        var returned = await Json(await principal.PostAsJsonAsync($"/api/school/lesson-plans/{id}/return", new { note = "Add a starter question." }, Ct));
        Assert.Equal("Returned", returned.GetProperty("status").GetString());
        Assert.Equal("Add a starter question.", returned.GetProperty("reviewerNote").GetString());
        Assert.Equal(principalId, returned.GetProperty("reviewedBy").GetGuid());

        var edited = await Json(await s.Teacher.PutAsJsonAsync($"/api/school/lesson-plans/{id}", Plan(s, "Fractions with a starter"), Ct));
        Assert.Equal("Returned", edited.GetProperty("status").GetString());
        Assert.Equal("Fractions with a starter", edited.GetProperty("topic").GetString());

        var again = await Json(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct));
        Assert.Equal("Submitted", again.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, again.GetProperty("reviewerNote").ValueKind);
    }

    [Fact]
    public async Task Assignment_must_match_class_subject_and_term()
    {
        var s = await SetupAsync("Linked Lesson Plan School");
        var homework = await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, title = "Fold a quarter", instructions = "Bring the folded circle.",
            dueAt = DateTimeOffset.UtcNow.AddDays(2),
        }, Ct));
        var assignmentId = homework.GetProperty("id").GetGuid();
        var linked = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s, assignmentId: assignmentId), Ct));
        Assert.Equal(assignmentId, linked.GetProperty("assignmentId").GetGuid());

        var missing = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s, assignmentId: Guid.NewGuid()), Ct), HttpStatusCode.NotFound);
        Assert.Equal("This assignment was not found.", missing.GetProperty("detail").GetString());

        var english = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "English", code = "ENG" }, Ct));
        var other = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/classes/{s.ClassId}/subjects", new { subjectId = english.GetProperty("id").GetGuid() }, Ct));
        var otherHomework = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId = other.GetProperty("id").GetGuid(), termId = s.TermId, title = "Read a page", instructions = "Read aloud.",
            dueAt = DateTimeOffset.UtcNow.AddDays(2),
        }, Ct));
        var mismatch = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", Plan(s, assignmentId: otherHomework.GetProperty("id").GetGuid()), Ct), HttpStatusCode.BadRequest);
        Assert.Contains("assignment", mismatch.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Review_setting_reads_back_the_saved_value()
    {
        var s = await SetupAsync("Review Setting Read School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        Assert.True((await Json(await s.School.Owner.GetAsync("/api/school/settings/lesson-plans", Ct))).GetProperty("needReview").GetBoolean());

        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/lesson-plans", new { needReview = false }, Ct));
        Assert.False((await Json(await s.School.Owner.GetAsync("/api/school/settings/lesson-plans", Ct))).GetProperty("needReview").GetBoolean());
        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/lesson-plans", new { needReview = true }, Ct));
        Assert.True((await Json(await s.School.Owner.GetAsync("/api/school/settings/lesson-plans", Ct))).GetProperty("needReview").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Teacher.GetAsync("/api/school/settings/lesson-plans", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/school/settings/lesson-plans", Ct)).StatusCode);
    }
}
