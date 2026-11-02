using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record FeeItemDto(Guid Id, string Name, bool IsOptional);
public sealed record SaveFeeItemRequest(string? Name, bool IsOptional);
public sealed record FeeLineInput(Guid FeeItemId, long AmountMinor);
public sealed record SaveFeeStructureRequest(IReadOnlyList<FeeLineInput>? Lines);
public sealed record FeeStructureLineDto(Guid FeeItemId, string FeeItemName, bool IsOptional, long AmountMinor);
public sealed record FeeStructureDto(Guid TermId, Guid LevelId, IReadOnlyList<FeeStructureLineDto> Lines);
public sealed record FeeStructureCopyDto(Guid TermId, int Levels, IReadOnlyList<FeeStructureLineDto> Lines);
public sealed record GenerateInvoicesRequest(Guid TermId);
/// <summary>An invoice Generate mentions: enough for the notice to link to it without reloading the list.</summary>
public sealed record GeneratedInvoiceRefDto(Guid Id, string Number, string StudentName);
/// <summary>
/// <c>NeedsReview</c>: refreshed invoices now flagged for review (a discount no longer fits, the instalments were cleared, or the level has no
/// structure). <c>WithPayments</c>: invoices left alone because they have payments, listed only when their lines differ from the structure.
/// <c>Invoices</c> names every id in both lists.
/// </summary>
public sealed record GenerateInvoicesResult(int Created, int Refreshed, int Skipped, IReadOnlyList<Guid> NeedsReview, IReadOnlyList<Guid> WithPayments,
    IReadOnlyList<GeneratedInvoiceRefDto> Invoices);
public sealed record OptionalItemsRequest(IReadOnlyList<Guid>? FeeItemIds);
public sealed record DiscountRequest(string? Kind, decimal Value, string? Reason);
public sealed record InstalmentInput(DateOnly DueDate, long AmountMinor);
public sealed record InvoiceLineDto(Guid Id, Guid? FeeItemId, string Description, long AmountMinor);
public sealed record InstalmentDto(Guid Id, DateOnly DueDate, long AmountMinor);
public sealed record InvoiceSummaryDto(
    Guid Id, string Number, Guid EnrolmentId, Guid StudentId, string StudentName, string AdmissionNo, Guid ClassId, string ClassName,
    Guid TermId, long Total, long Paid, long Balance, string Status, DateOnly DueDate);
public sealed record InvoiceDto(
    Guid Id, string Number, Guid EnrolmentId, Guid StudentId, string StudentName, string AdmissionNo, Guid ClassId, string ClassName,
    Guid TermId, long Total, long Paid, long Balance, string Status, DateOnly DueDate,
    IReadOnlyList<InvoiceLineDto> Lines, IReadOnlyList<InstalmentDto> Instalments, bool NeedsReview, IReadOnlyList<FeePaymentDto> Payments);

/// <summary>
/// Fee items, the fee structure per term and level, and the invoices built from them. Money is in kobo. Status rules: Unpaid (nothing paid),
/// PartlyPaid, Paid (paid covers the total, including a total of zero) and Cancelled (set explicitly, excluded from debtors, balance zero).
/// </summary>
public sealed class FeeService(IAppDbContext db, ITenantContext tenant, SchoolFeed feed, IDbErrors dbErrors)
{
    public const int MaxInstalments = 12;
    public const int DefaultDueDays = 14;
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Fee items ----------

    public async Task<IReadOnlyList<FeeItemDto>> ListItemsAsync(CancellationToken ct) =>
        await db.FeeItems.OrderBy(i => i.Name).Select(i => new FeeItemDto(i.Id, i.Name, i.IsOptional)).ToListAsync(ct);

