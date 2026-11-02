using Saas.Application.Payouts;
using FluentValidation;
using Saas.Application.Common;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

// Academy

public sealed record AcademyDto(string Name, string Slug, string? Headline, string? Bio, string? LogoUrl, string BrandColor, string Currency,
    PayoutAccountDto? PayoutAccount, string AcademyUrl);
public sealed record UpdateAcademyRequest(string Name, string Slug, string? Headline, string? Bio, string BrandColor);

// Studio

public sealed record SaveCourseRequest(
    string Title, string? Subtitle, string? Description, Guid? CategoryId, CourseLevel Level, string? Language, long PriceMinor, bool IsFree,
    List<string>? Outcomes, List<string>? Requirements, bool ListOnMarketplace);

public sealed record LessonSummaryDto(Guid Id, string Title, LessonType Type, int Position, bool IsPreview, int DurationSec, string VideoStatus);
public sealed record SectionDto(Guid Id, string Title, int Position, IReadOnlyList<LessonSummaryDto> Lessons);

public sealed record StudioCourseSummaryDto(
    Guid Id, string Title, string Slug, string? ThumbnailUrl, CourseStatus Status, MarketplaceStatus MarketplaceStatus, long PriceMinor,
    bool IsFree, int LessonsCount, int StudentsCount, double RatingAverage, int RatingCount, long RevenueMinor, DateTimeOffset UpdatedAt);

public sealed record StudioCourseDto(
    Guid Id, string Title, string? Subtitle, string? Description, Guid? CategoryId, CourseLevel Level, string Language, long PriceMinor,
    bool IsFree, IReadOnlyList<string> Outcomes, IReadOnlyList<string> Requirements, bool ListOnMarketplace, string Slug, string? ThumbnailUrl,
    CourseStatus Status, MarketplaceStatus MarketplaceStatus, string? MarketplaceNote, DateTimeOffset? PublishedAt, string CourseUrl,
    IReadOnlyList<SectionDto> Sections);

public sealed record TitleRequest(string Title);
public sealed record NewLessonRequest(string Title, LessonType Type);
public sealed record OrderSection(Guid Id, List<Guid> LessonIds);
public sealed record ReorderRequest(List<OrderSection> Sections);
public sealed record SaveLessonRequest(string Title, LessonType Type, bool IsPreview, string? Content);

public sealed record VideoDto(VideoStatus Status, string? Format, int DurationSec, string? ThumbnailUrl, string? Error, IReadOnlyList<string> Renditions);
public sealed record ResourceDto(Guid Id, string Title, string FileName, long SizeBytes, string Url);
public sealed record QuizOptionDto(Guid? Id, string Text, bool IsCorrect);
public sealed record QuizQuestionDto(Guid? Id, QuestionType Type, string Prompt, int Points, string? Explanation, List<QuizOptionDto>? Options, List<string>? AcceptedAnswers);
public sealed record SaveQuizRequest(int? TimeLimitMinutes, int PassMarkPercent, int? MaxAttempts, bool Shuffle, List<QuizQuestionDto> Questions);

public sealed record StudioLessonDto(
    Guid Id, string Title, LessonType Type, bool IsPreview, string? Content, Guid SectionId, Guid CourseId, int DurationSec,
    VideoDto? Video, IReadOnlyList<ResourceDto> Resources, SaveQuizRequest? Quiz);

public sealed record RecentEnrolmentDto(string StudentName, string CourseTitle, DateTimeOffset EnrolledAt, long AmountMinor, SaleChannel? Channel);
public sealed record StudioDashboardDto(string Currency, int StudentsTotal, int EnrolmentsThisMonth, long RevenueThisMonthMinor, long AvailableMinor,
    long PendingMinor, double RatingAverage, IReadOnlyList<RecentEnrolmentDto> RecentEnrolments);
public sealed record StudentDto(Guid EnrolmentId, Guid UserId, string Name, string Email, Guid CourseId, string CourseTitle, DateTimeOffset EnrolledAt,
    int ProgressPercent, DateTimeOffset? CompletedAt, EnrollmentSource Source, SaleChannel? Channel);

