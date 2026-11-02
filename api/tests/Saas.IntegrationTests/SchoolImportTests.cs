using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink student import: Excel or CSV upload, a held preview with New/Update/Error rows, and one confirm that saves everything or nothing.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string Header =
        "admission_no,first_name,middle_name,last_name,gender,date_of_birth,class,guardian1_name,guardian1_phone,guardian1_email,guardian1_relationship,guardian2_name,guardian2_phone,guardian2_email,guardian2_relationship";

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

    private static MultipartFormDataContent Form(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string Csv(params string[] lines) => string.Join("\r\n", new[] { Header }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewCsvAsync(HttpClient client, string csv) =>
        client.PostAsync("/api/school/students/import/preview", Form(Encoding.UTF8.GetBytes(csv), "students.csv"), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/students/import/{token}/confirm", null, Ct);

    private static byte[] Xlsx(Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Students");
        var columns = Header.Split(',');
        for (var i = 0; i < columns.Length; i++) ws.Cell(1, i + 1).Value = columns[i];
        fill(ws);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private Task<List<Student>> StudentsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).OrderBy(s => s.AdmissionNo).ToListAsync(Ct));

    [Fact]
    public async Task Preview_shows_new_update_and_error_rows_and_saves_nothing()
    {
        var school = await kit.SchoolAsync("Preview School");
        var classId = await ClassAsync(school.Owner, "A");
        await Json(await school.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = "ADM-1", firstName = "Old", lastName = "Name", gender = "Female", classId }, Ct));

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "ADM-1,Ada,,Obi,F,2012-03-04,JSS 1A,,,,,,,,",
            "ADM-2,Bayo,Tunde,Eze,Male,,jss1a,,,,,,,,",
            ",,,,,,,,,,,,,,",
            "ADM-3,Chi,,Okoro,F,,JSS 4B,,,,,,,,")));

        Assert.False(string.IsNullOrWhiteSpace(preview.GetProperty("token").GetString()));
        Assert.Equal(1, preview.GetProperty("newCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("updateCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("errorCount").GetInt32());
        Assert.True(preview.GetProperty("fitsPlan").GetBoolean());

        var rows = Rows(preview);
        Assert.Equal(3, rows.Count); // the blank line is skipped
        Assert.Equal("Update", rows[0].GetProperty("status").GetString());
        Assert.Equal(2, rows[0].GetProperty("row").GetInt32());
        Assert.Equal("New", rows[1].GetProperty("status").GetString());
        Assert.Equal("Bayo Tunde Eze", rows[1].GetProperty("name").GetString());
        Assert.Equal("JSS 1A", rows[1].GetProperty("className").GetString());
        Assert.Equal("Error", rows[2].GetProperty("status").GetString());
        Assert.Equal(5, rows[2].GetProperty("row").GetInt32());
        Assert.Contains("Row 5", rows[2].GetProperty("message").GetString());
        Assert.Contains("JSS 4B", rows[2].GetProperty("message").GetString());

        // Nothing was saved: the existing student is unchanged and no new student exists.
        var students = await StudentsAsync(school.TenantId);
        Assert.Single(students);
        Assert.Equal("Old", students[0].FirstName);
    }

    [Fact]
    public async Task Confirm_imports_all_rows_in_one_transaction()
    {
        var school = await kit.SchoolAsync("Confirm School");
        var classId = await ClassAsync(school.Owner, "A");

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "C-1,Ada,,Obi,F,,JSS 1A,Mrs Obi,08031234567,obi@example.com,Mother,,,,",
            "C-2,Bayo,,Eze,Male,,JSS 1A,,,,,,,,",
            "C-3,Chi,,Okoro,female,,JSS 1A,,,,,,,,")));
        var token = preview.GetProperty("token").GetString()!;

        var done = await Json(await ConfirmAsync(school.Owner, token));
        Assert.Equal(3, done.GetProperty("created").GetInt32());
        Assert.Equal(0, done.GetProperty("updated").GetInt32());

        var audit = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == school.TenantId && a.Action == "students.import").SingleAsync(Ct));
        Assert.Equal("ImportBatch", audit.EntityType);
        Assert.NotNull(audit.ActorId);
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal(3, after.RootElement.GetProperty("created").GetInt32());
        Assert.Equal(0, after.RootElement.GetProperty("updated").GetInt32());
        Assert.Equal("students.csv", after.RootElement.GetProperty("fileName").GetString());

        var students = await StudentsAsync(school.TenantId);
        Assert.Equal(3, students.Count);
        var enrolments = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId).ToListAsync(Ct));
        Assert.Equal(3, enrolments.Count);
        Assert.All(enrolments, e => Assert.Equal(classId, e.ClassId));
        var guardian = await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().SingleAsync(g => g.TenantId == school.TenantId, Ct));
        Assert.Equal("+2348031234567", guardian.Phone);

        // A batch is used once: a repeat confirm by the same user (a lost response) returns the first result and adds nobody again.
        var again = await Json(await ConfirmAsync(school.Owner, token));
        Assert.Equal((done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()),
            (again.GetProperty("created").GetInt32(), again.GetProperty("updated").GetInt32()));
        Assert.Equal(3, await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().CountAsync(s => s.TenantId == school.TenantId, Ct)));
    }

    [Fact]
    public async Task Confirm_checks_the_data_again_and_saves_nothing_when_a_row_has_gone_bad()
    {
        var school = await kit.SchoolAsync("Recheck School");
        var classId = await ClassAsync(school.Owner, "A");
        var spare = await ClassAsync(school.Owner, "B");

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "R-1,Ada,,Obi,F,,JSS 1A,,,,,,,,",
            "R-2,Bayo,,Eze,M,,JSS 1B,,,,,,,,")));
        Assert.Equal(0, preview.GetProperty("errorCount").GetInt32());

        // Someone adds R-1 by hand and removes class B before the confirm.
        await Json(await school.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = "R-1", firstName = "Manual", lastName = "Entry", gender = "Female", classId }, Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await school.Owner.DeleteAsync($"/api/school/classes/{spare}", Ct)).StatusCode);

        var problem = await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Single(problem.GetProperty("rows").EnumerateArray());
        Assert.Equal("Error", problem.GetProperty("rows")[0].GetProperty("status").GetString());

        var students = await StudentsAsync(school.TenantId);
        Assert.Single(students); // R-2 was not saved
        Assert.Equal("Manual", students[0].FirstName); // and R-1 was not overwritten
    }

    [Fact]
    public async Task Tokens_expire_and_belong_to_one_school_but_any_leader_may_confirm()
    {
        var school = await kit.SchoolAsync("Token School");
        await ClassAsync(school.Owner, "A");
        var (admin, _) = await kit.StaffAsync(school, MemberRole.Admin, "Vice principal");
        var other = await kit.SchoolAsync("Other Token School");

        // Another school cannot see the batch.
        var first = await Json(await PreviewCsvAsync(school.Owner, Csv("T-1,Ada,,Obi,F,,JSS 1A,,,,,,,,")));
        var foreign = await ProblemAsync(await ConfirmAsync(other.Owner, first.GetProperty("token").GetString()!), HttpStatusCode.NotFound);
        Assert.Equal("This import has expired. Upload the file again.", foreign.GetProperty("detail").GetString());
        await ProblemAsync(await ConfirmAsync(school.Owner, "no-such-token"), HttpStatusCode.NotFound);

        // Another admin of the same school can confirm.
        Assert.Equal(1, (await Json(await ConfirmAsync(admin, first.GetProperty("token").GetString()!))).GetProperty("created").GetInt32());

        // After 30 minutes the batch is gone.
        var second = await Json(await PreviewCsvAsync(school.Owner, Csv("T-2,Bayo,,Eze,M,,JSS 1A,,,,,,,,")));
        var token = second.GetProperty("token").GetString()!;
        await api.WithDbAsync(async db =>
        {
            var batch = await db.ImportBatches.IgnoreQueryFilters().SingleAsync(b => b.Token == token, Ct);
            Assert.InRange((batch.ExpiresAt - DateTimeOffset.UtcNow).TotalMinutes, 28, 31);
            batch.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync(Ct);
        });
        var expired = await ProblemAsync(await ConfirmAsync(school.Owner, token), HttpStatusCode.NotFound);
        Assert.Contains("expired", expired.GetProperty("detail").GetString());
        Assert.Single(await StudentsAsync(school.TenantId));
    }

    [Fact]
    public async Task Reimport_updates_by_admission_number()
    {
        var school = await kit.SchoolAsync("Reimport School");
        await ClassAsync(school.Owner, "A");
        await ClassAsync(school.Owner, "B");

        var firstPreview = await Json(await PreviewCsvAsync(school.Owner, Csv("RE-1,Ada,,Obi,F,,JSS 1A,,,,,,,,")));
        await Json(await ConfirmAsync(school.Owner, firstPreview.GetProperty("token").GetString()!));

        var second = await Json(await PreviewCsvAsync(school.Owner, Csv("re-1,Adaeze,,Obi-Eze,F,2012-03-04,JSS 1B,,,,,,,,")));
        Assert.Equal("Update", Rows(second)[0].GetProperty("status").GetString());
        var done = await Json(await ConfirmAsync(school.Owner, second.GetProperty("token").GetString()!));
        Assert.Equal(0, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());

        var student = Assert.Single(await StudentsAsync(school.TenantId));
        Assert.Equal("Adaeze", student.FirstName);
        Assert.Equal("Obi-Eze", student.LastName);
        Assert.Equal(new DateOnly(2012, 3, 4), student.DateOfBirth);
        var enrolment = await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().SingleAsync(e => e.StudentId == student.Id, Ct));
        var className = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == enrolment.ClassId).Select(c => c.Name).SingleAsync(Ct));
        Assert.Equal("JSS 1B", className);
    }

    [Fact]
    public async Task Siblings_share_a_guardian_matched_by_phone()
    {
        var school = await kit.SchoolAsync("Sibling School");
        await ClassAsync(school.Owner, "A");

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "S-1,Ada,,Obi,F,,JSS 1A,Mrs Obi,0803 123 4567,,Mother,,,,",
            "S-2,Bayo,,Obi,M,,JSS 1A,Mrs B. Obi,+2348031234567,obi@example.com,Mother,Mr Obi,08051112222,,Father")));
        Assert.Equal(0, preview.GetProperty("errorCount").GetInt32());
        await Json(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!));

        var guardians = await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().Where(g => g.TenantId == school.TenantId).OrderBy(g => g.CreatedAt).ToListAsync(Ct));
        Assert.Equal(2, guardians.Count); // Mrs Obi (shared) and Mr Obi
        var mother = guardians.Single(g => g.Phone == "+2348031234567");
        Assert.Equal("Mrs Obi", mother.FullName);
        Assert.Equal("obi@example.com", mother.Email); // the sibling's row added the email

        var links = await api.WithDbAsync(db => db.StudentGuardians.IgnoreQueryFilters().Where(l => l.TenantId == school.TenantId).ToListAsync(Ct));
        Assert.Equal(3, links.Count);
        Assert.Equal(2, links.Count(l => l.GuardianId == mother.Id));
        Assert.Equal(2, links.Count(l => l.IsPrimary)); // each student has one primary guardian
    }

    [Fact]
    public async Task Duplicate_admission_numbers_in_the_file_are_errors()
    {
        var school = await kit.SchoolAsync("Duplicate School");
        await ClassAsync(school.Owner, "A");

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "D-1,Ada,,Obi,F,,JSS 1A,,,,,,,,",
            "D-2,Bayo,,Eze,M,,JSS 1A,,,,,,,,",
            "d-1,Chi,,Okoro,F,,JSS 1A,,,,,,,,")));
        Assert.Equal(2, preview.GetProperty("errorCount").GetInt32());
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal("New", rows[1].GetProperty("status").GetString());
        Assert.Equal("Error", rows[2].GetProperty("status").GetString());
        Assert.Contains("rows 2 and 4", rows[2].GetProperty("message").GetString());

        var problem = await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal(2, problem.GetProperty("rows").GetArrayLength());
        Assert.Empty(await StudentsAsync(school.TenantId));
    }

    [Fact]
    public async Task Formula_cells_are_stored_as_text()
    {
        var school = await kit.SchoolAsync("Formula School");
        await ClassAsync(school.Owner, "A");

        var file = Xlsx(ws =>
        {
            ws.Cell(2, 1).Value = "F-1";
            ws.Cell(2, 2).Value = "=HYPERLINK(\"x\")"; // typed as text
            ws.Cell(2, 4).FormulaA1 = "HYPERLINK(\"y\")"; // a real formula cell
            ws.Cell(2, 5).Value = "F";
            ws.Cell(2, 7).Value = "JSS 1A";
        });
        var preview = await Json(await school.Owner.PostAsync("/api/school/students/import/preview", Form(file, "students.xlsx"), Ct));
        Assert.Equal(0, preview.GetProperty("errorCount").GetInt32());
        await Json(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!));

        var student = Assert.Single(await StudentsAsync(school.TenantId));
        Assert.Equal("=HYPERLINK(\"x\")", student.FirstName);
        Assert.Equal("=HYPERLINK(\"y\")", student.LastName);
    }

    [Fact]
    public async Task Dates_as_text_or_excel_serials_parse()
    {
        var school = await kit.SchoolAsync("Dates School");
        await ClassAsync(school.Owner, "A");

        var csv = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "DT-1,Ada,,Obi,F,2012-03-04,JSS 1A,,,,,,,,",
            "DT-2,Bayo,,Eze,M,04/03/2012,JSS 1A,,,,,,,,",
            "DT-3,Chi,,Okoro,F,40972,JSS 1A,,,,,,,,",
            "DT-4,Dayo,,Ade,M,31/02/2012,JSS 1A,,,,,,,,")));
        Assert.Equal(3, csv.GetProperty("newCount").GetInt32());
        Assert.Equal("Error", Rows(csv)[3].GetProperty("status").GetString());
        Assert.Contains("date of birth", Rows(csv)[3].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

        var xlsx = Xlsx(ws =>
        {
            ws.Cell(2, 1).Value = "DT-5"; ws.Cell(2, 2).Value = "Efe"; ws.Cell(2, 4).Value = "Bello"; ws.Cell(2, 5).Value = "M"; ws.Cell(2, 7).Value = "JSS 1A";
            ws.Cell(2, 6).Value = 40972; // an Excel serial number
            ws.Cell(3, 1).Value = "DT-6"; ws.Cell(3, 2).Value = "Femi"; ws.Cell(3, 4).Value = "Cole"; ws.Cell(3, 5).Value = "M"; ws.Cell(3, 7).Value = "JSS 1A";
            ws.Cell(3, 6).Value = new DateTime(2012, 3, 4); // a real date cell
        });
        var book = await Json(await school.Owner.PostAsync("/api/school/students/import/preview", Form(xlsx, "students.xlsx"), Ct));
        Assert.Equal(2, book.GetProperty("newCount").GetInt32());

        await Json(await ConfirmAsync(school.Owner, book.GetProperty("token").GetString()!));
        // The error row blocks its own file; the three good rows import once the bad one is dropped.
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(school.Owner, csv.GetProperty("token").GetString()!)).StatusCode);
        var clean = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "DT-1,Ada,,Obi,F,2012-03-04,JSS 1A,,,,,,,,", "DT-2,Bayo,,Eze,M,04/03/2012,JSS 1A,,,,,,,,", "DT-3,Chi,,Okoro,F,40972,JSS 1A,,,,,,,,")));
        await Json(await ConfirmAsync(school.Owner, clean.GetProperty("token").GetString()!));

        var students = await StudentsAsync(school.TenantId);
        Assert.Equal(5, students.Count);
        Assert.All(students, s => Assert.Equal(new DateOnly(2012, 3, 4), s.DateOfBirth));
    }

    [Fact]
    public async Task Over_the_plan_preview_says_how_many_fit_and_confirm_is_refused()
    {
        var school = await kit.SchoolAsync("Plan School"); // Free: 50 students
        await ClassAsync(school.Owner, "A");
        await api.WithDbAsync(async db =>
        {
            for (var i = 1; i <= 48; i++)
                db.Students.Add(new Student { TenantId = school.TenantId, AdmissionNo = $"P-{i:000}", FirstName = $"Pupil{i}", LastName = "Existing", AdmittedAt = new DateOnly(2026, 9, 1) });
            return await db.SaveChangesAsync(Ct);
        });

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "N-1,Ada,,Obi,F,,JSS 1A,,,,,,,,",
            "N-2,Bayo,,Eze,M,,JSS 1A,,,,,,,,",
            "N-3,Chi,,Okoro,F,,JSS 1A,,,,,,,,",
            "P-001,Pupil1,,Existing,F,,JSS 1A,,,,,,,,")));
        Assert.Equal(3, preview.GetProperty("newCount").GetInt32());
        Assert.False(preview.GetProperty("fitsPlan").GetBoolean());
        Assert.Equal(2, preview.GetProperty("room").GetInt32());

        var problem = await ProblemAsync(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Contains("2", problem.GetProperty("detail").GetString());
        Assert.Equal(48, (await StudentsAsync(school.TenantId)).Count);

        // Two new rows fit.
        var fits = await Json(await PreviewCsvAsync(school.Owner, Csv(
            "N-1,Ada,,Obi,F,,JSS 1A,,,,,,,,",
            "N-2,Bayo,,Eze,M,,JSS 1A,,,,,,,,",
            "P-001,Pupil1,,Renamed,F,,JSS 1A,,,,,,,,")));
        Assert.True(fits.GetProperty("fitsPlan").GetBoolean());
        var done = await Json(await ConfirmAsync(school.Owner, fits.GetProperty("token").GetString()!));
        Assert.Equal(2, done.GetProperty("created").GetInt32());
        Assert.Equal(1, done.GetProperty("updated").GetInt32());
    }

    [Fact]
    public async Task Files_over_2000_rows_or_5_MB_are_refused()
    {
        var school = await kit.SchoolAsync("Limits School");
        await ClassAsync(school.Owner, "A");

        var many = Csv(Enumerable.Range(1, 2001).Select(i => $"X-{i},Pupil,,Row,F,,JSS 1A,,,,,,,,").ToArray());
        var tooMany = await ProblemAsync(await PreviewCsvAsync(school.Owner, many), HttpStatusCode.BadRequest);
        Assert.Contains("2,000", tooMany.GetProperty("detail").GetString());

        var exactly = Csv(Enumerable.Range(1, 2000).Select(i => $"Y-{i},Pupil,,Row,F,,JSS 1A,,,,,,,,").ToArray());
        Assert.Equal(2000, (await Json(await PreviewCsvAsync(school.Owner, exactly))).GetProperty("newCount").GetInt32());

        var big = Csv("Z-1,Ada,,Obi,F,,JSS 1A," + new string('x', 5 * 1024 * 1024) + ",,,,,,,");
        var tooBig = await ProblemAsync(await PreviewCsvAsync(school.Owner, big), HttpStatusCode.BadRequest);
        Assert.Contains("5 MB", tooBig.GetProperty("detail").GetString());
    }


    [Fact]
    public async Task Readers_stop_at_the_row_cap_and_a_2500_row_csv_is_refused()
    {
        var csv = Csv(Enumerable.Range(1, 2500).Select(i => $"M-{i},Pupil,,Row,F,,JSS 1A,,,,,,,,").ToArray());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var rows = new Saas.Infrastructure.Schools.SpreadsheetReader().Read(stream, "big.csv", 2001);
        Assert.Equal(2001, rows.Count); // stopped, did not return all 2,500

        var school = await kit.SchoolAsync("Cap School");
        await ClassAsync(school.Owner, "A");
        var problem = await ProblemAsync(await PreviewCsvAsync(school.Owner, csv), HttpStatusCode.BadRequest);
        Assert.Contains("2,000", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_workbook_that_inflates_past_the_cap_is_refused()
    {
        var school = await kit.SchoolAsync("Bomb School");
        await ClassAsync(school.Owner, "A");

        var original = Xlsx(ws => { ws.Cell(2, 1).Value = "B-1"; ws.Cell(2, 2).Value = "Ada"; ws.Cell(2, 4).Value = "Obi"; ws.Cell(2, 5).Value = "F"; ws.Cell(2, 7).Value = "JSS 1A"; });
        using var output = new MemoryStream();
        using (var source = new System.IO.Compression.ZipArchive(new MemoryStream(original), System.IO.Compression.ZipArchiveMode.Read))
        using (var target = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.SmallestSize);
                using var input = entry.Open();
                using var sink = copy.Open();
                input.CopyTo(sink);
                if (entry.FullName == "xl/worksheets/sheet1.xml")
                {
                    // Trailing comment of 60 MB of spaces: tiny when compressed.
                    sink.Write(Encoding.UTF8.GetBytes("<!--"));
                    var chunk = new byte[1024 * 1024];
                    Array.Fill(chunk, (byte)' ');
                    for (var i = 0; i < 60; i++) sink.Write(chunk);
                    sink.Write(Encoding.UTF8.GetBytes("-->"));
                }
            }
        }
        Assert.True(output.Length < 5 * 1024 * 1024);
        var problem = await ProblemAsync(await school.Owner.PostAsync("/api/school/students/import/preview", Form(output.ToArray(), "students.xlsx"), Ct), HttpStatusCode.BadRequest);
        Assert.Contains("too large", problem.GetProperty("detail").GetString());
        Assert.Empty(await StudentsAsync(school.TenantId));
    }
    [Fact]
    public async Task Csv_with_quotes_commas_newlines_and_a_byte_order_mark_is_read()
    {
        var school = await kit.SchoolAsync("Csv School");
        await ClassAsync(school.Owner, "A");

        var text = "﻿" + Csv("Q-1,\"Ada, the \"\"first\"\"\",,Obi,F,,JSS 1A,\"Mrs Obi\nSenior\",,,,,,,");
        var preview = await Json(await PreviewCsvAsync(school.Owner, text));
        Assert.Equal(1, preview.GetProperty("newCount").GetInt32());
        await Json(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!));

        Assert.Equal("Ada, the \"first\"", Assert.Single(await StudentsAsync(school.TenantId)).FirstName);
        Assert.Equal("Mrs Obi\nSenior", (await api.WithDbAsync(db => db.Guardians.IgnoreQueryFilters().SingleAsync(g => g.TenantId == school.TenantId, Ct))).FullName);
    }

    [Fact]
    public async Task A_file_with_the_wrong_type_or_missing_columns_is_refused()
    {
        var school = await kit.SchoolAsync("Wrong File School");
        await ClassAsync(school.Owner, "A");

        await ProblemAsync(await school.Owner.PostAsync("/api/school/students/import/preview", Form([1, 2, 3], "students.pdf"), Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await school.Owner.PostAsync("/api/school/students/import/preview", Form([1, 2, 3, 4], "students.xlsx"), Ct), HttpStatusCode.BadRequest);
        var missing = await ProblemAsync(await school.Owner.PostAsync("/api/school/students/import/preview",
            Form(Encoding.UTF8.GetBytes("admission_no,first_name\r\nA-1,Ada\r\n"), "students.csv"), Ct), HttpStatusCode.BadRequest);
        Assert.Contains("last_name", missing.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Templates_download_for_leaders_and_import_is_leadership_only()
    {
        var school = await kit.SchoolAsync("Template School");
        await ClassAsync(school.Owner, "A");

        var csv = await school.Owner.GetAsync("/api/school/students/import/template.csv", Ct);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.StartsWith(Header, await csv.Content.ReadAsStringAsync(Ct).ContinueWith(t => t.Result.TrimStart('﻿'), Ct));

        var xlsx = await school.Owner.GetAsync("/api/school/students/import/template.xlsx", Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using (var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync(Ct)))
            Assert.Equal("admission_no", book.Worksheet(1).Cell(1, 1).GetString());

        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");
        foreach (var client in new[] { teacher, bursar })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/school/students/import/template.csv", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/school/students/import/template.xlsx", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PreviewCsvAsync(client, Csv("A-1,Ada,,Obi,F,,JSS 1A,,,,,,,,"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(client, "anything")).StatusCode);
        }
    }
}
