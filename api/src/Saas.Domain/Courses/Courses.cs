using Saas.Domain.Common;
using Saas.Domain.Payouts;

namespace Saas.Domain.Courses;

/// <summary>A tutor business's academy: its public page, branding and payout account.</summary>
[Audited]
public class Academy : TenantEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Headline { get; set; }
    public string? Bio { get; set; }
    public string? LogoUrl { get; set; }
    public string BrandColor { get; set; } = "#0B6E6E";
    public string Currency { get; set; } = "NGN";
    public PayoutAccount? PayoutAccount { get; set; }
}

/// <summary>Marketplace categories, shared by all academies (not tenant-owned).</summary>
public class CourseCategory : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public Guid? ParentId { get; set; }
    public int Position { get; set; }
}

[Audited]
public class Course : TenantEntity
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public CourseLevel Level { get; set; } = CourseLevel.AllLevels;
    public string Language { get; set; } = "English";
    public string? ThumbnailUrl { get; set; }
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "NGN";
    public bool IsFree { get; set; }
    public List<string> Outcomes { get; set; } = [];
    public List<string> Requirements { get; set; } = [];
    public CourseStatus Status { get; set; } = CourseStatus.Draft;
    public bool ListOnMarketplace { get; set; }
    public MarketplaceStatus MarketplaceStatus { get; set; } = MarketplaceStatus.NotListed;
    public string? MarketplaceNote { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }
    public int StudentsCount { get; set; }
    public List<CourseSection> Sections { get; set; } = [];
}

public enum CourseLevel { Beginner, Intermediate, Advanced, AllLevels }
public enum CourseStatus { Draft, Published, Archived }
public enum MarketplaceStatus { NotListed, InReview, Approved, Rejected }

public class CourseSection : TenantEntity
{
    public Guid CourseId { get; set; }
    public required string Title { get; set; }
    public int Position { get; set; }
    public List<Lesson> Lessons { get; set; } = [];
}

[Audited]
public class Lesson : TenantEntity
{
    public Guid CourseId { get; set; }
    public Guid SectionId { get; set; }
    public required string Title { get; set; }
    public LessonType Type { get; set; }
    public int Position { get; set; }
    public bool IsPreview { get; set; }
    /// <summary>Markdown: the lesson body for text lessons, notes under video lessons.</summary>
    public string? Content { get; set; }
    public int DurationSec { get; set; }
    public QuizDefinition? Quiz { get; set; }
}

public enum LessonType { Video, Text, File, Quiz }

public class Video : TenantEntity
{
    public Guid LessonId { get; set; }
    public VideoStatus Status { get; set; } = VideoStatus.Processing;
    /// <summary>"hls" when transcoded, "mp4" when served as uploaded (no FFmpeg on the server).</summary>
    public string? Format { get; set; }
    public required string SourceKey { get; set; }
    public long SourceSizeBytes { get; set; }
    /// <summary>Storage prefix of the playable files (master.m3u8 + renditions, or the mp4).</summary>
    public string? PlaybackKey { get; set; }
    public List<string> Renditions { get; set; } = [];
    public int DurationSec { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
}

public enum VideoStatus { Processing, Ready, Failed }

public class LessonResource : TenantEntity
{
    public Guid LessonId { get; set; }
    public required string Title { get; set; }
    public required string FileName { get; set; }
    public required string StorageKey { get; set; }
    public required string Url { get; set; }
    public long SizeBytes { get; set; }
}

public class QuizDefinition
{
    public int? TimeLimitMinutes { get; set; }
    public int PassMarkPercent { get; set; } = 70;
    public int? MaxAttempts { get; set; }
    public bool Shuffle { get; set; }
    public List<QuizQuestion> Questions { get; set; } = [];
}

public class QuizQuestion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public QuestionType Type { get; set; }
    public string Prompt { get; set; } = "";
    public int Points { get; set; } = 1;
    public string? Explanation { get; set; }
    public List<QuizOption> Options { get; set; } = [];
    public List<string> AcceptedAnswers { get; set; } = [];
}

public class QuizOption
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public enum QuestionType { SingleChoice, MultipleChoice, TrueFalse, ShortAnswer }

/// <summary>Learner-owned rows live in the course's tenant, so the tutor sees them and the tenant guard still applies.</summary>
public class QuizAttempt : TenantEntity
{
    public Guid LessonId { get; set; }
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public int ScorePercent { get; set; }
    public bool Passed { get; set; }
    public List<Guid> QuestionOrder { get; set; } = [];
    public List<QuizAnswer> Answers { get; set; } = [];
}

public class QuizAnswer
{
    public Guid QuestionId { get; set; }
    public List<Guid> OptionIds { get; set; } = [];
    public string? Text { get; set; }
    public bool Correct { get; set; }
}

public class Enrollment : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public EnrollmentSource Source { get; set; }
    public Guid? OrderId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ProgressPercent { get; set; }
    public Guid? LastLessonId { get; set; }
    public bool Revoked { get; set; }
}

public enum EnrollmentSource { Purchase, Free, Coupon }

public class LessonProgress : TenantEntity
{
    public Guid EnrollmentId { get; set; }
    public Guid LessonId { get; set; }
    public int PositionSec { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class LessonNote : TenantEntity
{
    public Guid LessonId { get; set; }
    public Guid UserId { get; set; }
    public string Body { get; set; } = "";
}

public class LessonQuestion : TenantEntity
{
    public Guid CourseId { get; set; }
    public Guid LessonId { get; set; }
    public Guid UserId { get; set; }
    public required string Body { get; set; }
    public List<LessonAnswer> Answers { get; set; } = [];
}

public class LessonAnswer : TenantEntity
{
    public Guid QuestionId { get; set; }
    public Guid UserId { get; set; }
    public bool IsTutor { get; set; }
    public required string Body { get; set; }
}

public class CourseReview : TenantEntity
{
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}

public class CourseCoupon : TenantEntity
{
    public required string Code { get; set; }
    public Guid? CourseId { get; set; }
    public CouponDiscountType DiscountType { get; set; }
    public long Value { get; set; }
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum CouponDiscountType { Percent, Fixed }

/// <summary>A learner buying one course. Paid to the platform; the tutor's share goes to <see cref="Earning"/>.</summary>
public class CourseOrder : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid CourseId { get; set; }
    public long PriceMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TotalMinor { get; set; }
    public required string Currency { get; set; }
    public string? CouponCode { get; set; }
    public SaleChannel Channel { get; set; }
    public OrderState Status { get; set; } = OrderState.Pending;
    /// <summary>Paystack reference (crs_...); unique.</summary>
    public required string Reference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
}

public enum SaleChannel { Marketplace, Academy }
public enum OrderState { Pending, Paid, Failed, Refunded }


public class Certificate : TenantEntity
{
    public Guid EnrollmentId { get; set; }
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Public verification code.</summary>
    public required string Code { get; set; }
    public required string StudentName { get; set; }
    public required string CourseTitle { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}
