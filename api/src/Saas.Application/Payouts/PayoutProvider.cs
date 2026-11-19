namespace Saas.Application.Payouts;

/// <summary>The <c>Payouts</c> config section. Both switches are off until the platform's live Paystack keys are in place.</summary>
public sealed class PayoutOptions
{
    public const string Section = "Payouts";

    /// <summary>
    /// When on, Finance approves a payout in the admin console and the system sends it with a Paystack Transfer. When off (the default),
    /// Finance sends the money by hand and marks the payout paid. Paystack's transfer OTP must be disabled for API transfers.
    /// </summary>
    public bool AutomaticTransfers { get; set; }

    /// <summary>
    /// When on, a seller can turn on direct settlement: a Paystack subaccount is created for them, and course sales and stays paid with
    /// Paystack are split at checkout so Paystack settles the seller's share straight to their bank. Off by default.
    /// </summary>
    public bool PaystackSubaccounts { get; set; }

    /// <summary>A transfer still in flight after this long is asked about (GET /transfer/verify) by the re-check job.</summary>
    public TimeSpan RecheckAfter { get; set; } = TimeSpan.FromMinutes(10);
}

public sealed record BankDto(string Name, string Code);

/// <summary>Where a transfer stands. <c>Unknown</c>: Paystack didn't answer (5xx, 429, timeout), so nothing can be concluded.</summary>
public enum TransferState { Pending, Success, Failed, NotFound, Unknown }

/// <param name="Status">Paystack's own status (pending, otp, success, failed, reversed...), when it gave one.</param>
/// <param name="Message">Paystack's message, for a refused transfer.</param>
public sealed record TransferOutcome(TransferState State, string? Status, string? TransferCode, string? Message);

/// <summary>A signed transfer.success, transfer.failed or transfer.reversed webhook from Paystack.</summary>
public sealed record TransferEvent(string Event, string Reference, long AmountMinor, string? TransferCode, string? Reason)
{
    /// <summary>The status the event stands for: success, failed or reversed.</summary>
    public string Status => Event["transfer.".Length..];
}

/// <summary>
/// The platform's Paystack account for paying sellers: bank list, account name lookup, transfer recipients, subaccounts and transfers.
/// Calls that change something throw <see cref="Common.ServiceUnavailableException"/> when Paystack can't be reached or refuses.
/// </summary>
public interface IPayoutProvider
{
    /// <summary>True when the platform's Paystack secret key is configured.</summary>
    bool IsConfigured { get; }
    Task<IReadOnlyList<BankDto>> BanksAsync(string currency, CancellationToken ct);
    /// <summary>The name on the account, or null when the bank doesn't know the account number.</summary>
    Task<string?> ResolveAccountAsync(string accountNumber, string bankCode, CancellationToken ct);
    /// <summary>Creates a "nuban" transfer recipient and returns its code (RCP_...).</summary>
    Task<string> CreateRecipientAsync(string name, string accountNumber, string bankCode, string currency, CancellationToken ct);
    /// <summary>Creates a subaccount that gets the payment less <paramref name="percentageCharge"/> percent, and returns its code (ACCT_...).</summary>
    Task<string> CreateSubaccountAsync(string businessName, string bankCode, string accountNumber, decimal percentageCharge, string? contactEmail,
        CancellationToken ct);
    /// <summary>POST /transfer. The same <paramref name="reference"/> twice is one transfer at Paystack.</summary>
    Task<TransferOutcome> TransferAsync(long amountMinor, string recipientCode, string reference, string reason, string currency, CancellationToken ct);
    /// <summary>GET /transfer/verify/:reference.</summary>
    Task<TransferOutcome> VerifyTransferAsync(string reference, CancellationToken ct);
}

public static class TransferStatuses
{
    /// <summary>Paystack's transfer statuses that are final failures (the money did not leave, or came back).</summary>
    public static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase) { "failed", "reversed", "rejected", "abandoned", "blocked" };

    public static TransferState StateOf(string? status) => status?.ToLowerInvariant() switch
    {
        "success" => TransferState.Success,
        { } s when Failed.Contains(s) => TransferState.Failed,
        _ => TransferState.Pending, // pending, otp, received, queued...
    };
}
