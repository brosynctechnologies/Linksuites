using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary><c>Force</c> changes the key even while parents are in the middle of paying.</summary>
public sealed record SchoolPaystackKeyRequest(string? SecretKey, bool Force = false);
public sealed record PaystackSettingsDto(bool Connected, string WebhookUrl);
public sealed record PayOnlineRequest(long AmountMinor);
public sealed record PayOnlineResult(string AuthorizationUrl, string Reference);
public sealed record RecordPaymentRequest(long AmountMinor, string? Method, string? Reference, DateOnly? PaidOn);
public sealed record ReversePaymentRequest(string? Reason);
public sealed record FeePaymentDto(
    Guid Id, Guid InvoiceId, long AmountMinor, string Currency, string Method, string Status, string? Reference, DateOnly PaidOn,
    string? ReceiptNumber, DateTimeOffset? ReversedAt, string? ReversalReason);
public sealed record ReversePaymentResult(FeePaymentDto Payment, string Message);
public sealed record PaymentStatusDto(Guid Id, string Reference, Guid StudentId, Guid InvoiceId, long AmountMinor, string Status, string? ReceiptNumber, long InvoiceBalance);
public sealed record ParentInvoiceDto(
    Guid Id, string Number, Guid TermId, string TermName, long Total, long Paid, long Balance, string Status, DateOnly DueDate,
    bool CanPayOnline, long? NextInstalmentMinor, long MinimumPartPaymentMinor,
    IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<InstalmentDto> Instalments, IReadOnlyList<FeePaymentDto> Payments);

/// <summary>What a fee receipt prints. Amounts are already formatted.</summary>
public sealed record ReceiptData(
    string SchoolName, string? Address, byte[]? Logo, string ReceiptNumber, string PaidOn, string StudentName, string AdmissionNo,
    string ClassName, string InvoiceNumber, string TermTitle, string Amount, string Method, string? Reference, string InvoiceBalance,
    string? ReversedNote, bool ShowBranding);

