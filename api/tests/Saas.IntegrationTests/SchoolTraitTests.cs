using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Psychomotor and affective ratings sit beside exam scores. They never become marks.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolTraitTests(ApiFixture api)
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
        HttpClient SubjectTeacher, HttpClient ClassTeacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var (subjectTeacher, subjectUser) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
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
        return new Setup(school, classId, termId, mathId, components, subjectTeacher, classTeacher);
    }

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    private static string TraitsUrl(Guid studentId, Guid termId, string kind) =>
        $"/api/school/students/{studentId}/traits?termId={termId}&kind={kind}";

    private static List<string> Names(JsonElement criteria) =>
        criteria.GetProperty("criteria").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToList();

    private static int? Rating(JsonElement body, string name)
    {
        var row = body.GetProperty("ratings").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        return row.GetProperty("value").ValueKind == JsonValueKind.Null ? null : row.GetProperty("value").GetInt32();
    }

    private static JsonElement StudentRow(JsonElement results, string firstName) =>
        results.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    [Fact]
    public async Task Template_seeds_psychomotor_and_affective_criteria()
    {
        var school = await kit.SchoolAsync("Trait Template School");
        (await school.Owner.PostAsJsonAsync("/api/school/setup/apply-template", new { levels = true, subjects = true }, Ct)).EnsureSuccessStatusCode();

        var psychomotor = await Json(await school.Owner.GetAsync("/api/school/traits?kind=Psychomotor", Ct));
        var affective = await Json(await school.Owner.GetAsync("/api/school/traits?kind=Affective", Ct));
        Assert.Equal(JsonValueKind.Null, psychomotor.GetProperty("levelId").ValueKind);
        Assert.Equal("Psychomotor", psychomotor.GetProperty("kind").GetString());
        Assert.Equal("Affective", affective.GetProperty("kind").GetString());
        Assert.Equal(
            ["Handwriting", "Drawing", "Hand coordination", "Use of tools", "Physical coordination", "Practical tasks"],
            Names(psychomotor));
        Assert.Equal(
            ["Appearance / neatness", "Punctuality", "Honesty", "Cooperation", "Conduct in class"],
            Names(affective));

        var again = await Json(await school.Owner.GetAsync("/api/school/traits?kind=Psychomotor", Ct));
        Assert.Equal(
            psychomotor.GetProperty("criteria").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList(),
            again.GetProperty("criteria").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList());
    }

    [Fact]
    public async Task Level_override_is_used_for_that_level_only()
    {
        var school = await kit.SchoolAsync("Trait Override School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Reader");
        var classId = await kit.ClassAsync(school);
        var classes = await Json(await school.Owner.GetAsync("/api/school/classes", Ct));
        var levelId = classes.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == classId).GetProperty("levelId").GetGuid();

        var defaults = await Json(await school.Owner.GetAsync("/api/school/traits?kind=Affective", Ct));
        Assert.Equal(5, defaults.GetProperty("criteria").GetArrayLength());

        await ProblemAsync(await teacher.PutAsJsonAsync($"/api/school/traits?kind=Affective&levelId={levelId}", new
        {
            criteria = new[] { new { name = "Appearance / neatness" } },
        }, Ct), HttpStatusCode.Forbidden);

        var saved = await Json(await school.Owner.PutAsJsonAsync($"/api/school/traits?kind=Affective&levelId={levelId}", new
        {
            criteria = new[] { new { name = "Appearance / neatness" }, new { name = "Honesty" } },
        }, Ct));
        Assert.Equal(levelId, saved.GetProperty("levelId").GetGuid());
        Assert.Equal(["Appearance / neatness", "Honesty"], Names(saved));

        var level = await Json(await school.Owner.GetAsync($"/api/school/traits?kind=Affective&levelId={levelId}", Ct));
        Assert.Equal(["Appearance / neatness", "Honesty"], Names(level));

        var schoolDefault = await Json(await school.Owner.GetAsync("/api/school/traits?kind=Affective", Ct));
        Assert.Equal(
            ["Appearance / neatness", "Punctuality", "Honesty", "Cooperation", "Conduct in class"],
            Names(schoolDefault));

        var fallback = await Json(await school.Owner.GetAsync($"/api/school/traits?kind=Psychomotor&levelId={levelId}", Ct));
        Assert.Equal(JsonValueKind.Null, fallback.GetProperty("levelId").ValueKind);
        Assert.Equal(
            ["Handwriting", "Drawing", "Hand coordination", "Use of tools", "Physical coordination", "Practical tasks"],
            Names(fallback));
    }

    [Fact]
    public async Task Class_teacher_rates_appearance()
    {
        var s = await SetupAsync("Appearance School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed");
        var criteria = await Json(await s.ClassTeacher.GetAsync("/api/school/traits?kind=Affective", Ct));
        var appearance = criteria.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Appearance / neatness");
        var url = TraitsUrl(studentId, s.TermId, "Affective");

        var saved = await Json(await s.ClassTeacher.PutAsJsonAsync(url, new { ratings = new[] { new { criterionId = appearance.GetProperty("id").GetGuid(), value = 4 } } }, Ct));
        Assert.Equal(4, Rating(saved, "Appearance / neatness"));
        Assert.Null(Rating(saved, "Punctuality"));

        var again = await Json(await s.ClassTeacher.GetAsync(url, Ct));
        Assert.Equal(4, Rating(again, "Appearance / neatness"));
    }

    [Fact]
    public async Task Subject_teacher_cannot_rate_traits()
    {
        var s = await SetupAsync("Subject Trait School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed");
        var criteria = await Json(await s.School.Owner.GetAsync("/api/school/traits?kind=Affective", Ct));
        var criterionId = criteria.GetProperty("criteria").EnumerateArray().First().GetProperty("id").GetGuid();
        var url = TraitsUrl(studentId, s.TermId, "Affective");
        var body = new { ratings = new[] { new { criterionId, value = 3 } } };

        var get = await ProblemAsync(await s.SubjectTeacher.GetAsync(url, Ct), HttpStatusCode.NotFound);
        Assert.Equal("Student was not found.", get.GetProperty("detail").GetString());
        var put = await ProblemAsync(await s.SubjectTeacher.PutAsJsonAsync(url, body, Ct), HttpStatusCode.NotFound);
        Assert.Equal("Student was not found.", put.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Trait_value_6_is_400()
    {
        var s = await SetupAsync("Trait Range School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed");
        var criteria = await Json(await s.ClassTeacher.GetAsync("/api/school/traits?kind=Affective", Ct));
        var criterionId = criteria.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Appearance / neatness").GetProperty("id").GetGuid();

        var problem = await ProblemAsync(
            await s.ClassTeacher.PutAsJsonAsync(TraitsUrl(studentId, s.TermId, "Affective"), new { ratings = new[] { new { criterionId, value = 6 } } }, Ct),
            HttpStatusCode.BadRequest);
        Assert.Equal("Rate each item from 1 to 5.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Traits_are_not_added_into_the_exam_total()
    {
        var s = await SetupAsync("Trait Total School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(s.School, studentId);
        var entries = new[] { (s.Components[0], 20m), (s.Components[1], 20m), (s.Components[2], 50m) }
            .Select(c => new { enrolmentId, componentId = c.Item1, value = c.Item2 }).ToArray();
        (await s.School.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId = s.MathId, termId = s.TermId, entries }, Ct)).EnsureSuccessStatusCode();

        var resultsUrl = $"/api/school/results/{s.ClassId}/{s.TermId}";
        var before = await Json(await s.School.Owner.GetAsync(resultsUrl, Ct));
        var total = StudentRow(before, "Ada").GetProperty("total").GetDecimal();
        var subjectTotal = StudentRow(before, "Ada").GetProperty("subjects").EnumerateArray().Single().GetProperty("total").GetDecimal();
        Assert.Equal(90m, total);
        Assert.Equal(90m, subjectTotal);
        var stored = await StoredAsync(s, enrolmentId);

        var criteria = await Json(await s.ClassTeacher.GetAsync("/api/school/traits?kind=Psychomotor", Ct));
        var ratings = criteria.GetProperty("criteria").EnumerateArray()
            .Select(c => new { criterionId = c.GetProperty("id").GetGuid(), value = 5 }).ToArray();
        (await s.ClassTeacher.PutAsJsonAsync(TraitsUrl(studentId, s.TermId, "Psychomotor"), new { ratings }, Ct)).EnsureSuccessStatusCode();

        var afterSave = await StoredAsync(s, enrolmentId);
        Assert.Equal(stored.Scores, afterSave.Scores);
        Assert.Equal(stored.ScoreRows, afterSave.ScoreRows);
        Assert.Equal(stored.TermTotal, afterSave.TermTotal);

        (await s.School.Owner.PostAsync($"{resultsUrl}/compute", null, Ct)).EnsureSuccessStatusCode();
        var after = await Json(await s.School.Owner.GetAsync(resultsUrl, Ct));
        Assert.Equal(total, StudentRow(after, "Ada").GetProperty("total").GetDecimal());
        Assert.Equal(subjectTotal, StudentRow(after, "Ada").GetProperty("subjects").EnumerateArray().Single().GetProperty("total").GetDecimal());

        (await s.School.Owner.PostAsync($"{resultsUrl}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{resultsUrl}/publish", null, Ct)).EnsureSuccessStatusCode();
        var changed = ratings.Select(r => new { r.criterionId, value = 1 }).ToArray();
        var edited = await Json(await s.ClassTeacher.PutAsJsonAsync(TraitsUrl(studentId, s.TermId, "Psychomotor"), new { ratings = changed }, Ct));
        Assert.All(edited.GetProperty("ratings").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("value").GetInt32()));

        var published = await Json(await s.School.Owner.GetAsync(resultsUrl, Ct));
        Assert.Equal(total, StudentRow(published, "Ada").GetProperty("total").GetDecimal());
        var still = await StoredAsync(s, enrolmentId);
        Assert.Equal(stored.Scores, still.Scores);
        Assert.Equal(stored.TermTotal, still.TermTotal);
    }

    private async Task<(decimal Scores, int ScoreRows, decimal? TermTotal)> StoredAsync(Setup s, Guid enrolmentId) =>
        await api.WithDbAsync(async db =>
        {
            var scores = await db.Scores.IgnoreQueryFilters()
                .Where(x => x.TenantId == s.School.TenantId && x.EnrolmentId == enrolmentId && x.TermId == s.TermId)
                .Select(x => x.Value).ToListAsync(Ct);
            var termTotal = await db.TermResults.IgnoreQueryFilters()
                .Where(x => x.TenantId == s.School.TenantId && x.EnrolmentId == enrolmentId && x.TermId == s.TermId)
                .Select(x => (decimal?)x.Total).FirstOrDefaultAsync(Ct);
            return (scores.Sum(), scores.Count, termTotal);
        });
}