public sealed record AnswerDto(Guid Id, string AuthorName, bool IsTutor, string Body, DateTimeOffset CreatedAt);
public sealed record QuestionDto(Guid Id, Guid CourseId, string CourseTitle, Guid LessonId, string LessonTitle, string AuthorName, string Body,
    DateTimeOffset CreatedAt, IReadOnlyList<AnswerDto> Answers);
public sealed record BodyRequest(string Body);
public sealed record StudioReviewDto(Guid Id, string CourseTitle, string StudentName, int Rating, string? Comment, DateTimeOffset CreatedAt);

public sealed record CouponDto(Guid Id, string Code, Guid? CourseId, CouponDiscountType DiscountType, long Value, int? MaxUses, int Uses,
    DateTimeOffset? ExpiresAt, bool IsActive);
/// <summary>ExpiresAt is read as a calendar date (its date in the offset sent, e.g. "2026-10-31") and stored as the end of that day in Africa/Lagos.</summary>
public sealed record SaveCouponRequest(string Code, Guid? CourseId, CouponDiscountType DiscountType, long Value, int? MaxUses, DateTimeOffset? ExpiresAt, bool IsActive);

public sealed record EarningDto(Guid Id, Guid OrderId, string CourseTitle, string StudentName, SaleChannel Channel, long GrossMinor, long FeeMinor,
    long NetMinor, DateTimeOffset AvailableAt, DateTimeOffset CreatedAt, string Status, bool Refundable);
public sealed record EarningsDto(string Currency, long AvailableMinor, long PendingMinor, long PaidOutMinor, Paged<EarningDto> Entries);

// Catalogue

public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId);
public sealed record CourseCardDto(Guid Id, string Slug, string AcademySlug, string AcademyName, string Title, string? Subtitle, string? ThumbnailUrl,
    long PriceMinor, string Currency, bool IsFree, CourseLevel Level, double RatingAverage, int RatingCount, int StudentsCount, int LessonsCount,
    int DurationSec, string? CategoryName);
public sealed record AcademyPageDto(string Name, string Slug, string? Headline, string? Bio, string? LogoUrl, string BrandColor, bool ShowBranding,
    IReadOnlyList<CourseCardDto> Courses);
public sealed record TutorDto(string Name, string? Headline, string? Bio, string? LogoUrl);
public sealed record PublicLessonDto(Guid Id, string Title, LessonType Type, int DurationSec, bool IsPreview);
public sealed record PublicSectionDto(Guid Id, string Title, IReadOnlyList<PublicLessonDto> Lessons);
public sealed record CourseDetailDto(Guid Id, string Slug, string AcademySlug, string AcademyName, string Title, string? Subtitle, string? ThumbnailUrl,
    long PriceMinor, string Currency, bool IsFree, CourseLevel Level, double RatingAverage, int RatingCount, int StudentsCount, int LessonsCount,
    int DurationSec, string? CategoryName, string? Description, string Language, IReadOnlyList<string> Outcomes, IReadOnlyList<string> Requirements,
    DateTimeOffset UpdatedAt, TutorDto Tutor, string BrandColor, IReadOnlyList<PublicSectionDto> Sections, bool Enrolled);
public sealed record PublicReviewDto(string StudentName, int Rating, string? Comment, DateTimeOffset CreatedAt);
public sealed record CertificateDto(string Code, string StudentName, string CourseTitle, string TutorName, string AcademyName, DateTimeOffset IssuedAt);

// Learning

public sealed record CheckoutRequest(Guid CourseId, string? CouponCode, SaleChannel? Channel);
public sealed record QuoteCourseRequest(Guid CourseId, string? CouponCode);
public sealed record CheckoutResultDto(bool Enrolled, string? AuthorizationUrl, Guid? OrderId, long PriceMinor, long DiscountMinor, long TotalMinor);
public sealed record CourseQuoteDto(long PriceMinor, long DiscountMinor, long TotalMinor, string? CouponMessage);
public sealed record VerifyOrderRequest(string Reference);
public sealed record VerifyOrderResult(bool Enrolled, string CourseUrl);

