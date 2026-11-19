using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Payouts;

namespace Saas.Application.Payouts;

/// <summary>
/// A business's bank account for payouts. As a request, <see cref="BankCode"/> is Paystack's code for the bank picked from the list: with
/// it the account name is checked with the bank. As an answer, <see cref="Verified"/> says the name was checked and
/// <see cref="DirectSettlement"/> that Paystack settles this seller's sales directly (a subaccount).
/// </summary>
public sealed record PayoutAccountDto(string BankName, string AccountNumber, string AccountName, string? BankCode = null, bool Verified = false,
    bool DirectSettlement = false);

public sealed record PayoutDto(Guid Id, long AmountMinor, PayoutStatus Status, string BankName, string AccountNumber, string AccountName,
    string? Reference, DateTimeOffset RequestedAt, DateTimeOffset? PaidAt, string? Note, string? AcademyName = null,
    string? Product = null, string? BusinessName = null, string? Currency = null, string? TransferReference = null, string? TransferStatus = null,
    string? FailureReason = null, int TransferAttempts = 0, bool AccountVerified = false);
public sealed record PayoutRequest(long AmountMinor);
public sealed record PaidRequest(string Reference);
public sealed record ResolveAccountRequest(string BankCode, string AccountNumber);
public sealed record ResolvedAccountDto(string AccountName);

/// <summary>What the seller apps and the admin console need to know about how payouts work right now.</summary>
/// <param name="AutomaticTransfers">Payouts are sent by Paystack Transfer once Finance approves them.</param>
/// <param name="DirectSettlement">Sellers can have Paystack settle their sales directly (subaccounts).</param>
/// <param name="CanVerify">The platform's Paystack key is set, so bank accounts can be checked.</param>
public sealed record PayoutSettingsDto(bool AutomaticTransfers, bool DirectSettlement, bool CanVerify);

public sealed class PayoutAccountDtoValidator : AbstractValidator<PayoutAccountDto>
{
    public PayoutAccountDtoValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.AccountNumber).NotEmpty().Matches("^[0-9]{6,20}$").WithMessage("Enter the account number using digits only.");
        RuleFor(x => x.AccountName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.BankCode).Matches("^[A-Za-z0-9-]{2,20}$").When(x => !string.IsNullOrWhiteSpace(x.BankCode)).WithMessage("Choose your bank from the list.");
    }
}

/// <summary>The current business's side of the shared ledger: balances, payout requests and history.</summary>
public sealed class PayoutService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, TimeProvider clock)
{
    /// <param name="at">The moment to balance at (now by default).</param>
    public async Task<LedgerRules.Ledger> LedgerAsync(CancellationToken ct, DateTimeOffset? at = null)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        var earnings = await db.Earnings.AsNoTracking()
            .Select(e => new LedgerRules.LedgerEntry(e.Id, e.NetMinor, e.AvailableAt, e.Reversed, e.CreatedAt, e.SettledByProvider)).ToListAsync(ct);
        var payouts = await db.Payouts.AsNoTracking()
            .Where(p => p.Status == PayoutStatus.Requested || p.Status == PayoutStatus.Processing || p.Status == PayoutStatus.Paid)
            .Select(p => new { p.AmountMinor, p.Status }).ToListAsync(ct);
        return LedgerRules.Balance(earnings, payouts.Sum(p => p.AmountMinor), payouts.Where(p => p.Status == PayoutStatus.Paid).Sum(p => p.AmountMinor),
            at ?? clock.GetUtcNow());
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
            Account = new PayoutAccount
            {
                BankName = account.BankName, AccountNumber = account.AccountNumber, AccountName = account.AccountName, BankCode = account.BankCode,
                VerifiedAt = account.VerifiedAt, RecipientCode = account.RecipientCode,
            },
        };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync(ct);
        return ToDto(payout);
    }

    public static PayoutDto ToDto(Payout p, Tenant? business = null) => new(p.Id, p.AmountMinor, p.Status, p.Account.BankName,
        p.Account.AccountNumber, p.Account.AccountName, p.Reference, p.CreatedAt, p.PaidAt, p.Note, business?.Name, business?.Product,
        business?.Name, business?.Currency, p.TransferReference, p.TransferStatus, p.FailureReason, p.TransferAttempts,
        p.Account.VerifiedAt is not null && p.Account.BankCode is not null);
}

