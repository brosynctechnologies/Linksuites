using Saas.Application.Payouts;
using Saas.Application.Courses;
using Saas.Domain.Courses;

namespace Saas.UnitTests;

public class QuizGradingTests
{
    private static readonly QuizOption A = new() { Text = "A", IsCorrect = true };
    private static readonly QuizOption B = new() { Text = "B" };
    private static readonly QuizOption C = new() { Text = "C", IsCorrect = true };

    private static QuizDefinition Quiz(params QuizQuestion[] questions) => new() { PassMarkPercent = 60, Questions = [.. questions] };

    private static Dictionary<Guid, QuizAnswer> Answers(params (QuizQuestion Q, QuizOption[] Options, string? Text)[] picks) =>
        picks.ToDictionary(p => p.Q.Id, p => new QuizAnswer { QuestionId = p.Q.Id, OptionIds = p.Options.Select(o => o.Id).ToList(), Text = p.Text });

    [Fact]
    public void Multiple_choice_needs_exactly_the_correct_set()
    {
        var q = new QuizQuestion { Type = QuestionType.MultipleChoice, Options = [A, B, C] };
        Assert.True(CourseRules.Grade(Quiz(q), Answers((q, [C, A], null))).Questions[0].Correct);
        Assert.False(CourseRules.Grade(Quiz(q), Answers((q, [A], null))).Questions[0].Correct);
        Assert.False(CourseRules.Grade(Quiz(q), Answers((q, [A, B, C], null))).Questions[0].Correct);
    }

    [Theory]
    [InlineData("Abuja", true)]
    [InlineData("  abuja  ", true)]
    [InlineData("Federal   Capital Territory", true)]
    [InlineData("Lagos", false)]
    [InlineData(null, false)]
    public void Short_answers_ignore_case_and_extra_spaces(string? text, bool expected)
    {
        var q = new QuizQuestion { Type = QuestionType.ShortAnswer, AcceptedAnswers = ["Abuja", "Federal Capital Territory"] };
        Assert.Equal(expected, CourseRules.Grade(Quiz(q), Answers((q, [], text))).Questions[0].Correct);
    }

    [Fact]
    public void Score_is_weighted_by_points_and_unanswered_counts_as_wrong()
    {
        var easy = new QuizQuestion { Type = QuestionType.SingleChoice, Points = 1, Options = [A, B] };
        var hard = new QuizQuestion { Type = QuestionType.TrueFalse, Points = 3, Options = [new() { Text = "True", IsCorrect = true }, new() { Text = "False" }] };
        var grade = CourseRules.Grade(Quiz(easy, hard), Answers((hard, [hard.Options[0]], null)));
        Assert.Equal(75, grade.ScorePercent);
        Assert.True(grade.Passed);
        Assert.False(CourseRules.Grade(Quiz(easy, hard), Answers((easy, [A], null))).Passed); // 25%
    }

    [Fact]
    public void Empty_quiz_scores_zero() => Assert.Equal(0, CourseRules.Grade(Quiz(), new Dictionary<Guid, QuizAnswer>()).ScorePercent);
}

public class CourseMoneyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Course = Guid.NewGuid();

    [Fact]
    public void Coupons_apply_with_their_limits()
    {
        var pct = new CourseCoupon { Code = "HALF", DiscountType = CouponDiscountType.Percent, Value = 50 };
        Assert.Equal((500_000L, (string?)null), CourseRules.ApplyCoupon(pct, Course, 1_000_000, Now));
        var big = new CourseCoupon { Code = "BIG", DiscountType = CouponDiscountType.Fixed, Value = 5_000_000 };
        Assert.Equal(1_000_000, CourseRules.ApplyCoupon(big, Course, 1_000_000, Now).DiscountMinor); // never below free
        Assert.NotNull(CourseRules.ApplyCoupon(new CourseCoupon { Code = "X", CourseId = Guid.NewGuid() }, Course, 1_000_000, Now).Message);
        Assert.NotNull(CourseRules.ApplyCoupon(new CourseCoupon { Code = "X", ExpiresAt = Now }, Course, 1_000_000, Now).Message);
        Assert.NotNull(CourseRules.ApplyCoupon(new CourseCoupon { Code = "X", MaxUses = 2, Uses = 2 }, Course, 1_000_000, Now).Message);
        Assert.NotNull(CourseRules.ApplyCoupon(null, Course, 1_000_000, Now).Message);
    }

    [Theory]
    [InlineData(SaleChannel.Marketplace, 0, 30, 300_000, 700_000)]
    [InlineData(SaleChannel.Academy, 10, 10, 100_000, 900_000)]
    [InlineData(SaleChannel.Academy, 0, 0, 0, 1_000_000)]
    public void Marketplace_sales_carry_the_marketplace_fee_and_academy_sales_the_plan_fee(
        SaleChannel channel, long planFee, int expectedPct, long expectedFee, long expectedNet) =>
        Assert.Equal((expectedPct, expectedFee, expectedNet), CourseRules.Split(1_000_000, channel, planFee));

    [Fact]
    public void Ledger_keeps_the_refund_window_and_pays_out_oldest_first()
    {
        var old1 = new LedgerRules.LedgerEntry(Guid.NewGuid(), 600_000, Now.AddDays(-10), false, Now.AddDays(-17));
        var old2 = new LedgerRules.LedgerEntry(Guid.NewGuid(), 400_000, Now.AddDays(-5), false, Now.AddDays(-12));
        var fresh = new LedgerRules.LedgerEntry(Guid.NewGuid(), 900_000, Now.AddDays(3), false, Now.AddDays(-4));
        var refunded = new LedgerRules.LedgerEntry(Guid.NewGuid(), 700_000, Now.AddDays(-1), true, Now.AddDays(-8));

        var ledger = LedgerRules.Balance([fresh, old2, refunded, old1], requestedOrPaidMinor: 700_000, paidMinor: 600_000, Now);

        Assert.Equal(900_000, ledger.PendingMinor);
        Assert.Equal(300_000, ledger.AvailableMinor); // 1,000,000 matured - 700,000 requested or paid
        Assert.Equal(LedgerRules.EarningState.PaidOut, ledger.States[old1.Id]);
        Assert.Equal(LedgerRules.EarningState.Available, ledger.States[old2.Id]);
        Assert.Equal(LedgerRules.EarningState.Pending, ledger.States[fresh.Id]);
        Assert.Equal(LedgerRules.EarningState.Reversed, ledger.States[refunded.Id]);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(3, 3, 100)]
    public void Progress_rounds_down(int done, int total, int expected) => Assert.Equal(expected, CourseRules.ProgressPercent(done, total));
}
