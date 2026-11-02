using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink student logins: the school sets passwords, students sign in on the school page with the admission number.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolStudentLoginTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);
    private const string Wrong = "Admission number or password is incorrect.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<(string Detail, JsonElement Body)> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {text}");
        var body = JsonDocument.Parse(text).RootElement.Clone();
        return (body.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "", body);
    }

    private async Task<(SchoolKit.School School, Guid StudentId, string AdmissionNo)> StudentSetupAsync(string name = "Lagos Grammar")
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var id = await kit.StudentAsync(school, classId, "Ada");
        var admissionNo = (await Json(await school.Owner.GetAsync($"/api/school/students/{id}", Ct))).GetProperty("admissionNo").GetString()!;
        return (school, id, admissionNo);
    }

    private Task<HttpResponseMessage> SignInAsync(string slug, string admissionNo, string password) =>
        api.CreateClient().PostAsJsonAsync($"/api/school/{slug}/student-login", new { admissionNo, password }, Ct);

    private Task<HttpResponseMessage> SetPasswordAsync(SchoolKit.School school, Guid id, string password) =>
        school.Owner.PostAsJsonAsync($"/api/school/students/{id}/login", new { password }, Ct);

    [Fact]
    public async Task A_secondary_student_signs_in_with_admission_number()
    {
        var (school, id, no) = await StudentSetupAsync();
        await Json(await SetPasswordAsync(school, id, "first-pass-1"));

        var response = await Json(await SignInAsync(school.Slug, no.ToLowerInvariant(), "first-pass-1"));
        Assert.Equal(JsonValueKind.Null, response.GetProperty("tenant").ValueKind); // a customer session: no school in the token
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.GetProperty("accessToken").GetString());
        var me = await Json(await client.GetAsync("/api/student/me", Ct));
        Assert.Equal("Ada Obi", me.GetProperty("name").GetString());
        Assert.Equal(no, me.GetProperty("admissionNo").GetString());
        Assert.Equal("JSS 1A", me.GetProperty("className").GetString());
        Assert.Equal("Lagos Grammar", me.GetProperty("schoolName").GetString());

        // The same message for a wrong school, an unknown admission number and a wrong password.
        foreach (var attempt in new[] { (school.Slug + "-x", no, "first-pass-1"), (school.Slug, "NOPE-1", "first-pass-1"), (school.Slug, no, "wrong-pass-1") })
            Assert.Equal(Wrong, (await ProblemAsync(await SignInAsync(attempt.Item1, attempt.Item2, attempt.Item3), HttpStatusCode.BadRequest)).Detail);

        // The school resets the password: the new one works, the old one and the old session do not.
        var refresh = response.GetProperty("refreshToken").GetString();
        await Json(await SetPasswordAsync(school, id, "second-pass-1"));
        await ProblemAsync(await SignInAsync(school.Slug, no, "first-pass-1"), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh }, Ct)).StatusCode);
        await Json(await SignInAsync(school.Slug, no, "second-pass-1"));
        Assert.True(await api.WithDbAsync(db => db.Users.CountAsync(u => u.Email!.EndsWith("@students.schoollink.invalid") && u.Email.Contains("." + school.Slug + "@"), Ct)) == 1);
    }

    [Fact]
    public async Task The_student_helper_returns_a_signed_in_client()
    {
        var (school, id, _) = await StudentSetupAsync();
        var client = await kit.StudentLoginAsync(school, id);
        Assert.Equal("Ada Obi", (await Json(await client.GetAsync("/api/student/me", Ct))).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Primary_students_cannot_get_logins()
    {
        var school = await kit.SchoolAsync("Little Stars");
        var levelId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/levels", new { name = "Primary 3", stage = "Primary", order = 5, hasStudentLogins = false }, Ct))).GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var id = await kit.StudentAsync(school, classId, "Tobi");

        var (_, body) = await ProblemAsync(await SetPasswordAsync(school, id, "primary-pass-1"), HttpStatusCode.BadRequest);
        Assert.True(body.GetProperty("errors").TryGetProperty("level", out _), body.ToString());
        Assert.False(await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().AnyAsync(s => s.Id == id && s.UserId != null, Ct)));
    }

    [Fact]
    public async Task Changing_an_admission_number_keeps_the_login_working()
    {
        var (school, id, oldNo) = await StudentSetupAsync();
        await Json(await SetPasswordAsync(school, id, "right-pass-1"));
        var newNo = oldNo + "-FIX";
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{id}", new { admissionNo = newNo, firstName = "Ada", lastName = "Obi", gender = "Female" }, Ct));

        await Json(await SignInAsync(school.Slug, newNo, "right-pass-1"));
        await ProblemAsync(await SignInAsync(school.Slug, oldNo, "right-pass-1"), HttpStatusCode.BadRequest);
        var email = await api.WithDbAsync(db => db.Users.Where(u => db.Students.IgnoreQueryFilters().Any(s => s.Id == id && s.UserId == u.Id)).Select(u => u.Email!).SingleAsync(Ct));
        Assert.Equal(Saas.Application.School.StudentLogins.EmailFor(newNo, school.Slug), email);

        // The old number can now be given to another student, with a login of their own.
        var other = await kit.StudentAsync(school, await kit.ClassAsync(school), "Bayo");
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{other}", new { admissionNo = oldNo, firstName = "Bayo", lastName = "Obi", gender = "Male" }, Ct));
        await Json(await SetPasswordAsync(school, other, "other-pass-1"));
    }

    [Fact]
    public async Task Deleting_a_student_frees_their_admission_number_for_a_new_login()
    {
        var (school, id, no) = await StudentSetupAsync();
        await Json(await SetPasswordAsync(school, id, "right-pass-1"));
        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/students/{id}", Ct)).StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Users.AnyAsync(u => u.Email!.StartsWith(no.ToLower() + ".") && u.Email.EndsWith("@students.schoollink.invalid"), Ct)));
        await ProblemAsync(await SignInAsync(school.Slug, no, "right-pass-1"), HttpStatusCode.BadRequest);

        var again = (await Json(await school.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = no, firstName = "Ada", lastName = "Obi", gender = "Female", classId = await kit.ClassAsync(school) }, Ct))).GetProperty("id").GetGuid();
        await Json(await SetPasswordAsync(school, again, "new-pass-123"));
        await Json(await SignInAsync(school.Slug, no, "new-pass-123"));
    }

    [Fact]
    public async Task A_password_needs_at_least_eight_characters()
    {
        var (school, id, _) = await StudentSetupAsync();
        var (_, body) = await ProblemAsync(await SetPasswordAsync(school, id, "short"), HttpStatusCode.BadRequest);
        Assert.True(body.GetProperty("errors").TryGetProperty("password", out _), body.ToString());
    }

    [Fact]
    public async Task Wrong_password_five_times_locks_the_student_for_10_minutes()
    {
        var (school, id, no) = await StudentSetupAsync();
        await Json(await SetPasswordAsync(school, id, "right-pass-1"));
        for (var i = 0; i < 5; i++) await ProblemAsync(await SignInAsync(school.Slug, no, "wrong-pass-1"), HttpStatusCode.BadRequest);

        // Locked: even the right password is refused.
        var (detail, _) = await ProblemAsync(await SignInAsync(school.Slug, no, "right-pass-1"), HttpStatusCode.BadRequest);
        Assert.Contains("Too many failed attempts", detail);
        var until = await api.WithDbAsync(async db => (await db.Users.Where(u => db.Students.IgnoreQueryFilters().Any(s => s.Id == id && s.UserId == u.Id)).Select(u => u.LockoutEnd).SingleAsync(Ct))!.Value);
        Assert.InRange((until - DateTimeOffset.UtcNow).TotalMinutes, 8, 10.5);

        // The lock ends by itself; the school can also clear it by resetting the password.
        await api.WithDbAsync(db => db.Users.Where(u => db.Students.IgnoreQueryFilters().Any(s => s.Id == id && s.UserId == u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        await Json(await SignInAsync(school.Slug, no, "right-pass-1"));
    }

    [Fact]
    public async Task A_student_token_cannot_reach_staff_routes()
    {
        var (school, id, _) = await StudentSetupAsync();
        var student = await kit.StudentLoginAsync(school, id);

        await ProblemAsync(await student.GetAsync("/api/school/setup", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await student.GetAsync("/api/school/students", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await student.PostAsJsonAsync($"/api/school/students/{id}/login", new { password = "hacked-pass-1" }, Ct), HttpStatusCode.Forbidden);
        // Nor the parent portal: a student is not a guardian.
        Assert.Empty((await Json(await student.GetAsync("/api/parent/children", Ct))).EnumerateArray());
        // And a staff token is not a student.
        await ProblemAsync(await school.Owner.GetAsync("/api/student/me", Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_leadership_sets_student_passwords()
    {
        var (school, id, _) = await StudentSetupAsync();
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        await ProblemAsync(await teacher.PostAsJsonAsync($"/api/school/students/{id}/login", new { password = "teacher-pass-1" }, Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_student_account_cannot_sign_in_by_email_or_get_emails()
    {
        var (school, id, _) = await StudentSetupAsync();
        await Json(await SetPasswordAsync(school, id, "right-pass-1"));
        var email = await api.WithDbAsync(db => db.Users.Where(u => db.Students.IgnoreQueryFilters().Any(s => s.Id == id && s.UserId == u.Id)).Select(u => u.Email!).SingleAsync(Ct));
        Assert.EndsWith("@students.schoollink.invalid", email);
        Assert.False(await api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.EmailConfirmed).SingleAsync(Ct)));

        var anon = api.CreateClient();
        foreach (var path in new[] { "/api/auth/login", "/api/auth/parent/login", "/api/auth/learner/login", "/api/auth/seeker/login" })
        {
            var r = await anon.PostAsJsonAsync(path, new { email, password = "right-pass-1" }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/otp/request", new { email }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsJsonAsync("/api/auth/password/forgot", new { email }, Ct)).StatusCode);
        Assert.False(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.Recipient == email, Ct)));
        Assert.False(await api.WithDbAsync(db => db.OtpCodes.AnyAsync(o => o.Target == email, Ct)));
    }

    [Fact]
    public async Task A_withdrawn_student_loses_access_at_once()
    {
        var (school, id, no) = await StudentSetupAsync();
        var student = await kit.StudentLoginAsync(school, id);
        var refresh = await api.WithDbAsync(db => db.RefreshTokens.Where(t => db.Students.IgnoreQueryFilters().Any(s => s.Id == id && s.UserId == t.UserId) && t.RevokedAt == null).CountAsync(Ct));
        Assert.True(refresh > 0);

        await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{id}/status", new { status = "Withdrawn", reason = "Moved abroad" }, Ct));

        var (detail, _) = await ProblemAsync(await student.GetAsync("/api/student/me", Ct), HttpStatusCode.Forbidden);
        Assert.Equal("Your account isn't active. Please see the school office.", detail);
        Assert.Equal(0, await api.WithDbAsync(db => db.RefreshTokens.Where(t => db.Students.IgnoreQueryFilters().Any(s => s.Id == id && s.UserId == t.UserId) && t.RevokedAt == null).CountAsync(Ct)));
        await ProblemAsync(await SignInAsync(school.Slug, no, SchoolKit.StudentPassword), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_suspended_school_closes_the_student_portal()
    {
        var (school, id, no) = await StudentSetupAsync();
        var student = await kit.StudentLoginAsync(school, id);
        await api.WithDbAsync(db => db.Tenants.Where(t => t.Id == school.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Suspended), Ct));

        var (detail, _) = await ProblemAsync(await student.GetAsync("/api/student/me", Ct), HttpStatusCode.Forbidden);
        Assert.Equal("This school's portal is unavailable.", detail);
        await ProblemAsync(await SignInAsync(school.Slug, no, SchoolKit.StudentPassword), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/api/school/{school.Slug}/public", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_school_sign_in_page_shows_the_school()
    {
        var school = await kit.SchoolAsync("Public Heights");
        var page = await Json(await api.CreateClient().GetAsync($"/api/school/{school.Slug}/public", Ct));
        Assert.Equal("Public Heights", page.GetProperty("name").GetString());
        Assert.True(page.TryGetProperty("logoUrl", out _));
        Assert.True(page.TryGetProperty("motto", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync("/api/school/no-such-school-xyz/public", Ct)).StatusCode);
    }

    [Fact]
    public async Task Students_of_two_schools_with_the_same_admission_number_stay_apart()
    {
        var a = await StudentSetupAsync("School Aye");
        var b = await StudentSetupAsync("School Bee");
        // Give B's student A's admission number (numbers are unique per school only).
        await Json(await b.School.Owner.PutAsJsonAsync($"/api/school/students/{b.StudentId}",
            new { admissionNo = a.AdmissionNo, firstName = "Bola", lastName = "Obi", gender = "Male" }, Ct));
        await Json(await SetPasswordAsync(a.School, a.StudentId, "pass-for-aye-1"));
        await Json(await SetPasswordAsync(b.School, b.StudentId, "pass-for-bee-1"));

        await Json(await SignInAsync(a.School.Slug, a.AdmissionNo, "pass-for-aye-1"));
        await ProblemAsync(await SignInAsync(a.School.Slug, a.AdmissionNo, "pass-for-bee-1"), HttpStatusCode.BadRequest);
        await Json(await SignInAsync(b.School.Slug, a.AdmissionNo, "pass-for-bee-1"));
    }

    [Fact]
    public async Task Primary_student_can_sit_cbt()
    {
        var school = await kit.SchoolAsync("Primary Sit School");
        (await school.Owner.PostAsJsonAsync("/api/school/setup/apply-template", new { levels = true, subjects = true }, Ct)).EnsureSuccessStatusCode();
        var levels = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        var primary = levels.Single(level => level.GetProperty("name").GetString() == "Primary 1");
        Assert.True(primary.GetProperty("hasStudentLogins").GetBoolean());
        var levelId = primary.GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var mathsId = (await Json(await school.Owner.GetAsync("/api/school/subjects", Ct))).EnumerateArray()
            .Single(subject => subject.GetProperty("code").GetString() == "MTH").GetProperty("id").GetGuid();
        var classSubjectId = (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = mathsId }, Ct))).GetProperty("id").GetGuid();
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var questionId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/questions", new
        {
            subjectId = mathsId, stem = "What is 2 + 2?", options = new[] { "3", "4", "5", "6" }, correctIndex = 1, marks = 2,
        }, Ct))).GetProperty("id").GetGuid();
        var paper = await Json(await school.Owner.PostAsJsonAsync("/api/school/cbt/papers", new
        {
            classSubjectId, termId, title = "Primary mathematics", durationMinutes = 20,
            openAt = DateTimeOffset.UtcNow.AddHours(-1), closeAt = DateTimeOffset.UtcNow.AddHours(2),
            drawCount = 0, pickQuestionIds = new[] { questionId },
        }, Ct));
        var paperId = paper.GetProperty("id").GetGuid();
        await Json(await school.Owner.PostAsync($"/api/school/cbt/papers/{paperId}/publish", null, Ct));

        var studentId = await kit.StudentAsync(school, classId, "Tobi");
        var student = await kit.StudentLoginAsync(school, studentId);
        var response = await student.PostAsync($"/api/student/cbt/papers/{paperId}/start", null, Ct);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {raw}");
        Assert.DoesNotContain("correctIndex", raw, StringComparison.OrdinalIgnoreCase);
        var started = JsonDocument.Parse(raw).RootElement;
        Assert.NotEqual(Guid.Empty, started.GetProperty("attemptId").GetGuid());
        Assert.Equal("What is 2 + 2?", Assert.Single(started.GetProperty("items").EnumerateArray()).GetProperty("stem").GetString());
    }
}
