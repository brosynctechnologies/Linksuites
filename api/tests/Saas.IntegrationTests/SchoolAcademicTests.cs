using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saas.Application.School;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink academic setup: levels, classes, subjects, the assessment scheme and grading bands.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolAcademicTests(ApiFixture api)
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
        Assert.Equal(expected, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static Task<HttpResponseMessage> ApplyAsync(SchoolKit.School s, bool levels, bool subjects) =>
        s.Owner.PostAsJsonAsync("/api/school/setup/apply-template", new { levels, subjects }, Ct);

    private static object[] Bands(params (decimal Min, decimal Max, string Grade)[] bands) =>
        bands.Select(b => (object)new { min = b.Min, max = b.Max, grade = b.Grade, remark = "Remark" }).ToArray();

    [Fact]
    public async Task Template_creates_levels_and_subjects()
    {
        var school = await kit.SchoolAsync("Template School");
        (await ApplyAsync(school, true, true)).EnsureSuccessStatusCode();

        var levels = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        Assert.Equal(15, levels.Count);
        Assert.Equal(["Creche", "Nursery 1", "Nursery 2", "Primary 1", "Primary 2", "Primary 3", "Primary 4", "Primary 5", "Primary 6",
            "JSS 1", "JSS 2", "JSS 3", "SSS 1", "SSS 2", "SSS 3"], levels.Select(l => l.GetProperty("name").GetString()));
        Assert.All(levels.Where(l => l.GetProperty("name").GetString()!.StartsWith("JSS") || l.GetProperty("name").GetString()!.StartsWith("SSS")),
            l => Assert.True(l.GetProperty("hasStudentLogins").GetBoolean()));
        Assert.All(levels.Where(l => l.GetProperty("name").GetString()!.StartsWith("Primary")),
            l => Assert.True(l.GetProperty("hasStudentLogins").GetBoolean()));
        var nursery1 = levels.Single(l => l.GetProperty("name").GetString() == "Nursery 1");
        Assert.Equal("Developmental", nursery1.GetProperty("assessmentMode").GetString());
        Assert.False(nursery1.GetProperty("hasStudentLogins").GetBoolean());
        var primary1 = levels.Single(l => l.GetProperty("name").GetString() == "Primary 1");
        Assert.Equal("Numeric", primary1.GetProperty("assessmentMode").GetString());
        Assert.True(primary1.GetProperty("hasStudentLogins").GetBoolean());
        Assert.True(levels.Single(l => l.GetProperty("name").GetString() == "JSS 1").GetProperty("hasStudentLogins").GetBoolean());

        var subjects = (await Json(await school.Owner.GetAsync("/api/school/subjects", Ct))).EnumerateArray().ToList();
        Assert.Equal(29, subjects.Count);
        var english = subjects.Single(s => s.GetProperty("name").GetString() == "English Language");
        Assert.Equal("ENG", english.GetProperty("code").GetString());
        Assert.Equal(subjects.Count, subjects.Select(s => s.GetProperty("code").GetString()).Distinct().Count());

        // Applying again does not duplicate anything.
        (await ApplyAsync(school, true, true)).EnsureSuccessStatusCode();
        Assert.Equal(15, (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).GetArrayLength());
        Assert.Equal(29, (await Json(await school.Owner.GetAsync("/api/school/subjects", Ct))).GetArrayLength());
    }

    [Fact]
    public async Task Posted_level_without_mode_defaults_from_stage()
    {
        var school = await kit.SchoolAsync("Default Mode School");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "N1", stage = "Nursery", order = 1, hasStudentLogins = false }, Ct));
        Assert.Equal("Developmental", level.GetProperty("assessmentMode").GetString());
    }

    [Fact]
    public async Task The_checklist_ticks_off_classes_and_subjects()
    {
        var school = await kit.SchoolAsync("Checklist School");
        (await ApplyAsync(school, true, false)).EnsureSuccessStatusCode();

        async Task<Dictionary<string, bool>> ChecklistAsync() =>
            (await Json(await school.Owner.GetAsync("/api/school/setup", Ct))).GetProperty("checklist").EnumerateArray()
                .ToDictionary(c => c.GetProperty("key").GetString()!, c => c.GetProperty("done").GetBoolean());

        var before = await ChecklistAsync();
        Assert.False(before["levels-and-classes"]); // levels alone are not enough
        Assert.False(before["subjects"]);

        var levels = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        (await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = levels[9].GetProperty("id").GetGuid(), arm = "A" }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Music", code = "MUS" }, Ct)).EnsureSuccessStatusCode();

        var after = await ChecklistAsync();
        Assert.True(after["levels-and-classes"]);
        Assert.True(after["subjects"]);
        Assert.False(after["students"]);
    }

    [Fact]
    public async Task Class_names_join_level_and_arm()
    {
        var school = await kit.SchoolAsync("Class Names School");
        (await ApplyAsync(school, true, false)).EnsureSuccessStatusCode();
        var levels = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        Guid Level(string name) => levels.Single(l => l.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

        var jss = await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = Level("JSS 1"), arm = "A" }, Ct));
        var primary = await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = Level("Primary 4"), arm = "B" }, Ct));
        Assert.Equal("JSS 1A", jss.GetProperty("name").GetString());
        Assert.Equal("Primary 4B", primary.GetProperty("name").GetString());

        // The class sits in the current session by default.
        var setup = await Json(await school.Owner.GetAsync("/api/school/setup", Ct));
        var current = setup.GetProperty("sessions").EnumerateArray().Single(s => s.GetProperty("isCurrent").GetBoolean()).GetProperty("id").GetGuid();
        Assert.Equal(current, jss.GetProperty("sessionId").GetGuid());

        // The same arm twice in one level and session is a conflict.
        var duplicate = await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = Level("JSS 1"), arm = "A" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var list = await Json(await school.Owner.GetAsync("/api/school/classes", Ct));
        Assert.Equal(2, list.GetArrayLength());
    }

    [Fact]
    public async Task Subjects_attach_to_classes_and_can_be_removed()
    {
        var school = await kit.SchoolAsync("Class Subjects School");
        (await ApplyAsync(school, true, true)).EnsureSuccessStatusCode();
        var level = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().First(l => l.GetProperty("name").GetString() == "SSS 1");
        var cls = await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "Science" }, Ct));
        var classId = cls.GetProperty("id").GetGuid();
        var subjects = (await Json(await school.Owner.GetAsync("/api/school/subjects", Ct))).EnumerateArray().ToList();
        var maths = subjects.Single(s => s.GetProperty("name").GetString() == "Mathematics").GetProperty("id").GetGuid();

        var added = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = maths }, Ct));
        Assert.Equal("Mathematics", added.GetProperty("subjectName").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = maths }, Ct)).StatusCode);

        Assert.Equal(1, (await Json(await school.Owner.GetAsync($"/api/school/classes/{classId}/subjects", Ct))).GetArrayLength());

        // A subject in use cannot be deleted; removing it from the class frees it.
        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.DeleteAsync($"/api/school/subjects/{maths}", Ct)).StatusCode);
        (await school.Owner.DeleteAsync($"/api/school/classes/{classId}/subjects/{maths}", Ct)).EnsureSuccessStatusCode();
        (await school.Owner.DeleteAsync($"/api/school/subjects/{maths}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_level_with_classes_cannot_be_deleted()
    {
        var school = await kit.SchoolAsync("Level Delete School");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels", new { name = "Reception", stage = "Nursery", order = 1, hasStudentLogins = false }, Ct));
        var levelId = level.GetProperty("id").GetGuid();
        (await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await school.Owner.DeleteAsync($"/api/school/levels/{levelId}", Ct)).StatusCode);

        var other = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels", new { name = "Spare", stage = "Primary", order = 2, hasStudentLogins = false }, Ct));
        (await school.Owner.DeleteAsync($"/api/school/levels/{other.GetProperty("id").GetGuid()}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Assessment_defaults_to_ca1_ca2_and_exam()
    {
        var school = await kit.SchoolAsync("Default Scheme School");
        var scheme = (await Json(await school.Owner.GetAsync("/api/school/assessment", Ct))).GetProperty("components").EnumerateArray().ToList();
        Assert.Equal(["CA1", "CA2", "Exam"], scheme.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal([20m, 20m, 60m], scheme.Select(c => c.GetProperty("maxScore").GetDecimal()));
    }

    [Fact]
    public async Task Assessment_components_must_total_100()
    {
        var school = await kit.SchoolAsync("Assessment School");

        var bad = await school.Owner.PutAsJsonAsync("/api/school/assessment", new
        {
            components = new object[] { new { name = "CA1", maxScore = 20 }, new { name = "Exam", maxScore = 60 } },
        }, Ct);
        var problem = await ProblemAsync(bad, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("components", out _));

        var good = await school.Owner.PutAsJsonAsync("/api/school/assessment", new
        {
            components = new object[] { new { name = "Test", maxScore = 30 }, new { name = "Project", maxScore = 10 }, new { name = "Exam", maxScore = 60 } },
        }, Ct);
        var saved = (await Json(good)).GetProperty("components").EnumerateArray().ToList();
        Assert.Equal(["Test", "Project", "Exam"], saved.Select(c => c.GetProperty("name").GetString()));

        var reread = (await Json(await school.Owner.GetAsync("/api/school/assessment", Ct))).GetProperty("components").EnumerateArray().ToList();
        Assert.Equal(3, reread.Count);

        // Duplicate names and empty schemes are rejected too.
        var dup = await school.Owner.PutAsJsonAsync("/api/school/assessment", new
        {
            components = new object[] { new { name = "Exam", maxScore = 50 }, new { name = "exam", maxScore = 50 } },
        }, Ct);
        await ProblemAsync(dup, HttpStatusCode.BadRequest);
        await ProblemAsync(await school.Owner.PutAsJsonAsync("/api/school/assessment", new { components = Array.Empty<object>() }, Ct), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_level_scheme_overrides_the_school_default()
    {
        var school = await kit.SchoolAsync("Override School");
        (await ApplyAsync(school, true, false)).EnsureSuccessStatusCode();
        var levels = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        var jss = levels.Single(l => l.GetProperty("name").GetString() == "JSS 1").GetProperty("id").GetGuid();
        var primary = levels.Single(l => l.GetProperty("name").GetString() == "Primary 1").GetProperty("id").GetGuid();

        (await school.Owner.PutAsJsonAsync("/api/school/assessment", new
        {
            levelId = jss,
            components = new object[] { new { name = "CA", maxScore = 40 }, new { name = "Exam", maxScore = 60 } },
        }, Ct)).EnsureSuccessStatusCode();

        var forJss = await Json(await school.Owner.GetAsync($"/api/school/assessment?levelId={jss}", Ct));
        Assert.Equal(["CA", "Exam"], forJss.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(jss, forJss.GetProperty("levelId").GetGuid());
        var forPrimary = await Json(await school.Owner.GetAsync($"/api/school/assessment?levelId={primary}", Ct));
        Assert.Equal(["CA1", "CA2", "Exam"], forPrimary.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, forPrimary.GetProperty("levelId").ValueKind);
    }

    [Fact]
    public async Task Grading_defaults_to_the_nigerian_bands()
    {
        var school = await kit.SchoolAsync("Default Bands School");
        var bands = (await Json(await school.Owner.GetAsync("/api/school/grading", Ct))).GetProperty("bands").EnumerateArray().ToList();
        Assert.Equal(["A1", "B2", "B3", "C4", "C5", "C6", "D7", "E8", "F9"], bands.Select(b => b.GetProperty("grade").GetString()));
        Assert.Equal(75m, bands[0].GetProperty("min").GetDecimal());
        Assert.Equal(100m, bands[0].GetProperty("max").GetDecimal());
        Assert.Equal("Excellent", bands[0].GetProperty("remark").GetString());
        Assert.Equal(0m, bands[^1].GetProperty("min").GetDecimal());
        Assert.Equal("Fail", bands[^1].GetProperty("remark").GetString());
    }

    [Fact]
    public async Task Grading_bands_must_not_overlap_or_leave_gaps()
    {
        var school = await kit.SchoolAsync("Grading School");

        async Task<HttpResponseMessage> PutAsync(object[] bands) =>
            await school.Owner.PutAsJsonAsync("/api/school/grading", new { bands }, Ct);

        // Gap: 40-49 is missing.
        var gap = await PutAsync(Bands((50, 100, "A"), (0, 39, "F")));
        Assert.True((await ProblemAsync(gap, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("bands", out _));
        // Overlap: 50-60 and 60-100 share 60.
        await ProblemAsync(await PutAsync(Bands((60, 100, "A"), (50, 60, "B"), (0, 49, "F"))), HttpStatusCode.BadRequest);
        // Does not start at 0 or reach 100.
        await ProblemAsync(await PutAsync(Bands((50, 100, "A"), (10, 49, "F"))), HttpStatusCode.BadRequest);
        await ProblemAsync(await PutAsync(Bands((50, 90, "A"), (0, 49, "F"))), HttpStatusCode.BadRequest);
        // Empty and duplicate grades.
        await ProblemAsync(await PutAsync([]), HttpStatusCode.BadRequest);
        await ProblemAsync(await PutAsync(Bands((50, 100, "A"), (0, 49, "A"))), HttpStatusCode.BadRequest);

        // A clean two-band scale saves, in any submitted order, and is returned best first.
        var good = await Json(await PutAsync(Bands((0, 49, "F"), (50, 100, "P"))));
        Assert.Equal(["P", "F"], good.GetProperty("bands").EnumerateArray().Select(b => b.GetProperty("grade").GetString()));

        // Bands that touch at one decimal place (74.9 then 75) are valid.
        var decimals = await PutAsync(Bands((75, 100, "A"), (0, 74.9m, "B")));
        Assert.True(decimals.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Service_resolves_level_overrides_before_the_school_default()
    {
        var school = await kit.SchoolAsync("Service School");
        (await ApplyAsync(school, true, false)).EnsureSuccessStatusCode();
        var levels = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        var sss = levels.Single(l => l.GetProperty("name").GetString() == "SSS 1").GetProperty("id").GetGuid();
        var jss = levels.Single(l => l.GetProperty("name").GetString() == "JSS 1").GetProperty("id").GetGuid();

        (await school.Owner.PutAsJsonAsync("/api/school/grading", new { levelId = sss, bands = Bands((0, 49, "F"), (50, 100, "P")) }, Ct)).EnsureSuccessStatusCode();

        var sssBands = await api.AsTenantAsync<AcademicService, IReadOnlyList<Saas.Domain.School.GradingBand>>(school.TenantId, s => s.BandsFor(sss));
        var jssBands = await api.AsTenantAsync<AcademicService, IReadOnlyList<Saas.Domain.School.GradingBand>>(school.TenantId, s => s.BandsFor(jss));
        Assert.Equal(["P", "F"], sssBands.Select(b => b.Grade));
        Assert.Equal(9, jssBands.Count);
        var scheme = await api.AsTenantAsync<AcademicService, IReadOnlyList<Saas.Domain.School.AssessmentComponent>>(school.TenantId, s => s.SchemeFor(jss));
        Assert.Equal(["CA1", "CA2", "Exam"], scheme.Select(c => c.Name));
    }

    [Fact]
    public async Task Teacher_cannot_edit_setup()
    {
        var school = await kit.SchoolAsync("Roles School");
        (await ApplyAsync(school, true, true)).EnsureSuccessStatusCode();
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var level = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct)))[0].GetProperty("id").GetGuid();

        // Staff read everything.
        foreach (var path in new[] { "levels", "classes", "subjects", "assessment", "grading" })
            Assert.True((await teacher.GetAsync($"/api/school/{path}", Ct)).IsSuccessStatusCode, path);

        var writes = new[]
        {
            await teacher.PostAsJsonAsync("/api/school/setup/apply-template", new { levels = true, subjects = true }, Ct),
            await teacher.PostAsJsonAsync("/api/school/levels", new { name = "X", stage = "Primary", order = 1 }, Ct),
            await teacher.PostAsJsonAsync("/api/school/classes", new { levelId = level, arm = "A" }, Ct),
            await teacher.PostAsJsonAsync("/api/school/subjects", new { name = "Music", code = "MUS" }, Ct),
            await teacher.PutAsJsonAsync("/api/school/assessment", new { components = new object[] { new { name = "Exam", maxScore = 100 } } }, Ct),
            await teacher.PutAsJsonAsync("/api/school/grading", new { bands = Bands((0, 100, "A")) }, Ct),
        };
        foreach (var response in writes)
        {
            var problem = await ProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("You need the Owner or Admin role for this.", problem.GetProperty("detail").GetString());
        }

        // An Admin can.
        var (admin, _) = await kit.StaffAsync(school, MemberRole.Admin, "Principal");
        Assert.True((await admin.PostAsJsonAsync("/api/school/subjects", new { name = "Music", code = "MUS" }, Ct)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Other_schools_do_not_see_these_records()
    {
        var one = await kit.SchoolAsync("Isolated One");
        var two = await kit.SchoolAsync("Isolated Two");
        (await ApplyAsync(one, true, true)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await Json(await two.Owner.GetAsync("/api/school/levels", Ct))).GetArrayLength());
        Assert.Equal(0, (await Json(await two.Owner.GetAsync("/api/school/subjects", Ct))).GetArrayLength());
    }
}
