using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Nursery and creche use developmental ratings. They are not exam marks and do not produce a numeric total.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolDevelopmentalTests(ApiFixture api)
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

    private sealed record Nursery(SchoolKit.School School, Guid ClassId, Guid LevelId, Guid TermId, HttpClient ClassTeacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>A Nursery stage level with no assessmentMode, then a class. The level is developmental.</summary>
    private async Task<Nursery> NurseryAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        Assert.Equal("Developmental", level.GetProperty("assessmentMode").GetString());
        var levelId = level.GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var (classTeacher, classUser) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = await StaffIdAsync(school, classUser) }, Ct)).EnsureSuccessStatusCode();
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        return new Nursery(school, classId, levelId, termId, classTeacher);
    }

    private static JsonElement StudentRow(JsonElement results, string firstName) =>
        results.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    [Fact]
    public async Task Nursery_card_has_no_numeric_total()
    {
        var n = await NurseryAsync("Nursery Card School");
        var studentId = await kit.StudentAsync(n.School, n.ClassId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(n.School, studentId);

        var areas = await Json(await n.ClassTeacher.GetAsync("/api/school/developmental/areas", Ct));
        Assert.Equal(JsonValueKind.Null, areas.GetProperty("levelId").ValueKind);
        Assert.Equal(
            ["Language", "Early numeracy", "Social", "Self-care", "Creativity", "Motor", "Participation", "Emotional"],
            areas.GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToList());
        var scale = await Json(await n.ClassTeacher.GetAsync("/api/school/developmental/scale", Ct));
        Assert.Equal(
            ["Excellent", "Very Good", "Good", "Developing", "Needs Support"],
            scale.GetProperty("scale").EnumerateArray().Select(s => s.GetProperty("label").GetString()).ToList());
        var excellent = scale.GetProperty("scale").EnumerateArray().Single(s => s.GetProperty("label").GetString() == "Excellent");

        var ratings = areas.GetProperty("areas").EnumerateArray().Select(a => new
        {
            areaId = a.GetProperty("id").GetGuid(),
            scaleId = excellent.GetProperty("id").GetGuid(),
            comment = a.GetProperty("name").GetString() == "Language" ? "Speaks in short sentences." : null,
        }).ToArray();
        var saved = await Json(await n.ClassTeacher.PutAsJsonAsync(
            $"/api/school/students/{studentId}/developmental?termId={n.TermId}", new { ratings }, Ct));
        Assert.Equal("Excellent", saved.GetProperty("ratings").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Language").GetProperty("scale").GetString());
        Assert.Equal("Speaks in short sentences.", saved.GetProperty("ratings").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Language").GetProperty("comment").GetString());

        var resultsUrl = $"/api/school/results/{n.ClassId}/{n.TermId}";
        (await n.School.Owner.PostAsync($"{resultsUrl}/compute", null, Ct)).EnsureSuccessStatusCode();
        var results = await Json(await n.School.Owner.GetAsync(resultsUrl, Ct));
        var ada = StudentRow(results, "Ada");
        Assert.Equal(0m, ada.GetProperty("total").GetDecimal());
        Assert.Equal(0, ada.GetProperty("position").GetInt32());
        Assert.False(ada.TryGetProperty("grade", out _));
        Assert.Equal(0, ada.GetProperty("subjects").GetArrayLength());
        Assert.Equal(8, ada.GetProperty("developmental").GetArrayLength());
        Assert.Equal("Excellent", ada.GetProperty("developmental").EnumerateArray().Single(d => d.GetProperty("area").GetString() == "Language").GetProperty("scale").GetString());

        var bank = await Json(await n.ClassTeacher.GetAsync("/api/school/comment-bank", Ct));
        var excellentLine = bank.GetProperty("entries").EnumerateArray().Single(e =>
            e.GetProperty("scope").GetString() == "Developmental" && e.GetProperty("matchKey").GetString() == "Excellent");
        var suggestions = await Json(await n.ClassTeacher.GetAsync(
            $"{resultsUrl}/suggestions?enrolmentId={enrolmentId}", Ct));
        Assert.Equal(0, suggestions.GetProperty("subject").GetArrayLength());
        Assert.Contains(excellentLine.GetProperty("text").GetString(), suggestions.GetProperty("teacherOverall").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Numeric_scores_refused_on_nursery()
    {
        var n = await NurseryAsync("Nursery Scores School");
        var studentId = await kit.StudentAsync(n.School, n.ClassId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(n.School, studentId);
        var subject = await Json(await n.School.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Play", code = "PLY" }, Ct));
        var link = await Json(await n.School.Owner.PostAsJsonAsync($"/api/school/classes/{n.ClassId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid() }, Ct));
        var classSubjectId = link.GetProperty("id").GetGuid();
        var grid = await Json(await n.School.Owner.GetAsync($"/api/school/scores?classSubjectId={classSubjectId}&termId={n.TermId}", Ct));
        var componentId = grid.GetProperty("components").EnumerateArray().First().GetProperty("id").GetGuid();

        var problem = await ProblemAsync(await n.School.Owner.PutAsJsonAsync("/api/school/scores", new
        {
            classSubjectId,
            termId = n.TermId,
            entries = new[] { new { enrolmentId, componentId, value = 10m } },
        }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal("This class uses developmental assessment.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Developmental_put_refused_on_jss()
    {
        var school = await kit.SchoolAsync("Jss Developmental School");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

        var problem = await ProblemAsync(await school.Owner.PutAsJsonAsync(
            $"/api/school/students/{studentId}/developmental?termId={termId}",
            new { ratings = new[] { new { areaId = Guid.NewGuid(), scaleId = Guid.NewGuid() } } }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal("This class uses numeric assessment.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Suggestions_stay_inside_the_class()
    {
        var school = await kit.SchoolAsync("Nursery Suggestion Scope");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        var levelId = level.GetProperty("id").GetGuid();
        var classA = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var classB = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "B" }, Ct))).GetProperty("id").GetGuid();
        var (teacherA, userA) = await kit.StaffAsync(school, MemberRole.Staff, "Class A teacher");
        var (teacherB, userB) = await kit.StaffAsync(school, MemberRole.Staff, "Class B teacher");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classA}/class-teacher", new { staffId = await StaffIdAsync(school, userA) }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classB}/class-teacher", new { staffId = await StaffIdAsync(school, userB) }, Ct)).EnsureSuccessStatusCode();
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

        var childA = await kit.StudentAsync(school, classA, "Ada", "Zed");
        var childB = await kit.StudentAsync(school, classB, "Bayo", "Yusuf");
        var enrolmentA = await EnrolmentAsync(school, childA);
        var enrolmentB = await EnrolmentAsync(school, childB);

        var areas = await Json(await teacherA.GetAsync("/api/school/developmental/areas", Ct));
        var scale = await Json(await teacherA.GetAsync("/api/school/developmental/scale", Ct));
        Guid Scale(string label) => scale.GetProperty("scale").EnumerateArray().Single(s => s.GetProperty("label").GetString() == label).GetProperty("id").GetGuid();
        object[] Rate(Guid scaleId) => areas.GetProperty("areas").EnumerateArray()
            .Select(a => new { areaId = a.GetProperty("id").GetGuid(), scaleId, comment = (string?)null }).ToArray();
        (await teacherA.PutAsJsonAsync($"/api/school/students/{childA}/developmental?termId={termId}", new { ratings = Rate(Scale("Excellent")) }, Ct)).EnsureSuccessStatusCode();
        (await teacherB.PutAsJsonAsync($"/api/school/students/{childB}/developmental?termId={termId}", new { ratings = Rate(Scale("Needs Support")) }, Ct)).EnsureSuccessStatusCode();

        var bank = await Json(await teacherA.GetAsync("/api/school/comment-bank", Ct));
        string Line(string key) => bank.GetProperty("entries").EnumerateArray().Single(e =>
            e.GetProperty("scope").GetString() == "Developmental" && e.GetProperty("matchKey").GetString() == key).GetProperty("text").GetString()!;

        var leaked = await ProblemAsync(
            await teacherA.GetAsync($"/api/school/results/{classA}/{termId}/suggestions?enrolmentId={enrolmentB}", Ct),
            HttpStatusCode.NotFound);
        Assert.Equal("Student was not found.", leaked.GetProperty("detail").GetString());

        var own = await Json(await teacherA.GetAsync($"/api/school/results/{classA}/{termId}/suggestions?enrolmentId={enrolmentA}", Ct));
        var overall = own.GetProperty("teacherOverall").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains(Line("Excellent"), overall);
        Assert.DoesNotContain(Line("Needs Support"), overall);
        Assert.Equal(0, own.GetProperty("subject").GetArrayLength());
    }
}
