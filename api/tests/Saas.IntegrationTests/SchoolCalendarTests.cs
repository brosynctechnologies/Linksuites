using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1: a school corrects its session and term dates and moves to the next term (term 1 → 2 → 3) itself. Starting the next
/// session stays with Promotion.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class SchoolCalendarTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> SetupAsync(HttpClient c) => await c.GetFromJsonAsync<JsonElement>("/api/school/setup", Ct);

    private static JsonElement CurrentSession(JsonElement setup) =>
        setup.GetProperty("sessions").EnumerateArray().Single(s => s.GetProperty("isCurrent").GetBoolean());

    private static JsonElement Term(JsonElement setup, int number) =>
        CurrentSession(setup).GetProperty("terms").EnumerateArray().Single(t => t.GetProperty("number").GetInt32() == number);

    [Fact]
    public async Task Owner_edits_term_dates_and_name()
    {
        var school = await kit.SchoolAsync("Calendar Dates School");
        var term2 = Term(await SetupAsync(school.Owner), 2);
        var start = DateOnly.Parse(term2.GetProperty("startDate").GetString()!).AddDays(3);
        var end = DateOnly.Parse(term2.GetProperty("endDate").GetString()!).AddDays(-2);

        var response = await school.Owner.PutAsJsonAsync($"/api/school/terms/{term2.GetProperty("id").GetGuid()}",
            new { name = "Second Term", startDate = start, endDate = end, nextTermStarts = end.AddDays(14) }, Ct);
        response.EnsureSuccessStatusCode();

        var saved = Term(await SetupAsync(school.Owner), 2);
        Assert.Equal("Second Term", saved.GetProperty("name").GetString());
        Assert.Equal(start, DateOnly.Parse(saved.GetProperty("startDate").GetString()!));
        Assert.Equal(end, DateOnly.Parse(saved.GetProperty("endDate").GetString()!));
        Assert.Equal(end.AddDays(14), DateOnly.Parse(saved.GetProperty("nextTermStarts").GetString()!));
    }

    [Fact]
    public async Task Term_dates_must_be_in_order_and_not_overlap_the_next_term()
    {
        var school = await kit.SchoolAsync("Calendar Order School");
        var setup = await SetupAsync(school.Owner);
        var term1 = Term(setup, 1);
        var term2Start = DateOnly.Parse(Term(setup, 2).GetProperty("startDate").GetString()!);
        var id = term1.GetProperty("id").GetGuid();
        var start = DateOnly.Parse(term1.GetProperty("startDate").GetString()!);

        var backwards = await school.Owner.PutAsJsonAsync($"/api/school/terms/{id}", new { name = "First Term", startDate = start, endDate = start.AddDays(-1) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);

        var overlap = await school.Owner.PutAsJsonAsync($"/api/school/terms/{id}", new { name = "First Term", startDate = start, endDate = term2Start.AddDays(5) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        Assert.Contains("Second Term", (await overlap.Content.ReadAsStringAsync(Ct)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_moves_to_the_next_term_and_attendance_follows()
    {
        var school = await kit.SchoolAsync("Calendar Move School");
        var term2 = Term(await SetupAsync(school.Owner), 2);
        var response = await school.Owner.PostAsync($"/api/school/terms/{term2.GetProperty("id").GetGuid()}/make-current", null, Ct);
        response.EnsureSuccessStatusCode();

        var setup = await SetupAsync(school.Owner);
        Assert.True(Term(setup, 2).GetProperty("isCurrent").GetBoolean());
        Assert.False(Term(setup, 1).GetProperty("isCurrent").GetBoolean());
        Assert.False(Term(setup, 3).GetProperty("isCurrent").GetBoolean());
        Assert.Single(CurrentSession(setup).GetProperty("terms").EnumerateArray(), t => t.GetProperty("isCurrent").GetBoolean());
    }

    [Fact]
    public async Task Session_name_and_dates_can_be_corrected()
    {
        var school = await kit.SchoolAsync("Calendar Session School");
        var session = CurrentSession(await SetupAsync(school.Owner));
        var start = DateOnly.Parse(session.GetProperty("startDate").GetString()!);
        var end = DateOnly.Parse(session.GetProperty("endDate").GetString()!);

        var ok = await school.Owner.PutAsJsonAsync($"/api/school/sessions/{session.GetProperty("id").GetGuid()}",
            new { name = "2026/2027 Session", startDate = start.AddDays(-7), endDate = end.AddDays(-14) }, Ct);
        ok.EnsureSuccessStatusCode();
        var saved = CurrentSession(await SetupAsync(school.Owner));
        Assert.Equal("2026/2027 Session", saved.GetProperty("name").GetString());
        Assert.Equal(start.AddDays(-7), DateOnly.Parse(saved.GetProperty("startDate").GetString()!));

        var backwards = await school.Owner.PutAsJsonAsync($"/api/school/sessions/{session.GetProperty("id").GetGuid()}",
            new { name = "2026/2027", startDate = end, endDate = start }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
    }

    [Fact]
    public async Task Only_owner_and_admin_change_the_calendar()
    {
        var school = await kit.SchoolAsync("Calendar Roles School");
        var (teacher, _) = await kit.StaffAsync(school, MemberRole.Staff, "Teacher");
        var term2 = Term(await SetupAsync(school.Owner), 2);
        var id = term2.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsync($"/api/school/terms/{id}/make-current", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync($"/api/school/terms/{id}",
            new { name = "x", startDate = "2026-01-01", endDate = "2026-02-01" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Another_schools_term_is_not_found()
    {
        var mine = await kit.SchoolAsync("Calendar Mine School");
        var theirs = await kit.SchoolAsync("Calendar Theirs School");
        var theirTerm = Term(await SetupAsync(theirs.Owner), 2).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Owner.PostAsync($"/api/school/terms/{theirTerm}/make-current", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_term_of_a_past_session_cannot_become_current()
    {
        var school = await kit.SchoolAsync("Calendar Past School");
        var termId = Term(await SetupAsync(school.Owner), 2).GetProperty("id").GetGuid();
        // Make that session a past one, as Promotion would after starting the next session.
        await api.WithDbAsync(async db =>
        {
            var session = db.AcademicSessions.IgnoreQueryFilters().Single(s => s.TenantId == school.TenantId && s.IsCurrent);
            session.IsCurrent = false;
            await db.SaveChangesAsync(Ct); // one current session per school: clear it before adding the next
            db.AcademicSessions.Add(new Saas.Domain.School.AcademicSession
            {
                TenantId = school.TenantId, Name = "Next", StartDate = session.EndDate.AddDays(1), EndDate = session.EndDate.AddYears(1), IsCurrent = true,
            });
            return await db.SaveChangesAsync(Ct);
        });

        var response = await school.Owner.PostAsync($"/api/school/terms/{termId}/make-current", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