public sealed record MyCourseDto(Guid CourseId, string Slug, string AcademySlug, string Title, string? ThumbnailUrl, string TutorName, int ProgressPercent,
    int LessonsDone, int LessonsTotal, Guid? LastLessonId, DateTimeOffset? CompletedAt, string? CertificateCode);
public sealed record LearnLessonDto(Guid Id, string Title, LessonType Type, int DurationSec, bool Completed, bool IsQuiz, bool Passed);
public sealed record LearnSectionDto(Guid Id, string Title, IReadOnlyList<LearnLessonDto> Lessons);
public sealed record LearnCourseDto(Guid CourseId, string Title, string AcademySlug, string Slug, string TutorName, int ProgressPercent,
    string? CertificateCode, string BrandColor, IReadOnlyList<LearnSectionDto> Sections, Guid? ResumeLessonId);

public sealed record PlaybackVideoDto(VideoStatus Status, string? Format, string? Url, string? ThumbnailUrl);
public sealed record PlaybackResourceDto(string Title, string Url, long SizeBytes);
public sealed record PlaybackQuizDto(int QuestionsCount, int? TimeLimitMinutes, int PassMarkPercent, int? MaxAttempts, int AttemptsUsed,
    int? BestScorePercent, bool Passed);
public sealed record LessonPlaybackDto(Guid Id, Guid CourseId, string Title, LessonType Type, string? Content, int DurationSec,
    PlaybackVideoDto? Video, IReadOnlyList<PlaybackResourceDto> Resources, PlaybackQuizDto? Quiz, int PositionSec, bool Completed,
    Guid? PreviousLessonId, Guid? NextLessonId, string? Watermark);

public sealed record ProgressRequest(int PositionSec, bool Completed);
public sealed record ProgressResult(int ProgressPercent, bool CourseCompleted, string? CertificateCode);
public sealed record NoteDto(string Body, DateTimeOffset? UpdatedAt);

public sealed record AttemptOptionDto(Guid Id, string Text);
public sealed record AttemptQuestionDto(Guid Id, QuestionType Type, string Prompt, int Points, IReadOnlyList<AttemptOptionDto> Options);
public sealed record StartedAttemptDto(Guid AttemptId, DateTimeOffset StartedAt, DateTimeOffset? EndsAt, IReadOnlyList<AttemptQuestionDto> Questions);
public sealed record SubmittedAnswer(Guid QuestionId, List<Guid>? OptionIds, string? Text);
public sealed record SubmitAttemptRequest(List<SubmittedAnswer> Answers);
public sealed record QuestionResultDto(Guid Id, string Prompt, bool Correct, string? Explanation, IReadOnlyList<Guid> YourOptionIds, string? YourText,
    IReadOnlyList<Guid> CorrectOptionIds);
public sealed record QuizResultDto(Guid AttemptId, int ScorePercent, bool Passed, DateTimeOffset? SubmittedAt, IReadOnlyList<QuestionResultDto> Questions);

public sealed record ReviewInput(int Rating, string? Comment);
public sealed record MyCertificateDto(string Code, string CourseTitle, DateTimeOffset IssuedAt, string PdfUrl, string VerifyUrl);

// Admin

public sealed record CourseReviewItemDto(Guid CourseId, string Title, string AcademyName, DateTimeOffset? SubmittedAt, int LessonsCount, long PriceMinor, string CourseUrl);
public sealed record ReviewDecision(bool Approve, string? Note);
public sealed record NoteRequest(string? Note);
public sealed record SaveCategoryRequest(string Name, string? Slug, Guid? ParentId);
public sealed record AdminOverviewDto(int Academies, int Courses, int Learners, long SalesThisMonthMinor, long PlatformFeesThisMonthMinor, int PendingReviews, int PendingPayouts);

