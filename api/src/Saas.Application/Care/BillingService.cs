using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

/// <summary>A bill line as staff see it. <see cref="ClinicalRef"/> is filled only for clinical callers and left out of the JSON otherwise.</summary>
public sealed record BillLineDto(
    Guid Id, Guid? PriceItemId, string Description, int Quantity, long UnitPriceMinor, long AmountMinor, BillLineSource Source, string? Reason,
    string? RemovedReason, DateTimeOffset? RemovedAt, DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClinicalRef = null);

/// <summary>A bill in a list. <c>CreditMinor</c> is money held beyond the net amount (balance 0, status InCredit).</summary>
public sealed record BillSummaryDto(
    Guid Id, string Number, Guid PatientId, string PatientNumber, string PatientName, Guid? VisitId, Guid? AdmissionId, BillStatus Status,
    long TotalMinor, long PaidMinor, long BalanceMinor, DateTimeOffset CreatedAt, DateTimeOffset? FinalisedAt, long CreditMinor, bool NeedsReview);

/// <summary>A discount or waiver as staff see it: kind, amount, reason, who and when.</summary>
public sealed record BillAdjustmentDto(Guid Id, AdjustmentKind Kind, long AmountMinor, string Reason, Guid ByUserId, DateTimeOffset At);

/// <summary>
/// One bill: totals (total, adjusted, paid, balance, credit), the review flag (an online overpayment), its lines, payments (every status) and
/// adjustments. Lines show the price-item name to clinical staff and leadership, the price category to the cashier, front desk and support (I2);
/// the clinical reference only to clinical callers.
/// </summary>
public sealed record BillDto(
    Guid Id, string Number, Guid PatientId, string PatientNumber, string PatientName, Guid? VisitId, Guid? AdmissionId, BillStatus Status,
    long TotalMinor, long PaidMinor, long BalanceMinor, DateTimeOffset CreatedAt, DateTimeOffset? FinalisedAt, long AdjustedMinor, long CreditMinor,
    bool NeedsReview, IReadOnlyList<BillLineDto> Lines, IReadOnlyList<CarePaymentDto> Payments, IReadOnlyList<BillAdjustmentDto> Adjustments);

public sealed record AddBillLineRequest(Guid? PriceItemId, int? Quantity, string? Reason);
public sealed record RemoveBillLineRequest(string? Reason);
public sealed record AdjustBillRequest(AdjustmentKind? Kind, long? AmountMinor, string? Reason);

/// <summary>
/// Patient bills: one per outpatient visit and one per admission, opened on first use. Lines freeze the price item's price at charge time.
/// Every change to a bill runs under the per-clinic bill lock (<see cref="LockKey"/>), which Task 11's payments take too.
/// Clinical services charge through <see cref="AddLineAsync"/> (no role check: the caller checked its own); staff routes go through
/// <see cref="AddManualLineAsync"/> and <see cref="RemoveLineAsync"/> (cashier or leadership, with a reason).
/// </summary>
public sealed class BillingService(IAppDbContext db, ITenantContext tenant, CareAccess access, ICurrentUser user, TimeProvider clock)
{
    public const int MaxQuantity = 10_000;
    public const string BillFinal = "This bill is final. No new charges can be added.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>The per-clinic care bill lock (a transaction-scoped advisory lock): bill opening, lines, removals and payments.</summary>
    public static long LockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0CA4EB111;

    // ---------- Used by clinical services ----------

    /// <summary>The visit's bill, created (BILL-{yyyy}-{00000}) on first use. 404 if the patient isn't this clinic's.</summary>
    public Task<PatientBill> OpenBillForVisitAsync(Guid patientId, Guid visitId, CancellationToken ct) =>
        OpenAsync(patientId, visitId, null, ct);

    /// <summary>The admission's bill, created on first use. 404 if the patient isn't this clinic's.</summary>
    public Task<PatientBill> OpenBillForAdmissionAsync(Guid patientId, Guid admissionId, CancellationToken ct) =>
        OpenAsync(patientId, null, admissionId, ct);

