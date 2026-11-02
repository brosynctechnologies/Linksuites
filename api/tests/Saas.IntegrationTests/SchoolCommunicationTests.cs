using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink announcements (audiences, portals, batched email broadcasts) and parent–school message threads.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolCommunicationTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private Task<List<UserNotification>> NotesAsync(Guid userId, string kind) =>
        api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == userId && n.Kind == kind).ToListAsync(Ct));

    private async Task<UserNotification> OneAsync(Guid userId, string kind, SchoolKit.School school, string link)
    {
        var note = Assert.Single(await NotesAsync(userId, kind));
        Assert.Equal("school", note.Product);
        Assert.Equal(school.TenantId, note.TenantId);
        Assert.StartsWith(link, note.Link);
        Assert.False(string.IsNullOrWhiteSpace(note.Title));
        return note;
    }

    private async Task NoneAsync(Guid userId, string kind) => Assert.Empty(await NotesAsync(userId, kind));

    private Task<int> EmailsAsync(string to, string template) =>
        api.WithDbAsync(db => db.Notifications.CountAsync(n => n.Recipient == to && n.TemplateKey == template, Ct));

    private Task<Guid> StaffIdAsync(Guid userId) =>
        api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId).Select(p => p.Id).FirstAsync(Ct));

    private Task<string> EmailOfAsync(Guid userId) =>
        api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email!).FirstAsync(Ct));

    private async Task<(HttpClient Client, Guid UserId, Guid StaffId)> ClassTeacherAsync(SchoolKit.School school, Guid classId, string title = "Form Teacher")
    {
        var (client, userId) = await kit.StaffAsync(school, MemberRole.Staff, title);
        var staffId = await StaffIdAsync(userId);
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct));
        return (client, userId, staffId);
    }

    /// <summary>A school with classes JSS 1A (Ada) and JSS 1B (Bayo); each child's guardian has an email and a parent account.</summary>
    private async Task<Family> FamiliesAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classA = await kit.ClassAsync(school, "A");
        var classB = await kit.ClassAsync(school, "B");
        var ada = await kit.StudentAsync(school, classA, "Ada");
        var bayo = await kit.StudentAsync(school, classB, "Bayo", "Eze");
        var mumEmail = $"mum-{Guid.NewGuid():N}@example.com";
        var dadEmail = $"dad-{Guid.NewGuid():N}@example.com";
        var mum = await kit.GuardianAsync(school, ada, "Ngozi Obi", null, mumEmail);
        var dad = await kit.GuardianAsync(school, bayo, "Tunde Eze", null, dadEmail);
        var (adaParent, adaParentId) = await kit.ParentAsync(school, mum);
        var (bayoParent, bayoParentId) = await kit.ParentAsync(school, dad);
        return new Family(school, classA, classB, ada, bayo, adaParent, adaParentId, mumEmail, bayoParent, bayoParentId, dadEmail);
    }

    private sealed record Family(
        SchoolKit.School School, Guid ClassA, Guid ClassB, Guid Ada, Guid Bayo,
        HttpClient AdaParent, Guid AdaParentId, string MumEmail, HttpClient BayoParent, Guid BayoParentId, string DadEmail);

    private Task<int> SendBroadcastsAsync(Guid tenantId, int batchSize = 100) =>
        api.AsTenantAsync<AnnouncementService, int>(tenantId, s => s.SendPendingBroadcastsAsync(batchSize, Ct));

    private static object Announcement(string title, string audience, Guid[]? classIds = null, Guid[]? levelIds = null, bool emailGuardians = false) =>
        new { title, body = "School closes at noon on Friday for the staff meeting.", audience, classIds = classIds ?? [], levelIds = levelIds ?? [], emailGuardians };

    private static async Task<List<string>> TitlesAsync(HttpClient client, string url) =>
        (await Json(await client.GetAsync(url, Ct))).EnumerateArray().Select(a => a.GetProperty("title").GetString()!).ToList();

    // ---------- Announcements ----------

    [Fact]
    public async Task Announcement_to_a_class_reaches_only_that_class_guardians()
    {
        var f = await FamiliesAsync("Class Notice School");
        var studentClient = await kit.StudentLoginAsync(f.School, f.Ada);
        var adaUserId = await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.Id == f.Ada).Select(s => s.UserId!.Value).FirstAsync(Ct));

        var posted = await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements",
            Announcement("JSS 1A trip on Friday", "Classes", classIds: [f.ClassA], emailGuardians: true), Ct));
        var whatsApp = posted.GetProperty("whatsAppText").GetString()!;
        Assert.Contains("JSS 1A trip on Friday", whatsApp);
        Assert.Contains("Class Notice School", whatsApp);

        // Portals: Ada's family and Ada see it; Bayo's family doesn't.
        Assert.Contains("JSS 1A trip on Friday", await TitlesAsync(f.AdaParent, $"/api/parent/children/{f.Ada}/announcements"));
        Assert.DoesNotContain("JSS 1A trip on Friday", await TitlesAsync(f.BayoParent, $"/api/parent/children/{f.Bayo}/announcements"));
        Assert.Contains("JSS 1A trip on Friday", await TitlesAsync(studentClient, "/api/student/announcements"));
        // A parent can't read another family's child's announcements.
        Assert.Equal(HttpStatusCode.NotFound, (await f.BayoParent.GetAsync($"/api/parent/children/{f.Ada}/announcements", Ct)).StatusCode);

        // In-app: one each for Ada's parent and Ada, nothing for Bayo's parent or the staff.
        await OneAsync(f.AdaParentId, "school.announcement", f.School, "/parent/announcements");
        await OneAsync(adaUserId, "school.announcement", f.School, "/student/announcements");
        await NoneAsync(f.BayoParentId, "school.announcement");
        await NoneAsync(await kit.OwnerIdAsync(f.School), "school.announcement");

        // Emails are queued for the broadcast job, never sent inline.
        Assert.Equal(0, await EmailsAsync(f.MumEmail, "school.announcement"));
        Assert.Equal(1, await SendBroadcastsAsync(f.School.TenantId));
        Assert.Equal(1, await EmailsAsync(f.MumEmail, "school.announcement"));
        Assert.Equal(0, await EmailsAsync(f.DadEmail, "school.announcement"));
        Assert.Equal(0, await SendBroadcastsAsync(f.School.TenantId));
        Assert.Equal(1, await EmailsAsync(f.MumEmail, "school.announcement"));

        // The whole school reaches both families; staff list every announcement, newest first.
        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements", Announcement("Mid-term break", "School"), Ct));
        Assert.Contains("Mid-term break", await TitlesAsync(f.BayoParent, $"/api/parent/children/{f.Bayo}/announcements"));
        // The whole school includes the staff.
        await OneAsync(await kit.OwnerIdAsync(f.School), "school.announcement", f.School, "/app/announcements");
        Assert.Equal(2, (await NotesAsync(f.AdaParentId, "school.announcement")).Count);
        Assert.Equal(["Mid-term break", "JSS 1A trip on Friday"], await TitlesAsync(f.School.Owner, "/api/school/announcements"));
        Assert.Equal(0, await SendBroadcastsAsync(f.School.TenantId)); // not emailed
    }

    [Fact]
    public async Task Level_announcements_reach_every_class_of_the_level_and_emails_go_in_batches()
    {
        var f = await FamiliesAsync("Level Notice School");
        var third = await kit.StudentAsync(f.School, f.ClassA, "Chidi", "Okafor");
        await kit.GuardianAsync(f.School, third, "Ifeoma Okafor", null, $"ify-{Guid.NewGuid():N}@example.com");
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == f.ClassA).Select(c => c.LevelId).FirstAsync(Ct));

        await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements",
            Announcement("JSS 1 exams", "Levels", levelIds: [levelId], emailGuardians: true), Ct));
        Assert.Contains("JSS 1 exams", await TitlesAsync(f.BayoParent, $"/api/parent/children/{f.Bayo}/announcements"));
        await OneAsync(f.BayoParentId, "school.announcement", f.School, "/parent/announcements");

        Assert.Equal(2, await SendBroadcastsAsync(f.School.TenantId, batchSize: 2));
        Assert.Equal(1, await SendBroadcastsAsync(f.School.TenantId, batchSize: 2));
        Assert.Equal(0, await SendBroadcastsAsync(f.School.TenantId, batchSize: 2));
        Assert.Equal(1, await EmailsAsync(f.DadEmail, "school.announcement"));

        // Bad audiences are refused.
        var bad = await f.School.Owner.PostAsJsonAsync("/api/school/announcements", Announcement("Empty", "Classes"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var unknown = await f.School.Owner.PostAsJsonAsync("/api/school/announcements", Announcement("Unknown", "Levels", levelIds: [Guid.NewGuid()]), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_announce_to_other_classes()
    {
        var f = await FamiliesAsync("Teacher Notice School");
        var (teacher, _, _) = await ClassTeacherAsync(f.School, f.ClassA);
        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");

        var other = await teacher.PostAsJsonAsync("/api/school/announcements", Announcement("Not mine", "Classes", classIds: [f.ClassB]), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        var mixed = await teacher.PostAsJsonAsync("/api/school/announcements", Announcement("Mixed", "Classes", classIds: [f.ClassA, f.ClassB]), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, mixed.StatusCode);
        var whole = await teacher.PostAsJsonAsync("/api/school/announcements", Announcement("Everyone", "School"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, whole.StatusCode);
        await NoneAsync(f.BayoParentId, "school.announcement");

        await Json(await teacher.PostAsJsonAsync("/api/school/announcements", Announcement("Homework reminder", "Classes", classIds: [f.ClassA]), Ct));
        await Json(await bursar.PostAsJsonAsync("/api/school/announcements", Announcement("Fees due", "School"), Ct));
        Assert.Contains("Fees due", await TitlesAsync(f.BayoParent, $"/api/parent/children/{f.Bayo}/announcements"));
        Assert.DoesNotContain("Homework reminder", await TitlesAsync(f.BayoParent, $"/api/parent/children/{f.Bayo}/announcements"));
    }

    [Fact]
    public async Task Staff_announcements_reach_only_the_staff()
    {
        var f = await FamiliesAsync("Staff Notice School");
        var (teacher, teacherId, _) = await ClassTeacherAsync(f.School, f.ClassA);
        var (bursar, bursarId) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var ownerId = await kit.OwnerIdAsync(f.School);
        var studentClient = await kit.StudentLoginAsync(f.School, f.Ada);
        var adaUserId = await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.Id == f.Ada).Select(s => s.UserId!.Value).FirstAsync(Ct));

        var denied = await teacher.PostAsJsonAsync("/api/school/announcements", Announcement("Teachers only", "Staff"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await Json(await bursar.PostAsJsonAsync("/api/school/announcements", Announcement("Staff meeting at 3pm", "Staff", emailGuardians: true), Ct));
        await OneAsync(ownerId, "school.announcement", f.School, "/app/announcements");
        await OneAsync(teacherId, "school.announcement", f.School, "/app/announcements");
        await OneAsync(bursarId, "school.announcement", f.School, "/app/announcements");
        await NoneAsync(f.AdaParentId, "school.announcement");
        await NoneAsync(f.BayoParentId, "school.announcement");
        await NoneAsync(adaUserId, "school.announcement");
        Assert.Equal(0, await api.WithDbAsync(db => db.AnnouncementDeliveries.IgnoreQueryFilters().CountAsync(d => d.TenantId == f.School.TenantId, Ct)));
        Assert.Equal(0, await SendBroadcastsAsync(f.School.TenantId));

        Assert.DoesNotContain("Staff meeting at 3pm", await TitlesAsync(f.AdaParent, $"/api/parent/children/{f.Ada}/announcements"));
        Assert.DoesNotContain("Staff meeting at 3pm", await TitlesAsync(studentClient, "/api/student/announcements"));
        Assert.Contains("Staff meeting at 3pm", await TitlesAsync(teacher, "/api/school/announcements"));

        // A class announcement tells no staff.
        await Json(await teacher.PostAsJsonAsync("/api/school/announcements", Announcement("Class trip", "Classes", classIds: [f.ClassA]), Ct));
        Assert.Single(await NotesAsync(ownerId, "school.announcement"));
    }

    [Fact]
    public async Task Announcement_attachments_are_private_and_shown_through_signed_links()
    {
        var f = await FamiliesAsync("Attachment Notice School");
        var id = (await Json(await f.School.Owner.PostAsJsonAsync("/api/school/announcements", Announcement("Calendar", "School"), Ct))).GetProperty("id").GetGuid();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        await Json(await f.School.Owner.PostAsync($"/api/school/announcements/{id}/attachment", new MultipartFormDataContent { { file, "file", "calendar.png" } }, Ct));

        var item = (await Json(await f.AdaParent.GetAsync($"/api/parent/children/{f.Ada}/announcements", Ct))).EnumerateArray().Single();
        var link = item.GetProperty("attachmentUrl").GetString()!;
        Assert.Contains("/api/school/announcement-files/", link);
        var anonymous = api.CreateClient();
        var path = new Uri(link).PathAndQuery;
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path, Ct)).StatusCode);
        var parts = path.Split('/');
        parts[^1] = new string(parts[^1].Reverse().ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);
        var key = await api.WithDbAsync(db => db.Announcements.IgnoreQueryFilters().Where(a => a.Id == id).Select(a => a.AttachmentKey).SingleAsync(Ct));
        Assert.StartsWith("private/", key);
    }

    // ---------- Messages ----------

    [Fact]
    public async Task Parent_messages_the_class_teacher_and_the_teacher_replies()
    {
        var f = await FamiliesAsync("Teacher Thread School");
        var (teacher, teacherId, _) = await ClassTeacherAsync(f.School, f.ClassA);
        var ownerId = await kit.OwnerIdAsync(f.School);

        var thread = await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "class-teacher", body = "Ada will be late on Monday." }, Ct));
        var threadId = thread.GetProperty("id").GetGuid();

        // The teacher hears about it, in the app and by email; the office doesn't.
        await OneAsync(teacherId, "school.message", f.School, $"/app/messages/{threadId}");
        Assert.Equal(1, await EmailsAsync(await EmailOfAsync(teacherId), "school.message"));
        await NoneAsync(ownerId, "school.message");

        // A second message from the parent goes to the same thread.
        var again = await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "class-teacher", body = "Sorry, Tuesday." }, Ct));
        Assert.Equal(threadId, again.GetProperty("id").GetGuid());

        var mine = (await Json(await teacher.GetAsync("/api/school/threads?mine=true", Ct))).EnumerateArray().ToList();
        Assert.Equal(threadId, Assert.Single(mine).GetProperty("id").GetGuid());
        var messages = (await Json(await teacher.GetAsync($"/api/school/threads/{threadId}/messages", Ct))).EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("Ada will be late on Monday.", messages[0].GetProperty("body").GetString());
        await Json(await teacher.PostAsJsonAsync($"/api/school/threads/{threadId}/messages", new { body = "Thanks for letting me know." }, Ct));

        await OneAsync(f.AdaParentId, "school.message", f.School, $"/parent/messages/{threadId}");
        Assert.Equal(1, await EmailsAsync(f.MumEmail, "school.message"));
        await NoneAsync(f.BayoParentId, "school.message");

        // The parent sees the reply; their own messages show as read by the teacher.
        var parentView = (await Json(await f.AdaParent.GetAsync($"/api/parent/threads/{threadId}/messages", Ct))).EnumerateArray().ToList();
        Assert.Equal(3, parentView.Count);
        Assert.Equal("Thanks for letting me know.", parentView[2].GetProperty("body").GetString());
        Assert.NotEqual(JsonValueKind.Null, parentView[0].GetProperty("readAt").ValueKind);
        Assert.False(parentView[2].GetProperty("fromParent").GetBoolean());

        // The parent replies on the thread itself; the teacher gets a second notification.
        await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/threads/{threadId}/messages", new { body = "See you then." }, Ct));
        Assert.Equal(3, (await NotesAsync(teacherId, "school.message")).Count);

        // A class without a class teacher: the parent is told to message the office.
        var noTeacher = await f.BayoParent.PostAsJsonAsync($"/api/parent/children/{f.Bayo}/threads", new { to = "class-teacher", body = "Hello" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, noTeacher.StatusCode);
        Assert.Contains("office", await noTeacher.Content.ReadAsStringAsync(Ct));

        // Message threads are part of a student's records.
        var delete = await f.School.Owner.DeleteAsync($"/api/school/students/{f.Ada}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Office_threads_are_seen_by_leadership_not_teachers()
    {
        var f = await FamiliesAsync("Office Thread School");
        var (teacher, teacherId, _) = await ClassTeacherAsync(f.School, f.ClassA);
        var (admin, adminId) = await kit.StaffAsync(f.School, MemberRole.Admin, "Principal");
        var ownerId = await kit.OwnerIdAsync(f.School);

        var threadId = (await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "office", body = "Can I pay the fees in two parts?" }, Ct))).GetProperty("id").GetGuid();

        await OneAsync(ownerId, "school.message", f.School, $"/app/messages/{threadId}");
        await OneAsync(adminId, "school.message", f.School, $"/app/messages/{threadId}");
        await NoneAsync(teacherId, "school.message");

        static async Task<List<Guid>> Ids(HttpClient c, string url) =>
            (await Json(await c.GetAsync(url, Ct))).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(threadId, await Ids(f.School.Owner, "/api/school/threads?mine=true"));
        Assert.Contains(threadId, await Ids(admin, "/api/school/threads"));
        Assert.DoesNotContain(threadId, await Ids(teacher, "/api/school/threads"));
        Assert.DoesNotContain(threadId, await Ids(teacher, "/api/school/threads?mine=true"));
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"/api/school/threads/{threadId}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.PostAsJsonAsync($"/api/school/threads/{threadId}/messages", new { body = "Hi" }, Ct)).StatusCode);

        await Json(await admin.PostAsJsonAsync($"/api/school/threads/{threadId}/messages", new { body = "Yes, speak to the bursar." }, Ct));
        await OneAsync(f.AdaParentId, "school.message", f.School, $"/parent/messages/{threadId}");

        // Another school's staff can't reach it either.
        var other = await kit.SchoolAsync("Other Office School");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.GetAsync($"/api/school/threads/{threadId}/messages", Ct)).StatusCode);
    }

    [Fact]
    public async Task Teachers_cannot_open_other_teachers_class_threads()
    {
        var f = await FamiliesAsync("Other Teacher Thread School");
        var (teacherA, _, _) = await ClassTeacherAsync(f.School, f.ClassA, "Teacher A");
        var (teacherB, teacherBId, _) = await ClassTeacherAsync(f.School, f.ClassB, "Teacher B");
        // A subject teacher of class A who is not its class teacher.
        var (maths, mathsId) = await kit.StaffAsync(f.School, MemberRole.Staff, "Maths Teacher");
        var subject = await Json(await f.School.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        var classSubject = (await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/classes/{f.ClassA}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid() }, Ct))).GetProperty("id").GetGuid();
        await Json(await f.School.Owner.PutAsJsonAsync($"/api/school/class-subjects/{classSubject}/teacher", new { staffId = await StaffIdAsync(mathsId) }, Ct));

        var threadId = (await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "class-teacher", body = "A question for Ada's teacher." }, Ct))).GetProperty("id").GetGuid();
        Assert.Single((await Json(await teacherA.GetAsync("/api/school/threads", Ct))).EnumerateArray());

        foreach (var other in new[] { teacherB, maths })
        {
            Assert.Empty((await Json(await other.GetAsync("/api/school/threads", Ct))).EnumerateArray());
            Assert.Empty((await Json(await other.GetAsync("/api/school/threads?mine=true", Ct))).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/school/threads/{threadId}/messages", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/school/threads/{threadId}/messages", new { body = "Hi" }, Ct)).StatusCode);
        }
        await NoneAsync(teacherBId, "school.message");
        await NoneAsync(mathsId, "school.message");
    }

    [Fact]
    public async Task Only_the_addressed_side_marks_a_parents_message_read()
    {
        var f = await FamiliesAsync("Read Receipt School");
        var (teacher, _, _) = await ClassTeacherAsync(f.School, f.ClassA);
        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");

        var threadId = (await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "class-teacher", body = "For the class teacher." }, Ct))).GetProperty("id").GetGuid();
        async Task<JsonValueKind> ParentMessageReadAsync(Guid id) =>
            (await Json(await f.AdaParent.GetAsync($"/api/parent/threads/{id}/messages", Ct))).EnumerateArray().First().GetProperty("readAt").ValueKind;
        async Task<int> UnreadAsync(HttpClient c, Guid id) =>
            (await Json(await c.GetAsync("/api/school/threads", Ct))).EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id).GetProperty("unread").GetInt32();

        // Leadership reading a teacher's thread leaves the parent's message unread for the teacher.
        Assert.Equal(1, (await Json(await f.School.Owner.GetAsync($"/api/school/threads/{threadId}/messages", Ct))).GetArrayLength());
        Assert.Equal(JsonValueKind.Null, await ParentMessageReadAsync(threadId));
        Assert.Equal(1, await UnreadAsync(teacher, threadId));

        await Json(await teacher.GetAsync($"/api/school/threads/{threadId}/messages", Ct));
        Assert.NotEqual(JsonValueKind.Null, await ParentMessageReadAsync(threadId));
        Assert.Equal(0, await UnreadAsync(teacher, threadId));

        // An office thread is read by the office, including the bursar.
        var office = (await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "office", body = "For the office." }, Ct))).GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, await ParentMessageReadAsync(office));
        await Json(await bursar.GetAsync($"/api/school/threads/{office}/messages", Ct));
        Assert.NotEqual(JsonValueKind.Null, await ParentMessageReadAsync(office));

        // The school's reply is read only when the parent opens the thread.
        await Json(await teacher.PostAsJsonAsync($"/api/school/threads/{threadId}/messages", new { body = "Noted." }, Ct));
        await Json(await f.School.Owner.GetAsync($"/api/school/threads/{threadId}/messages", Ct));
        var reply = (await Json(await teacher.GetAsync($"/api/school/threads/{threadId}/messages", Ct))).EnumerateArray().Last();
        Assert.Equal(JsonValueKind.Null, reply.GetProperty("readAt").ValueKind);
        var seen = (await Json(await f.AdaParent.GetAsync($"/api/parent/threads/{threadId}/messages", Ct))).EnumerateArray().Last();
        Assert.Equal("Noted.", seen.GetProperty("body").GetString());
        var afterParent = (await Json(await teacher.GetAsync($"/api/school/threads/{threadId}/messages", Ct))).EnumerateArray().Last();
        Assert.NotEqual(JsonValueKind.Null, afterParent.GetProperty("readAt").ValueKind);
    }

    [Fact]
    public async Task Parents_cannot_read_other_parents_threads()
    {
        var f = await FamiliesAsync("Private Thread School");
        var threadId = (await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "office", body = "A private question." }, Ct))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await f.BayoParent.GetAsync($"/api/parent/threads/{threadId}/messages", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.BayoParent.PostAsJsonAsync($"/api/parent/threads/{threadId}/messages", new { body = "Hi" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.BayoParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads", new { to = "office", body = "Hi" }, Ct)).StatusCode);
        Assert.Empty((await Json(await f.BayoParent.GetAsync($"/api/parent/children/{f.Bayo}/threads", Ct))).EnumerateArray());
        Assert.Single((await Json(await f.AdaParent.GetAsync($"/api/parent/children/{f.Ada}/threads", Ct))).EnumerateArray());
        // Staff tokens can't use the parent routes.
        Assert.Equal(HttpStatusCode.Forbidden, (await f.School.Owner.GetAsync($"/api/parent/threads/{threadId}/messages", Ct)).StatusCode);
        // Bad input.
        Assert.Equal(HttpStatusCode.BadRequest, (await f.AdaParent.PostAsJsonAsync($"/api/parent/threads/{threadId}/messages", new { body = "  " }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads", new { to = "headmaster", body = "Hi" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_message_can_carry_one_private_image()
    {
        var f = await FamiliesAsync("Image Thread School");
        var threadId = (await Json(await f.AdaParent.PostAsJsonAsync($"/api/parent/children/{f.Ada}/threads",
            new { to = "office", body = "Here is the receipt." }, Ct))).GetProperty("id").GetGuid();

        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var form = new MultipartFormDataContent { { file, "file", "receipt.png" }, { new StringContent("The teller"), "body" } };
        var sent = await Json(await f.AdaParent.PostAsync($"/api/parent/threads/{threadId}/messages/image", form, Ct));
        var link = sent.GetProperty("imageUrl").GetString()!;
        Assert.Contains("/api/school/message-images/", link);

        var staffView = (await Json(await f.School.Owner.GetAsync($"/api/school/threads/{threadId}/messages", Ct))).EnumerateArray().ToList();
        Assert.Equal("The teller", staffView[1].GetProperty("body").GetString());
        Assert.NotEqual(JsonValueKind.Null, staffView[1].GetProperty("imageUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, staffView[0].GetProperty("imageUrl").ValueKind);

        var anonymous = api.CreateClient();
        var path = new Uri(link).PathAndQuery;
        var ok = await anonymous.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("image/webp", ok.Content.Headers.ContentType?.MediaType);
        var parts = path.Split('/');
        parts[^1] = new string(parts[^1].Reverse().ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(string.Join('/', parts), Ct)).StatusCode);

        var bad = new ByteArrayContent("not an image"u8.ToArray());
        bad.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await f.AdaParent.PostAsync($"/api/parent/threads/{threadId}/messages/image", new MultipartFormDataContent { { bad, "file", "x.png" } }, Ct)).StatusCode);
    }
}
