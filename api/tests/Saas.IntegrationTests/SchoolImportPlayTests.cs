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

/// <summary>
/// Play imports (kinds <c>word-lists</c> and <c>teasers</c>): a teacher or leadership loads word lists and brain teasers from a file through the
/// Play screen's create paths, with the screen's rules. A teacher targets only the class subjects they teach. The bursar is refused before any lookup.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportPlayTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string WordHeader = "name,level,class,subject,words";
    private const string TeaserHeader = "level,prompt,answer";
    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string LettersOnly = "Use the letters A to Z only, with no spaces.";
    private const string ClassSubjectMissing = "This class subject was not found.";

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

    private static string Csv(string header, params string[] lines) => string.Join("\r\n", new[] { header }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewWordsAsync(HttpClient client, params string[] lines) =>
        client.PostAsync("/api/school/imports/word-lists/preview", Form(Csv(WordHeader, lines), "words.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewTeasersAsync(HttpClient client, params string[] lines) =>
        client.PostAsync("/api/school/imports/teasers/preview", Form(Csv(TeaserHeader, lines), "teasers.csv"), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, JsonElement preview) =>
        client.PostAsync($"/api/school/imports/{preview.GetProperty("token").GetString()}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private Task<List<WordList>> ListsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.WordLists.IgnoreQueryFilters().Where(w => w.TenantId == tenantId).OrderBy(w => w.Name).ToListAsync(Ct));

    private Task<List<BrainTeaser>> TeasersAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.BrainTeasers.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).OrderBy(t => t.Prompt).ToListAsync(Ct));

    private sealed record Setup(SchoolKit.School School, Guid LevelId, Guid MathsLinkId, Guid EnglishLinkId, HttpClient Teacher);

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
        var mathsLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = maths, teacherStaffId = staffId }, Ct));
        var englishLink = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = english }, Ct));
        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        return new Setup(school, levelId, mathsLink.GetProperty("id").GetGuid(), englishLink.GetProperty("id").GetGuid(), teacher);
    }

    [Fact]
    public async Task Word_lists_import()
    {
        var s = await SetupAsync("Word List Import School");

        var preview = await Json(await PreviewWordsAsync(s.School.Owner,
            "Animals,,,,\"cat, dog ,goat\"",
            "Numbers,jss1,,,\"one,two,three,\"",
            "Shapes,,jss 1a,mth,\"circle,square\"",
            "Reading,,JSS 1A,English,\"book,page\""));
        Assert.Equal("word-lists", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.All(rows, r => Assert.Equal("New", r.GetProperty("status").GetString()));
        Assert.Equal(["Animals", "Numbers", "Shapes", "Reading"], rows.Select(r => r.GetProperty("label").GetString()));
        Assert.Empty(await ListsAsync(s.School.TenantId));

        var done = await Json(await ConfirmAsync(s.School.Owner, preview));
        Assert.Equal(4, done.GetProperty("created").GetInt32());

        var lists = await ListsAsync(s.School.TenantId);
        var animals = lists.Single(l => l.Name == "Animals");
        Assert.Equal(["CAT", "DOG", "GOAT"], animals.Words);
        Assert.Null(animals.LevelId);
        Assert.Null(animals.ClassSubjectId);
        var numbers = lists.Single(l => l.Name == "Numbers");
        Assert.Equal(["ONE", "TWO", "THREE"], numbers.Words);
        Assert.Equal(s.LevelId, numbers.LevelId);
        Assert.Equal(s.MathsLinkId, lists.Single(l => l.Name == "Shapes").ClassSubjectId);
        Assert.Equal(s.EnglishLinkId, lists.Single(l => l.Name == "Reading").ClassSubjectId);

        // The screen lists them.
        var screen = (await Json(await s.School.Owner.GetAsync("/api/school/play/words", Ct))).EnumerateArray().ToList();
        Assert.Equal(4, screen.Count);
    }

    [Fact]
    public async Task Teacher_targets_only_class_subjects_they_teach()
    {
        var s = await SetupAsync("Word List Teacher School");

        var preview = await Json(await PreviewWordsAsync(s.Teacher,
            "Shapes,,JSS 1A,Mathematics,\"circle,square\"",
            "Reading,,JSS 1A,ENG,\"book,page\"",
            "Animals,,,,\"cat,dog\"",
            "Nowhere,,JSS 9Z,MTH,\"cat,dog\"",
            "Physics,,JSS 1A,Physics,\"force,mass\"",
            "Levels,SS 9,,,\"cat,dog\""));
        var rows = Rows(preview);
        Assert.Equal("New", rows[0].GetProperty("status").GetString());
        Assert.Equal(ClassSubjectMissing, rows[1].GetProperty("message").GetString());
        Assert.Equal("New", rows[2].GetProperty("status").GetString());
        Assert.Equal("This class was not found.", rows[3].GetProperty("message").GetString());
        Assert.Equal("This subject was not found.", rows[4].GetProperty("message").GetString());
        Assert.Equal("This level was not found.", rows[5].GetProperty("message").GetString());
        await ProblemAsync(await ConfirmAsync(s.Teacher, preview), HttpStatusCode.Conflict);
        Assert.Empty(await ListsAsync(s.School.TenantId));

        var own = await Json(await PreviewWordsAsync(s.Teacher, "Shapes,,JSS 1A,Mathematics,\"circle,square\""));
        await Json(await ConfirmAsync(s.Teacher, own));
        Assert.Equal(s.MathsLinkId, Assert.Single(await ListsAsync(s.School.TenantId)).ClassSubjectId);
    }

    /// <summary>Carried to Task 11 from Task 9: a real subject of the school that the class doesn't take is "This class subject was not found.", for leadership too.</summary>
    [Fact]
    public async Task Subject_the_class_does_not_take_error()
    {
        var s = await SetupAsync("Word List Untaken Subject School");
        (await s.School.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Biology", code = "BIO" }, Ct)).EnsureSuccessStatusCode();

        var preview = await Json(await PreviewWordsAsync(s.School.Owner,
            "Cells,,JSS 1A,Biology,\"cell,tissue\"",
            "Organs,,JSS 1A,BIO,\"heart,lung\""));
        var rows = Rows(preview);
        Assert.All(rows, r => Assert.Equal("Error", r.GetProperty("status").GetString()));
        Assert.All(rows, r => Assert.Equal(ClassSubjectMissing, r.GetProperty("message").GetString()));
        await ProblemAsync(await ConfirmAsync(s.School.Owner, preview), HttpStatusCode.Conflict);
        Assert.Empty(await ListsAsync(s.School.TenantId));
    }

    /// <summary>Carried to Task 11 from Task 9: the template's "How to fill in" sheet says words are separated by commas only.</summary>
    [Fact]
    public async Task Word_list_template_says_commas_only()
    {
        var s = await SetupAsync("Word List Note School");
        var xlsx = await s.School.Owner.GetAsync("/api/school/imports/word-lists/template.xlsx", Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync(Ct)));
        var notes = workbook.Worksheet("How to fill in").ColumnsUsed().First().CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Contains("Separate words with commas only, not spaces or new lines.", notes);
    }

    [Fact]
    public async Task Words_must_be_letters_error()
    {
        var s = await SetupAsync("Word List Letters School");

        var preview = await Json(await PreviewWordsAsync(s.School.Owner,
            "Digits,,,,\"cat,d0g\"",
            "Spaces,,,,\"ice cream,cake\"",
            "Short,,,,\"at,cat\"",
            "Twice,,,,\"cat,CAT\"",
            "Empty,,,,",
            ",,,,\"cat,dog\"",
            "Good,,,,\"cat,dog\""));
        var rows = Rows(preview);
        Assert.Equal(LettersOnly, rows[0].GetProperty("message").GetString());
        Assert.Equal(LettersOnly, rows[1].GetProperty("message").GetString());
        Assert.Equal("Each word needs 3 to 15 letters.", rows[2].GetProperty("message").GetString());
        Assert.Equal("Each word must be different.", rows[3].GetProperty("message").GetString());
        Assert.Equal("Add at least one word.", rows[4].GetProperty("message").GetString());
        Assert.Equal("Enter a name for the list.", rows[5].GetProperty("message").GetString());
        Assert.Equal("New", rows[6].GetProperty("status").GetString());

        // The same sentence as the screen gives for the same words.
        var screen = await ProblemAsync(await s.School.Owner.PostAsJsonAsync("/api/school/play/words",
            new { name = "Digits", words = new[] { "cat", "d0g" } }, Ct), HttpStatusCode.BadRequest);
        Assert.Contains(LettersOnly, screen.GetRawText());

        var problem = await ProblemAsync(await ConfirmAsync(s.School.Owner, preview), HttpStatusCode.Conflict);
        Assert.Equal(ErrorsMessage, problem.GetProperty("detail").GetString());
        Assert.Empty(await ListsAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Class_without_subject_error()
    {
        var s = await SetupAsync("Word List Pair School");

        var rows = Rows(await Json(await PreviewWordsAsync(s.School.Owner,
            "Shapes,,JSS 1A,,\"circle,square\"",
            "Shapes,,,MTH,\"circle,square\"",
            "Shapes,JSS 1,JSS 1A,MTH,\"circle,square\"")));
        Assert.Equal("Enter the subject as well as the class.", rows[0].GetProperty("message").GetString());
        Assert.Equal("Enter the class as well as the subject.", rows[1].GetProperty("message").GetString());
        // The screen's rule: a class subject or a level, not both.
        Assert.Equal("Choose a class subject or a level, not both.", rows[2].GetProperty("message").GetString());
        Assert.All(rows, r => Assert.Equal("Error", r.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Teasers_import()
    {
        var s = await SetupAsync("Teaser Import School");

        var preview = await Json(await PreviewTeasersAsync(s.Teacher,
            ",What has keys but opens no locks?,A piano",
            "jss 1,\"What has hands, but cannot clap?\",A clock"));
        Assert.Equal("teasers", preview.GetProperty("kind").GetString());
        Assert.All(Rows(preview), r => Assert.Equal("New", r.GetProperty("status").GetString()));
        var done = await Json(await ConfirmAsync(s.Teacher, preview));
        Assert.Equal(2, done.GetProperty("created").GetInt32());

        var teasers = await TeasersAsync(s.School.TenantId);
        Assert.Equal(2, teasers.Count);
        var piano = teasers.Single(t => t.Answer == "A piano");
        Assert.Equal("What has keys but opens no locks?", piano.Prompt);
        Assert.Null(piano.LevelId);
        Assert.False(piano.Archived);
        var clock = teasers.Single(t => t.Answer == "A clock");
        Assert.Equal("What has hands, but cannot clap?", clock.Prompt);
        Assert.Equal(s.LevelId, clock.LevelId);

        // The screen's limits and sentences.
        var errors = await Json(await PreviewTeasersAsync(s.School.Owner,
            ",,An answer",
            ",A riddle,",
            $",{new string('x', 501)},An answer",
            $",A riddle,{new string('y', 201)}",
            "SS 9,A riddle,An answer"));
        var rows = Rows(errors);
        Assert.Equal("Write the teaser.", rows[0].GetProperty("message").GetString());
        Assert.Equal("Write the answer.", rows[1].GetProperty("message").GetString());
        Assert.Equal("Use 500 characters or fewer.", rows[2].GetProperty("message").GetString());
        Assert.Equal("Use 200 characters or fewer.", rows[3].GetProperty("message").GetString());
        Assert.Equal("This level was not found.", rows[4].GetProperty("message").GetString());
        await ProblemAsync(await ConfirmAsync(s.School.Owner, errors), HttpStatusCode.Conflict);
        Assert.Equal(2, (await TeasersAsync(s.School.TenantId)).Count);
    }

    [Fact]
    public async Task Play_templates_download()
    {
        var s = await SetupAsync("Play Template School");

        foreach (var (kind, header, sheet) in new[] { ("word-lists", WordHeader, "Word lists"), ("teasers", TeaserHeader, "Teasers") })
        {
            var csv = await s.Teacher.GetAsync($"/api/school/imports/{kind}/template.csv", Ct);
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            Assert.Equal(header + "\r\n", Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct)).TrimStart('﻿'));

            var xlsx = await s.Teacher.GetAsync($"/api/school/imports/{kind}/template.xlsx", Ct);
            Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
            using var book = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync(Ct)));
            Assert.Equal(sheet, book.Worksheet(1).Name);
        }
    }

    [Fact]
    public async Task Bursar_refused()
    {
        var s = await SetupAsync("Play Import Bursar School");
        var (bursar, bursarId) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");

        foreach (var kind in new[] { "word-lists", "teasers" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/imports/{kind}/template.xlsx", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await bursar.GetAsync($"/api/school/imports/{kind}/template.csv", Ct)).StatusCode);
            // Refused before the file is looked at: a form with no file is 403, not "Choose a file to upload."
            Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync($"/api/school/imports/{kind}/preview",
                new MultipartFormDataContent { { new StringContent("none"), "note" } }, Ct)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewWordsAsync(bursar, "Animals,,,,\"cat,dog\"")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewTeasersAsync(bursar, ",A riddle,An answer")).StatusCode);

        // A held batch of the bursar's own (as if the role changed after the preview) is refused on confirm, and nothing is saved.
        await api.WithDbAsync(db =>
        {
            db.ImportBatches.Add(new ImportBatch
            {
                TenantId = s.School.TenantId, Token = "bursar-word-batch", Kind = "word-lists", CreatedBy = bursarId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
                Payload = """{"fileName":"words.csv","rows":[{"row":2,"cells":{"name":"Animals","level":"","class":"","subject":"","words":"cat,dog"}}]}""",
            });
            db.ImportBatches.Add(new ImportBatch
            {
                TenantId = s.School.TenantId, Token = "bursar-teaser-batch", Kind = "teasers", CreatedBy = bursarId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
                Payload = """{"fileName":"teasers.csv","rows":[{"row":2,"cells":{"level":"","prompt":"A riddle","answer":"An answer"}}]}""",
            });
            return db.SaveChangesAsync(Ct);
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync("/api/school/imports/bursar-word-batch/confirm", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PostAsync("/api/school/imports/bursar-teaser-batch/confirm", null, Ct)).StatusCode);
        Assert.Empty(await ListsAsync(s.School.TenantId));
        Assert.Empty(await TeasersAsync(s.School.TenantId));
    }
}