    /// <summary>
    /// Makes the admission's bill final at discharge (Task 10), under the bill lock: no new lines from then on (409 "This bill is final. No
    /// new charges can be added."). The balance stays as it is, so money still owed stays open as a debt and payments are still taken.
    /// Opens the bill first if it doesn't exist. Calling it again keeps the first time. Saves.
    /// </summary>
    public async Task<PatientBill> FinaliseAdmissionBillAsync(Guid patientId, Guid admissionId, DateTimeOffset at, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var bill = await OpenAsync(patientId, null, admissionId, ct); // takes the bill lock, held to the end of the transaction
        bill.FinalisedAt ??= at;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return bill;
    }

    /// <summary>
    /// Charges <paramref name="qty"/> of a price item on a bill at the item's current price (frozen on the line) and recomputes the totals.
    /// Auto source; <paramref name="clinicalRef"/> (for example a prescription item id) is only ever shown to clinical staff.
    /// 400 for an inactive or unknown item or a quantity outside 1–10,000; 404 for an unknown bill.
    /// </summary>
    public Task<BillLine> AddLineAsync(Guid billId, Guid priceItemId, int qty, string? clinicalRef, CancellationToken ct) =>
        AddAsync(billId, priceItemId, qty, BillLineSource.Auto, clinicalRef, null, ct);

    /// <summary>
    /// Recomputes the bill from the lines that aren't removed, its discount and waiver adjustments and the paid amount, then saves:
    /// <list type="bullet">
    /// <item>net = max(0, total − adjusted); balance = max(0, net − paid); credit = max(0, paid − net);</item>
    /// <item>status: InCredit when credit &gt; 0; otherwise PartPaid or Unpaid while a balance is owed; with nothing owed, Unpaid for an
    /// empty bill, Waived when a waiver was needed or nothing was paid (adjustments cleared it), otherwise Paid.</item>
    /// </list>
    /// Adjustments are fixed amounts, so a waiver covers only what was on the bill when it was granted: a later charge is owed (ruling).
    /// </summary>
    public Task RecalculateAsync(Guid billId, CancellationToken ct) => RecalculateBillAsync(db, billId, ct);

