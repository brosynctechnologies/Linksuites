using Saas.Application.School;

namespace Saas.UnitTests;

public class ReminderRulesTests
{
    private static readonly DateOnly Due = new(2026, 11, 10);

    private static IReadOnlyList<(DateOnly DueDate, long OutstandingMinor)> Owing(params (DateOnly, long)[] items) => items;

    private static bool Due1(DateOnly today, int sent = 0, DateOnly? last = null, long owed = 500_000, int before = 3, int every = 7) =>
        ReminderRules.Due(today, Owing((Due, owed)), sent, last, before, every);

    [Fact]
    public void Reminder_three_days_before_due()
    {
        Assert.False(Due1(Due.AddDays(-4)));
        Assert.True(Due1(Due.AddDays(-3)));
        // Already reminded for this due date: nothing more until it is overdue.
        Assert.False(Due1(Due.AddDays(-3), sent: 1, last: Due.AddDays(-3)));
        Assert.False(Due1(Due.AddDays(-1), sent: 1, last: Due.AddDays(-3)));
        Assert.False(Due1(Due, sent: 1, last: Due.AddDays(-3)));
        // A missed day is caught up on the next run.
        Assert.True(Due1(Due.AddDays(-2)));
        // The school's own setting decides the lead time.
        Assert.True(Due1(Due.AddDays(-5), before: 5));
        Assert.False(Due1(Due.AddDays(-6), before: 5));
    }

    [Fact]
    public void Then_every_seven_days_while_overdue()
    {
        var first = Due.AddDays(-3);
        // After the reminder before the due date, the next is seven days after it.
        Assert.False(Due1(Due.AddDays(1), sent: 1, last: first));
        Assert.False(Due1(first.AddDays(6), sent: 1, last: first));
        Assert.True(Due1(first.AddDays(7), sent: 1, last: first));
        // Then seven days after each reminder sent.
        var second = first.AddDays(7);
        Assert.False(Due1(second.AddDays(6), sent: 2, last: second));
        Assert.True(Due1(second.AddDays(7), sent: 2, last: second));
        // Overdue and never reminded: send now.
        Assert.True(Due1(Due.AddDays(2)));
        // A late sender is measured from when it actually sent, not from the original schedule.
        Assert.False(Due1(second.AddDays(8), sent: 2, last: second.AddDays(2)));
        Assert.True(Due1(second.AddDays(9), sent: 2, last: second.AddDays(2)));
        // The school's own interval.
        Assert.True(Due1(Due.AddDays(6), sent: 1, last: Due.AddDays(3), every: 3));
        Assert.False(Due1(Due.AddDays(5), sent: 1, last: Due.AddDays(3), every: 3));
    }

    [Fact]
    public void Never_more_than_six()
    {
        Assert.True(Due1(Due.AddDays(60), sent: 5, last: Due.AddDays(10)));
        Assert.False(Due1(Due.AddDays(60), sent: 6, last: Due.AddDays(10)));
        Assert.False(Due1(Due.AddDays(60), sent: 9, last: null));
        Assert.Equal(6, ReminderRules.MaxPerInvoice);
    }

    [Fact]
    public void Nothing_when_paid()
    {
        Assert.False(Due1(Due.AddDays(-3), owed: 0));
        Assert.False(Due1(Due.AddDays(20), owed: 0));
        Assert.False(ReminderRules.Due(Due.AddDays(20), [], 0, null, 3, 7));
        // Paid instalments are ignored; the next unpaid one decides.
        var plan = Owing((Due.AddDays(-30), 0), (Due, 250_000), (Due.AddDays(30), 250_000));
        Assert.True(ReminderRules.Due(Due.AddDays(-3), plan, 0, null, 3, 7));
        Assert.False(ReminderRules.Due(Due.AddDays(-4), plan, 0, null, 3, 7));
    }

    [Fact]
    public void The_earliest_unpaid_instalment_decides()
    {
        var plan = Owing((Due, 100_000), (Due.AddDays(30), 100_000));
        // Instalment one is overdue, so the overdue cadence applies even though instalment two is coming.
        Assert.True(ReminderRules.Due(Due.AddDays(8), plan, 1, Due.AddDays(1), 3, 7));
        Assert.False(ReminderRules.Due(Due.AddDays(7), plan, 1, Due.AddDays(1), 3, 7));
        // Instalment one paid: the reminder for instalment two comes three days before it, and one sent for one doesn't hide two.
        var later = Owing((Due, 0), (Due.AddDays(30), 100_000));
        Assert.True(ReminderRules.Due(Due.AddDays(27), later, 2, Due.AddDays(-3), 3, 7));
        Assert.False(ReminderRules.Due(Due.AddDays(26), later, 2, Due.AddDays(-3), 3, 7));
    }
}

public class FinanceRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    [Fact]
    public void Payments_cover_instalments_in_due_date_order()
    {
        var plan = new (DateOnly, long)[] { (new(2026, 12, 1), 300), (new(2026, 10, 1), 300), (new(2026, 11, 1), 400) };
        var left = FinanceRules.Outstanding(plan, paid: 450, balance: 550);
        Assert.Equal([(new DateOnly(2026, 10, 1), 0L), (new DateOnly(2026, 11, 1), 250L), (new DateOnly(2026, 12, 1), 300L)], left);
    }

    [Fact]
    public void Without_a_plan_the_whole_balance_falls_due_on_the_invoice_date()
    {
        var left = FinanceRules.Outstanding([], paid: 100, balance: 900, fallbackDue: new DateOnly(2026, 11, 1));
        Assert.Equal([(new DateOnly(2026, 11, 1), 900L)], left);
        Assert.Empty(FinanceRules.Outstanding([], paid: 1000, balance: 0, fallbackDue: new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Days_overdue_run_from_the_earliest_unpaid_instalment_that_is_past_due()
    {
        var plan = new (DateOnly DueDate, long OutstandingMinor)[] { (new(2026, 10, 1), 0), (new(2026, 11, 1), 250), (new(2026, 12, 1), 300) };
        Assert.Equal(19, FinanceRules.DaysOverdue(Today, plan));
        Assert.Equal(0, FinanceRules.DaysOverdue(new DateOnly(2026, 11, 1), plan)); // due today is not overdue yet
        Assert.Equal(1, FinanceRules.DaysOverdue(new DateOnly(2026, 11, 2), plan));
        Assert.Equal(0, FinanceRules.DaysOverdue(Today, [(new DateOnly(2026, 11, 1), 0L)]));
        Assert.Equal(0, FinanceRules.DaysOverdue(Today, []));
    }

    [Fact]
    public void A_payment_is_shared_across_the_fee_lines_in_proportion_and_the_remainder_goes_to_the_largest()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var shares = FinanceRules.AllocateByLine(1000, [(a, 500L), (b, 300L), (c, 200L)]);
        Assert.Equal(500, shares[a]);
        Assert.Equal(300, shares[b]);
        Assert.Equal(200, shares[c]);

        var odd = FinanceRules.AllocateByLine(100, [(a, 333L), (b, 333L), (c, 334L)]);
        Assert.Equal(100, odd.Values.Sum());
        Assert.Equal(33, odd[a]);
        Assert.Equal(33, odd[b]);
        Assert.Equal(34, odd[c]); // c is the largest line and takes the remainder (33 + 33 + 33 = 99, plus 1)

        Assert.Empty(FinanceRules.AllocateByLine(100, []));
        Assert.Empty(FinanceRules.AllocateByLine(100, [(a, 0L)]));
    }
}
