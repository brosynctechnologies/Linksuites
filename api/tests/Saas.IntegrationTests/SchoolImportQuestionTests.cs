using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School.Imports;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>
/// Question bank import (kind <c>questions</c>): a subject teacher or leadership loads questions from a file through the bank's create path. The
/// bursar is refused on template, preview and confirm. Also the two engine fixes made with it: CSV template cells are formula-safe, and the
/// reader takes the importer's own sheet.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportQuestionTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string Header = "subject,level,question,option_a,option_b,option_c,option_d,option_e,option_f,option_g,option_h,correct,marks";
    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string SubjectMissing = "This subject was not found.";

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

    private static string Csv(params string[] lines) => string.Join("\r\n", new[] { Header }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string csv) =>
        client.PostAsync("/api/school/imports/questions/preview", Form(Encoding.UTF8.GetBytes(csv), "questions.csv"), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private Task<List<BankQuestion>> BankAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.BankQuestions.IgnoreQueryFilters().Where(q => q.TenantId == tenantId).OrderBy(q => q.Stem).ToListAsync(Ct));

    private sealed record Setup(SchoolKit.School School, Guid LevelId, Guid MathsId, Guid EnglishId, HttpClient Teacher);

    /// <summary>JSS 1A with Mathematics (MTH, taught by the teacher) and English (ENG, taught by nobody).</summary>
    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var (teacher, teacherUserId) = await kit.StaffAsync(school, MemberRole.Staff, "Maths teacher");
        var staffId = await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters()
            .Where(p => p.UserId == teacherUserId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));
        var maths = (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct))).GetProperty("id").GetGuid();
        var english = (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "English", code = "ENG" }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = maths, teacherStaffId = staffId }, Ct));
        await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = english }, Ct));
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        return new Setup(school, levelId, maths, english, teacher);
    }

    [Fact]
    public async Task Valid_questions_import()
    {
        var s = await SetupAsync("Question Import School");

        var preview = await Json(await PreviewAsync(s.School.Owner, Csv(
            "Mathematics,,What is 2 + 2?,3,4,5,6,,,,,B,2",
            "mth,jss1,\"What is 10, take away 3?\",7,13,,,,,,,a,1.5",
            "english,JSS 1,Which word is a noun?,run,quickly,table,blue,green,,,,c,3")));
        Assert.Equal("questions", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("New", r.GetProperty("status").GetString()));
        Assert.Equal([2, 3, 4], rows.Select(r => r.GetProperty("row").GetInt32()));
        Assert.Equal("What is 2 + 2?", rows[0].GetProperty("label").GetString());
        var counts = preview.GetProperty("counts");
        Assert.Equal(3, counts.GetProperty("new").GetInt32());
        Assert.Equal(0, counts.GetProperty("error").GetInt32());
        Assert.Empty(await BankAsync(s.School.TenantId));

        var done = await Json(await ConfirmAsync(s.School.Owner, preview.GetProperty("token").GetString()!));
        Assert.Equal(3, done.GetProperty("created").GetInt32());
        Assert.Equal(0, done.GetProperty("updated").GetInt32());

        var bank = await BankAsync(s.School.TenantId);
        Assert.Equal(3, bank.Count);
        var plus = bank.Single(q => q.Stem == "What is 2 + 2?");
        Assert.Equal(s.MathsId, plus.SubjectId);
        Assert.Null(plus.LevelId);
        Assert.Equal(["3", "4", "5", "6"], plus.Options);
        Assert.Equal(1, plus.CorrectIndex);
        Assert.Equal(2m, plus.Marks);
        Assert.False(plus.Archived);
        var minus = bank.Single(q => q.Stem == "What is 10, take away 3?");
        Assert.Equal(s.MathsId, minus.SubjectId);
        Assert.Equal(s.LevelId, minus.LevelId);
        Assert.Equal(["7", "13"], minus.Options);
        Assert.Equal(0, minus.CorrectIndex);
        Assert.Equal(1.5m, minus.Marks);
        var noun = bank.Single(q => q.Stem == "Which word is a noun?");
        Assert.Equal(s.EnglishId, noun.SubjectId);
        Assert.Equal(s.LevelId, noun.LevelId);
        Assert.Equal(["run", "quickly", "table", "blue", "green"], noun.Options);
        Assert.Equal(2, noun.CorrectIndex);
        Assert.Equal(3m, noun.Marks);

        var audit = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == s.School.TenantId && a.Action == "imports.questions").SingleAsync(Ct));
        Assert.Equal("ImportBatch", audit.EntityType);
    }

    [Fact]
    public async Task Wrong_letter_is_an_error()
    {
        var s = await SetupAsync("Question Letter School");

        var preview = await Json(await PreviewAsync(s.School.Owner, Csv(
            "MTH,,What is 2 + 2?,3,4,5,6,,,,,E,2",
            "MTH,,What is 3 + 3?,5,6,7,8,,,,,B,2")));
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal("There is no option E for this question.", rows[0].GetProperty("message").GetString());
        Assert.Equal("New", rows[1].GetProperty("status").GetString());
        Assert.Equal(1, preview.GetProperty("counts").GetProperty("error").GetInt32());

        var problem = await ProblemAsync(await ConfirmAsync(s.School.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, problem.GetProperty("detail").GetString());
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        var error = Assert.Single(problem.GetProperty("rows").EnumerateArray());
        Assert.Equal(2, error.GetProperty("row").GetInt32());
        Assert.Empty(await BankAsync(s.School.TenantId));

        // Not a letter at all, and a gap between options, are errors too.
        var other = Rows(await Json(await PreviewAsync(s.School.Owner, Csv(
            "MTH,,What is 4 + 4?,7,8,,,,,,,1,2",
            "MTH,,What is 5 + 5?,9,,10,,,,,,A,2",
            "MTH,,What is 6 + 6?,12,,,,,,,,A,2",
            "MTH,,What is 7 + 7?,13,14,,,,,,,B,0"))));
        Assert.Equal("Enter the letter of the correct option, from A to H.", other[0].GetProperty("message").GetString());
        Assert.Equal("Fill in the options from option_a with no gaps.", other[1].GetProperty("message").GetString());
        Assert.Equal("Add at least two options.", other[2].GetProperty("message").GetString());
        Assert.Equal("The marks must be more than 0 and no more than 1000, with at most one decimal place.", other[3].GetProperty("message").GetString());
    }

    /// <summary>
    /// Review fix round 1: only the levels the question screen offers (numeric ones) can be named, and marks take a decimal point
    /// but no thousands separator, so "1,5" is an error row and never 15.
    /// </summary>
    [Fact]
    public async Task Developmental_level_and_comma_marks_are_errors()
    {
        var s = await SetupAsync("Question Level Marks School");
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/levels", new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));

        var preview = await Json(await PreviewAsync(s.School.Owner, Csv(
            "MTH,Nursery 1,How many legs has a cat?,2,4,,,,,,,B,1",
            "MTH,,What is 2 + 2?,3,4,,,,,,,B,\"1,5\"",
            "MTH,,What is 3 + 3?,5,6,,,,,,,B,1.5")));
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal("This level was not found.", rows[0].GetProperty("message").GetString());
        Assert.Equal("Error", rows[1].GetProperty("status").GetString());
        Assert.Equal("The marks must be more than 0 and no more than 1000, with at most one decimal place.", rows[1].GetProperty("message").GetString());
        Assert.Equal("New", rows[2].GetProperty("status").GetString());
        await ProblemAsync(await ConfirmAsync(s.School.Owner, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Empty(await BankAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Duplicate_options_error()
    {
        var s = await SetupAsync("Question Options School");

        var preview = await Json(await PreviewAsync(s.Teacher, Csv("MTH,,What is 2 + 2?,4,4,5,,,,,,A,2")));
        var row = Assert.Single(Rows(preview));
        Assert.Equal("Error", row.GetProperty("status").GetString());
        Assert.Equal("Each option must be different.", row.GetProperty("message").GetString());
        await ProblemAsync(await ConfirmAsync(s.Teacher, preview.GetProperty("token").GetString()!), HttpStatusCode.Conflict);
        Assert.Empty(await BankAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Same_question_twice_in_file_error()
    {
        var s = await SetupAsync("Question Twice School");

        var rows = Rows(await Json(await PreviewAsync(s.School.Owner, Csv(
            "MTH,,What is 2 + 2?,3,4,,,,,,,B,2",
            "Mathematics,,what is 2 + 2? ,4,5,,,,,,,A,1",
            "ENG,,What is 2 + 2?,3,4,,,,,,,B,2"))));
        Assert.Equal("This question appears more than once in the file.", rows[0].GetProperty("message").GetString());
        Assert.Equal("This question appears more than once in the file.", rows[1].GetProperty("message").GetString());
        // The same words for another subject are a different question.
        Assert.Equal("New", rows[2].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Teacher_other_subject_row_error()
    {
        var s = await SetupAsync("Question Teacher School");

        var preview = await Json(await PreviewAsync(s.Teacher, Csv(
            "MTH,,What is 2 + 2?,3,4,,,,,,,B,2",
            "English,,Which word is a noun?,run,table,,,,,,,B,2",
            "Physics,,What is a newton?,force,mass,,,,,,,A,2",
            "MTH,SS 9,What is 9 + 9?,18,19,,,,,,,A,2",
            "eng,,Pick the verb.,run,table,,,,,,,A,2")));
        var rows = Rows(preview);
        // The code of a subject the teacher does not teach is refused like its name.
        Assert.Equal("Error", rows[4].GetProperty("status").GetString());
        Assert.Equal(SubjectMissing, rows[4].GetProperty("message").GetString());
        Assert.Equal("New", rows[0].GetProperty("status").GetString());
        Assert.Equal("Error", rows[1].GetProperty("status").GetString());
        Assert.Equal(SubjectMissing, rows[1].GetProperty("message").GetString());
        Assert.Equal(SubjectMissing, rows[2].GetProperty("message").GetString());
        Assert.Equal("This level was not found.", rows[3].GetProperty("message").GetString());

        // Leadership reaches every subject; only the unknown ones stay errors.
        var owner = Rows(await Json(await PreviewAsync(s.School.Owner, Csv(
            "English,,Which word is a noun?,run,table,,,,,,,B,2", "Physics,,What is a newton?,force,mass,,,,,,,A,2"))));
        Assert.Equal("New", owner[0].GetProperty("status").GetString());
        Assert.Equal(SubjectMissing, owner[1].GetProperty("message").GetString());

        // A teacher's import of their own subject goes into the bank under their name.
        var own = await Json(await PreviewAsync(s.Teacher, Csv("MTH,,What is 2 + 2?,3,4,,,,,,,B,2")));
        await Json(await ConfirmAsync(s.Teacher, own.GetProperty("token").GetString()!));
        var saved = Assert.Single(await BankAsync(s.School.TenantId));
        Assert.Equal(s.MathsId, saved.SubjectId);
        Assert.NotEqual(await kit.OwnerIdAsync(s.School), saved.CreatedBy);
    }

    [Fact]
    public async Task Bursar_refused_before_lookup()
    {
        var s = await SetupAsync("Question Bursar School");
        var (bursar, bursarId) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");

        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/school/imports/questions/template.xlsx", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync("/api/school/imports/questions/template.csv", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(bursar, Csv("MTH,,What is 2 + 2?,3,4,,,,,,,B,2"))).StatusCode);
        // Refused before the file is even looked at: a form with no file is still 403, not "Choose a file to upload."
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync("/api/school/imports/questions/preview",
            new MultipartFormDataContent { { new StringContent("none"), "note" } }, Ct)).StatusCode);

        // A held batch of the bursar's own (as if the role changed after the preview) is refused on confirm, and nothing is saved.
        const string token = "bursar-question-batch";
        await api.WithDbAsync(db =>
        {
            db.ImportBatches.Add(new ImportBatch
            {
                TenantId = s.School.TenantId, Token = token, Kind = "questions", CreatedBy = bursarId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
                Payload = """{"fileName":"questions.csv","rows":[{"row":2,"cells":{"subject":"MTH","level":"","question":"What is 2 + 2?","option_a":"3","option_b":"4","option_c":"","option_d":"","option_e":"","option_f":"","option_g":"","option_h":"","correct":"B","marks":"2"}}]}""",
            });
            return db.SaveChangesAsync(Ct);
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(bursar, token)).StatusCode);
        Assert.Empty(await BankAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Question_templates_download()
    {
        var s = await SetupAsync("Question Template School");

        var csv = await s.Teacher.GetAsync("/api/school/imports/questions/template.csv", Ct);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)).TrimStart('﻿');
        Assert.Equal(Header + "\r\n", text);

        var xlsx = await s.Teacher.GetAsync("/api/school/imports/questions/template.xlsx", Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using var book = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal("Questions", book.Worksheet(1).Name);
        Assert.Equal(Header.Split(','), Enumerable.Range(1, 13).Select(i => book.Worksheet(1).Cell(1, i).GetString()));
    }

    // ---------- Engine fixes carried from Task 1 ----------

    /// <summary>Carry-over (a): every CSV template data cell goes through Csv.Cell, so a prefilled value starting with = + - or @ opens as text.</summary>
    [Fact]
    public void Csv_template_cells_are_formula_safe()
    {
        var columns = new List<ImportColumn> { new("admission_no", "admission_no", true, ""), new("student", "student", false, "") };
        var template = new ImportTemplate("Scores", columns,
            [["007", "=HYPERLINK(\"http://x\",\"Ada\")"], ["-12", "@Bayo, Eze"], ["+1", "Chi"]], []);

        var text = Encoding.UTF8.GetString(ImportTemplates.Csv(template));
        Assert.StartsWith("﻿", text);
        var lines = text.TrimStart('﻿').Split("\r\n");
        Assert.Equal("admission_no,student", lines[0]); // headings are the fixed column names, written plain as before
        Assert.Equal("\"007\",\"'=HYPERLINK(\"\"http://x\"\",\"\"Ada\"\")\"", lines[1]);
        Assert.Equal("\"'-12\",\"'@Bayo, Eze\"", lines[2]);
        Assert.Equal("\"'+1\",\"Chi\"", lines[3]);
    }

    /// <summary>Carry-over (b): an .xlsx is read from the importer's own sheet (Questions), not a sheet called Students or the first sheet.</summary>
    [Fact]
    public async Task Reader_prefers_the_importers_sheet()
    {
        var s = await SetupAsync("Question Sheet School");

        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("How to fill in").Cell(1, 1).Value = "Notes";
        var students = workbook.AddWorksheet("Students");
        students.Cell(1, 1).Value = "admission_no";
        students.Cell(2, 1).Value = "S-1";
        var sheet = workbook.AddWorksheet("Questions");
        var headings = Header.Split(',');
        for (var i = 0; i < headings.Length; i++) sheet.Cell(1, i + 1).Value = headings[i];
        string[] row = ["MTH", "", "What is 2 + 2?", "3", "4", "", "", "", "", "", "", "B", "2"];
        for (var i = 0; i < row.Length; i++) sheet.Cell(2, i + 1).Value = row[i];
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var preview = await Json(await s.School.Owner.PostAsync("/api/school/imports/questions/preview", Form(stream.ToArray(), "questions.xlsx"), Ct));
        var only = Assert.Single(Rows(preview));
        Assert.Equal("New", only.GetProperty("status").GetString());
        Assert.Equal("What is 2 + 2?", only.GetProperty("label").GetString());

        // A workbook without that sheet is read from its first sheet.
        using var plain = new XLWorkbook();
        var first = plain.AddWorksheet("Sheet1");
        for (var i = 0; i < headings.Length; i++) first.Cell(1, i + 1).Value = headings[i];
        for (var i = 0; i < row.Length; i++) first.Cell(2, i + 1).Value = row[i];
        plain.AddWorksheet("Students").Cell(1, 1).Value = "admission_no";
        using var plainStream = new MemoryStream();
        plain.SaveAs(plainStream);
        var fallback = await Json(await s.School.Owner.PostAsync("/api/school/imports/questions/preview", Form(plainStream.ToArray(), "questions.xlsx"), Ct));
        Assert.Equal("New", Assert.Single(Rows(fallback)).GetProperty("status").GetString());
    }
}
