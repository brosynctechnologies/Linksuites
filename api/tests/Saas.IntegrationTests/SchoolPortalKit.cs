using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.School;

namespace Saas.IntegrationTests;

/// <summary>
/// Shared setup for the parent and student portal tests: a school with a JSS 1A class, Mathematics and English, the current term around
/// today, and helpers to add a family (student, guardian, parent account), score, register, publish and bill.
/// </summary>
public sealed class SchoolPortalKit(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public static DateOnly Today => SchoolCalendar.Today(DateTimeOffset.UtcNow);
    public SchoolKit Kit { get; } = new(api);

    public sealed record Setup(SchoolKit.School School, Guid ClassId, Guid LevelId, Guid TermId, Guid MathId, Guid EnglishId, Guid[] Components);
    public sealed record Family(Guid StudentId, Guid EnrolmentId, Guid GuardianId, HttpClient Parent, string Name);

    public static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Asserts the status and returns the ProblemDetails detail (empty when there is none).</summary>
    public static async Task<string> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        if (body.Length == 0) return "";
        try
        {
            return JsonDocument.Parse(body).RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public async Task<Guid> ClassSubjectAsync(SchoolKit.School school, Guid classId, string name, string code)
    {
        var subjects = (await Json(await school.Owner.GetAsync("/api/school/subjects", Ct))).EnumerateArray().ToList();
        var existing = subjects.FirstOrDefault(s => s.GetProperty("code").GetString() == code);
        var subjectId = existing.ValueKind != JsonValueKind.Undefined
            ? existing.GetProperty("id").GetGuid()
            : (await Json(await school.Owner.PostAsJsonAsync("/api/school/subjects", new { name, code }, Ct))).GetProperty("id").GetGuid();
        return (await Json(await school.Owner.PostAsJsonAsync($"/api/school/classes/{classId}/subjects", new { subjectId }, Ct))).GetProperty("id").GetGuid();
    }

    public async Task<Setup> SetupAsync(string name)
    {
        var school = await Kit.SchoolAsync(name);
        var classId = await Kit.ClassAsync(school);
        var math = await ClassSubjectAsync(school, classId, "Mathematics", "MTH");
        var english = await ClassSubjectAsync(school, classId, "English", "ENG");
        var (termId, levelId) = await api.WithDbAsync(async db =>
        {
            await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.StartDate, Today.AddDays(-60)).SetProperty(t => t.EndDate, Today.AddDays(60)), Ct);
            var term = await db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct);
            var level = await db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct);
            return (term, level);
        });
        var grid = await Json(await school.Owner.GetAsync($"/api/school/scores?classSubjectId={math}&termId={termId}", Ct));
        var components = grid.GetProperty("components").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
        return new Setup(school, classId, levelId, termId, math, english, components);
    }

    public Task<Guid> EnrolmentAsync(SchoolKit.School school, Guid studentId) =>
        api.WithDbAsync(db => db.Enrolments.IgnoreQueryFilters().Where(e => e.TenantId == school.TenantId && e.StudentId == studentId).Select(e => e.Id).FirstAsync(Ct));

    /// <summary>A student in the class with their own guardian (unique email) and a signed-in parent account linked to them.</summary>
    public async Task<Family> FamilyAsync(Setup s, string firstName, string lastName, Guid? classId = null)
    {
        var studentId = await Kit.StudentAsync(s.School, classId ?? s.ClassId, firstName, lastName);
        var guardianId = await Kit.GuardianAsync(s.School, studentId, $"{firstName} Parent", null, $"{firstName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com");
        var (parent, _) = await Kit.ParentAsync(s.School, guardianId);
        return new Family(studentId, await EnrolmentAsync(s.School, studentId), guardianId, parent, $"{firstName} {lastName}");
    }

    public async Task ScoreAsync(Setup s, Guid classSubjectId, Guid enrolmentId, decimal ca1, decimal ca2, decimal exam)
    {
        var entries = new[] { (s.Components[0], ca1), (s.Components[1], ca2), (s.Components[2], exam) }
            .Select(c => new { enrolmentId, componentId = c.Item1, value = (decimal?)c.Item2 }).ToArray();
        await Json(await s.School.Owner.PutAsJsonAsync("/api/school/scores", new { classSubjectId, termId = s.TermId, entries }, Ct));
    }

    public static async Task RegisterAsync(Setup s, Guid classId, DateOnly date, params (Guid Enrolment, string Status)[] marks) =>
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/attendance/{classId}/{date:yyyy-MM-dd}",
            new { clientId = Guid.NewGuid().ToString(), marks = marks.Select(m => new { enrolmentId = m.Enrolment, status = m.Status }).ToArray() }, Ct));

    public static async Task LockAsync(Setup s, Guid? classId = null) =>
        await Json(await s.School.Owner.PostAsync($"/api/school/results/{classId ?? s.ClassId}/{s.TermId}/lock", null, Ct));

    public static async Task PublishAsync(Setup s, Guid? classId = null) =>
        await Json(await s.School.Owner.PostAsync($"/api/school/results/{classId ?? s.ClassId}/{s.TermId}/publish", null, Ct));

    public static async Task UnpublishAsync(Setup s, Guid? classId = null) =>
        await Json(await s.School.Owner.PostAsync($"/api/school/results/{classId ?? s.ClassId}/{s.TermId}/unpublish", null, Ct));

    /// <summary>A Tuition fee of the given amount for the class's level in the current term, and the invoices generated.</summary>
    public static async Task BillAsync(Setup s, long amountMinor = 10_000_000)
    {
        var itemId = (await Json(await s.School.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = $"Tuition {Guid.NewGuid():N}"[..20], isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await s.School.Owner.PutAsJsonAsync($"/api/school/fee-structure/{s.TermId}/{s.LevelId}", new { lines = new[] { new { feeItemId = itemId, amountMinor } } }, Ct));
        await Json(await s.School.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId = s.TermId }, Ct));
    }

    public static async Task<JsonElement> InvoiceOfAsync(Setup s, Guid studentId) =>
        (await Json(await s.School.Owner.GetAsync($"/api/school/invoices?termId={s.TermId}", Ct))).EnumerateArray()
            .Single(i => i.GetProperty("studentId").GetGuid() == studentId);

    public static async Task PayInFullAsync(Setup s, Guid studentId)
    {
        var invoice = await InvoiceOfAsync(s, studentId);
        await Json(await s.School.Owner.PostAsJsonAsync($"/api/school/invoices/{invoice.GetProperty("id").GetGuid()}/payments",
            new { amountMinor = invoice.GetProperty("balance").GetInt64(), method = "Cash", paidOn = Today }, Ct));
    }

    public static async Task<JsonElement> TimetableAsync(HttpClient staff, Guid classId, params object[] slots) =>
        await Json(await staff.PutAsJsonAsync($"/api/school/timetable/{classId}", slots, Ct));

    public static object Slot(string day, int period, string start, string end, Guid classSubjectId) =>
        new { day, period, start, end, classSubjectId };
}
