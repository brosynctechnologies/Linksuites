using System.Text.RegularExpressions;
using Saas.Domain.Courses;

namespace Saas.Application.Courses;

public sealed record GradedQuestion(Guid QuestionId, bool Correct, int Points);
public sealed record QuizGrade(int ScorePercent, bool Passed, IReadOnlyList<GradedQuestion> Questions);

/// <summary>Pure grading, pricing and money rules for courses, kept free of I/O so they're unit-tested.</summary>
public static partial class CourseRules
{
    public const int MarketplaceFeePercent = 30;
    public static readonly TimeSpan RefundWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Single choice and true/false need the one correct option; multiple choice needs exactly the correct set;
    /// short answers match an accepted answer ignoring case and extra spaces. Score is weighted by points.
    /// </summary>
    public static QuizGrade Grade(QuizDefinition quiz, IReadOnlyDictionary<Guid, QuizAnswer> answers)
    {
        var graded = new List<GradedQuestion>();
        foreach (var q in quiz.Questions)
        {
            answers.TryGetValue(q.Id, out var a);
            var correct = a is not null && q.Type switch
            {
                QuestionType.ShortAnswer => a.Text is { } text && q.AcceptedAnswers.Any(ok => Normalize(ok) == Normalize(text)),
                _ => a.OptionIds.Distinct().Order().SequenceEqual(q.Options.Where(o => o.IsCorrect).Select(o => o.Id).Order()),
            };
            graded.Add(new GradedQuestion(q.Id, correct, Math.Max(q.Points, 0)));
        }
        var total = graded.Sum(g => g.Points);
        var score = total == 0 ? 0 : (int)Math.Floor(graded.Where(g => g.Correct).Sum(g => g.Points) * 100.0 / total);
        return new QuizGrade(score, score >= quiz.PassMarkPercent, graded);
    }

    public static string Normalize(string text) => Spaces().Replace(text.Trim(), " ").ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");

    /// <summary>
    /// A coupon's end date is a calendar day in Africa/Lagos (where every studio date is shown): it works until the
    /// last moment of that day. Stored as the last microsecond (PostgreSQL's precision) before the next Lagos midnight.
    /// </summary>
    public static DateTimeOffset EndOfDayInLagos(DateOnly day)
    {
        var nextMidnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(nextMidnight, Lagos.GetUtcOffset(nextMidnight)).ToUniversalTime().AddTicks(-10);
    }

    /// <summary>Discount for a coupon, or a message saying why it can't be used.</summary>
    public static (long DiscountMinor, string? Message) ApplyCoupon(CourseCoupon? coupon, Guid courseId, long priceMinor, DateTimeOffset now)
    {
        if (coupon is null || !coupon.IsActive) return (0, "That coupon code isn't valid.");
        if (coupon.CourseId is { } only && only != courseId) return (0, "That coupon is for a different course.");
        if (coupon.ExpiresAt is { } end && end <= now) return (0, "That coupon has expired.");
        if (coupon.MaxUses is { } max && coupon.Uses >= max) return (0, "That coupon has been used up.");
        var discount = coupon.DiscountType == CouponDiscountType.Percent
            ? priceMinor * Math.Clamp(coupon.Value, 0, 100) / 100
            : coupon.Value;
        return (Math.Clamp(discount, 0, priceMinor), null);
    }

    /// <summary>The tutor's share of a sale: marketplace sales carry the marketplace fee, academy sales the plan's fee.</summary>
    public static (int FeePercent, long FeeMinor, long NetMinor) Split(long totalMinor, SaleChannel channel, long academyFeePercent)
    {
        var pct = channel == SaleChannel.Marketplace ? MarketplaceFeePercent : (int)Math.Clamp(academyFeePercent, 0, 100);
        var fee = (long)Math.Round(totalMinor * pct / 100m, MidpointRounding.AwayFromZero);
        return (pct, fee, totalMinor - fee);
    }

    /// <summary>Percent of lessons done (a quiz lesson counts once it's passed).</summary>
    public static int ProgressPercent(int done, int total) => total == 0 ? 0 : (int)Math.Floor(done * 100.0 / total);
}