/// <summary>
/// The seller's bank account, shared by every product that collects through the platform: Paystack's bank list, the account name check
/// (resolve account number), the transfer recipient for automatic payouts, and direct settlement (a Paystack subaccount).
/// </summary>
public sealed class PayoutAccountService(IPayoutProvider provider, PayoutOptions options, TimeProvider clock)
{
    private static readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<BankDto> Banks)> BankCache = new(StringComparer.OrdinalIgnoreCase);

    public PayoutSettingsDto Settings() => new(options.AutomaticTransfers, options.PaystackSubaccounts, provider.IsConfigured);

    /// <summary>Paystack's banks for <paramref name="currency"/>, kept for six hours.</summary>
    public async Task<IReadOnlyList<BankDto>> BanksAsync(string currency, CancellationToken ct)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "NGN" : currency.Trim().ToUpperInvariant();
        if (!provider.IsConfigured) throw new ServiceUnavailableException("Bank checks aren't set up yet. Type your bank's name instead.");
        lock (BankCache)
            if (BankCache.TryGetValue(currency, out var hit) && clock.GetUtcNow() - hit.At < TimeSpan.FromHours(6) && hit.Banks.Count > 0) return hit.Banks;
        var banks = await provider.BanksAsync(currency, ct);
        lock (BankCache) BankCache[currency] = (clock.GetUtcNow(), banks);
        return banks;
    }

    public async Task<ResolvedAccountDto> ResolveAsync(ResolveAccountRequest r, CancellationToken ct)
    {
        var (code, number) = (r.BankCode?.Trim() ?? "", r.AccountNumber?.Trim() ?? "");
        if (code.Length == 0) throw BadRequestException.Field("bankCode", "Choose your bank.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(number, "^[0-9]{6,20}$"))
            throw BadRequestException.Field("accountNumber", "Enter the account number using digits only.");
        if (!provider.IsConfigured) throw new ServiceUnavailableException("Bank checks aren't set up yet.");
        return new ResolvedAccountDto(await provider.ResolveAccountAsync(number, code, ct) ?? throw NotRecognised());
    }

    /// <summary>
    /// The account to save. With a bank code (and Paystack set up) the name comes from the bank and a transfer recipient is created;
    /// automatic transfers require that. A subaccount is kept only while the bank and account number stay the same.
    /// </summary>
    public async Task<PayoutAccount> BuildAsync(PayoutAccountDto r, PayoutAccount? current, string currency, CancellationToken ct)
    {
        var number = r.AccountNumber.Trim();
        var code = string.IsNullOrWhiteSpace(r.BankCode) ? null : r.BankCode.Trim();
        var account = new PayoutAccount { BankName = r.BankName.Trim(), AccountNumber = number, AccountName = r.AccountName.Trim(), BankCode = code };
        if (code is null)
        {
            if (options.AutomaticTransfers) throw BadRequestException.Field("bankCode", "Choose your bank from the list so we can check the account name.");
            return account;
        }
        if (!provider.IsConfigured)
        {
            if (options.AutomaticTransfers) throw new ServiceUnavailableException("Bank checks aren't set up yet. Please try again later.");
            return account;
        }
        account.AccountName = await provider.ResolveAccountAsync(number, code, ct) ?? throw NotRecognised();
        account.VerifiedAt = clock.GetUtcNow();
        var sameAccount = current is not null && current.BankCode == code && current.AccountNumber == number;
        account.RecipientCode = sameAccount && current!.RecipientCode is not null && current.AccountName == account.AccountName
            ? current.RecipientCode
            : await provider.CreateRecipientAsync(account.AccountName, number, code, currency, ct);
        account.SubaccountCode = sameAccount ? current!.SubaccountCode : null;
        return account;
    }

    /// <summary>
    /// Creates the Paystack subaccount that lets Paystack settle this seller's share directly. <paramref name="feePercent"/> is the
    /// platform's default share; each checkout also sends the exact fee (transaction_charge).
    /// </summary>
    public async Task<PayoutAccount> EnableDirectSettlementAsync(PayoutAccount? account, string businessName, decimal feePercent, string? contactEmail,
        CancellationToken ct)
    {
        if (!options.PaystackSubaccounts) throw new ConflictException("Direct settlement isn't available yet.");
        if (account is null || account.BankCode is null || account.VerifiedAt is null)
            throw new ConflictException("Add your bank account and pick your bank from the list first.");
        if (account.SubaccountCode is not null) return account;
        var code = await provider.CreateSubaccountAsync(businessName, account.BankCode, account.AccountNumber, feePercent, contactEmail, ct);
        return Copy(account, code);
    }

    public static PayoutAccount? DisableDirectSettlement(PayoutAccount? account) => account is null ? null : Copy(account, null);

    public static PayoutAccountDto ToDto(PayoutAccount p, bool mask) => new(p.BankName, mask ? AccountMask.Last4(p.AccountNumber) : p.AccountNumber,
        p.AccountName, p.BankCode, p.VerifiedAt is not null && p.BankCode is not null, p.SubaccountCode is not null);

    private static PayoutAccount Copy(PayoutAccount a, string? subaccount) => new()
    {
        BankName = a.BankName, AccountNumber = a.AccountNumber, AccountName = a.AccountName, BankCode = a.BankCode, VerifiedAt = a.VerifiedAt,
        RecipientCode = a.RecipientCode, SubaccountCode = subaccount,
    };

    private static BadRequestException NotRecognised() =>
        BadRequestException.Field("accountNumber", "The bank doesn't recognise this account number. Check the number and the bank.");
}

