using Saas.Domain.Common;

namespace Saas.Domain.Core;

public class Plan : Entity
{
    /// <summary>Product key, e.g. "whatsapp-orders". The plans table is shared by all six products.</summary>
    public required string Product { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "NGN";
    public BillingInterval Interval { get; set; } = BillingInterval.Month;
    /// <summary>Numeric limits; -1 means unlimited, 0/1 act as feature flags.</summary>
    public Dictionary<string, long> Limits { get; set; } = [];
    public List<string> Features { get; set; } = [];
    public bool IsDefault { get; set; }
    /// <summary>Not offered to new customers; existing subscribers keep it. Plans are never deleted.</summary>
    public bool IsHidden { get; set; }
    public int SortOrder { get; set; }

    public long Limit(string key) => Limits.TryGetValue(key, out var v) ? v : 0;
}

/// <summary>Term is four months, the billing period of school plans.</summary>
public enum BillingInterval { Month, Year, Term }

[Audited]
public class Subscription : TenantEntity
{
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset CurrentPeriodStart { get; set; }
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public string? Provider { get; set; }
    public string? ProviderRef { get; set; }
    public DateTimeOffset? CancelAt { get; set; }

    // What the business bought, frozen when the period was paid (or comped). Plan edits reach it at the next renewal.
    public long? PriceMinor { get; set; }
    public string? Currency { get; set; }
    public Dictionary<string, long>? Limits { get; set; }
    public List<string>? Features { get; set; }

    /// <summary>Freezes the plan's current price, limits and features onto this subscription.</summary>
    public void Snapshot(Plan plan, long priceMinor, string currency)
    {
        PriceMinor = priceMinor;
        Currency = currency;
        Limits = new Dictionary<string, long>(plan.Limits);
        Features = [.. plan.Features];
    }
}

public enum SubscriptionStatus { Active, PastDue, Cancelled }

public class Payment : TenantEntity
{
    public Guid? PayerUserId { get; set; }
    public long AmountMinor { get; set; }
    public required string Currency { get; set; }
    public required string Provider { get; set; }
    /// <summary>Our reference sent to the provider; unique, which makes webhooks idempotent.</summary>
    public required string ProviderRef { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    /// <summary>What the payment is for, e.g. "subscription".</summary>
    public required string Purpose { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>When the plan-payment re-check last asked the provider about this payment (throttles it to every 10 minutes).</summary>
    public DateTimeOffset? RecheckedAt { get; set; }
}

/// <summary>
/// The platform's payment settings: one row (<see cref="SingletonId"/>), edited in the admin console. Whether new payments may go
/// through the platform's online provider at all, which provider plan checkouts use (null: <c>Billing:PlanProvider</c> from config),
/// and the company account businesses pay plans into by bank transfer while online payment is off. Provider keys stay in config.
/// </summary>
public class PlatformPaymentSettings
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public bool OnlinePaymentsEnabled { get; set; }
    public string? PlanProvider { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountName { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>
/// Abandoned: a plan checkout the provider never confirmed within 24 hours, so the re-check stopped asking. It is soft: a later
/// confirmation (callback or return check) is still applied. Stored as text, so adding it needs no data change.
/// </summary>
public enum PaymentStatus { Pending, Succeeded, Failed, Refunded, Abandoned }