    public async Task<FeeItemDto> CreateItemAsync(SaveFeeItemRequest r, CancellationToken ct)
    {
        var name = CleanName(r);
        if (await db.FeeItems.AnyAsync(i => i.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("A fee item with that name already exists.", "duplicate_fee_item");
        var item = new FeeItem { TenantId = TenantId, Name = name, IsOptional = r.IsOptional };
        db.FeeItems.Add(item);
        await SaveItemAsync(ct);
        return new FeeItemDto(item.Id, item.Name, item.IsOptional);
    }

    public async Task<FeeItemDto> UpdateItemAsync(Guid id, SaveFeeItemRequest r, CancellationToken ct)
    {
        var item = await db.FeeItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fee item");
        var name = CleanName(r);
        if (await db.FeeItems.AnyAsync(i => i.Id != id && i.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("A fee item with that name already exists.", "duplicate_fee_item");
        item.Name = name;
        item.IsOptional = r.IsOptional;
        await SaveItemAsync(ct);
        return new FeeItemDto(item.Id, item.Name, item.IsOptional);
    }

    public async Task DeleteItemAsync(Guid id, CancellationToken ct)
    {
        var item = await db.FeeItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fee item");
        if (await db.FeeStructureLines.AnyAsync(l => l.FeeItemId == id, ct) || await db.InvoiceLines.AnyAsync(l => l.FeeItemId == id, ct))
            throw new ConflictException("This fee item is used in a fee structure or an invoice, so it can't be deleted.", "fee_item_in_use");
        db.FeeItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    private static string CleanName(SaveFeeItemRequest r)
    {
        var name = r.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw BadRequestException.Field("name", "Enter a name for the fee item.");
        if (name.Length > 80) throw BadRequestException.Field("name", "Use 80 characters or fewer for the name.");
        return name;
    }

    private async Task SaveItemAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (Exception e) when (dbErrors.IsUniqueViolation(e)) { throw new ConflictException("A fee item with that name already exists.", "duplicate_fee_item"); }
    }

    // ---------- Fee structure ----------

    public async Task<IReadOnlyList<FeeStructureDto>> GetStructureAsync(Guid termId, CancellationToken ct)
    {
        await TermAsync(termId, ct);
        var rows = await StructureLinesAsync(termId, null, ct);
        return rows.GroupBy(r => r.LevelId).Select(g => new FeeStructureDto(termId, g.Key, g.Select(x => x.Line).ToList())).ToList();
    }

    public async Task<FeeStructureDto> SaveStructureAsync(Guid termId, Guid levelId, SaveFeeStructureRequest r, CancellationToken ct)
    {
        await TermAsync(termId, ct);
        if (!await db.Levels.AnyAsync(l => l.Id == levelId, ct)) throw new NotFoundException("Level");
        var lines = r.Lines ?? throw BadRequestException.Field("lines", "Send the fee lines for this level.");
        if (lines.Select(l => l.FeeItemId).Distinct().Count() != lines.Count)
            throw BadRequestException.Field("lines", "Each fee item can appear only once.");
        if (lines.Any(l => l.AmountMinor <= 0))
            throw BadRequestException.Field("lines", "Each amount must be more than zero.");
        var ids = lines.Select(l => l.FeeItemId).ToList();
        if (await db.FeeItems.CountAsync(i => ids.Contains(i.Id), ct) != ids.Count)
            throw BadRequestException.Field("lines", "One of the fee items no longer exists. Reload and try again.");

        var existing = await db.FeeStructureLines.Where(l => l.TermId == termId && l.LevelId == levelId).ToListAsync(ct);
        foreach (var line in existing.Where(e => !ids.Contains(e.FeeItemId))) db.FeeStructureLines.Remove(line);
        foreach (var input in lines)
        {
            var row = existing.FirstOrDefault(e => e.FeeItemId == input.FeeItemId);
            if (row is null)
                db.FeeStructureLines.Add(new FeeStructureLine { TenantId = TenantId, TermId = termId, LevelId = levelId, FeeItemId = input.FeeItemId, AmountMinor = input.AmountMinor });
            else row.AmountMinor = input.AmountMinor;
        }
        await db.SaveChangesAsync(ct);
        return new FeeStructureDto(termId, levelId, (await StructureLinesAsync(termId, levelId, ct)).Select(x => x.Line).ToList());
    }

    /// <summary>Copies the structure of another term into this one, for the levels that have no fee lines in this term yet.</summary>
    public async Task<FeeStructureCopyDto> CopyStructureAsync(Guid termId, Guid fromTermId, CancellationToken ct)
    {
        await TermAsync(termId, ct);
        await TermAsync(fromTermId, ct);
        if (termId == fromTermId) throw BadRequestException.Field("fromTermId", "Choose a different term to copy from.");
        var have = await db.FeeStructureLines.Where(l => l.TermId == termId).Select(l => l.LevelId).Distinct().ToListAsync(ct);
        var source = await db.FeeStructureLines.Where(l => l.TermId == fromTermId && !have.Contains(l.LevelId)).ToListAsync(ct);
        foreach (var l in source)
            db.FeeStructureLines.Add(new FeeStructureLine { TenantId = TenantId, TermId = termId, LevelId = l.LevelId, FeeItemId = l.FeeItemId, AmountMinor = l.AmountMinor });
        await db.SaveChangesAsync(ct);
        var all = await StructureLinesAsync(termId, null, ct);
        return new FeeStructureCopyDto(termId, source.Select(l => l.LevelId).Distinct().Count(), all.Select(x => x.Line).ToList());
    }

    private async Task<List<(Guid LevelId, FeeStructureLineDto Line)>> StructureLinesAsync(Guid termId, Guid? levelId, CancellationToken ct)
    {
        var rows = await (from l in db.FeeStructureLines
                          join i in db.FeeItems on l.FeeItemId equals i.Id
                          where l.TermId == termId && (levelId == null || l.LevelId == levelId)
                          orderby i.Name
                          select new { l.LevelId, Line = new FeeStructureLineDto(i.Id, i.Name, i.IsOptional, l.AmountMinor) }).ToListAsync(ct);
        return rows.Select(r => (r.LevelId, r.Line)).ToList();
    }

    private async Task<Term> TermAsync(Guid termId, CancellationToken ct) =>
        await db.Terms.FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");

    // ---------- Generate ----------

    public async Task<GenerateInvoicesResult> GenerateAsync(GenerateInvoicesRequest r, CancellationToken ct)
    {
        var term = await TermAsync(r.TermId, ct);
        var session = await db.AcademicSessions.FirstAsync(s => s.Id == term.SessionId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(GenerateLockKey(TenantId), ct);

        var enrolments = await (from e in db.Enrolments
                                join s in db.Students on e.StudentId equals s.Id
                                join c in db.SchoolClasses on e.ClassId equals c.Id
                                where e.SessionId == term.SessionId && s.Status == StudentStatus.Active
                                select new { Enrolment = e.Id, c.LevelId, StudentId = s.Id, Name = s.FirstName + " " + s.LastName }).ToListAsync(ct);
        var structure = (await db.FeeStructureLines.Where(l => l.TermId == term.Id).ToListAsync(ct)).ToLookup(l => l.LevelId);
        var items = await db.FeeItems.ToDictionaryAsync(i => i.Id, ct);
        var invoices = (await db.StudentInvoices.Where(i => i.TermId == term.Id).ToListAsync(ct)).ToDictionary(i => i.EnrolmentId);
        var invoiceIds = invoices.Values.Select(i => i.Id).ToList();
        var allLines = (await db.InvoiceLines.Where(l => invoiceIds.Contains(l.InvoiceId)).OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToListAsync(ct)).ToLookup(l => l.InvoiceId);
        var allInstalments = (await db.Instalments.Where(x => invoiceIds.Contains(x.InvoiceId)).ToListAsync(ct)).ToLookup(x => x.InvoiceId);

        int created = 0, refreshed = 0, skipped = 0;
        var review = new List<Guid>();
        var withPayments = new List<Guid>();
        var named = new List<GeneratedInvoiceRefDto>();
        var dueDefault = term.StartDate.AddDays(DefaultDueDays);
        var touched = new List<StudentInvoice>();
        var issued = new List<(Guid StudentId, string Name, string Number)>();
        foreach (var e in enrolments)
        {
            var levelLines = structure[e.LevelId].ToList();
            if (invoices.TryGetValue(e.Enrolment, out var invoice))
            {
                if (invoice.Status == InvoiceStatus.Cancelled) continue;
                var lines = allLines[invoice.Id].ToList();
                if (invoice.Paid > 0)
                {
                    // Left alone: it is only mentioned when the structure would now give it different lines.
                    var (wouldBe, wouldDrop) = RefreshLines(invoice.Id, levelLines, items, lines);
                    if (wouldDrop || levelLines.Count == 0 || !SameLines(wouldBe, lines))
                    {
                        withPayments.Add(invoice.Id);
                        named.Add(new GeneratedInvoiceRefDto(invoice.Id, invoice.Number, e.Name));
                    }
                    continue;
                }
                var (kept, dropped) = RefreshLines(invoice.Id, levelLines, items, lines);
                foreach (var l in lines) db.InvoiceLines.Remove(l);
                foreach (var l in kept) db.InvoiceLines.Add(l);
                var total = Math.Max(0, kept.Sum(l => l.AmountMinor));
                var instalments = allInstalments[invoice.Id].ToList();
                var needsReview = dropped || levelLines.Count == 0;
                if (instalments.Count > 0 && instalments.Sum(x => x.AmountMinor) != total)
                {
                    foreach (var x in instalments) db.Instalments.Remove(x);
                    needsReview = true;
                }
                if (needsReview)
                {
                    // Persisted, so the invoice page shows the warning and Mark reviewed clears it.
                    invoice.NeedsReview = true;
                    review.Add(invoice.Id);
                    named.Add(new GeneratedInvoiceRefDto(invoice.Id, invoice.Number, e.Name));
                }
                invoice.Total = total;
                invoice.Balance = Math.Max(0, total - invoice.Paid);
                refreshed++;
                touched.Add(invoice);
                continue;
            }
            if (levelLines.Count == 0) { skipped++; continue; }
            var number = await db.NextDocumentNumberAsync(DocumentCounter.SchoolInvoice, ct);
            var created1 = new StudentInvoice
            {
                TenantId = TenantId, EnrolmentId = e.Enrolment, TermId = term.Id, DueDate = dueDefault,
                Number = $"INV-{session.StartDate.Year}-{number:D5}",
            };
            var (newLines, _) = RefreshLines(created1.Id, levelLines, items, []);
            db.StudentInvoices.Add(created1);
            foreach (var l in newLines) db.InvoiceLines.Add(l);
            created1.Total = Math.Max(0, newLines.Sum(l => l.AmountMinor));
            created1.Balance = created1.Total;
            created++;
            issued.Add((e.StudentId, e.Name, created1.Number));
        }
        foreach (var (studentId, name, number) in issued)
            feed.Notify(await feed.GuardianUsersAsync(studentId, ct), "school.invoice_issued", $"New {term.Name} invoice for {name}",
                $"Invoice {number} is ready to view and pay.", SchoolFeed.ParentLink("fees", studentId));
        await db.SaveChangesAsync(ct);
        foreach (var inv in touched) await RecalculateAsync(inv.Id, ct);
        await tx.CommitAsync(ct);
        return new GenerateInvoicesResult(created, refreshed, skipped, review, withPayments, named);
    }

    /// <summary>True when two sets of invoice lines have the same fee items, discounts and amounts (order aside).</summary>
    private static bool SameLines(List<InvoiceLine> a, List<InvoiceLine> b)
    {
        static IEnumerable<string> Key(List<InvoiceLine> lines) =>
            lines.Select(l => $"{l.FeeItemId}|{l.Description}|{l.AmountMinor}").Order(StringComparer.Ordinal);
        return a.Count == b.Count && Key(a).SequenceEqual(Key(b));
    }

    /// <summary>
    /// The lines an invoice should have: every required item of the structure, the optional items already chosen (still in the structure),
    /// and the existing discounts while the total stays at zero or more. Percent discounts are worked out again from the current tuition
    /// lines (<see cref="PercentOf"/>); amount discounts keep their value. <c>Dropped</c> is true when a discount no longer fits and was left out.
    /// </summary>
    private (List<InvoiceLine> Lines, bool Dropped) RefreshLines(Guid invoiceId, List<FeeStructureLine> structure, Dictionary<Guid, FeeItem> items, List<InvoiceLine> current)
    {
        var chosen = current.Where(l => l.FeeItemId is not null).Select(l => l.FeeItemId!.Value).ToHashSet();
        var result = new List<InvoiceLine>();
        foreach (var s in structure.OrderBy(s => items[s.FeeItemId].Name))
        {
            var item = items[s.FeeItemId];
            if (item.IsOptional && !chosen.Contains(item.Id)) continue;
            result.Add(NewLine(invoiceId, item.Id, item.Name, s.AmountMinor));
        }
        var running = result.Sum(l => l.AmountMinor);
        var tuition = result.Where(l => l.FeeItemId is { } f && IsTuition(items[f])).Sum(l => l.AmountMinor);
        var dropped = false;
        foreach (var d in current.Where(l => l.FeeItemId is null))
        {
            var isPercent = d.DiscountKind == DiscountKind.Percent && d.DiscountValue is not null;
            var amount = isPercent ? PercentOf(tuition, d.DiscountValue!.Value) : -d.AmountMinor;
            if (amount <= 0 || running - amount < 0) { dropped = true; continue; }
            running -= amount;
            result.Add(NewLine(invoiceId, null, d.Description, -amount, d.DiscountKind, d.DiscountValue));
        }
        return (result, dropped);
    }

    /// <summary>A percentage of an amount in kobo, rounded half away from zero to the whole kobo.</summary>
    private static long PercentOf(long amountMinor, decimal percent) =>
        (long)Math.Round(amountMinor * percent / 100m, MidpointRounding.AwayFromZero);

    private static bool IsTuition(FeeItem item) => item.Name.Contains("tuition", StringComparison.OrdinalIgnoreCase);

    private InvoiceLine NewLine(Guid invoiceId, Guid? feeItemId, string description, long amount, DiscountKind? kind = null, decimal? value = null) =>
        new() { TenantId = TenantId, InvoiceId = invoiceId, FeeItemId = feeItemId, Description = description, AmountMinor = amount, DiscountKind = kind, DiscountValue = value };

    /// <summary>Runs an edit of one invoice under the same per-school lock as generate, so edits and refreshes never interleave.</summary>
    private async Task<InvoiceDto> LockedAsync(Func<Task<InvoiceDto>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(GenerateLockKey(TenantId), ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    internal static long GenerateLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x46454547;

    // ---------- One invoice ----------

    public Task<InvoiceDto> OptionalItemsAsync(Guid invoiceId, OptionalItemsRequest r, CancellationToken ct) =>
        LockedAsync(() => OptionalItemsCoreAsync(invoiceId, r, ct), ct);

    public Task<InvoiceDto> DiscountAsync(Guid invoiceId, DiscountRequest r, CancellationToken ct) =>
        LockedAsync(() => DiscountCoreAsync(invoiceId, r, ct), ct);

    public Task<InvoiceDto> InstalmentsAsync(Guid invoiceId, IReadOnlyList<InstalmentInput>? input, CancellationToken ct) =>
        LockedAsync(() => InstalmentsCoreAsync(invoiceId, input, ct), ct);

    private async Task<InvoiceDto> OptionalItemsCoreAsync(Guid invoiceId, OptionalItemsRequest r, CancellationToken ct)
    {
        var invoice = await EditableAsync(invoiceId, ct);
        var wanted = (r.FeeItemIds ?? throw BadRequestException.Field("feeItemIds", "Send the optional items to include.")).Distinct().ToList();
        var level = await LevelOfAsync(invoice.EnrolmentId, ct);
        var structure = await db.FeeStructureLines.Where(l => l.TermId == invoice.TermId && l.LevelId == level).ToListAsync(ct);
        var items = await db.FeeItems.ToDictionaryAsync(i => i.Id, ct);
        foreach (var id in wanted)
            if (!items.TryGetValue(id, out var item) || !item.IsOptional || structure.All(s => s.FeeItemId != id))
                throw BadRequestException.Field("feeItemIds", "Choose optional items that are in this level's fee structure for the term.");

        var lines = await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).ToListAsync(ct);
        var optionalOn = lines.Where(l => l.FeeItemId is { } f && items.TryGetValue(f, out var i) && i.IsOptional).ToList();
        var removed = optionalOn.Where(l => !wanted.Contains(l.FeeItemId!.Value)).ToList();
        var added = wanted.Where(id => optionalOn.All(l => l.FeeItemId != id)).Select(id => NewLine(invoiceId, id, items[id].Name, structure.First(s => s.FeeItemId == id).AmountMinor)).ToList();
        var total = lines.Except(removed).Concat(added).Sum(l => l.AmountMinor);
        EnsureTotalFits(total, invoice, "feeItemIds");
        foreach (var l in removed) db.InvoiceLines.Remove(l);
        foreach (var l in added) db.InvoiceLines.Add(l);
        await db.SaveChangesAsync(ct);
        await DropStaleInstalmentsAsync(invoiceId, ct);
        await RecalculateAsync(invoiceId, ct);
        return await GetAsync(invoiceId, ct);
    }

    private async Task<InvoiceDto> DiscountCoreAsync(Guid invoiceId, DiscountRequest r, CancellationToken ct)
    {
        var invoice = await EditableAsync(invoiceId, ct);
        var reason = r.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) throw BadRequestException.Field("reason", "Say why the discount is given.");
        if (reason.Length > 150) throw BadRequestException.Field("reason", "Use 150 characters or fewer for the reason.");
        var lines = await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).ToListAsync(ct);
        var total = lines.Sum(l => l.AmountMinor);
        long amount;
        string description;
        DiscountKind kind;
        if (string.Equals(r.Kind, "Percent", StringComparison.OrdinalIgnoreCase))
        {
            kind = DiscountKind.Percent;
            if (r.Value <= 0 || r.Value > 100) throw BadRequestException.Field("value", "Enter a percentage above 0 and up to 100.");
            var tuition = await TuitionTotalAsync(lines, ct);
            if (tuition is null) throw BadRequestException.Field("value", "Add a tuition fee before a percentage discount.");
            // All percentage discounts on an invoice together may not pass 100% of the tuition.
            var already = lines.Where(l => l.DiscountKind == DiscountKind.Percent).Sum(l => l.DiscountValue ?? 0);
            if (already + r.Value > 100) throw BadRequestException.Field("value", "Discounts can't be more than the tuition.");
            amount = PercentOf(tuition.Value, r.Value);
            description = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Discount ({r.Value:0.##}% of tuition): {reason}");
        }
        else if (string.Equals(r.Kind, "Amount", StringComparison.OrdinalIgnoreCase))
        {
            kind = DiscountKind.Amount;
            if (r.Value <= 0 || r.Value != decimal.Truncate(r.Value)) throw BadRequestException.Field("value", "Enter the discount in kobo as a whole number above 0.");
            amount = (long)r.Value;
            description = $"Discount: {reason}";
        }
        else throw BadRequestException.Field("kind", "Choose Percent or Amount.");
        if (amount <= 0) throw BadRequestException.Field("value", "That discount works out to nothing. Enter a larger value.");
        EnsureTotalFits(total - amount, invoice, "value");
        db.InvoiceLines.Add(NewLine(invoiceId, null, description, -amount, kind, r.Value));
        await db.SaveChangesAsync(ct);
        await DropStaleInstalmentsAsync(invoiceId, ct);
        await RecalculateAsync(invoiceId, ct);
        return await GetAsync(invoiceId, ct);
    }

    /// <summary>The sum of the lines whose fee item name contains "tuition", or null when there are none.</summary>
    private async Task<long?> TuitionTotalAsync(List<InvoiceLine> lines, CancellationToken ct)
    {
        var ids = lines.Where(l => l.FeeItemId is not null).Select(l => l.FeeItemId!.Value).ToList();
        var tuitionIds = (await db.FeeItems.Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync(ct))
            .Where(i => i.Name.Contains("tuition", StringComparison.OrdinalIgnoreCase)).Select(i => i.Id).ToHashSet();
        var tuition = lines.Where(l => l.FeeItemId is { } f && tuitionIds.Contains(f)).ToList();
        return tuition.Count == 0 ? null : tuition.Sum(l => l.AmountMinor);
    }

    private static void EnsureTotalFits(long newTotal, StudentInvoice invoice, string field)
    {
        if (newTotal < 0) throw BadRequestException.Field(field, "That would take the invoice below zero. Use a smaller amount.");
        if (newTotal < invoice.Paid) throw BadRequestException.Field(field, "That would take the invoice below what has been paid. Reverse a payment first.");
    }

    private async Task<InvoiceDto> InstalmentsCoreAsync(Guid invoiceId, IReadOnlyList<InstalmentInput>? input, CancellationToken ct)
    {
        var invoice = await EditableAsync(invoiceId, ct);
        var list = input ?? [];
        if (list.Count > MaxInstalments) throw BadRequestException.Field("instalments", $"Use at most {MaxInstalments} instalments.");
        if (list.Count > 0)
        {
            var term = await TermAsync(invoice.TermId, ct);
            var session = await db.AcademicSessions.FirstAsync(s => s.Id == term.SessionId, ct);
            if (list.Any(x => x.AmountMinor <= 0)) throw BadRequestException.Field("instalments", "Each instalment must be more than zero.");
            if (list.Any(x => x.DueDate < session.StartDate || x.DueDate > session.EndDate))
                throw BadRequestException.Field("instalments", $"Each due date must fall within the session ({session.Name}).");
            if (list.Sum(x => x.AmountMinor) != invoice.Total)
                throw BadRequestException.Field("instalments", "The instalments must add up to the invoice total.");
        }
        var existing = await db.Instalments.Where(x => x.InvoiceId == invoiceId).ToListAsync(ct);
        foreach (var x in existing) db.Instalments.Remove(x);
        foreach (var x in list.OrderBy(x => x.DueDate))
            db.Instalments.Add(new Instalment { TenantId = TenantId, InvoiceId = invoiceId, DueDate = x.DueDate, AmountMinor = x.AmountMinor });
        await db.SaveChangesAsync(ct);
        return await GetAsync(invoiceId, ct);
    }

    /// <summary>Instalments that no longer add up to the invoice are cleared, so they never disagree with the total.</summary>
    private async Task DropStaleInstalmentsAsync(Guid invoiceId, CancellationToken ct)
    {
        var total = await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).SumAsync(l => (long?)l.AmountMinor, ct) ?? 0;
        var instalments = await db.Instalments.Where(x => x.InvoiceId == invoiceId).ToListAsync(ct);
        if (instalments.Count == 0 || instalments.Sum(x => x.AmountMinor) == Math.Max(0, total)) return;
        db.Instalments.RemoveRange(instalments);
        await db.SaveChangesAsync(ct);
    }

    private async Task<StudentInvoice> EditableAsync(Guid id, CancellationToken ct)
    {
        var invoice = await db.StudentInvoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice");
        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new ConflictException("This invoice is cancelled, so it can't change.", "invoice_cancelled");
        return invoice;
    }

    private async Task<Guid> LevelOfAsync(Guid enrolmentId, CancellationToken ct) =>
        await (from e in db.Enrolments join c in db.SchoolClasses on e.ClassId equals c.Id where e.Id == enrolmentId select c.LevelId).FirstAsync(ct);

    // ---------- Totals ----------

    /// <summary>
    /// Recomputes Total (sum of the lines, discounts negative, never below zero), Paid, Balance (never negative) and Status.
    /// A cancelled invoice stays cancelled. Paid is the sum of the received, non-reversed payments (<see cref="PaidAsync"/>).
    /// </summary>
    public async Task RecalculateAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await db.StudentInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException("Invoice");
        var sum = await db.InvoiceLines.Where(l => l.InvoiceId == invoiceId).SumAsync(l => (long?)l.AmountMinor, ct) ?? 0;
        invoice.Total = Math.Max(0, sum);
        invoice.Paid = await PaidAsync(invoice, ct);
        // A cancelled invoice is owed nothing; a live one with nothing to pay (a 100% discount, say) is Paid, not Unpaid.
        invoice.Balance = invoice.Status == InvoiceStatus.Cancelled ? 0 : Math.Max(0, invoice.Total - invoice.Paid);
        if (invoice.Status != InvoiceStatus.Cancelled)
            invoice.Status = invoice.Paid >= invoice.Total ? InvoiceStatus.Paid
                : invoice.Paid > 0 ? InvoiceStatus.PartlyPaid : InvoiceStatus.Unpaid;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The sum of the invoice's received payments that haven't been reversed. Pending and mismatched online charges don't count.</summary>
    private async Task<long> PaidAsync(StudentInvoice invoice, CancellationToken ct) =>
        await db.FeePayments.Where(p => p.InvoiceId == invoice.Id && p.Status == FeePaymentStatus.Succeeded && p.ReversedAt == null)
            .SumAsync(p => (long?)p.AmountMinor, ct) ?? 0;

    // ---------- Reads ----------

    public async Task<IReadOnlyList<InvoiceSummaryDto>> ListAsync(Guid? termId, Guid? classId, string? status, int? skip, int? take, CancellationToken ct)
    {
        InvoiceStatus? wanted = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<InvoiceStatus>(status, true, out var parsed)) throw BadRequestException.Field("status", "Choose Unpaid, PartlyPaid, Paid or Cancelled.");
            wanted = parsed;
        }
        var rows = await Query(i => (termId == null || i.TermId == termId) && (wanted == null || i.Status == wanted), classId)
            .OrderBy(x => x.ClassName).ThenBy(x => x.StudentName).ThenBy(x => x.Number)
            .Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 500, 1, 2000)).ToListAsync(ct);
        return rows.Select(x => new InvoiceSummaryDto(x.Id, x.Number, x.EnrolmentId, x.StudentId, x.StudentName, x.AdmissionNo, x.ClassId, x.ClassName,
            x.TermId, x.Total, x.Paid, x.Balance, x.Status.ToString(), x.DueDate)).ToList();
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct)
    {
        var x = await Query(i => i.Id == id, null).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Invoice");
        var lines = await db.InvoiceLines.Where(l => l.InvoiceId == id).OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Select(l => new InvoiceLineDto(l.Id, l.FeeItemId, l.Description, l.AmountMinor)).ToListAsync(ct);
        var instalments = await db.Instalments.Where(i => i.InvoiceId == id).OrderBy(i => i.DueDate)
            .Select(i => new InstalmentDto(i.Id, i.DueDate, i.AmountMinor)).ToListAsync(ct);
        // Started-but-unfinished online checkouts (Pending) are not payments yet; received and mismatched ones are shown.
        var payments = (await db.FeePayments.AsNoTracking().Where(p => p.InvoiceId == id && p.Status != FeePaymentStatus.Pending)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.CreatedAt).ToListAsync(ct)).Select(FeePaymentService.ToDto).ToList();
        var needsReview = await db.StudentInvoices.Where(i => i.Id == id).Select(i => i.NeedsReview).FirstAsync(ct);
        return new InvoiceDto(x.Id, x.Number, x.EnrolmentId, x.StudentId, x.StudentName, x.AdmissionNo, x.ClassId, x.ClassName,
            x.TermId, x.Total, x.Paid, x.Balance, x.Status.ToString(), x.DueDate, lines, instalments, needsReview, payments);
    }

