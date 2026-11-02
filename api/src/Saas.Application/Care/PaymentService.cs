using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>A payment as staff see it: amount, method, status, reference and receipt number (nothing clinical).</summary>
public sealed record CarePaymentDto(
    Guid Id, Guid BillId, Guid PatientId, long AmountMinor, PayMethod Method, string? Reference, string? ReceiptNumber, bool IsDeposit,
    Guid? RecordedByUserId, DateTimeOffset PaidAt, CarePaymentStatus Status, DateTimeOffset? ReversedAt, string? ReversalReason);

/// <summary>A desk payment. <c>IdempotencyKey</c> (or the <c>Idempotency-Key</c> header) makes a retry return the first payment.</summary>
public sealed record DeskPaymentRequest(long? AmountMinor, PayMethod? Method, string? Reference, string? IdempotencyKey);
public sealed record ReverseCarePaymentRequest(string? Reason);
public sealed record CareReversalResult(CarePaymentDto Payment, string Message);

/// <summary>What a payment receipt prints, already formatted. No bill lines and nothing clinical: the receipt goes to every member.</summary>
public sealed record CareReceiptData(
    string ClinicName, string? Address, string? Phone, byte[]? Logo, string ReceiptNumber, string PaidOn, string PatientName, string PatientNumber,
    string BillNumber, string Amount, string Method, string? Reference, string BillBalance, string? ReversedText, bool ShowBranding);