/// <summary>
/// Fee payments into the school's own Paystack account (online), payments the bursar records (cash, transfer, POS), receipts and
/// reversals. Every change to an invoice's payments runs under the per-school fee lock (<see cref="FeeService.GenerateLockKey"/>) and
/// recalculates the invoice in the same transaction. An online payment is a Pending row created when the parent starts paying; the
/// webhook and the return verify both call <see cref="ConfirmAsync"/>, whose conditional claim (Pending → Succeeded) applies it once.
/// </summary>
public sealed class FeePaymentService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    ParentAccess parents,
    FeeService fees,
    PlanService plans,
    IMerchantPayments payments,
    ISecretProtector secrets,
    NotificationQueue notifications,
    SchoolFeed feed,
    ISchoolPdf pdf,
    IFileStorage storage,
    AppUrls urls,
    TimeProvider clock,
    ILogger<FeePaymentService> logger,
    MerchantKeyRules keyRules)
{
    /// <summary>The smallest online part-payment: ₦1,000, or the balance when that is smaller.</summary>
    public const long MinimumPartPaymentMinor = 100_000;
    public const string ReferencePrefix = "sfee_";
    public const string NotSetUp = "Online payment isn't set up for this school.";
    public const string KeyChangeBusy = "Some parents are in the middle of paying. Try again in a few minutes, or ask support.";
    public static readonly TimeSpan OpenCheckoutWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan PreviousKeyWindow = TimeSpan.FromDays(7);
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Paystack key (Owner) ----------

    public async Task<PaystackSettingsDto> GetPaystackAsync(CancellationToken ct) =>
        Settings((await ProfileAsync(ct)).PaystackSecretKeyProtected is not null);

    public async Task<PaystackSettingsDto> SetPaystackKeyAsync(SchoolPaystackKeyRequest r, CancellationToken ct)
    {
        var key = keyRules.Validate(r.SecretKey);
        var profile = await ProfileAsync(ct);
        if (profile.PaystackSecretKeyProtected is { } saved && secrets.Unprotect(saved) == key) return Settings(true);
        await EnsureNoOpenCheckoutsAsync(r.Force, ct);
        RetireCurrentKey(profile);
        profile.PaystackSecretKeyProtected = secrets.Protect(key);
        await db.SaveChangesAsync(ct);
        return Settings(true);
    }

    public async Task RemovePaystackKeyAsync(bool force, CancellationToken ct)
    {
        var profile = await ProfileAsync(ct);
        if (profile.PaystackSecretKeyProtected is null) return;
        await EnsureNoOpenCheckoutsAsync(force, ct);
        RetireCurrentKey(profile);
        profile.PaystackSecretKeyProtected = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A checkout started in the last day may still be paid under the current key; changing it then needs <c>force</c>.</summary>
    private async Task EnsureNoOpenCheckoutsAsync(bool force, CancellationToken ct)
    {
        if (force) return;
        var since = clock.GetUtcNow() - OpenCheckoutWindow;
        if (await db.FeePayments.AnyAsync(p => p.Status == FeePaymentStatus.Pending && p.CreatedAt > since, ct))
            throw new ConflictException(KeyChangeBusy, "checkouts_open");
    }

    /// <summary>The key being replaced or removed stays usable for webhooks and verify for <see cref="PreviousKeyWindow"/>.</summary>
    private void RetireCurrentKey(SchoolProfile profile)
    {
        if (profile.PaystackSecretKeyProtected is null) return;
        profile.PreviousPaystackSecretKeyProtected = profile.PaystackSecretKeyProtected;
        profile.PreviousKeyRetiredAt = clock.GetUtcNow();
    }

    /// <summary>The keys a confirmation may be checked with: the current key, then the previous one while its 7 days last.</summary>
    private List<string> ConfirmationKeys(SchoolProfile profile)
    {
        var keys = new List<string>(2);
        if (profile.PaystackSecretKeyProtected is { } current) keys.Add(secrets.Unprotect(current));
        if (profile.PreviousPaystackSecretKeyProtected is { } previous && profile.PreviousKeyRetiredAt > clock.GetUtcNow() - PreviousKeyWindow)
            keys.Add(secrets.Unprotect(previous));
        return keys;
    }

    private PaystackSettingsDto Settings(bool connected) =>
        new(connected, $"{urls.ApiBaseUrl.TrimEnd('/')}/api/webhooks/paystack/school/{TenantId}");

    private async Task<SchoolProfile> ProfileAsync(CancellationToken ct) =>
        await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");

    /// <summary>True when the school can take fees online: a plan with online fees and a saved Paystack key.</summary>
    private async Task<bool> OnlineReadyAsync(SchoolProfile profile, CancellationToken ct) =>
        profile.PaystackSecretKeyProtected is not null && await plans.HasFeatureAsync(LimitKeys.OnlineFees, ct);

    // ---------- Parent: invoices and paying online ----------

    /// <summary>The child's invoices in their school, newest term first, with payments and whether Pay now is offered.</summary>
    public async Task<IReadOnlyList<ParentInvoiceDto>> ParentInvoicesAsync(Guid studentId, CancellationToken ct)
    {
        await parents.RequireChildAsync(studentId, ct);
        var profile = await db.SchoolProfiles.AsNoTracking().FirstAsync(ct);
        var online = await OnlineReadyAsync(profile, ct);
        var invoices = await (from i in db.StudentInvoices.AsNoTracking()
                              join e in db.Enrolments on i.EnrolmentId equals e.Id
                              join t in db.Terms on i.TermId equals t.Id
                              where e.StudentId == studentId
                              orderby t.StartDate descending
                              select new { Invoice = i, TermName = t.Name }).ToListAsync(ct);
        var ids = invoices.Select(x => x.Invoice.Id).ToList();
        var lines = (await db.InvoiceLines.AsNoTracking().Where(l => ids.Contains(l.InvoiceId)).OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToListAsync(ct)).ToLookup(l => l.InvoiceId);
        var instalments = (await db.Instalments.AsNoTracking().Where(x => ids.Contains(x.InvoiceId)).OrderBy(x => x.DueDate).ToListAsync(ct)).ToLookup(x => x.InvoiceId);
        var paid = (await db.FeePayments.AsNoTracking().Where(p => ids.Contains(p.InvoiceId) && p.Status == FeePaymentStatus.Succeeded)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.CreatedAt).ToListAsync(ct)).ToLookup(p => p.InvoiceId);
        return invoices.Select(x =>
        {
            var i = x.Invoice;
            var plan = instalments[i.Id].ToList();
            return new ParentInvoiceDto(i.Id, i.Number, i.TermId, x.TermName, i.Total, i.Paid, i.Balance, i.Status.ToString(), i.DueDate,
                online && Payable(i), NextInstalment(plan, i.Paid, i.Balance), Math.Min(MinimumPartPaymentMinor, i.Balance),
                lines[i.Id].Select(l => new InvoiceLineDto(l.Id, l.FeeItemId, l.Description, l.AmountMinor)).ToList(),
                plan.Select(p => new InstalmentDto(p.Id, p.DueDate, p.AmountMinor)).ToList(),
                paid[i.Id].Select(ToDto).ToList());
        }).ToList();
    }

    private static bool Payable(StudentInvoice i) => i.Status != InvoiceStatus.Cancelled && i.Balance > 0;

    /// <summary>What is still owed on the earliest instalment that isn't fully covered by the payments so far, or null without instalments.</summary>
    private static long? NextInstalment(IEnumerable<Instalment> instalments, long paid, long balance)
    {
        long running = 0;
        foreach (var x in instalments.OrderBy(x => x.DueDate))
        {
            running += x.AmountMinor;
            if (running > paid) return Math.Min(running - paid, balance);
        }
        return null;
    }

    /// <summary>
    /// Starts a Paystack checkout with the school's own key. Allowed amounts: the balance, the next instalment's outstanding amount, or
    /// anything from ₦1,000 (or the balance when smaller) up to the balance. A Pending payment row holds the reference until Paystack confirms.
    /// </summary>
    public async Task<PayOnlineResult> PayAsync(Guid studentId, Guid invoiceId, PayOnlineRequest r, CancellationToken ct)
    {
        await parents.RequireChildAsync(studentId, ct);
        var payerId = parents.CustomerUserId();
        var invoice = await (from i in db.StudentInvoices.AsNoTracking()
                             join e in db.Enrolments on i.EnrolmentId equals e.Id
                             where i.Id == invoiceId && e.StudentId == studentId
                             select i).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Invoice");
        var profile = await db.SchoolProfiles.AsNoTracking().FirstAsync(ct);
        if (!await OnlineReadyAsync(profile, ct)) throw new ConflictException(NotSetUp, "online_fees_unavailable");
        if (invoice.Status == InvoiceStatus.Cancelled) throw new ConflictException("This invoice is cancelled, so it can't be paid.", "invoice_cancelled");
        if (invoice.Balance <= 0) throw new ConflictException("This invoice is already paid.", "invoice_paid");

        var currency = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.Currency).FirstAsync(ct);
        var instalments = await db.Instalments.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToListAsync(ct);
        var next = NextInstalment(instalments, invoice.Paid, invoice.Balance);
        var minimum = Math.Min(MinimumPartPaymentMinor, invoice.Balance);
        var amount = r.AmountMinor;
        if (amount > invoice.Balance)
            throw BadRequestException.Field("amountMinor", $"The balance is {Money.Format(invoice.Balance, currency)}. Enter that amount or less.");
        if (amount != next && amount < minimum)
            throw BadRequestException.Field("amountMinor", $"Pay at least {Money.Format(minimum, currency)}, or the full balance.");

        var email = await db.Users.Where(u => u.Id == payerId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email)) throw BadRequestException.Field("email", "Add an email address to your account to pay online.");

        var reference = $"{ReferencePrefix}{Guid.NewGuid():N}";
        db.FeePayments.Add(new FeePayment
        {
            TenantId = TenantId, InvoiceId = invoiceId, AmountMinor = amount, Currency = currency, Method = FeePaymentMethod.Online,
            Status = FeePaymentStatus.Pending, ProviderRef = reference, PaidOn = SchoolCalendar.Today(clock.GetUtcNow()), PayerUserId = payerId,
        });
        // Saved before Paystack is called, so a webhook can never arrive for a reference we don't know.
        await db.SaveChangesAsync(ct);
        var url = await payments.InitializeAsync(secrets.Unprotect(profile.PaystackSecretKeyProtected!), new PaymentInit(
            email, amount, currency, reference, $"{urls.Web(Products.School)}/parent/pay/return?reference={Uri.EscapeDataString(reference)}",
            new() { ["tenantId"] = TenantId.ToString(), ["invoice"] = invoice.Number, ["studentId"] = studentId.ToString() }), ct);
        return new PayOnlineResult(url, reference);
    }

    /// <summary>
    /// The parent is back from Paystack. Only the parent who started the payment can ask (anyone else gets 404). Paystack is asked
    /// directly; a successful charge is confirmed exactly as the webhook would.
    /// </summary>
    public async Task<PaymentStatusDto> VerifyAsync(string reference, CancellationToken ct)
    {
        var payerId = parents.CustomerUserId();
        var found = await (from p in db.FeePayments.IgnoreQueryFilters()
                           where p.ProviderRef == reference && p.PayerUserId == payerId
                           join i in db.StudentInvoices.IgnoreQueryFilters() on new { I = p.InvoiceId, T = p.TenantId } equals new { I = i.Id, T = i.TenantId }
                           join e in db.Enrolments.IgnoreQueryFilters() on new { E = i.EnrolmentId, T = i.TenantId } equals new { E = e.Id, T = e.TenantId }
                           select new { p.TenantId, e.StudentId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Payment");
        await parents.RequireChildAsync(found.StudentId, ct); // the school's tenant from here on; 403 when suspended

        var payment = await db.FeePayments.AsNoTracking().FirstAsync(p => p.ProviderRef == reference, ct);
        if (IsOpen(payment.Status))
        {
            var profile = await db.SchoolProfiles.AsNoTracking().FirstAsync(ct);
            var keys = ConfirmationKeys(profile);
            var (charge, unknown, knownUnpaid) = await VerifyWithKeysAsync(keys, reference, ct);
            if (charge is not null) await ConfirmAsync(found.TenantId, charge, ct);
            // Not found with any key: usually the parent hasn't finished paying. When the key changed after the checkout started (or there is
            // no key left), the charge may sit in an account we can no longer reach, so the school is told. Paystack not answering is no
            // answer, and a reference Paystack knows but reports unpaid (abandoned, failed) is simply not paid: neither alerts.
            else if (!unknown && !knownUnpaid && (keys.Count == 0 || profile.PreviousKeyRetiredAt > payment.CreatedAt))
                await AlertUnverifiedAsync(payment, "verify", ct);
        }
        return await StatusAsync(reference, found.StudentId, ct);
    }

    /// <summary>Pending checkouts, and Abandoned ones (the school job stopped asking, but a late charge must still be applied), can be confirmed.</summary>
    private static bool IsOpen(FeePaymentStatus status) => status is FeePaymentStatus.Pending or FeePaymentStatus.Abandoned;

    /// <summary>
    /// Asks Paystack about the reference with each key. <c>Unknown</c> is true when a key couldn't be checked because Paystack was unavailable
    /// (5xx, 429, network, timeout); with no charge found that means "don't know", never "not found". <c>KnownUnpaid</c> is true when an
    /// account knows the reference but reports it unpaid (abandoned, failed...): the parent didn't pay, nothing is stranded.
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
                logger.LogWarning(ex, "School fee payment {Reference}: Paystack was unavailable when verifying", reference);
                unknown = true;
            }
        }
        return (null, unknown, knownUnpaid);
    }

    /// <summary>A checkout is only stranded once the parent has had this long to finish it.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(1);
    /// <summary>Checkouts older than this are no longer asked about.</summary>
    public static readonly TimeSpan RecheckUntil = TimeSpan.FromDays(7);
    /// <summary>How long one school's stranded payment check may run before leaving the rest for the next run.</summary>
    public static readonly TimeSpan RecheckBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The school job's check of stranded online checkouts (webhook missed, parent never came back): Pending payments older than an hour and
    /// younger than 7 days are verified with every key the school can use (current, then the previous one within its 7 days) and confirmed
    /// when Paystack has the charge. A checkout no key can verify that is older than 24 hours is reported to the school (once, by the same guard
    /// the return verify uses) and marked Abandoned so it stops being checked; one Paystack knows but reports unpaid (abandoned, failed) is
    /// marked Abandoned without an alert. Runs in the school's tenant scope, within <see cref="RecheckBudget"/>. Returns how many changed.
    /// </summary>
    public async Task<int> RecheckPendingAsync(DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (profile is null) return 0;
        var newest = now - RecheckAfter;
        var oldest = now - RecheckUntil;
        var pending = await db.FeePayments.AsNoTracking()
            .Where(p => p.Method == FeePaymentMethod.Online && p.Status == FeePaymentStatus.Pending && p.ProviderRef != null && p.CreatedAt <= newest && p.CreatedAt > oldest)
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
                logger.LogWarning("School fee payments: the stranded payment check ran out of time; the rest are checked on the next run");
                break;
            }
            try
            {
                var reference = payment.ProviderRef!;
                var (charge, unknown, knownUnpaid) = await VerifyWithKeysAsync(keys, reference, ct);
                if (charge is not null)
                {
                    await ConfirmAsync(TenantId, charge, ct);
                    changed++;
                }
                else if (unknown)
                {
                    logger.LogWarning("School fee payment {Reference}: Paystack could not answer; the payment stays pending and is checked again", reference);
                }
                else if (payment.CreatedAt <= now - OpenCheckoutWindow)
                {
                    // Paystack knows the checkout and says it wasn't paid (the parent backed out): abandoned quietly. Only a reference no
                    // key knows may be money stranded in an account we can't reach, so only that one alerts the school.
                    if (!knownUnpaid) await AlertUnverifiedAsync(payment, "stranded payment check", ct);
                    db.ChangeTracker.Clear();
                    var abandoned = await db.FeePayments.Where(p => p.Id == payment.Id && p.Status == FeePaymentStatus.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, FeePaymentStatus.Abandoned).SetProperty(p => p.UpdatedAt, now), ct);
                    changed += abandoned;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "School fee payment {Reference}: the stranded payment check failed", payment.ProviderRef);
            }
        }
        return changed;
    }

    /// <summary>Forgets the previous Paystack key once its 7 days are over. Runs in the school's tenant scope; true when a key was removed.</summary>
    public async Task<bool> RetirePreviousKeyAsync(DateTimeOffset now, CancellationToken ct)
    {
        var before = now - PreviousKeyWindow;
        return await db.SchoolProfiles.Where(p => p.PreviousKeyRetiredAt != null && p.PreviousKeyRetiredAt <= before)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PreviousPaystackSecretKeyProtected, (string?)null).SetProperty(p => p.PreviousKeyRetiredAt, (DateTimeOffset?)null), ct) > 0;
    }

    private async Task<PaymentStatusDto> StatusAsync(string reference, Guid studentId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var p = await db.FeePayments.AsNoTracking().FirstAsync(x => x.ProviderRef == reference, ct);
        var balance = await db.StudentInvoices.AsNoTracking().Where(i => i.Id == p.InvoiceId).Select(i => i.Balance).FirstAsync(ct);
        return new PaymentStatusDto(p.Id, reference, studentId, p.InvoiceId, p.AmountMinor, p.Status.ToString(), p.ReceiptNumber, balance);
    }

    /// <summary>
    /// The school's own Paystack webhook. False (401) when the school or its key is unknown or the signature is wrong; nothing is
    /// recorded then. A signed event for an unknown reference, or another school's, is acknowledged and ignored.
    /// </summary>
    public async Task<bool> HandleWebhookAsync(Guid tenantId, string rawBody, string? signature,
        Func<string, string, string?, bool> isValidSignature, Func<string, VerifiedCharge?> parse, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Product == Products.School, ct)) return false;
        tenant.SetTenant(tenantId);
        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        var keys = profile is null ? new List<string>() : ConfirmationKeys(profile);
        var current = profile?.PaystackSecretKeyProtected is { } protectedKey ? secrets.Unprotect(protectedKey) : null;
        var signedByCurrent = current is not null && isValidSignature(current, rawBody, signature);
        if (!signedByCurrent && !keys.Any(key => isValidSignature(key, rawBody, signature)))
        {
            // Nothing is recorded and nobody is emailed: an unsigned body could name any checkout. Stranded checkouts are reported by the
            // return verify (and the school jobs), which ask Paystack directly.
            logger.LogWarning("Rejected a school Paystack webhook for {TenantId}: no key or bad signature", tenantId);
            return false;
        }
        var charge = TryParse(parse, rawBody);
        if (charge is null || !charge.Reference.StartsWith(ReferencePrefix, StringComparison.Ordinal)) return true;
        if (signedByCurrent)
        {
            await ConfirmAsync(tenantId, charge, ct);
            return true;
        }

        // Signed only with the retired key. It may have been rotated because it leaked, so the body is not trusted: Paystack is asked
        // about the reference (previous key, then current) and only its answer is applied.
        if (!await db.FeePayments.AnyAsync(p => p.ProviderRef == charge.Reference && (p.Status == FeePaymentStatus.Pending || p.Status == FeePaymentStatus.Abandoned), ct)) return true;
        if ((await VerifyWithKeysAsync(keys.AsEnumerable().Reverse(), charge.Reference, ct)).Charge is { } verified)
        {
            await ConfirmAsync(tenantId, verified, ct);
            return true;
        }
        logger.LogWarning("School fee payment {Reference}: a webhook signed with the previous Paystack key wasn't confirmed by Paystack; ignored", charge.Reference);
        return true;
    }

    private VerifiedCharge? TryParse(Func<string, VerifiedCharge?> parse, string rawBody)
    {
        try { return parse(rawBody); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "Ignored an unreadable school Paystack webhook");
            return null;
        }
    }

    /// <summary>
    /// A charge for this checkout couldn't be checked with any of the school's keys. Nothing is applied; the proprietor and the bursars
    /// are emailed once per checkout (a conditional update marks it), so they can look in their Paystack dashboard.
    /// </summary>
    private async Task AlertUnverifiedAsync(FeePayment payment, string source, CancellationToken ct)
    {
        logger.LogWarning("School fee payment {Reference}: the {Source} couldn't be checked with any of the school's Paystack keys", payment.ProviderRef, source);
        var now = clock.GetUtcNow();
        var marked = await db.FeePayments.Where(p => p.Id == payment.Id && p.UnverifiedAlertedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UnverifiedAlertedAt, now), ct);
        if (marked == 0) return;
        var invoice = await (from i in db.StudentInvoices join e in db.Enrolments on i.EnrolmentId equals e.Id join s in db.Students on e.StudentId equals s.Id
                             where i.Id == payment.InvoiceId select new { i.Number, s.FirstName, s.MiddleName, s.LastName }).FirstAsync(ct);
        var school = await db.SchoolProfiles.Select(p => p.Name).FirstAsync(ct);
        foreach (var (userId, email) in await OwnerAndBursarsAsync(ct))
            notifications.Email(email, "school.payment_unverified", new()
            {
                ["school"] = school,
                ["child"] = ParentAccess.FullName(invoice.FirstName, invoice.MiddleName, invoice.LastName),
                ["invoice"] = invoice.Number,
                ["reference"] = payment.ProviderRef ?? "",
                ["amount"] = Money.Format(payment.AmountMinor, payment.Currency),
            }, TenantId, userId);
        feed.Notify(await feed.MembersAsync(ct, MemberRole.Owner, MemberRole.Accountant), "school.payment_unverified",
            $"Online payment for {ParentAccess.FullName(invoice.FirstName, invoice.MiddleName, invoice.LastName)} couldn't be checked",
            $"Check reference {payment.ProviderRef} in your Paystack dashboard. Nothing was applied to invoice {invoice.Number}.", "/app/fees");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Applies a charge Paystack reports as successful, once. The Pending row is claimed with a conditional update under the school's fee
    /// lock, so the webhook and the return verify arriving together record one payment. An amount or currency that differs from the
    /// row is not applied: the row becomes Mismatch and the bursars and the proprietor are emailed. A charge for more than the balance
    /// at this moment (the balance changed after the parent started) is still recorded and the invoice is flagged for review.
    /// </summary>
    public async Task ConfirmAsync(Guid tenantId, VerifiedCharge charge, CancellationToken ct)
    {
        if (tenant.TenantId is { } current && current != tenantId) return; // never another school's reference
        if (tenant.TenantId is null) tenant.SetTenant(tenantId);
        var row = await db.FeePayments.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderRef == charge.Reference, ct);
        if (row is null || row.Method != FeePaymentMethod.Online || !IsOpen(row.Status)) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(FeeService.GenerateLockKey(tenantId), ct);
        var now = clock.GetUtcNow();
        var pending = db.FeePayments.Where(p => p.Id == row.Id && (p.Status == FeePaymentStatus.Pending || p.Status == FeePaymentStatus.Abandoned));

        if (charge.AmountMinor != row.AmountMinor || !string.Equals(charge.Currency, row.Currency, StringComparison.OrdinalIgnoreCase))
        {
            var currency = charge.Currency.ToUpperInvariant();
            var flagged = await pending.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, FeePaymentStatus.Mismatch)
                .SetProperty(p => p.ChargedAmountMinor, charge.AmountMinor)
                .SetProperty(p => p.ChargedCurrency, currency)
                .SetProperty(p => p.UpdatedAt, now), ct);
            if (flagged == 0) return;
            logger.LogWarning("School fee payment {Reference}: Paystack reported {Amount} {Currency}, expected {Expected} {ExpectedCurrency}",
                charge.Reference, charge.AmountMinor, charge.Currency, row.AmountMinor, row.Currency);
            db.AuditLogs.Add(Audit("fee_payment.mismatch", row.Id, new { reference = charge.Reference, expected = row.AmountMinor, row.Currency, received = charge.AmountMinor, receivedCurrency = currency }, now));
            await QueueMismatchAsync(row, charge, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        var paidOn = SchoolCalendar.Today(charge.PaidAt);
        var claimed = await pending.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, FeePaymentStatus.Succeeded)
            .SetProperty(p => p.PaidAt, charge.PaidAt)
            .SetProperty(p => p.PaidOn, paidOn)
            .SetProperty(p => p.UpdatedAt, now), ct);
        if (claimed == 0) return; // the other caller got here first
        db.AuditLogs.Add(Audit("fee_payment.succeeded", row.Id, new { reference = charge.Reference, amount = charge.AmountMinor, charge.Currency, paidOn }, now));
        var payment = await db.FeePayments.FirstAsync(p => p.Id == row.Id, ct);
        await ApplyAsync(payment, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>A Succeeded payment joins the invoice: receipt number, review flag when it passes the balance, totals, receipt email. Inside the caller's transaction.</summary>
    private async Task ApplyAsync(FeePayment payment, CancellationToken ct)
    {
        var invoice = await db.StudentInvoices.FirstAsync(i => i.Id == payment.InvoiceId, ct);
        if (payment.AmountMinor > invoice.Balance || invoice.Status == InvoiceStatus.Cancelled) invoice.NeedsReview = true;
        var number = await db.NextDocumentNumberAsync(DocumentCounter.SchoolReceipt, ct);
        payment.ReceiptNumber = $"RCT-{payment.PaidOn.Year}-{number:D5}";
        await db.SaveChangesAsync(ct);
        await fees.RecalculateAsync(invoice.Id, ct);
        await QueueReceiptAsync(payment, invoice, ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Staff: manual payments, reversals, receipts ----------

    public async Task<FeePaymentDto> RecordAsync(Guid invoiceId, RecordPaymentRequest r, CancellationToken ct)
    {
        if (!Enum.TryParse<FeePaymentMethod>(r.Method, true, out var method) || method == FeePaymentMethod.Online || !Enum.IsDefined(method))
            throw BadRequestException.Field("method", "Choose Cash, Transfer or Pos.");
        if (r.AmountMinor <= 0) throw BadRequestException.Field("amountMinor", "Enter an amount above zero.");
        var paidOn = r.PaidOn ?? throw BadRequestException.Field("paidOn", "Enter the date the money was received.");
        if (paidOn > SchoolCalendar.Today(clock.GetUtcNow())) throw BadRequestException.Field("paidOn", "The payment date can't be in the future.");
        var reference = string.IsNullOrWhiteSpace(r.Reference) ? null : r.Reference.Trim();
        if (reference?.Length > 100) throw BadRequestException.Field("reference", "Use 100 characters or fewer for the reference.");

        // Joins an open transaction (a payments import records every row in one), so the import lands whole or not at all.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(FeeService.GenerateLockKey(TenantId), ct);
        var invoice = await db.StudentInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException("Invoice");
        if (invoice.Status == InvoiceStatus.Cancelled) throw new ConflictException("This invoice is cancelled, so it can't take payments.", "invoice_cancelled");
        var currency = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.Currency).FirstAsync(ct);
        if (r.AmountMinor > invoice.Balance)
            throw BadRequestException.Field("amountMinor", $"That is more than the balance of {Money.Format(invoice.Balance, currency)}. Enter that amount or less.");

        var payment = new FeePayment
        {
            TenantId = TenantId, InvoiceId = invoiceId, AmountMinor = r.AmountMinor, Currency = currency, Method = method,
            Status = FeePaymentStatus.Succeeded, Reference = reference, PaidOn = paidOn, RecordedBy = user.UserId,
        };
        db.FeePayments.Add(payment);
        await ApplyAsync(payment, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ToDto(payment);
    }

    /// <summary>Takes a payment out of the books with a reason (audited). Online payments are not refunded through Paystack.</summary>
    public async Task<ReversePaymentResult> ReverseAsync(Guid paymentId, ReversePaymentRequest r, CancellationToken ct)
    {
        var reason = r.Reason?.Trim() ?? "";
        if (reason.Length is < 5 or > 300) throw BadRequestException.Field("reason", "Say why in 5 to 300 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(FeeService.GenerateLockKey(TenantId), ct);
        var payment = await db.FeePayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw new NotFoundException("Payment");
        if (payment.Status != FeePaymentStatus.Succeeded) throw new ConflictException("Only a received payment can be reversed.", "payment_not_received");
        if (payment.ReversedAt is not null) throw new ConflictException("This payment has already been reversed.", "payment_reversed");
        payment.ReversedAt = clock.GetUtcNow();
        payment.ReversedBy = user.UserId;
        payment.ReversalReason = reason;
        await db.SaveChangesAsync(ct);
        await fees.RecalculateAsync(payment.InvoiceId, ct);
        var student = await (from i in db.StudentInvoices join e in db.Enrolments on i.EnrolmentId equals e.Id join s in db.Students on e.StudentId equals s.Id
                             where i.Id == payment.InvoiceId select new { s.Id, s.FirstName, s.MiddleName, s.LastName, i.Number }).FirstAsync(ct);
        feed.Notify(await feed.GuardianUsersAsync(student.Id, ct), "school.payment_reversed",
            $"A payment for {ParentAccess.FullName(student.FirstName, student.MiddleName, student.LastName)} was reversed",
            $"{Money.Format(payment.AmountMinor, payment.Currency)} was taken off invoice {student.Number}: {reason}", SchoolFeed.ParentLink("fees", student.Id));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var message = payment.Method == FeePaymentMethod.Online
            ? "Payment reversed in the school's books only. No refund was sent through Paystack: refund the parent from your Paystack dashboard if you need to."
            : "Payment reversed. The invoice balance is restored.";
        return new ReversePaymentResult(ToDto(payment), message);
    }

    /// <summary>The receipt of a received payment, for staff with fee access.</summary>
    public async Task<(byte[] Pdf, string FileName)> StaffReceiptAsync(Guid paymentId, CancellationToken ct) => await ReceiptAsync(paymentId, ct);

    /// <summary>The receipt for a parent linked to the payment's student (404 for anyone else).</summary>
    public async Task<(byte[] Pdf, string FileName)> ParentReceiptAsync(Guid paymentId, CancellationToken ct)
    {
        parents.CustomerUserId();
        var studentId = await (from p in db.FeePayments.IgnoreQueryFilters()
                               where p.Id == paymentId && p.Status == FeePaymentStatus.Succeeded
                               join i in db.StudentInvoices.IgnoreQueryFilters() on new { I = p.InvoiceId, T = p.TenantId } equals new { I = i.Id, T = i.TenantId }
                               join e in db.Enrolments.IgnoreQueryFilters() on new { E = i.EnrolmentId, T = i.TenantId } equals new { E = e.Id, T = e.TenantId }
                               select (Guid?)e.StudentId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Receipt");
        await parents.RequireChildAsync(studentId, ct);
        return await ReceiptAsync(paymentId, ct);
    }

    private async Task<(byte[] Pdf, string FileName)> ReceiptAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.FeePayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId && p.Status == FeePaymentStatus.Succeeded, ct)
            ?? throw new NotFoundException("Receipt");
        var row = await (from i in db.StudentInvoices.AsNoTracking()
                         join e in db.Enrolments on i.EnrolmentId equals e.Id
                         join s in db.Students on e.StudentId equals s.Id
                         join c in db.SchoolClasses on e.ClassId equals c.Id
                         join t in db.Terms on i.TermId equals t.Id
                         join ses in db.AcademicSessions on t.SessionId equals ses.Id
                         where i.Id == payment.InvoiceId
                         select new { i.Number, i.Balance, s.FirstName, s.MiddleName, s.LastName, s.AdmissionNo, ClassName = c.Name, TermName = t.Name, SessionName = ses.Name })
            .FirstAsync(ct);
        var profile = await db.SchoolProfiles.AsNoTracking().FirstAsync(ct);
        var logo = profile.LogoKey is { } key ? await ReadAsync(key, ct) : null;
        var reversed = payment.ReversedAt is { } at
            ? $"Reversed on {SchoolCalendar.Today(at).ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}: {payment.ReversalReason}"
            : null;
        var data = new ReceiptData(profile.Name, profile.Address, logo, payment.ReceiptNumber!,
            payment.PaidOn.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), ParentAccess.FullName(row.FirstName, row.MiddleName, row.LastName),
            row.AdmissionNo, row.ClassName, row.Number, $"{row.TermName}, {row.SessionName} session",
            Money.Format(payment.AmountMinor, payment.Currency), MethodLabel(payment.Method), payment.ProviderRef ?? payment.Reference,
            Money.Format(row.Balance, payment.Currency), reversed, !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct));
        return (await pdf.ReceiptAsync(data, ct), $"{payment.ReceiptNumber}.pdf");
    }

    public static string MethodLabel(FeePaymentMethod method) => method switch
    {
        FeePaymentMethod.Online => "Online (Paystack)",
        FeePaymentMethod.Pos => "POS",
        FeePaymentMethod.Transfer => "Bank transfer",
        _ => method.ToString(),
    };

    // ---------- Emails ----------

    /// <summary>
    /// Emails the receipt notice (a link to the portal, never the PDF) to the student's guardians and, online, the paying parent.
    /// Their accounts are also told in the app, and the bursars hear about online payments.
    /// </summary>
    private async Task QueueReceiptAsync(FeePayment payment, StudentInvoice invoice, CancellationToken ct)
    {
        var student = await (from e in db.Enrolments join s in db.Students on e.StudentId equals s.Id
                             where e.Id == invoice.EnrolmentId select new { s.Id, s.FirstName, s.MiddleName, s.LastName }).FirstAsync(ct);
        var recipients = await db.StudentGuardians.Where(l => l.StudentId == student.Id)
            .Join(db.Guardians.Where(g => g.Email != null), l => l.GuardianId, g => g.Id, (l, g) => g.Email!).ToListAsync(ct);
        if (payment.PayerUserId is { } payer && await db.Users.Where(u => u.Id == payer).Select(u => u.Email).FirstOrDefaultAsync(ct) is { } payerEmail)
            recipients.Add(payerEmail.ToLowerInvariant());
        var school = await db.SchoolProfiles.Select(p => p.Name).FirstAsync(ct);
        foreach (var to in recipients.Distinct(StringComparer.OrdinalIgnoreCase))
            notifications.Email(to, "school.receipt", new()
            {
                ["school"] = school,
                ["child"] = ParentAccess.FullName(student.FirstName, student.MiddleName, student.LastName),
                ["amount"] = Money.Format(payment.AmountMinor, payment.Currency),
                ["receipt"] = payment.ReceiptNumber!,
                ["invoice"] = invoice.Number,
                ["balance"] = Money.Format(invoice.Balance, payment.Currency),
                ["link"] = $"{urls.Web(Products.School)}/parent",
            }, TenantId);

        var child = ParentAccess.FullName(student.FirstName, student.MiddleName, student.LastName);
        var amount = Money.Format(payment.AmountMinor, payment.Currency);
        var parentUsers = await feed.GuardianUsersAsync(student.Id, ct);
        if (payment.PayerUserId is { } payerId) parentUsers.Add(payerId);
        feed.Notify(parentUsers, "school.payment_received", $"Payment received for {child}",
            $"{school} received {amount} (receipt {payment.ReceiptNumber}).", SchoolFeed.ParentLink("fees", student.Id));
        if (payment.Method == FeePaymentMethod.Online)
            feed.Notify(await feed.MembersAsync(ct, MemberRole.Accountant), "school.payment_received", $"Online payment for {child}",
                $"{amount} was paid online on invoice {invoice.Number}.", "/app/fees");
    }

    /// <summary>Emails the bursars (Accountant members) and the proprietor (Owner) that a charge didn't match and was not applied.</summary>
    private async Task QueueMismatchAsync(FeePayment row, VerifiedCharge charge, CancellationToken ct)
    {
        var invoice = await (from i in db.StudentInvoices join e in db.Enrolments on i.EnrolmentId equals e.Id join s in db.Students on e.StudentId equals s.Id
                             where i.Id == row.InvoiceId select new { i.Number, s.FirstName, s.MiddleName, s.LastName }).FirstAsync(ct);
        var school = await db.SchoolProfiles.Select(p => p.Name).FirstAsync(ct);
        foreach (var (userId, email) in await OwnerAndBursarsAsync(ct))
            notifications.Email(email, "school.payment_mismatch", new()
            {
                ["school"] = school,
                ["child"] = ParentAccess.FullName(invoice.FirstName, invoice.MiddleName, invoice.LastName),
                ["invoice"] = invoice.Number,
                ["reference"] = row.ProviderRef ?? "",
                ["expected"] = Money.Format(row.AmountMinor, row.Currency),
                ["received"] = Money.Format(charge.AmountMinor, charge.Currency.ToUpperInvariant()),
            }, TenantId, userId);
        var child = ParentAccess.FullName(invoice.FirstName, invoice.MiddleName, invoice.LastName);
        feed.Notify(await feed.MembersAsync(ct, MemberRole.Owner, MemberRole.Accountant), "school.payment_mismatch",
            $"Online payment for {child} needs checking",
            $"Paystack reported {Money.Format(charge.AmountMinor, charge.Currency.ToUpperInvariant())} instead of {Money.Format(row.AmountMinor, row.Currency)} on invoice {invoice.Number}. It was not applied.",
            "/app/fees");
    }

    /// <summary>The proprietor (Owner) and the bursars (Accountant members) with an email address.</summary>
    private async Task<List<(Guid UserId, string Email)>> OwnerAndBursarsAsync(CancellationToken ct)
    {
        var rows = await db.Memberships
            .Where(m => m.Status == MembershipStatus.Active && (m.Role == MemberRole.Owner || m.Role == MemberRole.Accountant))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email }).ToListAsync(ct);
        return rows.Where(r => !string.IsNullOrWhiteSpace(r.Email)).DistinctBy(r => r.Id).Select(r => (r.Id, r.Email!)).ToList();
    }

    // ---------- Helpers ----------

    public static FeePaymentDto ToDto(FeePayment p) => new(p.Id, p.InvoiceId, p.AmountMinor, p.Currency, p.Method.ToString(), p.Status.ToString(),
        p.ProviderRef ?? p.Reference, p.PaidOn, p.ReceiptNumber, p.ReversedAt, p.ReversalReason);

    private AuditLog Audit(string action, Guid paymentId, object after, DateTimeOffset now) => new()
    {
        TenantId = TenantId, ActorId = user.UserId, Action = action, EntityType = nameof(FeePayment), EntityId = paymentId.ToString(),
        AfterJson = JsonSerializer.Serialize(after), Ip = user.IpAddress, CreatedAt = now,
    };

    private async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(key, ct);
            if (stream is null) return null;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            return null; // a missing logo is left out
        }
    }
}
