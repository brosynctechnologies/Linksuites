using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using static Saas.IntegrationTests.SchoolPortalKit;

namespace Saas.IntegrationTests;

/// <summary>
/// Attendance per subject (wave 4, 4b item 1): off until the school turns it on; a register per lesson on the timetable, taken by the
/// subject teacher of that class subject or leadership; Present, Absent or Late; the current term and its lock respected; reports per
/// student per subject per term for staff and in the portals. Daily registers are untouched.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolSubjectAttendanceTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolPortalKit portal = new(api);

    private sealed record World(Setup S, HttpClient MathTeacher, HttpClient EnglishTeacher, Family Ada, Family Tolu);

    /// <summary>The n-th most recent school day (Monday to Saturday) on or before today.</summary>
    private static DateOnly SchoolDayBack(int n)
    {
        var day = Today;
        while (true)
        {
            if (day.DayOfWeek != DayOfWeek.Sunday)
            {
                if (n == 0) return day;
                n--;
            }
            day = day.AddDays(-1);
        }
    }

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    /// <summary>
    /// JSS 1A with Mathematics (its own teacher) at period 1 and English (another teacher) at period 2 on every school day, Ada and Tolu
    /// in the class with their parents, and subject attendance switched on.
    /// </summary>
    private async Task<World> WorldAsync(string name, bool enable = true)
    {
        var s = await portal.SetupAsync(name);
        var (math, mathUser) = await portal.Kit.StaffAsync(s.School, MemberRole.Staff, "Maths teacher");
        var (english, englishUser) = await portal.Kit.StaffAsync(s.School, MemberRole.Staff, "English teacher");
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/class-subjects/{s.MathId}/teacher", new { staffId = await StaffIdAsync(s.School, mathUser) }, Ct));
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/class-subjects/{s.EnglishId}/teacher", new { staffId = await StaffIdAsync(s.School, englishUser) }, Ct));
        var slots = new List<object>();
        foreach (var day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" })
        {
            slots.Add(Slot(day, 1, "08:00", "08:40", s.MathId));
            slots.Add(Slot(day, 2, "08:40", "09:20", s.EnglishId));
        }
        await TimetableAsync(s.School.Owner, s.ClassId, slots.ToArray());
        var ada = await portal.FamilyAsync(s, "Ada", "Zed");
        var tolu = await portal.FamilyAsync(s, "Tolu", "Ige");
        if (enable) await Json(await s.School.Owner.PutAsJsonAsync("/api/school/settings/subject-attendance", new { enabled = true }, Ct));
        return new World(s, math, english, ada, tolu);
    }

    private static string RegisterUrl(Guid classSubjectId, DateOnly date, int period) =>
        $"/api/school/subject-attendance/{classSubjectId}/{date:yyyy-MM-dd}/{period}";

    private static object Marks(params (Guid Enrolment, string Status)[] marks) =>
        new { clientId = Guid.NewGuid().ToString(), marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray() };

    private static JsonElement MarkOf(JsonElement register, Guid enrolmentId) =>
        register.GetProperty("marks").EnumerateArray().Single(m => m.GetProperty("enrolmentId").GetGuid() == enrolmentId);

    [Fact]
    public async Task Off_until_the_principal_turns_it_on()
    {
        var w = await WorldAsync("Subject Register Off", enable: false);
        var date = SchoolDayBack(0);

        var settings = await Json(await w.S.School.Owner.GetAsync("/api/school/settings/subject-attendance", Ct));
        Assert.False(settings.GetProperty("enabled").GetBoolean());
        var lessons = await Json(await w.MathTeacher.GetAsync($"/api/school/subject-attendance/lessons?date={date:yyyy-MM-dd}", Ct));
        Assert.False(lessons.GetProperty("enabled").GetBoolean());
        Assert.Empty(lessons.GetProperty("lessons").EnumerateArray());

        Assert.Equal("Subject attendance is off. The principal can turn it on in Settings.",
            await ProblemAsync(await w.MathTeacher.GetAsync(RegisterUrl(w.S.MathId, date, 1), Ct), HttpStatusCode.Conflict));
        var refused = await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 1), Marks(), Ct);
        Assert.Equal("Subject attendance is off. The principal can turn it on in Settings.", await ProblemAsync(refused, HttpStatusCode.Conflict));

        // A teacher cannot turn it on; the principal can.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.MathTeacher.PutAsJsonAsync("/api/school/settings/subject-attendance", new { enabled = true }, Ct)).StatusCode);
        var on = await Json(await w.S.School.Owner.PutAsJsonAsync("/api/school/settings/subject-attendance", new { enabled = true }, Ct));
        Assert.True(on.GetProperty("enabled").GetBoolean());
        await Json(await w.MathTeacher.GetAsync(RegisterUrl(w.S.MathId, date, 1), Ct));

        // The portals show nothing while it is off.
        await Json(await w.S.School.Owner.PutAsJsonAsync("/api/school/settings/subject-attendance", new { enabled = false }, Ct));
        var portalView = await Json(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/subject-attendance", Ct));
        Assert.False(portalView.GetProperty("enabled").GetBoolean());
        Assert.Empty(portalView.GetProperty("subjects").EnumerateArray());
    }

    [Fact]
    public async Task Subject_teacher_takes_the_register_of_a_lesson_on_the_timetable()
    {
        var w = await WorldAsync("Subject Register School");
        var date = SchoolDayBack(0);

        // The teacher's lessons of the day: only their own subject.
        var lessons = await Json(await w.MathTeacher.GetAsync($"/api/school/subject-attendance/lessons?date={date:yyyy-MM-dd}", Ct));
        Assert.True(lessons.GetProperty("enabled").GetBoolean());
        var lesson = Assert.Single(lessons.GetProperty("lessons").EnumerateArray());
        Assert.Equal(w.S.MathId, lesson.GetProperty("classSubjectId").GetGuid());
        Assert.Equal(1, lesson.GetProperty("period").GetInt32());
        Assert.Equal("08:00", lesson.GetProperty("start").GetString());
        Assert.Equal("Mathematics", lesson.GetProperty("subject").GetString());
        Assert.False(lesson.GetProperty("taken").GetBoolean());
        // Leadership sees every lesson of the day.
        var all = await Json(await w.S.School.Owner.GetAsync($"/api/school/subject-attendance/lessons?date={date:yyyy-MM-dd}", Ct));
        Assert.Equal(2, all.GetProperty("lessons").GetArrayLength());

        var fresh = await Json(await w.MathTeacher.GetAsync(RegisterUrl(w.S.MathId, date, 1), Ct));
        Assert.False(fresh.GetProperty("taken").GetBoolean());
        Assert.Equal(2, fresh.GetProperty("marks").GetArrayLength());
        Assert.All(fresh.GetProperty("marks").EnumerateArray(), m => Assert.Equal("Present", m.GetProperty("status").GetString()));

        var saved = await Json(await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 1), Marks((w.Ada.EnrolmentId, "Absent"), (w.Tolu.EnrolmentId, "Late")), Ct));
        Assert.True(saved.GetProperty("taken").GetBoolean());
        Assert.Equal("Absent", MarkOf(saved, w.Ada.EnrolmentId).GetProperty("status").GetString());
        Assert.Equal("Late", MarkOf(saved, w.Tolu.EnrolmentId).GetProperty("status").GetString());
        var after = await Json(await w.MathTeacher.GetAsync($"/api/school/subject-attendance/lessons?date={date:yyyy-MM-dd}", Ct));
        var taken = Assert.Single(after.GetProperty("lessons").EnumerateArray());
        Assert.True(taken.GetProperty("taken").GetBoolean());
        Assert.Equal(1, taken.GetProperty("absent").GetInt32());
        Assert.Equal(1, taken.GetProperty("late").GetInt32());

        // Leadership may change it; Excused is for the daily register only.
        await Json(await w.S.School.Owner.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 1), Marks((w.Ada.EnrolmentId, "Present")), Ct));
        var excused = await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 1), Marks((w.Ada.EnrolmentId, "Excused")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, excused.StatusCode);
        Assert.Contains("Use Present, Absent or Late for each student.", await excused.Content.ReadAsStringAsync(Ct));

        // Only the subject teacher of this class subject: the English teacher (who teaches the class) and the bursar are refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.EnglishTeacher.GetAsync(RegisterUrl(w.S.MathId, date, 1), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.EnglishTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 1), Marks(), Ct)).StatusCode);
        var (bursar, _) = await portal.Kit.StaffAsync(w.S.School, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync(RegisterUrl(w.S.MathId, date, 1), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/subject-attendance/lessons?date={date:yyyy-MM-dd}", Ct)).StatusCode);

        // A period with no Mathematics lesson on the timetable is not a lesson.
        Assert.Equal("This lesson was not found.", await ProblemAsync(await w.MathTeacher.GetAsync(RegisterUrl(w.S.MathId, date, 2), Ct), HttpStatusCode.NotFound));
        Assert.Equal("This lesson was not found.", await ProblemAsync(await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 3), Marks(), Ct), HttpStatusCode.NotFound));

        // The daily register is unchanged: not taken.
        var daily = await Json(await w.S.School.Owner.GetAsync($"/api/school/attendance/{w.S.ClassId}/{date:yyyy-MM-dd}", Ct));
        Assert.False(daily.GetProperty("taken").GetBoolean());
        Assert.Equal(0, await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().CountAsync(r => r.TenantId == w.S.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Current_term_window_and_term_lock_are_respected()
    {
        var w = await WorldAsync("Subject Register Lock");
        var date = SchoolDayBack(0);

        Assert.Equal(HttpStatusCode.BadRequest, (await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, Today.AddDays(1), 1), Marks(), Ct)).StatusCode);
        var old = SchoolDayBack(9);
        Assert.Equal("You can't change registers from that date.",
            await ProblemAsync(await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, old, 1), Marks(), Ct), HttpStatusCode.Conflict));
        // Leadership may take any date of the current term.
        await Json(await w.S.School.Owner.PutAsJsonAsync(RegisterUrl(w.S.MathId, old, 1), Marks((w.Ada.EnrolmentId, "Absent")), Ct));

        await LockAsync(w.S);
        var locked = await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, date, 1), Marks((w.Ada.EnrolmentId, "Absent")), Ct);
        Assert.Equal("This term is locked for the class, so registers can't change. Ask the principal to unlock it.", await ProblemAsync(locked, HttpStatusCode.Conflict));
        var stillReadable = await Json(await w.MathTeacher.GetAsync(RegisterUrl(w.S.MathId, date, 1), Ct));
        Assert.True(stillReadable.GetProperty("locked").GetBoolean());
        Assert.Equal(1, await api.WithDbAsync(db => db.SubjectRegisters.IgnoreQueryFilters().CountAsync(r => r.TenantId == w.S.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Reports_count_per_student_per_subject_for_staff_and_portals()
    {
        var w = await WorldAsync("Subject Register Reports");
        var today = SchoolDayBack(0);
        var before = SchoolDayBack(1);
        await Json(await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, today, 1), Marks((w.Ada.EnrolmentId, "Absent"), (w.Tolu.EnrolmentId, "Present")), Ct));
        await Json(await w.MathTeacher.PutAsJsonAsync(RegisterUrl(w.S.MathId, before, 1), Marks((w.Ada.EnrolmentId, "Late"), (w.Tolu.EnrolmentId, "Present")), Ct));
        await Json(await w.EnglishTeacher.PutAsJsonAsync(RegisterUrl(w.S.EnglishId, today, 2), Marks((w.Ada.EnrolmentId, "Present"), (w.Tolu.EnrolmentId, "Absent")), Ct));

        // One class subject's term: the subject teacher and leadership; the English teacher is refused Mathematics.
        var report = await Json(await w.MathTeacher.GetAsync($"/api/school/subject-attendance/report?classSubjectId={w.S.MathId}&termId={w.S.TermId}", Ct));
        Assert.Equal(2, report.GetProperty("lessons").GetInt32());
        var ada = report.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == w.Ada.EnrolmentId);
        Assert.Equal(0, ada.GetProperty("present").GetInt32());
        Assert.Equal(1, ada.GetProperty("absent").GetInt32());
        Assert.Equal(1, ada.GetProperty("late").GetInt32());
        Assert.Equal(50.0, ada.GetProperty("percent").GetDouble());
        var tolu = report.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("enrolmentId").GetGuid() == w.Tolu.EnrolmentId);
        Assert.Equal(2, tolu.GetProperty("present").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.EnglishTeacher.GetAsync($"/api/school/subject-attendance/report?classSubjectId={w.S.MathId}&termId={w.S.TermId}", Ct)).StatusCode);
        await Json(await w.S.School.Owner.GetAsync($"/api/school/subject-attendance/report?classSubjectId={w.S.MathId}&termId={w.S.TermId}", Ct));

        // One student, every subject: leadership and the teachers of the class.
        var student = await Json(await w.EnglishTeacher.GetAsync($"/api/school/students/{w.Ada.StudentId}/subject-attendance?termId={w.S.TermId}", Ct));
        var subjects = student.GetProperty("subjects").EnumerateArray().ToDictionary(x => x.GetProperty("subject").GetString()!);
        Assert.Equal(2, subjects["Mathematics"].GetProperty("lessons").GetInt32());
        Assert.Equal(1, subjects["Mathematics"].GetProperty("absent").GetInt32());
        Assert.Equal(1, subjects["English"].GetProperty("present").GetInt32());

        // The parent and the student see the child's own figures only.
        var parentView = await Json(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Ada.StudentId}/subject-attendance", Ct));
        Assert.True(parentView.GetProperty("enabled").GetBoolean());
        Assert.Equal(w.S.TermId, parentView.GetProperty("termId").GetGuid());
        var parentMath = parentView.GetProperty("subjects").EnumerateArray().Single(x => x.GetProperty("subject").GetString() == "Mathematics");
        Assert.Equal(1, parentMath.GetProperty("late").GetInt32());
        Assert.DoesNotContain("Tolu", parentView.GetRawText());
        await ProblemAsync(await w.Ada.Parent.GetAsync($"/api/parent/children/{w.Tolu.StudentId}/subject-attendance", Ct), HttpStatusCode.NotFound);

        var login = await portal.Kit.StudentLoginAsync(w.S.School, w.Tolu.StudentId);
        var own = await Json(await login.GetAsync("/api/student/subject-attendance", Ct));
        var toluEnglish = own.GetProperty("subjects").EnumerateArray().Single(x => x.GetProperty("subject").GetString() == "English");
        Assert.Equal(1, toluEnglish.GetProperty("absent").GetInt32());
        Assert.DoesNotContain("Ada", own.GetRawText());
    }

    [Fact]
    public async Task Another_schools_lesson_and_child_are_unknown()
    {
        var a = await WorldAsync("Subject Register Tenant A");
        var b = await WorldAsync("Subject Register Tenant B");
        var date = SchoolDayBack(0);
        await Json(await a.MathTeacher.PutAsJsonAsync(RegisterUrl(a.S.MathId, date, 1), Marks((a.Ada.EnrolmentId, "Absent")), Ct));

        Assert.Equal("Class subject was not found.", await ProblemAsync(await b.S.School.Owner.GetAsync(RegisterUrl(a.S.MathId, date, 1), Ct), HttpStatusCode.NotFound));
        await ProblemAsync(await b.S.School.Owner.PutAsJsonAsync(RegisterUrl(a.S.MathId, date, 1), Marks((a.Ada.EnrolmentId, "Present")), Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await b.S.School.Owner.GetAsync($"/api/school/subject-attendance/report?classSubjectId={a.S.MathId}&termId={a.S.TermId}", Ct), HttpStatusCode.NotFound);
        await ProblemAsync(await b.S.School.Owner.GetAsync($"/api/school/students/{a.Ada.StudentId}/subject-attendance", Ct), HttpStatusCode.NotFound);
        // School B's register cannot carry school A's student.
        await ProblemAsync(await b.S.School.Owner.PutAsJsonAsync(RegisterUrl(b.S.MathId, date, 1), Marks((a.Ada.EnrolmentId, "Absent")), Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await b.Ada.Parent.GetAsync($"/api/parent/children/{a.Ada.StudentId}/subject-attendance", Ct), HttpStatusCode.NotFound);

        var mark = await api.WithDbAsync(db => db.SubjectAttendanceMarks.IgnoreQueryFilters().Where(m => m.EnrolmentId == a.Ada.EnrolmentId).Select(m => m.Status).SingleAsync(Ct));
        Assert.Equal(Saas.Domain.School.AttendanceStatus.Absent, mark);
    }
}
