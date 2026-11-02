using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record FinanceSettingsDto(bool ResultsHoldForUnpaidFees, int ReminderDaysBefore, int ReminderEveryDays);
/// <summary>Leave a field out (null) to keep it.</summary>
public sealed record SaveFinanceSettingsRequest(bool? ResultsHoldForUnpaidFees, int? ReminderDaysBefore, int? ReminderEveryDays);
public sealed record CancelInvoiceRequest(string? Reason);
public sealed record DebtorDto(
    Guid InvoiceId, string InvoiceNumber, Guid StudentId, string StudentName, string AdmissionNo, Guid ClassId, string ClassName,
    long Total, long Paid, long Balance, int DaysOverdue, string? PrimaryGuardian, string? GuardianPhone);
public sealed record DebtorWhatsAppDto(string StudentName, string GuardianName, long Balance, string WhatsAppUrl);
public sealed record DebtorsDto(Guid TermId, int Count, long TotalBalance, IReadOnlyList<DebtorDto> Debtors);
public sealed record FinanceSummaryDto(Guid TermId, long Expected, long Collected, long Outstanding, long Expenses);
public sealed record CollectionRowDto(string Key, string Label, long AmountMinor);
public sealed record CollectionsDto(Guid TermId, string By, long Total, IReadOnlyList<CollectionRowDto> Rows);

