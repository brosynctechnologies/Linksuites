using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

// Payment settings

/// <summary>Which plan providers have their keys in this server's config (keys themselves never leave config).</summary>
public sealed record ConfiguredProvidersDto(bool Paystack, bool Opay);
public sealed record PaymentSettingsAdminDto(bool OnlinePaymentsEnabled, string PlanProvider, string? BankName, string? BankAccountNumber,
    string? BankAccountName, ConfiguredProvidersDto Providers, DateTimeOffset? UpdatedAt, string? UpdatedByName);
public sealed record SavePaymentSettingsRequest(bool OnlinePaymentsEnabled, string? PlanProvider, string? BankName, string? BankAccountNumber,
    string? BankAccountName);

/// <summary>
/// The admin console's payment switch, plan provider and company bank details (the one settings row). A save takes effect on this
/// server at once and on any other within <see cref="PaymentSettingsCache.Ttl"/>.
/// </summary>
public sealed partial class PlatformPaymentSettingsService(IAppDbContext db, PaymentSettings settings, PaymentSettingsCache cache,
    PlanGateways gateways, PlatformAudit audit, TimeProvider clock)
{
    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex AccountNumberPattern();

    private ConfiguredProvidersDto Providers() => new(
        gateways.For(BillingOptions.Paystack)?.IsConfigured == true, gateways.For(BillingOptions.Opay)?.IsConfigured == true);

    private bool IsConfigured(string provider) => gateways.For(provider)?.IsConfigured == true;

    public async Task<PaymentSettingsAdminDto> GetAsync(CancellationToken ct)
    {
        cache.Invalidate(); // the console always shows the row as it is now
        var current = await settings.GetAsync(ct);
        var row = await db.PlatformPaymentSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlatformPaymentSettings.SingletonId, ct);
        var by = row?.UpdatedByUserId is { } id ? await db.Users.Where(u => u.Id == id).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
        return new PaymentSettingsAdminDto(current.OnlinePaymentsEnabled, current.PlanProvider, current.BankName, current.BankAccountNumber,
            current.BankAccountName, Providers(), row?.UpdatedAt, by);
    }

    public async Task<PaymentSettingsAdminDto> UpdateAsync(SavePaymentSettingsRequest r, CancellationToken ct)
    {
        var provider = r.PlanProvider?.Trim().ToLowerInvariant() ?? "";
        if (provider is not (BillingOptions.Paystack or BillingOptions.Opay))
            throw BadRequestException.Field("planProvider", "Choose Paystack or OPay.");
        if (r.OnlinePaymentsEnabled && !IsConfigured(provider))
            throw BadRequestException.Field("onlinePaymentsEnabled",
                $"{(provider == BillingOptions.Opay ? "OPay" : "Paystack")} keys aren't set on the server, so online payment can't be switched on with it. " +
                "Add the keys to the server config first, or choose a provider that is set up.");
        var bankName = Name(r.BankName, "bankName", "Enter the bank name", PaymentSettingsRules.MaxBankName);
        var accountName = Name(r.BankAccountName, "bankAccountName", "Enter the account name", PaymentSettingsRules.MaxAccountName);
        var accountNumber = r.BankAccountNumber?.Trim() ?? "";
        if (!AccountNumberPattern().IsMatch(accountNumber))
            throw BadRequestException.Field("bankAccountNumber", "Enter the 10-digit account number.");

        var row = await db.PlatformPaymentSettings.FirstOrDefaultAsync(s => s.Id == PlatformPaymentSettings.SingletonId, ct);
        if (row is null)
        {
            row = new PlatformPaymentSettings();
            db.PlatformPaymentSettings.Add(row);
        }
        var before = Snapshot(row);
        row.OnlinePaymentsEnabled = r.OnlinePaymentsEnabled;
        row.PlanProvider = provider;
        row.BankName = bankName;
        row.BankAccountNumber = accountNumber;
        row.BankAccountName = accountName;
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedByUserId = audit.ActorId;
        audit.Log("payment_settings.update", nameof(PlatformPaymentSettings), PlatformPaymentSettings.SingletonId.ToString(), before: before, after: Snapshot(row));
        await db.SaveChangesAsync(ct);
        cache.Invalidate();
        return await GetAsync(ct);
    }

    private static object Snapshot(PlatformPaymentSettings s) => new
    {
        onlinePaymentsEnabled = s.OnlinePaymentsEnabled, planProvider = s.PlanProvider, bankName = s.BankName,
        bankAccountNumber = s.BankAccountNumber, bankAccountName = s.BankAccountName,
    };

    private static string Name(string? value, string field, string prompt, int max)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0 || v.Length > max) throw BadRequestException.Field(field, $"{prompt} (up to {max} characters).");
        return v;
    }
}