    /// <summary><see cref="RecalculateAsync"/> on a given context, for helpers outside the service (bed charges). The caller holds the bill lock.</summary>
    internal static async Task RecalculateBillAsync(IAppDbContext db, Guid billId, CancellationToken ct)
    {
        var bill = await FreshBillAsync(db, billId, ct) ?? throw new NotFoundException("Bill");
        bill.TotalMinor = Math.Max(0, await db.BillLines.Where(l => l.BillId == billId && l.RemovedAt == null).SumAsync(l => (long?)l.AmountMinor, ct) ?? 0);
        var adjustments = await db.BillAdjustments.AsNoTracking().Where(a => a.BillId == billId).Select(a => new { a.Kind, a.AmountMinor }).ToListAsync(ct);
        bill.AdjustedMinor = adjustments.Sum(a => a.AmountMinor);
        var (status, balance, credit) = Compute(bill.TotalMinor, bill.AdjustedMinor, bill.PaidMinor, adjustments.Any(a => a.Kind == AdjustmentKind.Waiver));
        bill.Status = status;
        bill.BalanceMinor = balance;
        bill.CreditMinor = credit;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The bill, tracked and reloaded from the database (I1). This context may have loaded it in an earlier transaction; a stale instance
    /// keeps an old original-value snapshot, and SaveChanges would then skip any recomputed column that happens to equal it. Reloading
    /// refreshes both the values and the snapshot, so every changed column is written. Callers hold the bill lock and have saved their own
    /// payment changes first. Null when the bill doesn't exist (or is another clinic's).
    /// Carry (Task 11): a tracked bill with unsaved changes would lose them to the reload, so that throws InvalidOperationException instead.
    /// </summary>
    internal static async Task<PatientBill?> FreshBillAsync(IAppDbContext db, Guid billId, CancellationToken ct)
    {
        var bill = await db.PatientBills.FirstOrDefaultAsync(b => b.Id == billId, ct);
        if (bill is null) return null;
        // Entry() detects changes on this entity, so a property set since the last save shows as Modified.
        if (db.Entry(bill).State != EntityState.Unchanged)
            throw new InvalidOperationException($"Bill {billId} has unsaved changes; save them before recalculating it.");
        await db.Entry(bill).ReloadAsync(ct);
        return bill;
    }

    /// <summary>The bill's status, balance and credit from its total, adjustments and paid amount (see <see cref="RecalculateAsync"/>).</summary>
    public static (BillStatus Status, long Balance, long Credit) Compute(long total, long adjusted, long paid, bool waived)
    {
        var net = Math.Max(0, total - adjusted);
        var balance = Math.Max(0, net - paid);
        var credit = Math.Max(0, paid - net);
        var status = credit > 0 ? BillStatus.InCredit
            : balance > 0 ? (paid > 0 ? BillStatus.PartPaid : BillStatus.Unpaid)
            : total == 0 && paid == 0 ? BillStatus.Unpaid
            : waived || paid == 0 ? BillStatus.Waived
            : BillStatus.Paid;
        return (status, balance, credit);
    }

    /// <summary>
    /// A discount or waiver (leadership: the medical director or an admin), with a reason, under the bill lock. The amount is at most the
    /// balance now (400 <c>amountMinor</c>; 409 when nothing is owed), so it covers only what is on the bill. Audited with ids and amounts.
    /// Final bills take adjustments too (forgiving a debt left at discharge).
    /// </summary>
    public async Task<BillDto> AdjustAsync(Guid billId, AdjustBillRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureLeadership(caller);
        if (caller.IsSupportView) throw new ForbiddenException();
        if (r.Kind is not { } kind || !Enum.IsDefined(kind)) throw BadRequestException.Field("kind", "Choose a discount or a waiver.");
        if (r.AmountMinor is not { } amount || amount < 1) throw BadRequestException.Field("amountMinor", "Enter an amount above zero.");
        var reason = Reason(r.Reason, kind == AdjustmentKind.Waiver ? "Give a reason for the waiver." : "Give a reason for the discount.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId), ct);
        var bill = await db.PatientBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, ct) ?? throw new NotFoundException("Bill");
        if (bill.BalanceMinor <= 0) throw new ConflictException("Nothing is owed on this bill.", "bill_nothing_owed");
        if (amount > bill.BalanceMinor)
            throw BadRequestException.Field("amountMinor", $"The balance is {Money.Format(bill.BalanceMinor, "NGN")}. Enter that amount or less.");
        var now = clock.GetUtcNow();
        var adjustment = new BillAdjustment
        {
            TenantId = TenantId, BillId = bill.Id, PatientId = bill.PatientId, Kind = kind, AmountMinor = amount, Reason = reason, ByUserId = caller.UserId, At = now,
        };
        db.BillAdjustments.Add(adjustment);
        // Ids and amounts only: the typed reason stays on the adjustment row.
        db.AuditLogs.Add(new Saas.Domain.Core.AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.bill_adjusted", EntityType = nameof(PatientBill), EntityId = bill.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { adjustmentId = adjustment.Id, billId = bill.Id, patientId = bill.PatientId, kind = kind.ToString(), amountMinor = amount, balanceBeforeMinor = bill.BalanceMinor }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(bill.Id, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(billId, caller, ct);
    }

    // ---------- Staff routes ----------

    public async Task<IReadOnlyList<BillSummaryDto>> ListAsync(Guid? patientId, BillStatus? status, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        return await (from b in db.PatientBills.AsNoTracking()
                      join p in db.Patients on b.PatientId equals p.Id
                      where (patientId == null || b.PatientId == patientId) && (status == null || b.Status == status)
                      orderby b.CreatedAt descending
                      select new BillSummaryDto(b.Id, b.Number, b.PatientId, p.Number, p.FirstName + " " + p.LastName, b.VisitId, b.AdmissionId,
                          b.Status, b.TotalMinor, b.PaidMinor, b.BalanceMinor, b.CreatedAt, b.FinalisedAt, b.CreditMinor, b.NeedsReview))
            .Take(200).ToListAsync(ct);
    }

    public async Task<BillDto> GetAsync(Guid id, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        return await ReadAsync(id, caller, ct);
    }

    public async Task<BillDto> AddManualLineAsync(Guid billId, AddBillLineRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PriceListService.EnsureWriter(caller);
        var reason = Reason(r.Reason, "Give a reason for adding this line.");
        if (r.PriceItemId is not { } itemId) throw BadRequestException.Field("priceItemId", "Choose an item from the price list.");
        await AddAsync(billId, itemId, r.Quantity ?? 1, BillLineSource.Manual, null, reason, ct);
        return await ReadAsync(billId, caller, ct);
    }

    public async Task<BillDto> RemoveLineAsync(Guid billId, Guid lineId, RemoveBillLineRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PriceListService.EnsureWriter(caller);
        var reason = Reason(r.Reason, "Give a reason for removing this line.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId), ct);
        var line = await db.BillLines.FirstOrDefaultAsync(l => l.Id == lineId && l.BillId == billId, ct) ?? throw new NotFoundException("Bill line");
        if (line.RemovedAt is not null) throw new ConflictException("This line was already removed.", "line_removed");
        line.RemovedReason = reason;
        line.RemovedAt = clock.GetUtcNow();
        line.RemovedByUserId = user.UserId;
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(billId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(billId, caller, ct);
    }

    /// <summary>The cashier (or leadership) has dealt with an online overpayment: clears the review flag, under the bill lock.</summary>
    public async Task<BillDto> MarkReviewedAsync(Guid billId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PriceListService.EnsureWriter(caller);
        if (caller.IsSupportView) throw new ForbiddenException();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId), ct);
        var bill = await FreshBillAsync(db, billId, ct) ?? throw new NotFoundException("Bill");
        bill.NeedsReview = false;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(billId, caller, ct);
    }

    // ---------- Internals ----------

    private async Task<PatientBill> OpenAsync(Guid patientId, Guid? visitId, Guid? admissionId, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId), ct);
        var existing = await db.PatientBills.FirstOrDefaultAsync(b =>
            (visitId != null && b.VisitId == visitId) || (admissionId != null && b.AdmissionId == admissionId), ct);
        if (existing is not null)
        {
            if (existing.PatientId != patientId) throw new ConflictException("That bill belongs to another patient.", "bill_patient_mismatch");
            return existing;
        }
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient");

        var number = await db.NextDocumentNumberAsync(DocumentCounter.CareBill, ct);
        var year = CareCalendar.Today(clock.GetUtcNow()).Year;
        var bill = new PatientBill
        {
            TenantId = TenantId, PatientId = patientId, VisitId = visitId, AdmissionId = admissionId,
            Number = $"BILL-{year.ToString(CultureInfo.InvariantCulture)}-{number.ToString("D5", CultureInfo.InvariantCulture)}",
        };
        db.PatientBills.Add(bill);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return bill;
    }

