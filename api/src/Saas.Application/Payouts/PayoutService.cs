using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Payouts;

namespace Saas.Application.Payouts;

public sealed record PayoutAccountDto(string BankName, string AccountNumber, string AccountName);
public sealed record PayoutDto(Guid Id, long AmountMinor, PayoutStatus Status, string BankName, string AccountNumber, string AccountName,
    string? Reference, DateTimeOffset RequestedAt, DateTimeOffset? PaidAt, string? Note, string? AcademyName = null,
    string? Product = null, string? BusinessName = null, string? Currency = null);
public sealed record PayoutRequest(long AmountMinor);
public sealed record PaidRequest(string Reference);

public sealed class PayoutAccountDtoValidator : AbstractValidator<PayoutAccountDto>
{
    public PayoutAccountDtoValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.AccountNumber).NotEmpty().Matches("^[0-9]{6,20}$").WithMessage("Enter the account number using digits only.");
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(120);
    }
}

/// <summary>The current business's side of the shared ledger: balances, payout requests and history.</summary>
public sealed class PayoutService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, TimeProvider clock)
{
    public async Task<LedgerRules.Ledger> LedgerAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        var earnings = await db.Earnings.AsNoTracking()
            .Select(e => new LedgerRules.LedgerEntry(e.Id, e.NetMinor, e.AvailableAt, e.Reversed, e.CreatedAt)).ToListAsync(ct);
        var payouts = await db.Payouts.AsNoTracking().Where(p => p.Status != PayoutStatus.Rejected).Select(p => new { p.AmountMinor, p.Status }).ToListAsync(ct);
        return LedgerRules.Balance(earnings, payouts.Sum(p => p.AmountMinor), payouts.Where(p => p.Status == PayoutStatus.Paid).Sum(p => p.AmountMinor),
            clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<PayoutDto>> ListAsync(CancellationToken ct) =>
        (await db.Payouts.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct))
            .Select(p => ToDto(p)).Select(d => user.IsSupportView ? d with { AccountNumber = AccountMask.Last4(d.AccountNumber) } : d).ToList();

    /// <summary>Requests a payout of available money to <paramref name="account"/> (the business's saved bank account).</summary>
    public async Task<PayoutDto> RequestAsync(long amountMinor, PayoutAccount? account, string currency, CancellationToken ct)
    {
        if (account is null) throw new ConflictException("Add the bank account to pay out to first.");
        if (amountMinor < LedgerRules.MinimumPayoutMinor)
            throw BadRequestException.Field("amountMinor", $"The minimum payout is {Money.Format(LedgerRules.MinimumPayoutMinor, currency)}.");
        var ledger = await LedgerAsync(ct);
        if (amountMinor > ledger.AvailableMinor)
            throw BadRequestException.Field("amountMinor", $"You can request up to {Money.Format(ledger.AvailableMinor, currency)}.");
        var payout = new Payout
        {
            AmountMinor = amountMinor, RequestedBy = user.UserId,
            Account = new PayoutAccount { BankName = account.BankName, AccountNumber = account.AccountNumber, AccountName = account.AccountName },
        };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync(ct);
        return ToDto(payout);
    }

    public static PayoutDto ToDto(Payout p, Tenant? business = null) => new(p.Id, p.AmountMinor, p.Status, p.Account.BankName,
        p.Account.AccountNumber, p.Account.AccountName, p.Reference, p.CreatedAt, p.PaidAt, p.Note, business?.Name, business?.Product,
        business?.Name, business?.Currency);
}

/// <summary>The platform admin's payout queue across every product that collects money through the platform.</summary>
public sealed class PayoutAdminService(IAppDbContext db, ITenantContext tenant, NotificationQueue notifications, TimeProvider clock)
{
    public async Task<IReadOnlyList<PayoutDto>> ListAsync(PayoutStatus? status, CancellationToken ct)
    {
        var query = db.Payouts.IgnoreQueryFilters().AsNoTracking();
        if (status is { } s) query = query.Where(p => p.Status == s);
        var payouts = await query.OrderBy(p => p.CreatedAt).Take(500).ToListAsync(ct);
        var tenantIds = payouts.Select(p => p.TenantId).Distinct().ToList();
        var businesses = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        return payouts.Select(p => PayoutService.ToDto(p, businesses.GetValueOrDefault(p.TenantId))).ToList();
    }

    /// <summary>Records that the money was sent (by bank transfer from the platform account).</summary>
    public async Task<PayoutDto> MarkPaidAsync(Guid id, PaidRequest r, CancellationToken ct)
    {
        var (payout, business) = await LoadAsync(id, ct);
        if (payout.Status != PayoutStatus.Requested) throw new ConflictException("This payout was already handled.");
        if (string.IsNullOrWhiteSpace(r.Reference)) throw BadRequestException.Field("reference", "Enter the transfer reference.");
        payout.Status = PayoutStatus.Paid;
        payout.Reference = r.Reference.Trim();
        payout.PaidAt = clock.GetUtcNow();
        await NotifyAsync(payout, business, "payout.paid", ct);
        await db.SaveChangesAsync(ct);
        return PayoutService.ToDto(payout, business);
    }

    /// <summary>Rejecting returns the amount to the business's available balance.</summary>
    public async Task<PayoutDto> RejectAsync(Guid id, string? note, CancellationToken ct)
    {
        var (payout, business) = await LoadAsync(id, ct);
        if (payout.Status != PayoutStatus.Requested) throw new ConflictException("This payout was already handled.");
        payout.Status = PayoutStatus.Rejected;
        payout.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await NotifyAsync(payout, business, "payout.rejected", ct);
        await db.SaveChangesAsync(ct);
        return PayoutService.ToDto(payout, business);
    }

    private async Task<(Payout, Tenant)> LoadAsync(Guid id, CancellationToken ct)
    {
        var tenantId = await db.Payouts.IgnoreQueryFilters().Where(p => p.Id == id).Select(p => (Guid?)p.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payout");
        if (tenant.TenantId != tenantId) tenant.SetTenant(tenantId);
        var payout = await db.Payouts.FirstAsync(p => p.Id == id, ct);
        return (payout, await db.Tenants.FirstAsync(t => t.Id == tenantId, ct));
    }

    private async Task NotifyAsync(Payout payout, Tenant business, string template, CancellationToken ct)
    {
        var owner = await db.Memberships.Where(m => m.Role == MemberRole.Owner).Select(m => m.User!.Email).FirstOrDefaultAsync(ct);
        if (owner is not null)
            notifications.Email(owner, template, new()
            {
                ["amount"] = Money.Format(payout.AmountMinor, business.Currency), ["reference"] = payout.Reference ?? "", ["note"] = payout.Note ?? "",
            }, payout.TenantId);
    }
}

/// <summary>Bank account numbers shown in a support view (and other places that only need to recognise the account).</summary>
public static class AccountMask
{
    public static string Last4(string number) => number.Length <= 4 ? number : "••••" + number[^4..];
}
