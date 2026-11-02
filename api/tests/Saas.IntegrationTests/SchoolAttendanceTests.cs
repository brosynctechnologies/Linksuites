using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink attendance: registers saved once per class and date, who may take them, and absence alerts sent once per student per day.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolAttendanceTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);

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

    /// <summary>The current term covers today whatever the calendar says, so dates a few days back are inside it.</summary>
    private async Task<SchoolKit.School> SchoolWithTermAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct));
        return school;
    }

    private async Task<(HttpClient Client, Guid StaffId)> TeacherAsync(SchoolKit.School school)
    {
        var (client, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));
        return (client, staffId);
    }

    private static string Url(Guid classId, DateOnly date) => $"/api/school/attendance/{classId}/{date:yyyy-MM-dd}";

    private static object Put(string clientId, IEnumerable<(Guid Enrolment, string Status, string? Note)> marks) =>
        new { clientId, marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status, note = m.Note }).ToArray() };

    private static Dictionary<string, JsonElement> MarksByName(JsonElement register) =>
        register.GetProperty("marks").EnumerateArray().ToDictionary(m => m.GetProperty("studentName").GetString()!);

    [Fact]
    public async Task Register_defaults_to_present_and_saves_marks()
    {
        var school = await SchoolWithTermAsync("Register Academy");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        await kit.StudentAsync(school, classId, "Tolu");
        await kit.StudentAsync(school, classId, "Chidi");

        var fresh = await Json(await school.Owner.GetAsync(Url(classId, Today), Ct));
        Assert.False(fresh.GetProperty("taken").GetBoolean());
        Assert.Equal(3, fresh.GetProperty("marks").GetArrayLength());
        Assert.All(fresh.GetProperty("marks").EnumerateArray(), m => Assert.Equal("Present", m.GetProperty("status").GetString()));

        var marks = MarksByName(fresh);
        var saved = await Json(await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("device-1", new[]
        {
            (marks["Ada Obi"].GetProperty("enrolmentId").GetGuid(), "Absent", (string?)"Sick at home"),
            (marks["Tolu Obi"].GetProperty("enrolmentId").GetGuid(), "Late", null),
        }), Ct));
        Assert.True(saved.GetProperty("taken").GetBoolean());

        var again = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        Assert.Equal("Absent", again["Ada Obi"].GetProperty("status").GetString());
        Assert.Equal("Sick at home", again["Ada Obi"].GetProperty("note").GetString());
        Assert.Equal("Late", again["Tolu Obi"].GetProperty("status").GetString());
        Assert.Equal("Present", again["Chidi Obi"].GetProperty("status").GetString()); // left out of the save: present

        // Marks for an enrolment that is not in this class, a bad status and a future date are rejected.
        var otherClass = await kit.ClassAsync(school, "B");
        await kit.StudentAsync(school, otherClass, "Zara");
        var outsider = MarksByName(await Json(await school.Owner.GetAsync(Url(otherClass, Today), Ct)))["Zara Obi"].GetProperty("enrolmentId").GetGuid();
        var wrongClass = await ProblemAsync(await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("x", [(outsider, "Absent", null)]), Ct), HttpStatusCode.BadRequest);
        Assert.True(wrongClass.GetProperty("errors").TryGetProperty("marks", out _), wrongClass.ToString());
        var badStatus = await ProblemAsync(await school.Owner.PutAsJsonAsync(Url(classId, Today),
            Put("x", [(marks["Ada Obi"].GetProperty("enrolmentId").GetGuid(), "Sleeping", null)]), Ct), HttpStatusCode.BadRequest);
        Assert.True(badStatus.GetProperty("errors").TryGetProperty("marks", out _), badStatus.ToString());
        var future = await ProblemAsync(await school.Owner.PutAsJsonAsync(Url(classId, Today.AddDays(3)), Put("x", []), Ct), HttpStatusCode.BadRequest);
        Assert.True(future.GetProperty("errors").TryGetProperty("date", out _), future.ToString());
    }

    [Fact]
    public async Task Resending_the_same_register_keeps_one_register_with_the_latest_marks()
    {
        var school = await SchoolWithTermAsync("Resend Academy");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        await kit.StudentAsync(school, classId, "Tolu");
        var marks = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        var ada = marks["Ada Obi"].GetProperty("enrolmentId").GetGuid();
        var tolu = marks["Tolu Obi"].GetProperty("enrolmentId").GetGuid();

        for (var i = 0; i < 3; i++) // the phone resends the same register until it hears back
            (await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("phone-1", [(ada, "Absent", null)]), Ct)).EnsureSuccessStatusCode();
        // A later edit from another device wins.
        (await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("phone-2", [(ada, "Present", null), (tolu, "Excused", "Clinic")]), Ct)).EnsureSuccessStatusCode();

        Assert.Equal(1, await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().CountAsync(r => r.TenantId == school.TenantId && r.ClassId == classId, Ct)));
        Assert.Equal(2, await api.WithDbAsync(db => db.AttendanceMarks.IgnoreQueryFilters().CountAsync(m => m.TenantId == school.TenantId, Ct)));
        var latest = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        Assert.Equal("Present", latest["Ada Obi"].GetProperty("status").GetString());
        Assert.Equal("Excused", latest["Tolu Obi"].GetProperty("status").GetString());
        Assert.Equal("phone-2", await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().Where(r => r.TenantId == school.TenantId).Select(r => r.ClientId!).SingleAsync(Ct)));
    }

    private static object PutFrom(string clientId, string baseClientId, IEnumerable<(Guid Enrolment, string Status, string? Note)> marks) =>
        new { clientId, baseClientId, marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status, note = m.Note }).ToArray() };

    [Fact]
    public async Task A_delayed_offline_save_does_not_overwrite_a_newer_register()
    {
        var school = await SchoolWithTermAsync("Stale Resend Academy");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        // Phone A opens the register before anyone has taken it (base: not taken), then goes offline and queues "Ada Absent".
        var loaded = await Json(await school.Owner.GetAsync(Url(classId, Today), Ct));
        Assert.False(loaded.GetProperty("taken").GetBoolean());
        var ada = MarksByName(loaded)["Ada Obi"].GetProperty("enrolmentId").GetGuid();

        // The principal, online, marks Ada present.
        (await school.Owner.PutAsJsonAsync(Url(classId, Today), PutFrom("principal-1", "", [(ada, "Present", null)]), Ct)).EnsureSuccessStatusCode();

        // Phone A reconnects and sends its old save: refused, and the principal's marks stay.
        var stale = await ProblemAsync(await school.Owner.PutAsJsonAsync(Url(classId, Today), PutFrom("phone-a-1", "", [(ada, "Absent", null)]), Ct), HttpStatusCode.Conflict);
        Assert.Equal("register_changed", stale.GetProperty("code").GetString());
        var after = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        Assert.Equal("Present", after["Ada Obi"].GetProperty("status").GetString());
        Assert.Equal("principal-1", await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().Where(r => r.TenantId == school.TenantId).Select(r => r.ClientId!).SingleAsync(Ct)));

        // A resend of the principal's own save is still a no-op, and a save based on the stored register goes through.
        (await school.Owner.PutAsJsonAsync(Url(classId, Today), PutFrom("principal-1", "", [(ada, "Present", null)]), Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync(Url(classId, Today), PutFrom("phone-a-2", "principal-1", [(ada, "Late", null)]), Ct)).EnsureSuccessStatusCode();
        Assert.Equal("Late", MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)))["Ada Obi"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Two_saves_at_once_leave_one_register()
    {
        var school = await SchoolWithTermAsync("Parallel Academy");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        await kit.StudentAsync(school, classId, "Tolu");
        var marks = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        var ada = marks["Ada Obi"].GetProperty("enrolmentId").GetGuid();
        var tolu = marks["Tolu Obi"].GetProperty("enrolmentId").GetGuid();

        var responses = await Task.WhenAll(
            school.Owner.PutAsJsonAsync(Url(classId, Today), Put("a", [(ada, "Absent", null), (tolu, "Late", null)]), Ct),
            school.Owner.PutAsJsonAsync(Url(classId, Today), Put("b", [(ada, "Late", null), (tolu, "Absent", null)]), Ct));
        foreach (var r in responses) Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");

        Assert.Equal(1, await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().CountAsync(r => r.TenantId == school.TenantId && r.ClassId == classId, Ct)));
        Assert.Equal(2, await api.WithDbAsync(db => db.AttendanceMarks.IgnoreQueryFilters().CountAsync(m => m.TenantId == school.TenantId, Ct)));
        // Each of the two saves was whole: the stored marks are one save's marks, not a mix.
        var stored = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        var pair = (stored["Ada Obi"].GetProperty("status").GetString(), stored["Tolu Obi"].GetProperty("status").GetString());
        Assert.True(pair is ("Absent", "Late") or ("Late", "Absent"), pair.ToString());
    }

    [Fact]
    public async Task Absence_alert_is_sent_once_per_student_per_day()
    {
        var school = await SchoolWithTermAsync("Alert Academy");
        var classId = await kit.ClassAsync(school);
        var adaId = await kit.StudentAsync(school, classId, "Ada");
        var toluId = await kit.StudentAsync(school, classId, "Tolu");
        var email = $"mum-{Guid.NewGuid():N}@example.com";
        await kit.GuardianAsync(school, adaId, "Ngozi Obi", "0803 123 4567", email);
        await kit.GuardianAsync(school, toluId, "No Contact", null, null);
        var marks = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        var ada = marks["Ada Obi"].GetProperty("enrolmentId").GetGuid();
        var tolu = marks["Tolu Obi"].GetProperty("enrolmentId").GetGuid();

        var first = await Json(await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("c1", [(ada, "Absent", null), (tolu, "Absent", null)]), Ct));
        var parents = first.GetProperty("messageParents").EnumerateArray().ToList();
        var message = Assert.Single(parents); // Tolu's guardian has no phone
        Assert.Equal("Ada Obi", message.GetProperty("studentName").GetString());
        Assert.Equal("Ngozi Obi", message.GetProperty("guardianName").GetString());
        var url = message.GetProperty("whatsAppUrl").GetString()!;
        Assert.StartsWith("https://wa.me/2348031234567?text=", url);
        Assert.Contains("Ada", Uri.UnescapeDataString(url));

        var mails = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.absent" && n.Recipient == email).ToListAsync(Ct));
        var mail = Assert.Single(mails);
        Assert.Contains("Ada Obi", mail.Payload["child"]);
        Assert.Equal("Alert Academy", mail.Payload["school"]);

        // Present, then absent again the same day, and a resend from another device: no second email, nothing to message.
        (await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("c2", [(ada, "Present", null)]), Ct)).EnsureSuccessStatusCode();
        var again = await Json(await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("c3", [(ada, "Absent", null)]), Ct));
        Assert.Equal(0, again.GetProperty("messageParents").GetArrayLength());
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.absent" && n.Recipient == email, Ct)));
        Assert.Equal(2, await api.WithDbAsync(db => db.AbsenceAlerts.IgnoreQueryFilters().CountAsync(a => a.TenantId == school.TenantId, Ct)));

        // The next day is a new alert.
        var yesterday = Today.AddDays(-1);
        (await school.Owner.PutAsJsonAsync(Url(classId, yesterday), Put("c4", [(ada, "Absent", null)]), Ct)).EnsureSuccessStatusCode();
        Assert.Equal(2, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.absent" && n.Recipient == email, Ct)));
    }

    [Fact]
    public async Task Teachers_cannot_take_other_classes_registers()
    {
        var school = await SchoolWithTermAsync("Teacher Academy");
        var mine = await kit.ClassAsync(school);
        var theirs = await kit.ClassAsync(school, "B");
        await kit.StudentAsync(school, mine, "Ada");
        await kit.StudentAsync(school, theirs, "Zara");
        var (teacher, staffId) = await TeacherAsync(school);
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{mine}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();

        (await teacher.GetAsync(Url(mine, Today), Ct)).EnsureSuccessStatusCode();
        (await teacher.PutAsJsonAsync(Url(mine, Today), Put("t1", []), Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Url(theirs, Today), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync(Url(theirs, Today), Put("t2", []), Ct)).StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().CountAsync(r => r.TenantId == school.TenantId && r.ClassId == theirs, Ct)));

        // The bursar does not take registers, and only leadership sees the school-wide summary.
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PutAsJsonAsync(Url(mine, Today), Put("b1", []), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"/api/school/attendance/summary?date={Today:yyyy-MM-dd}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Teachers_cannot_edit_registers_older_than_7_days()
    {
        var school = await SchoolWithTermAsync("Window Academy");
        var classId = await kit.ClassAsync(school);
        await kit.StudentAsync(school, classId, "Ada");
        var (teacher, staffId) = await TeacherAsync(school);
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();

        (await teacher.PutAsJsonAsync(Url(classId, Today.AddDays(-7)), Put("w1", []), Ct)).EnsureSuccessStatusCode();
        var old = await ProblemAsync(await teacher.PutAsJsonAsync(Url(classId, Today.AddDays(-8)), Put("w2", []), Ct), HttpStatusCode.Conflict);
        Assert.Equal("You can't change registers from that date.", old.GetProperty("detail").GetString());

        // Leadership may edit any date in the current term, but not before it starts.
        (await school.Owner.PutAsJsonAsync(Url(classId, Today.AddDays(-8)), Put("w3", []), Ct)).EnsureSuccessStatusCode();
        await ProblemAsync(await school.Owner.PutAsJsonAsync(Url(classId, Today.AddDays(-61)), Put("w4", []), Ct), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Summary_and_student_totals_count_late_as_present()
    {
        var school = await SchoolWithTermAsync("Totals Academy");
        var classId = await kit.ClassAsync(school);
        var emptyClass = await kit.ClassAsync(school, "B");
        var adaId = await kit.StudentAsync(school, classId, "Ada");
        await kit.StudentAsync(school, classId, "Tolu");
        await kit.StudentAsync(school, classId, "Chidi");
        await kit.StudentAsync(school, classId, "Emeka");
        var marks = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        Guid E(string n) => marks[n].GetProperty("enrolmentId").GetGuid();

        (await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("s1", [(E("Ada Obi"), "Absent", null), (E("Tolu Obi"), "Late", null), (E("Chidi Obi"), "Excused", null)]), Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync(Url(classId, Today.AddDays(-1)), Put("s2", [(E("Ada Obi"), "Late", null)]), Ct)).EnsureSuccessStatusCode();

        var summary = (await Json(await school.Owner.GetAsync($"/api/school/attendance/summary?date={Today:yyyy-MM-dd}", Ct))).EnumerateArray().ToList();
        var row = summary.Single(r => r.GetProperty("classId").GetGuid() == classId);
        Assert.True(row.GetProperty("taken").GetBoolean());
        Assert.Equal(1, row.GetProperty("present").GetInt32()); // Emeka
        Assert.Equal(1, row.GetProperty("absent").GetInt32());
        Assert.Equal(1, row.GetProperty("late").GetInt32());
        Assert.Equal(1, row.GetProperty("excused").GetInt32());
        Assert.Equal(50.0, row.GetProperty("percent").GetDouble()); // (1 present + 1 late) of 4
        var empty = summary.Single(r => r.GetProperty("classId").GetGuid() == emptyClass);
        Assert.False(empty.GetProperty("taken").GetBoolean());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("percent").ValueKind);

        var ada = await Json(await school.Owner.GetAsync($"/api/school/students/{adaId}/attendance", Ct));
        Assert.Equal(2, ada.GetProperty("daysOpen").GetInt32());
        Assert.Equal(0, ada.GetProperty("present").GetInt32()); // today Absent, yesterday Late: Late has its own column
        Assert.Equal(1, ada.GetProperty("late").GetInt32());
        Assert.Equal(1, ada.GetProperty("absent").GetInt32());
        Assert.Equal(0, ada.GetProperty("excused").GetInt32());

        // A teacher who does not teach the class cannot see the student's attendance.
        var (stranger, _) = await TeacherAsync(school);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/school/students/{adaId}/attendance", Ct)).StatusCode);
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        // Nor can the bursar: the spec's role table gives the bursar no attendance access (Task 21 ruling).
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/students/{adaId}/attendance", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_student_with_attendance_cannot_be_deleted()
    {
        var school = await SchoolWithTermAsync("History Academy");
        var classId = await kit.ClassAsync(school);
        var adaId = await kit.StudentAsync(school, classId, "Ada");
        var marks = MarksByName(await Json(await school.Owner.GetAsync(Url(classId, Today), Ct)));
        (await school.Owner.PutAsJsonAsync(Url(classId, Today), Put("h1", [(marks["Ada Obi"].GetProperty("enrolmentId").GetGuid(), "Present", null)]), Ct)).EnsureSuccessStatusCode();

        await ProblemAsync(await school.Owner.DeleteAsync($"/api/school/students/{adaId}", Ct), HttpStatusCode.Conflict);
    }
}
