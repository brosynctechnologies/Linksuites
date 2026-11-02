using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>
/// Attendance (kind <c>attendance</c>), a roster template for one class and a range of dates (at most 31 days, none after today): one column
/// per date with P, A, L or E, blank meaning "not taken". Each changed date is saved through the register's own save, so one register per class
/// and day, the latest marks win and an absence alert is never repeated. Only today's date messages parents; earlier dates send nothing.
/// Access follows the register: the bursar is refused first, teachers reach only their classes and the 7-day window.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportAttendanceTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";

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

    private static MultipartFormDataContent Form(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Scope(Guid classId, DateOnly from, DateOnly to) => $"?classId={classId}&from={D(from)}&to={D(to)}";

    /// <summary>
    /// A file with the heading line <paramref name="header"/> (sheet_for added last), the student lines and then the template's
    /// register_version row with a blank version (not taken) under every date. Every line ends in <paramref name="sheetFor"/>.
    /// </summary>
    private static string Csv(string header, string sheetFor, params string[] lines) =>
        CsvVersions(header, sheetFor, Enumerable.Repeat("", header.Split(',').Length - 2).ToArray(), lines);

    /// <summary>As <see cref="Csv"/>, with the register_version row carrying <paramref name="versions"/> (one per date column).</summary>
    private static string CsvVersions(string header, string sheetFor, string[] versions, params string[] lines) =>
        string.Join("\r\n", new[] { header + ",sheet_for" }
            .Concat(lines.Append($"register_version,,{string.Join(",", versions)}").Select(l => $"{l},\"{sheetFor}\""))) + "\r\n";

    /// <summary>The register's stored version (client id) for a date.</summary>
    private Task<string?> VersionAsync(Guid classId, DateOnly date) =>
        api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().Where(r => r.ClassId == classId && r.Date == date).Select(r => r.ClientId).FirstOrDefaultAsync(Ct));

    /// <summary>Saves a register on the screen as a phone would: the stored version as its base, the given marks, everyone else present.</summary>
    private async Task ScreenSaveAsync(HttpClient client, Guid classId, DateOnly date, string clientId, params (string Name, string Status, string? Note)[] marks)
    {
        var loaded = await RegisterAsync(client, classId, date);
        var ids = loaded.Register.GetProperty("marks").EnumerateArray().ToDictionary(m => m.GetProperty("studentName").GetString()!, m => m.GetProperty("enrolmentId").GetGuid());
        var baseClientId = loaded.Taken ? loaded.Register.GetProperty("clientId").GetString() ?? "" : "";
        (await client.PutAsJsonAsync(Url(classId, date), new
        {
            clientId, baseClientId, marks = marks.Select(m => new { enrolmentId = ids[m.Name], status = m.Status, note = m.Note }).ToArray(),
        }, Ct)).EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string scope, string csv) =>
        client.PostAsync($"/api/school/imports/attendance/preview{scope}", Form(Encoding.UTF8.GetBytes(csv), "attendance.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewFileAsync(HttpClient client, string scope, byte[] bytes, string fileName) =>
        client.PostAsync($"/api/school/imports/attendance/preview{scope}", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();
    private static string Token(JsonElement preview) => preview.GetProperty("token").GetString()!;

    private static void AssertCounts(JsonElement preview, int @new, int update, int unchanged, int error)
    {
        var c = preview.GetProperty("counts");
        Assert.Equal((@new, update, unchanged, error),
            (c.GetProperty("new").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("error").GetInt32()));
    }

    private static string Url(Guid classId, DateOnly date) => $"/api/school/attendance/{classId}/{D(date)}";

    /// <summary>Each student's (status, note) on the register for a date, by student name, and whether it was taken.</summary>
    private static async Task<(bool Taken, Dictionary<string, (string Status, string? Note)> Marks, JsonElement Register)> RegisterAsync(HttpClient client, Guid classId, DateOnly date)
    {
        var register = await Json(await client.GetAsync(Url(classId, date), Ct));
        var marks = register.GetProperty("marks").EnumerateArray().ToDictionary(m => m.GetProperty("studentName").GetString()!,
            m => (m.GetProperty("status").GetString()!, m.GetProperty("note").ValueKind == JsonValueKind.Null ? null : m.GetProperty("note").GetString()));
        return (register.GetProperty("taken").GetBoolean(), marks, register);
    }

    private sealed record Setup(SchoolKit.School School, Guid ClassId, HttpClient Teacher, Guid AdaId, string Ada, Guid ToluId, string Tolu, string Session);

    /// <summary>A JSS 1A class with Ada Zed and Tolu Obi and a class teacher; the current term covers today, 60 days either side.</summary>
    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct));
        var classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId }, Ct)).EnsureSuccessStatusCode();
        var adaId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var toluId = await kit.StudentAsync(school, classId, "Tolu", "Obi");
        var session = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId)
            .Join(db.AcademicSessions.IgnoreQueryFilters(), c => c.SessionId, s => s.Id, (c, s) => s.Name).FirstAsync(Ct));
        return new Setup(school, classId, teacher, adaId, await AdmissionNoAsync(adaId), toluId, await AdmissionNoAsync(toluId), session);
    }

    private async Task<string> AdmissionNoAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.Id == studentId).Select(x => x.AdmissionNo).FirstAsync(Ct));

    /// <summary>"2026/2027 / JSS 1A / 2026-10-19 to 2026-10-21".</summary>
    private static string SheetFor(Setup s, DateOnly from, DateOnly to) => $"{s.Session} / JSS 1A / {D(from)} to {D(to)}";

    private Task<int> RegistersAsync(Guid tenantId) => api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId, Ct));

    private Task<int> AbsentMailsAsync(string email) =>
        api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.absent" && n.Recipient == email, Ct));

    // ---------- Saving ----------

    [Fact]
    public async Task Attendance_import_saves_registers()
    {
        var s = await SetupAsync("Attendance Import School");
        var (d1, d2) = (Today.AddDays(-2), Today.AddDays(-1));
        var sheetFor = SheetFor(s, d1, d2);

        // A register already taken on d2 (Tolu late with a note), loaded by a phone that then went offline.
        var loaded = await RegisterAsync(s.Teacher, s.ClassId, d2);
        var enrolments = loaded.Register.GetProperty("marks").EnumerateArray().ToDictionary(m => m.GetProperty("studentName").GetString()!, m => m.GetProperty("enrolmentId").GetGuid());
        (await s.Teacher.PutAsJsonAsync(Url(s.ClassId, d2), new
        {
            clientId = "screen-1", baseClientId = "",
            marks = new[] { new { enrolmentId = enrolments["Tolu Obi"], status = "Late", note = (string?)"Bus" } },
        }, Ct)).EnsureSuccessStatusCode();

        // d2's version is the screen save's; d1 is not taken (blank).
        var preview = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d2),
            CsvVersions($"admission_no,student,{D(d1)},{D(d2)}", sheetFor, ["", "screen-1"], $"{s.Ada},Ada Zed,a,A", $"{s.Tolu},Tolu Obi,l,")));
        Assert.Equal("attendance", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal(2, rows.Count); // the register_version row is not a student
        Assert.Contains("Ada Zed", rows[0].GetProperty("label").GetString());
        Assert.Equal("Update", rows[0].GetProperty("status").GetString()); // d2 had Ada present: "A" changes a stored mark
        Assert.Equal("New", rows[1].GetProperty("status").GetString()); // Tolu's only change is on d1; blank d2 keeps Late
        Assert.Equal(1, await RegistersAsync(s.School.TenantId));

        var done = await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        Assert.Equal((1, 1), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));

        var day1 = await RegisterAsync(s.Teacher, s.ClassId, d1);
        Assert.True(day1.Taken);
        Assert.Equal("Absent", day1.Marks["Ada Zed"].Status);
        Assert.Equal("Late", day1.Marks["Tolu Obi"].Status);
        var day2 = await RegisterAsync(s.Teacher, s.ClassId, d2);
        Assert.Equal("Absent", day2.Marks["Ada Zed"].Status);
        // d2 was saved again for Ada; Tolu's cell was blank, so his stored mark and its note stay.
        Assert.Equal(("Late", "Bus"), day2.Marks["Tolu Obi"]);
        Assert.NotEqual("screen-1", await VersionAsync(s.ClassId, d2));
        Assert.Equal(2, await RegistersAsync(s.School.TenantId));

        // The template carries the stored marks as letters and sheet_for; uploaded unchanged, every row is Unchanged and nothing is written.
        var xlsx = await s.Teacher.GetAsync("/api/school/imports/attendance/template.xlsx" + Scope(s.ClassId, d1, d2), Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Attendance");
            Assert.Equal(["admission_no", "student", D(d1), D(d2), "sheet_for"], Enumerable.Range(1, 5).Select(c => sheet.Cell(1, c).GetString()).ToList());
            var adaRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.Ada);
            Assert.Equal(("A", "A", sheetFor), (sheet.Cell(adaRow, 3).GetString(), sheet.Cell(adaRow, 4).GetString(), sheet.Cell(adaRow, 5).GetString()));
            // After the students, the register_version row: each date's stored version.
            Assert.Equal("register_version", sheet.Cell(4, 1).GetString());
            Assert.Equal((await VersionAsync(s.ClassId, d1), await VersionAsync(s.ClassId, d2)), (sheet.Cell(4, 3).GetString(), sheet.Cell(4, 4).GetString()));
        }
        var registersBefore = await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().Where(r => r.TenantId == s.School.TenantId)
            .OrderBy(r => r.Date).Select(r => r.ClientId).ToListAsync(Ct));
        var roundTrip = await Json(await PreviewFileAsync(s.Teacher, Scope(s.ClassId, d1, d2), bytes, "attendance.xlsx"));
        AssertCounts(roundTrip, 0, 0, 2, 0);
        Assert.Equal(0, roundTrip.GetProperty("warnings").GetArrayLength());
        var csv = await (await s.Teacher.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, d1, d2), Ct)).Content.ReadAsByteArrayAsync(Ct);
        AssertCounts(await Json(await PreviewFileAsync(s.Teacher, Scope(s.ClassId, d1, d2), csv, "attendance.csv")), 0, 0, 2, 0);
        var r0 = await Json(await ConfirmAsync(s.Teacher, Token(roundTrip)));
        Assert.Equal((0, 0), (r0.GetProperty("created").GetInt32(), r0.GetProperty("updated").GetInt32()));
        Assert.Equal(registersBefore, await api.WithDbAsync(db => db.AttendanceRegisters.IgnoreQueryFilters().Where(r => r.TenantId == s.School.TenantId)
            .OrderBy(r => r.Date).Select(r => r.ClientId).ToListAsync(Ct)));

        // Importing a day again keeps one register for it and the latest marks win.
        var again = await Json(await PreviewAsync(s.School.Owner, Scope(s.ClassId, d1, d2),
            CsvVersions($"admission_no,student,{D(d1)},{D(d2)}", sheetFor, [(await VersionAsync(s.ClassId, d1))!, (await VersionAsync(s.ClassId, d2))!],
                $"{s.Ada},Ada Zed,E,", $"{s.Tolu},Tolu Obi,,")));
        AssertCounts(again, 0, 1, 1, 0);
        await Json(await ConfirmAsync(s.School.Owner, Token(again)));
        Assert.Equal(2, await RegistersAsync(s.School.TenantId));
        Assert.Equal("Excused", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Ada Zed"].Status);
        Assert.Equal("Late", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Tolu Obi"].Status);

        // A phone that opened d1 before anyone took it sends its old copy: refused, never silently over the import.
        var stale = await ProblemAsync(await s.Teacher.PutAsJsonAsync(Url(s.ClassId, d1), new
        {
            clientId = "phone-old", baseClientId = "",
            marks = new[] { new { enrolmentId = enrolments["Ada Zed"], status = "Present", note = (string?)null } },
        }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("register_changed", stale.GetProperty("code").GetString());
        Assert.Equal("Excused", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Ada Zed"].Status);
    }

    [Fact]
    public async Task Untaken_day_marks_blank_students_present_with_a_warning()
    {
        var s = await SetupAsync("Attendance Partial School");
        var d1 = Today.AddDays(-1);
        var preview = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1),
            Csv($"admission_no,student,{D(d1)}", SheetFor(s, d1, d1), $"{s.Ada},Ada Zed,A", $"{s.Tolu},Tolu Obi,")));
        AssertCounts(preview, 1, 0, 1, 0);
        var warning = Assert.Single(preview.GetProperty("warnings").EnumerateArray()).GetString();
        Assert.Equal($"No register was taken on {d1.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}. 1 student left blank that day will be marked present, as on the register.", warning);
        await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        var day = await RegisterAsync(s.Teacher, s.ClassId, d1);
        Assert.Equal(("Absent", "Present"), (day.Marks["Ada Zed"].Status, day.Marks["Tolu Obi"].Status));
    }

    [Fact]
    public async Task Other_letter_error()
    {
        var s = await SetupAsync("Attendance Letter School");
        var d1 = Today.AddDays(-1);
        var preview = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1),
            Csv($"admission_no,student,{D(d1)}", SheetFor(s, d1, d1), $"{s.Ada},Ada Zed,X", $"{s.Tolu},Tolu Obi,Present", "NOPE-1,Nobody,P")));
        var rows = Rows(preview);
        var expected = $"Mark {d1.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)} with P, A, L or E (present, absent, late or excused).";
        Assert.Equal(("Error", expected), (rows[0].GetProperty("status").GetString(), rows[0].GetProperty("message").GetString()));
        Assert.Equal(("Error", expected), (rows[1].GetProperty("status").GetString(), rows[1].GetProperty("message").GetString()));
        Assert.Equal("This student is not in this class.", rows[2].GetProperty("message").GetString());

        var refused = await ProblemAsync(await ConfirmAsync(s.Teacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Equal(0, await RegistersAsync(s.School.TenantId));

        // Another class's or another range's template is refused by its sheet_for.
        var other = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1),
            Csv($"admission_no,student,{D(d1)}", SheetFor(s, d1.AddDays(-1), d1), $"{s.Ada},Ada Zed,A")));
        Assert.Equal($"This sheet was made for {SheetFor(s, d1.AddDays(-1), d1)}. Download the template for this class and these dates.",
            Assert.Single(Rows(other)).GetProperty("message").GetString());
    }

    // ---------- Scope ----------

    [Fact]
    public async Task Range_over_31_days_400()
    {
        var s = await SetupAsync("Attendance Range School");
        var tooLong = await ProblemAsync(await s.School.Owner.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, Today.AddDays(-31), Today), Ct), HttpStatusCode.BadRequest);
        Assert.Equal("Choose at most 31 days.", tooLong.GetProperty("errors").GetProperty("to")[0].GetString());
        (await s.School.Owner.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, Today.AddDays(-30), Today), Ct)).EnsureSuccessStatusCode();

        var backwards = await ProblemAsync(await s.School.Owner.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, Today, Today.AddDays(-1)), Ct), HttpStatusCode.BadRequest);
        Assert.Equal("Choose a start date on or before the end date.", backwards.GetProperty("errors").GetProperty("from")[0].GetString());
        var missing = await ProblemAsync(await s.School.Owner.GetAsync($"/api/school/imports/attendance/template.csv?classId={s.ClassId}&to={D(Today)}", Ct), HttpStatusCode.BadRequest);
        Assert.True(missing.GetProperty("errors").TryGetProperty("from", out _), missing.ToString());

        // A file column for a date outside the chosen range is refused rather than skipped.
        var d1 = Today.AddDays(-1);
        var outside = await ProblemAsync(await PreviewAsync(s.School.Owner, Scope(s.ClassId, d1, d1),
            Csv($"admission_no,student,{D(d1)},{D(Today.AddDays(-5))}", SheetFor(s, d1, d1), $"{s.Ada},Ada Zed,A,P")), HttpStatusCode.BadRequest);
        Assert.Contains(Today.AddDays(-5).ToString("d MMMM yyyy", CultureInfo.InvariantCulture), outside.GetProperty("detail").GetString());
        Assert.Equal(0, await RegistersAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Future_date_400()
    {
        var s = await SetupAsync("Attendance Future School");
        var future = await ProblemAsync(await PreviewAsync(s.School.Owner, Scope(s.ClassId, Today, Today.AddDays(1)),
            Csv($"admission_no,student,{D(Today)},{D(Today.AddDays(1))}", SheetFor(s, Today, Today.AddDays(1)), $"{s.Ada},Ada Zed,A,A")), HttpStatusCode.BadRequest);
        Assert.Equal("Choose today or an earlier date.", future.GetProperty("errors").GetProperty("to")[0].GetString());
        Assert.Equal(0, await RegistersAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Access_follows_the_register()
    {
        var s = await SetupAsync("Attendance Access School");
        var other = await kit.ClassAsync(s.School, "B");

        // The bursar is refused before anything is looked up, even for a made-up class.
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        var refused = await ProblemAsync(await bursar.GetAsync("/api/school/imports/attendance/template.csv" + Scope(Guid.NewGuid(), Today, Today), Ct), HttpStatusCode.Forbidden);
        Assert.Equal("Attendance is for the school's leadership and teachers.", refused.GetProperty("detail").GetString());

        // A teacher reaches only their own classes, and only the register's 7-day window.
        var notMine = await ProblemAsync(await s.Teacher.GetAsync("/api/school/imports/attendance/template.csv" + Scope(other, Today, Today), Ct), HttpStatusCode.NotFound);
        Assert.Equal("Class was not found.", notMine.GetProperty("detail").GetString());
        (await s.Teacher.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, Today.AddDays(-7), Today), Ct)).EnsureSuccessStatusCode();
        var old = await ProblemAsync(await s.Teacher.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, Today.AddDays(-8), Today), Ct), HttpStatusCode.Conflict);
        Assert.Equal("You can't change registers from that date.", old.GetProperty("detail").GetString());

        // Leadership reaches any class and any date of the current term, but not before it starts.
        (await s.School.Owner.GetAsync("/api/school/imports/attendance/template.csv" + Scope(other, Today.AddDays(-20), Today.AddDays(-8)), Ct)).EnsureSuccessStatusCode();
        await ProblemAsync(await s.School.Owner.GetAsync("/api/school/imports/attendance/template.csv" + Scope(s.ClassId, Today.AddDays(-61), Today.AddDays(-50)), Ct), HttpStatusCode.Conflict);
    }

    // ---------- Messages ----------

    [Fact]
    public async Task Past_absence_sends_no_message()
    {
        var s = await SetupAsync("Attendance Past School");
        var email = $"mum-{Guid.NewGuid():N}@example.com";
        await kit.GuardianAsync(s.School, s.AdaId, "Ngozi Zed", "0803 123 4567", email);
        var d1 = Today.AddDays(-1);

        var preview = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1), Csv($"admission_no,student,{D(d1)}", SheetFor(s, d1, d1), $"{s.Ada},Ada Zed,A", $"{s.Tolu},Tolu Obi,P")));
        Assert.Equal(0, preview.GetProperty("warnings").GetArrayLength());
        await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        Assert.Equal("Absent", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Ada Zed"].Status);
        Assert.Equal(0, await AbsentMailsAsync(email));

        // The absence is recorded as handled: saving that day on the screen later with Ada still absent does not message the parent then.
        var register = await RegisterAsync(s.Teacher, s.ClassId, d1);
        var ada = register.Register.GetProperty("marks").EnumerateArray().Single(m => m.GetProperty("studentName").GetString() == "Ada Zed").GetProperty("enrolmentId").GetGuid();
        var screen = await Json(await s.Teacher.PutAsJsonAsync(Url(s.ClassId, d1), new
        {
            clientId = "screen-later", baseClientId = register.Register.GetProperty("clientId").GetString(),
            marks = new[] { new { enrolmentId = ada, status = "Absent", note = (string?)"Sick" } },
        }, Ct));
        Assert.Equal(0, screen.GetProperty("messageParents").GetArrayLength());
        Assert.Equal(0, await AbsentMailsAsync(email));
    }

    [Fact]
    public async Task Today_absence_messages_parent()
    {
        var s = await SetupAsync("Attendance Today School");
        var email = $"mum-{Guid.NewGuid():N}@example.com";
        await kit.GuardianAsync(s.School, s.AdaId, "Ngozi Zed", "0803 123 4567", email);
        var d1 = Today.AddDays(-1);
        var sheetFor = SheetFor(s, d1, Today);

        var preview = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, Today),
            Csv($"admission_no,student,{D(d1)},{D(Today)}", sheetFor, $"{s.Ada},Ada Zed,A,A", $"{s.Tolu},Tolu Obi,P,P")));
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "Parents of students marked absent today get the absence message, as on the register. Earlier dates send no message.");
        await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        var mail = Assert.Single(await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.absent" && n.Recipient == email).ToListAsync(Ct)));
        Assert.Equal(Today.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), mail.Payload["date"]);
        Assert.Equal(2, await api.WithDbAsync(db => db.AbsenceAlerts.IgnoreQueryFilters().CountAsync(a => a.TenantId == s.School.TenantId, Ct)));

        // Present then absent again today, by import: as on the register, the parent is not messaged twice.
        await Json(await ConfirmAsync(s.Teacher, Token(await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, Today, Today),
            CsvVersions($"admission_no,student,{D(Today)}", SheetFor(s, Today, Today), [(await VersionAsync(s.ClassId, Today))!], $"{s.Ada},Ada Zed,P"))))));
        Assert.Equal("Present", (await RegisterAsync(s.Teacher, s.ClassId, Today)).Marks["Ada Zed"].Status);
        await Json(await ConfirmAsync(s.Teacher, Token(await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, Today, Today),
            CsvVersions($"admission_no,student,{D(Today)}", SheetFor(s, Today, Today), [(await VersionAsync(s.ClassId, Today))!], $"{s.Ada},Ada Zed,a"))))));
        Assert.Equal("Absent", (await RegisterAsync(s.Teacher, s.ClassId, Today)).Marks["Ada Zed"].Status);
        Assert.Equal(1, await AbsentMailsAsync(email));
    }

    // ---------- Excel dates ----------

    [Fact]
    public async Task Excel_dates_in_headers_parse()
    {
        var s = await SetupAsync("Attendance Excel School");
        var (d1, d2, d3) = (Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1));
        var sheetFor = SheetFor(s, d1, d3);

        // Headings retyped in Excel: a real date cell, a date serial number with a date format, and day/month/year text.
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Attendance");
            sheet.Cell(1, 1).Value = "admission_no";
            sheet.Cell(1, 2).Value = "student";
            sheet.Cell(1, 3).Value = d1.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(1, 3).Style.NumberFormat.Format = "dd/mm/yyyy";
            sheet.Cell(1, 4).Value = d2.ToDateTime(TimeOnly.MinValue).ToOADate();
            sheet.Cell(1, 4).Style.NumberFormat.Format = "d mmm yyyy";
            sheet.Cell(1, 5).Value = d3.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            sheet.Cell(1, 6).Value = "sheet_for";
            sheet.Cell(2, 1).Value = s.Ada;
            sheet.Cell(2, 2).Value = "Ada Zed";
            sheet.Cell(2, 3).Value = "A";
            sheet.Cell(2, 4).Value = "L";
            sheet.Cell(2, 5).Value = "E";
            sheet.Cell(2, 6).Value = sheetFor;
            sheet.Cell(3, 1).Value = "register_version"; // no day taken yet: blank versions
            sheet.Cell(3, 6).Value = sheetFor;
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            bytes = stream.ToArray();
        }

        var preview = await Json(await PreviewFileAsync(s.Teacher, Scope(s.ClassId, d1, d3), bytes, "attendance.xlsx"));
        AssertCounts(preview, 1, 0, 0, 0);
        Assert.Equal(0, preview.GetProperty("warnings").EnumerateArray().Count(w => w.GetString()!.Contains("ignored")));
        await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        Assert.Equal("Absent", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Ada Zed"].Status);
        Assert.Equal("Late", (await RegisterAsync(s.Teacher, s.ClassId, d2)).Marks["Ada Zed"].Status);
        Assert.Equal("Excused", (await RegisterAsync(s.Teacher, s.ClassId, d3)).Marks["Ada Zed"].Status);
        Assert.Equal(3, await RegistersAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Plain_number_serial_heading_parses_and_other_numbers_are_ignored()
    {
        var s = await SetupAsync("Attendance Serial School");
        var d1 = Today.AddDays(-1);
        var serial = d1.ToDateTime(TimeOnly.MinValue).ToOADate().ToString("F0", CultureInfo.InvariantCulture);

        // A General-format cell holding the date's serial reads as that day; "2.5" is not a date and is ignored with a warning.
        var preview = await Json(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1),
            Csv($"admission_no,student,{serial},2.5", SheetFor(s, d1, d1), $"{s.Ada},Ada Zed,A,x")));
        AssertCounts(preview, 1, 0, 0, 0);
        Assert.Contains(preview.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "This column was ignored: 2.5.");
        await Json(await ConfirmAsync(s.Teacher, Token(preview)));
        Assert.Equal("Absent", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Ada Zed"].Status);
    }

    [Fact]
    public async Task Two_columns_with_one_heading_400()
    {
        var s = await SetupAsync("Attendance Duplicate Heading School");
        var d1 = Today.AddDays(-1);
        var refused = await ProblemAsync(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1),
            Csv($"admission_no,student,{D(d1)},{D(d1)}", SheetFor(s, d1, d1), $"{s.Ada},Ada Zed,A,P")), HttpStatusCode.BadRequest);
        Assert.Equal($"The file has two columns called {D(d1)}. Keep one and upload it again.", refused.GetProperty("detail").GetString());

        // The shared reader does the same for a workbook, whatever the kind: headings that read as one key ("Student" and "student").
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Traits");
            sheet.Cell(1, 1).Value = "admission_no";
            sheet.Cell(1, 2).Value = "student";
            sheet.Cell(1, 3).Value = "Student";
            sheet.Cell(2, 1).Value = s.Ada;
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            bytes = stream.ToArray();
        }
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == s.School.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var traits = await ProblemAsync(await s.Teacher.PostAsync($"/api/school/imports/traits/preview?classId={s.ClassId}&termId={termId}&kind=Psychomotor",
            Form(bytes, "traits.xlsx"), Ct), HttpStatusCode.BadRequest);
        Assert.Equal("The file has two columns called Student. Keep one and upload it again.", traits.GetProperty("detail").GetString());
        Assert.Equal(0, await RegistersAsync(s.School.TenantId));
    }

    // ---------- Register versions ----------

    private static string StaleMessage(DateOnly date) =>
        $"{date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}: {AttendanceService.RegisterChanged} Download the template again.";

    /// <summary>The real .xlsx template with one cell changed: <paramref name="admissionNo"/>'s mark in date column <paramref name="column"/> (3 = first date).</summary>
    private async Task<byte[]> TemplateWithAsync(Setup s, DateOnly from, DateOnly to, string admissionNo, int column, string value)
    {
        var bytes = await (await s.Teacher.GetAsync("/api/school/imports/attendance/template.xlsx" + Scope(s.ClassId, from, to), Ct)).Content.ReadAsByteArrayAsync(Ct);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Attendance");
        var row = sheet.RowsUsed().Single(r => r.Cell(1).GetString() == admissionNo).RowNumber();
        sheet.Cell(row, column).Value = value;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task Stale_file_after_screen_save_is_refused()
    {
        var s = await SetupAsync("Attendance Stale File School");
        var (d1, d2) = (Today.AddDays(-2), Today.AddDays(-1));
        await ScreenSaveAsync(s.Teacher, s.ClassId, d1, "screen-1", ("Ada Zed", "Late", null));
        var file = await TemplateWithAsync(s, d1, d2, s.Ada, 4, "A"); // Ada absent on d2

        // Someone saves d1 on the screen after the file was made.
        await ScreenSaveAsync(s.Teacher, s.ClassId, d1, "screen-2", ("Ada Zed", "Excused", null));

        var preview = await Json(await PreviewFileAsync(s.Teacher, Scope(s.ClassId, d1, d2), file, "attendance.xlsx"));
        AssertCounts(preview, 0, 0, 0, 2);
        Assert.All(Rows(preview), r => Assert.Equal(StaleMessage(d1), r.GetProperty("message").GetString()));
        var refused = await ProblemAsync(await ConfirmAsync(s.Teacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, refused.GetProperty("detail").GetString());
        Assert.Equal("Excused", (await RegisterAsync(s.Teacher, s.ClassId, d1)).Marks["Ada Zed"].Status);
        Assert.Equal("screen-2", await VersionAsync(s.ClassId, d1));
        Assert.Equal(1, await RegistersAsync(s.School.TenantId)); // d2 not written
    }

    [Fact]
    public async Task Change_between_preview_and_confirm_409()
    {
        var s = await SetupAsync("Attendance Stale Preview School");
        var d1 = Today.AddDays(-1);
        await ScreenSaveAsync(s.Teacher, s.ClassId, d1, "screen-1");
        var file = await TemplateWithAsync(s, d1, d1, s.Ada, 3, "A");
        var preview = await Json(await PreviewFileAsync(s.Teacher, Scope(s.ClassId, d1, d1), file, "attendance.xlsx"));
        AssertCounts(preview, 0, 1, 1, 0);

        await ScreenSaveAsync(s.Teacher, s.ClassId, d1, "screen-2", ("Tolu Obi", "Late", null));

        var refused = await ProblemAsync(await ConfirmAsync(s.Teacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", refused.GetProperty("code").GetString());
        var day = await RegisterAsync(s.Teacher, s.ClassId, d1);
        Assert.Equal(("Present", "Late"), (day.Marks["Ada Zed"].Status, day.Marks["Tolu Obi"].Status));
        Assert.Equal("screen-2", await VersionAsync(s.ClassId, d1));
    }

    [Fact]
    public async Task Missing_register_version_row_400()
    {
        var s = await SetupAsync("Attendance No Version School");
        var d1 = Today.AddDays(-1);
        var sheetFor = SheetFor(s, d1, d1);
        var csv = $"admission_no,student,{D(d1)},sheet_for\r\n{s.Ada},Ada Zed,A,\"{sheetFor}\"\r\n";
        var refused = await ProblemAsync(await PreviewAsync(s.Teacher, Scope(s.ClassId, d1, d1), csv), HttpStatusCode.BadRequest);
        Assert.Equal("Download the template again; this file is missing its register_version row.", refused.GetProperty("detail").GetString());
        Assert.Equal(0, await RegistersAsync(s.School.TenantId));
    }
}
