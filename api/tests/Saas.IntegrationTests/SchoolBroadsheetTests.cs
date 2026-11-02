using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;
using Saas.Domain.Core;
using UglyToad.PdfPig;

namespace Saas.IntegrationTests;

/// <summary>Class broadsheet: leadership PDF and CSV of the results grid. Nursery prints developmental levels, not a mark sheet.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolBroadsheetTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    private readonly SchoolKit kit = new(api);

    private static readonly string[] WaecGrades = ["A1", "B2", "B3", "C4", "C5", "C6", "D7", "E8", "F9"];

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

    private sealed record Setup(SchoolKit.School School, Guid ClassId, Guid TermId, Guid MathId, Guid EnglishId, Guid[] Components);

    private async Task<Guid> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code)
    {
        var subject = await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct));
        var link = await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId = subject.GetProperty("id").GetGuid() }, Ct));
        return link.GetProperty("id").GetGuid();
    }

    private async Task<Guid> StaffIdAsync(SchoolKit.School school, Guid userId) =>
        await api.WithDbAsync(db => db.StaffProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId && p.TenantId == school.TenantId).Select(p => p.Id).FirstAsync(Ct));

    private async Task<Setup> SetupAsync(string name)
    {
        var school = await kit.SchoolAsync(name);
        var classId = await kit.ClassAsync(school);
        var math = await ClassSubjectAsync(school, classId, "Mathematics", "MTH");
        var english = await ClassSubjectAsync(school, classId, "English", "ENG");
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={math}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        return new Setup(school, classId, termId, math, english, components);
    }

    private async Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        await api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    private async Task ScoreAsync(Setup s, Guid classSubjectId, Guid enrolmentId, decimal? ca1, decimal? ca2, decimal? exam)
    {
        var entries = new[] { (s.Components[0], ca1), (s.Components[1], ca2), (s.Components[2], exam) }
            .Select(c => new { enrolmentId, componentId = c.Item1, value = c.Item2 }).ToArray();
        (await s.School.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId, termId = s.TermId, entries }, Ct)).EnsureSuccessStatusCode();
    }

    private static JsonElement StudentRow(JsonElement results, string firstName) =>
        results.GetProperty("students").EnumerateArray().Single(x => x.GetProperty("name").GetString()!.StartsWith(firstName));

    private async Task TakeRegisterAsync(Setup s, DateOnly date, params (Guid Enrolment, string Status)[] marks) =>
        (await s.School.Owner.PutAsJsonAsync($"/api/school/attendance/{s.ClassId}/{date:yyyy-MM-dd}",
            new { clientId = Guid.NewGuid().ToString(), marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray() }, Ct)).EnsureSuccessStatusCode();

    /// <summary>=Ada and Bayo tie on 160. Chi has Mathematics only. =Ada's name must be formula-escaped in the CSV.</summary>
    private async Task<(Guid Ada, Guid Bayo, Guid Chi)> ScoredClassAsync(Setup s)
    {
        var ada = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "=Ada", "Zed"));
        var bayo = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Bayo", "Yusuf"));
        var chi = await EnrolmentAsync(s.School, await kit.StudentAsync(s.School, s.ClassId, "Chi", "Xu"));
        await ScoreAsync(s, s.MathId, ada, 20, 20, 50);     // 90
        await ScoreAsync(s, s.EnglishId, ada, 10, 10, 50);  // 70
        await ScoreAsync(s, s.MathId, bayo, 20, 20, 45);    // 85
        await ScoreAsync(s, s.EnglishId, bayo, 15, 10, 50); // 75
        await ScoreAsync(s, s.MathId, chi, 10, 10, 30);     // 50
        return (ada, bayo, chi);
    }

    private static string PdfUrl(Setup s) => $"/api/school/results/{s.ClassId}/{s.TermId}/broadsheet.pdf";
    private static string CsvUrl(Setup s) => $"/api/school/results/{s.ClassId}/{s.TermId}/broadsheet.csv";

    private static string PdfText(byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static async Task<byte[]> FileAsync(HttpClient client, string url, string mediaType)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Private, "broadsheet must be a private response");
        Assert.True(response.Headers.CacheControl?.NoStore, "broadsheet must not be cached");
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }

    private static List<string> CsvLine(string line)
    {
        var cells = new List<string>();
        var i = 0;
        while (true)
        {
            if (i >= line.Length)
            {
                cells.Add("");
                break;
            }
            if (line[i] == '"')
            {
                var value = new StringBuilder();
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    value.Append(line[i]);
                    i++;
                }
                cells.Add(value.ToString());
                if (i >= line.Length) break;
                Assert.Equal(',', line[i]);
                i++;
                if (i == line.Length) { cells.Add(""); break; }
            }
            else
            {
                var comma = line.IndexOf(',', i);
                if (comma < 0) { cells.Add(line[i..]); break; }
                cells.Add(line[i..comma]);
                i = comma + 1;
                if (i == line.Length) { cells.Add(""); break; }
            }
        }
        return cells;
    }

    private static (string Text, List<string> Headers, List<List<string>> Rows) Csv(byte[] bytes)
    {
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        Assert.EndsWith("\r\n", text);
        Assert.DoesNotContain('\n', text.Replace("\r\n", ""));
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        return (text, CsvLine(lines[0]), lines.Skip(1).Select(CsvLine).ToList());
    }

    private static int Col(List<string> headers, string name)
    {
        var index = headers.FindIndex(h => h == name);
        Assert.True(index >= 0, $"missing column {name}. Headers: {string.Join(" | ", headers)}");
        return index;
    }

    [Fact]
    public async Task Broadsheet_csv_matches_computed_totals()
    {
        var s = await SetupAsync("Broadsheet Academy");
        var (ada, bayo, chi) = await ScoredClassAsync(s);
        await TakeRegisterAsync(s, Today.AddDays(-2), (ada, "Present"), (bayo, "Late"), (chi, "Absent"));
        await TakeRegisterAsync(s, Today.AddDays(-1), (ada, "Excused"), (bayo, "Present"), (chi, "Present"));

        var results = await Json(await s.School.Owner.GetAsync($"/api/school/results/{s.ClassId}/{s.TermId}", Ct));
        var adaJson = StudentRow(results, "=Ada");
        Assert.Equal(160m, adaJson.GetProperty("total").GetDecimal());
        Assert.Equal(80m, adaJson.GetProperty("average").GetDecimal());
        Assert.Equal(1, adaJson.GetProperty("position").GetInt32());
        Assert.Equal(1, adaJson.GetProperty("daysPresent").GetInt32());
        Assert.Equal(0, adaJson.GetProperty("daysLate").GetInt32());
        Assert.Equal(0, adaJson.GetProperty("daysAbsent").GetInt32());
        Assert.Equal(1, adaJson.GetProperty("daysExcused").GetInt32());
        Assert.Equal(2, adaJson.GetProperty("daysOpen").GetInt32());

        var (csvText, headers, rows) = Csv(await FileAsync(s.School.Owner, CsvUrl(s), "text/csv"));
        Assert.Contains("\"'=Ada Zed\"", csvText);
        Assert.Equal(["Admission number", "Student"], headers.Take(2));
        Assert.Contains("Present", headers);
        Assert.Contains("Late", headers);
        Assert.Contains("Absent", headers);
        Assert.Contains("Excused", headers);
        Assert.Contains("Open days", headers);
        Assert.Contains("Mathematics grade", headers);
        Assert.Equal(results.GetProperty("students").GetArrayLength(), rows.Count);

        foreach (var student in results.GetProperty("students").EnumerateArray())
        {
            var row = rows.Single(r => r[Col(headers, "Admission number")] == student.GetProperty("admissionNo").GetString());
            Assert.Equal(student.GetProperty("total").GetDecimal(), decimal.Parse(row[Col(headers, "Total")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("average").GetDecimal(), decimal.Parse(row[Col(headers, "Average")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("position").GetInt32(), int.Parse(row[Col(headers, "Position")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("daysPresent").GetInt32(), int.Parse(row[Col(headers, "Present")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("daysLate").GetInt32(), int.Parse(row[Col(headers, "Late")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("daysAbsent").GetInt32(), int.Parse(row[Col(headers, "Absent")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("daysExcused").GetInt32(), int.Parse(row[Col(headers, "Excused")], CultureInfo.InvariantCulture));
            Assert.Equal(student.GetProperty("daysOpen").GetInt32(), int.Parse(row[Col(headers, "Open days")], CultureInfo.InvariantCulture));
            foreach (var subject in student.GetProperty("subjects").EnumerateArray())
            {
                var name = subject.GetProperty("subject").GetString()!;
                Assert.Equal(subject.GetProperty("total").GetDecimal(), decimal.Parse(row[Col(headers, name)], CultureInfo.InvariantCulture));
                Assert.Equal(subject.GetProperty("grade").GetString(), row[Col(headers, $"{name} grade")]);
            }
            var present = student.GetProperty("subjects").EnumerateArray().Select(x => x.GetProperty("subject").GetString()).ToHashSet();
            foreach (var name in new[] { "Mathematics", "English" }.Where(n => !present.Contains(n)))
                Assert.Equal("", row[Col(headers, name)]);
        }

        var pdf = await FileAsync(s.School.Owner, PdfUrl(s), "application/pdf");
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        var text = PdfText(pdf);
        Assert.Contains("Broadsheet", text);
        Assert.Contains("Broadsheet Academy", text);
        Assert.Contains("Mathematics", text);
        Assert.Contains("160", text);
        Assert.Contains("Position", text);
        Assert.Contains("A1", text);
        Assert.Contains("Present", text);
        Assert.Contains("Late", text);
        Assert.Contains("Absent", text);
        Assert.Contains("Excused", text);
        Assert.DoesNotContain("BROADSHEET", text);
        Assert.DoesNotContain("!", text);
    }

    [Fact]
    public async Task Teacher_cannot_export_broadsheet()
    {
        var s = await SetupAsync("Broadsheet Access");
        await ScoredClassAsync(s);
        var (classTeacher, classTeacherUser) = await kit.StaffAsync(s.School, MemberRole.Staff, "Class teacher");
        (await s.School.Owner.PutAsJsonAsync($"/api/school/classes/{s.ClassId}/class-teacher", new { staffId = await StaffIdAsync(s.School, classTeacherUser) }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await classTeacher.GetAsync($"/api/school/results/{s.ClassId}/{s.TermId}", Ct)).StatusCode);

        foreach (var url in new[] { PdfUrl(s), CsvUrl(s) })
        {
            var problem = await ProblemAsync(await classTeacher.GetAsync(url, Ct), HttpStatusCode.Forbidden);
            Assert.Equal("You need the Owner or Admin role for this.", problem.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Nursery_broadsheet_has_no_waec_grade()
    {
        var school = await kit.SchoolAsync("Nursery Broadsheet");
        var level = await Json(await school.Owner.PostAsJsonAsync("/api/school/levels",
            new { name = "Nursery 1", stage = "Nursery", order = 2, hasStudentLogins = false }, Ct));
        Assert.Equal("Developmental", level.GetProperty("assessmentMode").GetString());
        var levelId = level.GetProperty("id").GetGuid();
        var classId = (await Json(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm = "A" }, Ct))).GetProperty("id").GetGuid();
        var (classTeacher, classUser) = await kit.StaffAsync(school, MemberRole.Staff, "Class teacher");
        (await school.Owner.PutAsJsonAsync($"/api/school/classes/{classId}/class-teacher", new { staffId = await StaffIdAsync(school, classUser) }, Ct)).EnsureSuccessStatusCode();
        var termId = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            return await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
        });

        var studentId = await kit.StudentAsync(school, classId, "Ada", "Zed");
        var enrolmentId = await EnrolmentAsync(school, studentId);
        var areas = await Json(await classTeacher.GetAsync("/api/school/developmental/areas", Ct));
        var scale = await Json(await classTeacher.GetAsync("/api/school/developmental/scale", Ct));
        var excellent = scale.GetProperty("scale").EnumerateArray().Single(x => x.GetProperty("label").GetString() == "Excellent");
        var ratings = areas.GetProperty("areas").EnumerateArray().Select(a => new
        {
            areaId = a.GetProperty("id").GetGuid(),
            scaleId = excellent.GetProperty("id").GetGuid(),
        }).ToArray();
        (await classTeacher.PutAsJsonAsync($"/api/school/students/{studentId}/developmental?termId={termId}", new { ratings }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/attendance/{classId}/{Today.AddDays(-1):yyyy-MM-dd}",
            new { clientId = Guid.NewGuid().ToString(), marks = new[] { new { enrolmentId, status = "Present" } } }, Ct)).EnsureSuccessStatusCode();
        (await school.Owner.PutAsJsonAsync($"/api/school/attendance/{classId}/{Today:yyyy-MM-dd}",
            new { clientId = Guid.NewGuid().ToString(), marks = new[] { new { enrolmentId, status = "Late" } } }, Ct)).EnsureSuccessStatusCode();

        var resultsUrl = $"/api/school/results/{classId}/{termId}";
        var results = await Json(await school.Owner.GetAsync(resultsUrl, Ct));
        var ada = results.GetProperty("students").EnumerateArray().Single();
        Assert.Equal(0, ada.GetProperty("subjects").GetArrayLength());
        Assert.Equal("Excellent", ada.GetProperty("developmental").EnumerateArray().Single(d => d.GetProperty("area").GetString() == "Language").GetProperty("scale").GetString());
        Assert.Equal(1, ada.GetProperty("daysPresent").GetInt32());
        Assert.Equal(1, ada.GetProperty("daysLate").GetInt32());
        Assert.Equal(2, ada.GetProperty("daysOpen").GetInt32());

        var sheet = new Setup(school, classId, termId, Guid.Empty, Guid.Empty, []);
        var (csvText, headers, rows) = Csv(await FileAsync(school.Owner, CsvUrl(sheet), "text/csv"));
        Assert.Contains("Language", headers);
        Assert.Contains("Early numeracy", headers);
        Assert.DoesNotContain(headers, h => h.Contains("grade", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headers, h => h.Equals("Total", StringComparison.Ordinal) || h.Equals("Average", StringComparison.Ordinal) || h.Equals("Position", StringComparison.Ordinal));
        Assert.Contains("Present", headers);
        Assert.Contains("Late", headers);
        Assert.Contains("Absent", headers);
        Assert.Contains("Excused", headers);
        Assert.Contains("Open days", headers);
        var row = rows.Single();
        Assert.Equal("Excellent", row[Col(headers, "Language")]);
        Assert.Equal(1, int.Parse(row[Col(headers, "Present")], CultureInfo.InvariantCulture));
        Assert.Equal(1, int.Parse(row[Col(headers, "Late")], CultureInfo.InvariantCulture));
        Assert.Equal(0, int.Parse(row[Col(headers, "Absent")], CultureInfo.InvariantCulture));
        Assert.Equal(0, int.Parse(row[Col(headers, "Excused")], CultureInfo.InvariantCulture));
        Assert.Equal(2, int.Parse(row[Col(headers, "Open days")], CultureInfo.InvariantCulture));
        Assert.DoesNotContain("A1", csvText);
        Assert.All(rows.SelectMany(r => r), cell => Assert.DoesNotContain(WaecGrades, g => cell == g));

        var pdf = PdfText(await FileAsync(school.Owner, PdfUrl(sheet), "application/pdf"));
        Assert.Contains("Language", pdf);
        Assert.Contains("Excellent", pdf);
        Assert.Contains("Developmental levels", pdf);
        Assert.Contains("not exam marks", pdf);
        Assert.DoesNotContain("A1", pdf);
        Assert.DoesNotContain("1st", pdf);
        Assert.DoesNotContain("Position in class", pdf);
        Assert.DoesNotContain("BROADSHEET", pdf);
        Assert.DoesNotContain("!", pdf);
    }
}