// Validators

public sealed class UpdateAcademyRequestValidator : AbstractValidator<UpdateAcademyRequest>
{
    public UpdateAcademyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Slug).NotEmpty().Must(Slugs.IsWellFormed).WithMessage("Use 3-60 lowercase letters, numbers and dashes (some words are reserved).");
        RuleFor(x => x.Headline).MaximumLength(160);
        RuleFor(x => x.Bio).MaximumLength(2000);
        RuleFor(x => x.BrandColor).Matches("^#[0-9a-fA-F]{6}$").WithMessage("Use a hex colour such as #0B6E6E.");
    }
}

public sealed class SaveCourseRequestValidator : AbstractValidator<SaveCourseRequest>
{
    public SaveCourseRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Subtitle).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(20_000);
        RuleFor(x => x.Level).IsInEnum();
        RuleFor(x => x.Language).MaximumLength(40);
        RuleFor(x => x.PriceMinor).InclusiveBetween(0, 100_000_000_000);
        RuleFor(x => x.PriceMinor).GreaterThanOrEqualTo(10_000).When(x => !x.IsFree).WithMessage("Paid courses cost at least ₦100.");
        RuleFor(x => x.Outcomes).Must(o => o is null || (o.Count <= 20 && o.All(s => s.Length <= 200))).WithMessage("Use up to 20 short outcomes.");
        RuleFor(x => x.Requirements).Must(o => o is null || (o.Count <= 20 && o.All(s => s.Length <= 200))).WithMessage("Use up to 20 short requirements.");
    }
}

public sealed class SaveLessonRequestValidator : AbstractValidator<SaveLessonRequest>
{
    public SaveLessonRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Content).MaximumLength(100_000);
    }
}

public sealed class SaveQuizRequestValidator : AbstractValidator<SaveQuizRequest>
{
    public SaveQuizRequestValidator()
    {
        RuleFor(x => x.TimeLimitMinutes).InclusiveBetween(1, 600);
        RuleFor(x => x.PassMarkPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.MaxAttempts).InclusiveBetween(1, 100);
        RuleFor(x => x.Questions).NotEmpty().WithMessage("Add at least one question.").Must(q => q.Count <= 200);
        RuleForEach(x => x.Questions).ChildRules(q =>
        {
            q.RuleFor(x => x.Prompt).NotEmpty().MaximumLength(2000);
            q.RuleFor(x => x.Points).InclusiveBetween(0, 100);
            q.RuleFor(x => x.Explanation).MaximumLength(2000);
            q.RuleFor(x => x).Must(HasAnswer).WithName("questions").WithMessage("Each question needs a correct answer.");
        });
    }

    private static bool HasAnswer(QuizQuestionDto q) => q.Type switch
    {
        QuestionType.ShortAnswer => q.AcceptedAnswers?.Any(a => !string.IsNullOrWhiteSpace(a)) == true,
        QuestionType.SingleChoice or QuestionType.TrueFalse => (q.Options?.Count(o => o.IsCorrect) ?? 0) == 1 && q.Options!.Count >= 2,
        _ => (q.Options?.Count(o => o.IsCorrect) ?? 0) >= 1 && q.Options!.Count >= 2,
    };
}

public sealed class SaveCouponRequestValidator : AbstractValidator<SaveCouponRequest>
{
    public SaveCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$").WithMessage("Use letters, numbers, dashes or underscores.");
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.Value).InclusiveBetween(1, 100).When(x => x.DiscountType == CouponDiscountType.Percent);
        RuleFor(x => x.Value).GreaterThan(0).When(x => x.DiscountType == CouponDiscountType.Fixed);
        RuleFor(x => x.MaxUses).GreaterThan(0);
    }
}

public sealed class ReviewInputValidator : AbstractValidator<ReviewInput>
{
    public ReviewInputValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed class BodyRequestValidator : AbstractValidator<BodyRequest>
{
    public BodyRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(5000);
}
