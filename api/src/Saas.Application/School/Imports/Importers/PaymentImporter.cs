using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Payments received at the school (cash, transfer, POS), one per row: <c>invoice_no</c> (or <c>admission_no</c> when the student has exactly
/// one open invoice), <c>amount</c> in naira, <c>method</c>, <c>reference</c> and <c>paid_on</c>. Each row is recorded through the payments
/// screen's own path (<see cref="FeePaymentService.RecordAsync"/>), so receipt numbers, the receipt emails and the parents' notices are the
/// screen's; they are rows written in the engine's transaction, so nothing goes out before the import commits. Rows are checked against each
/// other too: two rows on one invoice must fit its balance together, and a reference can be used once (in the school's payments or the file).
/// Optional scope <c>termId</c>: an admission number then looks only at that term's invoices. The fee policy: proprietor, principal and bursar.
/// </summary>
public sealed class PaymentImporter(IAppDbContext db, ITenantContext tenant, ICurrentUser user, FeePaymentService payments, TimeProvider clock)
    : IImporter, IImportNotice
{
    public const string KindName = "payments";
    public const string Sheet = "Payments";
    public const string NoInvoice = "Enter the invoice number or the admission number.";
    public const string InvoiceMissing = "This invoice was not found.";
    public const string InvoiceCancelled = "This invoice is cancelled, so it can't take payments.";
    public const string OtherStudent = "This invoice is for another student. Check the invoice and admission numbers.";
    public const string StudentMissing = "This student was not found.";
    public const string BadMethod = "Choose Cash, Transfer or Pos.";
    public const string NoDate = "Enter the date the money was received.";
    public const string FutureDate = "The payment date can't be in the future.";
    public const string LongReference = "Use 100 characters or fewer for the reference.";
    public const string ReferenceUsed = "This reference has already been used for a payment.";
    public const string ReferenceEarlier = "This reference is used on an earlier row of the file.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Payment import finished";
    public string NoticeLink => "/app/fees/payments";

    private static readonly ImportColumns AllColumns = new(
    [
        new("invoice_no", "invoice_no", false, "The invoice number, for example INV-2026-00012. Or leave it blank and fill admission_no."),
        new("admission_no", "admission_no", false, "The student's admission number. Enough on its own when the student has one open invoice."),
        new("amount", "amount", true, FeeImports.AmountHelp + " No more than what is still owed."),
        new("method", "method", true, "Cash, Transfer or Pos."),
        new("reference", "reference", false, "Optional. The teller, transfer or POS reference. Each reference can be used once."),
        new("paid_on", "paid_on", true, "The day the money was received, for example 2026-10-26 or 26/10/2026. Not in the future."),
    ]);

    private static readonly string[] Notes =
    [
        "One payment per row on the Payments sheet. Delete nothing from the heading row.",
        "invoice_no: the invoice the money is for. Or leave it blank and enter the admission_no of a student with one open invoice.",
        "amount: in naira with up to two decimal places, for example 45000 or 12,500.50. Rows on the same invoice must fit its balance together.",
        "method: Cash, Transfer or Pos. Online payments through Paystack are recorded on their own.",
        "reference: optional. A reference already used for a payment, or used twice in the file, is refused.",
        "paid_on: 2026-10-26, 26/10/2026 or an Excel date. Not in the future.",
        "Every row is recorded as a new payment with its own receipt, and the parents are told, as when you record one on the payments page.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        FeeImports.EnsureFeeRole(user);
        if (scope.Get("termId") is not null) scope.RequireGuid("termId");
        return Task.CompletedTask;
    }

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(Sheet, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        Guid? termId = scope.Get("termId") is null ? null : scope.RequireGuid("termId");
        string? termName = null;
        if (termId is { } tid)
            termName = await db.Terms.AsNoTracking().Where(t => t.Id == tid).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Term");

        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var currency = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Currency).FirstAsync(ct);
        var today = SchoolCalendar.Today(clock.GetUtcNow());

        string Cell(ImportRowData r, string key) => (r.Cells.GetValueOrDefault(key) ?? "").Trim();
        var numbers = rows.Select(r => Cell(r, "invoice_no").ToLowerInvariant()).Where(n => n.Length > 0).Distinct().ToList();
        var admissions = rows.Select(r => Cell(r, "admission_no").ToLowerInvariant()).Where(n => n.Length > 0).Distinct().ToList();
        var references = rows.Select(r => Cell(r, "reference").ToLowerInvariant()).Where(n => n.Length > 0).Distinct().ToList();

        var invoices = await (from i in db.StudentInvoices.AsNoTracking()
                              join e in db.Enrolments on i.EnrolmentId equals e.Id
                              join s in db.Students on e.StudentId equals s.Id
                              where numbers.Contains(i.Number.ToLower()) || admissions.Contains(s.AdmissionNo.ToLower())
                              select new InvoiceRef(i.Id, i.Number, i.TermId, i.Status, i.Balance, s.Id, s.AdmissionNo, s.FirstName, s.MiddleName, s.LastName))
            .ToListAsync(ct);
        var students = await db.Students.AsNoTracking().Where(s => admissions.Contains(s.AdmissionNo.ToLower()))
            .Select(s => new { s.Id, s.AdmissionNo }).ToListAsync(ct);
        // A reversed payment gave its money back, so its reference may be used again; any payment that still counts holds its reference.
        var used = (await db.FeePayments.AsNoTracking()
                .Where(p => p.Reference != null && p.ReversedAt == null && p.Status == FeePaymentStatus.Succeeded && references.Contains(p.Reference.ToLower()))
                .Select(p => p.Reference!).ToListAsync(ct))
            .Select(r => r.ToLowerInvariant()).ToHashSet();

        var byNumber = invoices.GroupBy(i => i.Number.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var studentByAdmission = students.GroupBy(s => s.AdmissionNo.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        var seenReferences = new HashSet<string>();
        var taken = new Dictionary<Guid, long>(); // what earlier valid rows of the file already put on each invoice

        var results = new List<ImportRowResult>(rows.Count);
        foreach (var row in rows)
        {
            var number = Cell(row, "invoice_no");
            var admission = Cell(row, "admission_no");
            var label = number.Length > 0 ? number : admission;
            ImportRowResult Error(string message) => new(row.Row, ImportRowStatus.Error, label, message, null);

            // Which invoice.
            InvoiceRef? invoice;
            if (number.Length > 0)
            {
                if (!byNumber.TryGetValue(number.ToLowerInvariant(), out invoice)) { results.Add(Error(InvoiceMissing)); continue; }
                label = $"{invoice.Number}, {invoice.StudentName}";
                if (admission.Length > 0 && !string.Equals(admission, invoice.AdmissionNo, StringComparison.OrdinalIgnoreCase)) { results.Add(Error(OtherStudent)); continue; }
            }
            else if (admission.Length > 0)
            {
                if (!studentByAdmission.TryGetValue(admission.ToLowerInvariant(), out var studentId)) { results.Add(Error(StudentMissing)); continue; }
                var open = invoices.Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Cancelled && i.Balance > 0
                    && (termId is null || i.TermId == termId)).ToList();
                var when = termName is null ? "" : $" for {termName}";
                if (open.Count == 0) { results.Add(Error($"This student has no open invoice{when}. Enter the invoice number.")); continue; }
                if (open.Count > 1) { results.Add(Error($"This student has more than one open invoice{when}. Enter the invoice number.")); continue; }
                invoice = open[0];
                label = $"{invoice.Number}, {invoice.StudentName}";
            }
            else { results.Add(Error(NoInvoice)); continue; }
            if (invoice.Status == InvoiceStatus.Cancelled) { results.Add(Error(InvoiceCancelled)); continue; }

            // The payment itself, with the screen's sentences.
            var methodText = Cell(row, "method");
            if (!Enum.TryParse<FeePaymentMethod>(methodText, true, out var method) || method == FeePaymentMethod.Online || !Enum.IsDefined(method)
                || methodText.Any(char.IsDigit)) { results.Add(Error(BadMethod)); continue; }
            var amount = FeeImports.ParseNaira(Cell(row, "amount"), out var amountError);
            if (amount is null) { results.Add(Error(amountError!)); continue; }
            if (amount <= 0) { results.Add(Error(FeeImports.AboveZero)); continue; }
            var dateText = Cell(row, "paid_on");
            if (dateText.Length == 0) { results.Add(Error(NoDate)); continue; }
            var paidOn = FeeImports.ParseDate(dateText, out var dateError);
            if (paidOn is null) { results.Add(Error(dateError ?? NoDate)); continue; }
            if (paidOn > today) { results.Add(Error(FutureDate)); continue; }
            var reference = Cell(row, "reference");
            if (reference.Length > 100) { results.Add(Error(LongReference)); continue; }
            if (reference.Length > 0)
            {
                var key = reference.ToLowerInvariant();
                if (used.Contains(key)) { results.Add(Error(ReferenceUsed)); continue; }
                if (!seenReferences.Add(key)) { results.Add(Error(ReferenceEarlier)); continue; }
            }

            // The balance, after what earlier rows of the file put on the same invoice.
            var remaining = invoice.Balance - taken.GetValueOrDefault(invoice.Id);
            if (amount > remaining)
            {
                results.Add(Error($"That is more than the balance of {Money.Format(Math.Max(0, remaining), currency)}. Enter that amount or less."));
                continue;
            }
            taken[invoice.Id] = taken.GetValueOrDefault(invoice.Id) + amount.Value;

            results.Add(new ImportRowResult(row.Row, ImportRowStatus.New, label, null,
                new PaymentPlan(invoice.Id, new RecordPaymentRequest(amount.Value, method.ToString(), reference.Length > 0 ? reference : null, paidOn))));
        }
        return results;
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var planned = rows.Where(r => r.Status == ImportRowStatus.New && r.Plan is PaymentPlan).ToList();
        if (planned.Count == 0) return new ImportApplied(0, 0);

        // The fee lock first, in the engine's transaction: the same single lock RecordAsync (and Generate, reversals, online confirms) takes,
        // so there is no second lock to order against, and it is re-entrant for the RecordAsync calls below. The evaluation ran before the
        // transaction, so another import or a screen payment may have landed since; under the lock nothing can change, so check again.
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        await db.LockAsync(FeeService.GenerateLockKey(tenantId), ct);
        await RecheckAsync(planned, tenantId, ct);

        var created = 0;
        foreach (var row in planned)
        {
            if (row.Plan is not PaymentPlan plan) continue;
            // The screen's path: the fee lock, the receipt number, the invoice recalculated, the receipt emails and parent notices queued.
            await payments.RecordAsync(plan.InvoiceId, plan.Request, ct);
            created++;
        }
        return new ImportApplied(created, 0);
    }

    /// <summary>
    /// Under the fee lock: each reference is still unused and each invoice is still open with room for the file's rows, read live. Any row that
    /// no longer fits is a 409 <c>import_errors</c> naming the row, as a confirm that found it at evaluation would be; nothing is recorded.
    /// </summary>
    private async Task RecheckAsync(IReadOnlyList<ImportRowResult> planned, Guid tenantId, CancellationToken ct)
    {
        var plans = planned.Select(r => (Row: r, Plan: (PaymentPlan)r.Plan!)).ToList();
        var references = plans.Select(p => p.Plan.Request.Reference?.ToLowerInvariant()).OfType<string>().Distinct().ToList();
        var invoiceIds = plans.Select(p => p.Plan.InvoiceId).Distinct().ToList();
        var used = (await db.FeePayments.AsNoTracking()
                .Where(p => p.Reference != null && p.ReversedAt == null && p.Status == FeePaymentStatus.Succeeded && references.Contains(p.Reference.ToLower()))
                .Select(p => p.Reference!).ToListAsync(ct))
            .Select(r => r.ToLowerInvariant()).ToHashSet();
        var invoices = await db.StudentInvoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => new { i.Status, i.Balance }, ct);
        var currency = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Currency).FirstAsync(ct);

        var taken = new Dictionary<Guid, long>();
        var errors = new List<ImportRowResult>();
        foreach (var (row, plan) in plans)
        {
            string? message = null;
            if (plan.Request.Reference is { } reference && used.Contains(reference.ToLowerInvariant())) message = ReferenceUsed;
            else if (!invoices.TryGetValue(plan.InvoiceId, out var invoice)) message = InvoiceMissing;
            else if (invoice.Status == InvoiceStatus.Cancelled) message = InvoiceCancelled;
            else
            {
                var remaining = invoice.Balance - taken.GetValueOrDefault(plan.InvoiceId);
                if (plan.Request.AmountMinor > remaining)
                    message = $"That is more than the balance of {Money.Format(Math.Max(0, remaining), currency)}. Enter that amount or less.";
                else taken[plan.InvoiceId] = taken.GetValueOrDefault(plan.InvoiceId) + plan.Request.AmountMinor;
            }
            if (message is not null) errors.Add(row with { Status = ImportRowStatus.Error, Message = message, Plan = null });
        }
        if (errors.Count > 0) throw new ImportErrorsException(ImportEngine.ErrorsMessage, errors.Select(e => (object)ImportEngine.ToPreviewRow(e)).ToList());
    }

    private sealed record PaymentPlan(Guid InvoiceId, RecordPaymentRequest Request);

    private sealed record InvoiceRef(Guid Id, string Number, Guid TermId, InvoiceStatus Status, long Balance, Guid StudentId, string AdmissionNo,
        string FirstName, string? MiddleName, string LastName)
    {
        public string StudentName => ParentAccess.FullName(FirstName, MiddleName, LastName);
    }
}
