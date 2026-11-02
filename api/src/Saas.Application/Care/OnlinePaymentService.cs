using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary><c>Force</c> changes the key even while patients are in the middle of paying.</summary>
public sealed record CarePaystackKeyRequest(string? SecretKey, bool Force = false);
/// <summary><c>Available</c>: the clinic's plan includes online payments. <c>Connected</c>: a key is saved (never returned).</summary>
public sealed record CarePaystackSettingsDto(bool Connected, bool Available, string WebhookUrl);
public sealed record CareCheckoutResult(string AuthorizationUrl, string Reference);
public sealed record CareOnlineStatusDto(Guid Id, string Reference, Guid BillId, long AmountMinor, CarePaymentStatus Status, string? ReceiptNumber, long BillBalanceMinor);

/// <summary>
/// Online bill payments into the clinic's own Paystack account, the way SchoolLink takes fees (<c>FeePaymentService</c>):
/// <list type="bullet">
/// <item>the medical director saves the key (encrypted with <see cref="ISecretProtector"/>; Clinic plan or higher, otherwise 402); a
/// replaced key is still accepted for 7 days so checkouts started under it confirm;</item>
/// <item>the portal (Task 15) starts a checkout: a Pending payment with reference <c>care_{billId}_{random}</c>;</item>
/// <item>the webhook (signed with the clinic key; a webhook signed only with the previous key is checked with Paystack) and the return
/// verify both call <see cref="ConfirmAsync"/>, whose conditional claim (Pending or Abandoned → Succeeded) under the bill lock records the
/// payment at most once;</item>
/// <item>a different amount or currency becomes Mismatch (not applied; the director and cashiers are told); a charge for more than the balance
/// at that moment is recorded and the bill flagged for review;</item>
/// <item>Abandoned (set by <see cref="RecheckPendingAsync"/>) is a soft state: a later confirmation is still applied.</item>
/// </list>
/// </summary>
public sealed class OnlinePaymentService(
    IAppDbContext db,
    ITenantContext tenant,
    CareAccess access,
    BillingService billing,
    PaymentService desk,
    PlanService plans,
    IMerchantPayments payments,
    ISecretProtector secrets,
    CareNotices notices,
    AppUrls urls,
    TimeProvider clock,
    ILogger<OnlinePaymentService> logger,
    MerchantKeyRules keyRules)
{
    /// <summary>The smallest online part-payment: ₦1,000, or the balance when that is smaller.</summary>
    public const long MinimumPartPaymentMinor = 100_000;
    public const string ReferencePrefix = "care_";
    public const string NotSetUp = "Online payment isn't set up for this clinic.";
    public const string PlanNeeded = "Online payments aren't included in your plan. Upgrade to the Clinic plan to take payments online.";
    public const string KeyChangeBusy = "Some patients are in the middle of paying. Try again in a few minutes, or ask support.";
    public const string MismatchKind = CareNotices.PaymentMismatch;
    public const string UnverifiedKind = CareNotices.PaymentUnverified;
    public static readonly TimeSpan OpenCheckoutWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan PreviousKeyWindow = TimeSpan.FromDays(7);
    /// <summary>A checkout is only stranded once the patient has had this long to finish it.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(1);
    /// <summary>Checkouts older than this are no longer asked about.</summary>
    public static readonly TimeSpan RecheckUntil = TimeSpan.FromDays(7);
    /// <summary>How long one clinic's stranded payment check may run before leaving the rest for the next run.</summary>
    public static readonly TimeSpan RecheckBudget = TimeSpan.FromMinutes(2);
    /// <summary>Ruling (Task 13 M5): a Pending checkout is asked about at most this often in its first 24 hours...</summary>
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(10);
    /// <summary>...and at most this often after that.</summary>
    public static readonly TimeSpan RecheckEveryLater = TimeSpan.FromHours(1);

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Paystack key (the medical director) ----------

    public async Task<CarePaystackSettingsDto> GetPaystackAsync(CancellationToken ct)
    {
        await OwnerAsync(ct, write: false);
        return await SettingsAsync((await ProfileAsync(ct)).PaystackSecretKeyProtected is not null, ct);
    }

    /// <summary>Saves the clinic's Paystack secret key: the medical director, on a plan with online payments (402 otherwise).</summary>
    public async Task<CarePaystackSettingsDto> SetPaystackKeyAsync(CarePaystackKeyRequest r, CancellationToken ct)
    {
        await OwnerAsync(ct, write: true);
        if (!await plans.HasFeatureAsync(LimitKeys.OnlinePayments, ct)) throw new PlanLimitException(PlanNeeded);
        var key = keyRules.Validate(r.SecretKey);
        var profile = await ProfileAsync(ct);
        if (profile.PaystackSecretKeyProtected is { } saved && secrets.Unprotect(saved) == key) return await SettingsAsync(true, ct);
        await EnsureNoOpenCheckoutsAsync(r.Force, ct);
        RetireCurrentKey(profile);
        profile.PaystackSecretKeyProtected = secrets.Protect(key);
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(true, ct);
    }

    public async Task RemovePaystackKeyAsync(bool force, CancellationToken ct)
    {
        await OwnerAsync(ct, write: true);
        var profile = await ProfileAsync(ct);
        if (profile.PaystackSecretKeyProtected is null) return;
        await EnsureNoOpenCheckoutsAsync(force, ct);
        RetireCurrentKey(profile);
        profile.PaystackSecretKeyProtected = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task OwnerAsync(CancellationToken ct, bool write)
    {
        var caller = await access.CallerAsync(ct);
        if (caller.MemberRole != MemberRole.Owner || (write && caller.IsSupportView)) throw new ForbiddenException();
    }

    /// <summary>A checkout started in the last day may still be paid under the current key; changing it then needs <c>force</c>.</summary>
    private async Task EnsureNoOpenCheckoutsAsync(bool force, CancellationToken ct)
    {
        if (force) return;
        var since = clock.GetUtcNow() - OpenCheckoutWindow;
        if (await db.CarePayments.AnyAsync(p => p.Status == CarePaymentStatus.Pending && p.CreatedAt > since, ct))
            throw new ConflictException(KeyChangeBusy, "checkouts_open");
    }

    /// <summary>The key being replaced or removed stays usable for webhooks and verify for <see cref="PreviousKeyWindow"/>.</summary>
    private void RetireCurrentKey(ClinicProfile profile)
    {
        if (profile.PaystackSecretKeyProtected is null) return;
        profile.PaystackPreviousKeyProtected = profile.PaystackSecretKeyProtected;
        profile.PreviousKeyUntil = clock.GetUtcNow() + PreviousKeyWindow;
    }

    /// <summary>When the previous key was retired (its 7 days end at <see cref="ClinicProfile.PreviousKeyUntil"/>).</summary>
    private static DateTimeOffset? RetiredAt(ClinicProfile profile) => profile.PreviousKeyUntil - PreviousKeyWindow;

    /// <summary>The keys a confirmation may be checked with: the current key, then the previous one while its 7 days last.</summary>
    private List<string> ConfirmationKeys(ClinicProfile profile)
    {
        var keys = new List<string>(2);
        if (profile.PaystackSecretKeyProtected is { } current) keys.Add(secrets.Unprotect(current));
        if (profile.PaystackPreviousKeyProtected is { } previous && profile.PreviousKeyUntil > clock.GetUtcNow())
            keys.Add(secrets.Unprotect(previous));
        return keys;
    }

    private async Task<CarePaystackSettingsDto> SettingsAsync(bool connected, CancellationToken ct) =>
        new(connected, await plans.HasFeatureAsync(LimitKeys.OnlinePayments, ct), $"{urls.ApiBaseUrl.TrimEnd('/')}/api/webhooks/paystack/care/{TenantId}");

    private async Task<ClinicProfile> ProfileAsync(CancellationToken ct) =>
        await db.ClinicProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");

    /// <summary>True when the clinic can take payments online: a plan with online payments and a saved Paystack key.</summary>
    private async Task<bool> OnlineReadyAsync(ClinicProfile profile, CancellationToken ct) =>
        profile.PaystackSecretKeyProtected is not null && await plans.HasFeatureAsync(LimitKeys.OnlinePayments, ct);

    // ---------- Checkout (the portal) ----------

    /// <summary>
    /// Starts a Paystack checkout with the clinic's own key for <paramref name="payerUserId"/> (the portal checks the patient link first,
    /// Task 15). The amount is the balance by default, or a part of at least ₦1,000 (or the balance when smaller). A Pending payment row
    /// holds the reference (care_{billId}_{random}) until Paystack confirms. 409 when online payment isn't set up or the bill owes nothing.
    /// </summary>
    public async Task<CareCheckoutResult> StartCheckoutAsync(Guid billId, Guid payerUserId, long? amountMinor, CancellationToken ct)
    {
        var bill = await db.PatientBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, ct) ?? throw new NotFoundException("Bill");
        var profile = await db.ClinicProfiles.AsNoTracking().FirstAsync(ct);
        if (!await OnlineReadyAsync(profile, ct)) throw new ConflictException(NotSetUp, "online_payments_unavailable");
        if (bill.BalanceMinor <= 0) throw new ConflictException("This bill is already paid.", "bill_paid");

        var currency = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.Currency).FirstAsync(ct);
        var amount = amountMinor ?? bill.BalanceMinor;
        var minimum = Math.Min(MinimumPartPaymentMinor, bill.BalanceMinor);
        if (amount > bill.BalanceMinor)
            throw BadRequestException.Field("amountMinor", $"The balance is {Money.Format(bill.BalanceMinor, currency)}. Enter that amount or less.");
        if (amount < minimum) throw BadRequestException.Field("amountMinor", $"Pay at least {Money.Format(minimum, currency)}, or the full balance.");

        var email = await db.Users.Where(u => u.Id == payerUserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email)) throw BadRequestException.Field("email", "Add an email address to your account to pay online.");

        var reference = $"{ReferencePrefix}{billId:N}_{Guid.NewGuid():N}"[..(ReferencePrefix.Length + 32 + 1 + 16)];
        db.CarePayments.Add(new CarePayment
        {
            TenantId = TenantId, BillId = bill.Id, PatientId = bill.PatientId, AmountMinor = amount, Currency = currency, Method = PayMethod.Online,
            Status = CarePaymentStatus.Pending, ProviderRef = reference, PayerUserId = payerUserId, PaidAt = clock.GetUtcNow(),
        });
        // Saved before Paystack is called, so a webhook can never arrive for a reference we don't know.
        await db.SaveChangesAsync(ct);
        var url = await payments.InitializeAsync(secrets.Unprotect(profile.PaystackSecretKeyProtected!), new PaymentInit(
            email, amount, currency, reference, $"{urls.Web(Products.Care)}/portal/pay/return?reference={Uri.EscapeDataString(reference)}",
            new() { ["tenantId"] = TenantId.ToString(), ["bill"] = bill.Number }), ct);
        return new CareCheckoutResult(url, reference);
    }

    // ---------- Verify ----------

    /// <summary>The cashier's (or leadership's) "check with Paystack" for an online checkout.</summary>
    public async Task<CareOnlineStatusDto> VerifyForStaffAsync(string reference, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PriceListService.EnsureWriter(caller);
        // A verify can record a payment, issue a receipt number and send notices: a write, so never from support's read-only view.
        if (caller.IsSupportView) throw new ForbiddenException();
        return await VerifyAsync(reference, ct);
    }

    /// <summary>
    /// The return verify, in the clinic's tenant (the portal checks that the caller started the payment, Task 15). Paystack is asked
    /// directly; a successful charge is confirmed exactly as the webhook would. 404 for an unknown reference.
    /// </summary>
    public async Task<CareOnlineStatusDto> VerifyAsync(string reference, CancellationToken ct)
    {
        var payment = await db.CarePayments.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderRef == reference, ct) ?? throw new NotFoundException("Payment");
        if (IsOpen(payment.Status))
        {
            var profile = await db.ClinicProfiles.AsNoTracking().FirstAsync(ct);
            var keys = ConfirmationKeys(profile);
            var (charge, unknown, knownUnpaid) = await VerifyWithKeysAsync(keys, reference, ct);
            if (charge is not null) await ConfirmAsync(TenantId, charge, ct);
            // Not found with any key: usually the patient hasn't finished paying. When the key changed after the checkout started (or there
            // is no key left), the charge may sit in an account we can no longer reach, so the clinic is told.
            else if (!unknown && !knownUnpaid && (keys.Count == 0 || RetiredAt(profile) > payment.CreatedAt))
                await AlertUnverifiedAsync(payment, ct);
        }
        db.ChangeTracker.Clear();
        var p = await db.CarePayments.AsNoTracking().FirstAsync(x => x.ProviderRef == reference, ct);
        var balance = await db.PatientBills.AsNoTracking().Where(b => b.Id == p.BillId).Select(b => b.BalanceMinor).FirstAsync(ct);
        return new CareOnlineStatusDto(p.Id, reference, p.BillId, p.AmountMinor, p.Status, p.ReceiptNumber, balance);
    }

    /// <summary>Pending checkouts, and Abandoned ones (the job stopped asking, but a late charge must still be applied), can be confirmed.</summary>
    private static bool IsOpen(CarePaymentStatus status) => status is CarePaymentStatus.Pending or CarePaymentStatus.Abandoned;

    /// <summary>
    /// Asks Paystack about the reference with each key. <c>Unknown</c>: a key couldn't be checked (5xx, 429, network, timeout).
    /// <c>KnownUnpaid</c>: an account knows the reference but reports it unpaid (abandoned, failed).
    /// </summary>
    private async Task<(VerifiedCharge? Charge, bool Unknown, bool KnownUnpaid)> VerifyWithKeysAsync(IEnumerable<string> keys, string reference, CancellationToken ct)
    {
        var unknown = false;
        var knownUnpaid = false;
        foreach (var key in keys)
        {
            try
            {
                var lookup = await payments.LookupAsync(key, reference, ct);
                if (lookup.Charge is { } c && c.Reference == reference) return (c, false, false);
                if (lookup.KnownUnpaid) knownUnpaid = true;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Care payment {Reference}: Paystack was unavailable when verifying", reference);
                unknown = true;
            }
        }
        return (null, unknown, knownUnpaid);
    }

    // ---------- Stranded checkouts and old keys (Task 13's care jobs call these) ----------

    /// <summary>
    /// Checks stranded online checkouts (webhook missed, patient never came back): Pending payments older than an hour and younger than 7
    /// days are verified with every key and confirmed when Paystack has the charge. One older than 24 hours that no key finds is reported to
    /// the clinic (once) and marked Abandoned; one Paystack knows but reports unpaid is marked Abandoned quietly. Abandoned is soft: a later
    /// webhook or verify still applies it. Runs in the clinic's tenant scope within <see cref="RecheckBudget"/>; returns how many changed.
    /// Each checkout is asked about at most every 10 minutes in its first 24 hours, then hourly (<see cref="CarePayment.RecheckedAt"/>).
    /// </summary>
    public async Task<int> RecheckPendingAsync(DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.ClinicProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (profile is null) return 0;
        var newest = now - RecheckAfter;
        var oldest = now - RecheckUntil;
        var dayOld = now - OpenCheckoutWindow;
        var soonDue = now - RecheckEvery;
        var lateDue = now - RecheckEveryLater;
        // Throttled (M5): under a day old, at most every 10 minutes; after that, hourly.
        var pending = await db.CarePayments.AsNoTracking()
            .Where(p => p.Method == PayMethod.Online && p.Status == CarePaymentStatus.Pending && p.ProviderRef != null && p.CreatedAt <= newest && p.CreatedAt > oldest)
            .Where(p => p.RecheckedAt == null
                        || (p.CreatedAt > dayOld && p.RecheckedAt <= soonDue)
                        || (p.CreatedAt <= dayOld && p.RecheckedAt <= lateDue))
            .OrderBy(p => p.CreatedAt).Take(200).ToListAsync(ct);
        if (pending.Count == 0) return 0;
        var keys = ConfirmationKeys(profile);
        var changed = 0;
        var started = clock.GetTimestamp();
        foreach (var payment in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) > RecheckBudget)
            {
                logger.LogWarning("Care payments: the stranded payment check ran out of time; the rest are checked on the next run");
                break;
            }
            try
            {
                // Stamped before asking (outside any transaction), so a failed or slow check also waits its turn; the condition keeps a
                // second instance from asking at the same moment.
                var previous = payment.RecheckedAt;
                var claimed = await db.CarePayments.Where(p => p.Id == payment.Id && p.Status == CarePaymentStatus.Pending && p.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecheckedAt, (DateTimeOffset?)now), ct);
                if (claimed == 0) continue;
                var reference = payment.ProviderRef!;
                var (charge, unknown, knownUnpaid) = await VerifyWithKeysAsync(keys, reference, ct);
                if (charge is not null)
                {
                    await ConfirmAsync(TenantId, charge, ct);
                    changed++;
                }
                else if (!unknown && payment.CreatedAt <= now - OpenCheckoutWindow)
                {
                    if (!knownUnpaid) await AlertUnverifiedAsync(payment, ct);
                    db.ChangeTracker.Clear();
                    changed += await db.CarePayments.Where(p => p.Id == payment.Id && p.Status == CarePaymentStatus.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, CarePaymentStatus.Abandoned).SetProperty(p => p.UpdatedAt, now), ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Care payment {Reference}: the stranded payment check failed", payment.ProviderRef);
            }
        }
        return changed;
    }

    /// <summary>Forgets the previous Paystack key once its 7 days are over. Runs in the clinic's tenant scope; true when a key was removed.</summary>
    public async Task<bool> RetirePreviousKeyAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.ClinicProfiles.Where(p => p.PreviousKeyUntil != null && p.PreviousKeyUntil <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaystackPreviousKeyProtected, (string?)null).SetProperty(p => p.PreviousKeyUntil, (DateTimeOffset?)null), ct) > 0;

    // ---------- Webhook ----------

    /// <summary>
    /// The clinic's own Paystack webhook. False (401) when the clinic or its key is unknown or the signature is wrong; nothing is recorded
    /// then. A signed event for an unknown reference, or another clinic's, is acknowledged and ignored. A webhook signed only with the
    /// previous key is not trusted as sent: Paystack is asked about the reference and only its answer is applied.
    /// </summary>
    public async Task<bool> HandleWebhookAsync(Guid tenantId, string rawBody, string? signature,
        Func<string, string, string?, bool> isValidSignature, Func<string, VerifiedCharge?> parse, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Product == Products.Care, ct)) return false;
        tenant.SetTenant(tenantId);
        var profile = await db.ClinicProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var keys = profile is null ? new List<string>() : ConfirmationKeys(profile);
        var current = profile?.PaystackSecretKeyProtected is { } protectedKey ? secrets.Unprotect(protectedKey) : null;
        var signedByCurrent = current is not null && isValidSignature(current, rawBody, signature);
        if (!signedByCurrent && !keys.Any(key => isValidSignature(key, rawBody, signature)))
        {
            logger.LogWarning("Rejected a care Paystack webhook for {TenantId}: no key or bad signature", tenantId);
            return false;
        }
        var charge = TryParse(parse, rawBody);
        if (charge is null || !charge.Reference.StartsWith(ReferencePrefix, StringComparison.Ordinal)) return true;
        if (signedByCurrent)
        {
            await ConfirmAsync(tenantId, charge, ct);
            return true;
        }
        if (!await db.CarePayments.AnyAsync(p => p.ProviderRef == charge.Reference && (p.Status == CarePaymentStatus.Pending || p.Status == CarePaymentStatus.Abandoned), ct)) return true;
        if ((await VerifyWithKeysAsync(keys.AsEnumerable().Reverse(), charge.Reference, ct)).Charge is { } verified)
        {
            await ConfirmAsync(tenantId, verified, ct);
            return true;
        }
        logger.LogWarning("Care payment {Reference}: a webhook signed with the previous Paystack key wasn't confirmed by Paystack; ignored", charge.Reference);
        return true;
    }

    private VerifiedCharge? TryParse(Func<string, VerifiedCharge?> parse, string rawBody)
    {
        try { return parse(rawBody); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "Ignored an unreadable care Paystack webhook");
            return null;
        }
    }

    // ---------- Confirm ----------

    /// <summary>
    /// Applies a charge Paystack reports as successful, once. The Pending (or Abandoned) row is claimed with a conditional update under the
    /// clinic's bill lock, so the webhook and the return verify arriving together record one payment. A different amount or currency is not
    /// applied: the row becomes Mismatch, an audit row (ids and amounts) is written and the director and cashiers are told. A charge for more
    /// than the balance at this moment is still recorded; the bill is flagged for review and the surplus shows as credit.
    /// </summary>
    public async Task ConfirmAsync(Guid tenantId, VerifiedCharge charge, CancellationToken ct)
    {
        if (tenant.TenantId is { } current && current != tenantId) return; // never another clinic's reference
        if (tenant.TenantId is null) tenant.SetTenant(tenantId);
        var row = await db.CarePayments.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderRef == charge.Reference, ct);
        if (row is null || row.Method != PayMethod.Online || !IsOpen(row.Status)) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(BillingService.LockKey(tenantId), ct);
        var now = clock.GetUtcNow();
        var open = db.CarePayments.Where(p => p.Id == row.Id && (p.Status == CarePaymentStatus.Pending || p.Status == CarePaymentStatus.Abandoned));
        var bill = await db.PatientBills.AsNoTracking().Where(b => b.Id == row.BillId).Select(b => new { b.Number, b.BalanceMinor }).FirstAsync(ct);

        if (charge.AmountMinor != row.AmountMinor || !string.Equals(charge.Currency, row.Currency, StringComparison.OrdinalIgnoreCase))
        {
            var currency = charge.Currency.ToUpperInvariant();
            var flagged = await open.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, CarePaymentStatus.Mismatch)
                .SetProperty(p => p.ChargedAmountMinor, charge.AmountMinor)
                .SetProperty(p => p.ChargedCurrency, currency)
                .SetProperty(p => p.UpdatedAt, now), ct);
            if (flagged == 0) return;
            // M1: a mismatched charge needs review: the patient's money may be sitting in Paystack, so the bill isn't chased as a plain debt.
            await db.PatientBills.Where(b => b.Id == row.BillId).ExecuteUpdateAsync(s => s.SetProperty(b => b.NeedsReview, true).SetProperty(b => b.UpdatedAt, now), ct);
            logger.LogWarning("Care payment {Reference}: Paystack reported {Amount} {Currency}, expected {Expected} {ExpectedCurrency}",
                charge.Reference, charge.AmountMinor, charge.Currency, row.AmountMinor, row.Currency);
            db.AuditLogs.Add(Audit("care.payment_mismatch", row, new
            {
                paymentId = row.Id, billId = row.BillId, reference = charge.Reference, expectedMinor = row.AmountMinor, row.Currency,
                receivedMinor = charge.AmountMinor, receivedCurrency = currency,
            }, now));
            await notices.PaymentAlertAsync(MismatchKind, "Online payment needs checking",
                $"Paystack reported {Money.Format(charge.AmountMinor, currency)} instead of {Money.Format(row.AmountMinor, row.Currency)} on bill {bill.Number}. It was not applied.",
                row.BillId, charge.Reference, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        var claimed = await open.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, CarePaymentStatus.Succeeded)
            .SetProperty(p => p.PaidAt, charge.PaidAt)
            .SetProperty(p => p.UpdatedAt, now), ct);
        if (claimed == 0) return; // the other caller got here first
        db.ChangeTracker.Clear();
        var payment = await db.CarePayments.FirstAsync(p => p.Id == row.Id, ct);
        payment.ReceiptNumber = await desk.NextReceiptAsync(charge.PaidAt, ct);
        var tracked = await db.PatientBills.FirstAsync(b => b.Id == row.BillId, ct);
        var paid = await db.PatientBills.AsNoTracking().Where(b => b.Id == row.BillId).Select(b => b.PaidMinor).FirstAsync(ct);
        tracked.PaidMinor = checked(paid + payment.AmountMinor);
        if (payment.AmountMinor > bill.BalanceMinor)
        {
            // The balance changed after the patient started (a desk payment, an adjustment): recorded, and the cashier reviews the surplus.
            tracked.NeedsReview = true;
            db.AuditLogs.Add(Audit("care.payment_overpaid", payment, new
            {
                paymentId = payment.Id, billId = payment.BillId, amountMinor = payment.AmountMinor, balanceBeforeMinor = bill.BalanceMinor,
            }, now));
        }
        await db.SaveChangesAsync(ct);
        await billing.RecalculateAsync(payment.BillId, ct);
        // The patient's portal users hear the amount, the bill and the receipt number (Task 14), committed with the payment.
        await notices.PaymentReceivedAsync(payment.PatientId, bill.Number, payment.AmountMinor, payment.ReceiptNumber!, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>A checkout couldn't be checked with any of the clinic's keys. Nothing is applied; the director and cashiers are told once.</summary>
    private async Task AlertUnverifiedAsync(CarePayment payment, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // The marker and the alerts commit together (Task 14): a failed save can't leave the checkout marked but nobody told.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var marked = await db.CarePayments.Where(p => p.Id == payment.Id && p.UnverifiedAlertedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UnverifiedAlertedAt, now), ct);
        if (marked == 0) return;
        var number = await db.PatientBills.Where(b => b.Id == payment.BillId).Select(b => b.Number).FirstAsync(ct);
        await notices.PaymentAlertAsync(UnverifiedKind, "Online payment couldn't be checked",
            $"Check reference {payment.ProviderRef} in your Paystack dashboard. Nothing was applied to bill {number}.", payment.BillId,
            payment.ProviderRef ?? "", ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private AuditLog Audit(string action, CarePayment payment, object after, DateTimeOffset now) => new()
    {
        TenantId = TenantId, Action = action, EntityType = nameof(CarePayment), EntityId = payment.Id.ToString(),
        AfterJson = JsonSerializer.Serialize(after), CreatedAt = now,
    };
}
