using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1 gaps in the school specs: nursery results in the portals, the parent's own phone and email, CBT class performance, removing a
/// level's own trait list, editing and archiving Play items, and editing and deleting exam periods.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolWaveOneTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private Task<Guid> CurrentTermAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

    private Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private Task<Guid> LevelOfAsync(Guid classId) =>
        api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));

    // ---------- 1. Nursery results and trait ratings in the portals ----------

    [Fact]
    public async Task Nursery_results_and_traits_reach_the_parent_and_student_portals()
    {
        var school = await kit.SchoolAsync("Wave Nursery Portal School");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 2", stage = "Nursery", order = 3, hasStudentLogins = true }, Ct));
        var levelId = level.GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var termId = await CurrentTermAsync(school);
        var ada = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var bola = await kit.StudentAsync(school, classId, "Bola", "Zed");
        var guardianId = await kit.GuardianAsync(school, ada, "Ada Parent", null, $"ada-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);

        var areas = await Json(await school.Owner.GetAsync($"/api/school/developmental/areas?levelId={levelId}", Ct));
        var scale = await Json(await school.Owner.GetAsync($"/api/school/developmental/scale?levelId={levelId}", Ct));
        Guid Scale(string label) => scale.GetProperty("scale").EnumerateArray().Single(s => s.GetProperty("label").GetString() == label).GetProperty("id").GetGuid();
        var language = areas.GetProperty("areas").EnumerateArray().Single(a => a.GetProperty("name").GetString() == "Language").GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{ada}/developmental?termId={termId}",
            new { ratings = new[] { new { areaId = language, scaleId = Scale("Excellent"), comment = "Speaks in short sentences." } } }, Ct));
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{bola}/developmental?termId={termId}",
            new { ratings = new[] { new { areaId = language, scaleId = Scale("Developing"), comment = "Bola's own comment." } } }, Ct));
        var affective = await Json(await school.Owner.GetAsync($"/api/school/traits?kind=Affective&levelId={levelId}", Ct));
        var punctuality = affective.GetProperty("criteria").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Punctuality").GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{ada}/traits?termId={termId}&kind=Affective",
            new { ratings = new[] { new { criterionId = punctuality, value = 4 } } }, Ct));

        var parentUrl = $"/api/parent/children/{ada}/results/{termId}";
        var before = await Json(await parent.GetAsync(parentUrl, Ct));
        Assert.False(before.GetProperty("published").GetBoolean());
        Assert.Empty(before.GetProperty("entries").EnumerateArray());

        var resultsUrl = $"/api/school/results/{classId}/{termId}";
        (await school.Owner.PostAsync($"{resultsUrl}/compute", null, Ct)).EnsureSuccessStatusCode();
        await Json(await school.Owner.PostAsync($"{resultsUrl}/lock", null, Ct));
        await Json(await school.Owner.PostAsync($"{resultsUrl}/publish", null, Ct));

        var student = await kit.StudentLoginAsync(school, ada);
        foreach (var body in new[] { await Json(await parent.GetAsync(parentUrl, Ct)), await Json(await student.GetAsync($"/api/student/results/{termId}", Ct)) })
        {
            var entry = Assert.Single(body.GetProperty("entries").EnumerateArray());
            Assert.Equal("Developmental", entry.GetProperty("mode").GetString());
            Assert.Empty(entry.GetProperty("subjects").EnumerateArray());
            var rows = entry.GetProperty("developmental").EnumerateArray().ToList();
            Assert.Equal(8, rows.Count);
            var lang = rows.Single(r => r.GetProperty("area").GetString() == "Language");
            Assert.Equal("Excellent", lang.GetProperty("scale").GetString());
            Assert.Equal("Speaks in short sentences.", lang.GetProperty("comment").GetString());
            Assert.DoesNotContain(rows, r => r.GetProperty("comment").ValueKind == JsonValueKind.String && r.GetProperty("comment").GetString() == "Bola's own comment.");
            var unrated = rows.Single(r => r.GetProperty("area").GetString() == "Motor");
            Assert.Equal(JsonValueKind.Null, unrated.GetProperty("scale").ValueKind);

            var block = Assert.Single(entry.GetProperty("traits").EnumerateArray());
            Assert.Equal("Affective", block.GetProperty("heading").GetString());
            var trait = Assert.Single(block.GetProperty("rows").EnumerateArray());
            Assert.Equal("Punctuality", trait.GetProperty("name").GetString());
            Assert.Equal(4, trait.GetProperty("value").GetInt32());
        }

        // Unpublished again: nothing reaches the families.
        await Json(await school.Owner.PostAsync($"{resultsUrl}/unpublish", null, Ct));
        Assert.Empty((await Json(await parent.GetAsync(parentUrl, Ct))).GetProperty("entries").EnumerateArray());
    }

    // ---------- 2. Parent profile: phone and email ----------

    [Fact]
    public async Task Parent_changes_phone_and_the_linked_guardian_follows()
    {
        var school = await kit.SchoolAsync("Wave Phone School");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ada Parent", "08030000001", $"ada-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);

        var profile = await Json(await parent.GetAsync("/api/parent/profile", Ct));
        Assert.Equal("Parent Account", profile.GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phone").ValueKind);

        var bad = await ProblemAsync(await parent.PutAsJsonAsync("/api/parent/profile/phone", new { phone = "12" }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal("Enter a phone number such as 0803 123 4567 or +234 803 123 4567.", bad.GetProperty("detail").GetString());

        var saved = await Json(await parent.PutAsJsonAsync("/api/parent/profile/phone", new { phone = "0803 123 4567" }, Ct));
        Assert.Equal("+2348031234567", saved.GetProperty("phone").GetString());
        Assert.Equal("+2348031234567", (await Json(await parent.GetAsync("/api/parent/profile", Ct))).GetProperty("phone").GetString());
        var guardianPhone = await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().Where(g => g.Id == guardianId).Select(g => g.Phone).FirstAsync(Ct));
        Assert.Equal("+2348031234567", guardianPhone);

        // Staff sessions are not parent sessions.
        Assert.Equal(HttpStatusCode.Forbidden, (await school.Owner.PutAsJsonAsync("/api/parent/profile/phone", new { phone = "08031234567" }, Ct)).StatusCode);
    }

    private Task<string> LatestChangeCodeAsync(string email) =>
        api.WithDbAsync(async db => (await db.Notifications.Where(n => n.TemplateKey == "email.change_code" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct)).Payload["code"]);

    [Fact]
    public async Task Email_change_needs_the_password_and_signs_out_every_other_session()
    {
        var (parent, userId, oldEmail) = await kit.ParentAccountAsync();
        var newEmail = $"moved-{Guid.NewGuid():N}@example.com";
        var otherDevice = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/parent/login", new { email = oldEmail, password = "password123" }, Ct));

        // A session alone (a stolen token, a shared phone) can't start the move.
        await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email", new { email = newEmail }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "wrong-password", email = newEmail }, Ct), HttpStatusCode.BadRequest);

        Assert.Equal(HttpStatusCode.NoContent,
            (await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = newEmail }, Ct)).StatusCode);
        var code = await LatestChangeCodeAsync(newEmail);
        await Json(await parent.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = newEmail, code }, Ct));

        // Every session from before the change is ended.
        var refresh = await api.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = otherDevice.GetProperty("refreshToken").GetString() }, Ct);
        Assert.False(refresh.IsSuccessStatusCode);
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Parent_changes_email_only_after_the_code_sent_to_the_new_address()
    {
        var school = await kit.SchoolAsync("Wave Email School");
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var guardianId = await kit.GuardianAsync(school, studentId, "Ada Parent", null, $"ada-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(school, guardianId);
        var oldEmail = (await Json(await parent.GetAsync("/api/parent/profile", Ct))).GetProperty("email").GetString()!;
        var newEmail = $"new-{Guid.NewGuid():N}@example.com";

        // An address that already belongs to another account is refused.
        var (_, _, takenEmail) = await kit.ParentAccountAsync();
        var taken = await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = takenEmail }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("Another account already uses this email address.", taken.GetProperty("detail").GetString());
        await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = "not-an-email" }, Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = oldEmail.ToUpperInvariant() }, Ct), HttpStatusCode.BadRequest);

        Assert.Equal(HttpStatusCode.NoContent, (await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = newEmail.ToUpperInvariant() }, Ct)).StatusCode);
        var code = await LatestChangeCodeAsync(newEmail);
        Assert.Matches("^[0-9]{6}$", code);
        // Nothing changes until the code is entered.
        Assert.Equal(oldEmail, (await Json(await parent.GetAsync("/api/parent/profile", Ct))).GetProperty("email").GetString());

        var wrong = code == "000000" ? "111111" : "000000";
        var refused = await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = newEmail, code = wrong }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal("That code is wrong or has expired. Request a new one.", refused.GetProperty("detail").GetString());
        // Another account can't use this account's code.
        var (stranger, _, _) = await kit.ParentAccountAsync();
        await ProblemAsync(await stranger.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = newEmail, code }, Ct), HttpStatusCode.BadRequest);

        var changed = await Json(await parent.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = newEmail, code }, Ct));
        Assert.Equal(newEmail, changed.GetProperty("email").GetString());
        Assert.Equal(newEmail, (await Json(await parent.GetAsync("/api/me", Ct))).GetProperty("user").GetProperty("email").GetString());
        var guardianEmail = await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().Where(g => g.Id == guardianId).Select(g => g.Email).FirstAsync(Ct));
        Assert.Equal(newEmail, guardianEmail);
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "email.changed" && n.Recipient == oldEmail, Ct)));

        // The code is spent, and the new address signs in with the same password.
        await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = newEmail, code }, Ct), HttpStatusCode.BadRequest);
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/parent/login", new { email = newEmail, password = "password123" }, Ct));
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/parent/login", new { email = oldEmail, password = "password123" }, Ct)).IsSuccessStatusCode);
    }

    // ---------- 3. CBT class performance ----------

    [Fact]
    public async Task Cbt_attempts_show_class_performance_and_question_success()
    {
        var school = await kit.SchoolAsync("Wave Cbt School");
        var classId = await kit.ClassAsync(school);
        var maths = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var subjectId = maths.GetProperty("id").GetGuid();
        var classSubjectId = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId }, Ct))).GetProperty("id").GetGuid();
        var termId = await CurrentTermAsync(school);
        Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
        var first = Id(await Json(await school.Owner.PostAsJsonAsync("/api/school/questions",
            new { subjectId, stem = "What is 2 + 2?", options = new[] { "2", "4", "6", "8" }, correctIndex = 1, marks = 2m }, Ct)));
        var second = Id(await Json(await school.Owner.PostAsJsonAsync("/api/school/questions",
            new { subjectId, stem = "What is 3 + 3?", options = new[] { "2", "4", "6", "8" }, correctIndex = 2, marks = 2m }, Ct)));
        var paperId = Id(await Json(await school.Owner.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId, termId, title = "Class test", durationMinutes = 30,
            openAt = DateTimeOffset.UtcNow.AddHours(-1), closeAt = DateTimeOffset.UtcNow.AddHours(3),
            drawCount = 0, pickQuestionIds = new[] { first, second },
        }, Ct)));
        await Json(await school.Owner.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct));

        async Task SitAsync(string name, string answerFirst, string answerSecond)
        {
            var pupil = await kit.StudentLoginAsync(school, await kit.StudentAsync(school, classId, name));
            var started = await Json(await pupil.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
            var attemptId = started.GetProperty("attemptId").GetGuid();
            foreach (var item in started.GetProperty("items").EnumerateArray())
            {
                var option = item.GetProperty("stem").GetString() == "What is 2 + 2?" ? answerFirst : answerSecond;
                var index = item.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToList().IndexOf(option);
                await Json(await pupil.PutAsJsonAsync($"/api/student/cbt/attempts/{attemptId}/answers", new { itemId = item.GetProperty("id").GetGuid(), chosenIndex = index }, Ct));
            }
            await Json(await pupil.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));
        }
        await SitAsync("Ada", "4", "6");   // 4 of 4
        await SitAsync("Bola", "4", "2");  // 2 of 4
        await kit.StudentAsync(school, classId, "Chidi"); // never starts

        var attempts = await Json(await school.Owner.GetAsync($"/api/school/cbt/papers/{paperId}/attempts", Ct));
        var performance = attempts.GetProperty("performance");
        Assert.Equal(2, performance.GetProperty("handedIn").GetInt32());
        Assert.Equal(3, performance.GetProperty("classSize").GetInt32());
        Assert.Equal(3m, performance.GetProperty("averageMarks").GetDecimal());
        Assert.Equal(4m, performance.GetProperty("highestMarks").GetDecimal());
        Assert.Equal(2m, performance.GetProperty("lowestMarks").GetDecimal());
        Assert.Equal(4m, performance.GetProperty("paperMarks").GetDecimal());
        var questions = performance.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(2, questions.Count);
        var q1 = questions.Single(q => q.GetProperty("stem").GetString() == "What is 2 + 2?");
        Assert.Equal(2, q1.GetProperty("handedIn").GetInt32());
        Assert.Equal(2, q1.GetProperty("correct").GetInt32());
        Assert.Equal(1m, q1.GetProperty("successRate").GetDecimal());
        Assert.Equal(2m, q1.GetProperty("averageMark").GetDecimal());
        var q2 = questions.Single(q => q.GetProperty("stem").GetString() == "What is 3 + 3?");
        Assert.Equal(1, q2.GetProperty("correct").GetInt32());
        Assert.Equal(0.5m, q2.GetProperty("successRate").GetDecimal());
        Assert.Equal(1m, q2.GetProperty("averageMark").GetDecimal());
        // The hardest question comes first.
        Assert.Equal("What is 3 + 3?", questions[0].GetProperty("stem").GetString());
    }

    // ---------- 4. Removing a level's own trait list ----------

    [Fact]
    public async Task Level_trait_override_can_be_removed_until_it_has_ratings()
    {
        var school = await kit.SchoolAsync("Wave Trait Reset School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var classId = await kit.ClassAsync(school);
        var levelId = await LevelOfAsync(classId);
        var termId = await CurrentTermAsync(school);
        var url = $"/api/school/traits?kind=Affective&levelId={levelId}";
        await Json(await school.Owner.PutAsJsonAsync(url, new { criteria = new[] { new { name = "Honesty" } } }, Ct));

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.DeleteAsync($"/api/school/traits/{levelId}?kind=Affective", Ct)).StatusCode);
        var other = await kit.SchoolAsync("Wave Trait Other School");
        await ProblemAsync(await other.Owner.DeleteAsync($"/api/school/traits/{levelId}?kind=Affective", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await school.Owner.DeleteAsync($"/api/school/traits/{levelId}", Ct), HttpStatusCode.BadRequest);

        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/traits/{levelId}?kind=Affective", Ct)).StatusCode);
        var back = await Json(await school.Owner.GetAsync(url, Ct));
        Assert.Equal(JsonValueKind.Null, back.GetProperty("levelId").ValueKind);
        Assert.Equal(5, back.GetProperty("criteria").GetArrayLength());
        // Removing it again is harmless.
        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/traits/{levelId}?kind=Affective", Ct)).StatusCode);

        // Once a child is rated on the level's own list, the list stays.
        var saved = await Json(await school.Owner.PutAsJsonAsync(url, new { criteria = new[] { new { name = "Kindness" } } }, Ct));
        var kindness = saved.GetProperty("criteria")[0].GetProperty("id").GetGuid();
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{studentId}/traits?termId={termId}&kind=Affective",
            new { ratings = new[] { new { criterionId = kindness, value = 5 } } }, Ct));
        var blocked = await ProblemAsync(await school.Owner.DeleteAsync($"/api/school/traits/{levelId}?kind=Affective", Ct), HttpStatusCode.Conflict);
        Assert.Equal("Children have already been rated on this level's own list, so it can't be removed. You can still rename its items.", blocked.GetProperty("detail").GetString());
        Assert.Equal(levelId, (await Json(await school.Owner.GetAsync(url, Ct))).GetProperty("levelId").GetGuid());
    }

    // ---------- 5. Play: edit and archive ----------

    [Fact]
    public async Task Teasers_and_word_lists_are_edited_and_archived()
    {
        var school = await kit.SchoolAsync("Wave Play School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (otherTeacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Other teacher");
        var classId = await kit.ClassAsync(school);
        var student = await kit.StudentLoginAsync(school, await kit.StudentAsync(school, classId, "Ada"));

        var teaser = await Json(await teacher.PostAsJsonAsync("/api/school/play/teasers", new { prompt = "What has keys but no locks?", answer = "A piano" }, Ct));
        var teaserId = teaser.GetProperty("id").GetGuid();
        var edited = await Json(await teacher.PutAsJsonAsync($"/api/school/play/teasers/{teaserId}", new { prompt = "What has many keys but opens no door?", answer = "A piano" }, Ct));
        Assert.Equal("What has many keys but opens no door?", edited.GetProperty("prompt").GetString());
        await ProblemAsync(await teacher.PutAsJsonAsync($"/api/school/play/teasers/{teaserId}", new { prompt = "", answer = "A piano" }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.PutAsJsonAsync($"/api/school/play/teasers/{teaserId}", new { prompt = "Mine now", answer = "No" }, Ct)).StatusCode);
        var other = await kit.SchoolAsync("Wave Play Other School");
        await ProblemAsync(await other.Owner.PutAsJsonAsync($"/api/school/play/teasers/{teaserId}", new { prompt = "Hijack", answer = "No" }, Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await other.Owner.PostAsync($"/api/school/play/teasers/{teaserId}/archive", null, Ct), HttpStatusCode.NotFound);

        Assert.Contains((await Json(await student.GetAsync("/api/student/play/teasers", Ct))).EnumerateArray(), t => t.GetProperty("id").GetGuid() == teaserId);
        Assert.True((await Json(await school.Owner.PostAsync($"/api/school/play/teasers/{teaserId}/archive", null, Ct))).GetProperty("archived").GetBoolean());
        Assert.DoesNotContain((await Json(await student.GetAsync("/api/student/play/teasers", Ct))).EnumerateArray(), t => t.GetProperty("id").GetGuid() == teaserId);
        Assert.False((await Json(await teacher.PostAsync($"/api/school/play/teasers/{teaserId}/unarchive", null, Ct))).GetProperty("archived").GetBoolean());
        Assert.Contains((await Json(await student.GetAsync("/api/student/play/teasers", Ct))).EnumerateArray(), t => t.GetProperty("id").GetGuid() == teaserId);

        var list = await Json(await teacher.PostAsJsonAsync("/api/school/play/words", new { name = "Fruits", words = new[] { "MANGO", "GUAVA" } }, Ct));
        var listId = list.GetProperty("id").GetGuid();
        Assert.False(list.GetProperty("archived").GetBoolean());
        var renamed = await Json(await teacher.PutAsJsonAsync($"/api/school/play/words/{listId}", new { name = "Fruit words", words = new[] { "MANGO", "PAWPAW", "ORANGE" } }, Ct));
        Assert.Equal("Fruit words", renamed.GetProperty("name").GetString());
        Assert.Equal(["MANGO", "PAWPAW", "ORANGE"], renamed.GetProperty("words").EnumerateArray().Select(w => w.GetString()).ToList());
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.PostAsync($"/api/school/play/words/{listId}/archive", null, Ct)).StatusCode);
        await ProblemAsync(await other.Owner.PutAsJsonAsync($"/api/school/play/words/{listId}", new { name = "Hijack", words = new[] { "HIJACK" } }, Ct), HttpStatusCode.NotFound);

        var search = await Json(await student.GetAsync("/api/student/play/word-search", Ct));
        Assert.Contains("PAWPAW", search.GetProperty("words").EnumerateArray().Select(w => w.GetString()));
        var archived = await Json(await teacher.PostAsync($"/api/school/play/words/{listId}/archive", null, Ct));
        Assert.True(archived.GetProperty("archived").GetBoolean());
        var after = await Json(await student.GetAsync("/api/student/play/word-search", Ct));
        Assert.DoesNotContain("PAWPAW", after.GetProperty("words").EnumerateArray().Select(w => w.GetString()));
        // Staff still see the archived list, marked as archived.
        Assert.True((await Json(await teacher.GetAsync("/api/school/play/words", Ct))).EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == listId).GetProperty("archived").GetBoolean());
        Assert.False((await Json(await teacher.PostAsync($"/api/school/play/words/{listId}/unarchive", null, Ct))).GetProperty("archived").GetBoolean());
    }

    // ---------- 6. Exam periods: edit and delete ----------

    [Fact]
    public async Task Exam_period_is_edited_and_deleted_only_when_empty()
    {
        var school = await kit.SchoolAsync("Wave Exam Period School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var classId = await kit.ClassAsync(school);
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var classSubjectId = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid() }, Ct))).GetProperty("id").GetGuid();
        var termId = await CurrentTermAsync(school);
        var day = new DateOnly(2026, 12, 7);
        var created = await Json(await school.Owner.PostAsJsonAsync("/api/school/exam-periods",
            new { termId, name = "First term exam", kind = "Examination", start = day, end = day.AddDays(4) }, Ct));
        var periodId = created.GetProperty("id").GetGuid();
        var url = $"/api/school/exam-periods/{periodId}";

        var edited = await Json(await school.Owner.PutAsJsonAsync(url, new { termId, name = "First term examination", kind = "Test", start = day, end = day.AddDays(2) }, Ct));
        Assert.Equal("First term examination", edited.GetProperty("name").GetString());
        Assert.Equal("Test", edited.GetProperty("kind").GetString());
        Assert.Equal(day.AddDays(2), DateOnly.Parse(edited.GetProperty("end").GetString()!));
        await ProblemAsync(await school.Owner.PutAsJsonAsync(url, new { termId, name = "X", kind = "Test", start = day, end = day.AddDays(-1) }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync(url, new { termId, name = "X", kind = "Test", start = day, end = day }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.DeleteAsync(url, Ct)).StatusCode);
        var other = await kit.SchoolAsync("Wave Exam Other School");
        await ProblemAsync(await other.Owner.PutAsJsonAsync(url, new { termId, name = "X", kind = "Test", start = day, end = day }, Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await other.Owner.DeleteAsync(url, Ct), HttpStatusCode.NotFound);

        var sitting = await Json(await school.Owner.PostAsJsonAsync($"{url}/sittings", new
        {
            classSubjectId, date = day.AddDays(1), start = "09:00", end = "10:00", durationMinutes = 60, venue = "Hall A", instructions = "Answer all questions.",
        }, Ct));
        var narrowed = await ProblemAsync(await school.Owner.PutAsJsonAsync(url, new { termId, name = "First term examination", kind = "Test", start = day, end = day }, Ct), HttpStatusCode.BadRequest);
        Assert.Equal("1 sitting falls outside these dates. Move it or remove it first.", narrowed.GetProperty("detail").GetString());

        var blocked = await ProblemAsync(await school.Owner.DeleteAsync(url, Ct), HttpStatusCode.Conflict);
        Assert.Equal("This exam period has 1 sitting. Remove its sittings first, then delete the period.", blocked.GetProperty("detail").GetString());
        Assert.Equal("exam_period_has_sittings", blocked.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/exam-sittings/{sitting.GetProperty("id").GetGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync(url, Ct)).StatusCode);
        await ProblemAsync(await school.Owner.GetAsync(url, Ct), HttpStatusCode.NotFound);
        Assert.Empty((await Json(await school.Owner.GetAsync($"/api/school/exam-periods?termId={termId}", Ct))).EnumerateArray());
    }
}
