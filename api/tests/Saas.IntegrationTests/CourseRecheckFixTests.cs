using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Courses;

namespace Saas.IntegrationTests;

/// <summary>Fixes from the LearnLink recheck (bookings-courses.md, C1-C4).</summary>
[Collection(nameof(ApiCollection))]
public sealed class CourseRecheckFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task C1_Students_carry_the_sale_channel_and_how_they_enrolled()
    {
        var (tutor, tenantId) = await TutorAsync("Channel");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var paid = await CreateCourseAsync(tutor, "Paid course", 1_000_000);
        await AddTextLessonsAsync(tutor, paid, 1);
        await PublishAsync(tutor, paid);
        var free = await CreateCourseAsync(tutor, "Free course", 0, isFree: true);
        await AddTextLessonsAsync(tutor, free, 1);
        await PublishAsync(tutor, free);
        (await tutor.PostAsJsonAsync("/api/studio/coupons", new
        {
            code = "ALLFREE", courseId = (Guid?)null, discountType = "Percent", value = 100, maxUses = (int?)null,
            expiresAt = (string?)null, isActive = true,
        }, Ct)).EnsureSuccessStatusCode();

        var buyer = await LearnerAsync("Buyer");
        await BuyAsync(buyer, paid, 1_000_000, "Academy");
        var couponed = await LearnerAsync("Couponed");
        (await couponed.PostAsJsonAsync("/api/learn/checkout", new { courseId = paid, couponCode = "ALLFREE" }, Ct)).EnsureSuccessStatusCode();
        var freeLearner = await LearnerAsync("Freebie");
        (await freeLearner.PostAsJsonAsync("/api/learn/checkout", new { courseId = free }, Ct)).EnsureSuccessStatusCode();

        var students = (await Json(await tutor.GetAsync("/api/studio/students?pageSize=50", Ct))).GetProperty("items").EnumerateArray()
            .ToDictionary(s => s.GetProperty("name").GetString()!);
        Assert.Equal("Purchase", students["Buyer Learner"].GetProperty("source").GetString());
        Assert.Equal("Academy", students["Buyer Learner"].GetProperty("channel").GetString());
        Assert.Equal("Coupon", students["Couponed Learner"].GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, students["Couponed Learner"].GetProperty("channel").ValueKind);
        Assert.Equal("Free", students["Freebie Learner"].GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, students["Freebie Learner"].GetProperty("channel").ValueKind);
    }

    [Fact]
    public async Task C2_Deleting_the_last_unfinished_lesson_completes_the_course_and_issues_the_certificate()
    {
        var (tutor, tenantId) = await TutorAsync("Shrink");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "Four readings", 0, isFree: true);
        var lessons = await AddTextLessonsAsync(tutor, course, 4);
        await PublishAsync(tutor, course);
        var learner = await LearnerAsync("Stuck");
        var other = await LearnerAsync("Halfway");
        foreach (var l in new[] { learner, other }) (await l.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct)).EnsureSuccessStatusCode();
        foreach (var lesson in lessons.Take(3)) await CompleteAsync(learner, lesson);
        await CompleteAsync(other, lessons[0]);
        Assert.Equal(75, (await MineAsync(learner, course)).GetProperty("progressPercent").GetInt32());

        (await tutor.DeleteAsync($"/api/studio/lessons/{lessons[3]}", Ct)).EnsureSuccessStatusCode();

        var mine = await MineAsync(learner, course);
        Assert.Equal(100, mine.GetProperty("progressPercent").GetInt32());
        Assert.Equal(3, mine.GetProperty("lessonsTotal").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("completedAt").ValueKind);
        Assert.False(string.IsNullOrEmpty(mine.GetProperty("certificateCode").GetString()));
        Assert.Equal(100, (await Json(await learner.GetAsync($"/api/learn/courses/{course}", Ct))).GetProperty("progressPercent").GetInt32());
        Assert.Equal(33, (await MineAsync(other, course)).GetProperty("progressPercent").GetInt32());
    }

    [Fact]
    public async Task C2_Adding_a_lesson_reopens_a_finished_course_but_keeps_the_certificate()
    {
        var (tutor, tenantId) = await TutorAsync("Grow");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "Growing course", 0, isFree: true);
        var lessons = await AddTextLessonsAsync(tutor, course, 4);
        await PublishAsync(tutor, course);
        var learner = await LearnerAsync("Finisher");
        (await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct)).EnsureSuccessStatusCode();
        foreach (var lesson in lessons) await CompleteAsync(learner, lesson);
        var finished = await MineAsync(learner, course);
        Assert.Equal(100, finished.GetProperty("progressPercent").GetInt32());
        var code = finished.GetProperty("certificateCode").GetString();
        Assert.False(string.IsNullOrEmpty(code));

        var added = await AddTextLessonsAsync(tutor, course, 1);

        var mine = await MineAsync(learner, course);
        Assert.Equal(80, mine.GetProperty("progressPercent").GetInt32());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("completedAt").ValueKind);
        Assert.Equal(code, mine.GetProperty("certificateCode").GetString());

        // Finishing the new lesson completes it again without a second certificate.
        await CompleteAsync(learner, added[0]);
        var again = await MineAsync(learner, course);
        Assert.Equal(100, again.GetProperty("progressPercent").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, again.GetProperty("completedAt").ValueKind);
        Assert.Equal(1, await api.WithDbAsync(db => db.Certificates.IgnoreQueryFilters().CountAsync(c => c.CourseId == course, Ct)));
    }

    [Fact]
    public async Task C3_Deleting_the_last_viewed_lesson_clears_resume()
    {
        var (tutor, _) = await TutorAsync("Resume");
        var course = await CreateCourseAsync(tutor, "Resumable", 0, isFree: true);
        var lessons = await AddTextLessonsAsync(tutor, course, 3);
        await PublishAsync(tutor, course);
        var learner = await LearnerAsync("Reader");
        (await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct)).EnsureSuccessStatusCode();
        await CompleteAsync(learner, lessons[0]);
        (await learner.PutAsJsonAsync($"/api/learn/lessons/{lessons[1]}/progress", new { positionSec = 0, completed = false }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(lessons[1], (await MineAsync(learner, course)).GetProperty("lastLessonId").GetGuid());

        (await tutor.DeleteAsync($"/api/studio/lessons/{lessons[1]}", Ct)).EnsureSuccessStatusCode();

        var mine = await MineAsync(learner, course);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("lastLessonId").ValueKind);
        var detail = await Json(await learner.GetAsync($"/api/learn/courses/{course}", Ct));
        Assert.Equal(lessons[2], detail.GetProperty("resumeLessonId").GetGuid()); // the first lesson not yet done
        Assert.Equal(HttpStatusCode.OK, (await learner.GetAsync($"/api/learn/lessons/{detail.GetProperty("resumeLessonId").GetGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task C4_A_coupon_works_all_day_on_its_end_date_in_Lagos()
    {
        var (tutor, tenantId) = await TutorAsync("Coupon");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "JAMB prep", 1_200_000);
        // What the studio's date input sends.
        var saved = await Json(await tutor.PostAsJsonAsync("/api/studio/coupons", new
        {
            code = "JAMB2026", courseId = (Guid?)null, discountType = "Percent", value = 10, maxUses = (int?)null,
            expiresAt = "2026-10-31", isActive = true,
        }, Ct));
        Assert.Equal("2026-10-31", saved.GetProperty("expiresAt").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(1)).ToString("yyyy-MM-dd"));

        var coupon = await api.WithDbAsync(db => db.CourseCoupons.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.TenantId == tenantId, Ct));
        var lagos = TimeSpan.FromHours(1);
        var (morning, morningMessage) = CourseRules.ApplyCoupon(coupon, course, 1_200_000, new DateTimeOffset(2026, 10, 31, 10, 0, 0, lagos));
        Assert.Null(morningMessage);
        Assert.Equal(120_000, morning);
        var (lateEvening, _) = CourseRules.ApplyCoupon(coupon, course, 1_200_000, new DateTimeOffset(2026, 10, 31, 23, 59, 0, lagos));
        Assert.Equal(120_000, lateEvening);
        var (_, nextDay) = CourseRules.ApplyCoupon(coupon, course, 1_200_000, new DateTimeOffset(2026, 11, 1, 0, 0, 1, lagos));
        Assert.Equal("That coupon has expired.", nextDay);
    }

    // Helpers

    private async Task<(HttpClient Client, Guid TenantId)> TutorAsync(string name)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Tutor", email = $"crs-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
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
        var response = await client.PostAsJsonAsync("/api/auth/learner/signup", new
        {
            fullName = $"{name} Learner", email = $"crs-{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
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

    /// <summary>Adds text lessons to the course's first section and returns their ids.</summary>
    private async Task<List<Guid>> AddTextLessonsAsync(HttpClient tutor, Guid course, int count)
    {
        var detail = await Json(await tutor.GetAsync($"/api/studio/courses/{course}", Ct));
        var section = detail.GetProperty("sections")[0];
        var before = section.GetProperty("lessons").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToHashSet();
        JsonElement built = default;
        for (var i = 0; i < count; i++)
        {
            var response = await tutor.PostAsJsonAsync($"/api/studio/sections/{section.GetProperty("id").GetGuid()}/lessons",
                new { title = $"Reading {before.Count + i + 1}", type = "Text" }, Ct);
            response.EnsureSuccessStatusCode();
            built = await Json(response);
        }
        return built.GetProperty("sections")[0].GetProperty("lessons").EnumerateArray().Select(l => l.GetProperty("id").GetGuid())
            .Where(id => !before.Contains(id)).ToList();
    }

    private async Task PublishAsync(HttpClient tutor, Guid course) =>
        (await tutor.PostAsync($"/api/studio/courses/{course}/publish", null, Ct)).EnsureSuccessStatusCode();

    private async Task BuyAsync(HttpClient learner, Guid course, long amount, string channel)
    {
        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course, channel }, Ct));
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, amount, "NGN", DateTimeOffset.UtcNow);
        (await learner.PostAsJsonAsync($"/api/learn/orders/{orderId}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
    }

    private async Task CompleteAsync(HttpClient learner, Guid lesson) =>
        (await learner.PutAsJsonAsync($"/api/learn/lessons/{lesson}/progress", new { positionSec = 0, completed = true }, Ct)).EnsureSuccessStatusCode();

    private async Task<JsonElement> MineAsync(HttpClient learner, Guid course) =>
        (await Json(await learner.GetAsync("/api/learn/enrollments", Ct))).EnumerateArray().Single(e => e.GetProperty("courseId").GetGuid() == course);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