// Bank transfers for plans

/// <summary>Confirm: <see cref="Anyway"/> with a <see cref="Note"/> confirms a withdrawn or rejected claim too.</summary>
public sealed record ConfirmTransferRequest(bool Anyway, string? Note);

public sealed record TransferClaimDto(Guid Id, string Reference, string Status, Guid BusinessId, string BusinessName, string Product,
    string PlanCode, string PlanName, long AmountMinor, string Currency, string? ClaimedByName, string? ClaimedByEmail, DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt, string? RejectReason);

/// <summary>
/// Plan payments by bank transfer, for finance admins: the claims (pending first), and confirm or reject after checking the company
/// account. Confirming runs the same confirm path as a provider payment (<see cref="BillingService.ConfirmTransferAsync"/>).
/// </summary>
public sealed class PlatformTransfersService(IAppDbContext db, BillingService billing, PlatformAudit audit, PlatformViewer viewer,
    NotificationQueue notifications, AppUrls urls, Saas.Application.Auth.PlatformOptions platform, TimeProvider clock)
{
    public const int ListLimit = 200;

    /// <summary>All pending claims, then the most recent decided ones, up to <see cref="ListLimit"/>.</summary>
    public async Task<IReadOnlyList<TransferClaimDto>> ListAsync(string? status, CancellationToken ct)
    {
        var q = db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.Provider == BillingService.Transfer && p.Purpose == "subscription");
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, ignoreCase: true, out var st)) q = q.Where(p => p.Status == st);
        var rows = await q.OrderBy(p => p.Status == PaymentStatus.Pending ? 0 : 1).ThenByDescending(p => p.CreatedAt).Take(ListLimit).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    /// <summary>
    /// Confirms a Pending claim. With <see cref="ConfirmTransferRequest.Anyway"/> and a note (10-300 characters) it also confirms a claim
    /// that was withdrawn (Abandoned) or rejected (Failed), for money that arrived after all; audit-logged as <c>transfer.confirm_anyway</c>.
    /// </summary>
    public async Task<TransferClaimDto> ConfirmAsync(Guid id, ConfirmTransferRequest? r, CancellationToken ct)
    {
        var anyway = r?.Anyway == true;
        string? note = null;
        if (anyway)
        {
            note = r!.Note?.Trim() ?? "";
            if (note.Length is < 10 or > 300) throw BadRequestException.Field("note", "Write a note of 10 to 300 characters: what you found and where.");
        }
        await billing.ConfirmTransferAsync(id, anyway, async (payment, c) =>
        {
            var was = payment.Status;
            audit.Log(was == PaymentStatus.Pending ? "transfer.confirm" : "transfer.confirm_anyway", nameof(Payment), payment.Id.ToString(), payment.TenantId,
                before: new { status = was.ToString() },
                after: new
                {
                    status = nameof(PaymentStatus.Succeeded), reference = payment.ProviderRef, amountMinor = payment.AmountMinor,
                    plan = payment.Metadata.GetValueOrDefault("planCode"), note,
                });
            await EmailBusinessAsync(payment, "plan.transfer_confirmed", reason: null, c);
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<TransferClaimDto> RejectAsync(Guid id, ReasonRequest r, CancellationToken ct)
    {
        var reason = Reasons.Require(r.Reason);
        await billing.RejectTransferAsync(id, async (payment, c) =>
        {
            payment.Metadata = new Dictionary<string, string>(payment.Metadata) { ["rejectReason"] = reason };
            payment.UpdatedAt = clock.GetUtcNow();
            audit.Log("transfer.reject", nameof(Payment), payment.Id.ToString(), payment.TenantId,
                before: new { status = nameof(PaymentStatus.Pending) },
                after: new { status = nameof(PaymentStatus.Failed), reference = payment.ProviderRef, reason });
            await EmailBusinessAsync(payment, "plan.transfer_rejected", reason, c);
        }, ct);
        return await GetAsync(id, ct);
    }

    private async Task<TransferClaimDto> GetAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.Payments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.Provider == BillingService.Transfer, ct)
            ?? throw new NotFoundException("Transfer");
        return (await ToDtosAsync([row], ct))[0];
    }

    /// <summary>Queues the email to whoever made the claim (else the business's owner), branded as the business's product.</summary>
    private async Task EmailBusinessAsync(Payment payment, string template, string? reason, CancellationToken ct)
    {
        var business = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == payment.TenantId).Select(t => new { t.Name, t.Product }).FirstAsync(ct);
        // Whoever made the claim, while still an active member of the business; else its (active) owner.
        var to = (payment.PayerUserId is { } payer
                     ? await (from m in db.Memberships.IgnoreQueryFilters() join u in db.Users on m.UserId equals u.Id
                              where m.TenantId == payment.TenantId && m.UserId == payer && m.Status == MembershipStatus.Active
                              select new { u.Id, u.FullName, u.Email }).FirstOrDefaultAsync(ct)
                     : null)
            ?? await (from m in db.Memberships.IgnoreQueryFilters() join u in db.Users on m.UserId equals u.Id
                      where m.TenantId == payment.TenantId && m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active
                      orderby m.CreatedAt
                      select new { u.Id, u.FullName, u.Email }).FirstOrDefaultAsync(ct);
        if (to?.Email is null) return;
        // The confirmation's subscription isn't saved yet: it is read from the change tracker.
        var until = template == "plan.transfer_confirmed"
            ? db.Subscriptions.Local.FirstOrDefault(s => s.ProviderRef == payment.ProviderRef)?.CurrentPeriodEnd
            : null;
        var payload = new Dictionary<string, string>
        {
            ["name"] = to.FullName, ["business"] = business.Name, ["plan"] = payment.Metadata.GetValueOrDefault("planName") ?? "",
            ["amount"] = Money.Format(payment.AmountMinor, payment.Currency), ["reference"] = payment.ProviderRef,
            ["link"] = $"{urls.Web(business.Product)}{BillingService.BillingPath(business.Product)}",
        };
        if (until is { } end) payload["until"] = end.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        if (reason is not null) payload["reason"] = reason;
        // Platform emails come from a no-reply address: the rejection names where to write instead.
        if (platform.AdminEmails.Select(e => e.Trim()).FirstOrDefault(e => e.Length > 0) is { } contact) payload["contact"] = contact;
        notifications.Email(to.Email, template, payload, payment.TenantId, to.Id);
    }

    private async Task<List<TransferClaimDto>> ToDtosAsync(List<Payment> rows, CancellationToken ct)
    {
        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var userIds = rows.Where(r => r.PayerUserId is not null).Select(r => r.PayerUserId!.Value).Distinct().ToList();
        var tenants = await db.Tenants.IgnoreQueryFilters().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => new { t.Name, t.Product }, ct);
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new { u.FullName, u.Email }, ct);
        var contacts = await viewer.SeesContactDetailsAsync(ct);
        return rows.Select(p =>
        {
            var t = tenants.GetValueOrDefault(p.TenantId);
            var u = p.PayerUserId is { } payer ? users.GetValueOrDefault(payer) : null;
            var email = u?.Email is { } e ? (contacts ? e : PlatformViewer.Mask(e)) : null;
            return new TransferClaimDto(p.Id, p.ProviderRef, p.Status.ToString(), p.TenantId, t?.Name ?? "", t?.Product ?? "",
                p.Metadata.GetValueOrDefault("planCode") ?? "", p.Metadata.GetValueOrDefault("planName") ?? "", p.AmountMinor, p.Currency,
                u?.FullName, email, p.CreatedAt, p.Status == PaymentStatus.Pending ? null : p.UpdatedAt ?? p.PaidAt,
                p.Metadata.GetValueOrDefault("rejectReason"));
        }).ToList();
    }
}
