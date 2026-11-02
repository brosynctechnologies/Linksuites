using Saas.Application.Payouts;
using Saas.Domain.Payouts;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Api.Infrastructure;
using Saas.Application.Auth;
using Saas.Application.Common;
using Saas.Application.Courses;
using Saas.Domain.Core;
using Saas.Domain.Courses;
using Saas.Infrastructure.Auth;
using Saas.Infrastructure.Persistence;

namespace Saas.Api.Endpoints;

/// <summary>Online Course Platform: learner accounts, studio, catalogue, learning, admin and secure media.</summary>
public static partial class CourseEndpoints
{
    public static void MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        // Learner accounts (no business attached).
        var learnerAuth = app.MapGroup("/api/auth/learner").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
        learnerAuth.MapPost("/signup", (LearnerSignupRequest r, AuthService s, CancellationToken ct) => s.LearnerSignupAsync(r, ct))
            .Validate<LearnerSignupRequest>();
        learnerAuth.MapPost("/login", (LoginRequest r, AuthService s, CancellationToken ct) => s.LearnerLoginAsync(r, ct)).Validate<LoginRequest>();

        // Studio (tutor business).
        var studio = app.MapGroup("/api").RequireAuthorization(Policies.CoursesProduct);
        MemberRole[] owners = [MemberRole.Owner, MemberRole.Admin];
        MemberRole[] editors = [MemberRole.Owner, MemberRole.Admin, MemberRole.Staff];

        var academy = studio.MapGroup("/academy").WithTags("Courses: studio").WritableBy(owners);
        academy.MapGet("/", (StudioService s, CancellationToken ct) => s.GetAcademyAsync(ct));
        academy.MapPut("/", (UpdateAcademyRequest r, StudioService s, CancellationToken ct) => s.UpdateAcademyAsync(r, ct)).Validate<UpdateAcademyRequest>();
        academy.MapPost("/logo", (IFormFile file, StudioService s, CancellationToken ct) => s.UploadLogoAsync(file.ToUpload(), ct)).DisableAntiforgery();
        academy.MapPut("/payout-account", (PayoutAccountDto r, StudioService s, CancellationToken ct) => s.SetPayoutAccountAsync(r, ct))
            .Validate<PayoutAccountDto>().RequireAuthorization(Policies.Owner);

