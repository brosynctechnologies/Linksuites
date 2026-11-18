using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 2 known bugs in SchoolLink: re-imports keep what a blank cell leaves out, locked terms freeze trait ratings, a subject teacher
/// does not see overall standings, student accounts can't become parents, and the smaller fixes listed in the roadmap.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolWaveTwoTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private static DateOnly Today => Saas.Application.School.SchoolCalendar.Today(DateTimeOffset.UtcNow);

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

    private Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>A class with Mathematics taught by a teacher; the current term covers today.</summary>
    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid ClassSubjectId, Guid SubjectId, Guid TermId, HttpClient Teacher);

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = await StaffIdAsync(school, userId) }, Ct));
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        return new Setup(school, classId, link.GetProperty("id").GetGuid(), subject.GetProperty("id").GetGuid(), termId, teacher);
    }

    // ---------- 1. Re-import with blank cells ----------

    private const string StudentHeader =
        "admission_no,first_name,middle_name,last_name,gender,date_of_birth,class,guardian1_name,guardian1_phone,guardian1_email,guardian1_relationship,guardian2_name,guardian2_phone,guardian2_email,guardian2_relationship";

    private static MultipartFormDataContent CsvForm(params string[] lines)
    {
        var csv = string.Join("\r\n", new[] { StudentHeader }.Concat(lines)) + "\r\n";
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", "students.csv" } };
    }

    [Fact]
    public async Task Reimport_with_blank_middle_name_and_birth_date_keeps_them()
    {
        var school = await kit.SchoolAsync("Blank Reimport School");
        await kit.ClassAsync(school);
        var first = await Json(await school.Owner.PostAsync("/api/school/students/import/preview", CsvForm("BR-1,Ada,Chioma,Obi,F,2012-03-04,JSS 1A,,,,,,,,"), Ct));
        await Json(await school.Owner.PostAsync($"/api/school/students/import/{first.GetProperty("token").GetString()}/confirm", null, Ct));

        // The second file only corrects the last name; its middle name and date of birth cells are empty.
        var second = await Json(await school.Owner.PostAsync("/api/school/students/import/preview", CsvForm("BR-1,Ada,,Obi-Eze,F,,JSS 1A,,,,,,,,"), Ct));
        await Json(await school.Owner.PostAsync($"/api/school/students/import/{second.GetProperty("token").GetString()}/confirm", null, Ct));

        var student = await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().SingleAsync(s => s.TenantId == school.TenantId, Ct));
        Assert.Equal("Obi-Eze", student.LastName);
        Assert.Equal("Chioma", student.MiddleName);
        Assert.Equal(new DateOnly(2012, 3, 4), student.DateOfBirth);
    }

    // ---------- 2. Trait ratings freeze with the term lock ----------

    [Fact]
    public async Task Trait_ratings_are_refused_once_the_term_is_locked()
    {
        var s = await SetupAsync("Trait Lock School");
        var ada = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        var affective = await Json(await s.School.Owner.GetAsync("/api/school/traits?kind=Affective", Ct));
        var criterion = affective.GetProperty("criteria").EnumerateArray().First().GetProperty("id").GetGuid();
        var url = $"/api/school/students/{ada}/traits?termId={s.TermId}&kind=Affective";
        await Json(await s.School.Owner.PutAsJsonAsync(url, new { ratings = new[] { new { criterionId = criterion, value = 3 } } }, Ct));

        await Json(await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct));
        var refused = await ProblemAsync(await s.School.Owner.PutAsJsonAsync(url, new { ratings = new[] { new { criterionId = criterion, value = 5 } } }, Ct),
            HttpStatusCode.Conflict);
        Assert.Equal("term_locked", refused.GetProperty("code").GetString());
        var stored = await api.WithDbAsync(db => db.TraitRatings.IgnoreQueryFilters().Where(r => r.CriterionId == criterion).Select(r => r.Value).SingleAsync(Ct));
        Assert.Equal(3, stored);
    }

    // ---------- 4. A student account is not a parent ----------

    [Fact]
    public async Task A_student_account_cannot_accept_a_guardian_invite()
    {
        var school = await kit.SchoolAsync("Student Invite School");
        var classId = await kit.ClassAsync(school);
        var ada = await kit.StudentAsync(school, classId, "Ada");
        var bola = await kit.StudentAsync(school, classId, "Bola");
        var guardianId = await kit.GuardianAsync(school, bola, "Bola Parent", null, $"bola-{Guid.NewGuid():N}@example.com");
        var token = await kit.InviteAsync(school, guardianId);
        var student = await kit.StudentLoginAsync(school, ada);

        await ProblemAsync(await student.PostAsync($"/api/school-invites/{token}/accept", null, Ct), HttpStatusCode.Forbidden);
        Assert.Null(await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().Where(g => g.Id == guardianId).Select(g => g.UserId).SingleAsync(Ct)));
    }

    // ---------- 5. Absence message for a past day ----------

    [Fact]
    public async Task Absence_message_for_an_earlier_day_does_not_say_today()
    {
        var s = await SetupAsync("Past Absence School");
        var ada = await kit.StudentAsync(s.School, s.ClassId, "Ada");
        await kit.GuardianAsync(s.School, ada, "Ngozi Obi", "0803 123 4567", null);
        var enrolment = await EnrolmentAsync(s.School, ada);
        var day = Today.AddDays(-1);
        var saved = await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/attendance/{s.ClassId}/{day:yyyy-MM-dd}",
            new { clientId = "past-1", marks = new[] { new { enrolmentId = enrolment, status = "Absent" } } }, Ct));
        var text = Uri.UnescapeDataString(Assert.Single(saved.GetProperty("messageParents").EnumerateArray()).GetProperty("whatsAppUrl").GetString()!);
        Assert.DoesNotContain("today", text);
        Assert.Contains($"on {day.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}", text);
    }

    // ---------- 6. Half-written lesson plan drafts ----------

    [Fact]
    public async Task A_lesson_plan_draft_saves_half_written_and_submit_asks_for_the_rest()
    {
        var s = await SetupAsync("Draft Plan School");
        var draft = await Json(await s.Teacher.PostAsJsonAsync("/api/school/lesson-plans", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, week = 2, date = Today, topic = "Fractions",
            objectives = "Name a half.",
        }, Ct));
        var id = draft.GetProperty("id").GetGuid();
        Assert.Equal("", draft.GetProperty("materials").GetString());

        var refused = await ProblemAsync(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct), HttpStatusCode.BadRequest);
        Assert.True(refused.GetProperty("errors").TryGetProperty("materials", out _), refused.ToString());
        Assert.Equal("Draft", (await Json(await s.Teacher.GetAsync($"/api/school/lesson-plans/{id}", Ct))).GetProperty("status").GetString());

        await Json(await s.Teacher.PutAsJsonAsync($"/api/school/lesson-plans/{id}", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, week = 2, date = Today, topic = "Fractions",
            objectives = "Name a half.", materials = "Paper.", methods = "Demonstration.", activities = "Folding.", previousKnowledge = "Counting.",
            introduction = "An orange.", presentation = "Fold.", conclusion = "Recap.", evaluation = "Fold a quarter.",
        }, Ct));
        Assert.Equal("Submitted", (await Json(await s.Teacher.PostAsync($"/api/school/lesson-plans/{id}/submit", null, Ct))).GetProperty("status").GetString());
    }

    // ---------- 7. Assignments: 404 for others' class subjects; removing a counted component ----------

    [Fact]
    public async Task Assignments_for_a_class_subject_the_teacher_does_not_teach_are_404()
    {
        var s = await SetupAsync("Assignment 404 School");
        var (other, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        await ProblemAsync(await other.GetAsync($"/api/school/assignments?classSubjectId={s.ClassSubjectId}&termId={s.TermId}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await other.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, title = "Week 1", instructions = "Do it.", dueAt = DateTimeOffset.UtcNow.AddDays(2),
        }, Ct), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_component_with_counted_work_cannot_be_removed()
    {
        var s = await SetupAsync("Counted Component School");
        var scheme = await Json(await s.School.Owner.GetAsync("/api/school/assessment", Ct));
        var parts = scheme.GetProperty("components").EnumerateArray().ToList();
        Guid Id(string name) => parts.Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
        await Json(await s.Teacher.PostAsJsonAsync("/api/school/assignments", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, title = "Exam project", instructions = "Do it.", dueAt = DateTimeOffset.UtcNow.AddDays(2),
            maxMarks = 10m, assessmentComponentId = Id("Exam"),
        }, Ct));

        var refused = await ProblemAsync(await s.School.Owner.PutAsJsonAsync("/api/school/assessment", new
        {
            levelId = (Guid?)null,
            components = new[] { new { name = "CA1", maxScore = 20m, id = (Guid?)Id("CA1") }, new { name = "CA2", maxScore = 80m, id = (Guid?)Id("CA2") } },
        }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("component_has_work", refused.GetProperty("code").GetString());
        Assert.Equal(3, (await Json(await s.School.Owner.GetAsync("/api/school/assessment", Ct))).GetProperty("components").GetArrayLength());
    }

    // ---------- 8, 9, 14. CBT: hand-in after the lock, server time, "of N" ----------

    private async Task<(Guid PaperId, HttpClient Student)> CountedPaperAsync(Setup s)
    {
        var scheme = await Json(await s.School.Owner.GetAsync("/api/school/assessment", Ct));
        var exam = scheme.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Exam").GetProperty("id").GetGuid();
        var question = await Json(await s.Teacher.PostAsJsonAsync("/api/school/questions",
            new { subjectId = s.SubjectId, stem = "What is 2 + 2?", options = new[] { "2", "4", "6", "8" }, correctIndex = 1, marks = 2m }, Ct));
        var paper = await Json(await s.Teacher.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId = s.ClassSubjectId, termId = s.TermId, title = "Counted test", durationMinutes = 30,
            openAt = DateTimeOffset.UtcNow.AddHours(-1), closeAt = DateTimeOffset.UtcNow.AddHours(3), assessmentComponentId = exam, drawCount = 0,
            pickQuestionIds = new[] { question.GetProperty("id").GetGuid() },
        }, Ct));
        var paperId = paper.GetProperty("id").GetGuid();
        await Json(await s.Teacher.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct));
        var student = await kit.StudentLoginAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Ada"));
        return (paperId, student);
    }

    [Fact]
    public async Task A_counted_hand_in_after_the_term_locks_is_marked_without_a_score()
    {
        var s = await SetupAsync("Locked Hand In School");
        var (paperId, student) = await CountedPaperAsync(s);
        var start = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var attemptId = start.GetProperty("attemptId").GetGuid();
        await Json(await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct));

        var handedIn = await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));
        Assert.NotEqual(JsonValueKind.Null, handedIn.GetProperty("submittedAt").ValueKind);
        Assert.Equal(0, await api.WithDbAsync(db => db.Scores.IgnoreQueryFilters().CountAsync(x => x.TenantId == s.School.TenantId, Ct)));
    }

    [Fact]
    public async Task A_sitting_carries_the_server_time_and_a_late_hand_in_is_stamped_at_the_deadline()
    {
        var s = await SetupAsync("Server Clock School");
        var (paperId, student) = await CountedPaperAsync(s);
        var start = await Json(await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct));
        var serverNow = start.GetProperty("serverNow").GetDateTimeOffset();
        Assert.InRange(serverNow, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        var attemptId = start.GetProperty("attemptId").GetGuid();
        Assert.NotEqual(JsonValueKind.Undefined, (await Json(await student.GetAsync($"/api/student/cbt/attempts/{attemptId}", Ct))).GetProperty("serverNow").ValueKind);

        // The device's clock let the sitting run on: the hand-in arrives after the deadline and is stamped at the deadline.
        var deadline = DateTimeOffset.UtcNow.AddMinutes(-5);
        await api.WithDbAsync(db => db.CbtAttempts.IgnoreQueryFilters().Where(a => a.Id == attemptId)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.DeadlineAt, deadline), Ct));
        var handedIn = await Json(await student.PostAsync($"/api/student/cbt/attempts/{attemptId}/submit", null, Ct));
        Assert.InRange(Math.Abs(deadline.ToUnixTimeMilliseconds() - handedIn.GetProperty("submittedAt").GetDateTimeOffset().ToUnixTimeMilliseconds()), 0L, 1L);
        Assert.Equal(2m, handedIn.GetProperty("outOf").GetDecimal());
    }

    // ---------- 10. Import previews are rate limited ----------

    [Fact]
    public void Import_previews_are_rate_limited_per_user()
    {
        _ = api.CreateClient();
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        string? Policy(string route) => endpoints
            .Where(e => e.RoutePattern.RawText == route && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
            .Select(e => e.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().LastOrDefault()?.PolicyName).Single();
        Assert.Equal("import-preview", Policy("/api/school/imports/{kind}/preview"));
        Assert.Equal("import-preview", Policy("/api/school/students/import/preview"));
    }

    // ---------- 11. Each app shows its own notifications ----------

    [Fact]
    public async Task Staff_and_parent_sessions_see_only_their_own_apps_notifications()
    {
        var school = await kit.SchoolAsync("Two Hat School");
        var (parent, parentUserId, _) = await kit.ParentAccountAsync();
        var ownerId = await kit.OwnerIdAsync(school);
        await api.WithDbAsync(db =>
        {
            foreach (var userId in new[] { ownerId, parentUserId })
            {
                db.UserNotifications.Add(new UserNotification
                {
                    UserId = userId, TenantId = school.TenantId, Product = "school", Kind = "school.absent", Title = "Parent note", Body = "b",
                    Link = $"/parent/attendance?child={Guid.NewGuid()}",
                });
                db.UserNotifications.Add(new UserNotification
                {
                    UserId = userId, TenantId = school.TenantId, Product = "school", Kind = "school.message", Title = "Staff note", Body = "b", Link = "/app/messages",
                });
            }
            return db.SaveChangesAsync(Ct);
        });

        var staffFeed = await Json(await school.Owner.GetAsync("/api/me/notifications", Ct));
        Assert.Equal(["Staff note"], staffFeed.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("title").GetString()!).ToList());
        var parentFeed = await Json(await parent.GetAsync("/api/me/notifications", Ct));
        Assert.Equal(["Parent note"], parentFeed.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("title").GetString()!).ToList());
        Assert.Equal(1, (await Json(await parent.GetAsync("/api/me/notifications/unread-count", Ct))).GetProperty("count").GetInt32());
    }

    // ---------- 14. Payment search on the server ----------

    [Fact]
    public async Task Invoice_list_searches_and_filters_owing_on_the_server()
    {
        var school = await kit.SchoolAsync("Invoice Search School");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Adaeze", "Okafor");
        await kit.StudentAsync(school, classId, "Bola", "Yusuf");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await CurrentTermAsync(school);
        var tuition = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = tuition, amountMinor = 5_000_000L } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));

        var found = await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}&q=okaf&owing=true", Ct));
        Assert.Equal("Adaeze Okafor", Assert.Single(found.EnumerateArray()).GetProperty("studentName").GetString());
        var number = Assert.Single(found.EnumerateArray()).GetProperty("number").GetString()!;
        var byNumber = await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}&q={Uri.EscapeDataString(number)}", Ct));
        Assert.Equal(number, Assert.Single(byNumber.EnumerateArray()).GetProperty("number").GetString());
    }

    // ---------- 15. Terms inside the session; the email change races ----------

    [Fact]
    public async Task Terms_stay_inside_their_session()
    {
        var school = await kit.SchoolAsync("Term Bounds School");
        var setup = await Json(await school.Owner.GetAsync("/api/school/setup", Ct));
        var session = setup.GetProperty("sessions").EnumerateArray().Single(x => x.GetProperty("isCurrent").GetBoolean());
        var sessionStart = DateOnly.Parse(session.GetProperty("startDate").GetString()!);
        var sessionEnd = DateOnly.Parse(session.GetProperty("endDate").GetString()!);
        var first = session.GetProperty("terms").EnumerateArray().Single(t => t.GetProperty("number").GetInt32() == 1);
        var third = session.GetProperty("terms").EnumerateArray().Single(t => t.GetProperty("number").GetInt32() == 3);
        var firstEnd = DateOnly.Parse(first.GetProperty("endDate").GetString()!);

        var early = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/terms/{first.GetProperty("id").GetGuid()}",
            new { name = first.GetProperty("name").GetString(), startDate = sessionStart.AddDays(-3), endDate = firstEnd, nextTermStarts = (DateOnly?)null }, Ct),
            HttpStatusCode.BadRequest);
        Assert.True(early.GetProperty("errors").TryGetProperty("startDate", out _), early.ToString());

        var thirdStart = DateOnly.Parse(third.GetProperty("startDate").GetString()!);
        await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/terms/{third.GetProperty("id").GetGuid()}",
            new { name = third.GetProperty("name").GetString(), startDate = thirdStart, endDate = sessionEnd.AddDays(5), nextTermStarts = (DateOnly?)null }, Ct),
            HttpStatusCode.BadRequest);

        // Shrinking the session past a term is refused too.
        var shrink = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/sessions/{session.GetProperty("id").GetGuid()}",
            new { name = session.GetProperty("name").GetString(), startDate = sessionStart, endDate = thirdStart }, Ct), HttpStatusCode.BadRequest);
        Assert.True(shrink.GetProperty("errors").TryGetProperty("endDate", out _), shrink.ToString());
    }

    private Task<string> LatestChangeCodeAsync(string email, Guid userId) =>
        api.WithDbAsync(async db => (await db.Notifications.Where(n => n.TemplateKey == "email.change_code" && n.Recipient == email && n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct)).Payload["code"]);

    [Fact]
    public async Task Two_parents_confirming_the_same_new_email_together_get_one_success_and_conflicts()
    {
        var target = $"race-{Guid.NewGuid():N}@example.com";
        var parents = new List<(HttpClient Client, string Code)>();
        for (var i = 0; i < 4; i++)
        {
            var (client, userId, _) = await kit.ParentAccountAsync();
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = target }, Ct)).StatusCode);
            parents.Add((client, await LatestChangeCodeAsync(target, userId)));
        }
        var responses = await Task.WhenAll(parents.Select(p => p.Client.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = target, code = p.Code }, Ct)));
        var codes = responses.Select(r => (int)r.StatusCode).OrderBy(c => c).ToList();
        Assert.Equal([200, 409, 409, 409], codes);
    }

    [Fact]
    public async Task Wrong_email_codes_sent_together_use_up_exactly_five_attempts()
    {
        var (parent, userId, _) = await kit.ParentAccountAsync();
        var target = $"guess-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.NoContent,
            (await parent.PostAsJsonAsync("/api/parent/profile/email", new { currentPassword = "password123", email = target }, Ct)).StatusCode);
        var code = await LatestChangeCodeAsync(target, userId);
        var wrong = code == "000000" ? "111111" : "000000";
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => parent.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = target, code = wrong }, Ct)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        var attempts = await api.WithDbAsync(db => db.OtpCodes.Where(o => o.Target == target).Select(o => o.Attempts).SingleAsync(Ct));
        Assert.Equal(5, attempts);
        // The right code is no longer accepted either.
        await ProblemAsync(await parent.PostAsJsonAsync("/api/parent/profile/email/confirm", new { email = target, code }, Ct), HttpStatusCode.BadRequest);
    }
}
