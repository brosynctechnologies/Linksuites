using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink results: computing totals, positions and attendance, remarks, and publishing a class's term.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolResultTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
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

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid TermId, Guid MathId, Guid EnglishId, Guid[] Components);

    private async Task<Guid> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code, Guid? teacherStaffId = null)
    {
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId }, Ct));
        return link.GetProperty("id").GetGuid();
    }

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var math = await ClassSubjectAsync(school, classId, "Mathematics", "MTH");
        var english = await ClassSubjectAsync(school, classId, "English", "ENG");
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={math}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        return new Setup(school, classId, termId, math, english, components);
    }

    private static string ResultsUrl(Setup s) => $"/api/school/results/{s.ClassId}/{s.TermId}";

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>Saves CA1, CA2 and exam for one student in one class subject.</summary>
    private async Task ScoreAsync(Setup s, Guid classSubjectId, Guid enrolmentId, decimal? ca1, decimal? ca2, decimal? exam, HttpClient? by = null)
    {
        var entries = new[] { (s.Components[0], ca1), (s.Components[1], ca2), (s.Components[2], exam) }
            .Select(c => new { enrolmentId, componentId = c.Item1, value = c.Item2 }).ToArray();
        (await (by ?? s.School.Owner).PutAsJsonAsync("/api/school/scores", new { classSubjectId, termId = s.TermId, entries }, Ct)).EnsureSuccessStatusCode();
    }

    private static JsonElement StudentRow(JsonElement results, string firstName) =>
        results.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    private static JsonElement SubjectRow(JsonElement student, string subject) =>
        student.GetProperty("subjects").EnumerateArray().Single(x => x.GetProperty("subject").GetString() == subject);

    private async Task TakeRegisterAsync(Setup s, DateOnly date, params (Guid Enrolment, string Status)[] marks) =>
        (await s.School.Owner.PutAsJsonAsync($"/api/school/attendance/{s.ClassId}/{date:yyyy-MM-dd}",
            new { clientId = Guid.NewGuid().ToString(), marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray() }, Ct)).EnsureSuccessStatusCode();

    /// <summary>Ada and Bayo tie on an average of 80; Chi has Mathematics only.</summary>
    private async Task<(Guid Ada, Guid Bayo, Guid Chi)> ScoredClassAsync(Setup s)
    {
        var ada = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Ada", "Zed"));
        var bayo = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Bayo", "Yusuf"));
        var chi = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Chi", "Xu"));
        await ScoreAsync(s, s.MathId, ada, 20, 20, 50);     // 90
        await ScoreAsync(s, s.EnglishId, ada, 10, 10, 50);  // 70
        await ScoreAsync(s, s.MathId, bayo, 20, 20, 45);    // 85
        await ScoreAsync(s, s.EnglishId, bayo, 15, 10, 50); // 75
        await ScoreAsync(s, s.MathId, chi, 10, 10, 30);     // 50
        return (ada, bayo, chi);
    }

    [Fact]
    public async Task Computed_results_have_totals_positions_and_attendance()
    {
        var s = await SetupAsync("Result Academy");
        var (ada, bayo, chi) = await ScoredClassAsync(s);
        await TakeRegisterAsync(s, Today.AddDays(-2), (ada, "Present"), (bayo, "Present"), (chi, "Present"));
        await TakeRegisterAsync(s, Today.AddDays(-1), (ada, "Absent"), (bayo, "Late"), (chi, "Present"));

        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.False(results.GetProperty("locked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, results.GetProperty("publishedAt").ValueKind);

        var a = StudentRow(results, "Ada");
        Assert.Equal(160m, a.GetProperty("total").GetDecimal());
        Assert.Equal(80m, a.GetProperty("average").GetDecimal());
        Assert.Equal(1, a.GetProperty("position").GetInt32());
        Assert.Equal(3, a.GetProperty("classSize").GetInt32());
        Assert.Equal(1, a.GetProperty("daysPresent").GetInt32());
        Assert.Equal(2, a.GetProperty("daysOpen").GetInt32());
        Assert.False(a.GetProperty("incomplete").GetBoolean());
        var aMath = SubjectRow(a, "Mathematics");
        Assert.Equal(90m, aMath.GetProperty("total").GetDecimal());
        Assert.Equal("A1", aMath.GetProperty("grade").GetString());
        Assert.Equal(75m, aMath.GetProperty("classAverage").GetDecimal());
        Assert.Equal(1, aMath.GetProperty("position").GetInt32());
        var aEnglish = SubjectRow(a, "English");
        Assert.Equal("B2", aEnglish.GetProperty("grade").GetString());
        Assert.Equal(2, aEnglish.GetProperty("position").GetInt32());
        Assert.Equal(72.5m, aEnglish.GetProperty("classAverage").GetDecimal());

        var b = StudentRow(results, "Bayo");
        Assert.Equal(1, b.GetProperty("position").GetInt32()); // ties share the position
        Assert.Equal(1, b.GetProperty("daysPresent").GetInt32());
        Assert.Equal(2, SubjectRow(b, "Mathematics").GetProperty("position").GetInt32());

        var c = StudentRow(results, "Chi");
        Assert.Equal(50m, c.GetProperty("average").GetDecimal()); // average over the one subject with scores
        Assert.Equal(3, c.GetProperty("position").GetInt32());
        Assert.True(c.GetProperty("incomplete").GetBoolean()); // English not scored yet
        Assert.Single(c.GetProperty("subjects").EnumerateArray());
    }

    [Fact]
    public async Task Compute_does_not_fill_subject_comment()
    {
        var s = await SetupAsync("No Auto Remark School");
        await ScoredClassAsync(s);

        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var math = SubjectRow(StudentRow(results, "Ada"), "Mathematics");
        Assert.Equal(JsonValueKind.Null, math.GetProperty("remark").ValueKind);
        Assert.Equal("A1", math.GetProperty("grade").GetString());
        Assert.Equal(JsonValueKind.Null, SubjectRow(StudentRow(results, "Ada"), "English").GetProperty("remark").ValueKind);
    }

    [Fact]
    public async Task Late_is_not_counted_as_present()
    {
        var s = await SetupAsync("Late Not Present School");
        var (ada, bayo, chi) = await ScoredClassAsync(s);
        await TakeRegisterAsync(s, Today.AddDays(-2), (ada, "Present"), (bayo, "Present"), (chi, "Present"));
        await TakeRegisterAsync(s, Today.AddDays(-1), (ada, "Absent"), (bayo, "Late"), (chi, "Present"));

        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var b = StudentRow(results, "Bayo");
        Assert.Equal(1, b.GetProperty("daysPresent").GetInt32());
        Assert.Equal(1, b.GetProperty("daysLate").GetInt32());
        Assert.Equal(0, b.GetProperty("daysAbsent").GetInt32());
        Assert.Equal(0, b.GetProperty("daysExcused").GetInt32());
        Assert.Equal(2, b.GetProperty("daysOpen").GetInt32());
    }

    [Fact]
    public async Task Attendance_counts_only_from_the_students_arrival()
    {
        var s = await SetupAsync("Transfer Result School");
        var (ada, _, _) = await ScoredClassAsync(s);
        // Ada has been there from the start; Dami joins after three registers were already taken.
        await TakeRegisterAsync(s, Today.AddDays(-5), (ada, "Present"));
        await TakeRegisterAsync(s, Today.AddDays(-4), (ada, "Present"));
        var dami = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Dami", "Wale"));
        await TakeRegisterAsync(s, Today.AddDays(-1), (ada, "Present"), (dami, "Present"));
        await TakeRegisterAsync(s, Today, (ada, "Present"), (dami, "Absent"));

        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var d = StudentRow(results, "Dami");
        Assert.Equal(1, d.GetProperty("daysPresent").GetInt32());
        Assert.Equal(2, d.GetProperty("daysOpen").GetInt32());
        Assert.Equal(4, StudentRow(results, "Ada").GetProperty("daysOpen").GetInt32());
    }

    [Fact]
    public async Task Edits_make_results_stale_and_they_recompute_before_publish()
    {
        var s = await SetupAsync("Stale Result School");
        var (_, bayo, _) = await ScoredClassAsync(s);
        var first =await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(1, StudentRow(first, "Ada").GetProperty("position").GetInt32());

        // Bayo's exam goes up: Bayo takes first place without anyone asking for a recompute.
        await ScoreAsync(s, s.MathId, bayo, 20, 20, 60);
        var edited = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(175m, StudentRow(edited, "Bayo").GetProperty("total").GetDecimal());
        Assert.Equal(1, StudentRow(edited, "Bayo").GetProperty("position").GetInt32());
        Assert.Equal(2, StudentRow(edited, "Ada").GetProperty("position").GetInt32());
        Assert.True(StudentRow(edited, "Chi").GetProperty("incomplete").GetBoolean());

        // Locking counts what is missing as zero, and publishing shows the locked figures.
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();
        var published = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.True(published.GetProperty("locked").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, published.GetProperty("publishedAt").ValueKind);
        var c = StudentRow(published, "Chi");
        Assert.False(c.GetProperty("incomplete").GetBoolean());
        Assert.Equal(25m, c.GetProperty("average").GetDecimal()); // after the lock a subject with no scores counts as 0: (50 + 0) / 2
        Assert.Equal(2, c.GetProperty("subjects").GetArrayLength());
        var english = SubjectRow(c, "English");
        Assert.Equal(0m, english.GetProperty("total").GetDecimal());
        Assert.Equal("F9", english.GetProperty("grade").GetString());
    }

    private async Task<Setup> PublishedClassAsync(string name)
    {
        var s = await SetupAsync(name);
        await ScoredClassAsync(s);
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();
        return s;
    }

    private static string ResultsUrl(Setup s, Guid classId) => $"/api/school/results/{classId}/{s.TermId}";

    private async Task<Guid> StudentIdOfAsync(SchoolKit.School school, string firstName) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.TenantId == school.TenantId && x.FirstName == firstName).Select(x => x.Id).FirstAsync(Ct));

    [Fact]
    public async Task A_transfer_after_publish_keeps_the_old_classes_published_figures()
    {
        var s = await PublishedClassAsync("Frozen Transfer School");
        var before = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var classB = await kit.ClassAsync(s.School, "B");
        (await s.School.Owner.PostAsJsonAsync($"/api/school/students/{await StudentIdOfAsync(s.School, "Ada")}/transfer", new { classId = classB }, Ct)).EnsureSuccessStatusCode();

        // The new class shows Ada with no published results and no subjects from the old class.
        var other = await Json(await s.School.Owner.GetAsync(ResultsUrl(s, classB), Ct));
        Assert.Equal(JsonValueKind.Null, other.GetProperty("publishedAt").ValueKind);
        Assert.Equal(0, StudentRow(other, "Ada").GetProperty("subjects").GetArrayLength());

        // The old class still shows what was published: Ada, her subjects, the class size and the positions.
        var after = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("publishedAt").ValueKind);
        Assert.Equal(3, after.GetProperty("students").GetArrayLength());
        Assert.Equal(2, StudentRow(after, "Ada").GetProperty("subjects").GetArrayLength());
        foreach (var name in new[] { "Ada", "Bayo", "Chi" })
        {
            Assert.Equal(StudentRow(before, name).GetProperty("position").GetInt32(), StudentRow(after, name).GetProperty("position").GetInt32());
            Assert.Equal(3, StudentRow(after, name).GetProperty("classSize").GetInt32());
        }
    }

    [Fact]
    public async Task A_withdrawal_after_publish_leaves_the_published_class_unchanged()
    {
        var s = await PublishedClassAsync("Frozen Withdrawal School");
        var before = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        (await s.School.Owner.PostAsJsonAsync($"/api/school/students/{await StudentIdOfAsync(s.School, "Ada")}/status", new { status = "Withdrawn", reason = "Moved away" }, Ct)).EnsureSuccessStatusCode();
        var after = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(3, after.GetProperty("students").GetArrayLength());
        foreach (var name in new[] { "Ada", "Bayo", "Chi" })
        {
            Assert.Equal(StudentRow(before, name).GetProperty("position").GetInt32(), StudentRow(after, name).GetProperty("position").GetInt32());
            Assert.Equal(3, StudentRow(after, name).GetProperty("classSize").GetInt32());
        }
    }

    [Fact]
    public async Task A_student_who_joins_after_publish_waits_for_the_next_publish()
    {
        var s = await PublishedClassAsync("Late Joiner School");
        await kit.StudentAsync(s.School, s.ClassId, "Late", "Joiner");
        var view = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(3, view.GetProperty("students").GetArrayLength());
        Assert.Equal(1, view.GetProperty("pendingStudents").GetInt32());
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();
        var again = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(4, again.GetProperty("students").GetArrayLength());
        Assert.Equal(0, again.GetProperty("pendingStudents").GetInt32());
    }

    [Fact]
    public async Task Unlocking_published_results_is_refused()
    {
        var s = await PublishedClassAsync("Unlock Published School");
        var problem = await ProblemAsync(await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unlock", null, Ct), HttpStatusCode.Conflict);
        Assert.Equal("Unpublish the results before unlocking.", problem.GetProperty("detail").GetString());
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unlock", null, Ct)).EnsureSuccessStatusCode();
    }

    private static object NewScale(string topGrade) => new
    {
        bands = new object[]
        {
            new { min = 0, max = 49, grade = "F", remark = "Fail" },
            new { min = 50, max = 89, grade = "P", remark = "Pass" },
            new { min = 90, max = 100, grade = topGrade, remark = "Top" },
        },
    };

    [Fact]
    public async Task A_grading_edit_updates_unpublished_results_but_not_published_ones()
    {
        var open = await SetupAsync("Band Open School");
        await ScoredClassAsync(open);
        var first = await Json(await open.School.Owner.GetAsync(ResultsUrl(open), Ct));
        Assert.Equal("A1", SubjectRow(StudentRow(first, "Ada"), "Mathematics").GetProperty("grade").GetString());
        (await open.School.Owner.PutAsJsonAsync("/api/school/grading", NewScale("AA"), Ct)).EnsureSuccessStatusCode();
        var second = await Json(await open.School.Owner.GetAsync(ResultsUrl(open), Ct));
        Assert.Equal("AA", SubjectRow(StudentRow(second, "Ada"), "Mathematics").GetProperty("grade").GetString());

        var frozen = await PublishedClassAsync("Band Published School");
        (await frozen.School.Owner.PutAsJsonAsync("/api/school/grading", NewScale("AA"), Ct)).EnsureSuccessStatusCode();
        var kept = await Json(await frozen.School.Owner.GetAsync(ResultsUrl(frozen), Ct));
        Assert.Equal("A1", SubjectRow(StudentRow(kept, "Ada"), "Mathematics").GetProperty("grade").GetString());
        // After an unpublish the new scale applies.
        (await frozen.School.Owner.PostAsync($"{ResultsUrl(frozen)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        var changed = await Json(await frozen.School.Owner.GetAsync(ResultsUrl(frozen), Ct));
        Assert.Equal("AA", SubjectRow(StudentRow(changed, "Ada"), "Mathematics").GetProperty("grade").GetString());
    }

    [Fact]
    public async Task A_register_edit_updates_attendance_on_the_next_view()
    {
        var s = await SetupAsync("Register Edit School");
        var (ada, bayo, chi) = await ScoredClassAsync(s);
        await TakeRegisterAsync(s, Today.AddDays(-2), (ada, "Present"), (bayo, "Present"), (chi, "Present"));
        var first = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(1, StudentRow(first, "Ada").GetProperty("daysPresent").GetInt32());
        await TakeRegisterAsync(s, Today.AddDays(-2), (ada, "Absent"), (bayo, "Present"), (chi, "Present"));
        var second = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(0, StudentRow(second, "Ada").GetProperty("daysPresent").GetInt32());
        Assert.Equal(1, StudentRow(second, "Ada").GetProperty("daysOpen").GetInt32());
    }

    [Fact]
    public async Task After_the_lock_a_missing_component_counts_as_zero()
    {
        var s = await SetupAsync("Zero Result School");
        var (_, _, chi) = await ScoredClassAsync(s);
        await ScoreAsync(s, s.EnglishId, chi, 20, null, null); // CA2 and exam missing
        var before = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.True(StudentRow(before, "Chi").GetProperty("incomplete").GetBoolean());
        Assert.Equal(20m, SubjectRow(StudentRow(before, "Chi"), "English").GetProperty("total").GetDecimal());

        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        var after = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        var c = StudentRow(after, "Chi");
        Assert.False(c.GetProperty("incomplete").GetBoolean());
        Assert.Equal(20m, SubjectRow(c, "English").GetProperty("total").GetDecimal());
        Assert.Equal("F9", SubjectRow(c, "English").GetProperty("grade").GetString());
        Assert.Equal(35m, c.GetProperty("average").GetDecimal()); // (50 + 20) / 2
    }

    [Fact]
    public async Task Publish_requires_a_locked_term()
    {
        var s = await SetupAsync("Publish Lock School");
        await ScoredClassAsync(s);
        var problem = await ProblemAsync(await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct), HttpStatusCode.Conflict);
        Assert.Contains("lock", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == s.School.TenantId && a.Action == "results.publish", Ct)));

        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == s.School.TenantId && a.Action == "results.unpublish", Ct)));
        var after = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("publishedAt").ValueKind);
    }

    [Fact]
    public async Task Publishing_emails_guardians_once()
    {
        var s = await SetupAsync("Notice School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ngozi", "Eze");
        var enrolment = await EnrolmentAsync(s.School, studentId);
        var mother = $"mum-{Guid.NewGuid():N}@example.com";
        await kit.GuardianAsync(s.School, studentId, "Mrs Eze", "+2348031234567", mother);
        await kit.GuardianAsync(s.School, studentId, "Mr Eze", "+2348037654321", null); // no email: nobody to send to
        await ScoreAsync(s, s.MathId, enrolment, 20, 20, 50);
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();

        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();
        var mails = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.results_published" && n.Recipient == mother).ToListAsync(Ct));
        var mail = Assert.Single(mails);
        Assert.Equal("Ngozi Eze", mail.Payload["child"]);
        Assert.EndsWith("/parent", mail.Payload["link"]);

        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode(); // publishing twice
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode(); // and again after an unpublish
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.results_published" && n.Recipient == mother, Ct)));
    }

    [Fact]
    public async Task Unpublished_results_are_invisible_to_parents()
    {
        var s = await SetupAsync("Hidden Results School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Ife", "Bello");
        var guardianId = await kit.GuardianAsync(s.School, studentId, "Mrs Bello", "+2348031112222", $"bello-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await kit.ParentAsync(s.School, guardianId);
        await ScoreAsync(s, s.MathId, await EnrolmentAsync(s.School, studentId), 20, 20, 50);
        // The staff results route is closed to parents, published or not.
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync(ResultsUrl(s), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Remarks_are_limited_by_role_and_survive_recomputation()
    {
        var s = await SetupAsync("Remark School");
        var (classTeacher, ctUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        var (subjectTeacher, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Subject teacher");
        var staffId = await StaffIdAsync(s.School, ctUser);
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        var (ada, _, _) = await ScoredClassAsync(s);

        var remarksUrl = $"{ResultsUrl(s)}/remarks";
        var set = await Json(await classTeacher.PutAsJsonAsync(remarksUrl, new { enrolmentId = ada, teacherRemark = "A joy to teach." }, Ct));
        Assert.Equal("A joy to teach.", StudentRow(set, "Ada").GetProperty("teacherRemark").GetString());
        // The class teacher cannot write the principal's remark, and an unrelated teacher can neither read nor write.
        await ProblemAsync(await classTeacher.PutAsJsonAsync(remarksUrl, new { enrolmentId = ada, principalRemark = "Well done." }, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await subjectTeacher.PutAsJsonAsync(remarksUrl, new { enrolmentId = ada, teacherRemark = "x" }, Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await subjectTeacher.GetAsync(ResultsUrl(s), Ct), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await classTeacher.GetAsync(ResultsUrl(s), Ct)).StatusCode);
        await ProblemAsync(await classTeacher.PostAsync($"{ResultsUrl(s)}/publish", null, Ct), HttpStatusCode.Forbidden);

        var principal = await Json(await s.School.Owner.PutAsJsonAsync(remarksUrl, new { enrolmentId = ada, principalRemark = "Well done." }, Ct));
        Assert.Equal("Well done.", StudentRow(principal, "Ada").GetProperty("principalRemark").GetString());
        Assert.Equal("A joy to teach.", StudentRow(principal, "Ada").GetProperty("teacherRemark").GetString());

        var tooLong = await ProblemAsync(await s.School.Owner.PutAsJsonAsync(remarksUrl, new { enrolmentId = ada, teacherRemark = new string('x', 501) }, Ct), HttpStatusCode.BadRequest);
        Assert.True(tooLong.GetProperty("errors").TryGetProperty("teacherRemark", out _));

        // A score edit recomputes the results and the remarks stay.
        await ScoreAsync(s, s.MathId, ada, 20, 20, 40);
        var again = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(150m, StudentRow(again, "Ada").GetProperty("total").GetDecimal());
        Assert.Equal("A joy to teach.", StudentRow(again, "Ada").GetProperty("teacherRemark").GetString());
        Assert.Equal("Well done.", StudentRow(again, "Ada").GetProperty("principalRemark").GetString());
    }

    [Fact]
    public async Task A_mistaken_student_can_be_deleted_after_their_results_page_was_viewed()
    {
        var s = await SetupAsync("History Result School");
        var studentId = await kit.StudentAsync(s.School, s.ClassId, "Temi", "Ola");
        Assert.Equal(HttpStatusCode.OK, (await s.School.Owner.GetAsync(ResultsUrl(s), Ct)).StatusCode); // computes an empty result row for Temi
        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync($"/api/school/students/{studentId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_class_with_results_cannot_be_deleted()
    {
        var s = await SetupAsync("Class Records School");
        await ScoredClassAsync(s);
        await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct)); // computes the stored results
        var classB = await kit.ClassAsync(s.School, "B");
        foreach (var name in new[] { "Ada", "Bayo", "Chi" })
            (await s.School.Owner.PostAsJsonAsync($"/api/school/students/{await StudentIdOfAsync(s.School, name)}/transfer", new { classId = classB }, Ct)).EnsureSuccessStatusCode();
        var problem = await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/classes/{s.ClassId}", Ct), HttpStatusCode.Conflict);
        Assert.Equal("This class has records. It can't be deleted.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_subject_of_a_locked_class_cannot_be_removed()
    {
        var s = await SetupAsync("Subject Records School");
        await ScoredClassAsync(s);
        var subjects = await Json(await s.School.Owner.GetAsync($"/api/school/classes/{s.ClassId}/subjects", Ct));
        Guid SubjectIdOf(Guid classSubjectId) => subjects.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == classSubjectId).GetProperty("subjectId").GetGuid();
        var physics = await ClassSubjectAsync(s.School, s.ClassId, "Physics", "PHY");
        subjects = await Json(await s.School.Owner.GetAsync($"/api/school/classes/{s.ClassId}/subjects", Ct));
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        var problem = await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/classes/{s.ClassId}/subjects/{SubjectIdOf(physics)}", Ct), HttpStatusCode.Conflict);
        Assert.Equal("Results exist for this subject. It can't be removed.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_student_with_published_results_cannot_be_deleted()
    {
        var s = await SetupAsync("Published Delete School");
        await ScoredClassAsync(s);
        var late = await kit.StudentAsync(s.School, s.ClassId, "Quiet", "Pupil"); // no scores at all
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/lock", null, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/publish", null, Ct)).EnsureSuccessStatusCode();
        await ProblemAsync(await s.School.Owner.DeleteAsync($"/api/school/students/{late}", Ct), HttpStatusCode.Conflict);
        (await s.School.Owner.PostAsync($"{ResultsUrl(s)}/unpublish", null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await s.School.Owner.DeleteAsync($"/api/school/students/{late}", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_departed_students_days_open_end_at_their_last_register()
    {
        var s = await SetupAsync("Departed Days School");
        var (ada, bayo, chi) = await ScoredClassAsync(s);
        foreach (var d in new[] { 5, 4, 3 }) await TakeRegisterAsync(s, Today.AddDays(-d), (ada, "Present"), (bayo, "Present"), (chi, "Present"));
        var classB = await kit.ClassAsync(s.School, "B");
        (await s.School.Owner.PostAsJsonAsync($"/api/school/students/{await StudentIdOfAsync(s.School, "Ada")}/transfer", new { classId = classB }, Ct)).EnsureSuccessStatusCode();
        foreach (var d in new[] { 2, 1 }) await TakeRegisterAsync(s, Today.AddDays(-d), (bayo, "Present"), (chi, "Present"));
        var results = await Json(await s.School.Owner.GetAsync(ResultsUrl(s), Ct));
        Assert.Equal(3, StudentRow(results, "Ada").GetProperty("daysOpen").GetInt32());
        Assert.Equal(3, StudentRow(results, "Ada").GetProperty("daysPresent").GetInt32());
        Assert.Equal(5, StudentRow(results, "Bayo").GetProperty("daysOpen").GetInt32());
    }

    private sealed record Grid(
        Setup Setup, HttpClient SubjectTeacher, HttpClient ClassTeacher, HttpClient OtherTeacher, Guid StudentId, Guid EnrolmentId);

    /// <summary>Mathematics is taught by the subject teacher. English is not. The class teacher is someone else.</summary>
    private async Task<Grid> SubjectGridAsync(string name)
    {
        var s = await SetupAsync(name);
        var (subjectTeacher, subjectUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Maths teacher");
        var (classTeacher, classUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        var (otherTeacher, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId = await StaffIdAsync(s.School, classUser) }, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.PutAsJsonAsync($"/api/school/class-subjects/{s.MathId}/teacher", new { staffId = await StaffIdAsync(s.School, subjectUser) }, Ct)).EnsureSuccessStatusCode();
        var (ada, _, _) = await ScoredClassAsync(s);
        (await s.School.Owner.PutAsJsonAsync($"{ResultsUrl(s)}/remarks", new { enrolmentId = ada, teacherRemark = "A joy to teach.", principalRemark = "Well done." }, Ct)).EnsureSuccessStatusCode();
        return new Grid(s, subjectTeacher, classTeacher, otherTeacher, await StudentIdOfAsync(s.School, "Ada"), ada);
    }

    private static string ChildUrl(Grid g, bool suggestions = false)
    {
        var url = $"/api/school/results/students/{g.StudentId}?termId={g.Setup.TermId}";
        return suggestions ? $"{url}&suggestions=true" : url;
    }

    private static List<string> SubjectNames(JsonElement student) =>
        student.GetProperty("subjects").EnumerateArray().Select(x => x.GetProperty("subject").GetString()!).OrderBy(x => x).ToList();

    [Fact]
    public async Task Subject_teacher_sees_only_their_column_on_the_class_grid()
    {
        var g = await SubjectGridAsync("Subject Column School");
        var results = await Json(await g.SubjectTeacher.GetAsync(ResultsUrl(g.Setup), Ct));
        var ada = StudentRow(results, "Ada");
        Assert.Equal(160m, ada.GetProperty("total").GetDecimal());
        Assert.Equal(["Mathematics"], SubjectNames(ada));
        Assert.Equal(90m, SubjectRow(ada, "Mathematics").GetProperty("total").GetDecimal());
        Assert.Equal(["Mathematics"], SubjectNames(StudentRow(results, "Bayo")));
        Assert.Equal(["Mathematics"], SubjectNames(StudentRow(results, "Chi")));

        var full = await Json(await g.ClassTeacher.GetAsync(ResultsUrl(g.Setup), Ct));
        Assert.Equal(160m, StudentRow(full, "Ada").GetProperty("total").GetDecimal());
        Assert.Equal(["English", "Mathematics"], SubjectNames(StudentRow(full, "Ada")));
        var owner = await Json(await g.Setup.School.Owner.GetAsync(ResultsUrl(g.Setup), Ct));
        Assert.Equal(["English", "Mathematics"], SubjectNames(StudentRow(owner, "Ada")));

        await ProblemAsync(await g.OtherTeacher.GetAsync(ResultsUrl(g.Setup), Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Class_teacher_sees_every_subject_on_the_child()
    {
        var g = await SubjectGridAsync("Class Child School");
        var child = await Json(await g.ClassTeacher.GetAsync(ChildUrl(g), Ct));
        Assert.Equal(g.StudentId, child.GetProperty("studentId").GetGuid());
        Assert.Equal(g.EnrolmentId, child.GetProperty("enrolmentId").GetGuid());
        Assert.Equal("Ada Zed", child.GetProperty("name").GetString());
        Assert.Equal(g.Setup.ClassId, child.GetProperty("classId").GetGuid());
        Assert.Equal("JSS 1A", child.GetProperty("className").GetString());
        Assert.Equal(g.Setup.TermId, child.GetProperty("termId").GetGuid());
        Assert.False(child.GetProperty("locked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, child.GetProperty("publishedAt").ValueKind);
        Assert.Equal(0, child.GetProperty("daysPresent").GetInt32());
        Assert.Equal(0, child.GetProperty("daysAbsent").GetInt32());
        Assert.Equal(0, child.GetProperty("daysLate").GetInt32());
        Assert.Equal(0, child.GetProperty("daysExcused").GetInt32());
        Assert.Equal(["English", "Mathematics"], SubjectNames(child));
        Assert.Equal(0, child.GetProperty("developmental").GetArrayLength());
        Assert.Contains(child.GetProperty("psychomotor").EnumerateArray(), x => x.GetProperty("name").GetString() == "Handwriting");
        Assert.Contains(child.GetProperty("affective").EnumerateArray(), x => x.GetProperty("name").GetString() == "Punctuality");
        Assert.Equal("A joy to teach.", child.GetProperty("teacherRemark").GetString());
        Assert.Equal("Well done.", child.GetProperty("principalRemark").GetString());
        Assert.Equal(JsonValueKind.Null, child.GetProperty("suggestions").ValueKind);
    }

    [Fact]
    public async Task Subject_teacher_child_omits_other_subjects()
    {
        var g = await SubjectGridAsync("Subject Child School");
        var child = await Json(await g.SubjectTeacher.GetAsync(ChildUrl(g, suggestions: true), Ct));
        Assert.Equal(["Mathematics"], SubjectNames(child));
        Assert.Equal(90m, SubjectRow(child, "Mathematics").GetProperty("total").GetDecimal());
        Assert.Equal(g.Setup.MathId, SubjectRow(child, "Mathematics").GetProperty("classSubjectId").GetGuid());
        Assert.True(child.GetProperty("psychomotor").GetArrayLength() > 0);
        Assert.True(child.GetProperty("affective").GetArrayLength() > 0);
        Assert.Equal(0, child.GetProperty("developmental").GetArrayLength());
        Assert.Equal(0, child.GetProperty("daysPresent").GetInt32());
        Assert.Equal(0, child.GetProperty("daysAbsent").GetInt32());
        Assert.Equal(0, child.GetProperty("daysLate").GetInt32());
        Assert.Equal(0, child.GetProperty("daysExcused").GetInt32());
        Assert.Equal("A joy to teach.", child.GetProperty("teacherRemark").GetString());
        Assert.Equal("Well done.", child.GetProperty("principalRemark").GetString());
        Assert.True(child.GetProperty("suggestions").TryGetProperty("teacherOverall", out _));
        Assert.True(child.GetProperty("suggestions").TryGetProperty("subject", out _));
        Assert.True(child.GetProperty("suggestions").TryGetProperty("principalOverall", out _));
    }

    [Fact]
    public async Task Foreign_id_is_404()
    {
        var g = await SubjectGridAsync("Foreign Child School");
        var classB = await kit.ClassAsync(g.Setup.School, "B");
        var otherId = await kit.StudentAsync(g.Setup.School, classB, "Ngozi", "Kanu");
        var foreign = await ProblemAsync(
            await g.ClassTeacher.GetAsync($"/api/school/results/students/{otherId}?termId={g.Setup.TermId}", Ct),
            HttpStatusCode.NotFound);
        Assert.Equal("Child was not found.", foreign.GetProperty("detail").GetString());
        var bySubject = await ProblemAsync(
            await g.SubjectTeacher.GetAsync($"/api/school/results/students/{otherId}?termId={g.Setup.TermId}", Ct),
            HttpStatusCode.NotFound);
        Assert.Equal("Child was not found.", bySubject.GetProperty("detail").GetString());
        var unknown = await ProblemAsync(
            await g.ClassTeacher.GetAsync($"/api/school/results/students/{Guid.NewGuid()}?termId={g.Setup.TermId}", Ct),
            HttpStatusCode.NotFound);
        Assert.Equal("Child was not found.", unknown.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Several_classes_are_404_for_a_teacher_of_neither()
    {
        var s = await SetupAsync("Two Class Child School");
        var (classTeacher, classUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        var (stranger, _) = await kit.StaffAsync(s.School, MemberRole.Staff, "Other teacher");
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId = await StaffIdAsync(s.School, classUser) }, Ct)).EnsureSuccessStatusCode();
        await ScoredClassAsync(s);
        (await s.School.Owner.GetAsync(ResultsUrl(s), Ct)).EnsureSuccessStatusCode();
        var classB = await kit.ClassAsync(s.School, "B");
        var adaId = await StudentIdOfAsync(s.School, "Ada");
        (await s.School.Owner.PostAsJsonAsync($"/api/school/students/{adaId}/transfer", new { classId = classB }, Ct)).EnsureSuccessStatusCode();
        (await s.School.Owner.GetAsync(ResultsUrl(s, classB), Ct)).EnsureSuccessStatusCode();

        var url = $"/api/school/results/students/{adaId}?termId={s.TermId}";
        const string choose = "This student was in more than one class this term. Choose the class.";
        var forClassTeacher = await ProblemAsync(await classTeacher.GetAsync(url, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(choose, forClassTeacher.GetProperty("detail").GetString());
        var hidden = await ProblemAsync(await stranger.GetAsync(url, Ct), HttpStatusCode.NotFound);
        Assert.Equal("Child was not found.", hidden.GetProperty("detail").GetString());
        var forLeadership = await ProblemAsync(await s.School.Owner.GetAsync(url, Ct), HttpStatusCode.BadRequest);
        Assert.Equal(choose, forLeadership.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Bursar_cannot_read_results()
    {
        var s = await SetupAsync("Bursar Results School");
        await ScoredClassAsync(s);
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await ProblemAsync(await bursar.GetAsync(ResultsUrl(s), Ct), HttpStatusCode.Forbidden);
        var ada = await StudentIdOfAsync(s.School, "Ada");
        await ProblemAsync(await bursar.GetAsync($"/api/school/results/students/{ada}?termId={s.TermId}", Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await bursar.GetAsync($"/api/school/results/students/{Guid.NewGuid()}?termId={s.TermId}", Ct), HttpStatusCode.Forbidden);
    }
}
