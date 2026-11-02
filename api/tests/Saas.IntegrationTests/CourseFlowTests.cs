using Saas.Domain.Payouts;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Infrastructure.Courses;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class CourseFlowTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tutor_builds_learner_buys_watches_passes_quiz_and_gets_certificate()
    {
        var (tutor, tenantId) = await TutorAsync("Ada");
        await api.MakeProAsync(tenantId, Products.Courses, "pro");
        var course = await CreateCourseAsync(tutor, "Excel for accountants", 1_000_000);
        var (videoLesson, textLesson, quizLesson, correctOption) = await BuildCurriculumAsync(tutor, course);
        await PublishAsync(tutor, course);

        var learner = await LearnerAsync("Bola");
        var studio = await learner.GetAsync("/api/studio/courses", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, studio.StatusCode); // learner tokens can't use the studio

        // Before enrolling: no lessons.
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync($"/api/learn/lessons/{videoLesson}", Ct)).StatusCode);

        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));
        Assert.False(checkout.GetProperty("enrolled").GetBoolean());
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, 1_000_000, "NGN", DateTimeOffset.UtcNow);
        var verified = await Json(await learner.PostAsJsonAsync($"/api/learn/orders/{orderId}/verify", new { reference = init.Reference }, Ct));
        Assert.True(verified.GetProperty("enrolled").GetBoolean());

        // Not in the marketplace, so it's an academy sale: Pro plan takes no fee.
        var earning = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().SingleAsync(e => e.TenantId == tenantId, Ct));
        Assert.Equal((0L, 1_000_000L), (earning.FeeMinor, earning.NetMinor));

        // Signed video link works; a tampered one doesn't.
        var lesson = await Json(await learner.GetAsync($"/api/learn/lessons/{videoLesson}", Ct));
        Assert.Equal("Ready", lesson.GetProperty("video").GetProperty("status").GetString());
        Assert.Equal("mp4", lesson.GetProperty("video").GetProperty("format").GetString());
        Assert.Equal(nextOf(lesson), textLesson);
        var url = new Uri(lesson.GetProperty("video").GetProperty("url").GetString()!);
        var media = await api.CreateClient().GetAsync(url.PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal(FakeVideo, await media.Content.ReadAsByteArrayAsync(Ct));
        var tampered = url.PathAndQuery.Replace("/video.mp4", "").Split('/');
        tampered[^1] = new string('A', tampered[^1].Length);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.CreateClient().GetAsync(string.Join('/', tampered) + "/video.mp4", Ct)).StatusCode);

        // Progress, then the quiz: a wrong answer fails, the right one passes and finishes the course.
        await learner.PutAsJsonAsync($"/api/learn/lessons/{videoLesson}/progress", new { positionSec = 30, completed = true }, Ct);
        var afterText = await Json(await learner.PutAsJsonAsync($"/api/learn/lessons/{textLesson}/progress", new { positionSec = 0, completed = true }, Ct));
        Assert.Equal(66, afterText.GetProperty("progressPercent").GetInt32());

        var attempt = await Json(await learner.PostAsync($"/api/learn/quizzes/{quizLesson}/attempts", null, Ct));
        var question = attempt.GetProperty("questions")[0];
        var wrongOption = question.GetProperty("options").EnumerateArray().First(o => o.GetProperty("id").GetGuid() != correctOption).GetProperty("id").GetGuid();
        var failed = await Json(await learner.PostAsJsonAsync($"/api/learn/attempts/{attempt.GetProperty("attemptId").GetGuid()}/submit",
            new { answers = new[] { new { questionId = question.GetProperty("id").GetGuid(), optionIds = new[] { wrongOption } } } }, Ct));
        Assert.False(failed.GetProperty("passed").GetBoolean());
        Assert.Equal(correctOption, failed.GetProperty("questions")[0].GetProperty("correctOptionIds")[0].GetGuid());

        var second = await Json(await learner.PostAsync($"/api/learn/quizzes/{quizLesson}/attempts", null, Ct));
        var passed = await Json(await learner.PostAsJsonAsync($"/api/learn/attempts/{second.GetProperty("attemptId").GetGuid()}/submit",
            new { answers = new[] { new { questionId = question.GetProperty("id").GetGuid(), optionIds = new[] { correctOption } } } }, Ct));
        Assert.True(passed.GetProperty("passed").GetBoolean());

        var mine = (await Json(await learner.GetAsync("/api/learn/enrollments", Ct)))[0];
        Assert.Equal(100, mine.GetProperty("progressPercent").GetInt32());
        var code = mine.GetProperty("certificateCode").GetString()!;
        var verify = await Json(await api.CreateClient().GetAsync($"/api/certificates/{code}", Ct));
        Assert.Equal("Bola Learner", verify.GetProperty("studentName").GetString());
        var pdf = await api.CreateClient().GetAsync($"/api/certificates/{code}/pdf", Ct);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        // Review updates the course rating.
        await learner.PostAsJsonAsync($"/api/learn/courses/{course}/review", new { rating = 4, comment = "Clear and practical" }, Ct);
        var summary = (await Json(await tutor.GetAsync("/api/studio/courses", Ct)))[0];
        Assert.Equal(4.0, summary.GetProperty("ratingAverage").GetDouble());
        Assert.Equal(1, summary.GetProperty("studentsCount").GetInt32());

        static Guid nextOf(JsonElement l) => l.GetProperty("nextLessonId").GetGuid();
    }

    [Fact]
    public async Task Marketplace_needs_admin_approval_and_charges_the_marketplace_fee()
    {
        var (tutor, tenantId) = await TutorAsync("Market");
        var course = await CreateCourseAsync(tutor, "JAMB physics crash course", 2_000_000);
        await BuildCurriculumAsync(tutor, course);
        await PublishAsync(tutor, course);

        // Needs a thumbnail, description and category before review.
        Assert.Equal(HttpStatusCode.Conflict, (await tutor.PostAsync($"/api/studio/courses/{course}/submit-to-marketplace", null, Ct)).StatusCode);
        var category = (await Json(await api.CreateClient().GetAsync("/api/catalog/categories", Ct)))[0].GetProperty("id").GetGuid();
        (await tutor.PutAsJsonAsync($"/api/studio/courses/{course}", new
        {
            title = "JAMB physics crash course", description = "Everything for the exam.", categoryId = category, level = "Beginner",
            priceMinor = 2_000_000, isFree = false, listOnMarketplace = true,
        }, Ct)).EnsureSuccessStatusCode();
        using (var thumb = new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "thumb.png" } })
            (await tutor.PostAsync($"/api/studio/courses/{course}/thumbnail", thumb, Ct)).EnsureSuccessStatusCode();
        var submitted = await Json(await tutor.PostAsync($"/api/studio/courses/{course}/submit-to-marketplace", null, Ct));
        Assert.Equal("InReview", submitted.GetProperty("marketplaceStatus").GetString());

        var anon = api.CreateClient();
        Assert.Equal(0, (await Json(await anon.GetAsync("/api/catalog/courses?q=JAMB physics", Ct))).GetProperty("total").GetInt32());

        var learner = await LearnerAsync("Nobody");
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync("/api/admin/course-reviews", Ct)).StatusCode);
        await LearnerAsync("Admin", ApiFixture.AdminEmail);
        var admin = (await PlatformAuthTests.PanelLoginAsync(api, ApiFixture.AdminEmail, "password123")).Client;
        Assert.Contains((await Json(await admin.GetAsync("/api/admin/course-reviews", Ct))).EnumerateArray(), c => c.GetProperty("courseId").GetGuid() == course);
        (await admin.PostAsJsonAsync($"/api/admin/course-reviews/{course}", new { approve = true }, Ct)).EnsureSuccessStatusCode();

        var found = await Json(await anon.GetAsync("/api/catalog/courses?q=JAMB physics", Ct));
        Assert.Equal(1, found.GetProperty("total").GetInt32());

        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course, channel = "Marketplace" }, Ct));
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, 2_000_000, "NGN", DateTimeOffset.UtcNow);
        await learner.PostAsJsonAsync($"/api/learn/orders/{checkout.GetProperty("orderId").GetGuid()}/verify", new { reference = init.Reference }, Ct);
        var earning = await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().SingleAsync(e => e.TenantId == tenantId, Ct));
        Assert.Equal((30, 600_000L, 1_400_000L), (earning.FeePercent, earning.FeeMinor, earning.NetMinor));
    }

    [Fact]
    public async Task Earnings_unlock_after_the_refund_window_and_payouts_are_approved_by_the_platform()
    {
        var (tutor, tenantId) = await TutorAsync("Payout");
        var course = await CreateCourseAsync(tutor, "Payout course", 1_000_000);
        await BuildCurriculumAsync(tutor, course);
        await PublishAsync(tutor, course);
        var learner = await LearnerAsync("Buyer");
        await BuyAsync(learner, course, 1_000_000);

        var earnings = await Json(await tutor.GetAsync("/api/studio/earnings", Ct));
        Assert.Equal(900_000, earnings.GetProperty("pendingMinor").GetInt64()); // Free plan: 10% academy fee
        Assert.Equal(0, earnings.GetProperty("availableMinor").GetInt64());
        Assert.False((await tutor.PostAsJsonAsync("/api/studio/payouts", new { amountMinor = 500_000 }, Ct)).IsSuccessStatusCode); // still pending

        await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AvailableAt, DateTimeOffset.UtcNow.AddDays(-1)), Ct));
        Assert.Equal(HttpStatusCode.Conflict, (await tutor.PostAsJsonAsync("/api/studio/payouts", new { amountMinor = 500_000 }, Ct)).StatusCode); // no bank account
        (await tutor.PutAsJsonAsync("/api/academy/payout-account", new { bankName = "GTBank", accountNumber = "0123456789", accountName = "Payout Owner" }, Ct))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await tutor.PostAsJsonAsync("/api/studio/payouts", new { amountMinor = 950_000 }, Ct)).StatusCode);
        var payout = await Json(await tutor.PostAsJsonAsync("/api/studio/payouts", new { amountMinor = 600_000 }, Ct));
        Assert.Equal(300_000, (await Json(await tutor.GetAsync("/api/studio/earnings", Ct))).GetProperty("availableMinor").GetInt64());

        await LearnerAsync("Admin", ApiFixture.AdminEmail);
        var admin = (await PlatformAuthTests.PanelLoginAsync(api, ApiFixture.AdminEmail, "password123")).Client;
        var queue = await Json(await admin.GetAsync("/api/admin/payouts?status=Requested", Ct));
        Assert.Contains(queue.EnumerateArray(), p => p.GetProperty("id").GetGuid() == payout.GetProperty("id").GetGuid());
        var paid = await Json(await admin.PostAsJsonAsync($"/api/admin/payouts/{payout.GetProperty("id").GetGuid()}/paid", new { reference = "TRF-778" }, Ct));
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        var after = await Json(await tutor.GetAsync("/api/studio/earnings", Ct));
        Assert.Equal(600_000, after.GetProperty("paidOutMinor").GetInt64());
        Assert.Equal(300_000, after.GetProperty("availableMinor").GetInt64());
    }

    [Fact]
    public async Task Refund_inside_the_window_removes_access_and_the_earning()
    {
        var (tutor, tenantId) = await TutorAsync("Refund");
        var course = await CreateCourseAsync(tutor, "Refund course", 500_000);
        var (videoLesson, _, _, _) = await BuildCurriculumAsync(tutor, course);
        await PublishAsync(tutor, course);
        var learner = await LearnerAsync("Unhappy");
        var orderId = await BuyAsync(learner, course, 500_000);

        (await tutor.PostAsync($"/api/studio/orders/{orderId}/refund", null, Ct)).EnsureSuccessStatusCode();

        Assert.Contains(api.Gateway.Refunds, r => r.AmountMinor == 500_000);
        Assert.Equal(HttpStatusCode.Forbidden, (await learner.GetAsync($"/api/learn/lessons/{videoLesson}", Ct)).StatusCode);
        Assert.True(await api.WithDbAsync(db => db.Earnings.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).AllAsync(e => e.Reversed, Ct)));
        Assert.Equal(0, (await Json(await tutor.GetAsync("/api/studio/earnings", Ct))).GetProperty("pendingMinor").GetInt64());
    }

    [Fact]
    public async Task Free_plan_limits_courses_and_free_courses_enrol_at_once()
    {
        var (tutor, _) = await TutorAsync("Limits");
        var free = await CreateCourseAsync(tutor, "Free intro", 0, isFree: true);
        await CreateCourseAsync(tutor, "Second", 100_000);
        var third = await tutor.PostAsJsonAsync("/api/studio/courses", NewCourse("Third", 100_000), Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, third.StatusCode);

        await BuildCurriculumAsync(tutor, free);
        await PublishAsync(tutor, free);
        var learner = await LearnerAsync("Freebie");
        var result = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = free }, Ct));
        Assert.True(result.GetProperty("enrolled").GetBoolean());
        var me = await Json(await learner.GetAsync("/api/me", Ct));
        Assert.Equal(JsonValueKind.Null, me.GetProperty("tenant").ValueKind);
    }

    [Fact]
    public void Hls_ladder_never_upscales()
    {
        Assert.Equal(["240p", "480p"], FfmpegCommands.For(540).Select(r => r.Name));
        Assert.Equal(["240p"], FfmpegCommands.For(144).Select(r => r.Name));
        Assert.Equal(4, FfmpegCommands.For(2160).Count);
        Assert.Contains("BANDWIDTH=996000,RESOLUTION=854x480", FfmpegCommands.Master(FfmpegCommands.For(480)));
    }

    // Helpers

    private static readonly byte[] FakeVideo = [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6d, 0x70, 0x34, 0x32, 1, 2, 3, 4];
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private async Task<(HttpClient Client, Guid TenantId)> TutorAsync(string name)
    {
        var client = api.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Tutor", email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com", password = "password123",
            country = "NG", product = "courses", businessName = $"{name} Academy",
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private async Task<HttpClient> LearnerAsync(string name, string? email = null)
    {
        var client = api.CreateClient();
        email ??= $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/learner/signup", new { fullName = $"{name} Learner", email, password = "password123" }, Ct);
        if (response.StatusCode == HttpStatusCode.BadRequest) // the admin account already exists from another test
            response = await client.PostAsJsonAsync("/api/auth/learner/login", new { email, password = "password123" }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await Json(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    private static object NewCourse(string title, long price, bool isFree = false) =>
        new { title, priceMinor = price, isFree, level = "AllLevels", listOnMarketplace = false };

    private async Task<Guid> CreateCourseAsync(HttpClient tutor, string title, long price, bool isFree = false)
    {
        var response = await tutor.PostAsJsonAsync("/api/studio/courses", NewCourse(title, price, isFree), Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    /// <summary>A video lesson (processed), a text lesson and a one-question quiz.</summary>
    private async Task<(Guid Video, Guid Text, Guid Quiz, Guid CorrectOption)> BuildCurriculumAsync(HttpClient tutor, Guid course)
    {
        var detail = await Json(await tutor.GetAsync($"/api/studio/courses/{course}", Ct));
        var section = detail.GetProperty("sections")[0].GetProperty("id").GetGuid();
        await tutor.PostAsJsonAsync($"/api/studio/sections/{section}/lessons", new { title = "Welcome video", type = "Video" }, Ct);
        await tutor.PostAsJsonAsync($"/api/studio/sections/{section}/lessons", new { title = "Reading", type = "Text" }, Ct);
        var built = await Json(await tutor.PostAsJsonAsync($"/api/studio/sections/{section}/lessons", new { title = "Check yourself", type = "Quiz" }, Ct));
        var lessons = built.GetProperty("sections")[0].GetProperty("lessons").EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToList();

        using (var video = new MultipartFormDataContent { { new ByteArrayContent(FakeVideo), "file", "welcome.mp4" } })
            (await tutor.PostAsync($"/api/studio/lessons/{lessons[0]}/video", video, Ct)).EnsureSuccessStatusCode();
        using (var scope = api.Services.CreateScope())
        {
            var processor = scope.ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<VideoProcessor>().FirstOrDefault()
                ?? ActivatorUtilities.CreateInstance<VideoProcessor>(scope.ServiceProvider);
            while (await processor.ProcessNextAsync(Ct)) { }
        }
        (await tutor.PutAsJsonAsync($"/api/studio/lessons/{lessons[1]}", new { title = "Reading", type = "Text", isPreview = false, content = "# Hello" }, Ct))
            .EnsureSuccessStatusCode();
        var correct = Guid.NewGuid();
        var quiz = await tutor.PutAsJsonAsync($"/api/studio/lessons/{lessons[2]}/quiz", new
        {
            passMarkPercent = 100, shuffle = false,
            questions = new[]
            {
                new
                {
                    type = "SingleChoice", prompt = "What does =SUM(A1:A3) do?", points = 1, explanation = "It adds the three cells.",
                    options = new[] { new { id = (Guid?)correct, text = "Adds A1 to A3", isCorrect = true }, new { id = (Guid?)null, text = "Counts them", isCorrect = false } },
                },
            },
        }, Ct);
        quiz.EnsureSuccessStatusCode();
        return (lessons[0], lessons[1], lessons[2], correct);
    }

    private async Task PublishAsync(HttpClient tutor, Guid course) =>
        (await tutor.PostAsync($"/api/studio/courses/{course}/publish", null, Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> BuyAsync(HttpClient learner, Guid course, long amount)
    {
        var checkout = await Json(await learner.PostAsJsonAsync("/api/learn/checkout", new { courseId = course }, Ct));
        var orderId = checkout.GetProperty("orderId").GetGuid();
        var init = api.Gateway.Initialized.Single(i => checkout.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Gateway.Charges[init.Reference] = new VerifiedCharge(init.Reference, amount, "NGN", DateTimeOffset.UtcNow);
        (await learner.PostAsJsonAsync($"/api/learn/orders/{orderId}/verify", new { reference = init.Reference }, Ct)).EnsureSuccessStatusCode();
        return orderId;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
