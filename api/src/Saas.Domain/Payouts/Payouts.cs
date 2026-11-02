using Saas.Domain.Common;
using Saas.Domain.Courses;

namespace Saas.Domain.Payouts;

/// <summary>Where a business wants its earnings sent. Stored as JSON on the business's profile and copied onto each payout.</summary>
public class PayoutAccount
{
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
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
}

public enum PayoutStatus { Requested, Paid, Rejected }
