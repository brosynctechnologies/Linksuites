using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Application.School.Imports;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>The shared import engine: generic template, preview and confirm routes, held batches bound to their user, limits and the roster helper.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportEngineTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string Expired = "This import has expired. Upload the file again.";
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

    private static MultipartFormDataContent Form(byte[] bytes, string fileName)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string Csv(params string[] lines) => string.Join("\r\n", new[] { Header }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, byte[] bytes, string fileName, string kind = "students") =>
        client.PostAsync($"/api/school/imports/{kind}/preview", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> PreviewCsvAsync(HttpClient client, string csv) =>
        PreviewAsync(client, Encoding.UTF8.GetBytes(csv), "students.csv");

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private Task<List<Student>> StudentsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).OrderBy(s => s.AdmissionNo).ToListAsync(Ct));

    [Fact]
    public async Task Unknown_kind_is_404()
    {
        var school = await kit.SchoolAsync("Unknown Kind School");

        foreach (var response in new[]
        {
            await school.Owner.GetAsync("/api/school/imports/nothing/template.xlsx", Ct),
            await school.Owner.GetAsync("/api/school/imports/nothing/template.csv", Ct),
            await PreviewAsync(school.Owner, Encoding.UTF8.GetBytes(Csv("U-1,Ada,,Obi,F,,JSS 1A,,,,,,,,")), "students.csv", "nothing"),
        })
        {
            var problem = await ProblemAsync(response, HttpStatusCode.NotFound);
            Assert.Equal("Import was not found.", problem.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Other_users_token_is_404()
    {
        var school = await kit.SchoolAsync("Other User Token School");
        await kit.ClassAsync(school);
        var (admin, _) = await kit.StaffAsync(school, MemberRole.Admin, "Vice principal");

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv("OU-1,Ada,,Obi,F,,JSS 1A,,,,,,,,")));
        var token = preview.GetProperty("token").GetString()!;
        Assert.Equal("students", preview.GetProperty("kind").GetString());

        var problem = await ProblemAsync(await ConfirmAsync(admin, token), HttpStatusCode.NotFound);
        Assert.Equal(Expired, problem.GetProperty("detail").GetString());
        Assert.Equal("import_expired", problem.GetProperty("code").GetString());
        Assert.Empty(await StudentsAsync(school.TenantId));

        // Another school cannot see it either; the user who uploaded it still can.
        var other = await kit.SchoolAsync("Other User Token School 2");
        Assert.Equal(Expired, (await ProblemAsync(await ConfirmAsync(other.Owner, token), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        var done = await Json(await ConfirmAsync(school.Owner, token));
        Assert.Equal(1, done.GetProperty("created").GetInt32());
        Assert.Equal(0, done.GetProperty("updated").GetInt32());

        var audit = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == school.TenantId && a.Action == "imports.students").SingleAsync(Ct));
        Assert.Equal("ImportBatch", audit.EntityType);
    }

    [Fact]
    public async Task Expired_token_is_404()
    {
        var school = await kit.SchoolAsync("Expired Token School");
        await kit.ClassAsync(school);

        var preview = await Json(await PreviewCsvAsync(school.Owner, Csv("EX-1,Ada,,Obi,F,,JSS 1A,,,,,,,,")));
        var token = preview.GetProperty("token").GetString()!;
        await api.WithDbAsync(async db =>
        {
            var batch = await db.ImportBatches.IgnoreQueryFilters().SingleAsync(b => b.Token == token, Ct);
            Assert.Equal("students", batch.Kind);
            Assert.InRange((batch.ExpiresAt - DateTimeOffset.UtcNow).TotalMinutes, 28, 31);
            batch.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync(Ct);
        });

        var problem = await ProblemAsync(await ConfirmAsync(school.Owner, token), HttpStatusCode.NotFound);
        Assert.Equal(Expired, problem.GetProperty("detail").GetString());
        Assert.Equal(Expired, (await ProblemAsync(await ConfirmAsync(school.Owner, "no-such-token"), HttpStatusCode.NotFound)).GetProperty("detail").GetString());
        Assert.Empty(await StudentsAsync(school.TenantId));
    }

    [Fact]
    public async Task Formula_cells_stay_text()
    {
        var school = await kit.SchoolAsync("Engine Formula School");
        await kit.ClassAsync(school);

        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet("Students");
        var columns = Header.Split(',');
        for (var i = 0; i < columns.Length; i++) ws.Cell(1, i + 1).Value = columns[i];
        ws.Cell(2, 1).Value = "FX-1";
        ws.Cell(2, 2).FormulaA1 = "HYPERLINK(\"x\")";
        ws.Cell(2, 4).Value = "Obi";
        ws.Cell(2, 5).Value = "F";
        ws.Cell(2, 7).Value = "JSS 1A";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var preview = await Json(await PreviewAsync(school.Owner, stream.ToArray(), "students.xlsx"));
        var row = Assert.Single(Rows(preview));
        Assert.Equal("New", row.GetProperty("status").GetString());
        Assert.Contains("=HYPERLINK(\"x\")", row.GetProperty("label").GetString());
        await Json(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!));
        Assert.Equal("=HYPERLINK(\"x\")", Assert.Single(await StudentsAsync(school.TenantId)).FirstName);
    }

    [Fact]
    public async Task Two_thousand_one_rows_refused()
    {
        var school = await kit.SchoolAsync("Engine Limits School");
        await kit.ClassAsync(school);

        var many = Csv(Enumerable.Range(1, 2001).Select(i => $"L-{i},Pupil,,Row,F,,JSS 1A,,,,,,,,").ToArray());
        var problem = await ProblemAsync(await PreviewCsvAsync(school.Owner, many), HttpStatusCode.BadRequest);
        Assert.Equal("The file has more than 2,000 rows. Import at most 2,000 rows at a time and upload the rest in another file.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(0, await api.WithDbAsync(db => db.ImportBatches.IgnoreQueryFilters().CountAsync(b => b.TenantId == school.TenantId, Ct)));
    }

    [Fact]
    public async Task Missing_column_is_named()
    {
        var school = await kit.SchoolAsync("Engine Columns School");
        await kit.ClassAsync(school);

        var csv = "admission_no,first_name,middle_name,gender,class\r\nMC-1,Ada,,F,JSS 1A\r\n";
        var problem = await ProblemAsync(await PreviewCsvAsync(school.Owner, csv), HttpStatusCode.BadRequest);
        Assert.Equal("The file is missing the column last_name. Download the template to see every column.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Leading_zero_admission_numbers_survive()
    {
        var school = await kit.SchoolAsync("Leading Zero School");
        await kit.ClassAsync(school);

        var template = await school.Owner.GetAsync("/api/school/imports/students/template.xlsx", Ct);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", template.Content.Headers.ContentType?.MediaType);
        using var book = new XLWorkbook(new MemoryStream(await template.Content.ReadAsByteArrayAsync(Ct)));
        var sheet = book.Worksheet(1);
        Assert.Equal("Students", sheet.Name);
        var headings = Header.Split(',');
        for (var i = 0; i < headings.Length; i++)
        {
            Assert.Equal(headings[i], sheet.Cell(1, i + 1).GetString());
            Assert.True(sheet.Cell(1, i + 1).Style.Font.Bold);
            Assert.Equal("@", sheet.Column(i + 1).Style.NumberFormat.Format);
        }
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.True(book.TryGetWorksheet("How to fill in", out _));

        // Typed into the text-formatted template, 007 stays 007.
        sheet.Cell(2, 1).Value = "007";
        sheet.Cell(2, 2).Value = "Ada";
        sheet.Cell(2, 4).Value = "Obi";
        sheet.Cell(2, 5).Value = "F";
        sheet.Cell(2, 7).Value = "JSS 1A";
        sheet.Cell(2, 9).Value = "08031234567";
        sheet.Cell(2, 8).Value = "Mrs Obi";
        using var filled = new MemoryStream();
        book.SaveAs(filled);

        var preview = await Json(await PreviewAsync(school.Owner, filled.ToArray(), "students.xlsx"));
        Assert.Contains("007", Assert.Single(Rows(preview)).GetProperty("label").GetString());
        await Json(await ConfirmAsync(school.Owner, preview.GetProperty("token").GetString()!));
        Assert.Equal("007", Assert.Single(await StudentsAsync(school.TenantId)).AdmissionNo);

        var csv = await school.Owner.GetAsync("/api/school/imports/students/template.csv", Ct);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var bytes = await csv.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal(Header + "\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Duplicate_student_rows_are_errors()
    {
        var roster = new Dictionary<string, Guid> { ["A-1"] = Guid.NewGuid(), ["A-2"] = Guid.NewGuid(), ["A-3"] = Guid.NewGuid() };
        ImportRowData Row(int n, string admissionNo) => new(n, new Dictionary<string, string> { ["admission_no"] = admissionNo });
        var rows = new[] { Row(2, "A-1"), Row(3, "a-1"), Row(4, "A-2"), Row(5, "Z-9") };

        var matched = RosterRows.Match(roster, rows);

        Assert.Equal(4, matched.Count);
        Assert.Equal("This student appears more than once in the file.", matched[0].Error);
        Assert.Equal("This student appears more than once in the file.", matched[1].Error);
        Assert.Null(matched[2].Error);
        Assert.Equal(roster["A-2"], matched[2].Enrolment);
        Assert.Equal("This student is not in this class.", matched[3].Error);
        Assert.Equal([2, 3, 4, 5], matched.Select(m => m.Row.Row));
    }

    [Fact]
    public async Task Generic_and_old_student_routes_agree()
    {
        var school = await kit.SchoolAsync("Agree School");
        var classId = await kit.ClassAsync(school);
        await Json(await school.Owner.PostAsJsonAsync("/api/school/students", new { admissionNo = "AG-1", firstName = "Old", lastName = "Name", gender = "Female", classId }, Ct));
        var csv = Csv(
            "AG-1,Ada,,Obi,F,2012-03-04,JSS 1A,,,,,,,,",
            "AG-2,Bayo,Tunde,Eze,Male,,jss1a,,,,,,,,",
            ",,,,,,,,,,,,,,",
            "AG-3,Chi,,Okoro,F,,JSS 4B,,,,,,,,",
            "AG-2,Dayo,,Ade,M,,JSS 1A,,,,,,,,");

        var old = await Json(await school.Owner.PostAsync("/api/school/students/import/preview", Form(Encoding.UTF8.GetBytes(csv), "students.csv"), Ct));
        var generic = await Json(await PreviewCsvAsync(school.Owner, csv));

        var oldRows = Rows(old).Select(r => (r.GetProperty("row").GetInt32(), r.GetProperty("status").GetString())).ToList();
        var genericRows = Rows(generic).Select(r => (r.GetProperty("row").GetInt32(), r.GetProperty("status").GetString())).ToList();
        Assert.Equal(oldRows, genericRows);
        Assert.Equal(4, genericRows.Count);
        Assert.Equal(Rows(old).Select(r => r.GetProperty("message").GetString()), Rows(generic).Select(r => r.GetProperty("message").GetString()));

        var counts = generic.GetProperty("counts");
        Assert.Equal(old.GetProperty("newCount").GetInt32(), counts.GetProperty("new").GetInt32());
        Assert.Equal(old.GetProperty("updateCount").GetInt32(), counts.GetProperty("update").GetInt32());
        Assert.Equal(old.GetProperty("errorCount").GetInt32(), counts.GetProperty("error").GetInt32());
        Assert.Equal(0, counts.GetProperty("unchanged").GetInt32());
        Assert.Equal((0, 1, 3), (counts.GetProperty("new").GetInt32(), counts.GetProperty("update").GetInt32(), counts.GetProperty("error").GetInt32()));

        // Confirm refuses the whole file with the error rows, in the generic row shape.
        var problem = await ProblemAsync(await ConfirmAsync(school.Owner, generic.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal("Some rows have errors. Fix them in the file and upload it again; nothing was imported.", problem.GetProperty("detail").GetString());
        Assert.Equal([3, 5, 6], problem.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("row").GetInt32()));
        Assert.Equal("Old", Assert.Single(await StudentsAsync(school.TenantId)).FirstName);
    }

    [Fact]
    public async Task Generic_student_import_is_leadership_only()
    {
        var school = await kit.SchoolAsync("Engine Roles School");
        await kit.ClassAsync(school);
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var (bursar, _) = await kit.StaffAsync(school, MemberRole.Accountant, "Bursar");

        foreach (var client in new[] { teacher, bursar })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/school/imports/students/template.xlsx", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/school/imports/students/template.csv", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PreviewCsvAsync(client, Csv("RL-1,Ada,,Obi,F,,JSS 1A,,,,,,,,"))).StatusCode);
        }
        Assert.Empty(await StudentsAsync(school.TenantId));
    }
}
