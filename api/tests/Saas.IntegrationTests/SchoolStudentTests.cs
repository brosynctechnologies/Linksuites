using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink students: enrolment in the current session, unique admission numbers, the plan limit, status changes, guardians and private photos.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolStudentTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    // A 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

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

    private static async Task<Guid> ClassAsync(HttpClient owner, string arm)
    {
        var levels = (await Json(await owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        var level = levels.FirstOrDefault(l => l.GetProperty("name").GetString() == "JSS 1");
        var levelId = level.ValueKind != JsonValueKind.Undefined
            ? level.GetProperty("id").GetGuid()
            : (await Json(await owner.PostAsJsonAsync("/api/school/levels", new { name = "JSS 1", stage = "Jss", order = 10, hasStudentLogins = true }, Ct))).GetProperty("id").GetGuid();
        return (await Json(await owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm }, Ct))).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AddStudentAsync(HttpClient client, Guid classId, string admissionNo, string first = "Ada", string last = "Obi") =>
        client.PostAsJsonAsync("/api/school/students", new { admissionNo, firstName = first, lastName = last, gender = "Female", classId }, Ct);

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string query = "") =>
        (await Json(await client.GetAsync($"/api/school/students{query}", Ct))).EnumerateArray().ToList();

    private static async Task<Guid> StaffIdAsync(HttpClient owner, Guid userId) =>
        (await Json(await owner.GetAsync("/api/school/staff", Ct))).EnumerateArray()
            .Single(s => s.GetProperty("userId").GetGuid() == userId).GetProperty("staffId").GetGuid();

    private static MultipartFormDataContent PhotoForm(byte[] bytes, string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "photo" } };
    }

    [Fact]
    public async Task Student_is_created_with_an_enrolment_in_the_current_session()
    {
        var school = await kit.SchoolAsync("Enrol School");
        var classId = await ClassAsync(school.Owner, "A");

        var created = await Json(await AddStudentAsync(school.Owner, classId, "ADM-001"));
        Assert.Equal("ADM-001", created.GetProperty("admissionNo").GetString());
        Assert.Equal("Active", created.GetProperty("status").GetString());
        Assert.Equal(classId, created.GetProperty("classId").GetGuid());
        Assert.Equal("JSS 1A", created.GetProperty("className").GetString());

        var enrolment = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().SingleAsync(e => e.StudentId == created.GetProperty("id").GetGuid(), Ct));
        var currentSession = await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == school.TenantId && s.IsCurrent, Ct));
        Assert.Equal(currentSession.Id, enrolment.SessionId);
        Assert.Equal(classId, enrolment.ClassId);

        // The checklist item is done and the list shows the student.
        var setup = await Json(await school.Owner.GetAsync("/api/school/setup", Ct));
        Assert.True(setup.GetProperty("checklist").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "students").GetProperty("done").GetBoolean());
        Assert.Single(await ListAsync(school.Owner, $"?classId={classId}"));

        // A class of another session is refused.
        var otherSession = await api.WithDbAsync(async db =>
        {
            var s = new AcademicSession { TenantId = school.TenantId, Name = "2020/2021", StartDate = new(2020, 9, 1), EndDate = new(2021, 8, 31) };
            db.AcademicSessions.Add(s);
            await db.SaveChangesAsync(Ct);
            return s.Id;
        });
        var oldClass = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes",
            new { levelId = (await Json(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().First().GetProperty("id").GetGuid(), arm = "Z", sessionId = otherSession }, Ct))).GetProperty("id").GetGuid();
        var problem = await ProblemAsync(await AddStudentAsync(school.Owner, oldClass, "ADM-002"), HttpStatusCode.BadRequest);
        Assert.Contains("classId", problem.GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Admission_numbers_are_unique_per_school()
    {
        var school = await kit.SchoolAsync("Unique School");
        var classId = await ClassAsync(school.Owner, "A");
        await Json(await AddStudentAsync(school.Owner, classId, "ADM-100"));

        var clash = await ProblemAsync(await AddStudentAsync(school.Owner, classId, "adm-100", "Bayo"), HttpStatusCode.Conflict);
        Assert.Contains("admission number", clash.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        // Another school may use the same number.
        var other = await kit.SchoolAsync("Other Unique School");
        var otherClass = await ClassAsync(other.Owner, "A");
        await Json(await AddStudentAsync(other.Owner, otherClass, "ADM-100"));
    }

    [Fact]
    public async Task Student_limit_is_enforced()
    {
        var school = await kit.SchoolAsync("Limit School"); // Free: 50 students
        var classId = await ClassAsync(school.Owner, "A");
        for (var i = 1; i <= 50; i++)
            Assert.Equal(HttpStatusCode.OK, (await AddStudentAsync(school.Owner, classId, $"L-{i:000}", $"Pupil{i}")).StatusCode);

        var over = await ProblemAsync(await AddStudentAsync(school.Owner, classId, "L-051", "Extra"), HttpStatusCode.PaymentRequired);
        Assert.Contains("50", over.GetProperty("detail").GetString());

        // Usage counts the active students.
        var usage = (await Json(await school.Owner.GetAsync("/api/billing", Ct))).GetProperty("usage").GetProperty("items");
        Assert.Contains(usage.EnumerateArray(), u => u.GetProperty("key").GetString() == "students" && u.GetProperty("used").GetInt32() == 50);

        // Withdrawing one frees a place.
        var first = (await ListAsync(school.Owner)).First();
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{first.GetProperty("id").GetGuid()}/status",
            new { status = "Withdrawn", date = "2026-10-01", reason = "Moved away" }, Ct));
        Assert.Equal(HttpStatusCode.OK, (await AddStudentAsync(school.Owner, classId, "L-051", "Extra")).StatusCode);
    }

    [Fact]
    public async Task Withdrawn_students_keep_their_records_but_leave_the_class_list()
    {
        var school = await kit.SchoolAsync("Withdraw School");
        var classId = await ClassAsync(school.Owner, "A");
        var kept = await Json(await AddStudentAsync(school.Owner, classId, "W-1", "Keep"));
        var leaver = await Json(await AddStudentAsync(school.Owner, classId, "W-2", "Leave"));
        var leaverId = leaver.GetProperty("id").GetGuid();

        var updated = await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{leaverId}/status",
            new { status = "Withdrawn", date = "2026-10-05", reason = "Family relocated" }, Ct));
        Assert.Equal("Withdrawn", updated.GetProperty("status").GetString());
        Assert.Equal("Family relocated", updated.GetProperty("statusReason").GetString());

        var classList = await ListAsync(school.Owner, $"?classId={classId}");
        Assert.Equal([kept.GetProperty("id").GetGuid()], classList.Select(s => s.GetProperty("id").GetGuid()));

        // The record, enrolment and a filtered list still have the student.
        var detail = await Json(await school.Owner.GetAsync($"/api/school/students/{leaverId}", Ct));
        Assert.Equal("Withdrawn", detail.GetProperty("status").GetString());
        Assert.Equal(classId, detail.GetProperty("classId").GetGuid());
        Assert.Single(await ListAsync(school.Owner, "?status=Withdrawn"));
        Assert.Equal(2, (await ListAsync(school.Owner, "?status=all")).Count);

        // Setup checklist and usage count active students only.
        Assert.Equal(1, await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().CountAsync(s => s.TenantId == school.TenantId && s.Status == StudentStatus.Active, Ct)));

        // The status change is audited.
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == school.TenantId && a.EntityType == nameof(Student) && a.EntityId == leaverId.ToString(), Ct)));
    }

    [Fact]
    public async Task Photos_are_private()
    {
        var school = await kit.SchoolAsync("Photo School");
        var classId = await ClassAsync(school.Owner, "A");
        var student = await Json(await AddStudentAsync(school.Owner, classId, "P-1"));
        var id = student.GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, student.GetProperty("photoUrl").ValueKind);

        // Not an image: refused.
        var bad = await school.Owner.PostAsync($"/api/school/students/{id}/photo", PhotoForm("not an image"u8.ToArray(), "image/png"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var withPhoto = await Json(await school.Owner.PostAsync($"/api/school/students/{id}/photo", PhotoForm(Png), Ct));
        var link = withPhoto.GetProperty("photoUrl").GetString()!;
        Assert.Contains("/api/school/photos/", link);

        // The signed link works without signing in.
        var path = new Uri(link).PathAndQuery;
        var anonymous = api.CreateClient();
        var ok = await anonymous.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/webp", ok.Content.Headers.ContentType?.MediaType);

        // A tampered signature and an expired link are refused.
        var parts = path.Split('/');
        parts[^1] = new string(parts[^1].Reverse().ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);
        parts = path.Split('/');
        parts[^2] = "1000000000";
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);

        // The storage object is never public.
        var key = await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.Id == id).Select(s => s.PhotoKey).SingleAsync(Ct));
        Assert.NotNull(key);
        Assert.StartsWith("private/", key);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/uploads/{key}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Siblings_share_one_guardian_matched_by_normalised_phone_or_email()
    {
        var school = await kit.SchoolAsync("Guardian School");
        var classId = await ClassAsync(school.Owner, "A");
        var a = (await Json(await AddStudentAsync(school.Owner, classId, "G-1", "Ada"))).GetProperty("id").GetGuid();
        var b = (await Json(await AddStudentAsync(school.Owner, classId, "G-2", "Bola"))).GetProperty("id").GetGuid();
        var c = (await Json(await AddStudentAsync(school.Owner, classId, "G-3", "Chike"))).GetProperty("id").GetGuid();

        var first = await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{a}/guardians",
            new { fullName = "Mrs Obi", phone = "0803 123 4567", email = "Obi@Example.com", relationship = "Mother" }, Ct));
        var g1 = first.GetProperty("guardians").EnumerateArray().Single();
        Assert.Equal("+2348031234567", g1.GetProperty("phone").GetString());
        Assert.Equal("obi@example.com", g1.GetProperty("email").GetString());
        Assert.True(g1.GetProperty("isPrimary").GetBoolean());

        var second = await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{b}/guardians",
            new { fullName = "Mrs Obi", phone = "+234-803-123-4567", relationship = "Mother" }, Ct));
        Assert.Equal(g1.GetProperty("guardianId").GetGuid(), second.GetProperty("guardians").EnumerateArray().Single().GetProperty("guardianId").GetGuid());

        var third = await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{c}/guardians",
            new { fullName = "Mrs Obi", email = "OBI@example.com", relationship = "Mother" }, Ct));
        Assert.Equal(g1.GetProperty("guardianId").GetGuid(), third.GetProperty("guardians").EnumerateArray().Single().GetProperty("guardianId").GetGuid());
        Assert.Equal(1, await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().CountAsync(g => g.TenantId == school.TenantId, Ct)));

        // A second guardian, then make them primary: only one primary at a time.
        var withFather = await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{a}/guardians",
            new { fullName = "Mr Obi", phone = "08099998888", relationship = "Father" }, Ct));
        var father = withFather.GetProperty("guardians").EnumerateArray().Single(g => g.GetProperty("fullName").GetString() == "Mr Obi");
        Assert.False(father.GetProperty("isPrimary").GetBoolean());
        var primary = await Json(await school.Owner.PostAsync($"/api/school/students/{a}/guardians/{father.GetProperty("guardianId").GetGuid()}/primary", null, Ct));
        Assert.Equal(["Mr Obi"], primary.GetProperty("guardians").EnumerateArray().Where(g => g.GetProperty("isPrimary").GetBoolean()).Select(g => g.GetProperty("fullName").GetString()));

        // Removing a guardian from one child keeps them on the siblings.
        var removed = await school.Owner.DeleteAsync($"/api/school/students/{b}/guardians/{g1.GetProperty("guardianId").GetGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await Json(await school.Owner.GetAsync($"/api/school/students/{b}", Ct))).GetProperty("guardians").EnumerateArray());
        Assert.Equal(2, (await Json(await school.Owner.GetAsync($"/api/school/students/{a}", Ct))).GetProperty("guardians").GetArrayLength());

        // A bad phone number says what to do.
        var bad = await ProblemAsync(await school.Owner.PostAsJsonAsync($"/api/school/students/{c}/guardians",
            new { fullName = "X", phone = "12", relationship = "Aunt" }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("phone", bad.GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Transfer_moves_the_current_enrolment_within_the_session()
    {
        var school = await kit.SchoolAsync("Transfer School");
        var a = await ClassAsync(school.Owner, "A");
        var b = await ClassAsync(school.Owner, "B");
        var student = await Json(await AddStudentAsync(school.Owner, a, "T-1"));
        var id = student.GetProperty("id").GetGuid();

        var moved = await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{id}/transfer", new { classId = b }, Ct));
        Assert.Equal(b, moved.GetProperty("classId").GetGuid());
        Assert.Equal("JSS 1B", moved.GetProperty("className").GetString());
        Assert.Equal(1, await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().CountAsync(e => e.StudentId == id, Ct)));
        Assert.Empty(await ListAsync(school.Owner, $"?classId={a}"));
        Assert.Single(await ListAsync(school.Owner, $"?classId={b}"));

        var missing = await school.Owner.PostAsJsonAsync($"/api/school/students/{id}/transfer", new { classId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Roles_decide_who_reads_and_writes_students()
    {
        var school = await kit.SchoolAsync("Role Students School");
        var mine = await ClassAsync(school.Owner, "A");
        var theirs = await ClassAsync(school.Owner, "B");
        var inMine = await Json(await AddStudentAsync(school.Owner, mine, "R-1", "Mine"));
        var inTheirs = await Json(await AddStudentAsync(school.Owner, theirs, "R-2", "Theirs"));
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{inMine.GetProperty("id").GetGuid()}/guardians",
            new { fullName = "Parent One", phone = "08031112222", relationship = "Mother" }, Ct));

        var (teacher, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        var (admin, _) = await kit.StaffAsync(school, MemberRole.Admin, "Admin");
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/classes/{mine}/class-teacher", new { staffId = await StaffIdAsync(school.Owner, teacherUser) }, Ct));

        // Teacher: only their class, with guardian contacts; no writes.
        var teacherList = await ListAsync(teacher);
        Assert.Equal([inMine.GetProperty("id").GetGuid()], teacherList.Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/school/students?classId={theirs}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/school/students/{inTheirs.GetProperty("id").GetGuid()}", Ct)).StatusCode);
        var teacherDetail = await Json(await teacher.GetAsync($"/api/school/students/{inMine.GetProperty("id").GetGuid()}", Ct));
        Assert.Equal("+2348031112222", teacherDetail.GetProperty("guardians").EnumerateArray().Single().GetProperty("phone").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await AddStudentAsync(teacher, mine, "R-9")).StatusCode);

        // Bursar: the whole list, no writes.
        Assert.Equal(2, (await ListAsync(bursar)).Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddStudentAsync(bursar, mine, "R-9")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsJsonAsync($"/api/school/students/{inMine.GetProperty("id").GetGuid()}/status", new { status = "Withdrawn", date = "2026-10-01" }, Ct)).StatusCode);

        // Admin writes.
        Assert.Equal(HttpStatusCode.OK, (await AddStudentAsync(admin, mine, "R-3", "Added")).StatusCode);

        // A teacher with no classes sees nothing.
        var (idle, _) = await kit.StaffAsync(school, MemberRole.Staff, "Idle teacher");
        Assert.Empty(await ListAsync(idle));
    }

    [Fact]
    public async Task A_student_created_by_mistake_can_be_deleted()
    {
        var school = await kit.SchoolAsync("Mistake School");
        var classId = await ClassAsync(school.Owner, "A");
        var id = (await Json(await AddStudentAsync(school.Owner, classId, "D-1"))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/students/{id}/guardians", new { fullName = "Parent", phone = "08035550000", relationship = "Mother" }, Ct));

        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/students/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.GetAsync($"/api/school/students/{id}", Ct)).StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().CountAsync(g => g.TenantId == school.TenantId, Ct)));
    }

    [Fact]
    public async Task A_student_with_history_cannot_be_deleted()
    {
        var school = await kit.SchoolAsync("History School");
        var classId = await ClassAsync(school.Owner, "A");
        var id = (await Json(await AddStudentAsync(school.Owner, classId, "H-1"))).GetProperty("id").GetGuid();
        await api.WithDbAsync(async db =>
        {
            var old = new AcademicSession { TenantId = school.TenantId, Name = "2019/2020", StartDate = new(2019, 9, 1), EndDate = new(2020, 8, 31) };
            db.AcademicSessions.Add(old);
            var level = await db.Levels.IgnoreQueryFilters().FirstAsync(l => l.TenantId == school.TenantId, Ct);
            var cls = new SchoolClass { TenantId = school.TenantId, SessionId = old.Id, LevelId = level.Id, Arm = "Q", Name = "JSS 1Q" };
            db.SchoolClasses.Add(cls);
            db.Enrolments.Add(new Enrolment { TenantId = school.TenantId, StudentId = id, SessionId = old.Id, ClassId = cls.Id });
            await db.SaveChangesAsync(Ct);
            return true;
        });

        var problem = await ProblemAsync(await school.Owner.DeleteAsync($"/api/school/students/{id}", Ct), HttpStatusCode.Conflict);
        Assert.Contains("Withdraw or transfer", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, (await school.Owner.GetAsync($"/api/school/students/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Students_are_edited_and_one_school_cannot_see_another()
    {
        var school = await kit.SchoolAsync("Edit Student School");
        var other = await kit.SchoolAsync("Spy School");
        var classId = await ClassAsync(school.Owner, "A");
        var id = (await Json(await AddStudentAsync(school.Owner, classId, "E-1", "Ada"))).GetProperty("id").GetGuid();

        var edited = await Json(await school.Owner.PutAsJsonAsync($"/api/school/students/{id}",
            new { admissionNo = "E-1", firstName = "Adaeze", middleName = "N", lastName = "Obi", gender = "Female", dateOfBirth = "2014-03-02" }, Ct));
        Assert.Equal("Adaeze", edited.GetProperty("firstName").GetString());
        Assert.Equal("2014-03-02", edited.GetProperty("dateOfBirth").GetString());
        Assert.Single(await ListAsync(school.Owner, "?q=adaeze"));
        Assert.Empty(await ListAsync(school.Owner, "?q=zzz"));

        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.GetAsync($"/api/school/students/{id}", Ct)).StatusCode);
        Assert.Empty(await ListAsync(other.Owner));

        var blank = await ProblemAsync(await school.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = " ", firstName = "", lastName = "", gender = "Female", classId }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains("admissionNo", blank.GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }
}
