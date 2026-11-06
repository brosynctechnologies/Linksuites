using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Care;

namespace Saas.Application.Care;

public sealed record MethodTotalDto(PayMethod Method, int Count, long AmountMinor);
public sealed record TakingsDayDto(DateOnly Date, int Count, long TotalMinor, IReadOnlyList<MethodTotalDto> ByMethod);
public sealed record TakingsReportDto(DateOnly From, DateOnly To, int Count, long TotalMinor, IReadOnlyList<MethodTotalDto> ByMethod, IReadOnlyList<TakingsDayDto> Days);

public sealed record CareDebtorDto(
    Guid PatientId, string PatientNumber, string PatientName, string? Phone, int Bills, long BalanceMinor, DateTimeOffset OldestBillAt, string? WhatsAppUrl);
public sealed record CareDebtorsReportDto(int Count, long TotalMinor, IReadOnlyList<CareDebtorDto> Debtors);

public sealed record CategoryTotalDto(PriceCategory Category, int Lines, long AmountMinor);
public sealed record RevenueReportDto(
    DateOnly From, DateOnly To, IReadOnlyList<CategoryTotalDto> ByCategory, long ChargedMinor, long DiscountsMinor, long WaiversMinor, long NetMinor);

public sealed record DepositRowDto(
    Guid AdmissionId, Guid PatientId, string PatientNumber, string PatientName, string? WardName, string? BedLabel, DateTimeOffset AdmittedAt,
    long DepositMinor, long ChargesMinor, long PaidMinor, long BalanceMinor, long CreditMinor);
/// <summary>What an unsigned draft is: <c>consultation</c>, <c>ward-round</c> or <c>discharge-summary</c>.</summary>
public static class UnsignedDraftKinds
{
    public const string Consultation = "consultation";
    public const string WardRound = "ward-round";
    public const string DischargeSummary = "discharge-summary";
}

/// <summary>
/// One unsigned draft on the director's list (Task 16): ids, the patient's number, the author, the visit's or admission's Lagos date and the
/// draft's age in Lagos days. No note text, no patient name.
/// </summary>
public sealed record UnsignedDraftDto(
    string Kind, Guid Id, Guid PatientId, string PatientNumber, Guid DoctorUserId, string DoctorName, Guid? VisitId, Guid? AdmissionId, DateOnly Date, int AgeDays);

public sealed record DepositsReportDto(int Count, long DepositsMinor, long CreditMinor, IReadOnlyList<DepositRowDto> Admissions);