/// <summary>
/// Patient payments at the desk (cash, POS, transfer; admission deposits too), reversals and receipts. Every change runs under the per-clinic
/// bill lock (<see cref="BillingService.LockKey"/>), re-reads the bill's paid amount once the lock is held, saves the payment and the paid
/// amount, then recalculates the bill. Online payments are <see cref="OnlinePaymentService"/>.
/// </summary>
public sealed class PaymentService(
    IAppDbContext db, ITenantContext tenant, BillingService billing, ICurrentUser user, TimeProvider clock, CareAccess access, ICarePdf pdf,
    IFileStorage storage, PlanService plans, CareNotices notices)
{
    /// <summary>The largest single desk payment: ₦100,000,000.</summary>
    public const long MaxAmountMinor = 10_000_000_000;
    public const string KeyReused = "This payment key was already used for a different payment.";
    public const string NotReceived = "Only a received payment can be reversed.";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Desk payments ----------

    /// <summary>The cashier's (or leadership's) desk payment route. 403 for anyone else.</summary>
    public async Task<CarePaymentDto> RecordAtDeskAsync(Guid billId, DeskPaymentRequest r, string? headerKey, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PriceListService.EnsureWriter(caller);
        if (caller.IsSupportView) throw new ForbiddenException();
        var key = string.IsNullOrWhiteSpace(r.IdempotencyKey) ? headerKey : r.IdempotencyKey;
        return await RecordDeskAsync(billId, r.AmountMinor ?? 0, r.Method ?? (PayMethod)(-1), r.Reference, deposit: false, key, ct);
    }

    /// <summary>
    /// Records money taken at the desk (cash, POS or transfer) on a bill. No role check here: the calling route checks its own (the
    /// admission bed route for deposits, the cashier's route). 400 <c>amountMinor</c>, <c>method</c> or <c>reference</c>; 404 for an
    /// unknown bill.
    /// </summary>
    public Task<CarePaymentDto> RecordDeskAsync(Guid billId, long amountMinor, PayMethod method, string? reference, CancellationToken ct) =>
        RecordDeskAsync(billId, amountMinor, method, reference, deposit: false, idempotencyKey: null, ct);

    /// <summary>As <see cref="RecordDeskAsync(Guid, long, PayMethod, string?, CancellationToken)"/>; <paramref name="deposit"/> marks an admission deposit.</summary>
    public Task<CarePaymentDto> RecordDeskAsync(Guid billId, long amountMinor, PayMethod method, string? reference, bool deposit, CancellationToken ct) =>
        RecordDeskAsync(billId, amountMinor, method, reference, deposit, idempotencyKey: null, ct);

    /// <summary>
    /// The desk payment. With an <paramref name="idempotencyKey"/>, a payment already recorded under that key (looked up once the lock is
    /// held) is returned as it is and nothing new is recorded; the same key for another bill or amount is 409 <c>idempotency_key_reused</c>.
    /// </summary>
    public async Task<CarePaymentDto> RecordDeskAsync(Guid billId, long amountMinor, PayMethod method, string? reference, bool deposit, string? idempotencyKey,
        CancellationToken ct)
    {
        ValidateDesk(amountMinor, method, reference, "amountMinor");
        var cleanReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > 100 }) throw BadRequestException.Field("idempotencyKey", "The payment key can have at most 100 characters.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(BillingService.LockKey(TenantId), ct);
        if (key is not null && await db.CarePayments.AsNoTracking().FirstOrDefaultAsync(p => p.IdempotencyKey == key, ct) is { } earlier)
        {
            if (earlier.BillId != billId || earlier.AmountMinor != amountMinor || earlier.Method != method)
                throw new ConflictException(KeyReused, "idempotency_key_reused");
            return ToDto(earlier);
        }
        // The bill may have been tracked before the lock (stale values and snapshot); reload it now that the lock is held.
        var bill = await BillingService.FreshBillAsync(db, billId, ct) ?? throw new NotFoundException("Bill");
        var now = clock.GetUtcNow();
        var payment = new CarePayment
        {
            TenantId = TenantId, BillId = bill.Id, PatientId = bill.PatientId, AmountMinor = amountMinor, Method = method, Reference = cleanReference,
            ReceiptNumber = await NextReceiptAsync(now, ct), IsDeposit = deposit, RecordedByUserId = user.UserId, PaidAt = now, IdempotencyKey = key,
            Status = CarePaymentStatus.Succeeded,
        };
        db.CarePayments.Add(payment);
        bill.PaidMinor = checked(bill.PaidMinor + amountMinor);
        // Callers save payment changes before recalculating.
        await db.SaveChangesAsync(ct);
        await billing.RecalculateAsync(bill.Id, ct);
        // The patient's portal users hear the amount, the bill and the receipt number (Task 14), committed with the payment.
        await notices.PaymentReceivedAsync(bill.PatientId, bill.Number, amountMinor, payment.ReceiptNumber!, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ToDto(payment);
    }

    /// <summary>The next receipt number, RCP-{Lagos year}-{00000}, from the clinic's atomic counter. Inside the caller's transaction.</summary>
    internal async Task<string> NextReceiptAsync(DateTimeOffset at, CancellationToken ct)
    {
        var number = await db.NextDocumentNumberAsync(DocumentCounter.CareReceipt, ct);
        var year = CareCalendar.Today(at).Year;
        return $"RCP-{year.ToString(CultureInfo.InvariantCulture)}-{number.ToString("D5", CultureInfo.InvariantCulture)}";
    }

    /// <summary>The checks on a desk payment: a positive amount up to <see cref="MaxAmountMinor"/>, cash, POS or transfer, a reference of at most 100 characters.</summary>
    public static void ValidateDesk(long amountMinor, PayMethod? method, string? reference, string amountField)
    {
        if (amountMinor < 1) throw BadRequestException.Field(amountField, "Enter an amount above zero.");
        if (amountMinor > MaxAmountMinor) throw BadRequestException.Field(amountField, $"Enter an amount up to {Money.Format(MaxAmountMinor, "NGN")}.");
        if (method is not (PayMethod.Cash or PayMethod.Pos or PayMethod.Transfer))
            throw BadRequestException.Field("method", "Choose cash, POS or transfer.");
        if (reference is { Length: > 100 } && reference.Trim().Length > 100)
            throw BadRequestException.Field("reference", "The reference can have at most 100 characters.");
    }

    // ---------- Reversals ----------

    /// <summary>The reversal route: the cashier or leadership. 403 for anyone else.</summary>
    public async Task<CareReversalResult> ReverseForStaffAsync(Guid paymentId, ReverseCarePaymentRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PriceListService.EnsureWriter(caller);
        if (caller.IsSupportView) throw new ForbiddenException();
        return await ReverseAsync(paymentId, r.Reason, ct);
    }

    /// <summary>
    /// Takes a received payment back out of the books with a reason (5–300 characters), under the bill lock: the bill's paid amount drops and
    /// the bill is recalculated. Audited with ids and amounts only (the reason stays on the payment). Online payments are not refunded
    /// through Paystack. 409 <c>payment_not_received</c> for a payment that isn't Succeeded (already reversed, pending, mismatch).
    /// </summary>
    public async Task<CareReversalResult> ReverseAsync(Guid paymentId, string? reason, CancellationToken ct)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length is < 5 or > 300) throw BadRequestException.Field("reason", "Say why in 5 to 300 characters.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(BillingService.LockKey(TenantId), ct);
        // Reloaded under the lock: a racing second reversal sees the first one's status, and a stale tracked row can't hide a change.
        var payment = await db.CarePayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw new NotFoundException("Payment");
        await db.Entry(payment).ReloadAsync(ct);
        if (payment.Status != CarePaymentStatus.Succeeded) throw new ConflictException(NotReceived, "payment_not_received");
        var bill = await BillingService.FreshBillAsync(db, payment.BillId, ct) ?? throw new NotFoundException("Bill");
        var now = clock.GetUtcNow();
        payment.Status = CarePaymentStatus.Reversed;
        payment.ReversedAt = now;
        payment.ReversedByUserId = user.UserId;
        payment.ReversalReason = why;
        bill.PaidMinor -= payment.AmountMinor;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = user.UserId, Action = "care.payment_reversed", EntityType = nameof(CarePayment), EntityId = payment.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new
            {
                paymentId = payment.Id, billId = payment.BillId, patientId = payment.PatientId, amountMinor = payment.AmountMinor, method = payment.Method.ToString(),
                payment.ReceiptNumber,
            }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await billing.RecalculateAsync(payment.BillId, ct);
        // M3: the review flag was about money held beyond the bill (an online overpayment) or a mismatched charge. With no credit left and
        // no Mismatch on the bill, there is nothing left to review.
        if (bill.NeedsReview && bill.CreditMinor == 0
            && !await db.CarePayments.AnyAsync(p => p.BillId == bill.Id && p.Status == CarePaymentStatus.Mismatch, ct))
        {
            bill.NeedsReview = false;
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        var message = payment.Method == PayMethod.Online
            ? "Payment reversed in the clinic's books only. No refund was sent through Paystack: refund the patient from your Paystack dashboard if you need to."
            : "Payment reversed. The bill balance is restored.";
        return new CareReversalResult(ToDto(payment), message);
    }

    // ---------- Receipts ----------

    /// <summary>
    /// The receipt PDF of a received (or later reversed) payment, for any clinic member: clinic, patient name and number, bill number, amount,
    /// method, reference and the bill's balance. No bill lines, nothing clinical. 404 for a pending or mismatched checkout, or another clinic's.
    /// </summary>
    public async Task<(byte[] Pdf, string FileName)> ReceiptAsync(Guid paymentId, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        return await ReceiptPdfAsync(paymentId, ct);
    }

    /// <summary>The receipt itself, without a staff check (Task 15's portal checks the patient link first).</summary>
    public async Task<(byte[] Pdf, string FileName)> ReceiptPdfAsync(Guid paymentId, CancellationToken ct)
    {
        var data = await ReceiptDataAsync(paymentId, ct);
        return (await pdf.ReceiptAsync(data, ct), $"{data.ReceiptNumber}.pdf");
    }

    /// <summary>What the receipt prints (no staff check). 404 for a pending or mismatched checkout, or another clinic's payment.</summary>
    public async Task<CareReceiptData> ReceiptDataAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.CarePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.ReceiptNumber != null && (p.Status == CarePaymentStatus.Succeeded || p.Status == CarePaymentStatus.Reversed), ct)
            ?? throw new NotFoundException("Receipt");
        var row = await (from b in db.PatientBills.AsNoTracking()
                         join p in db.Patients on b.PatientId equals p.Id
                         where b.Id == payment.BillId
                         select new { b.Number, b.BalanceMinor, p.FirstName, p.MiddleName, p.LastName, PatientNumber = p.Number }).FirstAsync(ct);
        var clinic = await db.ClinicProfiles.AsNoTracking().FirstAsync(ct);
        var reversed = payment.ReversedAt is { } at
            // M5: the free-text reason stays off the receipt (every member and, from Task 15, the patient can print it).
            ? $"Reversed on {CareCalendar.Today(at).ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}"
            : null;
        var name = string.Join(' ', new[] { row.FirstName, row.MiddleName, row.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));
        return new CareReceiptData(clinic.Name, clinic.Address, clinic.Phone, await PrescriptionService.LogoAsync(storage, clinic.LogoKey, ct),
            payment.ReceiptNumber!, CareCalendar.Today(payment.PaidAt).ToString("d MMMM yyyy", CultureInfo.InvariantCulture), name, row.PatientNumber,
            row.Number, Money.Format(payment.AmountMinor, payment.Currency), MethodLabel(payment.Method), payment.ProviderRef ?? payment.Reference,
            Money.Format(row.BalanceMinor, payment.Currency), reversed, !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct));
    }

    public static string MethodLabel(PayMethod method) => method switch
    {
        PayMethod.Online => "Online (Paystack)",
        PayMethod.Pos => "POS",
        PayMethod.Transfer => "Bank transfer",
        _ => "Cash",
    };

    public static CarePaymentDto ToDto(CarePayment p) =>
        new(p.Id, p.BillId, p.PatientId, p.AmountMinor, p.Method, p.ProviderRef ?? p.Reference, p.ReceiptNumber, p.IsDeposit, p.RecordedByUserId, p.PaidAt,
            p.Status, p.ReversedAt, p.ReversalReason);
}
