namespace Saas.Application.Billing;

/// <summary>What a provider says about one of our references.</summary>
public enum ProviderChargeState
{
    /// <summary>Paid. <see cref="ProviderChargeStatus.AmountMinor"/> and <see cref="ProviderChargeStatus.Currency"/> say how much.</summary>
    Success,
    /// <summary>Started but not finished (or not finished yet).</summary>
    Pending,
    /// <summary>The provider says it failed or was closed.</summary>
    Failed,
    /// <summary>The provider doesn't know the reference.</summary>
    NotFound,
}

public sealed record ProviderChargeStatus(ProviderChargeState State, long AmountMinor, string? Currency)
{
    public static readonly ProviderChargeStatus NotFound = new(ProviderChargeState.NotFound, 0, null);
    public static readonly ProviderChargeStatus Pending = new(ProviderChargeState.Pending, 0, null);
}

/// <summary>
/// A provider that can take plan payments for LinkSuite (Paystack, OPay). Besides starting a checkout it can be asked for a reference's
/// status, which is the only thing that confirms a plan payment.
/// </summary>
public interface IPlanPaymentGateway : IPaymentGateway
{
    /// <summary>
    /// The provider's answer for <paramref name="reference"/>. Throws <see cref="HttpRequestException"/> when the provider can't answer
    /// (5xx, 429, network, timeout, not configured), so callers keep the payment Pending and try again later.
    /// </summary>
    Task<ProviderChargeStatus> GetStatusAsync(string reference, CancellationToken ct);
}

/// <summary><c>Billing:PlanProvider</c> (env <c>BILLING_PROVIDER</c>): which provider new plan checkouts use.</summary>
public sealed class BillingOptions
{
    public const string Section = "Billing";
    public const string Paystack = "paystack";
    public const string Opay = "opay";

    /// <summary><c>paystack</c> (the default) or <c>opay</c>.</summary>
    public string PlanProvider { get; set; } = Paystack;

    /// <summary>
    /// <c>Billing:TransferReferenceKey</c>: the secret the bank-transfer references shown to businesses are derived with, so nobody can
    /// work out another business's next reference. Empty: derived from <c>Jwt:SigningKey</c> (a separate purpose, same secret store).
    /// </summary>
    public string TransferReferenceKey { get; set; } = "";
}

/// <summary>
/// Picks the plan-billing provider: the configured one for a new checkout, and the payment's own <c>Provider</c> for everything after,
/// so payments taken before a switch are still verified with the provider that took them. Course sales, boosts and stays don't use
/// this; they keep the single <see cref="IPaymentGateway"/>.
/// </summary>
public sealed class PlanGateways(IEnumerable<IPlanPaymentGateway> gateways, BillingOptions options)
{
    /// <summary>The provider new checkouts use, lower-cased.</summary>
    public string ConfiguredName { get; } = (string.IsNullOrWhiteSpace(options.PlanProvider) ? BillingOptions.Paystack : options.PlanProvider).Trim().ToLowerInvariant();

    /// <summary>The gateway for new checkouts; null when the configured name isn't a provider we have.</summary>
    public IPlanPaymentGateway? Configured => For(ConfiguredName);

    /// <summary>True when new plan checkouts can start.</summary>
    public bool IsConfigured => Configured?.IsConfigured == true;

    /// <summary>The gateway for a provider name. The last registration wins, so a test can stand in for a real provider.</summary>
    public IPlanPaymentGateway? For(string provider) =>
        gateways.LastOrDefault(g => string.Equals(g.Name, provider, StringComparison.OrdinalIgnoreCase));
}