/// <summary>
/// Paystack split payments. A checkout for a seller with a subaccount (and subaccounts switched on, and Paystack taking the payment) is
/// started with the subaccount and the platform fee; the split is remembered by reference so the earning is recorded as settled by Paystack.
/// </summary>
public sealed class PaymentSplits(IAppDbContext db, PayoutOptions options)
{
    /// <summary>The split for a checkout, or null to collect the whole payment on the platform account as before.</summary>
    public async Task<PaymentSplitTerms?> ForCheckoutAsync(string providerName, Guid tenantId, string reference, long totalMinor, long feeMinor,
        CancellationToken ct)
    {
        if (!options.PaystackSubaccounts || !string.Equals(providerName, BillingOptions.Paystack, StringComparison.OrdinalIgnoreCase)) return null;
        // Paystack needs the seller's share to be positive: a 100% fee is collected whole.
        if (feeMinor < 0 || feeMinor >= totalMinor) return null;
        var account = await db.Academies.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).Select(a => a.PayoutAccount).FirstOrDefaultAsync(ct)
            ?? await db.AgencyProfiles.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).Select(a => a.PayoutAccount).FirstOrDefaultAsync(ct);
        if (account?.SubaccountCode is not { Length: > 0 } code) return null;
        db.PaymentSplits.Add(new PaymentSplit { Reference = reference, TenantId = tenantId, SubaccountCode = code, FeeMinor = feeMinor });
        await db.SaveChangesAsync(ct);
        return new PaymentSplitTerms(code, feeMinor);
    }

    /// <summary>The split a payment was started with, if any.</summary>
    public Task<PaymentSplit?> FindAsync(string reference, CancellationToken ct) =>
        db.PaymentSplits.AsNoTracking().FirstOrDefaultAsync(s => s.Reference == reference, ct);
}

