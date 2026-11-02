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
/// Trait ratings (kind <c>traits</c>) and developmental ratings (kind <c>developmental</c>), both roster templates for one class and term,
/// saved per student through the child page's own saves. The class teacher or leadership only; the bursar is refused before any lookup.
/// A blank cell keeps the stored rating or comment; an equal value is unchanged. Developmental is for nursery and creche classes only, and a
/// locked term refuses it with the sheet's <c>term_locked</c> 409.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolImportRatingTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private const string ErrorsMessage = "Some rows have errors. Fix them in the file and upload it again; nothing was imported.";
    private const string RateOneToFive = "Rate each item from 1 to 5.";
    private const string TermLocked = "This term is locked for the class, so scores can't change. Ask the principal to unlock it.";

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

    /// <summary>
    /// A file with the heading line <paramref name="header"/> (sheet_for and row_version added last) and every line ending in
    /// <paramref name="sheetFor"/> and a blank row_version: right for a student with nothing stored.
    /// </summary>
    private static string Csv(string header, string sheetFor, params string[] lines) => CsvAt(header, sheetFor, null, lines);

    /// <summary>As <see cref="Csv"/>, with each line's row_version taken from <paramref name="versions"/> (the real template's) by its admission number.</summary>
    private static string CsvAt(string header, string sheetFor, IReadOnlyDictionary<string, string>? versions, params string[] lines) =>
        string.Join("\r\n", new[] { header + ",sheet_for,row_version" }
            .Concat(lines.Select(l => $"{l},\"{sheetFor}\",{versions?.GetValueOrDefault(l.Split(',')[0]) ?? ""}"))) + "\r\n";

    private static Task<Dictionary<string, string>> VersionsAsync(HttpClient client, string kind, string scope) =>
        ImportTemplateKit.VersionsAsync(client, $"/api/school/imports/{kind}/template.csv{scope}");

    private static string TraitScope(Guid classId, Guid termId, string kind = "Psychomotor") => $"?classId={classId}&termId={termId}&kind={kind}";
    private static string DevScope(Guid classId, Guid termId) => $"?classId={classId}&termId={termId}";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string kind, string scope, string csv) =>
        client.PostAsync($"/api/school/imports/{kind}/preview{scope}", Form(Encoding.UTF8.GetBytes(csv), $"{kind}.csv"), Ct);

    private static Task<HttpResponseMessage> PreviewFileAsync(HttpClient client, string kind, string scope, byte[] bytes, string fileName) =>
        client.PostAsync($"/api/school/imports/{kind}/preview{scope}", Form(bytes, fileName), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/school/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();

    private static void AssertCounts(JsonElement preview, int @new, int update, int unchanged, int error)
    {
        var c = preview.GetProperty("counts");
        Assert.Equal((@new, update, unchanged, error),
            (c.GetProperty("new").GetInt32(), c.GetProperty("update").GetInt32(), c.GetProperty("unchanged").GetInt32(), c.GetProperty("error").GetInt32()));
    }

    private static string Token(JsonElement preview) => preview.GetProperty("token").GetString()!;

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<string> AdmissionNoAsync(Guid studentId) =>
        await api.WithDbAsync(db => db.Students.IgnoreQueryFilters().Where(x => x.Id == studentId).Select(x => x.AdmissionNo).FirstAsync(Ct));

    /// <summary>"2026/2027 / JSS 1A / Psychomotor / First term": the session, the class, the list (traits only) and the term.</summary>
    private async Task<string> SheetForAsync(Guid classId, Guid termId, string? list) => await api.WithDbAsync(async db =>
    {
        var cls = await db.SchoolClasses.IgnoreQueryFilters().FirstAsync(c => c.Id == classId, Ct);
        var session = await db.AcademicSessions.IgnoreQueryFilters().Where(x => x.Id == cls.SessionId).Select(x => x.Name).FirstAsync(Ct);
        var term = await db.Terms.IgnoreQueryFilters().Where(t => t.Id == termId).Select(t => t.Name).FirstAsync(Ct);
        return list is null ? $"{session} / {cls.Name} / {term}" : $"{session} / {cls.Name} / {list} / {term}";
    });

    private Task<Guid> CurrentTermAsync(SchoolKit.School school) =>
        api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid TermId, HttpClient ClassTeacher, Guid AdaId, string Ada, Guid ToluId, string Tolu);

    /// <summary>A class with Ada Zed and Tolu Obi and a class teacher. <paramref name="nursery"/> makes it a Nursery 1 class (developmental).</summary>
    private async Task<Setup> SetupAsync(string name, bool nursery = false)
    {
        var school = await kit.SchoolAsync(name);
        Guid classId;
        if (nursery)
        {
            var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
                new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
            classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId = level.GetProperty("id").GetGuid(), arm = "A" }, Ct))).GetProperty("id").GetGuid();
        }
        else classId = await kit.ClassAsync(school);
        var (teacher, userId) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = await StaffIdAsync(school, userId) }, Ct)).EnsureSuccessStatusCode();
        var adaId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var toluId = await kit.StudentAsync(school, classId, "Tolu", "Obi");
        return new Setup(school, classId, await CurrentTermAsync(school), teacher, adaId, await AdmissionNoAsync(adaId), toluId, await AdmissionNoAsync(toluId));
    }

    private static async Task<Dictionary<string, int?>> TraitsAsync(HttpClient client, Guid studentId, Guid termId, string kind = "Psychomotor") =>
        (await Json(await client.GetAsync($"/api/school/students/{studentId}/traits?termId={termId}&kind={kind}", Ct)))
        .GetProperty("ratings").EnumerateArray()
        .ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("value").ValueKind == JsonValueKind.Null ? (int?)null : r.GetProperty("value").GetInt32());

    private static async Task<Dictionary<string, (string? Scale, string? Comment)>> DevelopmentalAsync(HttpClient client, Guid studentId, Guid termId) =>
        (await Json(await client.GetAsync($"/api/school/students/{studentId}/developmental?termId={termId}", Ct)))
        .GetProperty("ratings").EnumerateArray()
        .ToDictionary(r => r.GetProperty("name").GetString()!, r => (r.GetProperty("scale").GetString(), r.GetProperty("comment").GetString()));

    private static async Task TypedTraitsAsync(HttpClient client, Guid studentId, Guid termId, params (string Name, int Value)[] ratings)
    {
        var view = await Json(await client.GetAsync($"/api/school/students/{studentId}/traits?termId={termId}&kind=Psychomotor", Ct));
        var ids = view.GetProperty("ratings").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("criterionId").GetGuid());
        (await client.PutAsJsonAsync($"/api/school/students/{studentId}/traits?termId={termId}&kind=Psychomotor",
            new { ratings = ratings.Select(r => new { criterionId = ids[r.Name], value = r.Value }).ToArray() }, Ct)).EnsureSuccessStatusCode();
    }

    private static async Task TypedDevelopmentalAsync(HttpClient client, Guid studentId, Guid termId, params (string Area, string Label, string? Comment)[] ratings)
    {
        var view = await Json(await client.GetAsync($"/api/school/students/{studentId}/developmental?termId={termId}", Ct));
        var areas = view.GetProperty("ratings").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("areaId").GetGuid());
        var scale = (await Json(await client.GetAsync("/api/school/developmental/scale", Ct))).GetProperty("scale").EnumerateArray()
            .ToDictionary(p => p.GetProperty("label").GetString()!, p => p.GetProperty("id").GetGuid());
        (await client.PutAsJsonAsync($"/api/school/students/{studentId}/developmental?termId={termId}",
            new { ratings = ratings.Select(r => new { areaId = areas[r.Area], scaleId = scale[r.Label], comment = r.Comment }).ToArray() }, Ct)).EnsureSuccessStatusCode();
    }

    private Task<int> TraitRowsAsync(Guid tenantId) => api.WithDbAsync(db => db.TraitRatings.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId, Ct));
    private Task<int> DevelopmentalRowsAsync(Guid tenantId) => api.WithDbAsync(db => db.DevelopmentalRatings.IgnoreQueryFilters().CountAsync(x => x.TenantId == tenantId, Ct));

    // ---------- Traits ----------

    [Fact]
    public async Task Trait_import_saves_ratings()
    {
        var s = await SetupAsync("Trait Import School");
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, "Psychomotor");
        Assert.Matches(@"^\d{4}/\d{4} / JSS 1A / Psychomotor / ", sheetFor);

        var preview = await Json(await PreviewAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Handwriting,Drawing", sheetFor, $"{s.Ada},Ada Zed,4,5", $"{s.Tolu},Tolu Obi,,")));
        Assert.Equal("traits", preview.GetProperty("kind").GetString());
        var rows = Rows(preview);
        Assert.Equal("New", rows[0].GetProperty("status").GetString());
        Assert.Contains("Ada Zed", rows[0].GetProperty("label").GetString());
        Assert.Equal("Unchanged", rows[1].GetProperty("status").GetString());
        AssertCounts(preview, 1, 0, 1, 0);
        Assert.Equal(0, await TraitRowsAsync(s.School.TenantId));

        var done = await Json(await ConfirmAsync(s.ClassTeacher, Token(preview)));
        Assert.Equal((1, 0), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        var ada = await TraitsAsync(s.ClassTeacher, s.AdaId, s.TermId);
        Assert.Equal(4, ada["Handwriting"]);
        Assert.Equal(5, ada["Drawing"]);
        Assert.Null(ada["Use of tools"]);
        Assert.All((await TraitsAsync(s.ClassTeacher, s.ToluId, s.TermId)).Values, v => Assert.Null(v));
        // The affective list is a different file: nothing landed there.
        Assert.All((await TraitsAsync(s.ClassTeacher, s.AdaId, s.TermId, "Affective")).Values, v => Assert.Null(v));

        // The real template carries the stored ratings and sheet_for; uploaded unchanged, every row is Unchanged.
        var xlsx = await s.School.Owner.GetAsync("/api/school/imports/traits/template.xlsx" + TraitScope(s.ClassId, s.TermId), Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Traits");
            var headings = Enumerable.Range(1, 9).Select(c => sheet.Cell(1, c).GetString()).ToList();
            Assert.Equal(["admission_no", "student", "Handwriting", "Drawing", "Hand coordination", "Use of tools", "Physical coordination", "Practical tasks", "sheet_for"], headings);
            var adaRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.Ada);
            Assert.Equal(("4", "5", ""), (sheet.Cell(adaRow, 3).GetString(), sheet.Cell(adaRow, 4).GetString(), sheet.Cell(adaRow, 5).GetString()));
            Assert.Equal(sheetFor, sheet.Cell(adaRow, 9).GetString());
        }
        var roundTrip = await Json(await PreviewFileAsync(s.School.Owner, "traits", TraitScope(s.ClassId, s.TermId), bytes, "traits.xlsx"));
        AssertCounts(roundTrip, 0, 0, 2, 0);
        Assert.Equal(0, roundTrip.GetProperty("warnings").GetArrayLength());

        // The psychomotor template uploaded as affective has none of the affective items, so it is refused, naming them.
        var asAffective = await ProblemAsync(await PreviewFileAsync(s.School.Owner, "traits", TraitScope(s.ClassId, s.TermId, "Affective"), bytes, "traits.xlsx"), HttpStatusCode.BadRequest);
        Assert.StartsWith("The file is missing the columns Appearance / neatness, Punctuality,", asAffective.GetProperty("detail").GetString());
        // A file that has an affective item but the psychomotor sheet_for is refused by its sheet_for.
        var mixed = Rows(await Json(await PreviewAsync(s.School.Owner, "traits", TraitScope(s.ClassId, s.TermId, "Affective"),
            Csv("admission_no,student,Punctuality", sheetFor, $"{s.Ada},Ada Zed,4"))));
        Assert.Equal($"This sheet was made for {sheetFor}. Download the template for this class and term.", Assert.Single(mixed).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Trait_value_outside_1_to_5_error()
    {
        var s = await SetupAsync("Trait Range School");
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, "Psychomotor");
        var preview = await Json(await PreviewAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Handwriting,Drawing", sheetFor, $"{s.Ada},Ada Zed,6,", $"{s.Tolu},Tolu Obi,3,4")));
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal(RateOneToFive, rows[0].GetProperty("message").GetString());
        AssertCounts(preview, 1, 0, 0, 1);

        var problem = await ProblemAsync(await ConfirmAsync(s.ClassTeacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal(ErrorsMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(0, await TraitRowsAsync(s.School.TenantId));

        // Zero, a fraction and a word are refused with the same sentence.
        var more = Rows(await Json(await PreviewAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Handwriting,Drawing", sheetFor, $"{s.Ada},Ada Zed,0,", $"{s.Tolu},Tolu Obi,,2.5"))))
            .Concat(Rows(await Json(await PreviewAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId),
                Csv("admission_no,student,Handwriting", sheetFor, $"{s.Ada},Ada Zed,good"))))).ToList();
        Assert.All(more, r =>
        {
            Assert.Equal("Error", r.GetProperty("status").GetString());
            Assert.Equal(RateOneToFive, r.GetProperty("message").GetString());
        });
    }

    [Fact]
    public async Task Subject_teacher_refused_traits()
    {
        var s = await SetupAsync("Trait Access School");
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, "Psychomotor");
        var csv = Csv("admission_no,student,Handwriting", sheetFor, $"{s.Ada},Ada Zed,4");

        // A teacher who teaches a subject in the class but is not its class teacher: "not found", as on the child page.
        var (subjectTeacher, userId) = await kit.StaffAsync(s.School, MemberRole.Staff, "Maths teacher");
        var subject = await Json(await s.School.Owner.PostAsJsonAsync("/api/school/subjects", new { name = "Mathematics", code = "MTH" }, Ct));
        (await s.School.Owner.PostAsJsonAsync($"/api/school/classes/{s.ClassId}/subjects",
            new { subjectId = subject.GetProperty("id").GetGuid(), teacherStaffId = await StaffIdAsync(s.School, userId) }, Ct)).EnsureSuccessStatusCode();
        const string NotFound = "Class was not found.";
        var template = await ProblemAsync(await subjectTeacher.GetAsync("/api/school/imports/traits/template.xlsx" + TraitScope(s.ClassId, s.TermId), Ct), HttpStatusCode.NotFound);
        Assert.Equal(NotFound, template.GetProperty("detail").GetString());
        var preview = await ProblemAsync(await PreviewAsync(subjectTeacher, "traits", TraitScope(s.ClassId, s.TermId), csv), HttpStatusCode.NotFound);
        Assert.Equal(NotFound, preview.GetProperty("detail").GetString());
        var dev = await ProblemAsync(await PreviewAsync(subjectTeacher, "developmental", DevScope(s.ClassId, s.TermId), csv), HttpStatusCode.NotFound);
        Assert.Equal(NotFound, dev.GetProperty("detail").GetString());
        // The child page answers the same teacher with 404 too.
        await ProblemAsync(await subjectTeacher.GetAsync($"/api/school/students/{s.AdaId}/traits?termId={s.TermId}&kind=Psychomotor", Ct), HttpStatusCode.NotFound);

        // The bursar is refused before anything is looked up, even for a made-up class.
        var (bursar, _) = await kit.StaffAsync(s.School, MemberRole.Accountant, "Bursar");
        await ProblemAsync(await bursar.GetAsync("/api/school/imports/traits/template.csv" + TraitScope(Guid.NewGuid(), s.TermId), Ct), HttpStatusCode.Forbidden);
        await ProblemAsync(await PreviewAsync(bursar, "traits", TraitScope(s.ClassId, s.TermId), csv), HttpStatusCode.Forbidden);
        await ProblemAsync(await bursar.GetAsync("/api/school/imports/developmental/template.csv" + DevScope(Guid.NewGuid(), s.TermId), Ct), HttpStatusCode.Forbidden);

        // The kind is required.
        var noKind = await ProblemAsync(await s.ClassTeacher.GetAsync($"/api/school/imports/traits/template.xlsx?classId={s.ClassId}&termId={s.TermId}", Ct), HttpStatusCode.BadRequest);
        Assert.Equal("Choose Psychomotor or Affective.", noKind.GetProperty("errors").GetProperty("kind")[0].GetString());

        // Leadership reaches the class; nothing was written by any of the refused calls.
        AssertCounts(await Json(await PreviewAsync(s.School.Owner, "traits", TraitScope(s.ClassId, s.TermId), csv)), 1, 0, 0, 0);
        Assert.Equal(0, await TraitRowsAsync(s.School.TenantId));
    }

    // ---------- Developmental ----------

    [Fact]
    public async Task Developmental_import_saves_labels()
    {
        var s = await SetupAsync("Developmental Import School", nursery: true);
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, null);
        Assert.Matches(@"^\d{4}/\d{4} / Nursery 1A / ", sheetFor);

        var preview = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Language,Language comment,Social,Self-care", sheetFor,
                $"{s.Ada},Ada Zed,very good,Speaks in full sentences.,Excellent,", $"{s.Tolu},Tolu Obi,,,,")));
        Assert.Equal("developmental", preview.GetProperty("kind").GetString());
        AssertCounts(preview, 1, 0, 1, 0);
        Assert.Equal(0, await DevelopmentalRowsAsync(s.School.TenantId));

        var done = await Json(await ConfirmAsync(s.ClassTeacher, Token(preview)));
        Assert.Equal((1, 0), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        var ada = await DevelopmentalAsync(s.ClassTeacher, s.AdaId, s.TermId);
        Assert.Equal(("Very Good", "Speaks in full sentences."), ada["Language"]);
        Assert.Equal(("Excellent", (string?)null), ada["Social"]);
        Assert.Equal(((string?)null, (string?)null), ada["Self-care"]);
        Assert.Equal(2, await DevelopmentalRowsAsync(s.School.TenantId));

        // The template has one column per area and one comment column per area, prefilled; unchanged it is all Unchanged.
        var xlsx = await s.School.Owner.GetAsync("/api/school/imports/developmental/template.xlsx" + DevScope(s.ClassId, s.TermId), Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync(Ct);
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = workbook.Worksheet("Developmental");
            Assert.Equal(["admission_no", "student", "Language", "Language comment", "Early numeracy", "Early numeracy comment"],
                Enumerable.Range(1, 6).Select(c => sheet.Cell(1, c).GetString()));
            // sheet_for, then the hidden row_version last.
            var last = sheet.LastColumnUsed()!.ColumnNumber() - 1;
            Assert.Equal("sheet_for", sheet.Cell(1, last).GetString());
            Assert.Equal("row_version", sheet.Cell(1, last + 1).GetString());
            Assert.True(sheet.Column(last + 1).IsHidden);
            var adaRow = Enumerable.Range(2, 2).Single(r => sheet.Cell(r, 1).GetString() == s.Ada);
            Assert.Equal(("Very Good", "Speaks in full sentences."), (sheet.Cell(adaRow, 3).GetString(), sheet.Cell(adaRow, 4).GetString()));
            Assert.Equal(sheetFor, sheet.Cell(adaRow, last).GetString());
        }
        var roundTrip = await Json(await PreviewFileAsync(s.School.Owner, "developmental", DevScope(s.ClassId, s.TermId), bytes, "developmental.xlsx"));
        AssertCounts(roundTrip, 0, 0, 2, 0);
        Assert.Equal(0, roundTrip.GetProperty("warnings").GetArrayLength());
        var csvBytes = await (await s.School.Owner.GetAsync("/api/school/imports/developmental/template.csv" + DevScope(s.ClassId, s.TermId), Ct)).Content.ReadAsByteArrayAsync(Ct);
        AssertCounts(await Json(await PreviewFileAsync(s.School.Owner, "developmental", DevScope(s.ClassId, s.TermId), csvBytes, "developmental.csv")), 0, 0, 2, 0);
    }

    [Fact]
    public async Task Unknown_label_error()
    {
        var s = await SetupAsync("Developmental Label School", nursery: true);
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, null);
        var preview = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Language", sheetFor, $"{s.Ada},Ada Zed,Brilliant", $"{s.Tolu},Tolu Obi,Good")));
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal("Language must be one of Excellent, Very Good, Good, Developing or Needs Support.", rows[0].GetProperty("message").GetString());
        AssertCounts(preview, 1, 0, 0, 1);
        var problem = await ProblemAsync(await ConfirmAsync(s.ClassTeacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal(0, await DevelopmentalRowsAsync(s.School.TenantId));
    }

    [Fact]
    public async Task Comment_without_rating_error()
    {
        var s = await SetupAsync("Developmental Comment School", nursery: true);
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, null);
        var preview = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Language,Language comment", sheetFor, $"{s.Ada},Ada Zed,,Quiet this term.")));
        var row = Rows(preview)[0];
        Assert.Equal("Error", row.GetProperty("status").GetString());
        Assert.Equal("Choose a rating for Language before the comment.", row.GetProperty("message").GetString());
        await ProblemAsync(await ConfirmAsync(s.ClassTeacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal(0, await DevelopmentalRowsAsync(s.School.TenantId));

        // With a rating already saved, a comment alone is fine: it is added to the saved rating.
        await TypedDevelopmentalAsync(s.ClassTeacher, s.AdaId, s.TermId, ("Language", "Good", null));
        var versions = await VersionsAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId));
        var again = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId),
            CsvAt("admission_no,student,Language,Language comment", sheetFor, versions, $"{s.Ada},Ada Zed,,Quiet this term.")));
        AssertCounts(again, 0, 1, 0, 0);
        await Json(await ConfirmAsync(s.ClassTeacher, Token(again)));
        Assert.Equal(("Good", "Quiet this term."), (await DevelopmentalAsync(s.ClassTeacher, s.AdaId, s.TermId))["Language"]);
    }

    [Fact]
    public async Task Developmental_locked_term_409()
    {
        var s = await SetupAsync("Developmental Lock School", nursery: true);
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, null);
        var csv = Csv("admission_no,student,Language", sheetFor, $"{s.Ada},Ada Zed,Good");
        var preview = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId), csv));
        AssertCounts(preview, 1, 0, 0, 0);

        (await s.School.Owner.PostAsync($"/api/school/results/{s.ClassId}/{s.TermId}/lock", null, Ct)).EnsureSuccessStatusCode();

        var problem = await ProblemAsync(await ConfirmAsync(s.ClassTeacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("term_locked", problem.GetProperty("code").GetString());
        Assert.Equal(TermLocked, problem.GetProperty("detail").GetString());
        var again = await ProblemAsync(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId), csv), HttpStatusCode.Conflict);
        Assert.Equal("term_locked", again.GetProperty("code").GetString());
        Assert.Equal(0, await DevelopmentalRowsAsync(s.School.TenantId));

        // A numeric class is refused with the child page's sentence.
        var numeric = await SetupAsync("Developmental Numeric School");
        var refused = await ProblemAsync(await numeric.School.Owner.GetAsync("/api/school/imports/developmental/template.xlsx" + DevScope(numeric.ClassId, numeric.TermId), Ct), HttpStatusCode.BadRequest);
        Assert.Equal("This class uses numeric assessment.", refused.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Blank_rating_keeps_stored()
    {
        // Traits: Handwriting 3 and Drawing 2 saved; the file leaves Handwriting blank and changes Drawing.
        var s = await SetupAsync("Rating Blank School");
        await TypedTraitsAsync(s.ClassTeacher, s.AdaId, s.TermId, ("Handwriting", 3), ("Drawing", 2));
        var traitsFor = await SheetForAsync(s.ClassId, s.TermId, "Psychomotor");
        var traitVersions = await VersionsAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId));
        var traits = await Json(await PreviewAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId),
            CsvAt("admission_no,student,Handwriting,Drawing", traitsFor, traitVersions, $"{s.Ada},Ada Zed,,4", $"{s.Tolu},Tolu Obi,,")));
        AssertCounts(traits, 0, 1, 1, 0);
        var done = await Json(await ConfirmAsync(s.ClassTeacher, Token(traits)));
        Assert.Equal((0, 1), (done.GetProperty("created").GetInt32(), done.GetProperty("updated").GetInt32()));
        var ada = await TraitsAsync(s.ClassTeacher, s.AdaId, s.TermId);
        Assert.Equal((3, 4), (ada["Handwriting"], ada["Drawing"]));
        // An equal value is Unchanged.
        AssertCounts(await Json(await PreviewAsync(s.ClassTeacher, "traits", TraitScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Handwriting,Drawing", traitsFor, $"{s.Ada},Ada Zed,3,4"))), 0, 0, 1, 0);

        // Developmental: Language Good with a comment saved; a blank rating and comment keep both, and a new rating keeps the comment.
        var n = await SetupAsync("Developmental Blank School", nursery: true);
        await TypedDevelopmentalAsync(n.ClassTeacher, n.AdaId, n.TermId, ("Language", "Good", "Kind to others."), ("Social", "Developing", "Shy."));
        var devFor = await SheetForAsync(n.ClassId, n.TermId, null);
        var devVersions = await VersionsAsync(n.ClassTeacher, "developmental", DevScope(n.ClassId, n.TermId));
        var dev = await Json(await PreviewAsync(n.ClassTeacher, "developmental", DevScope(n.ClassId, n.TermId),
            CsvAt("admission_no,student,Language,Language comment,Social,Social comment,Motor", devFor, devVersions, $"{n.Ada},Ada Zed,,,excellent,,Good", $"{n.Tolu},Tolu Obi,,,,,")));
        AssertCounts(dev, 0, 1, 1, 0);
        await Json(await ConfirmAsync(n.ClassTeacher, Token(dev)));
        var saved = await DevelopmentalAsync(n.ClassTeacher, n.AdaId, n.TermId);
        Assert.Equal(("Good", "Kind to others."), saved["Language"]);
        Assert.Equal(("Excellent", "Shy."), saved["Social"]);
        Assert.Equal(("Good", (string?)null), saved["Motor"]);
        // Equal labels and comments, in any case, are Unchanged.
        AssertCounts(await Json(await PreviewAsync(n.ClassTeacher, "developmental", DevScope(n.ClassId, n.TermId),
            Csv("admission_no,student,Language,Language comment,Social", devFor, $"{n.Ada},Ada Zed,GOOD,Kind to others.,Excellent"))), 0, 0, 1, 0);
    }

    [Fact]
    public async Task Developmental_scale_codes_are_accepted_and_documented()
    {
        var s = await SetupAsync("Developmental Code School", nursery: true);
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, null);

        // The template's "How to fill in" sheet says a code works as well as a label, naming each code.
        var xlsx = await s.School.Owner.GetAsync("/api/school/imports/developmental/template.xlsx" + DevScope(s.ClassId, s.TermId), Ct);
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using (var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync(Ct))))
        {
            var notes = workbook.Worksheet("How to fill in").ColumnsUsed().First().CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains(notes, n => n.StartsWith("You can type the scale's code instead of the label:", StringComparison.Ordinal)
                && n.Contains("very-good for Very Good", StringComparison.Ordinal) && n.Contains("needs-support for Needs Support", StringComparison.Ordinal));
        }

        // Codes, in any case, save as the scale point they name.
        var preview = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Language,Social", sheetFor, $"{s.Ada},Ada Zed,needs-support,VERY-GOOD")));
        AssertCounts(preview, 1, 0, 0, 0);
        await Json(await ConfirmAsync(s.ClassTeacher, Token(preview)));
        var ada = await DevelopmentalAsync(s.ClassTeacher, s.AdaId, s.TermId);
        Assert.Equal(("Needs Support", (string?)null), ada["Language"]);
        Assert.Equal(("Very Good", (string?)null), ada["Social"]);
    }

    [Fact]
    public async Task Developmental_comment_over_500_error()
    {
        var s = await SetupAsync("Developmental Long Comment School", nursery: true);
        var sheetFor = await SheetForAsync(s.ClassId, s.TermId, null);
        var exactly500 = new string('a', 500);
        var preview = await Json(await PreviewAsync(s.ClassTeacher, "developmental", DevScope(s.ClassId, s.TermId),
            Csv("admission_no,student,Language,Language comment", sheetFor,
                $"{s.Ada},Ada Zed,Good,{new string('b', 501)}", $"{s.Tolu},Tolu Obi,Good,  {exactly500}  ")));
        var rows = Rows(preview);
        Assert.Equal("Error", rows[0].GetProperty("status").GetString());
        Assert.Equal("The Language comment can have at most 500 characters.", rows[0].GetProperty("message").GetString());
        // 500 characters with spaces around them is within the limit once trimmed, as on the child page.
        Assert.Equal("New", rows[1].GetProperty("status").GetString());
        var problem = await ProblemAsync(await ConfirmAsync(s.ClassTeacher, Token(preview)), HttpStatusCode.Conflict);
        Assert.Equal("import_errors", problem.GetProperty("code").GetString());
        Assert.Equal(0, await DevelopmentalRowsAsync(s.School.TenantId));
    }
}
