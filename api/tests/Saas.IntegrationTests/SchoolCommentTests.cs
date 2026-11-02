using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Subject comments are optional, picked from a comment bank, and frozen once the class is published.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolCommentTests(ApiFixture api)
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

    private sealed record Setup(
        SchoolKit.School School, Guid ClassId, Guid TermId, Guid MathId, Guid[] Components,
        HttpClient SubjectTeacher, HttpClient OtherTeacher, HttpClient ClassTeacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var (subjectTeacher, subjectUser) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var (otherTeacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Other teacher");
        var (classTeacher, classUser) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        var classId = await kit.ClassAsync(school);
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = await StaffIdAsync(school, classUser) }, Ct)).EnsureSuccessStatusCode();
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = await StaffIdAsync(school, subjectUser) }, Ct));
        var mathId = link.GetProperty("id").GetGuid();
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={mathId}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        return new Setup(school, classId, termId, mathId, components, subjectTeacher, otherTeacher, classTeacher);
    }

    private static string ResultsUrl(Setup s) => $"/api/school/results/{s.ClassId}/{s.TermId}";

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>Mathematics 90, which is A1 on the default scale.</summary>
    private async Task<Guid> ScoreA1Async(Setup s)
    {
        var enrolmentId = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed"));
        var entries = new[] { (s.Components[0], 20m), (s.Components[1], 20m), (s.Components[2], 50m) }
            .Select(c => new { enrolmentId, componentId = c.Item1, value = c.Item2 }).ToArray();
        (await s.School.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = s.MathId, termId = s.TermId, entries }, Ct)).EnsureSuccessStatusCode();
        return enrolmentId;
    }

    private static JsonElement StudentRow(JsonElement results, string firstName) =>
        results.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    private static JsonElement SubjectRow(JsonElement student, string subject) =>
        student.GetProperty("subjects").EnumerateArray().Single(x => x.GetProperty("subject").GetString() == subject);

    private static string SuggestionsUrl(Setup s, Guid enrolmentId, Guid? classSubjectId = null)
    {
        var url = $"{ResultsUrl(s)}/suggestions?enrolmentId={enrolmentId}";
        return classSubjectId is { } id ? $"{url}&classSubjectId={id}" : url;
    }

    private static string SubjectCommentsUrl(Setup s) => $"{ResultsUrl(s)}/subject-comments";

    [Fact]
    public async Task Comment_bank_seeds_grade_lines_and_leadership_replaces_it()
    {
        var school = await kit.SchoolAsync("Comment Bank School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Reader");

        var bank = await Json(await teacher.GetAsync("/api/school/comment-bank", Ct));
        var entries = bank.GetProperty("entries").EnumerateArray().ToList();
        var a1 = entries.Single(e => e.GetProperty("scope").GetString() == "Subject" && e.GetProperty("matchKey").GetString() == "A1");
        Assert.Equal("Excellent", a1.GetProperty("text").GetString());
        Assert.Contains(entries, e => e.GetProperty("scope").GetString() == "TeacherOverall");
        Assert.Contains(entries, e => e.GetProperty("scope").GetString() == "PrincipalOverall");

        await ProblemAsync(await teacher.PutAsJsonAsync("/api/school/comment-bank", new
        {
            entries = new[] { new { scope = "Subject", matchKey = "A1", text = "Changed by a teacher.", order = 0 } },
        }, Ct), HttpStatusCode.Forbidden);

        var saved = await Json(await school.Owner.PutAsJsonAsync("/api/school/comment-bank", new
        {
            entries = new object[]
            {
                new { scope = "Subject", matchKey = "A1", text = "Outstanding work in this subject.", order = 0 },
                new { scope = "TeacherOverall", matchKey = "A1", text = "A strong term. Keep working with the same care.", order = 0 },
                new { scope = "PrincipalOverall", matchKey = "A1", text = "The school is pleased with this term's work.", order = 0 },
            },
        }, Ct));
        var after = saved.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("text").GetString()).ToList();
        Assert.Equal(3, after.Count);
        Assert.Contains("Outstanding work in this subject.", after);
        Assert.DoesNotContain("Excellent", after);

        var again = await Json(await teacher.GetAsync("/api/school/comment-bank", Ct));
        Assert.Equal(3, again.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Suggestion_matches_grade()
    {
        var s = await SetupAsync("Suggestion School");
        var ada = await ScoreA1Async(s);

        var suggestions = await Json(await s.SubjectTeacher.GetAsync(SuggestionsUrl(s, ada, s.MathId), Ct));
        var subject = suggestions.GetProperty("subject").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Contains(subject, text => text.Contains("Excellent", StringComparison.Ordinal));
        Assert.True(suggestions.TryGetProperty("teacherOverall", out _));
        Assert.True(suggestions.TryGetProperty("principalOverall", out _));
    }

    [Fact]
    public async Task Teacher_picks_a_suggestion()
    {
        var s = await SetupAsync("Pick Comment School");
        var ada = await ScoreA1Async(s);
        var suggestions = await Json(await s.SubjectTeacher.GetAsync(SuggestionsUrl(s, ada, s.MathId), Ct));
        var text = suggestions.GetProperty("subject").EnumerateArray().Select(x => x.GetString()!).First(x => x.Contains("Excellent", StringComparison.Ordinal));

        await ProblemAsync(await s.OtherTeacher.PutAsJsonAsync(SubjectCommentsUrl(s), new { enrolmentId = ada, classSubjectId = s.MathId, comment = text }, Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await s.ClassTeacher.PutAsJsonAsync(SubjectCommentsUrl(s), new { enrolmentId = ada, classSubjectId = s.MathId, comment = text }, Ct), HttpStatusCode.NotFound);

        (await s.SubjectTeacher.PutAsJsonAsync(SubjectCommentsUrl(s), new { enrolmentId = ada, classSubjectId = s.MathId, comment = text }, Ct)).EnsureSuccessStatusCode();

        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var math = SubjectRow(StudentRow(results, "Ada"), "Mathematics");
        Assert.Equal(text, math.GetProperty("remark").GetString());
        Assert.Equal("A1", math.GetProperty("grade").GetString());

        // A later recompute keeps the comment the teacher picked and still does not copy the band word over it.
        var lower = new[] { (s.Components[0], 10m), (s.Components[1], 10m), (s.Components[2], 40m) }
            .Select(c => new { enrolmentId = ada, componentId = c.Item1, value = c.Item2 }).ToArray();
        (await s.School.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = s.MathId, termId = s.TermId, entries = lower }, Ct)).EnsureSuccessStatusCode();
        var again = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var after = SubjectRow(StudentRow(again, "Ada"), "Mathematics");
        Assert.Equal(text, after.GetProperty("remark").GetString());
        Assert.Equal("C4", after.GetProperty("grade").GetString());
    }

    [Fact]
    public async Task Published_subject_comment_is_frozen()
    {
        var s = await SetupAsync("Frozen Comment School");
        var ada = await ScoreA1Async(s);
        var url = SubjectCommentsUrl(s);
        (await s.School.Owner.PutAsJsonAsync(url, new { enrolmentId = ada, classSubjectId = s.MathId, comment = "Excellent" }, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();

        var frozen = await ProblemAsync(await s.SubjectTeacher.PutAsJsonAsync(url, new { enrolmentId = ada, classSubjectId = s.MathId, comment = "A later edit." }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("Unpublish the results before changing remarks.", frozen.GetProperty("detail").GetString());

        var still = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal("Excellent", SubjectRow(StudentRow(still, "Ada"), "Mathematics").GetProperty("remark").GetString());

        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        (await s.SubjectTeacher.PutAsJsonAsync(url, new { enrolmentId = ada, classSubjectId = s.MathId, comment = "A later edit." }, Ct)).EnsureSuccessStatusCode();
        var open = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal("A later edit.", SubjectRow(StudentRow(open, "Ada"), "Mathematics").GetProperty("remark").GetString());
    }
}
