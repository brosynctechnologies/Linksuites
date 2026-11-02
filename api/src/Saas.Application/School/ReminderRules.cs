namespace Saas.Application.School;

/// <summary>When a fee reminder is due for one invoice. Pure: the daily job passes in what it knows and sends when this says yes.</summary>
public static class ReminderRules
{
    /// <summary>The most reminders one invoice ever gets.</summary>
    public const int MaxPerInvoice = 6;

    /// <summary>
    /// True when a reminder should go out today. The next unpaid instalment (the earliest with something outstanding) decides:
    /// <list type="bullet">
    /// <item>Not overdue yet: a reminder is due from <c>daysBefore</c> days before its due date, once. If the last reminder was sent on or after that day, it
    /// has been done; a day the job missed is made up on the next run.</item>
    /// <item>Overdue: due when none has been sent, or when <c>everyDays</c> days have passed since the last one sent (the last reminder, whether it was
    /// the one before the due date or an overdue one).</item>
    /// </list>
    /// Never when nothing is outstanding, and never once <see cref="MaxPerInvoice"/> have been sent.
    /// </summary>
    public static bool Due(DateOnly today, IReadOnlyList<(DateOnly DueDate, long OutstandingMinor)> instalments, int sentCount, DateOnly? lastSent, int daysBefore, int everyDays)
    {
        if (sentCount >= MaxPerInvoice) return false;
        var next = instalments.Where(x => x.OutstandingMinor > 0).OrderBy(x => x.DueDate).Select(x => ((DateOnly DueDate, long OutstandingMinor)?)x).FirstOrDefault();
        if (next is not { } n) return false;

        if (today <= n.DueDate)
        {
            var from = n.DueDate.AddDays(-Math.Max(0, daysBefore));
            return today >= from && (lastSent is null || lastSent < from);
        }
        return lastSent is null || today.DayNumber - lastSent.Value.DayNumber >= Math.Max(1, everyDays);
    }
}

/// <summary>Pure rules behind the debtors list and the collection reports.</summary>
public static class FinanceRules
{
    /// <summary>
    /// What is still owed on each due date, earliest first. The payments so far cover the instalments in due-date order; an invoice
    /// without instalments owes its whole balance on <paramref name="fallbackDue"/> (the invoice's due date). Rows with nothing owed are kept for a plan
    /// and left out for the single due date.
    /// </summary>
    public static IReadOnlyList<(DateOnly DueDate, long OutstandingMinor)> Outstanding(
        IEnumerable<(DateOnly DueDate, long AmountMinor)> instalments, long paid, long balance, DateOnly? fallbackDue = null)
    {
        var plan = instalments.OrderBy(x => x.DueDate).ToList();
        if (plan.Count == 0)
            return balance > 0 && fallbackDue is { } due ? [(due, balance)] : [];
        var left = Math.Max(0, paid);
        var result = new List<(DateOnly, long)>(plan.Count);
        foreach (var (dueDate, amount) in plan)
        {
            var covered = Math.Min(left, amount);
            left -= covered;
            result.Add((dueDate, amount - covered));
        }
        return result;
    }

    /// <summary>Whole days since the earliest instalment that is past due and not covered by payments. 0 when none is overdue (due today is not overdue).</summary>
    public static int DaysOverdue(DateOnly today, IReadOnlyList<(DateOnly DueDate, long OutstandingMinor)> outstanding)
    {
        var overdue = outstanding.Where(x => x.OutstandingMinor > 0 && x.DueDate < today).Select(x => x.DueDate).DefaultIfEmpty().Min();
        return overdue == default ? 0 : today.DayNumber - overdue.DayNumber;
    }

    /// <summary>
    /// Shares a payment across an invoice's fee lines pro rata by line amount. Each line gets its floor share (whole kobo); the rounding
    /// remainder goes to the largest line (the first of equals), so the shares always add up to the payment. Lines of zero or less are not
    /// part of the base: callers leave discount lines out. Empty when there is nothing to share across.
    /// </summary>
    public static Dictionary<Guid, long> AllocateByLine(long amountMinor, IReadOnlyList<(Guid Key, long AmountMinor)> lines)
    {
        var positive = lines.Where(l => l.AmountMinor > 0).ToList();
        var result = new Dictionary<Guid, long>();
        var baseTotal = positive.Sum(l => l.AmountMinor);
        if (positive.Count == 0 || baseTotal <= 0 || amountMinor <= 0) return result;
        long given = 0;
        foreach (var (key, line) in positive)
        {
            var share = (long)((Int128)amountMinor * line / baseTotal);
            result[key] = result.GetValueOrDefault(key) + share;
            given += share;
        }
        var largest = positive.MaxBy(l => l.AmountMinor).Key; // MaxBy keeps the first of equal lines
        result[largest] += amountMinor - given;
        return result;
    }
}