/// <summary>
/// Debtors, fee reminders, the results hold, invoice cancel and review, and the finance reports. Owner, Admin and the bursar use it
/// (the routes are on the fee policy); <see cref="IsHeldAsync"/> is read by the parent and student portals. Money is in kobo.
/// </summary>
public sealed class FinanceService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    FeeService fees,
    NotificationQueue notifications,
    SchoolFeed feed,
    AppUrls urls,
    IDbErrors dbErrors,
    TimeProvider clock)
{
    public const int MaxReminderDaysBefore = 30;
    public const int MaxReminderEveryDays = 30;
    public const string DebtorsCsvHeader = "admission number,student,class,invoice,total,paid,balance,days overdue,primary guardian,guardian phone";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();
    private DateOnly Today => SchoolCalendar.Today(clock.GetUtcNow());

    // ---------- Settings and the results hold ----------

    public async Task<FinanceSettingsDto> GetSettingsAsync(CancellationToken ct) =>
        Settings(await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School"));

    public async Task<FinanceSettingsDto> UpdateSettingsAsync(SaveFinanceSettingsRequest r, CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        if (r.ReminderDaysBefore is { } before && before is < 0 or > MaxReminderDaysBefore)
            throw BadRequestException.Field("reminderDaysBefore", $"Enter a number of days from 0 to {MaxReminderDaysBefore}.");
        if (r.ReminderEveryDays is { } every && every is < 1 or > MaxReminderEveryDays)
            throw BadRequestException.Field("reminderEveryDays", $"Enter a number of days from 1 to {MaxReminderEveryDays}.");
        var was = Settings(profile);
        if (r.ResultsHoldForUnpaidFees is { } hold) profile.ResultsHoldForUnpaidFees = hold;
        if (r.ReminderDaysBefore is { } b) profile.ReminderDaysBefore = b;
        if (r.ReminderEveryDays is { } e) profile.ReminderEveryDays = e;
        var now = Settings(profile);
        // The school profile is not audited (it holds the encrypted Paystack key), so this change writes its own entry.
        db.AuditLogs.Add(Audit("school.finance_settings", nameof(SchoolProfile), profile.Id.ToString(), was, now));
        await db.SaveChangesAsync(ct);
        return now;
    }

    private static FinanceSettingsDto Settings(SchoolProfile p) => new(p.ResultsHoldForUnpaidFees, p.ReminderDaysBefore, p.ReminderEveryDays);

    /// <summary>
    /// True when the school holds results for unpaid fees (the setting is on) and the student's invoice for the term, if it has a live one
    /// (not cancelled), still has a balance. The parent and student portals show "Results are held. Please see the school office." then.
    /// </summary>
    public async Task<bool> IsHeldAsync(Guid studentId, Guid termId, CancellationToken ct)
    {
        if (!await db.SchoolProfiles.AsNoTracking().AnyAsync(p => p.ResultsHoldForUnpaidFees, ct)) return false;
        return await (from i in db.StudentInvoices.AsNoTracking()
                      join e in db.Enrolments on i.EnrolmentId equals e.Id
                      where e.StudentId == studentId && i.TermId == termId && i.Status != InvoiceStatus.Cancelled && i.Balance > 0
                      select i.Id).AnyAsync(ct);
    }

    // ---------- Cancel and review an invoice ----------

    /// <summary>
    /// Cancels an invoice (it leaves debtors, reminders and the finance totals). Refused while it has received, non-reversed payments: reverse
    /// them first. Runs under the per-school fee lock, so it can't interleave with a payment or a refresh.
    /// </summary>
    public async Task<InvoiceDto> CancelInvoiceAsync(Guid invoiceId, CancelInvoiceRequest r, CancellationToken ct)
    {
        var reason = r.Reason?.Trim() ?? "";
        if (reason.Length is < 5 or > 300) throw BadRequestException.Field("reason", "Say why in 5 to 300 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(FeeService.GenerateLockKey(TenantId), ct);
        var invoice = await db.StudentInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException("Invoice");
        if (invoice.Status == InvoiceStatus.Cancelled) throw new ConflictException("This invoice is already cancelled.", "invoice_cancelled");
        if (await db.FeePayments.AnyAsync(p => p.InvoiceId == invoiceId && p.Status == FeePaymentStatus.Succeeded && p.ReversedAt == null, ct))
            throw new ConflictException("This invoice has payments. Reverse them before cancelling.", "invoice_has_payments");
        var was = invoice.Status;
        invoice.Status = InvoiceStatus.Cancelled;
        // Nothing is owed on a cancelled invoice, so every screen that shows its balance shows zero.
        var balanceWas = invoice.Balance;
        invoice.Balance = 0;
        db.AuditLogs.Add(Audit("invoice.cancelled", nameof(StudentInvoice), invoiceId.ToString(), new { status = was.ToString(), balance = balanceWas }, new { status = "Cancelled", reason }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await fees.GetAsync(invoiceId, ct);
    }

    /// <summary>The bursar has looked at an invoice flagged for review (a payment came in for more than the balance): the flag is cleared.</summary>
    public async Task<InvoiceDto> MarkReviewedAsync(Guid invoiceId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(FeeService.GenerateLockKey(TenantId), ct);
        var invoice = await db.StudentInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException("Invoice");
        if (invoice.NeedsReview)
        {
            invoice.NeedsReview = false;
            db.AuditLogs.Add(Audit("invoice.reviewed", nameof(StudentInvoice), invoiceId.ToString(), new { needsReview = true }, new { needsReview = false }));
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await fees.GetAsync(invoiceId, ct);
    }

    // ---------- Debtors ----------

    private async Task<Term> TermAsync(Guid? termId, CancellationToken ct)
    {
        var query = db.Terms.AsNoTracking();
        return (termId is { } id ? await query.FirstOrDefaultAsync(t => t.Id == id, ct) : await query.FirstOrDefaultAsync(t => t.IsCurrent, ct))
            ?? throw new NotFoundException("Term");
    }

    /// <summary>
    /// Invoices of the term with a balance, not cancelled, worst first (most days overdue, then largest balance). Days overdue run from the
    /// earliest instalment (or the invoice's due date) that is past due and not covered by payments, which are applied in due-date order.
    /// <paramref name="minDaysOverdue"/> 0 keeps everyone with a balance.
    /// </summary>
    public async Task<DebtorsDto> DebtorsAsync(Guid? termId, Guid? classId, int? minDaysOverdue, CancellationToken ct)
    {
        var term = await TermAsync(termId, ct);
        var min = Math.Max(0, minDaysOverdue ?? 0);
        var today = Today;
        var rows = await (from i in db.StudentInvoices.AsNoTracking()
                          join e in db.Enrolments on i.EnrolmentId equals e.Id
                          join s in db.Students on e.StudentId equals s.Id
                          join c in db.SchoolClasses on e.ClassId equals c.Id
                          where i.TermId == term.Id && i.Status != InvoiceStatus.Cancelled && i.Balance > 0 && (classId == null || c.Id == classId)
                          select new
                          {
                              InvoiceId = i.Id, i.Number, StudentId = s.Id, s.FirstName, s.MiddleName, s.LastName, s.AdmissionNo,
                              ClassId = c.Id, ClassName = c.Name, i.Total, i.Paid, i.Balance, i.DueDate,
                          }).ToListAsync(ct);
        var invoiceIds = rows.Select(r => r.InvoiceId).ToList();
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var plans = (await db.Instalments.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId)).ToListAsync(ct)).ToLookup(x => x.InvoiceId);
        var guardians = await PrimaryGuardiansAsync(studentIds, requireEmail: false, ct);

        var debtors = new List<DebtorDto>();
        foreach (var r in rows)
        {
            var owing = FinanceRules.Outstanding(plans[r.InvoiceId].Select(x => (x.DueDate, x.AmountMinor)), r.Paid, r.Balance, r.DueDate);
            var days = FinanceRules.DaysOverdue(today, owing);
            if (days < min) continue;
            guardians.TryGetValue(r.StudentId, out var g);
            debtors.Add(new DebtorDto(r.InvoiceId, r.Number, r.StudentId, ParentAccess.FullName(r.FirstName, r.MiddleName, r.LastName), r.AdmissionNo,
                r.ClassId, r.ClassName, r.Total, r.Paid, r.Balance, days, g?.FullName, g?.Phone));
        }
        var ordered = debtors.OrderByDescending(d => d.DaysOverdue).ThenByDescending(d => d.Balance).ThenBy(d => d.ClassName).ThenBy(d => d.StudentName).ToList();
        return new DebtorsDto(term.Id, ordered.Count, ordered.Sum(d => d.Balance), ordered);
    }

    /// <summary>
    /// The debtors list as CSV (UTF-8). Every text cell goes through the shared <see cref="Saas.Application.Platform.Csv.Cell"/> (quoted, and a cell that
    /// starts with = + - @ is made safe, so a name or phone typed by a family can't run as a spreadsheet formula); amounts are naira with 2 decimals.
    /// </summary>
    public async Task<byte[]> DebtorsCsvAsync(Guid? termId, Guid? classId, int? minDaysOverdue, CancellationToken ct)
    {
        var list = await DebtorsAsync(termId, classId, minDaysOverdue, ct);
        var cell = Saas.Application.Platform.Csv.Cell;
        var sb = new StringBuilder(DebtorsCsvHeader).Append("\r\n");
        foreach (var d in list.Debtors)
            sb.Append(string.Join(',', cell(d.AdmissionNo), cell(d.StudentName), cell(d.ClassName), cell(d.InvoiceNumber), Naira(d.Total), Naira(d.Paid), Naira(d.Balance),
                d.DaysOverdue.ToString(CultureInfo.InvariantCulture), cell(d.PrimaryGuardian), cell(CsvPhone(d.GuardianPhone)))).Append("\r\n");
        // UTF-8 with a byte order mark and CRLF line ends, so Excel shows names with diacritics correctly.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>
    /// The bursar's WhatsApp list: each debtor whose primary guardian has a phone, with a wa.me link and the reminder text ready to send.
    /// Same filters and order as <see cref="DebtorsAsync"/>; <c>Balance</c> is in kobo like everywhere else.
    /// </summary>
    public async Task<IReadOnlyList<DebtorWhatsAppDto>> DebtorsWhatsAppAsync(Guid? termId, int? minDaysOverdue, CancellationToken ct)
    {
        var list = await DebtorsAsync(termId, null, minDaysOverdue, ct);
        var school = await db.SchoolProfiles.AsNoTracking().Select(p => p.Name).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var currency = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.Currency).FirstAsync(ct);
        var rows = new List<DebtorWhatsAppDto>();
        foreach (var d in list.Debtors)
        {
            if (d.PrimaryGuardian is null) continue;
            var url = GuardianInviteService.WhatsAppUrl(d.GuardianPhone,
                $"Hello {d.PrimaryGuardian}, this is a reminder from {school} that {d.StudentName}'s school fees balance of {Money.Format(d.Balance, currency)} is outstanding. Thank you.");
            if (url is not null) rows.Add(new DebtorWhatsAppDto(d.StudentName, d.PrimaryGuardian, d.Balance, url));
        }
        return rows;
    }

    /// <summary>
    /// A stored phone (+234XXXXXXXXXX) as people write it: 0803 123 4567. Any other number is its digits in groups of three (the last group
    /// takes a leftover digit), so the cell never starts with + or = and a spreadsheet keeps it as text.
    /// </summary>
    internal static string? CsvPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0) return null;
        if (digits.Length == 13 && digits.StartsWith("234", StringComparison.Ordinal))
            return $"0{digits[3..6]} {digits[6..9]} {digits[9..]}";
        var groups = new List<string>();
        var i = 0;
        while (digits.Length - i > 4) { groups.Add(digits.Substring(i, 3)); i += 3; }
        groups.Add(digits[i..]);
        return string.Join(' ', groups);
    }

    private static string Naira(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record GuardianContact(string FullName, string? Phone, string? Email);

    /// <summary>Each student's primary guardian (or the earliest added when none is marked). With <paramref name="requireEmail"/>, only guardians with an email count, primary first.</summary>
    private async Task<Dictionary<Guid, GuardianContact>> PrimaryGuardiansAsync(List<Guid> studentIds, bool requireEmail, CancellationToken ct)
    {
        var links = await (from l in db.StudentGuardians.AsNoTracking()
                           join g in db.Guardians.AsNoTracking() on l.GuardianId equals g.Id
                           where studentIds.Contains(l.StudentId) && (!requireEmail || g.Email != null)
                           select new { l.StudentId, l.IsPrimary, l.CreatedAt, g.FullName, g.Phone, g.Email }).ToListAsync(ct);
        return links.GroupBy(l => l.StudentId).ToDictionary(g => g.Key,
            g => g.OrderByDescending(l => l.IsPrimary).ThenBy(l => l.CreatedAt).Select(l => new GuardianContact(l.FullName, l.Phone, l.Email)).First());
    }

    // ---------- Reminders ----------

    /// <summary>
    /// Queues today's fee reminder emails for the current school and records them (<see cref="FeeReminder"/>). The daily job runs this per school;
    /// <see cref="ReminderRules.Due"/> decides per invoice using the school's own days-before and every-days settings. One email per invoice, to the
    /// primary guardian with an email (another guardian with an email when the primary has none). An invoice nobody can be emailed about is left
    /// alone and tried again on the next run. Cancelled and paid invoices and students who have left are skipped. Returns how many were queued.
    /// </summary>
    public async Task<int> SendDueRemindersAsync(DateOnly today, CancellationToken ct)
    {
        var profile = await db.SchoolProfiles.AsNoTracking().FirstOrDefaultAsync(ct);
        if (profile is null) return 0;
        var candidates = await (from i in db.StudentInvoices.AsNoTracking()
                                join e in db.Enrolments on i.EnrolmentId equals e.Id
                                join s in db.Students on e.StudentId equals s.Id
                                where i.Status != InvoiceStatus.Cancelled && i.Balance > 0 && s.Status == StudentStatus.Active
                                select new { InvoiceId = i.Id, i.Number, i.Balance, i.Paid, i.DueDate, StudentId = s.Id, s.FirstName, s.MiddleName, s.LastName }).ToListAsync(ct);
        if (candidates.Count == 0) return 0;
        var ids = candidates.Select(c => c.InvoiceId).ToList();
        var plans = (await db.Instalments.AsNoTracking().Where(x => ids.Contains(x.InvoiceId)).ToListAsync(ct)).ToLookup(x => x.InvoiceId);
        var history = (await db.FeeReminders.AsNoTracking().Where(r => ids.Contains(r.InvoiceId)).Select(r => new { r.InvoiceId, r.SentOn }).ToListAsync(ct))
            .GroupBy(r => r.InvoiceId).ToDictionary(g => g.Key, g => (Count: g.Count(), Last: g.Max(r => r.SentOn)));
        var guardians = await PrimaryGuardiansAsync(candidates.Select(c => c.StudentId).Distinct().ToList(), requireEmail: true, ct);
        var currency = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.Currency).FirstAsync(ct);
        var link = $"{urls.Web(Products.School)}/parent";

        var sent = 0;
        foreach (var c in candidates)
        {
            var owing = FinanceRules.Outstanding(plans[c.InvoiceId].Select(x => (x.DueDate, x.AmountMinor)), c.Paid, c.Balance, c.DueDate);
            history.TryGetValue(c.InvoiceId, out var past);
            if (!ReminderRules.Due(today, owing, past.Count, past.Count == 0 ? null : past.Last, profile.ReminderDaysBefore, profile.ReminderEveryDays)) continue;
            if (!guardians.TryGetValue(c.StudentId, out var guardian) || guardian.Email is null) continue;

            // The email and the bell say the same thing: the next unpaid instalment (amount and due date) first, then the full balance.
            var next = owing.Where(x => x.OutstandingMinor > 0).OrderBy(x => x.DueDate).First();
            var overdue = next.DueDate < today;
            var dueAmount = Money.Format(next.OutstandingMinor, currency);
            var balance = Money.Format(c.Balance, currency);
            var dueDate = next.DueDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            var child = ParentAccess.FullName(c.FirstName, c.MiddleName, c.LastName);
            notifications.Email(guardian.Email, "school.fee_reminder", new()
            {
                ["school"] = profile.Name,
                ["guardian"] = guardian.FullName,
                ["child"] = child,
                ["invoice"] = c.Number,
                ["dueAmount"] = dueAmount,
                ["balance"] = balance,
                ["dueDate"] = dueDate,
                ["overdue"] = overdue ? "true" : "false",
                ["link"] = link,
            }, TenantId);
            feed.Notify(await feed.GuardianUsersAsync(c.StudentId, ct), "school.fee_reminder",
                overdue ? $"{child}'s school fees are overdue" : $"{child}'s school fees are due soon",
                FeeReminderText(dueAmount, dueDate, overdue, c.Number, balance),
                SchoolFeed.ParentLink("fees", c.StudentId));
            db.FeeReminders.Add(new FeeReminder
            {
                TenantId = TenantId, InvoiceId = c.InvoiceId, SentAt = clock.GetUtcNow(), SentOn = today,
                Kind = overdue ? FeeReminderKind.Overdue : FeeReminderKind.BeforeDue,
            });
            try
            {
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (Exception ex) when (dbErrors.IsUniqueViolation(ex))
            {
                db.ChangeTracker.Clear(); // another run already reminded this invoice today
            }
        }
        return sent;
    }

    /// <summary>
    /// The reminder's wording, shared with the email template (school.fee_reminder): the amount and date of the next unpaid instalment, then the
    /// invoice's full balance when it is more than that.
    /// </summary>
    public static string FeeReminderText(string dueAmount, string dueDate, bool overdue, string invoice, string balance) =>
        (overdue ? $"{dueAmount} was due on {dueDate} and is still unpaid." : $"{dueAmount} is due on {dueDate}.") +
        (balance == dueAmount ? $" Invoice {invoice}." : $" The full balance on invoice {invoice} is {balance}.");

    // ---------- Reports ----------

    /// <summary>
    /// Expected (the non-cancelled invoices' totals), collected (received, non-reversed payments on those invoices), outstanding (their balances)
    /// and expenses dated within the term.
    /// </summary>
    public async Task<FinanceSummaryDto> SummaryAsync(Guid? termId, CancellationToken ct)
    {
        var term = await TermAsync(termId, ct);
        var invoices = db.StudentInvoices.AsNoTracking().Where(i => i.TermId == term.Id && i.Status != InvoiceStatus.Cancelled);
        var expected = await invoices.SumAsync(i => (long?)i.Total, ct) ?? 0;
        var outstanding = await invoices.SumAsync(i => (long?)i.Balance, ct) ?? 0;
        var collected = await (from p in db.FeePayments.AsNoTracking()
                               join i in invoices on p.InvoiceId equals i.Id
                               where p.Status == FeePaymentStatus.Succeeded && p.ReversedAt == null
                               select (long?)p.AmountMinor).SumAsync(ct) ?? 0;
        var expenses = await db.SchoolExpenses.AsNoTracking().Where(x => x.Date >= term.StartDate && x.Date <= term.EndDate).SumAsync(x => (long?)x.AmountMinor, ct) ?? 0;
        return new FinanceSummaryDto(term.Id, expected, collected, outstanding, expenses);
    }

    /// <summary>
    /// What was collected in the term (received, non-reversed payments on non-cancelled invoices), grouped by the student's class, by fee item or by
    /// payment method. By item, each payment is shared across its invoice's fee lines pro rata by line amount (see <see cref="FinanceRules.AllocateByLine"/>):
    /// discount lines are not part of the base, and the rounding remainder goes to the largest line, so the items always add up to the payments.
    /// </summary>
    public async Task<CollectionsDto> CollectionsAsync(Guid? termId, string? by, CancellationToken ct)
    {
        var grouping = by?.Trim().ToLowerInvariant();
        if (grouping is not ("class" or "item" or "method")) throw BadRequestException.Field("by", "Choose class, item or method.");
        var term = await TermAsync(termId, ct);
        var payments = from p in db.FeePayments.AsNoTracking()
                       join i in db.StudentInvoices.AsNoTracking() on p.InvoiceId equals i.Id
                       where i.TermId == term.Id && i.Status != InvoiceStatus.Cancelled && p.Status == FeePaymentStatus.Succeeded && p.ReversedAt == null
                       select new { p.InvoiceId, p.AmountMinor, p.Method, i.EnrolmentId };

        List<CollectionRowDto> rows;
        if (grouping == "method")
        {
            rows = (await payments.GroupBy(p => p.Method).Select(g => new { g.Key, Sum = g.Sum(x => x.AmountMinor) }).ToListAsync(ct))
                .Select(g => new CollectionRowDto(g.Key.ToString(), FeePaymentService.MethodLabel(g.Key), g.Sum)).ToList();
        }
        else if (grouping == "class")
        {
            rows = (await (from p in payments
                           join e in db.Enrolments on p.EnrolmentId equals e.Id
                           join c in db.SchoolClasses on e.ClassId equals c.Id
                           group p by new { c.Id, c.Name } into g
                           select new { g.Key.Id, g.Key.Name, Sum = g.Sum(x => x.AmountMinor) }).ToListAsync(ct))
                .Select(g => new CollectionRowDto(g.Id.ToString(), g.Name, g.Sum)).ToList();
        }
        else
        {
            var paid = await payments.Select(p => new { p.InvoiceId, p.AmountMinor }).ToListAsync(ct);
            var invoiceIds = paid.Select(p => p.InvoiceId).Distinct().ToList();
            var lines = (await db.InvoiceLines.AsNoTracking().Where(l => invoiceIds.Contains(l.InvoiceId) && l.FeeItemId != null)
                .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id).ToListAsync(ct)).ToLookup(l => l.InvoiceId);
            var names = await db.FeeItems.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var byItem = new Dictionary<string, (string Label, long Sum)>();
            void Add(string key, string label, long amount) => byItem[key] = (label, byItem.GetValueOrDefault(key).Sum + amount);
            foreach (var p in paid)
            {
                // Lines of one fee item are grouped first, so two lines for the same item share one key.
                var itemLines = lines[p.InvoiceId].GroupBy(l => l.FeeItemId!.Value).Select(g => (Key: g.Key, Amount: g.Sum(l => l.AmountMinor))).ToList();
                var shares = FinanceRules.AllocateByLine(p.AmountMinor, itemLines);
                if (shares.Count == 0) { Add("unallocated", "Unallocated", p.AmountMinor); continue; }
                foreach (var (item, share) in shares) Add(item.ToString(), names.GetValueOrDefault(item, "Fee item"), share);
            }
            rows = byItem.Select(x => new CollectionRowDto(x.Key, x.Value.Label, x.Value.Sum)).ToList();
        }
        var ordered = rows.Where(r => r.AmountMinor != 0).OrderByDescending(r => r.AmountMinor).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return new CollectionsDto(term.Id, grouping, ordered.Sum(r => r.AmountMinor), ordered);
    }

    // ---------- Helpers ----------

    private AuditLog Audit(string action, string entityType, string entityId, object? before, object? after) => new()
    {
        TenantId = TenantId, ActorId = user.UserId, Action = action, EntityType = entityType, EntityId = entityId,
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = after is null ? null : JsonSerializer.Serialize(after),
        Ip = user.IpAddress, CreatedAt = clock.GetUtcNow(),
    };
}