    private async Task<BillLine> AddAsync(Guid billId, Guid priceItemId, int qty, BillLineSource source, string? clinicalRef, string? reason, CancellationToken ct)
    {
        if (qty is < 1 or > MaxQuantity) throw BadRequestException.Field("quantity", "Enter a quantity from 1 to 10,000.");
        if (clinicalRef is { Length: > 100 }) throw new ArgumentException("A clinical reference can have at most 100 characters.", nameof(clinicalRef));

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId), ct);
        // Read under the lock, so a line racing the discharge either lands before the bill is final or is refused.
        var finalised = await db.PatientBills.AsNoTracking().Where(b => b.Id == billId).Select(b => new { b.FinalisedAt }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Bill");
        if (finalised.FinalisedAt is not null) throw new ConflictException(BillFinal, "bill_final");
        var item = await db.PriceItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == priceItemId, ct);
        if (item is null || !item.Active) throw BadRequestException.Field("priceItemId", "That item isn't on the active price list.");

        var line = new BillLine
        {
            TenantId = TenantId, BillId = billId, PriceItemId = item.Id, Description = item.Name, Quantity = qty,
            UnitPriceMinor = item.PriceMinor, AmountMinor = checked(item.PriceMinor * qty), Source = source,
            ClinicalRef = clinicalRef, Reason = reason, AddedByUserId = user.UserId,
        };
        db.BillLines.Add(line);
        await db.SaveChangesAsync(ct);
        await RecalculateAsync(billId, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return line;
    }

    /// <summary>
    /// Final review I2 (user decision): who sees bill-line item names. Clinical staff and leadership (the medical director, an admin) do; the
    /// cashier, front desk and support's view see each line by its price category ("Drug", "Test") and amount only, with no price-item id
    /// to look the name up by. The patient's own portal keeps the names (<see cref="PortalService.BillsAsync"/>).
    /// </summary>
    public static bool SeesItemNames(CareCaller c) => !c.IsSupportView && (c.IsClinical || c.IsLeadership);

    /// <summary>The category a non-clinical role reads a line as: "Drug", "Test", "Consultation"…; a line with no price item reads "Other".</summary>
    public static string CategoryLabel(PriceCategory? category) => (category ?? PriceCategory.Other).ToString();

    internal async Task<BillDto> ReadAsync(Guid id, CareCaller caller, CancellationToken ct)
    {
        var clinical = caller.IsClinical;
        var names = SeesItemNames(caller);
        var row = await (from b in db.PatientBills.AsNoTracking()
                         join p in db.Patients on b.PatientId equals p.Id
                         where b.Id == id
                         select new { Bill = b, p.Number, Name = p.FirstName + " " + p.LastName }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Bill");
        var lines = await db.BillLines.AsNoTracking().Where(l => l.BillId == id).OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToListAsync(ct);
        var itemIds = lines.Where(l => l.PriceItemId != null).Select(l => l.PriceItemId!.Value).Distinct().ToList();
        var categories = names ? new Dictionary<Guid, PriceCategory>() : await db.PriceItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Category, ct);
        string Describe(BillLine l) => names ? l.Description
            : CategoryLabel(l.PriceItemId is { } itemId && categories.TryGetValue(itemId, out var c) ? c : null);
        var payments = await db.CarePayments.AsNoTracking().Where(p => p.BillId == id).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        var adjustments = await db.BillAdjustments.AsNoTracking().Where(a => a.BillId == id).OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        var bill = row.Bill;
        // Non-clinical staff never get the clinical reference; the cashier, front desk and support's view get the category, not the name.
        return new BillDto(bill.Id, bill.Number, bill.PatientId, row.Number, row.Name, bill.VisitId, bill.AdmissionId, bill.Status, bill.TotalMinor,
            bill.PaidMinor, bill.BalanceMinor, bill.CreatedAt, bill.FinalisedAt, bill.AdjustedMinor, bill.CreditMinor, bill.NeedsReview,
            lines.Select(l => new BillLineDto(l.Id, names ? l.PriceItemId : null, Describe(l), l.Quantity, l.UnitPriceMinor, l.AmountMinor, l.Source, l.Reason,
                l.RemovedReason, l.RemovedAt, l.CreatedAt, clinical ? l.ClinicalRef : null)).ToList(),
            payments.Select(PaymentService.ToDto).ToList(),
            adjustments.Select(a => new BillAdjustmentDto(a.Id, a.Kind, a.AmountMinor, a.Reason, a.ByUserId, a.At)).ToList());
    }

    private static string Reason(string? raw, string missing)
    {
        var reason = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        if (reason is null) throw BadRequestException.Field("reason", missing);
        if (reason.Length > 300) throw BadRequestException.Field("reason", "The reason can have at most 300 characters.");
        return reason;
    }
}