        var courses = studio.MapGroup("/studio").WithTags("Courses: studio").WritableBy(editors);
        courses.MapGet("/courses", (StudioService s, CancellationToken ct) => s.ListAsync(ct));
        courses.MapPost("/courses", (SaveCourseRequest r, StudioService s, CancellationToken ct) => s.SaveAsync(null, r, ct)).Validate<SaveCourseRequest>();
        courses.MapGet("/courses/{id:guid}", (Guid id, StudioService s, CancellationToken ct) => s.GetAsync(id, ct));
        courses.MapPut("/courses/{id:guid}", (Guid id, SaveCourseRequest r, StudioService s, CancellationToken ct) => s.SaveAsync(id, r, ct))
            .Validate<SaveCourseRequest>();
        courses.MapDelete("/courses/{id:guid}", async (Guid id, StudioService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        courses.MapPost("/courses/{id:guid}/thumbnail", (Guid id, IFormFile file, StudioService s, CancellationToken ct) =>
            s.UploadThumbnailAsync(id, file.ToUpload(), ct)).DisableAntiforgery();
        courses.MapPost("/courses/{id:guid}/publish", (Guid id, StudioService s, CancellationToken ct) => s.PublishAsync(id, true, ct));
        courses.MapPost("/courses/{id:guid}/unpublish", (Guid id, StudioService s, CancellationToken ct) => s.PublishAsync(id, false, ct));
        courses.MapPost("/courses/{id:guid}/submit-to-marketplace", (Guid id, StudioService s, CancellationToken ct) => s.SubmitToMarketplaceAsync(id, ct));
        courses.MapPost("/courses/{id:guid}/sections", (Guid id, TitleRequest r, StudioService s, CancellationToken ct) => s.AddSectionAsync(id, r, ct));
        courses.MapPut("/sections/{id:guid}", (Guid id, TitleRequest r, StudioService s, CancellationToken ct) => s.RenameSectionAsync(id, r, ct));
        courses.MapDelete("/sections/{id:guid}", (Guid id, StudioService s, CancellationToken ct) => s.DeleteSectionAsync(id, ct));
        courses.MapPost("/sections/{id:guid}/lessons", (Guid id, NewLessonRequest r, StudioService s, CancellationToken ct) => s.AddLessonAsync(id, r, ct));
        courses.MapPut("/courses/{id:guid}/order", (Guid id, ReorderRequest r, StudioService s, CancellationToken ct) => s.ReorderAsync(id, r, ct));
        courses.MapGet("/lessons/{id:guid}", (Guid id, StudioService s, CancellationToken ct) => s.GetLessonAsync(id, ct));
        courses.MapPut("/lessons/{id:guid}", (Guid id, SaveLessonRequest r, StudioService s, CancellationToken ct) => s.SaveLessonAsync(id, r, ct))
            .Validate<SaveLessonRequest>();
        courses.MapDelete("/lessons/{id:guid}", async (Guid id, StudioService s, CancellationToken ct) =>
        {
            await s.DeleteLessonAsync(id, ct);
            return Results.NoContent();
        });
        courses.MapPost("/lessons/{id:guid}/video", (Guid id, IFormFile file, StudioService s, CancellationToken ct) =>
            s.UploadVideoAsync(id, file.ToUpload(), ct)).DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(StudioService.MaxVideoBytes + 1024 * 1024))
            .WithFormOptions(multipartBodyLengthLimit: StudioService.MaxVideoBytes + 1024 * 1024);
        courses.MapGet("/lessons/{id:guid}/video", async (Guid id, StudioService s, CancellationToken ct) =>
            await s.VideoStatusAsync(id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        courses.MapPost("/lessons/{id:guid}/resources", (Guid id, IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? title, StudioService s,
            CancellationToken ct) => s.AddResourceAsync(id, file.ToUpload(), title, ct)).DisableAntiforgery();
        courses.MapDelete("/lessons/{id:guid}/resources/{resourceId:guid}", (Guid id, Guid resourceId, StudioService s, CancellationToken ct) =>
            s.DeleteResourceAsync(id, resourceId, ct));
        courses.MapPut("/lessons/{id:guid}/quiz", (Guid id, SaveQuizRequest r, StudioService s, CancellationToken ct) => s.SaveQuizAsync(id, r, ct))
            .Validate<SaveQuizRequest>();

        courses.MapGet("/dashboard", (StudioPeopleService s, CancellationToken ct) => s.DashboardAsync(ct));
        courses.MapGet("/students", (Guid? courseId, string? q, int? page, int? pageSize, StudioPeopleService s, CancellationToken ct) =>
            s.StudentsAsync(courseId, q, page ?? 1, pageSize ?? 50, ct));
        courses.MapGet("/questions", (Guid? courseId, bool? unanswered, StudioPeopleService s, CancellationToken ct) =>
            s.QuestionsAsync(courseId, unanswered ?? false, ct));
        courses.MapPost("/questions/{id:guid}/answers", (Guid id, BodyRequest r, StudioPeopleService s, CancellationToken ct) => s.AnswerAsync(id, r, ct))
            .Validate<BodyRequest>();
        courses.MapGet("/reviews", (Guid? courseId, StudioPeopleService s, CancellationToken ct) => s.ReviewsAsync(courseId, ct));
        courses.MapGet("/coupons", (StudioPeopleService s, CancellationToken ct) => s.CouponsAsync(ct));
        courses.MapPost("/coupons", (SaveCouponRequest r, StudioPeopleService s, CancellationToken ct) => s.SaveCouponAsync(null, r, ct)).Validate<SaveCouponRequest>();
        courses.MapPut("/coupons/{id:guid}", (Guid id, SaveCouponRequest r, StudioPeopleService s, CancellationToken ct) => s.SaveCouponAsync(id, r, ct))
            .Validate<SaveCouponRequest>();
        courses.MapDelete("/coupons/{id:guid}", async (Guid id, StudioPeopleService s, CancellationToken ct) =>
        {
            await s.DeleteCouponAsync(id, ct);
            return Results.NoContent();
        });
        // Earnings and payouts (which show the bank account) are for Owner and Admin.
        courses.MapGet("/earnings", (int? page, StudioPeopleService s, CancellationToken ct) => s.EarningsAsync(page ?? 1, 50, ct)).RequireAuthorization(Policies.Admin);
        courses.MapGet("/payouts", (StudioPeopleService s, CancellationToken ct) => s.PayoutsAsync(ct)).RequireAuthorization(Policies.Admin);
        courses.MapPost("/payouts", (PayoutRequest r, StudioPeopleService s, CancellationToken ct) => s.RequestPayoutAsync(r, ct))
            .RequireAuthorization(Policies.Owner);
        courses.MapPost("/orders/{id:guid}/refund", async (Guid id, StudioPeopleService s, CancellationToken ct) =>
        {
            await s.RefundAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);

        // Public catalogue.
        var catalog = app.MapGroup("/api/catalog").WithTags("Courses: catalogue");
        catalog.MapGet("/categories", (CourseCatalogService s, CancellationToken ct) => s.CategoriesAsync(ct));
        catalog.MapGet("/courses", (string? q, string? category, CourseLevel? level, string? price, string? sort, int? page, int? pageSize,
            CourseCatalogService s, CancellationToken ct) => s.SearchAsync(q, category, level, price, sort, page ?? 1, pageSize ?? 24, ct));
        catalog.MapGet("/academies/{slug}", (string slug, CourseCatalogService s, CancellationToken ct) => s.AcademyAsync(slug, ct));
        catalog.MapGet("/courses/{academy}/{course}", (string academy, string course, CourseCatalogService s, CancellationToken ct) =>
            s.CourseAsync(academy, course, ct));
        catalog.MapGet("/courses/{academy}/{course}/reviews", (string academy, string course, int? page, CourseCatalogService s, CancellationToken ct) =>
            s.ReviewsAsync(academy, course, page ?? 1, ct));
        catalog.MapGet("/lessons/{id:guid}/preview", (Guid id, CourseCatalogService s, CancellationToken ct) => s.PreviewAsync(id, ct));
        app.MapGet("/api/certificates/{code}", (string code, CourseCatalogService s, CancellationToken ct) => s.CertificateAsync(code, ct))
            .WithTags("Courses: catalogue");
        app.MapGet("/api/certificates/{code}/pdf", async (string code, CourseCatalogService s, CancellationToken ct) =>
        {
            var (pdf, name) = await s.CertificatePdfAsync(code, ct);
            return Results.File(pdf, "application/pdf", name);
        }).WithTags("Courses: catalogue");

        // Learning (any signed-in user; acts as that user).
        var learn = app.MapGroup("/api/learn").WithTags("Courses: learning").RequireAuthorization();
        learn.MapPost("/checkout/quote", (QuoteCourseRequest r, LearnService s, CancellationToken ct) => s.QuoteAsync(r, ct));
        learn.MapPost("/checkout", (CheckoutRequest r, LearnService s, CancellationToken ct) => s.CheckoutAsync(r, ct)).RequireRateLimiting(RateLimits.PublicWrite);
        learn.MapPost("/orders/{id:guid}/verify", (Guid id, VerifyOrderRequest r, LearnService s, CancellationToken ct) => s.VerifyAsync(id, r, ct));
        learn.MapGet("/enrollments", (LearnService s, CancellationToken ct) => s.MyCoursesAsync(ct));
        learn.MapGet("/courses/{id:guid}", (Guid id, LearnService s, CancellationToken ct) => s.CourseAsync(id, ct));
        learn.MapGet("/lessons/{id:guid}", (Guid id, LearnService s, CancellationToken ct) => s.LessonAsync(id, ct));
        learn.MapPut("/lessons/{id:guid}/progress", (Guid id, ProgressRequest r, LearnService s, CancellationToken ct) => s.SaveProgressAsync(id, r, ct));
        learn.MapGet("/lessons/{id:guid}/note", (Guid id, LearnService s, CancellationToken ct) => s.NoteAsync(id, ct));
        learn.MapPut("/lessons/{id:guid}/note", (Guid id, BodyRequest r, LearnService s, CancellationToken ct) => s.SaveNoteAsync(id, r, ct));
        learn.MapGet("/lessons/{id:guid}/questions", (Guid id, LearnService s, CancellationToken ct) => s.QuestionsAsync(id, ct));
        learn.MapPost("/lessons/{id:guid}/questions", (Guid id, BodyRequest r, LearnService s, CancellationToken ct) => s.AskAsync(id, r, ct))
            .Validate<BodyRequest>();
        learn.MapPost("/questions/{id:guid}/answers", (Guid id, BodyRequest r, LearnService s, CancellationToken ct) => s.ReplyAsync(id, r, ct))
            .Validate<BodyRequest>();
        learn.MapPost("/quizzes/{lessonId:guid}/attempts", (Guid lessonId, LearnService s, CancellationToken ct) => s.StartQuizAsync(lessonId, ct));
        learn.MapGet("/quizzes/{lessonId:guid}/attempts", (Guid lessonId, LearnService s, CancellationToken ct) => s.AttemptsAsync(lessonId, ct));
        learn.MapPost("/attempts/{id:guid}/submit", (Guid id, SubmitAttemptRequest r, LearnService s, CancellationToken ct) => s.SubmitQuizAsync(id, r, ct));
        learn.MapPost("/courses/{id:guid}/review", (Guid id, ReviewInput r, LearnService s, CancellationToken ct) => s.ReviewAsync(id, r, ct))
            .Validate<ReviewInput>();
        learn.MapPut("/courses/{id:guid}/review", (Guid id, ReviewInput r, LearnService s, CancellationToken ct) => s.ReviewAsync(id, r, ct))
            .Validate<ReviewInput>();
        learn.MapGet("/certificates", (LearnService s, CancellationToken ct) => s.CertificatesAsync(ct));

        // Platform admin.
        var admin = app.MapGroup("/api/admin").WithTags("Courses: admin").RequireAuthorization(Policies.PlatformSession, Policies.PlatformAdmin)
            .RequireRateLimiting(RateLimits.Admin);
        admin.MapGet("/overview", (CourseAdminService s, CancellationToken ct) => s.OverviewAsync(ct));
        admin.MapGet("/course-reviews", (CourseAdminService s, CancellationToken ct) => s.ReviewQueueAsync(ct)).RequireAuthorization(Policies.PlatformModerator);
        admin.MapPost("/course-reviews/{id:guid}", async (Guid id, ReviewDecision r, CourseAdminService s, CancellationToken ct) =>
        {
            await s.DecideAsync(id, r, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformModerator);
        admin.MapGet("/payouts", (PayoutStatus? status, PayoutAdminService s, CancellationToken ct) => s.ListAsync(status, ct)).RequireAuthorization(Policies.PlatformFinance);
        admin.MapPost("/payouts/{id:guid}/paid", (Guid id, PaidRequest r, PayoutAdminService s, CancellationToken ct) => s.MarkPaidAsync(id, r, ct)).RequireAuthorization(Policies.PlatformFinance);
        admin.MapPost("/payouts/{id:guid}/reject", (Guid id, NoteRequest r, PayoutAdminService s, CancellationToken ct) => s.RejectAsync(id, r.Note, ct)).RequireAuthorization(Policies.PlatformFinance);
        admin.MapGet("/categories", (CourseAdminService s, CancellationToken ct) => s.CategoriesAsync(ct)).RequireAuthorization(Policies.PlatformModerator);
        admin.MapPost("/categories", (SaveCategoryRequest r, CourseAdminService s, CancellationToken ct) => s.SaveCategoryAsync(null, r, ct)).RequireAuthorization(Policies.PlatformModerator);
        admin.MapPut("/categories/{id:guid}", (Guid id, SaveCategoryRequest r, CourseAdminService s, CancellationToken ct) => s.SaveCategoryAsync(id, r, ct)).RequireAuthorization(Policies.PlatformModerator);

        // Signed video files. The signature, viewer and expiry are in the path so HLS segment URLs stay relative.
        app.MapGet("/api/media/{video:guid}/{viewer:guid}/{expires:long}/{signature}/{**file}", async (Guid video, Guid viewer, long expires,
            string signature, string file, MediaSigner signer, AppDbContext db, IFileStorage storage, CancellationToken ct) =>
        {
            if (!signer.IsValid(video, viewer, expires, signature) || !SafeFile().IsMatch(file) || file.Contains(".."))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var v = await db.Videos.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == video && x.Status == VideoStatus.Ready, ct);
            if (v?.PlaybackKey is null) return Results.NotFound();
            var key = v.Format == "hls" ? $"{v.PlaybackKey}/{file}" : file == "video.mp4" ? v.PlaybackKey : null;
            if (key is null) return Results.NotFound();
            if (storage.TemporaryUrl(key, TimeSpan.FromMinutes(10)) is { } direct) return Results.Redirect(direct);
            var stream = await storage.OpenReadAsync(key, ct);
            return stream is null ? Results.NotFound() : Results.Stream(stream, ContentType(key), enableRangeProcessing: true);
        }).ExcludeFromDescription();
    }

    [GeneratedRegex("^[A-Za-z0-9_./-]{1,200}$")]
    private static partial Regex SafeFile();

    private static string ContentType(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".ts" => "video/mp2t",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        _ => "video/mp4",
    };
}
