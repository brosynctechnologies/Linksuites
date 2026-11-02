using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>
/// Setup imports (kinds <c>subjects</c>, <c>classes</c>, <c>class-subjects</c> and <c>staff</c>): leadership loads the school's setup from a
/// file through the setup screens' own service methods. Teachers and the bursar are refused on template, preview and confirm.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportSetupTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string StaffMissing = "No active staff member of this school has this email.";

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["subjects"] = "name,code",
        ["classes"] = "level,arm,class_teacher",
        ["class-subjects"] = "class,subject,teacher",
        ["staff"] = "email,role,job_title,staff_no,phone",
    };

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

    private static MultipartFormDataContent Form(string csv, string fileName)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string Csv(string kind, params string[] lines) => string.Join("\r\n", new[] { Headers[kind] }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string kind, params string[] lines) =>
        client.PostAsync($"/api/school/imports/{kind}/preview", Form(Csv(kind, lines), kind + ".csv"), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static async Task<JsonElement> ConfirmOkAsync(HttpClient client, JsonElement preview) =>
        await Json(await ConfirmAsync(client, preview.GetProperty("token").GetString()!));

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();
    private static string Status(JsonElement row) => row.GetProperty("status").GetString()!;
    private static string? Message(JsonElement row) => row.GetProperty("message").GetString();

    private Task<string> EmailOfAsync(Guid userId) =>
        api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).FirstAsync(Ct));

    private Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private Task<List<Subject>> SubjectsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Subjects.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).OrderBy(s => s.Code).ToListAsync(Ct));

    private Task<List<SchoolClass>> ClassesAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).OrderBy(c => c.Name).ToListAsync(Ct));

    private Task<List<ClassSubject>> LinksAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.ClassSubjects.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ToListAsync(Ct));

    private Task<List<Invitation>> InvitesAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Invitations.IgnoreQueryFilters().Where(i => i.TenantId == tenantId && i.AcceptedAt == null).OrderBy(i => i.Email).ToListAsync(Ct));

    private Task<int> InviteEmailsAsync(string email) =>
        api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.TemplateKey == "team.invite" && n.Recipient == email, Ct));

    private async Task<Guid> SubjectAsync(SchoolKit.School school, string name, string code) =>
        (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct))).GetProperty("id").GetGuid();

    // ---------- Subjects ----------

    [Fact]
    public async Task Subjects_import_creates_and_updates_by_code()
    {
        var school = await kit.SchoolAsync("Subject Import School");
        var maths = await SubjectAsync(school, "Maths", "MTH");
        var civic = await SubjectAsync(school, "Civic Education", "CVE");

        var preview = await Json(await PreviewAsync(school.Owner, "subjects",
            "Mathematics,mth",
            "English Language,ENG",
            "Civic Education,CVE",
            "Basic Science, bsc "));
        Assert.Equal("subjects", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal(["Update", "New", "Unchanged", "New"], rows.Select(Status));
        Assert.All(rows, r => Assert.Null(Message(r)));
        var counts = preview.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("new").GetInt32());
        Assert.Equal(1, counts.GetProperty("update").GetInt32());
        Assert.Equal(1, counts.GetProperty("unchanged").GetInt32());
        Assert.Equal("Maths", (await SubjectsAsync(school.TenantId)).Single(s => s.Id == maths).Name); // preview saves nothing

        var done = await ConfirmOkAsync(school.Owner, preview);
        Assert.Equal(2, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());

        var subjects = await SubjectsAsync(school.TenantId);
        Assert.Equal(["BSC", "CVE", "ENG", "MTH"], subjects.Select(s => s.Code));
        Assert.Equal("Mathematics", subjects.Single(s => s.Id == maths).Name);
        Assert.Equal("Civic Education", subjects.Single(s => s.Id == civic).Name);
        Assert.Equal("Basic Science", subjects.Single(s => s.Code == "BSC").Name);
        Assert.Equal("English Language", subjects.Single(s => s.Code == "ENG").Name);
    }

    [Fact]
    public async Task Subject_duplicates_are_errors()
    {
        var school = await kit.SchoolAsync("Subject Duplicate School");
        await SubjectAsync(school, "Mathematics", "MTH");

        var preview = await Json(await PreviewAsync(school.Owner, "subjects",
            "Mathematics,MAT",       // the name belongs to MTH
            "Agric,AGR",
            "Agricultural Science,agr", // the same code twice in the file
            "Music,MUS",
            "music,MSC",             // the same name twice in the file
            "French,FR-1",           // not letters or digits
            ",HIS"));
        var rows = Rows(preview);
        Assert.All(rows, r => Assert.Equal("Error", Status(r)));
        Assert.Equal("A subject with that name already exists.", Message(rows[0]));
        Assert.Equal("This code appears more than once in the file.", Message(rows[1]));
        Assert.Equal("This code appears more than once in the file.", Message(rows[2]));
        Assert.Equal("This subject name appears more than once in the file.", Message(rows[3]));
        Assert.Equal("This subject name appears more than once in the file.", Message(rows[4]));
        Assert.Equal("Enter a code of up to 10 letters or digits, such as MTH.", Message(rows[5]));
        Assert.Equal("Enter the subject name.", Message(rows[6]));

        var refused = await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Single(await SubjectsAsync(school.TenantId));
    }

    // ---------- Classes ----------

    [Fact]
    public async Task Classes_import_sets_class_teacher_by_email()
    {
        var school = await kit.SchoolAsync("Class Import School");
        var jss1a = await kit.ClassAsync(school, "A");
        var (_, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Form teacher");
        var email = await EmailOfAsync(teacherUserId);
        var staffId = await StaffIdAsync(school, teacherUserId);
        var sessionId = (await ClassesAsync(school.TenantId)).Single().SessionId;

        var preview = await Json(await PreviewAsync(school.Owner, "classes",
            $"JSS 1,A,{email.ToUpperInvariant()}",
            "jss1,B,",
            $"JSS 1,C,{email}"));
        var rows = Rows(preview);
        Assert.Equal(["Update", "New", "New"], rows.Select(Status));
        Assert.Equal("JSS 1A", rows[0].GetProperty("label").GetString());
        Assert.Equal("JSS 1B", rows[1].GetProperty("label").GetString());

        var done = await ConfirmOkAsync(school.Owner, preview);
        Assert.Equal(2, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());

        var classes = await ClassesAsync(school.TenantId);
        Assert.Equal(["JSS 1A", "JSS 1B", "JSS 1C"], classes.Select(c => c.Name));
        Assert.All(classes, c => Assert.Equal(sessionId, c.SessionId));
        Assert.Equal(staffId, classes.Single(c => c.Id == jss1a).ClassTeacherStaffId);
        Assert.Null(classes.Single(c => c.Name == "JSS 1B").ClassTeacherStaffId);
        Assert.Equal(staffId, classes.Single(c => c.Name == "JSS 1C").ClassTeacherStaffId);

        // The same file again changes nothing; a blank class teacher keeps the one set.
        var again = await Json(await PreviewAsync(school.Owner, "classes", $"JSS 1,A,{email}", "JSS 1,B,", "JSS 1,C,"));
        Assert.All(Rows(again), r => Assert.Equal("Unchanged", Status(r)));
    }

    [Fact]
    public async Task Unknown_level_error()
    {
        var school = await kit.SchoolAsync("Class Level School");
        await kit.ClassAsync(school, "A");

        var preview = await Json(await PreviewAsync(school.Owner, "classes",
            "SSS 9,A,",
            "JSS 1,B,",
            "jss 1,b,",
            ",C,",
            "JSS 1,,"));
        var rows = Rows(preview);
        Assert.All(rows, r => Assert.Equal("Error", Status(r)));
        Assert.Equal("This level was not found.", Message(rows[0]));
        Assert.Equal("This class appears more than once in the file.", Message(rows[1]));
        Assert.Equal("This class appears more than once in the file.", Message(rows[2]));
        Assert.Equal("Choose a level.", Message(rows[3]));
        Assert.Equal("Enter the arm.", Message(rows[4]));

        var refused = await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Single(await ClassesAsync(school.TenantId));
    }

    // ---------- Class subjects ----------

    [Fact]
    public async Task Class_subjects_import_sets_teacher()
    {
        var school = await kit.SchoolAsync("Class Subject Import School");
        var classId = await kit.ClassAsync(school, "A");
        var (_, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Subject teacher");
        var email = await EmailOfAsync(teacherUserId);
        var staffId = await StaffIdAsync(school, teacherUserId);
        var maths = await SubjectAsync(school, "Mathematics", "MTH");
        var english = await SubjectAsync(school, "English", "ENG");
        var science = await SubjectAsync(school, "Basic Science", "BSC");
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = maths }, Ct));

        var preview = await Json(await PreviewAsync(school.Owner, "class-subjects",
            $"JSS 1A,mth,{email}",
            $"jss1a,English,{email}",
            "JSS 1 A,BSC,"));
        var rows = Rows(preview);
        Assert.Equal(["Update", "New", "New"], rows.Select(Status));
        Assert.Equal("JSS 1A / Mathematics", rows[0].GetProperty("label").GetString());

        var done = await ConfirmOkAsync(school.Owner, preview);
        Assert.Equal(2, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());

        var links = await LinksAsync(school.TenantId);
        Assert.Equal(3, links.Count);
        Assert.All(links, l => Assert.Equal(classId, l.ClassId));
        Assert.Equal(staffId, links.Single(l => l.SubjectId == maths).TeacherStaffId);
        Assert.Equal(staffId, links.Single(l => l.SubjectId == english).TeacherStaffId);
        Assert.Null(links.Single(l => l.SubjectId == science).TeacherStaffId);

        var again = await Json(await PreviewAsync(school.Owner, "class-subjects", $"JSS 1A,MTH,{email}", "JSS 1A,ENG,", "JSS 1A,BSC,"));
        Assert.All(Rows(again), r => Assert.Equal("Unchanged", Status(r)));
    }

    [Fact]
    public async Task Unknown_staff_email_error()
    {
        var school = await kit.SchoolAsync("Unknown Staff School");
        await kit.ClassAsync(school, "A");
        await SubjectAsync(school, "Mathematics", "MTH");
        await SubjectAsync(school, "English", "ENG");

        var subjects = await Json(await PreviewAsync(school.Owner, "class-subjects",
            "JSS 1A,MTH,nobody@example.com",
            "JSS 2A,MTH,",
            "JSS 1A,Physics,",
            "JSS 1A,ENG,",
            "JSS 1A,english,"));
        var rows = Rows(subjects);
        Assert.All(rows, r => Assert.Equal("Error", Status(r)));
        Assert.Equal(StaffMissing, Message(rows[0]));
        Assert.Equal("This class was not found.", Message(rows[1]));
        Assert.Equal("This subject was not found.", Message(rows[2]));
        Assert.Equal("This class and subject appear more than once in the file.", Message(rows[3]));
        Assert.Equal("This class and subject appear more than once in the file.", Message(rows[4]));
        await ProblemAsync(await ConfirmAsync(school.Owner, subjects.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Empty(await LinksAsync(school.TenantId));

        // A pending invitation is not staff yet.
        var invited = $"pending-{Guid.NewGuid():N}@example.com";
        await Json(await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = invited, role = "Staff" }, Ct));
        var classes = await Json(await PreviewAsync(school.Owner, "classes", "JSS 1,A,nobody@example.com", $"JSS 1,B,{invited}"));
        Assert.All(Rows(classes), r => Assert.Equal(StaffMissing, Message(r)));
    }

    // ---------- Staff ----------

    [Fact]
    public async Task Staff_import_invites_and_updates()
    {
        var school = await kit.SchoolAsync("Staff Import School");
        var (_, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher"); // standard plan
        var teacherEmail = await EmailOfAsync(teacherUserId);
        var bursar = $"bursar-{Guid.NewGuid():N}@example.com";
        var principal = $"principal-{Guid.NewGuid():N}@example.com";

        var preview = await Json(await PreviewAsync(school.Owner, "staff",
            $"{teacherEmail.ToUpperInvariant()},Teacher,Class teacher,007,08031234567",
            $"{bursar},bursar,,,",
            $"{principal},Principal,Vice principal,,"));
        var rows = Rows(preview);
        Assert.Equal(["Update", "New", "New"], rows.Select(Status));
        Assert.Equal(teacherEmail, rows[0].GetProperty("label").GetString());
        // Profile fields of someone not yet on the team are not saved; the preview says so.
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("after they accept", StringComparison.Ordinal));
        Assert.Equal(0, await InviteEmailsAsync(bursar)); // no email before the import is confirmed

        var done = await ConfirmOkAsync(school.Owner, preview);
        Assert.Equal(2, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());

        var profile = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().SingleAsync(p => p.UserId == teacherUserId && p.TenantId == school.TenantId, Ct));
        Assert.Equal("Class teacher", profile.JobTitle);
        Assert.Equal("007", profile.StaffNo);
        Assert.Equal("08031234567", profile.Phone);

        var invites = await InvitesAsync(school.TenantId);
        Assert.Equal(MemberRole.Accountant, invites.Single(i => i.Email == bursar).Role);
        Assert.Equal(MemberRole.Admin, invites.Single(i => i.Email == principal).Role);
        Assert.Equal(1, await InviteEmailsAsync(bursar));
        Assert.Equal(1, await InviteEmailsAsync(principal));

        // Again: a blank cell keeps the stored value, equal values are unchanged, and a pending invitation is refused as on the team screen.
        var again = await Json(await PreviewAsync(school.Owner, "staff", $"{teacherEmail},,,007,", $"{bursar},Bursar,,,"));
        var second = Rows(again);
        Assert.Equal("Unchanged", Status(second[0]));
        Assert.Equal("Error", Status(second[1]));
        Assert.Equal("This person already has an invitation. Resend it instead.", Message(second[1]));
    }

    [Fact]
    public async Task Staff_rows_are_checked_like_the_screens()
    {
        var school = await kit.SchoolAsync("Staff Check School");
        var (_, aUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher one");
        var (_, bUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher two");
        var a = await EmailOfAsync(aUserId);
        var b = await EmailOfAsync(bUserId);
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/staff/{await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == bUserId && m.TenantId == school.TenantId).Select(m => m.Id).FirstAsync(Ct))}",
            new { jobTitle = "Teacher", staffNo = "T-01" }, Ct));
        var fresh = $"fresh-{Guid.NewGuid():N}@example.com";

        var preview = await Json(await PreviewAsync(school.Owner, "staff",
            $"{a},Teacher,,t-01,",            // staff number of another member
            "not-an-email,Teacher,,,",
            $"{fresh},Bursar,,,",
            $"{fresh.ToUpperInvariant()},Teacher,,,",
            $"{b},Teacher,{new string('x', 81)},,"));
        var rows = Rows(preview);
        Assert.All(rows, r => Assert.Equal("Error", Status(r)));
        Assert.Equal("Another staff member has that staff number. Enter a different one.", Message(rows[0]));
        Assert.Equal("Enter a valid email address.", Message(rows[1]));
        Assert.Equal("This email appears more than once in the file.", Message(rows[2]));
        Assert.Equal("This email appears more than once in the file.", Message(rows[3]));
        Assert.Equal("The job title can have at most 80 characters.", Message(rows[4]));

        // The same staff number for two people in one file; b keeping their own number is fine on its own.
        var numbers = Rows(await Json(await PreviewAsync(school.Owner, "staff", $"{a},,,Z9,", $"{b},,,z9,")));
        Assert.All(numbers, r => Assert.Equal("This staff number appears more than once in the file.", Message(r)));
        var own = Rows(await Json(await PreviewAsync(school.Owner, "staff", $"{b},,,T-01,")));
        Assert.Equal("Unchanged", Status(own[0]));

        var roles = await Json(await PreviewAsync(school.Owner, "staff", $"{fresh},Cleaner,,,", $"new-{Guid.NewGuid():N}@example.com,,,,"));
        Assert.All(Rows(roles), r => Assert.Equal("Enter the role: Principal, Bursar or Teacher.", Message(r)));
    }

    [Fact]
    public async Task Proprietor_role_refused()
    {
        var school = await kit.SchoolAsync("Proprietor Import School");
        var ownerEmail = await EmailOfAsync(await kit.OwnerIdAsync(school));
        var someone = $"owner-{Guid.NewGuid():N}@example.com";

        var preview = await Json(await PreviewAsync(school.Owner, "staff",
            $"{someone},Proprietor,,,",
            $"{ownerEmail},proprietor,Director,,"));
        var rows = Rows(preview);
        Assert.All(rows, r => Assert.Equal("Error", Status(r)));
        Assert.All(rows, r => Assert.Equal("The proprietor can't be added from a file. Use Principal, Bursar or Teacher.", Message(r)));

        await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Empty(await InvitesAsync(school.TenantId));
    }

    [Fact]
    public async Task Seat_limit_error()
    {
        var school = await kit.SchoolAsync("Seat Import School"); // Free plan: 3 seats, the owner takes one
        var pending = $"pending-{Guid.NewGuid():N}@example.com";
        await Json(await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = pending, role = "Staff" }, Ct)); // a pending invite takes a seat
        var first = $"first-{Guid.NewGuid():N}@example.com";
        var second = $"second-{Guid.NewGuid():N}@example.com";
        var third = $"third-{Guid.NewGuid():N}@example.com";

        var preview = await Json(await PreviewAsync(school.Owner, "staff",
            "bad-email,Teacher,,,", // an error row first: it takes no seat, so the last free seat goes to the next row
            $"{first},Teacher,,,",
            $"{second},Teacher,,,",
            $"{third},Bursar,,,"));
        var rows = Rows(preview);
        Assert.Equal(["Error", "New", "Error", "Error"], rows.Select(Status));
        Assert.Equal("Enter a valid email address.", Message(rows[0]));

        var refused = await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Equal([pending], (await InvitesAsync(school.TenantId)).Select(i => i.Email));

        // The same sentence as the team screen once every seat is taken.
        await Json(await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = first, role = "Staff" }, Ct));
        var screen = await ProblemAsync(await school.Owner.PostAsJsonAsync("/api/team/invites", new { email = second, role = "Staff" }, Ct), HttpStatusCode.PaymentRequired);
        var sentence = screen.GetProperty("detail").GetString();
        Assert.Equal(sentence, Message(rows[2]));
        Assert.Equal(sentence, Message(rows[3]));
    }

    // ---------- Fix round 1 ----------

    private Task<Guid> MembershipIdAsync(SchoolKit.School school, Guid userId) =>
        api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == userId && m.TenantId == school.TenantId).Select(m => m.Id).FirstAsync(Ct));

    private Task<MemberRole> RoleAsync(SchoolKit.School school, Guid userId) =>
        api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == userId && m.TenantId == school.TenantId).Select(m => m.Role).FirstAsync(Ct));

    /// <summary>The ruling: an import never changes a member's role. A different role on an existing member's row warns and leaves the role.</summary>
    [Fact]
    public async Task Existing_member_role_never_changes()
    {
        var school = await kit.SchoolAsync("Staff Role School");
        var (_, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Role teacher");
        var teacherEmail = await EmailOfAsync(teacherUserId);
        var ownerId = await kit.OwnerIdAsync(school);
        var ownerEmail = await EmailOfAsync(ownerId);

        var preview = await Json(await PreviewAsync(school.Owner, "staff",
            $"{teacherEmail},Principal,Head of maths,,",
            $"{ownerEmail},Principal,,,"));
        var rows = Rows(preview);
        Assert.Equal(["Update", "Unchanged"], rows.Select(Status));
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(),
            w => w.GetString() == "The roles of people already on the team are not changed by an import. Change a role on the staff page.");

        var done = await ConfirmOkAsync(school.Owner, preview);
        Assert.Equal(0, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());
        Assert.Equal(MemberRole.Staff, await RoleAsync(school, teacherUserId));
        Assert.Equal(MemberRole.Owner, await RoleAsync(school, ownerId));
        Assert.Empty(await InvitesAsync(school.TenantId));
        var profile = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().SingleAsync(p => p.UserId == teacherUserId && p.TenantId == school.TenantId, Ct));
        Assert.Equal("Head of maths", profile.JobTitle);
    }

    /// <summary>Staff numbers are unique across every profile of the school, disabled members included, as on the screen and the unique index.</summary>
    [Fact]
    public async Task Disabled_members_staff_number_is_taken()
    {
        var school = await kit.SchoolAsync("Disabled Staff School");
        var (_, aUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Active teacher");
        var (_, bUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Gone teacher");
        var a = await EmailOfAsync(aUserId);
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/staff/{await MembershipIdAsync(school, bUserId)}", new { jobTitle = "Teacher", staffNo = "D-1" }, Ct));
        await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == bUserId && m.TenantId == school.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MembershipStatus.Disabled), Ct));

        var rows = Rows(await Json(await PreviewAsync(school.Owner, "staff", $"{a},,,d-1,")));
        Assert.Equal("Error", Status(rows[0]));
        Assert.Equal("Another staff member has that staff number. Enter a different one.", Message(rows[0]));
    }

    /// <summary>An active member without a staff profile is on the team: their row updates (the profile is made as the staff screen makes it), never invites.</summary>
    [Fact]
    public async Task Member_without_profile_is_not_invited()
    {
        var school = await kit.SchoolAsync("No Profile School");
        var (_, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Profileless teacher");
        var email = await EmailOfAsync(userId);
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).ExecuteDeleteAsync(Ct));

        var preview = await Json(await PreviewAsync(school.Owner, "staff", $"{email},Teacher,Librarian,L-7,"));
        var rows = Rows(preview);
        Assert.Equal("Update", Status(rows[0]));
        Assert.Null(Message(rows[0]));

        var done = await ConfirmOkAsync(school.Owner, preview);
        Assert.Equal(1, done.GetProperty("updated").GetInt32());
        Assert.Empty(await InvitesAsync(school.TenantId));
        var profile = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().SingleAsync(p => p.UserId == userId && p.TenantId == school.TenantId, Ct));
        Assert.Equal("Librarian", profile.JobTitle);
        Assert.Equal("L-7", profile.StaffNo);
    }

    /// <summary>
    /// A confirm that fails partway through applying (here a database error on the second invitation) rolls back the first invitation and its
    /// outbox email: no team.invite row is left for anyone in the file.
    /// </summary>
    [Fact]
    public async Task Failed_confirm_leaves_no_invite_email()
    {
        var school = await kit.SchoolAsync("Rollback Invite School");
        await api.MakeProAsync(school.TenantId, "school", "standard");
        var first = $"first-{Guid.NewGuid():N}@example.com";
        var boom = $"boom-{Guid.NewGuid():N}@example.com";
        var function = $"t7_boom_{Guid.NewGuid():N}";

        var preview = await Json(await PreviewAsync(school.Owner, "staff", $"{first},Teacher,,,", $"{boom},Bursar,,,"));
        Assert.Equal(["New", "New"], Rows(preview).Select(Status));

        // Test-only trigger: inserting the second row's invitation fails, after the first row's invitation and email were added.
        await api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            $"CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.email = '{boom}' THEN RAISE EXCEPTION 'boom'; END IF; RETURN NEW; END $$; " +
            $"CREATE TRIGGER {function} BEFORE INSERT ON invitations FOR EACH ROW EXECUTE FUNCTION {function}();", Ct));
        try
        {
            var response = await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!);
            Assert.False(response.IsSuccessStatusCode);
        }
        finally
        {
            await api.WithDbAsync(db => db.Database.ExecuteSqlRawAsync($"DROP TRIGGER IF EXISTS {function} ON invitations; DROP FUNCTION IF EXISTS {function}();", Ct));
        }

        Assert.Empty(await InvitesAsync(school.TenantId));
        Assert.Equal(0, await InviteEmailsAsync(first));
        Assert.Equal(0, await InviteEmailsAsync(boom));
        Assert.Equal(0, await api.WithDbAsync(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.TenantId == school.TenantId && n.TemplateKey == "team.invite", Ct)));
    }

    [Fact]
    public async Task Blank_level_says_choose_a_level()
    {
        var school = await kit.SchoolAsync("Blank Level School");
        var rows = Rows(await Json(await PreviewAsync(school.Owner, "classes", ",A,")));
        Assert.Equal("Choose a level.", Message(rows[0]));
    }

    // ---------- Access ----------

    [Fact]
    public async Task Teacher_refused_setup_imports()
    {
        var school = await kit.SchoolAsync("Setup Access School");
        var (teacher, teacherId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (bursar, bursarId) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        var samples = new Dictionary<string, string>
        {
            ["subjects"] = "Mathematics,MTH",
            ["classes"] = "JSS 1,A,",
            ["class-subjects"] = "JSS 1A,MTH,",
            ["staff"] = $"x-{Guid.NewGuid():N}@example.com,Teacher,,,",
        };

        foreach (var (client, userId) in new[] { (teacher, teacherId), (bursar, bursarId) })
        {
            foreach (var (kind, line) in samples)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/school/imports/{kind}/template.xlsx", Ct)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/school/imports/{kind}/template.csv", Ct)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(client, kind, line)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/school/imports/{kind}/preview",
                    new MultipartFormDataContent { { new StringContent("none"), "note" } }, Ct)).StatusCode);

                // A batch held under their own id (as if their role changed after the preview) is refused on confirm.
                var token = $"setup-{kind}-{Guid.NewGuid():N}";
                var cells = string.Join(",", Headers[kind].Split(',').Zip(line.Split(','), (k, v) => $"\"{k}\":\"{v}\""));
                await api.WithDbAsync(db =>
                {
                    db.ImportBatches.Add(new ImportBatch
                    {
                        TenantId = school.TenantId, Token = token, Kind = kind, CreatedBy = userId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
                        Payload = "{\"fileName\":\"" + kind + ".csv\",\"rows\":[{\"row\":2,\"cells\":{" + cells + "}}]}",
                    });
                    return db.SaveChangesAsync(Ct);
                });
                Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(client, token)).StatusCode);
            }
        }

        Assert.Empty(await SubjectsAsync(school.TenantId));
        Assert.Empty(await ClassesAsync(school.TenantId));
        Assert.Empty(await InvitesAsync(school.TenantId));

        // Leadership gets every template.
        foreach (var kind in samples.Keys)
        {
            var csv = await school.Owner.GetAsync($"/api/school/imports/{kind}/template.csv", Ct);
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            Assert.Equal(Headers[kind] + "\r\n", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)).TrimStart('﻿'));
        }
    }
}
