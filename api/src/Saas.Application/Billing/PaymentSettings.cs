using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Billing;

/// <summary>The limits the admin console's payment settings are checked against.</summary>
public static class PaymentSettingsRules
{
    public const int MaxBankName = 60;
    public const int MaxAccountName = 100;
    /// <summary>The provider name a bank-transfer plan payment records.</summary>
    public const string TransferProvider = "transfer";
}

/// <summary>
/// The platform payment settings as every payment path reads them. <see cref="PlanProvider"/> is already resolved (the row's choice,
/// else <c>Billing:PlanProvider</c>). Bank details are null when not set.
/// </summary>
public sealed record PaymentSettingsSnapshot(bool OnlinePaymentsEnabled, string PlanProvider, string? BankName, string? BankAccountNumber,
    string? BankAccountName)
{
    public bool HasBankDetails =>
        !string.IsNullOrWhiteSpace(BankName) && !string.IsNullOrWhiteSpace(BankAccountNumber) && !string.IsNullOrWhiteSpace(BankAccountName);
}

/// <summary>
/// Keeps the settings row for <see cref="Ttl"/> per API process, so a busy page doesn't read it on every request. A save on this process
/// drops it at once (<see cref="Invalidate"/>); another process sees the change within <see cref="Ttl"/>. A read that started before an
/// invalidation never stores its (possibly old) answer.
/// </summary>
public sealed class PaymentSettingsCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);
    private readonly Lock gate = new();
    private PaymentSettingsSnapshot? value;
    private long expiresAt;
    private long version;

    public (PaymentSettingsSnapshot? Value, long Version) Get()
    {
        lock (gate)
            return (value is not null && Environment.TickCount64 < expiresAt ? value : null, version);
    }

    public void Set(PaymentSettingsSnapshot snapshot, long readVersion)
    {
        lock (gate)
        {
            if (readVersion != version) return;
            value = snapshot;
            expiresAt = Environment.TickCount64 + (long)Ttl.TotalMilliseconds;
        }
    }

    public void Invalidate()
    {
        lock (gate)
        {
            version++;
            value = null;
        }
    }
}

/// <summary>Reads the platform payment settings (the one row), through <see cref="PaymentSettingsCache"/>.</summary>
public sealed class PaymentSettings(IAppDbContext db, PaymentSettingsCache cache, BillingOptions options)
{
    /// <summary><c>Billing:PlanProvider</c> from config: the provider when the row names none.</summary>
    public string DefaultProvider { get; } =
        (string.IsNullOrWhiteSpace(options.PlanProvider) ? BillingOptions.Paystack : options.PlanProvider).Trim().ToLowerInvariant();

    public async Task<PaymentSettingsSnapshot> GetAsync(CancellationToken ct)
    {
        var (cached, version) = cache.Get();
        if (cached is not null) return cached;
        var row = await db.PlatformPaymentSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlatformPaymentSettings.SingletonId, ct);
        // No row (it is inserted by the migration) reads as online payment off and no bank details: nothing is taken by mistake.
        var snapshot = new PaymentSettingsSnapshot(row?.OnlinePaymentsEnabled ?? false,
            string.IsNullOrWhiteSpace(row?.PlanProvider) ? DefaultProvider : row.PlanProvider.Trim().ToLowerInvariant(),
            Blank(row?.BankName), Blank(row?.BankAccountNumber), Blank(row?.BankAccountName));
        cache.Set(snapshot, version);
        return snapshot;
    }

    /// <summary>
    /// Whether a new payment may start through the platform's online provider (plan checkouts, course sales, stays, boosts). Confirming
    /// and refunding payments already taken never asks this.
    /// </summary>
    public async Task<bool> OnlinePaymentsEnabledAsync(CancellationToken ct) => (await GetAsync(ct)).OnlinePaymentsEnabled;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