    private sealed class Row
    {
        public Guid Id { get; init; }
        public string Number { get; init; } = "";
        public Guid EnrolmentId { get; init; }
        public Guid StudentId { get; init; }
        public string StudentName { get; init; } = "";
        public string AdmissionNo { get; init; } = "";
        public Guid ClassId { get; init; }
        public string ClassName { get; init; } = "";
        public Guid TermId { get; init; }
        public long Total { get; init; }
        public long Paid { get; init; }
        public long Balance { get; init; }
        public InvoiceStatus Status { get; init; }
        public DateOnly DueDate { get; init; }
    }

    private IQueryable<Row> Query(System.Linq.Expressions.Expression<Func<StudentInvoice, bool>> filter, Guid? classId) =>
        from i in db.StudentInvoices.Where(filter)
        join e in db.Enrolments on i.EnrolmentId equals e.Id
        join s in db.Students on e.StudentId equals s.Id
        join c in db.SchoolClasses on e.ClassId equals c.Id
        where classId == null || c.Id == classId
        select new Row
        {
            Id = i.Id, Number = i.Number, EnrolmentId = e.Id, StudentId = s.Id, StudentName = s.FirstName + " " + s.LastName,
            AdmissionNo = s.AdmissionNo, ClassId = c.Id, ClassName = c.Name, TermId = i.TermId, Total = i.Total, Paid = i.Paid,
            Balance = i.Balance, Status = i.Status, DueDate = i.DueDate,
        };
}