/// <summary>
/// The cashier's and leadership's reports, each with a CSV. Money only: payment methods, price categories (never an item's name, so no drug or
/// test names), patient demographics and wards. Nothing clinical.
/// <list type="bullet">
/// <item>Takings: Succeeded payments (desk, deposits and online) by Lagos day and method. A reversed payment is left out of every day,
/// including the day it was taken; pending, mismatched and abandoned checkouts are never takings.</item>
/// <item>Debtors: patients with bills whose balance is above zero (a bill in credit has balance 0), with a WhatsApp reminder link carrying only
/// "Hello {name}, you have a balance of {amount} at {clinic}." No link for a patient recorded as deceased, or without a phone.</item>
/// <item>Revenue: bill lines charged in the range (removed lines left out; a bed line by its <c>ChargeDay</c>) by the price item's category (a line with no item is Other), then the
/// discounts and waivers granted in the range and the net.</item>
/// <item>Deposits: admissions still admitted, with the deposits received on the admission bill and the credit the bill still holds.</item>
/// </list>
/// </summary>
public sealed class CareReportService(IAppDbContext db, CareAccess access, ITenantContext tenant, TimeProvider clock)
{
    public const string Refused = "Only the cashier, admins and the medical director can see reports.";
    public const int MaxDays = 366;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>The cashier and leadership (Owner, Admin); 403 for front desk and clinical staff.</summary>
    private async Task EnsureAccessAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (!caller.IsLeadership && !caller.IsCashier) throw new ForbiddenException(Refused);
    }

    /// <summary>The range in Lagos days (default today), as UTC instants [start, end).</summary>
    private (DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset End) Range(DateOnly? from, DateOnly? to)
    {
        var last = to ?? CareCalendar.Today(clock.GetUtcNow());
        var first = from ?? last;
        if (first > last) throw BadRequestException.Field("from", "The start date must be on or before the end date.");
        if (last.DayNumber - first.DayNumber >= MaxDays) throw BadRequestException.Field("to", "Choose a range of at most 366 days.");
        return (first, last, Start(first), Start(last.AddDays(1)));
    }

    private static DateTimeOffset Start(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();

    private static readonly PayMethod[] Methods = [PayMethod.Cash, PayMethod.Pos, PayMethod.Transfer, PayMethod.Online];

    // ---------- Unsigned drafts (Task 16) ----------

    public const string DirectorOnly = "Only the medical director can see unsigned notes.";

    /// <summary>
    /// The medical director's list of notes left unsigned: consultation drafts on visits that are Done, and every unsigned ward-round and
    /// discharge-summary draft. Patient number, the author's name, the visit's or admission's Lagos date and the draft's age in Lagos days,
    /// oldest first. Never the note text. 403 for everyone but the Owner (support's view reads it: nothing on it is clinical).
    /// </summary>
    public async Task<IReadOnlyList<UnsignedDraftDto>> UnsignedDraftsAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (caller.MemberRole != Saas.Domain.Core.MemberRole.Owner) throw new ForbiddenException(DirectorOnly);
        var consultations = await (from c in db.Consultations.AsNoTracking()
                                   join v in db.Visits.AsNoTracking() on c.VisitId equals v.Id
                                   join p in db.Patients.AsNoTracking() on c.PatientId equals p.Id
                                   where c.Status == NoteStatus.Draft && v.Stage == VisitStage.Done
                                   select new { c.Id, c.PatientId, p.Number, c.DoctorUserId, VisitId = (Guid?)v.Id, AdmissionId = (Guid?)null, On = v.ArrivedAt, c.CreatedAt })
            .ToListAsync(ct);
        var rounds = await (from w in db.WardRounds.AsNoTracking()
                            join a in db.Admissions.AsNoTracking() on w.AdmissionId equals a.Id
                            join p in db.Patients.AsNoTracking() on w.PatientId equals p.Id
                            where w.Status == NoteStatus.Draft
                            select new { w.Id, w.PatientId, p.Number, w.DoctorUserId, VisitId = (Guid?)null, AdmissionId = (Guid?)a.Id, On = a.AdmittedAt, w.CreatedAt })
            .ToListAsync(ct);
        var summaries = await (from s in db.DischargeSummaries.AsNoTracking()
                               join a in db.Admissions.AsNoTracking() on s.AdmissionId equals a.Id
                               join p in db.Patients.AsNoTracking() on s.PatientId equals p.Id
                               where s.Status == NoteStatus.Draft
                               select new { s.Id, s.PatientId, p.Number, s.DoctorUserId, VisitId = (Guid?)null, AdmissionId = (Guid?)a.Id, On = a.AdmittedAt, s.CreatedAt })
            .ToListAsync(ct);
        var all = consultations.Select(x => (Kind: UnsignedDraftKinds.Consultation, Row: x))
            .Concat(rounds.Select(x => (Kind: UnsignedDraftKinds.WardRound, Row: x)))
            .Concat(summaries.Select(x => (Kind: UnsignedDraftKinds.DischargeSummary, Row: x))).ToList();
        var doctorIds = all.Select(x => x.Row.DoctorUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => doctorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var today = CareCalendar.Today(clock.GetUtcNow());
        return all
            .Select(x => new UnsignedDraftDto(x.Kind, x.Row.Id, x.Row.PatientId, x.Row.Number, x.Row.DoctorUserId, names.GetValueOrDefault(x.Row.DoctorUserId, ""),
                x.Row.VisitId, x.Row.AdmissionId, CareCalendar.Today(x.Row.On), Math.Max(0, today.DayNumber - CareCalendar.Today(x.Row.CreatedAt).DayNumber)))
            .OrderByDescending(d => d.AgeDays).ThenBy(d => d.PatientNumber, StringComparer.Ordinal).ThenBy(d => d.Id).ToList();
    }

    // ---------- Takings ----------

    public async Task<TakingsReportDto> TakingsAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await EnsureAccessAsync(ct);
        var range = Range(from, to);
        var payments = await db.CarePayments.AsNoTracking()
            .Where(p => p.Status == CarePaymentStatus.Succeeded && p.PaidAt >= range.Start && p.PaidAt < range.End)
            .Select(p => new { p.Method, p.AmountMinor, p.PaidAt }).ToListAsync(ct);
        var days = payments.GroupBy(p => CareCalendar.Today(p.PaidAt)).OrderBy(g => g.Key)
            .Select(g => new TakingsDayDto(g.Key, g.Count(), g.Sum(p => p.AmountMinor),
                Methods.Select(m => new MethodTotalDto(m, g.Count(p => p.Method == m), g.Where(p => p.Method == m).Sum(p => p.AmountMinor)))
                    .Where(m => m.Count > 0).ToList()))
            .ToList();
        var byMethod = Methods.Select(m => new MethodTotalDto(m, payments.Count(p => p.Method == m), payments.Where(p => p.Method == m).Sum(p => p.AmountMinor))).ToList();
        return new TakingsReportDto(range.From, range.To, payments.Count, payments.Sum(p => p.AmountMinor), byMethod, days);
    }

    public async Task<byte[]> TakingsCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var report = await TakingsAsync(from, to, ct);
        var sb = new StringBuilder("date,method,payments,amount\r\n");
        foreach (var day in report.Days)
            foreach (var m in day.ByMethod)
                sb.Append(CultureInfo.InvariantCulture, $"{day.Date:yyyy-MM-dd},{Cell(PaymentService.MethodLabel(m.Method))},{m.Count},{Major(m.AmountMinor)}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"total,,{report.Count},{Major(report.TotalMinor)}\r\n");
        return Bytes(sb);
    }

    // ---------- Debtors ----------

    public async Task<CareDebtorsReportDto> DebtorsAsync(CancellationToken ct)
    {
        await EnsureAccessAsync(ct);
        var rows = await (from b in db.PatientBills.AsNoTracking()
                          join p in db.Patients.AsNoTracking() on b.PatientId equals p.Id
                          // Final review minor 1: a bill flagged for review (an online mismatch, money perhaps sitting in Paystack) is not chased.
                          where b.BalanceMinor > 0 && !b.NeedsReview
                          select new { p.Id, p.Number, p.FirstName, p.LastName, p.Phone, p.Status, b.BalanceMinor, b.CreatedAt }).ToListAsync(ct);
        var clinic = await db.ClinicProfiles.AsNoTracking().Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "";
        var currency = await db.Tenants.Where(t => t.Id == TenantId).Select(t => t.Currency).FirstAsync(ct);
        var debtors = rows.GroupBy(r => r.Id).Select(g =>
            {
                var p = g.First();
                var name = $"{p.FirstName} {p.LastName}";
                var balance = g.Sum(r => r.BalanceMinor);
                // Never a reminder to the phone of a patient recorded as deceased.
                var url = p.Status == PatientStatus.Deceased
                    ? null
                    : GuardianInviteService.WhatsAppUrl(p.Phone, $"Hello {name}, you have a balance of {Money.Format(balance, currency)} at {clinic}.");
                return new CareDebtorDto(p.Id, p.Number, name, p.Phone, g.Count(), balance, g.Min(r => r.CreatedAt), url);
            })
            .OrderByDescending(d => d.BalanceMinor).ThenBy(d => d.OldestBillAt).ThenBy(d => d.PatientNumber).ToList();
        return new CareDebtorsReportDto(debtors.Count, debtors.Sum(d => d.BalanceMinor), debtors);
    }

    public async Task<byte[]> DebtorsCsvAsync(CancellationToken ct)
    {
        var report = await DebtorsAsync(ct);
        var sb = new StringBuilder("patient_number,patient_name,phone,bills,balance,oldest_bill\r\n");
        foreach (var d in report.Debtors)
            sb.Append(CultureInfo.InvariantCulture,
                $"{Cell(d.PatientNumber)},{Cell(d.PatientName)},{Cell(FinanceService.CsvPhone(d.Phone))},{d.Bills},{Major(d.BalanceMinor)},{CareCalendar.Today(d.OldestBillAt):yyyy-MM-dd}\r\n");
        return Bytes(sb);
    }

    // ---------- Revenue ----------

    public async Task<RevenueReportDto> RevenueAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await EnsureAccessAsync(ct);
        var range = Range(from, to);
        var lines = await (from l in db.BillLines.AsNoTracking()
                           join i in db.PriceItems.AsNoTracking() on l.PriceItemId equals i.Id into items
                           from i in items.DefaultIfEmpty()
                           // A bed line counts on the day it charges for (Task 13), whenever the job wrote it; other lines on their charge time.
                           where l.RemovedAt == null
                                 && (l.ChargeDay != null
                                     ? l.ChargeDay >= range.From && l.ChargeDay <= range.To
                                     : l.CreatedAt >= range.Start && l.CreatedAt < range.End)
                           select new { Category = i == null ? (PriceCategory?)null : i.Category, l.AmountMinor }).ToListAsync(ct);
        var adjustments = await db.BillAdjustments.AsNoTracking().Where(a => a.At >= range.Start && a.At < range.End)
            .Select(a => new { a.Kind, a.AmountMinor }).ToListAsync(ct);
        var byCategory = Enum.GetValues<PriceCategory>().Select(c =>
        {
            var mine = lines.Where(l => (l.Category ?? PriceCategory.Other) == c).ToList();
            return new CategoryTotalDto(c, mine.Count, mine.Sum(l => l.AmountMinor));
        }).ToList();
        var charged = lines.Sum(l => l.AmountMinor);
        var discounts = adjustments.Where(a => a.Kind == AdjustmentKind.Discount).Sum(a => a.AmountMinor);
        var waivers = adjustments.Where(a => a.Kind == AdjustmentKind.Waiver).Sum(a => a.AmountMinor);
        return new RevenueReportDto(range.From, range.To, byCategory, charged, discounts, waivers, charged - discounts - waivers);
    }

    public async Task<byte[]> RevenueCsvAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var report = await RevenueAsync(from, to, ct);
        var sb = new StringBuilder("category,lines,amount\r\n");
        foreach (var c in report.ByCategory)
            sb.Append(CultureInfo.InvariantCulture, $"{Cell(c.Category.ToString())},{c.Lines},{Major(c.AmountMinor)}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"{Cell("charged")},,{Major(report.ChargedMinor)}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"{Cell("discounts")},,{Major(report.DiscountsMinor)}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"{Cell("waivers")},,{Major(report.WaiversMinor)}\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"{Cell("net")},,{Major(report.NetMinor)}\r\n");
        return Bytes(sb);
    }

    // ---------- Deposits ----------

    public async Task<DepositsReportDto> DepositsAsync(CancellationToken ct)
    {
        await EnsureAccessAsync(ct);
        // Demographics, bed and money only: the admission's reason and admitting diagnosis are never read.
        var admissions = await (from a in db.Admissions.AsNoTracking()
                                join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                                join b in db.Beds.AsNoTracking() on a.BedId equals b.Id into beds
                                from b in beds.DefaultIfEmpty()
                                join w in db.Wards.AsNoTracking() on b.WardId equals w.Id into wards
                                from w in wards.DefaultIfEmpty()
                                where a.Status == AdmissionStatus.Admitted
                                select new
                                {
                                    a.Id, PatientId = p.Id, p.Number, p.FirstName, p.LastName, a.AdmittedAt,
                                    WardName = w == null ? null : w.Name, BedLabel = b == null ? null : b.Label,
                                }).ToListAsync(ct);
        var ids = admissions.Select(a => a.Id).ToList();
        var bills = await db.PatientBills.AsNoTracking().Where(b => b.AdmissionId != null && ids.Contains(b.AdmissionId.Value))
            .Select(b => new { b.Id, AdmissionId = b.AdmissionId!.Value, b.TotalMinor, b.PaidMinor, b.BalanceMinor, b.CreditMinor }).ToListAsync(ct);
        var billIds = bills.Select(b => b.Id).ToList();
        var deposits = (await db.CarePayments.AsNoTracking()
                .Where(p => p.IsDeposit && p.Status == CarePaymentStatus.Succeeded && billIds.Contains(p.BillId))
                .Select(p => new { p.BillId, p.AmountMinor }).ToListAsync(ct))
            .GroupBy(p => p.BillId).ToDictionary(g => g.Key, g => g.Sum(p => p.AmountMinor));
        var rows = admissions.OrderBy(a => a.AdmittedAt).Select(a =>
        {
            var bill = bills.FirstOrDefault(b => b.AdmissionId == a.Id);
            return new DepositRowDto(a.Id, a.PatientId, a.Number, $"{a.FirstName} {a.LastName}", a.WardName, a.BedLabel, a.AdmittedAt,
                bill is null ? 0 : deposits.GetValueOrDefault(bill.Id), bill?.TotalMinor ?? 0, bill?.PaidMinor ?? 0, bill?.BalanceMinor ?? 0, bill?.CreditMinor ?? 0);
        }).ToList();
        return new DepositsReportDto(rows.Count, rows.Sum(r => r.DepositMinor), rows.Sum(r => r.CreditMinor), rows);
    }

    public async Task<byte[]> DepositsCsvAsync(CancellationToken ct)
    {
        var report = await DepositsAsync(ct);
        var sb = new StringBuilder("patient_number,patient_name,ward,bed,admitted_on,deposit,charges,paid,balance,credit\r\n");
        foreach (var r in report.Admissions)
            sb.Append(CultureInfo.InvariantCulture,
                $"{Cell(r.PatientNumber)},{Cell(r.PatientName)},{Cell(r.WardName)},{Cell(r.BedLabel)},{CareCalendar.Today(r.AdmittedAt):yyyy-MM-dd},{Major(r.DepositMinor)},{Major(r.ChargesMinor)},{Major(r.PaidMinor)},{Major(r.BalanceMinor)},{Major(r.CreditMinor)}\r\n");
        return Bytes(sb);
    }

    // ---------- CSV ----------

    /// <summary>The shared formula-safe, always-quoted cell (as the SchoolLink debtors CSV).</summary>
    private static string Cell(string? value) => Platform.Csv.Cell(value);

    private static string Major(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>UTF-8 with a byte order mark and CRLF line ends, so Excel shows names with accents correctly.</summary>
    private static byte[] Bytes(StringBuilder sb) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
}
