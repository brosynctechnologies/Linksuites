using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Question bank, CBT papers, and students sitting them. A subject teacher or leadership writes questions and papers.
/// A draw-only paper reuses the bank. The bursar is refused before a subject, a paper, or an attempt is looked up.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolCbtTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private static readonly DateOnly ExamDay = new(2026, 11, 16);
    private static readonly DateTimeOffset WindowOpen = new(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset WindowClose = new(2026, 11, 16, 11, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset OtherOpen = new(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset OtherClose = new(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(1));

    private const string NotEnough = "There are not enough questions in the bank for this test.";
    private const string SubjectMissing = "This subject was not found.";
    private const string TestMissing = "This test was not found.";
    private const string Developmental = "This class uses developmental assessment.";
    private const string WindowRequired = "Choose when the test opens and when it closes.";
    private const string TimeUp = "Time is up. Your answers were saved.";
    private const string AlreadyStarted = "You have already started this test.";
    private const string HandedIn = "This test has already been handed in.";
    private const string TermLocked = "This term is locked for the class, so scores can't change. Ask the principal to unlock it.";

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
        SchoolKit.School School, Guid ClassId, Guid LevelId, Guid MathsSubjectId, Guid EnglishSubjectId,
        Guid MathsId, Guid EnglishId, Guid TermId, HttpClient Teacher, HttpClient OtherTeacher);

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var (otherTeacher, otherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "English teacher");
        var teacherStaffId = await StaffIdAsync(school, teacherUserId);
        var otherStaffId = await StaffIdAsync(school, otherUserId);
        var maths = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var english = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "English", code = "ENG" }, Ct));
        var mathsLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = maths.GetProperty("id").GetGuid(), teacherStaffId }, Ct));
        var englishLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = english.GetProperty("id").GetGuid(), teacherStaffId = otherStaffId }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        return new Setup(school, classId, levelId, maths.GetProperty("id").GetGuid(), english.GetProperty("id").GetGuid(),
            mathsLink.GetProperty("id").GetGuid(), englishLink.GetProperty("id").GetGuid(), termId, teacher, otherTeacher);
    }

    private static object Question(Guid subjectId, string stem, Guid? levelId = null, int correctIndex = 1, decimal marks = 2, string[]? options = null) => new
    {
        subjectId,
        levelId,
        stem,
        options = options ?? ["2", "4", "6", "8"],
        correctIndex,
        marks,
    };

    /// <summary>A paper whose window is open now, so a student can start it during the test.</summary>
    private static object SittingPaper(
        Setup s, int drawCount, Guid[]? picks = null, Guid? assessmentComponentId = null, int durationMinutes = 40,
        DateTimeOffset? openAt = null, DateTimeOffset? closeAt = null) => new
    {
        classSubjectId = s.MathsId,
        termId = s.TermId,
        title = "First term mathematics test",
        durationMinutes,
        openAt = openAt ?? DateTimeOffset.UtcNow.AddHours(-1),
        closeAt = closeAt ?? DateTimeOffset.UtcNow.AddHours(3),
        assessmentComponentId,
        drawCount,
        pickQuestionIds = picks ?? [],
    };

    private async Task<Guid> PublishSittingAsync(Setup s, object paper)
    {
        var created = await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", paper, Ct));
        var id = created.GetProperty("id").GetGuid();
        await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{id}/publish", null, Ct));
        return id;
    }

    private async Task<HttpClient> PupilAsync(Setup s, string name, Guid? classId = null)
    {
        var id = await kit.StudentAsync(s.School, classId ?? s.ClassId, name);
        return await kit.StudentLoginAsync(s.School, id);
    }

    private async Task<Guid> ExamIdAsync(Setup s)
    {
        var assessment = await Json(await s.School.Owner.GetAsync("/api/school/assessment", Ct));
        var exam = assessment.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Exam");
        Assert.Equal(60m, exam.GetProperty("maxScore").GetDecimal());
        return exam.GetProperty("id").GetGuid();
    }

    private static int IndexOfOption(JsonElement item, string text)
    {
        var index = item.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList().IndexOf(text);
        Assert.True(index >= 0, $"Option {text} was not on the item.");
        return index;
    }

    private async Task AnswerAsync(HttpClient student, Guid attemptId, JsonElement item, string option)
    {
        var response = await student.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers",
            new { itemId = item.GetProperty("id").GetGuid(), chosenIndex = IndexOfOption(item, option) }, Ct);
        await Json(response);
    }

    private static string Arrangement(JsonElement item) =>
        item.GetProperty("stem").GetString() + "|" + string.Join(",", item.GetProperty("options").EnumerateArray().Select(o => o.GetString()));

    private static void AssertNoAnswerKey(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain("correct", property.Name, StringComparison.OrdinalIgnoreCase);
                Assert.NotEqual("key", property.Name, StringComparer.OrdinalIgnoreCase);
                Assert.NotEqual("answer", property.Name, StringComparer.OrdinalIgnoreCase);
                AssertNoAnswerKey(property.Value);
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) AssertNoAnswerKey(item);
    }

    private static object Paper(Setup s, int drawCount, Guid[]? picks = null, Guid? examSittingId = null, DateTimeOffset? openAt = null, DateTimeOffset? closeAt = null, bool window = true) => new
    {
        classSubjectId = s.MathsId,
        termId = s.TermId,
        title = "First term mathematics test",
        durationMinutes = 40,
        examSittingId,
        openAt = examSittingId is null && window ? openAt ?? WindowOpen : openAt,
        closeAt = examSittingId is null && window ? closeAt ?? WindowClose : closeAt,
        drawCount,
        pickQuestionIds = picks ?? Array.Empty<Guid>(),
    };

    private async Task<List<Guid>> BankAsync(Setup s, int count, Guid? levelId = null)
    {
        var ids = new List<Guid>();
        for (var i = 1; i <= count; i++)
        {
            var saved = await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions", Question(s.MathsSubjectId, $"What is {i} + {i}?", levelId), Ct));
            ids.Add(saved.GetProperty("id").GetGuid());
            Assert.Equal(s.MathsSubjectId, saved.GetProperty("subjectId").GetGuid());
            Assert.Equal(1, saved.GetProperty("correctIndex").GetInt32());
            Assert.Equal(2m, saved.GetProperty("marks").GetDecimal());
            Assert.False(saved.GetProperty("archived").GetBoolean());
            Assert.Equal(4, saved.GetProperty("options").GetArrayLength());
        }
        return ids;
    }

    [Fact]
    public async Task Draw_only_paper_does_not_need_new_questions()
    {
        var s = await SetupAsync("Draw Only School");
        var ids = await BankAsync(s, 5);
        Assert.Equal(5, ids.Count);

        var listed = await Json(await s.Teacher.GetAsync($"/api/school/questions?subjectId={s.MathsSubjectId}", Ct));
        Assert.Equal(5, listed.GetArrayLength());

        var response = await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 3), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {body}");
        var paper = JsonDocument.Parse(body).RootElement;
        Assert.Equal(s.MathsId, paper.GetProperty("classSubjectId").GetGuid());
        Assert.Equal(s.TermId, paper.GetProperty("termId").GetGuid());
        Assert.Equal("First term mathematics test", paper.GetProperty("title").GetString());
        Assert.Equal(40, paper.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(3, paper.GetProperty("drawCount").GetInt32());
        Assert.Empty(paper.GetProperty("pickQuestionIds").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, paper.GetProperty("examSittingId").ValueKind);
        Assert.Equal(JsonValueKind.Null, paper.GetProperty("assessmentComponentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, paper.GetProperty("publishedAt").ValueKind);
        Assert.Equal(WindowOpen, paper.GetProperty("openAt").GetDateTimeOffset());
        Assert.Equal(WindowClose, paper.GetProperty("closeAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Draw_count_too_high()
    {
        var s = await SetupAsync("Draw Too High School");
        await BankAsync(s, 5);

        var problem = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 6), Ct), HttpStatusCode.BadRequest);
        Assert.Equal(NotEnough, problem.GetProperty("detail").GetString());

        var still = await Json(await s.Teacher.GetAsync($"/api/school/questions?subjectId={s.MathsSubjectId}", Ct));
        Assert.Equal(5, still.GetArrayLength());
    }

    [Fact]
    public async Task Teacher_cannot_author_another_subjects_bank()
    {
        var s = await SetupAsync("Other Subject Bank School");
        var denied = await ProblemAsync(await s.OtherTeacher.PostAsJsonAsync("/api/school/questions", Question(s.MathsSubjectId, "What is 3 + 3?"), Ct), HttpStatusCode.NotFound);
        Assert.Equal(SubjectMissing, denied.GetProperty("detail").GetString());
        var hidden = await ProblemAsync(await s.OtherTeacher.GetAsync($"/api/school/questions?subjectId={s.MathsSubjectId}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(SubjectMissing, hidden.GetProperty("detail").GetString());

        var missing = await ProblemAsync(await s.Teacher.GetAsync($"/api/school/questions?subjectId={Guid.NewGuid()}", Ct), HttpStatusCode.NotFound);
        Assert.Equal(SubjectMissing, missing.GetProperty("detail").GetString());

        var saved = await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions", Question(s.MathsSubjectId, "What is 3 + 3?"), Ct));
        Assert.Equal("What is 3 + 3?", saved.GetProperty("stem").GetString());
        var leadership = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/questions", Question(s.EnglishSubjectId, "Which word is a noun?"), Ct));
        Assert.Equal(s.EnglishSubjectId, leadership.GetProperty("subjectId").GetGuid());
        var english = await Json(await s.OtherTeacher.GetAsync($"/api/school/questions?subjectId={s.EnglishSubjectId}", Ct));
        Assert.Equal("Which word is a noun?", Assert.Single(english.EnumerateArray()).GetProperty("stem").GetString());
    }

    [Fact]
    public async Task Tagged_level_and_picks_limit_the_draw()
    {
        var s = await SetupAsync("Tagged Draw School");
        var otherLevel = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "SSS 1", stage = "Sss", order = 20, hasStudentLogins = true }, Ct))).GetProperty("id").GetGuid();
        var untagged = await BankAsync(s, 3);
        var tagged = await BankAsync(s, 2, s.LevelId);
        var elsewhere = await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions", Question(s.MathsSubjectId, "What is 9 + 9?", otherLevel), Ct));
        Assert.Equal(otherLevel, elsewhere.GetProperty("levelId").GetGuid());

        var forLevel = await Json(await s.Teacher.GetAsync($"/api/school/questions?subjectId={s.MathsSubjectId}&levelId={s.LevelId}", Ct));
        Assert.Equal(2, forLevel.GetArrayLength());
        Assert.All(forLevel.EnumerateArray(), row => Assert.Equal(s.LevelId, row.GetProperty("levelId").GetGuid()));

        var tooMany = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 6), Ct), HttpStatusCode.BadRequest);
        Assert.Equal(NotEnough, tooMany.GetProperty("detail").GetString());

        var picks = untagged.Concat(tagged).Take(3).ToArray();
        var over = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 3, picks), Ct), HttpStatusCode.BadRequest);
        Assert.Equal(NotEnough, over.GetProperty("detail").GetString());

        var foreignPick = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 0, [elsewhere.GetProperty("id").GetGuid()]), Ct), HttpStatusCode.BadRequest);
        Assert.NotEqual(NotEnough, foreignPick.GetProperty("detail").GetString());

        var response = await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 2, picks), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {body}");
        var paper = JsonDocument.Parse(body).RootElement;
        Assert.Equal(2, paper.GetProperty("drawCount").GetInt32());
        Assert.Equal(picks, paper.GetProperty("pickQuestionIds").EnumerateArray().Select(x => x.GetGuid()).ToArray());

        var pickOnly = await s.School.Owner.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 0, tagged.ToArray()), Ct);
        Assert.Equal(HttpStatusCode.Created, pickOnly.StatusCode);
    }

    [Fact]
    public async Task Sitting_window_replaces_open_and_close()
    {
        var s = await SetupAsync("Sitting Window School");
        await BankAsync(s, 1);
        var periodId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/exam-periods", new
        {
            termId = s.TermId, name = "First term examination", kind = "Examination", start = ExamDay, end = ExamDay,
        }, Ct))).GetProperty("id").GetGuid();
        var sitting = await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/exam-periods/{periodId}/sittings", new
        {
            classSubjectId = s.MathsId, date = ExamDay, start = "09:00", end = "11:00", durationMinutes = 120,
            venue = "Hall A", instructions = "Answer every question.",
        }, Ct));
        var sittingId = sitting.GetProperty("id").GetGuid();

        var response = await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers",
            Paper(s, 1, examSittingId: sittingId, openAt: OtherOpen, closeAt: OtherClose, window: false), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{(int)response.StatusCode} {body}");
        var paper = JsonDocument.Parse(body).RootElement;
        Assert.Equal(sittingId, paper.GetProperty("examSittingId").GetGuid());
        Assert.Equal(WindowOpen, paper.GetProperty("openAt").GetDateTimeOffset());
        Assert.Equal(WindowClose, paper.GetProperty("closeAt").GetDateTimeOffset());

        var missingWindow = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1, window: false), Ct), HttpStatusCode.BadRequest);
        Assert.Equal(WindowRequired, missingWindow.GetProperty("detail").GetString());

        var unknownSitting = await ProblemAsync(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1, examSittingId: Guid.NewGuid(), window: false), Ct), HttpStatusCode.NotFound);
        Assert.Equal("This timetable was not found.", unknownSitting.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Developmental_class_cannot_have_a_paper()
    {
        var school = await kit.SchoolAsync("Developmental Paper School");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        Assert.Equal("Developmental", level.GetProperty("assessmentMode").GetString());
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Numeracy", code = "NUM" }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid() }, Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var subjectId = subject.GetProperty("id").GetGuid();
        await Json(await school.Owner.PostAsJsonAsync("/api/school/questions", Question(subjectId, "How many legs does a cat have?", correctIndex: 0), Ct));

        var problem = await ProblemAsync(await school.Owner.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId = link.GetProperty("id").GetGuid(),
            termId,
            title = "Nursery practice",
            durationMinutes = 20,
            openAt = WindowOpen,
            closeAt = WindowClose,
            drawCount = 1,
            pickQuestionIds = Array.Empty<Guid>(),
        }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(Developmental, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Foreign_paper_is_404()
    {
        var s = await SetupAsync("Foreign Paper School");
        await BankAsync(s, 1);
        var created = await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var paperId = (await created.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();

        var missing = await ProblemAsync(await s.School.Owner.PostAsync($"/api/school/cbt/papers/{Guid.NewGuid()}/publish", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(TestMissing, missing.GetProperty("detail").GetString());
        var other = await ProblemAsync(await s.OtherTeacher.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(TestMissing, other.GetProperty("detail").GetString());
        var otherPaper = await ProblemAsync(await s.OtherTeacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1), Ct), HttpStatusCode.NotFound);
        Assert.Equal("Class subject was not found.", otherPaper.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Subject_teacher_publishes()
    {
        var s = await SetupAsync("Publish Paper School");
        await BankAsync(s, 1);
        var created = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1), Ct));
        var paperId = created.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, created.GetProperty("publishedAt").ValueKind);

        var published = await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct));
        Assert.Equal(paperId, published.GetProperty("id").GetGuid());
        var at = published.GetProperty("publishedAt").GetDateTimeOffset();
        var again = await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct));
        Assert.Equal(at, again.GetProperty("publishedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Bursar_is_refused_before_lookup()
    {
        var s = await SetupAsync("Bursar Cbt School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await BankAsync(s, 1);
        var paperId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1), Ct))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/questions", Question(Guid.NewGuid(), "What is 1 + 1?"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/questions?subjectId={Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/questions?subjectId={s.MathsSubjectId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/cbt/papers/{Guid.NewGuid()}/publish", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/cbt/papers/{paperId}/release", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/cbt/papers/{Guid.NewGuid()}/release", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/cbt/attempts/{Guid.NewGuid()}/reopen", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Student_payload_omits_the_key()
    {
        var s = await SetupAsync("Omit Key School");
        var questionId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "Which way is north?", correctIndex: 2, options: ["A", "B", "C", "D"]), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [questionId]));
        var student = await PupilAsync(s, "Ada");

        var response = await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {raw}");
        Assert.DoesNotContain("correctIndex", raw, StringComparison.OrdinalIgnoreCase);
        var body = JsonDocument.Parse(raw).RootElement;
        AssertNoAnswerKey(body);
        Assert.NotEqual(Guid.Empty, body.GetProperty("attemptId").GetGuid());
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Which way is north?", item.GetProperty("stem").GetString());
        Assert.Equal(["A", "B", "C", "D"], item.GetProperty("options").EnumerateArray().Select(o => o.GetString()).OrderBy(o => o).ToArray());
        Assert.InRange((body.GetProperty("deadlineAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalMinutes, 39, 41);
    }

    [Fact]
    public async Task Shuffle_changes_item_or_option_order()
    {
        var s = await SetupAsync("Shuffle School");
        var stems = new HashSet<string>();
        for (var i = 1; i <= 5; i++)
        {
            var stem = $"Shuffle item {i}";
            stems.Add(stem);
            await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
                Question(s.MathsSubjectId, stem, options: ["a", "b", "c", "d"]), Ct));
        }
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 5));

        var differed = false;
        for (var n = 0; n < 5 && !differed; n++)
        {
            var one = await Json(await (await PupilAsync(s, $"Ada{n}")).PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
            var two = await Json(await (await PupilAsync(s, $"Bola{n}")).PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
            Assert.Equal(stems, one.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("stem").GetString()!).ToHashSet());
            Assert.Equal(stems, two.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("stem").GetString()!).ToHashSet());
            var left = one.GetProperty("items").EnumerateArray().Select(Arrangement).ToArray();
            var right = two.GetProperty("items").EnumerateArray().Select(Arrangement).ToArray();
            differed = !left.SequenceEqual(right);
        }
        Assert.True(differed);
    }

    [Fact]
    public async Task Time_up_saves_the_last_answer()
    {
        var s = await SetupAsync("Time Up School");
        await BankAsync(s, 1);
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 1));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        var item = Assert.Single(started.GetProperty("items").EnumerateArray());
        var itemId = item.GetProperty("id").GetGuid();
        var right = IndexOfOption(item, "4");
        var wrong = IndexOfOption(item, "8");

        await Json(await student.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers", new { itemId, chosenIndex = right }, Ct));
        var past = DateTimeOffset.UtcNow.AddMinutes(-1);
        await api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cbt_attempts SET deadline_at = {past} WHERE id = {attemptId}", Ct));

        var problem = await ProblemAsync(await student.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers",
            new { itemId, chosenIndex = wrong }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(TimeUp, problem.GetProperty("detail").GetString());

        // Reading the attempt after time is up marks it from the last saved answer (final review ruling: timed-out attempts are marked).
        var view = await Json(await student.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct));
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("submittedAt").ValueKind);
        Assert.Equal(2m, view.GetProperty("totalMarks").GetDecimal());
        Assert.Equal(right, await api.WithDbAsync(db => db.CbtAttemptItems.IgnoreQueryFilters().Where(i => i.Id == itemId).Select(i => i.ChosenIndex).SingleAsync(Ct)));

        var again = await ProblemAsync(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal(HandedIn, again.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_late_start_ends_at_the_close()
    {
        var s = await SetupAsync("Late Start School");
        await BankAsync(s, 1);
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 1, durationMinutes: 40,
            openAt: DateTimeOffset.UtcNow.AddHours(-1), closeAt: DateTimeOffset.UtcNow.AddMinutes(10)));
        var closeAt = await api.WithDbAsync(db => db.CbtPapers.IgnoreQueryFilters().Where(p => p.Id == paperId).Select(p => p.CloseAt).SingleAsync(Ct));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        Assert.Equal(closeAt, started.GetProperty("deadlineAt").GetDateTimeOffset());
        var itemId = Assert.Single(started.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();

        // Fifteen minutes pass: the paper has closed five minutes ago, and its key is out.
        var shift = TimeSpan.FromMinutes(15);
        await api.WithDbAsync(db => db.CbtPapers.IgnoreQueryFilters().Where(p => p.Id == paperId).ExecuteUpdateAsync(setters => setters
            .SetProperty(p => p.OpenAt, p => p.OpenAt - shift).SetProperty(p => p.CloseAt, p => p.CloseAt - shift), Ct));
        await api.WithDbAsync(db => db.CbtAttempts.IgnoreQueryFilters().Where(a => a.Id == attemptId).ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.StartedAt, a => a.StartedAt - shift).SetProperty(a => a.DeadlineAt, a => a.DeadlineAt - shift), Ct));

        var problem = await ProblemAsync(await student.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers",
            new { itemId, chosenIndex = 0 }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(TimeUp, problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_hidden_or_foreign_test_is_not_found()
    {
        var s = await SetupAsync("Hidden Cbt School");
        await BankAsync(s, 1);
        var student = await PupilAsync(s, "Ada");
        var classmate = await PupilAsync(s, "Bola");
        var wrongClass = await PupilAsync(s, "Kemi", await kit.ClassAsync(s.School, "B"));
        var elsewhere = await kit.SchoolAsync("Hidden Cbt Other School");
        var outsider = await kit.StudentLoginAsync(elsewhere, await kit.StudentAsync(elsewhere, await kit.ClassAsync(elsewhere), "Titi"));

        var unpublishedId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", SittingPaper(s, 1), Ct))).GetProperty("id").GetGuid();
        var earlyId = await PublishSittingAsync(s, SittingPaper(s, 1, openAt: DateTimeOffset.UtcNow.AddHours(2), closeAt: DateTimeOffset.UtcNow.AddHours(4)));
        var lateId = await PublishSittingAsync(s, SittingPaper(s, 1, openAt: DateTimeOffset.UtcNow.AddHours(-4), closeAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        var liveId = await PublishSittingAsync(s, SittingPaper(s, 1));

        foreach (var (client, paperId) in new[]
        {
            (student, unpublishedId), (student, earlyId), (student, lateId), (student, Guid.NewGuid()),
            (wrongClass, liveId), (outsider, liveId),
        })
        {
            var problem = await ProblemAsync(await client.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct), HttpStatusCode.NotFound);
            Assert.Equal(TestMissing, problem.GetProperty("detail").GetString());
        }

        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{liveId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        var itemId = Assert.Single(started.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        foreach (var client in new[] { classmate, outsider })
        {
            Assert.Equal(TestMissing, (await ProblemAsync(await client.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
            Assert.Equal(TestMissing, (await ProblemAsync(await client.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers", new { itemId, chosenIndex = 0 }, Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
            Assert.Equal(TestMissing, (await ProblemAsync(await client.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        }
        Assert.Equal(TestMissing, (await ProblemAsync(await student.GetAsync($"/api/student/cbt/attempts/{Guid.NewGuid()}", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.School.Owner.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Counted_cbt_matches_typed_score()
    {
        var s = await SetupAsync("Counted Cbt School");
        var examId = await ExamIdAsync(s);
        var questionId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "Which letter is the answer?", marks: 60, options: ["w", "x", "y", "z"]), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [questionId], examId));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        await AnswerAsync(student, attemptId, Assert.Single(started.GetProperty("items").EnumerateArray()), "x");
        await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));

        var grid = await Json(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.MathsId}&termId={s.TermId}", Ct));
        var ada = grid.GetProperty("students").EnumerateArray().Single(row => row.GetProperty("name").GetString() == "Ada Obi");
        Assert.Equal(60m, ada.GetProperty("values").GetProperty(examId.ToString()).GetDecimal());
        var enrolmentId = ada.GetProperty("enrolmentId").GetGuid();
        var scoreId = await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters()
            .Where(score => score.TenantId == s.School.TenantId && score.ComponentId == examId && score.EnrolmentId == enrolmentId)
            .Select(score => score.Id).SingleAsync(Ct));

        (await s.Teacher.PutAsJsonAsync("/api/school/scores", new
        {
            classSubjectId = s.MathsId, termId = s.TermId,
            entries = new[] { new { enrolmentId, componentId = examId, value = 60m } },
        }, Ct)).EnsureSuccessStatusCode();
        var typedId = await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters()
            .Where(score => score.TenantId == s.School.TenantId && score.ComponentId == examId && score.EnrolmentId == enrolmentId && score.Value == 60m)
            .Select(score => score.Id).SingleAsync(Ct));
        Assert.Equal(scoreId, typedId);
    }

    [Fact]
    public async Task Counted_cbt_scales_onto_the_component()
    {
        var s = await SetupAsync("Scaled Cbt School");
        var examId = await ExamIdAsync(s);
        var first = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "First scaled item", marks: 20, options: ["w", "x", "y", "z"]), Ct))).GetProperty("id").GetGuid();
        var second = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "Second scaled item", marks: 20, options: ["p", "q", "r", "s"]), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [first, second], examId));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        foreach (var item in started.GetProperty("items").EnumerateArray())
        {
            var stem = item.GetProperty("stem").GetString();
            await AnswerAsync(student, attemptId, item, stem == "First scaled item" ? "x" : "p");
        }
        await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));

        var grid = await Json(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.MathsId}&termId={s.TermId}", Ct));
        var ada = grid.GetProperty("students").EnumerateArray().Single(row => row.GetProperty("name").GetString() == "Ada Obi");
        Assert.Equal(30m, ada.GetProperty("values").GetProperty(examId.ToString()).GetDecimal());
    }

    [Fact]
    public async Task Practice_submit_does_not_write_a_score()
    {
        var s = await SetupAsync("Practice Cbt School");
        var examId = await ExamIdAsync(s);
        await BankAsync(s, 1);
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 1));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        await AnswerAsync(student, attemptId, Assert.Single(started.GetProperty("items").EnumerateArray()), "4");
        await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));

        var view = await Json(await student.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct));
        Assert.Equal(2m, view.GetProperty("totalMarks").GetDecimal());
        Assert.False(view.TryGetProperty("items", out _));
        var grid = await Json(await s.Teacher.GetAsync($"/api/school/scores?classSubjectId={s.MathsId}&termId={s.TermId}", Ct));
        var ada = grid.GetProperty("students").EnumerateArray().Single(row => row.GetProperty("name").GetString() == "Ada Obi");
        Assert.Equal(JsonValueKind.Null, ada.GetProperty("values").GetProperty(examId.ToString()).ValueKind);
        Assert.Equal(0, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(score => score.TenantId == s.School.TenantId, Ct)));
    }

    [Fact]
    public async Task The_key_stays_hidden_until_release_or_the_window_ends()
    {
        var s = await SetupAsync("Key Release School");
        var questionId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "Which option is shown?", options: ["w", "x", "y", "z"]), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [questionId]));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        await AnswerAsync(student, attemptId, Assert.Single(started.GetProperty("items").EnumerateArray()), "x");
        await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));

        var hidden = await student.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct);
        var hiddenRaw = await hidden.Content.ReadAsStringAsync(Ct);
        Assert.True(hidden.IsSuccessStatusCode, hiddenRaw);
        Assert.DoesNotContain("correct", hiddenRaw, StringComparison.OrdinalIgnoreCase);
        var hiddenBody = JsonDocument.Parse(hiddenRaw).RootElement;
        Assert.Equal(2m, hiddenBody.GetProperty("totalMarks").GetDecimal());
        Assert.False(hiddenBody.TryGetProperty("items", out _));

        var refused = await ProblemAsync(await s.OtherTeacher.PostAsync($"/api/school/cbt/papers/{paperId}/release", null, Ct), HttpStatusCode.NotFound);
        Assert.Equal(TestMissing, refused.GetProperty("detail").GetString());
        await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{paperId}/release", null, Ct));

        var released = await student.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct);
        var releasedRaw = await released.Content.ReadAsStringAsync(Ct);
        Assert.True(released.IsSuccessStatusCode, releasedRaw);
        Assert.DoesNotContain("correctIndex", releasedRaw, StringComparison.OrdinalIgnoreCase);
        var marked = Assert.Single(JsonDocument.Parse(releasedRaw).RootElement.GetProperty("items").EnumerateArray());
        Assert.True(marked.GetProperty("correct").GetBoolean());
        Assert.Equal(2m, marked.GetProperty("marksAwarded").GetDecimal());
        Assert.False(marked.TryGetProperty("correctIndex", out _));

        var otherQuestion = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "Window end item", options: ["w", "x", "y", "z"]), Ct))).GetProperty("id").GetGuid();
        var windowId = await PublishSittingAsync(s, SittingPaper(s, 0, [otherQuestion]));
        var second = await PupilAsync(s, "Bola");
        var secondStart = await Json(await second.PostAsync($"/api/student/cbt/papers/{windowId}/start", null, Ct));
        var secondAttempt = secondStart.GetProperty("attemptId").GetGuid();
        await AnswerAsync(second, secondAttempt, Assert.Single(secondStart.GetProperty("items").EnumerateArray()), "x");
        await Json(await second.PostAsync($"/api/student/cbt/attempts/{secondAttempt}/submit", null, Ct));
        var beforeEnd = await second.GetAsync($"/api/student/cbt/attempts/{secondAttempt}", Ct);
        Assert.DoesNotContain("correct", await beforeEnd.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);

        var ended = DateTimeOffset.UtcNow.AddMinutes(-1);
        await api.WithDbAsync(db => db.CbtPapers.IgnoreQueryFilters().Where(p => p.Id == windowId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.CloseAt, ended), Ct));
        var afterEnd = await second.GetAsync($"/api/student/cbt/attempts/{secondAttempt}", Ct);
        var afterRaw = await afterEnd.Content.ReadAsStringAsync(Ct);
        Assert.True(afterEnd.IsSuccessStatusCode, afterRaw);
        Assert.DoesNotContain("correctIndex", afterRaw, StringComparison.OrdinalIgnoreCase);
        Assert.True(Assert.Single(JsonDocument.Parse(afterRaw).RootElement.GetProperty("items").EnumerateArray()).GetProperty("correct").GetBoolean());
    }

    [Fact]
    public async Task A_locked_term_blocks_a_counted_start()
    {
        var s = await SetupAsync("Locked Cbt School");
        var examId = await ExamIdAsync(s);
        await BankAsync(s, 1);
        var countedId = await PublishSittingAsync(s, SittingPaper(s, 1, assessmentComponentId: examId));
        var practiceId = await PublishSittingAsync(s, SittingPaper(s, 1));
        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();
        var student = await PupilAsync(s, "Ada");

        var locked = await ProblemAsync(await student.PostAsync($"/api/student/cbt/papers/{countedId}/start", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal(TermLocked, locked.GetProperty("detail").GetString());
        Assert.Equal("term_locked", locked.GetProperty("code").GetString());
        var practice = await Json(await student.PostAsync($"/api/student/cbt/papers/{practiceId}/start", null, Ct));
        Assert.NotEqual(Guid.Empty, practice.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task A_second_start_needs_a_reopen()
    {
        var s = await SetupAsync("Reopen Cbt School");
        await BankAsync(s, 1);
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 1));
        var student = await PupilAsync(s, "Ada");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();

        var again = await ProblemAsync(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal(AlreadyStarted, again.GetProperty("detail").GetString());
        await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));
        var handed = await ProblemAsync(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal(HandedIn, handed.GetProperty("detail").GetString());
        var afterSubmit = await ProblemAsync(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal(AlreadyStarted, afterSubmit.GetProperty("detail").GetString());

        Assert.Equal(TestMissing, (await ProblemAsync(await s.OtherTeacher.PostAsync($"/api/school/cbt/attempts/{attemptId}/reopen", null, Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/cbt/attempts/{attemptId}/reopen", null, Ct)).StatusCode);
        await Json(await s.Teacher.PostAsync($"/api/school/cbt/attempts/{attemptId}/reopen", null, Ct));
        var restarted = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        Assert.NotEqual(attemptId, restarted.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task Start_keeps_picks_and_draws_the_rest()
    {
        var s = await SetupAsync("Pick Draw School");
        var ids = await BankAsync(s, 8);
        var picks = ids.Take(3).ToArray();
        var pickStems = new List<string>();
        for (var i = 1; i <= 3; i++) pickStems.Add($"What is {i} + {i}?");
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 2, picks));
        var started = await Json(await (await PupilAsync(s, "Ada")).PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var stems = started.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("stem").GetString()).ToList();
        Assert.Equal(5, stems.Count);
        Assert.Equal(5, stems.Distinct().Count());
        Assert.All(pickStems, stem => Assert.Contains(stem, stems));
    }

    [Fact]
    public async Task An_archived_question_does_not_change_the_attempt()
    {
        var s = await SetupAsync("Snapshot Cbt School");
        var questionId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "Original stem"), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [questionId]));
        var student = await PupilAsync(s, "Ada");
        var started = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        await api.WithDbAsync(db => db.BankQuestions.IgnoreQueryFilters().Where(q => q.Id == questionId).ExecuteUpdateAsync(
            setters => setters.SetProperty(q => q.Stem, "Changed after the sitting started").SetProperty(q => q.Archived, true), Ct));

        var view = await Json(await student.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct));
        Assert.Equal("Original stem", Assert.Single(view.GetProperty("items").EnumerateArray()).GetProperty("stem").GetString());
    }

    private static async Task<JsonElement> StudentPaperAsync(HttpClient student, Guid paperId)
    {
        var response = await student.GetAsync("/api/student/cbt/papers", Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, raw);
        Assert.DoesNotContain("correct", raw, StringComparison.OrdinalIgnoreCase);
        var list = JsonDocument.Parse(raw).RootElement.Clone();
        AssertNoAnswerKey(list);
        return list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == paperId);
    }

    [Fact]
    public async Task Student_list_shows_own_attempt_and_never_the_key()
    {
        var s = await SetupAsync("Student Paper List School");
        var questionId = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions", Question(s.MathsSubjectId, "What is 2 + 2?"), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [questionId]));
        var draft = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", SittingPaper(s, 0, [questionId]), Ct))).GetProperty("id").GetGuid();
        var ada = await PupilAsync(s, "Ada");
        var bola = await PupilAsync(s, "Bola");

        var listed = await Json(await ada.GetAsync("/api/student/cbt/papers", Ct));
        Assert.DoesNotContain(listed.EnumerateArray(), p => p.GetProperty("id").GetGuid() == draft);
        var before = await StudentPaperAsync(ada, paperId);
        Assert.Equal("First term mathematics test", before.GetProperty("title").GetString());
        Assert.Equal("Mathematics", before.GetProperty("subject").GetString());
        Assert.Equal(40, before.GetProperty("durationMinutes").GetInt32());
        Assert.Equal("NotStarted", before.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("attemptId").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("totalMarks").ValueKind);

        var started = await Json(await ada.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        var sitting = await StudentPaperAsync(ada, paperId);
        Assert.Equal("InProgress", sitting.GetProperty("status").GetString());
        Assert.Equal(attemptId, sitting.GetProperty("attemptId").GetGuid());
        Assert.Equal(JsonValueKind.Null, sitting.GetProperty("totalMarks").ValueKind);

        await AnswerAsync(ada, attemptId, Assert.Single(started.GetProperty("items").EnumerateArray()), "4");
        await Json(await ada.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));
        var handed = await StudentPaperAsync(ada, paperId);
        Assert.Equal("Submitted", handed.GetProperty("status").GetString());
        Assert.Equal(attemptId, handed.GetProperty("attemptId").GetGuid());
        Assert.Equal(2m, handed.GetProperty("totalMarks").GetDecimal());

        // Another student sees the paper, not Ada's attempt.
        var other = await StudentPaperAsync(bola, paperId);
        Assert.Equal("NotStarted", other.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("attemptId").ValueKind);

        // After a reopen the paper can be started again.
        await Json(await s.Teacher.PostAsync($"/api/school/cbt/attempts/{attemptId}/reopen", null, Ct));
        Assert.Equal("NotStarted", (await StudentPaperAsync(ada, paperId)).GetProperty("status").GetString());

        // Staff tokens are not students.
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Teacher.GetAsync("/api/student/cbt/papers", Ct)).StatusCode);
    }

    [Fact]
    public async Task Staff_paper_list_is_scoped_to_the_class_subject()
    {
        var s = await SetupAsync("Staff Paper List School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var ids = await BankAsync(s, 3);
        var first = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", Paper(s, 1), Ct))).GetProperty("id").GetGuid();
        var second = await PublishSittingAsync(s, SittingPaper(s, 0, [ids[0]]));
        var englishQuestion = (await Json(await s.OtherTeacher.PostAsJsonAsync("/api/school/questions", Question(s.EnglishSubjectId, "Pick the noun."), Ct))).GetProperty("id").GetGuid();
        var english = (await Json(await s.OtherTeacher.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId = s.EnglishId, termId = s.TermId, title = "English test", durationMinutes = 20,
            openAt = WindowOpen, closeAt = WindowClose, drawCount = 0, pickQuestionIds = new[] { englishQuestion },
        }, Ct))).GetProperty("id").GetGuid();

        var maths = await Json(await s.Teacher.GetAsync($"/api/school/cbt/papers?classSubjectId={s.MathsId}&termId={s.TermId}", Ct));
        Assert.Equal(new[] { first, second }.Order(), maths.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Order());
        Assert.All(maths.EnumerateArray(), p => Assert.Equal(s.MathsId, p.GetProperty("classSubjectId").GetGuid()));
        var published = maths.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == second);
        Assert.NotEqual(JsonValueKind.Null, published.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, published.GetProperty("releasedKeyAt").ValueKind);
        Assert.Equal([ids[0]], published.GetProperty("pickQuestionIds").EnumerateArray().Select(p => p.GetGuid()).ToArray());

        await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{second}/release", null, Ct));
        var afterRelease = await Json(await s.School.Owner.GetAsync($"/api/school/cbt/papers?classSubjectId={s.MathsId}&termId={s.TermId}", Ct));
        Assert.NotEqual(JsonValueKind.Null, afterRelease.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == second).GetProperty("releasedKeyAt").ValueKind);

        var englishList = await Json(await s.OtherTeacher.GetAsync($"/api/school/cbt/papers?classSubjectId={s.EnglishId}&termId={s.TermId}", Ct));
        Assert.Equal(english, Assert.Single(englishList.EnumerateArray()).GetProperty("id").GetGuid());
        var foreign = await ProblemAsync(await s.Teacher.GetAsync($"/api/school/cbt/papers?classSubjectId={s.EnglishId}&termId={s.TermId}", Ct), HttpStatusCode.NotFound);
        Assert.Equal("Class subject was not found.", foreign.GetProperty("detail").GetString());
        Assert.Empty((await Json(await s.Teacher.GetAsync($"/api/school/cbt/papers?classSubjectId={s.MathsId}&termId={Guid.NewGuid()}", Ct))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/cbt/papers?classSubjectId={Guid.NewGuid()}&termId={s.TermId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Attempts_list_shows_per_question_marks()
    {
        var s = await SetupAsync("Attempts List School");
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var right = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions", Question(s.MathsSubjectId, "What is 2 + 2?"), Ct))).GetProperty("id").GetGuid();
        var wrong = (await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            Question(s.MathsSubjectId, "What is 3 + 3?", correctIndex: 2, marks: 3), Ct))).GetProperty("id").GetGuid();
        var paperId = await PublishSittingAsync(s, SittingPaper(s, 0, [right, wrong]));
        var ada = await PupilAsync(s, "Ada");
        await PupilAsync(s, "Bola");

        var started = await Json(await ada.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = started.GetProperty("attemptId").GetGuid();
        foreach (var item in started.GetProperty("items").EnumerateArray())
            await AnswerAsync(ada, attemptId, item, item.GetProperty("stem").GetString() == "What is 2 + 2?" ? "4" : "2");

        var sitting = await Json(await s.Teacher.GetAsync($"/api/school/cbt/papers/{paperId}/attempts", Ct));
        var open = Assert.Single(sitting.GetProperty("students").EnumerateArray().Single(r => r.GetProperty("name").GetString() == "Ada Obi").GetProperty("attempts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, open.GetProperty("submittedAt").ValueKind);
        Assert.All(open.GetProperty("items").EnumerateArray(), i => Assert.Equal(JsonValueKind.Null, i.GetProperty("marksAwarded").ValueKind));

        await Json(await ada.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));
        var view = await Json(await s.Teacher.GetAsync($"/api/school/cbt/papers/{paperId}/attempts", Ct));
        Assert.Equal(paperId, view.GetProperty("paperId").GetGuid());
        var rows = view.GetProperty("students").EnumerateArray().ToList();
        Assert.Equal(["Ada Obi", "Bola Obi"], rows.Select(r => r.GetProperty("name").GetString()).Order().ToArray());
        Assert.Empty(rows.Single(r => r.GetProperty("name").GetString() == "Bola Obi").GetProperty("attempts").EnumerateArray());
        var adaRow = rows.Single(r => r.GetProperty("name").GetString() == "Ada Obi");
        Assert.False(string.IsNullOrEmpty(adaRow.GetProperty("admissionNo").GetString()));
        var attempt = Assert.Single(adaRow.GetProperty("attempts").EnumerateArray());
        Assert.Equal(attemptId, attempt.GetProperty("attemptId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, attempt.GetProperty("submittedAt").ValueKind);
        Assert.Equal(2m, attempt.GetProperty("totalMarks").GetDecimal());
        Assert.Equal(5m, attempt.GetProperty("paperMarks").GetDecimal());
        var marks = attempt.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("stem").GetString()!, i => (i.GetProperty("marks").GetDecimal(), i.GetProperty("marksAwarded").GetDecimal()));
        Assert.Equal((2m, 2m), marks["What is 2 + 2?"]);
        Assert.Equal((3m, 0m), marks["What is 3 + 3?"]);

        Assert.Equal(TestMissing, (await ProblemAsync(await s.OtherTeacher.GetAsync($"/api/school/cbt/papers/{paperId}/attempts", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal(TestMissing, (await ProblemAsync(await s.Teacher.GetAsync($"/api/school/cbt/papers/{Guid.NewGuid()}/attempts", Ct), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/cbt/papers/{Guid.NewGuid()}/attempts", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.School.Owner.GetAsync($"/api/school/cbt/papers/{paperId}/attempts", Ct)).StatusCode);
    }
}
