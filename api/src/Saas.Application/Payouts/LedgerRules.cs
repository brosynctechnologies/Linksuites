namespace Saas.Application.Payouts;

/// <summary>Pure balance rules for any product whose sales the platform collects (courses, short-let stays).</summary>
public static class LedgerRules
{
    public const long MinimumPayoutMinor = 500_000;

    /// <summary>
    /// <c>Requested</c>: wholly covered by payouts (at least partly one still waiting to be paid). <c>PartlyAvailable</c>:
    /// some of it is covered by payouts, the rest can still be requested.
    /// </summary>
    public enum EarningState { Pending, Available, PaidOut, Reversed, Requested, PartlyAvailable }

    public sealed record LedgerEntry(Guid Id, long NetMinor, DateTimeOffset AvailableAt, bool Reversed, DateTimeOffset CreatedAt);

    public sealed record Ledger(long PendingMinor, long AvailableMinor, long PaidOutMinor, IReadOnlyDictionary<Guid, EarningState> States);

    /// <summary>
    /// Balances from earnings and payouts. Payouts that are requested or paid are taken from available money in
    /// first-in, first-out order, so the oldest earnings show as paid out first.
    /// </summary>
    public static Ledger Balance(IReadOnlyList<LedgerEntry> earnings, long requestedOrPaidMinor, long paidMinor, DateTimeOffset now)
    {
        var states = new Dictionary<Guid, EarningState>();
        var pending = 0L;
        var matured = 0L;
        // Paid payouts cover the oldest earnings first, then payouts still waiting cover the next ones.
        var paidLeft = paidMinor;
        var requestedLeft = Math.Max(0, requestedOrPaidMinor - paidMinor);
        foreach (var e in earnings.OrderBy(e => e.AvailableAt).ThenBy(e => e.CreatedAt))
        {
            if (e.Reversed) { states[e.Id] = EarningState.Reversed; continue; }
            if (e.AvailableAt > now) { states[e.Id] = EarningState.Pending; pending += e.NetMinor; continue; }
            matured += e.NetMinor;
            if (paidLeft >= e.NetMinor) { states[e.Id] = EarningState.PaidOut; paidLeft -= e.NetMinor; continue; }
            var need = e.NetMinor - paidLeft;
            var coveredByPaid = paidLeft;
            paidLeft = 0;
            if (requestedLeft >= need) { states[e.Id] = EarningState.Requested; requestedLeft -= need; continue; }
            var covered = coveredByPaid + requestedLeft;
            requestedLeft = 0;
            states[e.Id] = covered > 0 ? EarningState.PartlyAvailable : EarningState.Available;
        }
        return new Ledger(pending, Math.Max(0, matured - requestedOrPaidMinor), paidMinor, states);
    }
}
