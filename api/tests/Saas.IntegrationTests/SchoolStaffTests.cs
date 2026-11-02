using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink staff: profiles for every member, class and subject teachers, and what a teacher sees of their own classes.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolStaffTests(ApiFixture api)
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

    private static async Task<List<JsonElement>> StaffListAsync(HttpClient client) =>
        (await Json(await client.GetAsync("/api/school/staff", Ct))).EnumerateArray().ToList();

    private static async Task<Guid> StaffIdAsync(HttpClient client, Guid userId) =>
        (await StaffListAsync(client)).Single(s => s.GetProperty("userId").GetGuid() == userId).GetProperty("staffId").GetGuid();

    private static async Task<(Guid ClassId, Guid ClassSubjectId, Guid SubjectId)> ClassWithSubjectAsync(HttpClient owner, string arm, string subjectCode)
    {
        var levelId = (await Json(await owner.GetAsync("/api/school/levels", Ct))).EnumerateArray()
            .FirstOrDefault(l => l.GetProperty("name").GetString() == "JSS 1").ValueKind == JsonValueKind.Undefined
            ? (await Json(await owner.PostAsJsonAsync("/api/school/levels", new { name = "JSS 1", stage = "Jss", order = 10, hasStudentLogins = true }, Ct))).GetProperty("id").GetGuid()
            : (await Json(await owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().First(l => l.GetProperty("name").GetString() == "JSS 1").GetProperty("id").GetGuid();
        var classId = (await Json(await owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm }, Ct))).GetProperty("id").GetGuid();
        var subjectId = (await Json(await owner.PostAsJsonAsync("/api/school/subjects", new { name = $"Subject {subjectCode}", code = subjectCode }, Ct))).GetProperty("id").GetGuid();
        var link = await Json(await owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId }, Ct));
        return (classId, link.GetProperty("id").GetGuid(), subjectId);
    }

    [Fact]
    public async Task Every_member_has_one_staff_profile_with_a_default_job_title()
    {
        var school = await kit.SchoolAsync("Profile School");
        var (_, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (_, bursarUser) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");

        var staff = await StaffListAsync(school.Owner);
        Assert.Equal(3, staff.Count);
        Assert.Equal(3, staff.Select(s => s.GetProperty("staffId").GetGuid()).Distinct().Count());
        Assert.Equal("Proprietor", staff.Single(s => s.GetProperty("role").GetString() == "Owner").GetProperty("jobTitle").GetString());
        Assert.Equal("Teacher", staff.Single(s => s.GetProperty("userId").GetGuid() == teacherUser).GetProperty("jobTitle").GetString());
        Assert.Equal("Bursar", staff.Single(s => s.GetProperty("userId").GetGuid() == bursarUser).GetProperty("jobTitle").GetString());

        // Reading again does not add profiles.
        Assert.Equal(3, (await StaffListAsync(school.Owner)).Count);
    }

    [Fact]
    public async Task The_owner_edits_a_staff_profile_and_staff_numbers_stay_unique()
    {
        var school = await kit.SchoolAsync("Edit Profile School");
        var (teacher, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staff = await StaffListAsync(school.Owner);
        var teacherRow = staff.Single(s => s.GetProperty("userId").GetGuid() == teacherUser);
        var ownerRow = staff.Single(s => s.GetProperty("role").GetString() == "Owner");

        var saved = await Json(await school.Owner.PutAsJsonAsync($"/api/school/staff/{teacherRow.GetProperty("membershipId").GetGuid()}",
            new { jobTitle = "Mathematics teacher", staffNo = "T-001", phone = "08030000000" }, Ct));
        Assert.Equal("Mathematics teacher", saved.GetProperty("jobTitle").GetString());
        Assert.Equal("T-001", saved.GetProperty("staffNo").GetString());

        var clash = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/staff/{ownerRow.GetProperty("membershipId").GetGuid()}",
            new { jobTitle = "Proprietor", staffNo = "t-001", phone = (string?)null }, Ct), HttpStatusCode.Conflict);
        Assert.Contains("staff number", clash.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);

        var blank = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/staff/{teacherRow.GetProperty("membershipId").GetGuid()}",
            new { jobTitle = " ", staffNo = (string?)null, phone = (string?)null }, Ct), HttpStatusCode.BadRequest);
        Assert.True(blank.GetProperty("errors").TryGetProperty("jobTitle", out _));

        // Staff management is for the Proprietor and school admins; a teacher can neither read nor edit it.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/school/staff", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync($"/api/school/staff/{ownerRow.GetProperty("membershipId").GetGuid()}",
            new { jobTitle = "Boss", staffNo = (string?)null, phone = (string?)null }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Teachers_are_assigned_to_classes_and_subjects()
    {
        var school = await kit.SchoolAsync("Assign School");
        var (_, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await StaffIdAsync(school.Owner, teacherUser);
        var (classId, classSubjectId, _) = await ClassWithSubjectAsync(school.Owner, "A", "MTH");

        var cls = await Json(await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct));
        Assert.Equal(staffId, cls.GetProperty("classTeacherStaffId").GetGuid());

        var link = await Json(await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{classSubjectId}/teacher", new { staffId }, Ct));
        Assert.Equal(staffId, link.GetProperty("teacherStaffId").GetGuid());

        // Clearing the teacher is allowed.
        var cleared = await Json(await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{classSubjectId}/teacher", new { staffId = (Guid?)null }, Ct));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("teacherStaffId").ValueKind);

        // A staff id from nowhere, or from another school, is rejected on every route that sets one.
        var other = await kit.SchoolAsync("Other Assign School");
        var strangerId = (await StaffListAsync(other.Owner)).Single().GetProperty("staffId").GetGuid();
        foreach (var bad in new[] { Guid.NewGuid(), strangerId })
        {
            var p1 = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = bad }, Ct), HttpStatusCode.BadRequest);
            Assert.True(p1.GetProperty("errors").TryGetProperty("staffId", out _));
            var p2 = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{classSubjectId}/teacher", new { staffId = bad }, Ct), HttpStatusCode.BadRequest);
            Assert.True(p2.GetProperty("errors").TryGetProperty("staffId", out _));
        }
        var viaCreate = await school.Owner.GetAsync("/api/school/levels", Ct);
        var levelId = (await Json(viaCreate)).EnumerateArray().First().GetProperty("id").GetGuid();
        var p4 = await ProblemAsync(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "B", classTeacherStaffId = Guid.NewGuid() }, Ct), HttpStatusCode.BadRequest);
        Assert.True(p4.GetProperty("errors").TryGetProperty("staffId", out _));

        // Only leadership assigns teachers.
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Second teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_member_cannot_be_assigned_and_teaches_nothing()
    {
        var school = await kit.SchoolAsync("Disabled School");
        var (_, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await StaffIdAsync(school.Owner, teacherUser);
        var (classId, classSubjectId, _) = await ClassWithSubjectAsync(school.Owner, "A", "MTH");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();

        await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == teacherUser)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MembershipStatus.Disabled), Ct));

        var p1 = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct), HttpStatusCode.BadRequest);
        Assert.True(p1.GetProperty("errors").TryGetProperty("staffId", out _));
        var p2 = await ProblemAsync(await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{classSubjectId}/teacher", new { staffId }, Ct), HttpStatusCode.BadRequest);
        Assert.True(p2.GetProperty("errors").TryGetProperty("staffId", out _));
    }

    [Fact]
    public async Task Removing_a_member_clears_their_teaching_assignments()
    {
        var school = await kit.SchoolAsync("Leaver School");
        var (_, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await StaffIdAsync(school.Owner, teacherUser);
        var (classId, classSubjectId, _) = await ClassWithSubjectAsync(school.Owner, "A", "MTH");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{classSubjectId}/teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();

        var membershipId = (await StaffListAsync(school.Owner)).Single(s => s.GetProperty("userId").GetGuid() == teacherUser).GetProperty("membershipId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/team/members/{membershipId}", Ct)).StatusCode);

        var classes = (await Json(await school.Owner.GetAsync("/api/school/classes", Ct))).EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, classes.Single().GetProperty("classTeacherStaffId").ValueKind);
        var subjects = (await Json(await school.Owner.GetAsync($"/api/school/classes/{classId}/subjects", Ct))).EnumerateArray().ToList();
        Assert.Equal(JsonValueKind.Null, subjects.Single().GetProperty("teacherStaffId").ValueKind);
        Assert.Single(await StaffListAsync(school.Owner));
    }

    [Fact]
    public async Task A_teacher_sees_only_their_classes()
    {
        var school = await kit.SchoolAsync("My Classes School");
        var (teacher, teacherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (other, otherUser) = await kit.StaffAsync(school, MemberRole.Staff, "Colleague");
        var staffId = await StaffIdAsync(school.Owner, teacherUser);
        var otherId = await StaffIdAsync(school.Owner, otherUser);

        var (classA, subjectA, _) = await ClassWithSubjectAsync(school.Owner, "A", "MTH"); // class teacher of A
        var (classB, subjectB, _) = await ClassWithSubjectAsync(school.Owner, "B", "ENG"); // teaches a subject in B only
        var (classC, subjectC, _) = await ClassWithSubjectAsync(school.Owner, "C", "PHY"); // someone else's
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classA}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{subjectB}/teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classC}/class-teacher", new { staffId = otherId }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/class-subjects/{subjectC}/teacher", new { staffId = otherId }, Ct)).EnsureSuccessStatusCode();

        var mine = await Json(await teacher.GetAsync("/api/school/my-classes", Ct));
        var asClassTeacher = mine.GetProperty("classTeacherOf").EnumerateArray().ToList();
        Assert.Equal([classA], asClassTeacher.Select(c => c.GetProperty("classId").GetGuid()));
        Assert.Equal("JSS 1A", asClassTeacher.Single().GetProperty("className").GetString());
        var subjects = mine.GetProperty("subjects").EnumerateArray().ToList();
        Assert.Equal([subjectB], subjects.Select(s => s.GetProperty("classSubjectId").GetGuid()));
        Assert.Equal(classB, subjects.Single().GetProperty("classId").GetGuid());
        Assert.Equal("JSS 1B", subjects.Single().GetProperty("className").GetString());
        Assert.Equal("Subject ENG", subjects.Single().GetProperty("subjectName").GetString());

        var theirs = await Json(await other.GetAsync("/api/school/my-classes", Ct));
        Assert.Equal([classC], theirs.GetProperty("classTeacherOf").EnumerateArray().Select(c => c.GetProperty("classId").GetGuid()));
        Assert.Equal([subjectC], theirs.GetProperty("subjects").EnumerateArray().Select(s => s.GetProperty("classSubjectId").GetGuid()));

        // The owner teaches nothing, so sees nothing.
        var owners = await Json(await school.Owner.GetAsync("/api/school/my-classes", Ct));
        Assert.Equal(0, owners.GetProperty("classTeacherOf").GetArrayLength());
        Assert.Equal(0, owners.GetProperty("subjects").GetArrayLength());
    }

    [Fact]
    public async Task The_staff_checklist_item_is_done_once_someone_joins_besides_the_owner()
    {
        var school = await kit.SchoolAsync("Staff Checklist School");
        async Task<bool> StaffDoneAsync() =>
            (await Json(await school.Owner.GetAsync("/api/school/setup", Ct))).GetProperty("checklist").EnumerateArray()
                .Single(c => c.GetProperty("key").GetString() == "staff").GetProperty("done").GetBoolean();

        Assert.False(await StaffDoneAsync());
        await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        Assert.True(await StaffDoneAsync());
    }

    [Fact]
    public async Task Seat_limit_applies_to_staff_invites()
    {
        var school = await kit.SchoolAsync("Seat School"); // Free plan: 3 seats, the owner takes one
        (await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = $"a-{Guid.NewGuid():N}@example.com", role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = $"b-{Guid.NewGuid():N}@example.com", role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var full = await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = $"c-{Guid.NewGuid():N}@example.com", role = "Staff" }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, full.StatusCode);
    }
}
