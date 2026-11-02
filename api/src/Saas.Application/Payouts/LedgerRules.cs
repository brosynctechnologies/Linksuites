namespace Saas.Application.Payouts;

/// <summary>Pure balance rules for any product whose sales the platform collects (courses, short-let stays).</summary>
public static class LedgerRules
{
    public const long MinimumPayoutMinor = 500_000;

    public enum EarningState { Pending, Available, PaidOut, Reversed }

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
        var toCover = paidMinor;
        foreach (var e in earnings.OrderBy(e => e.AvailableAt).ThenBy(e => e.CreatedAt))
        {
            if (e.Reversed) { states[e.Id] = EarningState.Reversed; continue; }
            if (e.AvailableAt > now) { states[e.Id] = EarningState.Pending; pending += e.NetMinor; continue; }
            matured += e.NetMinor;
            if (toCover >= e.NetMinor) { states[e.Id] = EarningState.PaidOut; toCover -= e.NetMinor; }
            else { states[e.Id] = EarningState.Available; toCover = 0; }
        }
        return new Ledger(pending, Math.Max(0, matured - requestedOrPaidMinor), paidMinor, states);
    }
}
