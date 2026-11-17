using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Courses;

namespace Saas.IntegrationTests;

/// <summary>Wave 2 (roadmap 2026-11-17) LearnLink fixes: C6, C8, C9 (C7 is a unit test).</summary>
[Collection(nameof(ApiCollection))]
public sealed class CourseWave2Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

    [Fact]
    public async Task C6_Reenrolling_after_a_refund_counts_the_student_again()
    {
        var (tutor, tenantId) = await TutorAsync("Rebuy");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "Rebuy course", 500_000);
        await AddTextLessonAsync(tutor, course);
        await PublishAsync(tutor, course);
        var learner = await LearnerAsync("Rebuyer");

        var first = await BuyAsync(learner, course, 500_000);
        Assert.Equal(1, await StudentsCountAsync(course));
        (await tutor.PostAsync($"/api/studio/orders/{first}/refund", null, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(0, await StudentsCountAsync(course));
        await BuyAsync(learner, course, 500_000);

        Assert.Equal(1, await StudentsCountAsync(course));
    }

    [Fact]
    public async Task C8_This_month_starts_at_Lagos_midnight_and_skips_refunded_enrolments()
    {
        var (tutor, tenantId) = await TutorAsync("Month");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "Month course", 0, isFree: true);
        await AddTextLessonAsync(tutor, course);
        await PublishAsync(tutor, course);
        foreach (var name in new[] { "Early", "Earlier", "Refunded" })
            (await (await LearnerAsync(name)).PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct)).EnsureSuccessStatusCode();

        // 00:30 on the 1st in Lagos is still the previous month in UTC.
        var nowLagos = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Lagos);
        var monthStartLagos = new DateTimeOffset(nowLagos.Year, nowLagos.Month, 1, 0, 0, 0, Lagos.GetUtcOffset(new DateTime(nowLagos.Year, nowLagos.Month, 1)));
        var justAfter = monthStartLagos.AddMinutes(30).ToUniversalTime();
        await api.WithDbAsync(async db =>
        {
            var rows = await db.Enrollments.IgnoreQueryFilters().Where(e => e.CourseId == course).OrderBy(e => e.CreatedAt).ToListAsync(Ct);
            rows[0].CreatedAt = justAfter;
            rows[1].CreatedAt = justAfter;
            rows[2].Revoked = true;
            return await db.SaveChangesAsync(Ct);
        });

        // A UTC month would count only the refunded one; the Lagos month counts the two early ones and skips the refund.
        var dashboard = await Json(await tutor.GetAsync("/api/studio/dashboard", Ct));
        Assert.Equal(2, dashboard.GetProperty("enrolmentsThisMonth").GetInt32());
    }

    [Fact]
    public async Task C9_Mark_complete_does_not_set_the_lesson_length_but_a_reported_media_duration_does()
    {
        var (tutor, tenantId) = await TutorAsync("Length");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "Length course", 0, isFree: true);
        var lesson = await AddTextLessonAsync(tutor, course);
        await PublishAsync(tutor, course);
        // An unprobed video lesson.
        await api.WithDbAsync(db => db.Lessons.IgnoreQueryFilters().Where(l => l.Id == lesson)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Type, LessonType.Video).SetProperty(l => l.DurationSec, 0), Ct));
        var learner = await LearnerAsync("Skipper");
        (await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct)).EnsureSuccessStatusCode();

        (await learner.PutAsJsonAsync($"/api/learn/lessons/{lesson}/progress", new { positionSec = 120, completed = true }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(0, await DurationAsync(lesson));

        (await learner.PutAsJsonAsync($"/api/learn/lessons/{lesson}/progress", new { positionSec = 130, completed = true, mediaDurationSec = 1200 }, Ct))
            .EnsureSuccessStatusCode();
        Assert.Equal(1200, await DurationAsync(lesson));
    }

    // Helpers

    private Task<int> StudentsCountAsync(Guid course) =>
        api.WithDbAsync(db => db.Courses.IgnoreQueryFilters().Where(c => c.Id == course).Select(c => c.StudentsCount).SingleAsync(Ct));

    private Task<int> DurationAsync(Guid lesson) =>
        api.WithDbAsync(db => db.Lessons.IgnoreQueryFilters().Where(l => l.Id == lesson).Select(l => l.DurationSec).SingleAsync(Ct));

    private async Task<(HttpClient Client, Guid TenantId)> TutorAsync(string name)
    {
        var client = api.CreateClient();
        var response = await api.SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = $"{name} Tutor", email = $"crs2-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "courses", businessName = $"{name} Academy",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private async Task<HttpClient> LearnerAsync(string name)
    {
        var client = api.CreateClient();
        var response = await api.SignUpAsync(client, "/api/auth/learner/signup", new
        {
            fullName = $"{name} Learner", email = $"crs2-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<Guid> CreateCourseAsync(HttpClient tutor, string title, long price, bool isFree = false)
    {
        var response = await tutor.PostAsJsonAsync("/api/studio/courses",
            new { title, priceMinor = price, isFree, level = "AllLevels", listOnMarketplace = false }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddTextLessonAsync(HttpClient tutor, Guid course)
    {
        var detail = await Json(await tutor.GetAsync($"/api/studio/courses/{course}", Ct));
        var section = detail.GetProperty("sections")[0];
        var response = await tutor.PostAsJsonAsync($"/api/studio/sections/{section.GetProperty("id").GetGuid()}/lessons",
            new { title = "Reading 1", type = "Text" }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("sections")[0].GetProperty("lessons")[0].GetProperty("id").GetGuid();
    }

    private async Task PublishAsync(HttpClient tutor, Guid course) =>
        (await tutor.PostAsync($"/api/studio/courses/{course}/publish", null, Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> BuyAsync(HttpClient learner, Guid course, long amount)
    {
        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course, channel = "Academy" }, Ct));
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, amount, "NGN", DateTimeOffset.UtcNow);
        (await learner.PostAsJsonAsync($"/api/learn/orders/{orderId}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        return orderId;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
