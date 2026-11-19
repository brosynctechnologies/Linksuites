namespace Saas.Application.Payouts;

/// <summary>Pure balance rules for any product whose sales the platform collects (courses, short-let stays).</summary>
public static class LedgerRules
{
    public const long MinimumPayoutMinor = 500_000;

    /// <summary>
    /// <c>Requested</c>: wholly covered by payouts (at least partly one still waiting to be paid). <c>PartlyAvailable</c>:
    /// some of it is covered by payouts, the rest can still be requested. <c>Settled</c>: Paystack split the payment and settled the
    /// seller's share straight to their bank (a subaccount), so it never passes through payouts. <c>Clawback</c>: a negative earning taking
    /// back a settled share the platform refunded; it comes off the available balance at once (and off later earnings while that is short).
    /// </summary>
    public enum EarningState { Pending, Available, PaidOut, Reversed, Requested, PartlyAvailable, Settled, Clawback }

    public sealed record LedgerEntry(Guid Id, long NetMinor, DateTimeOffset AvailableAt, bool Reversed, DateTimeOffset CreatedAt, bool SettledByProvider = false);

    /// <param name="SettledMinor">Earnings Paystack settled to the seller directly (subaccounts); not part of the other balances.</param>
    /// <param name="ClawbackMinor">Settled shares taken back after a platform refund (already deducted from the available balance).</param>
    public sealed record Ledger(long PendingMinor, long AvailableMinor, long PaidOutMinor, IReadOnlyDictionary<Guid, EarningState> States, long SettledMinor = 0,
        long ClawbackMinor = 0);

    /// <summary>
    /// Balances from earnings and payouts. Payouts that are requested, being sent or paid are taken from available money in
    /// first-in, first-out order, so the oldest earnings show as paid out first. Earnings settled by Paystack stand apart.
    /// </summary>
    public static Ledger Balance(IReadOnlyList<LedgerEntry> earnings, long requestedOrPaidMinor, long paidMinor, DateTimeOffset now)
    {
        var states = new Dictionary<Guid, EarningState>();
        var pending = 0L;
        var matured = 0L;
        var settled = 0L;
        var clawback = 0L;
        // Paid payouts cover the oldest earnings first, then payouts still waiting cover the next ones.
        var paidLeft = paidMinor;
        var requestedLeft = Math.Max(0, requestedOrPaidMinor - paidMinor);
        foreach (var e in earnings.OrderBy(e => e.AvailableAt).ThenBy(e => e.CreatedAt))
        {
            if (e.Reversed) { states[e.Id] = EarningState.Reversed; continue; }
            if (e.SettledByProvider) { states[e.Id] = EarningState.Settled; settled += e.NetMinor; continue; }
            if (e.NetMinor < 0) { states[e.Id] = EarningState.Clawback; matured += e.NetMinor; clawback -= e.NetMinor; continue; }
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
        return new Ledger(pending, Math.Max(0, matured - requestedOrPaidMinor), paidMinor, states, settled, clawback);
    }

    /// <summary>Payout statuses that hold money back from the available balance: requested, being sent, or paid.</summary>
    public static bool Holds(Saas.Domain.Payouts.PayoutStatus status) =>
        status is Saas.Domain.Payouts.PayoutStatus.Requested or Saas.Domain.Payouts.PayoutStatus.Processing or Saas.Domain.Payouts.PayoutStatus.Paid;
}
