using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// The student Play area: a word search and brain teasers. A teacher or leadership writes word lists and teasers; leadership turns
/// Play off. A student never sees a teaser's answer, and Play never writes a score.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolPlayTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string PlayOff = "Play is turned off for this school.";
    private const string ClassSubjectMissing = "Class subject was not found.";

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
        SchoolKit.School School, Guid ClassId, Guid LevelId, Guid MathsId, Guid EnglishId, Guid TermId,
        HttpClient Teacher, HttpClient OtherTeacher, Guid StudentId, HttpClient Student);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var (otherTeacher, otherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "English teacher");
        var maths = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var english = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "English", code = "ENG" }, Ct));
        var mathsLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = maths.GetProperty("id").GetGuid(), teacherStaffId = await StaffIdAsync(school, teacherUserId) }, Ct));
        var englishLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = english.GetProperty("id").GetGuid(), teacherStaffId = await StaffIdAsync(school, otherUserId) }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var student = await kit.StudentLoginAsync(school, studentId);
        return new Setup(school, classId, levelId, mathsLink.GetProperty("id").GetGuid(), englishLink.GetProperty("id").GetGuid(), termId,
            teacher, otherTeacher, studentId, student);
    }

    private static string[] Words(JsonElement body) => body.GetProperty("words").EnumerateArray().Select(w => w.GetString()!).ToArray();

    /// <summary>The grid is square, big enough for the longest word, one A–Z letter a cell, and each word reads once across or down.</summary>
    private static void AssertGridHolds(JsonElement body)
    {
        var words = Words(body);
        Assert.NotEmpty(words);
        var rows = body.GetProperty("grid").EnumerateArray().Select(r => r.EnumerateArray().Select(c => c.GetString()!).ToArray()).ToArray();
        var size = rows.Length;
        Assert.True(size >= words.Max(w => w.Length), $"a {size} by {size} grid is too small for {words.MaxBy(w => w.Length)}");
        Assert.All(rows, row =>
        {
            Assert.Equal(size, row.Length);
            Assert.All(row, cell => Assert.Matches("^[A-Z]$", cell));
        });
        var across = rows.Select(r => string.Concat(r)).ToArray();
        var down = Enumerable.Range(0, size).Select(c => string.Concat(rows.Select(r => r[c]))).ToArray();
        foreach (var word in words)
        {
            Assert.Matches("^[A-Z]+$", word);
            var count = across.Concat(down).Sum(line => Occurrences(line, word));
            Assert.True(count == 1, $"{word} reads {count} times in the grid");
        }
    }

    private static int Occurrences(string line, string word)
    {
        var count = 0;
        for (var i = line.IndexOf(word, StringComparison.Ordinal); i >= 0; i = line.IndexOf(word, i + 1, StringComparison.Ordinal)) count++;
        return count;
    }

    [Fact]
    public async Task Word_search_contains_the_word_list()
    {
        var s = await SetupAsync("Word Search School");
        var saved = await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/words",
            new { classSubjectId = s.MathsId, name = "Shapes and numbers", words = new[] { "Algebra", "angle", " Prime ", "RATIO", "Square" } }, Ct));
        Assert.Equal(s.MathsId, saved.GetProperty("classSubjectId").GetGuid());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("levelId").ValueKind);
        Assert.Equal("Shapes and numbers", saved.GetProperty("name").GetString());
        Assert.Equal(["ALGEBRA", "ANGLE", "PRIME", "RATIO", "SQUARE"], Words(saved));

        for (var n = 0; n < 5; n++)
        {
            var body = await Json(await s.Student.GetAsync($"/api/student/play/word-search?classSubjectId={s.MathsId}", Ct));
            Assert.Equal(["ALGEBRA", "ANGLE", "PRIME", "RATIO", "SQUARE"], Words(body));
            AssertGridHolds(body);
        }
    }

    [Fact]
    public async Task Word_search_falls_back_to_the_school_list_then_the_built_in_one()
    {
        var s = await SetupAsync("Word Fallback School");
        var builtIn = await Json(await s.Student.GetAsync("/api/student/play/word-search", Ct));
        AssertGridHolds(builtIn);

        var otherLevel = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "SSS 1", stage = "Sss", order = 20, hasStudentLogins = true }, Ct))).GetProperty("id").GetGuid();
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/play/words", new { levelId = otherLevel, name = "Senior words", words = new[] { "Osmosis", "Enzyme" } }, Ct));
        Assert.Equal(Words(builtIn).Order(), Words(await Json(await s.Student.GetAsync("/api/student/play/word-search", Ct))).Order());

        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/play/words", new { name = "School words", words = new[] { "River", "Ocean", "Market" } }, Ct));
        var school = await Json(await s.Student.GetAsync("/api/student/play/word-search", Ct));
        Assert.Equal(["RIVER", "OCEAN", "MARKET"], Words(school));
        AssertGridHolds(school);

        // A class subject with no list, and one that is not of the student's class, both use the school list.
        Assert.Equal(["RIVER", "OCEAN", "MARKET"], Words(await Json(await s.Student.GetAsync($"/api/student/play/word-search?classSubjectId={s.EnglishId}", Ct))));
        Assert.Equal(["RIVER", "OCEAN", "MARKET"], Words(await Json(await s.Student.GetAsync($"/api/student/play/word-search?classSubjectId={Guid.NewGuid()}", Ct))));

        await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/words", new { levelId = s.LevelId, name = "JSS 1 words", words = new[] { "Harmattan", "Savanna" } }, Ct));
        Assert.Equal(["HARMATTAN", "SAVANNA"], Words(await Json(await s.Student.GetAsync("/api/student/play/word-search", Ct))));
    }

    [Fact]
    public async Task Word_list_needs_letters_only()
    {
        var s = await SetupAsync("Word Rules School");
        foreach (var body in new object[]
        {
            new { name = "Bad words", words = new[] { "Ab3" } },
            new { name = "Bad words", words = new[] { "two words" } },
            new { name = "Bad words", words = Array.Empty<string>() },
            new { name = "", words = new[] { "River" } },
            new { name = "Twice", words = new[] { "River", "river" } },
            new { classSubjectId = s.MathsId, levelId = s.LevelId, name = "Both", words = new[] { "River" } },
        })
            await ProblemAsync(await s.School.Owner.PostAsJsonAsync("/api/school/play/words", body, Ct), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Teaser_payload_never_has_the_answer()
    {
        var s = await SetupAsync("Teaser School");
        var saved = await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/teasers",
            new { prompt = "What has to be broken before you can use it?", answer = "An egg" }, Ct));
        Assert.Equal("An egg", saved.GetProperty("answer").GetString());
        Assert.False(saved.GetProperty("archived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("levelId").ValueKind);
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/play/teasers",
            new { levelId = s.LevelId, prompt = "What gets wetter the more it dries?", answer = "A towel" }, Ct));
        var otherLevel = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "SSS 1", stage = "Sss", order = 20, hasStudentLogins = true }, Ct))).GetProperty("id").GetGuid();
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/play/teasers",
            new { levelId = otherLevel, prompt = "A senior riddle", answer = "Senior answer" }, Ct));
        var archivedId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/play/teasers",
            new { prompt = "An archived riddle", answer = "Archived answer" }, Ct))).GetProperty("id").GetGuid();
        await api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE brain_teasers SET archived = true WHERE id = {archivedId}", Ct));

        var response = await s.Student.GetAsync("/api/student/play/teasers", Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {raw}");
        Assert.DoesNotContain("answer", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("egg", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("towel", raw, StringComparison.OrdinalIgnoreCase);
        var prompts = JsonDocument.Parse(raw).RootElement.EnumerateArray().Select(t => t.GetProperty("prompt").GetString()).Order().ToArray();
        Assert.Equal(["What gets wetter the more it dries?", "What has to be broken before you can use it?"], prompts);
    }

    [Fact]
    public async Task Play_hidden_when_disabled()
    {
        var s = await SetupAsync("Play Off School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await Json(await s.Student.GetAsync("/api/student/play/teasers", Ct));
        await Json(await s.Student.GetAsync("/api/student/play/word-search", Ct));

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Teacher.PutAsJsonAsync("/api/school/settings/play", new { enabled = false }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PutAsJsonAsync("/api/school/settings/play", new { enabled = false }, Ct)).StatusCode);
        var off = await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/play", new { enabled = false }, Ct));
        Assert.False(off.GetProperty("enabled").GetBoolean());

        foreach (var url in new[] { "/api/student/play/teasers", "/api/student/play/word-search", $"/api/student/play/word-search?classSubjectId={s.MathsId}" })
        {
            var problem = await ProblemAsync(await s.Student.GetAsync(url, Ct), HttpStatusCode.NotFound);
            Assert.Equal(PlayOff, problem.GetProperty("detail").GetString());
        }

        var on = await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/play", new { enabled = true }, Ct));
        Assert.True(on.GetProperty("enabled").GetBoolean());
        await Json(await s.Student.GetAsync("/api/student/play/teasers", Ct));
    }

    [Fact]
    public async Task Play_does_not_write_a_score()
    {
        var s = await SetupAsync("Play Score School");
        var assessment = await Json(await s.School.Owner.GetAsync("/api/school/assessment", Ct));
        var examId = assessment.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Exam").GetProperty("id").GetGuid();
        var enrolmentId = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.StudentId == s.StudentId).Select(e => e.Id).SingleAsync(Ct));
        (await s.Teacher.PutAsJsonAsync("/api/school/scores", new
        {
            classSubjectId = s.MathsId, termId = s.TermId,
            entries = new[] { new { enrolmentId, componentId = examId, value = 40m } },
        }, Ct)).EnsureSuccessStatusCode();
        await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/words", new { classSubjectId = s.MathsId, name = "Maths words", words = new[] { "Fraction", "Decimal" } }, Ct));
        await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/teasers", new { prompt = "What has hands but cannot clap?", answer = "A clock" }, Ct));
        Task<int> ScoresAsync() => api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(score => score.EnrolmentId == enrolmentId, Ct));
        var before = await ScoresAsync();
        Assert.Equal(1, before);

        await Json(await s.Student.GetAsync("/api/student/play/teasers", Ct));
        await Json(await s.Student.GetAsync($"/api/student/play/word-search?classSubjectId={s.MathsId}", Ct));

        Assert.Equal(before, await ScoresAsync());
        Assert.Equal(0, await api.WithDbAsync(db => db.ReportCardBundles.IgnoreQueryFilters().CountAsync(b => b.TenantId == s.School.TenantId, Ct)));
    }

    [Fact]
    public async Task A_nursery_child_has_no_login_so_play_stays_closed()
    {
        var school = await kit.SchoolAsync("Nursery Play School");
        var levelId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct))).GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var childId = await kit.StudentAsync(school, classId, "Tobi");

        var refused = await ProblemAsync(await school.Owner.PostAsJsonAsync($"/api/school/students/{childId}/login", new { password = SchoolKit.StudentPassword }, Ct), HttpStatusCode.BadRequest);
        Assert.True(refused.GetProperty("errors").TryGetProperty("level", out _), refused.ToString());

        var anon = api.CreateClient();
        foreach (var url in new[] { "/api/student/play/teasers", "/api/student/play/word-search" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await school.Owner.GetAsync(url, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Bursar_is_refused_before_lookup()
    {
        var s = await SetupAsync("Play Bursar School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/play/words", new { classSubjectId = Guid.NewGuid(), name = "Words", words = new[] { "River" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/play/words", new { classSubjectId = s.MathsId, name = "Words", words = new[] { "River" } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/play/teasers", new { levelId = Guid.NewGuid(), prompt = "A riddle", answer = "An answer" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/student/play/teasers", Ct)).StatusCode);

        var other = await ProblemAsync(await s.OtherTeacher.PostAsJsonAsync("/api/school/play/words", new { classSubjectId = s.MathsId, name = "Words", words = new[] { "River" } }, Ct), HttpStatusCode.NotFound);
        Assert.Equal(ClassSubjectMissing, other.GetProperty("detail").GetString());
        var missing = await ProblemAsync(await s.School.Owner.PostAsJsonAsync("/api/school/play/words", new { classSubjectId = Guid.NewGuid(), name = "Words", words = new[] { "River" } }, Ct), HttpStatusCode.NotFound);
        Assert.Equal(ClassSubjectMissing, missing.GetProperty("detail").GetString());
        await ProblemAsync(await s.School.Owner.PostAsJsonAsync("/api/school/play/teasers", new { levelId = Guid.NewGuid(), prompt = "A riddle", answer = "An answer" }, Ct), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_list_word_lists_and_teasers()
    {
        var s = await SetupAsync("Play Lists School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/play/words", new { name = "School words", words = new[] { "River", "Ocean" } }, Ct));
        await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/words", new { levelId = s.LevelId, name = "Level words", words = new[] { "Savanna" } }, Ct));
        await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/words", new { classSubjectId = s.MathsId, name = "Maths words", words = new[] { "Angle" } }, Ct));
        await Json(await s.Teacher.PostAsJsonAsync("/api/school/play/teasers", new { prompt = "What has keys but opens no locks?", answer = "A piano" }, Ct));

        static string[] Names(JsonElement list) => list.EnumerateArray().Select(w => w.GetProperty("name").GetString()!).Order().ToArray();
        var owner = await Json(await s.School.Owner.GetAsync("/api/school/play/words", Ct));
        Assert.Equal(["Level words", "Maths words", "School words"], Names(owner));
        Assert.Equal(["ANGLE"], Words(owner.EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Maths words")));
        Assert.Equal(["Level words", "Maths words", "School words"], Names(await Json(await s.Teacher.GetAsync("/api/school/play/words", Ct))));
        // A class-subject list is shown to that subject's teacher and leadership only.
        Assert.Equal(["Level words", "School words"], Names(await Json(await s.OtherTeacher.GetAsync("/api/school/play/words", Ct))));

        var teaser = Assert.Single((await Json(await s.OtherTeacher.GetAsync("/api/school/play/teasers", Ct))).EnumerateArray());
        Assert.Equal("What has keys but opens no locks?", teaser.GetProperty("prompt").GetString());
        Assert.Equal("A piano", teaser.GetProperty("answer").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/school/play/words", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/school/play/teasers", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Student.GetAsync("/api/school/play/teasers", Ct)).StatusCode);
    }

    [Fact]
    public async Task Play_setting_reads_back_the_saved_value()
    {
        var s = await SetupAsync("Play Setting Read School");
        Assert.True((await Json(await s.School.Owner.GetAsync("/api/school/settings/play", Ct))).GetProperty("enabled").GetBoolean());
        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/play", new { enabled = false }, Ct));
        Assert.False((await Json(await s.School.Owner.GetAsync("/api/school/settings/play", Ct))).GetProperty("enabled").GetBoolean());
        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/play", new { enabled = true }, Ct));
        Assert.True((await Json(await s.School.Owner.GetAsync("/api/school/settings/play", Ct))).GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Teacher.GetAsync("/api/school/settings/play", Ct)).StatusCode);
    }
}
