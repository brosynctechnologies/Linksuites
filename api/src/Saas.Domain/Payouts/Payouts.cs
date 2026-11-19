using Saas.Domain.Common;
using Saas.Domain.Courses;

namespace Saas.Domain.Payouts;

/// <summary>Where a business wants its earnings sent. Stored as JSON on the business's profile and copied onto each payout.</summary>
public class PayoutAccount
{
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    /// <summary>Paystack's bank code, when the account was picked from Paystack's bank list.</summary>
    public string? BankCode { get; set; }
    /// <summary>When Paystack resolved the account number to <see cref="AccountName"/>.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }
    /// <summary>Paystack transfer recipient (RCP_...) for automatic payouts.</summary>
    public string? RecipientCode { get; set; }
    /// <summary>Paystack subaccount (ACCT_...): when set, and subaccounts are switched on, Paystack settles the seller's share directly.</summary>
    public string? SubaccountCode { get; set; }
}

public enum EarningSource { CourseOrder, Stay }

/// <summary>
/// Money the platform collected on a business's behalf (a course sale, a short-let stay), less the platform fee.
/// It becomes available to pay out at <see cref="AvailableAt"/>, after the refund window.
/// </summary>
public class Earning : TenantEntity
{
    public EarningSource Source { get; set; }
    /// <summary>The course order or stay this earning came from. One earning per source.</summary>
    public Guid SourceId { get; set; }
    public string Description { get; set; } = "";
    /// <summary>The buyer or guest.</summary>
    public Guid UserId { get; set; }
    public SaleChannel? Channel { get; set; }
    public Guid? CourseId { get; set; }
    public long GrossMinor { get; set; }
    public int FeePercent { get; set; }
    public long FeeMinor { get; set; }
    public long NetMinor { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public bool Reversed { get; set; }
    /// <summary>
    /// The payment was split by Paystack (a subaccount): the seller's share went straight to their bank, so it is never paid out
    /// from the platform's balance.
    /// </summary>
    public bool SettledByProvider { get; set; }
}

[Audited]
public class Payout : TenantEntity
{
    public long AmountMinor { get; set; }
    public PayoutStatus Status { get; set; } = PayoutStatus.Requested;
    public required PayoutAccount Account { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? RequestedBy { get; set; }

    // Automatic payouts through Paystack Transfers.

    /// <summary>The reference of the current transfer attempt (lowercase, unique). Resending the same attempt reuses it.</summary>
    public string? TransferReference { get; set; }
    /// <summary>Paystack's transfer code (TRF_...).</summary>
    public string? TransferCode { get; set; }
    /// <summary>Paystack's last word on the transfer: pending, otp, success, failed, reversed...</summary>
    public string? TransferStatus { get; set; }
    /// <summary>How many transfers were started for this payout (a retry after a failure is a new attempt with a new reference).</summary>
    public int TransferAttempts { get; set; }
    public DateTimeOffset? TransferSentAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    /// <summary>Why the last transfer failed, was reversed, or is stuck.</summary>
    public string? FailureReason { get; set; }
}

/// <summary>
/// <c>Processing</c>: Finance approved it and a Paystack transfer is in flight. <c>Failed</c>: the transfer failed or was reversed; the
/// amount is available again and Finance can retry.
/// </summary>
public enum PayoutStatus { Requested, Paid, Rejected, Processing, Failed }

/// <summary>A checkout that was started with a Paystack subaccount: the seller's share is settled by Paystack. One per payment reference.</summary>
public class PaymentSplit : Entity
{
    public required string Reference { get; set; }
    public Guid TenantId { get; set; }
    public required string SubaccountCode { get; set; }
    /// <summary>The platform fee Paystack keeps (transaction_charge).</summary>
    public long FeeMinor { get; set; }
}
