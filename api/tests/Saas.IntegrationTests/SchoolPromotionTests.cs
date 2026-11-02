using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink Task 8: promotion to the next session (preview, overrides, repeat, graduate, once only) and ID card PDFs.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolPromotionTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<Guid> LevelAsync(HttpClient owner, string name, Stage stage, int order, bool logins = false) =>
        (await Json(await owner.PostAsJsonAsync("/api/school/levels", new { name, stage = stage.ToString(), order, hasStudentLogins = logins }, Ct))).GetProperty("id").GetGuid();

    private static async Task<Guid> ClassAsync(HttpClient owner, Guid levelId, string arm) =>
        (await Json(await owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm }, Ct))).GetProperty("id").GetGuid();

    private static object Body(string name = "2027/2028") => new { sessionName = name, start = "2027-09-06", end = "2028-07-28", overrides = Array.Empty<object>() };

    private sealed record Ladder(SchoolKit.School School, Guid Jss1A, Guid Jss2A, Guid Jss3A);

    /// <summary>JSS 1, JSS 2 and JSS 3 (the top level) with one class each in the current session.</summary>
    private async Task<Ladder> LadderAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var j1 = await LevelAsync(school.Owner, "JSS 1", Stage.Jss, 10, true);
        var j2 = await LevelAsync(school.Owner, "JSS 2", Stage.Jss, 11, true);
        var j3 = await LevelAsync(school.Owner, "JSS 3", Stage.Jss, 12, true);
        return new Ladder(school, await ClassAsync(school.Owner, j1, "A"), await ClassAsync(school.Owner, j2, "A"), await ClassAsync(school.Owner, j3, "A"));
    }

    private static async Task<JsonElement> ClassesOfAsync(HttpClient owner, Guid sessionId) =>
        await Json(await owner.GetAsync($"/api/school/classes?sessionId={sessionId}", Ct));

    [Fact]
    public async Task Promotion_moves_classes_up_and_graduates_the_final_level()
    {
        var l = await LadderAsync("Promo School");
        var owner = l.School.Owner;
        var mover = await kit.StudentAsync(l.School, l.Jss1A, "Mover");
        var leaver = await kit.StudentAsync(l.School, l.Jss3A, "Leaver");
        var withdrawn = await kit.StudentAsync(l.School, l.Jss1A, "Gone");
        await Json(await owner.PostAsJsonAsync($"/api/school/students/{withdrawn}/status", new { status = "Withdrawn", reason = "Moved away" }, Ct));
        var leaverLogin = await kit.StudentLoginAsync(l.School, leaver);

        // A subject taught in JSS 1A by a teacher is copied with its teacher.
        var teacher = await kit.StaffAsync(l.School, MemberRole.Staff, "Maths teacher");
        var staffId = (await Json(await owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(s => s.GetProperty("userId").GetGuid() == teacher.UserId).GetProperty("staffId").GetGuid();
        var subjectId = (await Json(await owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct))).GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync($"/api/school/classes/{l.Jss1A}/subjects", new { subjectId, teacherStaffId = staffId }, Ct));
        await Json(await owner.PutAsJsonAsync($"/api/school/classes/{l.Jss1A}/class-teacher", new { staffId }, Ct));

        var preview = (await Json(await owner.GetAsync("/api/school/promotion/preview", Ct))).GetProperty("classes").EnumerateArray().ToList();
        Assert.Equal(3, preview.Count);
        var fromJss1 = preview.Single(p => p.GetProperty("fromClassId").GetGuid() == l.Jss1A);
        Assert.Equal("JSS 2A", fromJss1.GetProperty("toClassName").GetString());
        var moverRow = Assert.Single(fromJss1.GetProperty("students").EnumerateArray()); // the withdrawn student is not listed
        Assert.Equal(mover, moverRow.GetProperty("studentId").GetGuid());
        Assert.Equal("Promote", moverRow.GetProperty("action").GetString());
        var fromJss3 = preview.Single(p => p.GetProperty("fromClassId").GetGuid() == l.Jss3A);
        Assert.Equal("Graduate", fromJss3.GetProperty("toClassName").GetString());
        Assert.Equal("Graduate", fromJss3.GetProperty("students")[0].GetProperty("action").GetString());

        var result = await Json(await owner.PostAsJsonAsync("/api/school/promotion", Body(), Ct));
        var newSessionId = result.GetProperty("sessionId").GetGuid();

        await api.WithDbAsync(async db =>
        {
            var sessions = await db.AcademicSessions.IgnoreQueryFilters().Where(s => s.TenantId == l.School.TenantId).ToListAsync(Ct);
            Assert.Equal(2, sessions.Count);
            Assert.Equal(newSessionId, sessions.Single(s => s.IsCurrent).Id);
            var terms = await db.Terms.IgnoreQueryFilters().Where(t => t.SessionId == newSessionId).OrderBy(t => t.Number).ToListAsync(Ct);
            Assert.Equal([1, 2, 3], terms.Select(t => t.Number));
            Assert.Equal([true, false, false], terms.Select(t => t.IsCurrent));
            Assert.Equal(new DateOnly(2027, 9, 6), terms[0].StartDate);
            Assert.Single(await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == l.School.TenantId && t.IsCurrent).ToListAsync(Ct));

            var copied = await db.SchoolClasses.IgnoreQueryFilters().Where(c => c.SessionId == newSessionId).OrderBy(c => c.Name).ToListAsync(Ct);
            Assert.Equal(["JSS 1A", "JSS 2A", "JSS 3A"], copied.Select(c => c.Name));
            var newJss1 = copied.Single(c => c.Name == "JSS 1A");
            Assert.Equal(staffId, newJss1.ClassTeacherStaffId);
            var cs = await db.ClassSubjects.IgnoreQueryFilters().SingleAsync(x => x.ClassId == newJss1.Id, Ct);
            Assert.Equal(subjectId, cs.SubjectId);
            Assert.Equal(staffId, cs.TeacherStaffId);

            var moverEnrol = await db.Enrolments.IgnoreQueryFilters().SingleAsync(e => e.StudentId == mover && e.SessionId == newSessionId, Ct);
            Assert.Equal(copied.Single(c => c.Name == "JSS 2A").Id, moverEnrol.ClassId);
            Assert.False(await db.Enrolments.IgnoreQueryFilters().AnyAsync(e => e.StudentId == leaver && e.SessionId == newSessionId, Ct));
            Assert.False(await db.Enrolments.IgnoreQueryFilters().AnyAsync(e => e.StudentId == withdrawn && e.SessionId == newSessionId, Ct));
            Assert.Equal(StudentStatus.Graduated, (await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == leaver, Ct)).Status);
            Assert.Equal(StudentStatus.Withdrawn, (await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == withdrawn, Ct)).Status);

            // The graduate's login is signed out.
            var userId = (await db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == leaver, Ct)).UserId!.Value;
            Assert.DoesNotContain(await db.RefreshTokens.IgnoreQueryFilters().Where(t => t.UserId == userId).ToListAsync(Ct), t => t.RevokedAt == null);
            return true;
        });
        _ = leaverLogin;

        var current = await Json(await owner.GetAsync($"/api/school/students/{mover}", Ct));
        Assert.Equal("JSS 2A", current.GetProperty("className").GetString());
    }

    [Fact]
    public async Task Held_back_students_repeat()
    {
        var l = await LadderAsync("Repeat School");
        var owner = l.School.Owner;
        var held = await kit.StudentAsync(l.School, l.Jss1A, "Held");
        var moved = await kit.StudentAsync(l.School, l.Jss1A, "Moved");
        var senior = await kit.StudentAsync(l.School, l.Jss3A, "Senior");
        var jumper = await kit.StudentAsync(l.School, l.Jss1A, "Jumper");

        var body = new
        {
            sessionName = "2027/2028", start = "2027-09-06", end = "2028-07-28",
            overrides = new object[]
            {
                new { studentId = held, action = "Repeat" },
                new { studentId = senior, action = "Repeat" }, // a final-year student held back stays in JSS 3
                new { studentId = jumper, action = "Promote", classId = l.Jss3A }, // moved to a named class (id of the current class)
            },
        };
        await Json(await owner.PostAsJsonAsync("/api/school/promotion", body, Ct));

        async Task<string?> ClassOf(Guid id) => (await Json(await owner.GetAsync($"/api/school/students/{id}", Ct))).GetProperty("className").GetString();
        Assert.Equal("JSS 1A", await ClassOf(held));
        Assert.Equal("JSS 2A", await ClassOf(moved));
        Assert.Equal("JSS 3A", await ClassOf(senior));
        Assert.Equal("JSS 3A", await ClassOf(jumper));
        Assert.Equal("Active", (await Json(await owner.GetAsync($"/api/school/students/{senior}", Ct))).GetProperty("status").GetString());

        // Unknown student or class in an override is refused before anything is saved.
        var l2 = await LadderAsync("Bad Override School");
        var bad = await l2.School.Owner.PostAsJsonAsync("/api/school/promotion",
            new { sessionName = "2027/2028", start = "2027-09-06", end = "2028-07-28", overrides = new object[] { new { studentId = Guid.NewGuid(), action = "Repeat" } } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Single(await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(s => s.TenantId == l2.School.TenantId).ToListAsync(Ct)));
    }

    [Fact]
    public async Task Promotion_runs_once_per_session()
    {
        var l = await LadderAsync("Once School");
        var owner = l.School.Owner;
        await kit.StudentAsync(l.School, l.Jss1A, "Solo");
        await Json(await owner.PostAsJsonAsync("/api/school/promotion", Body(), Ct));

        // A second click with the same details is refused: the session exists.
        var again = await owner.PostAsJsonAsync("/api/school/promotion", Body(), Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // A promotion started from the old session (a stale page, the old session made current again) is refused once a later session exists.
        await api.WithDbAsync(async db =>
        {
            var sessions = await db.AcademicSessions.IgnoreQueryFilters().Where(s => s.TenantId == l.School.TenantId).ToListAsync(Ct);
            foreach (var s in sessions) s.IsCurrent = false;
            await db.SaveChangesAsync(Ct);
            sessions.OrderBy(s => s.StartDate).First().IsCurrent = true;
            return await db.SaveChangesAsync(Ct);
        });
        var stale = await owner.PostAsJsonAsync("/api/school/promotion", Body("2028/2029"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.Equal("The next session has already been started.", problem.GetProperty("detail").GetString());
        Assert.Equal(2, await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().CountAsync(s => s.TenantId == l.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Promotion_is_for_leadership_and_the_arm_falls_back_to_the_first_arm()
    {
        var l = await LadderAsync("Arm School");
        var owner = l.School.Owner;
        // JSS 1B has no JSS 2B: its students go to the first arm of JSS 2.
        var jss1 = (await Json(await owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().Single(x => x.GetProperty("name").GetString() == "JSS 1").GetProperty("id").GetGuid();
        var jss1B = await ClassAsync(owner, jss1, "B");
        var student = await kit.StudentAsync(l.School, jss1B, "Arm");

        var bursar = await kit.StaffAsync(l.School, MemberRole.Accountant, "Bursar");
        var teacher = await kit.StaffAsync(l.School, MemberRole.Staff, "Teacher");
        foreach (var client in new[] { bursar.Client, teacher.Client })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/school/promotion/preview", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/school/promotion", Body(), Ct)).StatusCode);
        }

        var preview = (await Json(await owner.GetAsync("/api/school/promotion/preview", Ct))).GetProperty("classes").EnumerateArray().Single(p => p.GetProperty("fromClassId").GetGuid() == jss1B);
        Assert.Equal("JSS 2A", preview.GetProperty("toClassName").GetString());
        await Json(await owner.PostAsJsonAsync("/api/school/promotion", Body(), Ct));
        Assert.Equal("JSS 2A", (await Json(await owner.GetAsync($"/api/school/students/{student}", Ct))).GetProperty("className").GetString());
    }

    [Fact]
    public async Task Students_without_a_class_must_be_placed_before_promotion()
    {
        var l = await LadderAsync("Unplaced School");
        var owner = l.School.Owner;
        var placed = await kit.StudentAsync(l.School, l.Jss1A, "Placed");
        var lost = await kit.StudentAsync(l.School, l.Jss1A, "Lost");
        var lost2 = await kit.StudentAsync(l.School, l.Jss1A, "Lost2");
        await api.WithDbAsync(async db =>
        {
            db.Enrolments.RemoveRange(await db.Enrolments.IgnoreQueryFilters().Where(e => e.StudentId == lost || e.StudentId == lost2).ToListAsync(Ct));
            return await db.SaveChangesAsync(Ct);
        });

        var preview = await Json(await owner.GetAsync("/api/school/promotion/preview", Ct));
        Assert.Equal(new[] { lost, lost2 }.Order(), preview.GetProperty("unplaced").EnumerateArray().Select(u => u.GetProperty("studentId").GetGuid()).Order());

        var refused = await owner.PostAsJsonAsync("/api/school/promotion", Body(), Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Some active students have no class this session. Place them in a class first.",
            (await refused.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("detail").GetString());
        Assert.Single(await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(s => s.TenantId == l.School.TenantId).ToListAsync(Ct)));

        // Placing one in a class and graduating the other lets the promotion run.
        var body = new
        {
            sessionName = "2027/2028", start = "2027-09-06", end = "2028-07-28",
            overrides = new object[] { new { studentId = lost, action = "Promote", classId = l.Jss2A }, new { studentId = lost2, action = "Graduate" } },
        };
        await Json(await owner.PostAsJsonAsync("/api/school/promotion", body, Ct));
        Assert.Equal("JSS 2A", (await Json(await owner.GetAsync($"/api/school/students/{lost}", Ct))).GetProperty("className").GetString());
        Assert.Equal("JSS 2A", (await Json(await owner.GetAsync($"/api/school/students/{placed}", Ct))).GetProperty("className").GetString());
        Assert.Equal("Graduated", (await Json(await owner.GetAsync($"/api/school/students/{lost2}", Ct))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Promotion_validates_the_session()
    {
        var l = await LadderAsync("Validate School");
        var owner = l.School.Owner;
        var noName = await owner.PostAsJsonAsync("/api/school/promotion", new { sessionName = " ", start = "2027-09-06", end = "2028-07-28", overrides = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        var backwards = await owner.PostAsJsonAsync("/api/school/promotion", new { sessionName = "2027/2028", start = "2028-07-28", end = "2027-09-06", overrides = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Single(await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(s => s.TenantId == l.School.TenantId).ToListAsync(Ct)));
    }

    // ---------- ID cards ----------

    private static int PageCount(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count;

    [Fact]
    public async Task Id_cards_render_a_pdf_for_a_class()
    {
        var school = await kit.SchoolAsync("Card School");
        var classId = await kit.ClassAsync(school);
        for (var i = 0; i < 9; i++) await kit.StudentAsync(school, classId, $"Pupil{i}");

        var response = await school.Owner.GetAsync($"/api/school/students/id-cards.pdf?classId={classId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(2, PageCount(bytes)); // ceil(9 / 8)

        // A student id outside the class matches nothing.
        var one = await school.Owner.GetAsync($"/api/school/students/id-cards.pdf?classId={classId}&studentIds={Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, one.StatusCode); // nothing matched
    }

    [Fact]
    public async Task Id_cards_can_be_picked_by_student_and_are_for_leadership_and_the_bursar()
    {
        var school = await kit.SchoolAsync("Card Pick School");
        var classId = await kit.ClassAsync(school);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await kit.StudentAsync(school, classId, $"Pick{i}"));

        // One student has a photo (read from private storage), the others get the initials placeholder.
        var photo = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        await Json(await school.Owner.PostAsync($"/api/school/students/{ids[0]}/photo", new MultipartFormDataContent { { photo, "file", "photo" } }, Ct));

        var picked = await school.Owner.GetAsync($"/api/school/students/id-cards.pdf?studentIds={ids[0]},{ids[1]}", Ct);
        Assert.Equal(HttpStatusCode.OK, picked.StatusCode);
        Assert.Equal(1, PageCount(await picked.Content.ReadAsByteArrayAsync(Ct)));

        var bursar = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.OK, (await bursar.Client.GetAsync($"/api/school/students/id-cards.pdf?classId={classId}", Ct)).StatusCode);
        var teacher = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.GetAsync($"/api/school/students/id-cards.pdf?classId={classId}", Ct)).StatusCode);

        // A class or students are required.
        Assert.Equal(HttpStatusCode.BadRequest, (await school.Owner.GetAsync("/api/school/students/id-cards.pdf", Ct)).StatusCode);
    }
}