/// <summary>
/// The platform admin's payout queue across every product that collects money through the platform. With automatic transfers on, Finance
/// approves a payout and it is sent by Paystack Transfer (one transfer per approval; Paystack's webhooks and a re-check settle it).
/// </summary>
public sealed class PayoutAdminService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, NotificationQueue notifications, TimeProvider clock,
    PayoutService ledger, IPayoutProvider provider, PayoutOptions options, Saas.Application.Auth.PlatformOptions platform, AppUrls urls,
    ILogger<PayoutAdminService> logger, IServiceScopeFactory scopes)
{
    public PayoutSettingsDto Settings() => new(options.AutomaticTransfers, options.PaystackSubaccounts, provider.IsConfigured);

    public async Task<IReadOnlyList<PayoutDto>> ListAsync(PayoutStatus? status, CancellationToken ct)
    {
        var query = db.Payouts.IgnoreQueryFilters().AsNoTracking();
        if (status is { } s) query = query.Where(p => p.Status == s);
        var payouts = await query.OrderBy(p => p.CreatedAt).Take(500).ToListAsync(ct);
        var tenantIds = payouts.Select(p => p.TenantId).Distinct().ToList();
        var businesses = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        return payouts.Select(p => PayoutService.ToDto(p, businesses.GetValueOrDefault(p.TenantId))).ToList();
    }

    /// <summary>Records that the money was sent by hand (bank transfer from the platform account). Not for a payout Paystack is sending.</summary>
    public async Task<PayoutDto> MarkPaidAsync(Guid id, PaidRequest r, CancellationToken ct)
    {
        var (payout, business) = await LoadAsync(id, ct);
        if (payout.Status != PayoutStatus.Requested)
            throw new ConflictException(payout.Status == PayoutStatus.Processing ? "Paystack is sending this payout." : "This payout was already handled.");
        if (string.IsNullOrWhiteSpace(r.Reference)) throw BadRequestException.Field("reference", "Enter the transfer reference.");
        payout.Status = PayoutStatus.Paid;
        payout.Reference = r.Reference.Trim();
        payout.PaidAt = clock.GetUtcNow();
        await NotifyAsync(payout, business, "payout.paid", ct);
        await db.SaveChangesAsync(ct);
        return PayoutService.ToDto(payout, business);
    }

    /// <summary>Rejecting returns the amount to the business's available balance. A failed transfer can be closed this way too.</summary>
    public async Task<PayoutDto> RejectAsync(Guid id, string? note, CancellationToken ct)
    {
        var (payout, business) = await LoadAsync(id, ct);
        if (payout.Status is not (PayoutStatus.Requested or PayoutStatus.Failed))
            throw new ConflictException(payout.Status == PayoutStatus.Processing ? "Paystack is sending this payout." : "This payout was already handled.");
        payout.Status = PayoutStatus.Rejected;
        payout.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await NotifyAsync(payout, business, "payout.rejected", ct);
        await db.SaveChangesAsync(ct);
        return PayoutService.ToDto(payout, business);
    }

    /// <summary>
    /// Finance's approval: sends the payout by Paystack Transfer. A requested payout gets its first attempt; a failed one a new attempt
    /// with a new reference, if the business's available balance still covers it. The status flip is one conditional update, so two
    /// approvals send one transfer.
    /// </summary>
    public async Task<PayoutDto> SendAsync(Guid id, CancellationToken ct)
    {
        if (!options.AutomaticTransfers)
            throw new ConflictException("Automatic transfers are off. Send the money by bank transfer, then mark the payout paid.");
        if (!provider.IsConfigured) throw new ServiceUnavailableException("Paystack isn't set up on the platform, so transfers can't be sent.");
        var (payout, business) = await LoadAsync(id, ct);
        if (payout.Status == PayoutStatus.Processing) throw new ConflictException("Paystack is already sending this payout.");
        if (payout.Status is not (PayoutStatus.Requested or PayoutStatus.Failed)) throw new ConflictException("This payout was already handled.");
        if (payout.Status == PayoutStatus.Failed && payout.AmountMinor > (await ledger.LedgerAsync(ct)).AvailableMinor)
            throw new ConflictException("The business's available balance no longer covers this payout (it was probably requested again). Reject it instead.");

        var recipient = await RecipientAsync(payout, business, ct);
        var (expected, attempts) = (payout.Status, payout.TransferAttempts);
        var attempt = attempts + 1;
        var reference = attempt == 1 ? $"po_{payout.Id:N}" : $"po_{payout.Id:N}_{attempt}";
        var now = clock.GetUtcNow();
        var approver = user.UserId;
        var claimed = await db.Payouts.Where(p => p.Id == id && p.Status == expected && p.TransferAttempts == attempts)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PayoutStatus.Processing).SetProperty(p => p.TransferAttempts, attempt)
                .SetProperty(p => p.TransferReference, reference).SetProperty(p => p.TransferSentAt, now).SetProperty(p => p.ApprovedBy, approver)
                .SetProperty(p => p.TransferStatus, (string?)null).SetProperty(p => p.TransferCode, (string?)null)
                .SetProperty(p => p.FailureReason, (string?)null).SetProperty(p => p.UpdatedAt, now), ct);
        if (claimed == 0) throw new ConflictException("Paystack is already sending this payout.");
        logger.LogInformation("Payout {PayoutId}: sending transfer {Reference} (attempt {Attempt})", id, reference, attempt);

        var outcome = await provider.TransferAsync(payout.AmountMinor, recipient, reference, Narration(business), business.Currency, CancellationToken.None);
        outcome = await ConfirmRefusalAsync(reference, outcome, CancellationToken.None);
        await ApplyAsync(id, reference, outcome, CancellationToken.None);
        return await DtoAsync(id, ct);
    }

    /// <summary>
    /// A signed transfer webhook. Wrong amounts and references no payout has are ignored; a success for an earlier attempt of a payout
    /// (its reference was replaced by a retry) means the business may have been paid twice, so Finance is told.
    /// </summary>
    public async Task ApplyTransferEventAsync(TransferEvent e, CancellationToken ct)
    {
        var payout = await db.Payouts.IgnoreQueryFilters().AsNoTracking().Where(p => p.TransferReference == e.Reference)
            .Select(p => new { p.Id, p.AmountMinor }).FirstOrDefaultAsync(ct);
        if (payout is null)
        {
            logger.LogInformation("Paystack {Event} for {Reference}: no payout has that transfer reference", e.Event, e.Reference);
            if (e.Status == "success" && PayoutIdOf(e.Reference) is { } earlier
                && await db.Payouts.IgnoreQueryFilters().AnyAsync(p => p.Id == earlier && p.AmountMinor == e.AmountMinor, ct))
            {
                logger.LogError("Paystack transfer.success for {Reference}, an earlier attempt of payout {PayoutId}", e.Reference, earlier);
                var (old, business) = await LoadAsync(earlier, ct);
                await AlertFinanceAsync(old, business, "payout.transfer_late_success", ct, e.Reference,
                    $"This was an earlier attempt ({e.Reference}); the payout's current attempt is {old.TransferReference}.");
                await db.SaveChangesAsync(ct);
            }
            return;
        }
        if (payout.AmountMinor != e.AmountMinor)
        {
            logger.LogWarning("Paystack {Event} for {Reference}: amount {Amount}, expected {Expected}; ignored", e.Event, e.Reference, e.AmountMinor, payout.AmountMinor);
            return;
        }
        await ApplyAsync(payout.Id, e.Reference, new TransferOutcome(TransferStatuses.StateOf(e.Status), e.Status, e.TransferCode, null), ct);
    }

    /// <summary>
    /// The re-check job: asks Paystack about transfers still in flight after <see cref="PayoutOptions.RecheckAfter"/>. Each payout is
    /// checked in its own scope (each belongs to its own business), and one that fails doesn't stop the rest. Runs whether automatic
    /// transfers are on or off, so transfers in flight when they are switched off are still settled.
    /// </summary>
    public async Task<int> RecheckTransfersAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!provider.IsConfigured) return 0;
        var due = now - options.RecheckAfter;
        var ids = await db.Payouts.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == PayoutStatus.Processing && p.TransferReference != null && p.TransferSentAt <= due)
            .OrderBy(p => p.TransferSentAt).Take(100).Select(p => p.Id).ToListAsync(ct);
        var settled = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<PayoutAdminService>().RecheckTransferAsync(id, ct)) settled++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Payout {PayoutId}: the transfer re-check failed", id);
            }
        }
        return settled;
    }

    /// <summary>
    /// One payout of the re-check (call it on a fresh scope). One Paystack definitely never got (verify: 404 or "transfer not found") is
    /// sent again with the same reference, which Paystack treats as the same transfer, but only while automatic transfers are on.
    /// True when the transfer ended (paid or failed).
    /// </summary>
    public async Task<bool> RecheckTransferAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Payouts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == PayoutStatus.Processing, ct);
        if (p?.TransferReference is not { } reference) return false;
        var outcome = await provider.VerifyTransferAsync(reference, ct);
        if (outcome.State == TransferState.NotFound)
        {
            if (!options.AutomaticTransfers || p.Account.RecipientCode is not { } recipient) return false;
            var business = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == p.TenantId, ct);
            logger.LogWarning("Payout {PayoutId}: Paystack has no transfer {Reference}; sending it again with the same reference", p.Id, reference);
            outcome = await ConfirmRefusalAsync(reference, await provider.TransferAsync(p.AmountMinor, recipient, reference, Narration(business), business.Currency, ct), ct);
        }
        await ApplyAsync(p.Id, reference, outcome, ct);
        return outcome.State is TransferState.Success or TransferState.Failed;
    }

    /// <summary>
    /// A refused POST /transfer only fails the payout when Paystack has no transfer with that reference: a refused re-send (a duplicate
    /// reference, say) may stand for a transfer that exists, so verify decides. No clear answer from verify keeps it in flight.
    /// </summary>
    private async Task<TransferOutcome> ConfirmRefusalAsync(string reference, TransferOutcome sent, CancellationToken ct)
    {
        if (sent.State != TransferState.Failed) return sent;
        var check = await provider.VerifyTransferAsync(reference, ct);
        return check.State switch
        {
            TransferState.NotFound => sent,
            TransferState.Unknown => new TransferOutcome(TransferState.Unknown, null, null, null),
            _ => check,
        };
    }

    /// <summary>
    /// Moves a payout on from Paystack's answer for <paramref name="reference"/>. Each step is a conditional update on the current attempt,
    /// so the send call, the webhook and the re-check can all report the same outcome and it is applied once.
    /// </summary>
    private async Task ApplyAsync(Guid id, string reference, TransferOutcome o, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var code = o.TransferCode;
        var current = db.Payouts.IgnoreQueryFilters().Where(p => p.Id == id && p.TransferReference == reference);
        switch (o.State)
        {
            case TransferState.Success:
                // The money left. In flight, it is simply paid. Marked Failed or Rejected earlier (a failure that wasn't, a reversal
                // that was itself reversed), it is paid too, which holds the amount again, and Finance checks nobody was paid twice.
                // A success after a reversal is a stale report (a reversal follows the success), so it never undoes the reversal.
                var before = await current.Where(p => p.Status == PayoutStatus.Processing
                        || ((p.Status == PayoutStatus.Failed || p.Status == PayoutStatus.Rejected) && p.TransferStatus != "reversed"))
                    .Select(p => (PayoutStatus?)p.Status).FirstOrDefaultAsync(ct);
                if (before is not { } was) break;
                if (await current.Where(p => p.Status == was).ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, PayoutStatus.Paid).SetProperty(p => p.PaidAt, now).SetProperty(p => p.Reference, reference)
                        .SetProperty(p => p.TransferStatus, "success").SetProperty(p => p.TransferCode, p => code ?? p.TransferCode)
                        .SetProperty(p => p.FailureReason, (string?)null).SetProperty(p => p.UpdatedAt, now), ct) == 1)
                {
                    var (payout, business) = await LoadAsync(id, ct);
                    await NotifyAsync(payout, business, "payout.paid", ct);
                    if (was != PayoutStatus.Processing)
                    {
                        logger.LogError("Payout {PayoutId}: transfer {Reference} succeeded after the payout was marked {Status}", id, reference, was);
                        await AlertFinanceAsync(payout, business, "payout.transfer_late_success", ct, reason: $"The payout had been marked {was}.");
                    }
                    await db.SaveChangesAsync(ct);
                }
                break;
            case TransferState.Failed:
                var status = o.Status ?? "failed";
                var reason = o.Message ?? (status == "reversed" ? "Paystack reversed the transfer; the money is back in the platform balance."
                    : $"Paystack reports the transfer {status}.");
                if (reason.Length > 500) reason = reason[..500];
                // A reversal can follow a success; anything else only fails a transfer still in flight.
                var failing = status == "reversed"
                    ? current.Where(p => p.Status == PayoutStatus.Processing || p.Status == PayoutStatus.Paid)
                    : current.Where(p => p.Status == PayoutStatus.Processing);
                if (await failing.ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, PayoutStatus.Failed).SetProperty(p => p.PaidAt, (DateTimeOffset?)null)
                        .SetProperty(p => p.TransferStatus, status).SetProperty(p => p.TransferCode, p => code ?? p.TransferCode)
                        .SetProperty(p => p.FailureReason, reason).SetProperty(p => p.UpdatedAt, now), ct) == 1)
                {
                    var (payout, business) = await LoadAsync(id, ct);
                    logger.LogWarning("Payout {PayoutId}: transfer {Reference} {Status}: {Reason}", id, reference, status, reason);
                    await AlertFinanceAsync(payout, business, "payout.transfer_failed", ct);
                    await db.SaveChangesAsync(ct);
                }
                break;
            case TransferState.Pending:
                var pending = o.Status ?? "pending";
                // "otp": the Paystack account still asks for an OTP on each transfer, which an API can't give. Finance must turn it off.
                var stuck = pending == "otp" ? "Paystack is waiting for an OTP. Turn off transfer OTP in the Paystack dashboard (see the deploy notes)." : null;
                var firstOtp = await current.Where(p => p.Status == PayoutStatus.Processing && p.TransferStatus != pending).AnyAsync(ct) && stuck is not null;
                await current.Where(p => p.Status == PayoutStatus.Processing).ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.TransferStatus, pending).SetProperty(p => p.TransferCode, p => code ?? p.TransferCode)
                    .SetProperty(p => p.FailureReason, stuck), ct);
                if (firstOtp)
                {
                    var (payout, business) = await LoadAsync(id, ct);
                    await AlertFinanceAsync(payout, business, "payout.transfer_otp", ct);
                    await db.SaveChangesAsync(ct);
                }
                break;
            default:
                // No answer (or not found yet): stays in flight; the re-check asks again later.
                break;
        }
    }

    private async Task<string> RecipientAsync(Payout payout, Tenant business, CancellationToken ct)
    {
        if (payout.Account.RecipientCode is { Length: > 0 } code) return code;
        if (payout.Account.BankCode is null)
            throw new ConflictException("This bank account wasn't checked with the bank. Ask the business to pick its bank in its payout settings, or pay it by hand.");
        var recipient = await provider.CreateRecipientAsync(payout.Account.AccountName, payout.Account.AccountNumber, payout.Account.BankCode, business.Currency, ct);
        payout.Account = new PayoutAccount
        {
            BankName = payout.Account.BankName, AccountNumber = payout.Account.AccountNumber, AccountName = payout.Account.AccountName,
            BankCode = payout.Account.BankCode, VerifiedAt = payout.Account.VerifiedAt, RecipientCode = recipient,
        };
        await db.SaveChangesAsync(ct);
        return recipient;
    }

    private static string Narration(Tenant business)
    {
        var text = $"LinkSuite payout {business.Name}";
        return text.Length > 100 ? text[..100] : text;
    }

    private async Task<PayoutDto> DtoAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var payout = await db.Payouts.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == id, ct);
        return PayoutService.ToDto(payout, await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == payout.TenantId, ct));
    }

    private async Task<(Payout, Tenant)> LoadAsync(Guid id, CancellationToken ct)
    {
        var tenantId = await db.Payouts.IgnoreQueryFilters().Where(p => p.Id == id).Select(p => (Guid?)p.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payout");
        if (tenant.TenantId != tenantId) tenant.SetTenant(tenantId);
        db.ChangeTracker.Clear();
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

    /// <summary>Emails Finance (and super admins); with nobody in Finance yet, the configured platform addresses, so it is never missed.</summary>
    private async Task AlertFinanceAsync(Payout payout, Tenant business, string template, CancellationToken ct, string? reference = null, string? reason = null)
    {
        var finance = await (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                             where a.RemovedAt == null && (a.Role == PlatformRole.Finance || a.Role == PlatformRole.SuperAdmin) && u.Email != null
                             select u.Email!).Distinct().ToListAsync(ct);
        if (finance.Count == 0)
            finance = platform.AdminEmails.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var email in finance)
            notifications.Email(email, template, new()
            {
                ["business"] = business.Name, ["amount"] = Money.Format(payout.AmountMinor, business.Currency),
                ["reference"] = reference ?? payout.TransferReference ?? "", ["status"] = payout.TransferStatus ?? "",
                ["reason"] = reason ?? payout.FailureReason ?? "", ["link"] = $"{urls.Web("admin")}/money/payouts",
            });
    }

    /// <summary>The payout a transfer reference was made for (<c>po_{id}</c> or <c>po_{id}_{attempt}</c>), if it is one of ours.</summary>
    private static Guid? PayoutIdOf(string reference) =>
        reference.StartsWith("po_", StringComparison.Ordinal) && reference.Length >= 35 && Guid.TryParseExact(reference[3..35], "N", out var id) ? id : null;
}

/// <summary>Bank account numbers shown in a support view (and other places that only need to recognise the account).</summary>
public static class AccountMask
{
    public static string Last4(string number) => number.Length <= 4 ? number : "••••" + number[^4..];
}
