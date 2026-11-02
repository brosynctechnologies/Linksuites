using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// The care job's steps for one clinic (Task 13), run by <see cref="CareJobs"/> in a scope with that clinic's tenant set. Each is
/// idempotent. Notices carry no clinical detail: patient numbers, ward names, times and amounts only, never a drug, a diagnosis or the
/// appointment's reason.
/// </summary>
public sealed class CareJobSteps(
    IAppDbContext db, ITenantContext tenant, CareNotices notices, ILogger<CareJobSteps> logger)
{
    /// <summary>Reminders for tomorrow's appointments go out from 08:00 Africa/Lagos.</summary>
    public static readonly TimeSpan ReminderTime = TimeSpan.FromHours(8);
    /// <summary>A Due dose becomes Missed this long after it was due.</summary>
    public static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(60);
    /// <summary>A dose more than this overdue (the job was down) becomes Missed without a notice (ruling M6).</summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromHours(6);
    /// <summary>At most this many reminders or missed doses a run; the rest wait for the next minute.</summary>
    public const int MaxPerRun = 500;

    public const string ReminderKind = CareNotices.AppointmentReminder;
    public const string MissedDoseKind = CareNotices.MissedDose;
    public const string LowDepositKind = CareNotices.LowDeposit;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    private static DateTimeOffset StartOf(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();

    // ---------- Appointment reminders ----------

    /// <summary>
    /// From 08:00 Lagos, each Booked appointment starting tomorrow (Lagos day) of an active patient is reminded once: a conditional update
    /// sets <see cref="Appointment.ReminderSentAt"/>, and only the run that set it tells the patient's portal users, in the same
    /// transaction. The text names the clinic and the time, never the reason. Returns how many appointments were marked.
    /// </summary>
    public async Task<int> SendRemindersAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (now.ToOffset(CareCalendar.LagosOffset).TimeOfDay < ReminderTime) return 0;
        var tomorrow = CareCalendar.Today(now).AddDays(1);
        var windowStart = StartOf(tomorrow);
        var windowEnd = StartOf(tomorrow.AddDays(1));
        var due = await (from a in db.Appointments.AsNoTracking()
                         join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                         where a.Status == AppointmentStatus.Booked && a.ReminderSentAt == null && a.Start >= windowStart && a.Start < windowEnd
                               && p.Status == PatientStatus.Active
                         orderby a.Start
                         select new { a.Id, a.PatientId, a.Start }).Take(MaxPerRun).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var sent = 0;
        foreach (var appointment in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var marked = await db.Appointments
                .Where(a => a.Id == appointment.Id && a.ReminderSentAt == null && a.Status == AppointmentStatus.Booked && a.Start == appointment.Start)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReminderSentAt, now), ct);
            if (marked == 0) continue;
            await notices.AppointmentReminderAsync(appointment.PatientId, appointment.Start, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            sent++;
        }
        return sent;
    }

    // ---------- No-shows ----------

    /// <summary>A Booked appointment that ended before today (Lagos) becomes NoShow. One conditional update; returns how many.</summary>
    public Task<int> MarkNoShowsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = StartOf(CareCalendar.Today(now));
        return db.Appointments.Where(a => a.Status == AppointmentStatus.Booked && a.End <= today)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.NoShow).SetProperty(a => a.UpdatedAt, (DateTimeOffset?)now), ct);
    }

    // ---------- Missed doses ----------

    /// <summary>
    /// A Due dose of an admitted admission, 60 minutes past its due time, becomes Missed (carry, Task 9: Due only, admitted only, one
    /// conditional update, so a dose recorded meanwhile is left alone). Only the run that flipped it tells the ward's nurses
    /// (<see cref="WardService.WardNursesAsync"/>; every active nurse when the patient has no bed yet), once, in the same transaction.
    /// A dose more than 6 hours overdue (<see cref="SilentAfter"/>, after an outage) is flipped with no notice (ruling M6).
    /// A Missed dose can still be recorded late. Returns how many doses it flagged.
    /// </summary>
    public async Task<int> FlagMissedDosesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - MissedAfter;
        var overdue = await (from d in db.MedicationDoses.AsNoTracking()
                             join a in db.Admissions.AsNoTracking() on d.AdmissionId equals a.Id
                             join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                             where d.Status == DoseStatus.Due && d.DueAt <= cutoff && a.Status == AdmissionStatus.Admitted
                             orderby d.DueAt
                             select new { d.Id, d.AdmissionId, d.DueAt, a.BedId, p.Number }).Take(MaxPerRun).ToListAsync(ct);
        if (overdue.Count == 0) return 0;

        var bedIds = overdue.Where(d => d.BedId != null).Select(d => d.BedId!.Value).Distinct().ToList();
        var wardOfBed = await db.Beds.AsNoTracking().Where(b => bedIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.WardId, ct);
        var wardIds = wardOfBed.Values.Distinct().ToList();
        var wards = await db.Wards.AsNoTracking().Where(w => wardIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        var nursesOf = new Dictionary<Guid, HashSet<Guid>>();
        async Task<(string Name, HashSet<Guid> Nurses)> WardAsync(Guid? bedId)
        {
            Ward? ward = bedId is { } b && wardOfBed.TryGetValue(b, out var wardId) ? wards.GetValueOrDefault(wardId) : null;
            var key = ward?.Id ?? Guid.Empty;
            if (!nursesOf.TryGetValue(key, out var nurses))
                nursesOf[key] = nurses = await WardService.WardNursesAsync(db, ward ?? new Ward { Name = "" }, ct);
            return (ward?.Name ?? "the wards", nurses);
        }

        var flagged = 0;
        foreach (var dose in overdue)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var changed = await db.MedicationDoses
                .Where(d => d.Id == dose.Id && d.Status == DoseStatus.Due
                            && db.Admissions.Any(a => a.Id == d.AdmissionId && a.Status == AdmissionStatus.Admitted))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DoseStatus.Missed).SetProperty(d => d.UpdatedAt, (DateTimeOffset?)now), ct);
            if (changed == 0) continue;
            flagged++;
            // Ruling (M6): a dose this stale (after an outage) is flipped quietly: a notice hours late helps no one.
            if (dose.DueAt < now - SilentAfter)
            {
                await tx.CommitAsync(ct);
                continue;
            }
            var (wardName, nurses) = await WardAsync(dose.BedId);
            notices.MissedDoseFor(nurses, dose.Number, wardName, dose.AdmissionId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return flagged;
    }

    // ---------- Bed charges ----------

    /// <summary>
    /// Once per Lagos day (<see cref="ClinicProfile.LastBedChargeDay"/>), charges every admitted admission's bed days up to yesterday
    /// (<see cref="BedCharges.ChargeOutstandingAsync"/>), each under the patient's admission lock in its own transaction. A day missed
    /// (the job was down) is charged by the next run, since every uncharged day of the stay is filled in. When an admission fails, the day
    /// isn't marked, so the next minute tries again. Discharged admissions are charged by the discharge itself. Returns the days charged.
    /// </summary>
    public async Task<int> ChargeBedDaysAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = CareCalendar.Today(now);
        var profile = await db.ClinicProfiles.AsNoTracking().Select(p => new { p.Id, p.LastBedChargeDay }).FirstOrDefaultAsync(ct);
        if (profile is null || profile.LastBedChargeDay >= today) return 0;
        var admissions = await db.Admissions.AsNoTracking().Where(a => a.Status == AdmissionStatus.Admitted && a.BedId != null)
            .OrderBy(a => a.AdmittedAt).Select(a => new { a.Id, a.PatientId }).ToListAsync(ct);
        var charged = 0;
        var failed = false;
        foreach (var admission in admissions)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.LockAsync(AdmissionService.PatientLockKey(TenantId, admission.PatientId), ct);
                if (!await db.Admissions.AnyAsync(a => a.Id == admission.Id && a.Status == AdmissionStatus.Admitted, ct)) continue;
                charged += await BedCharges.ChargeOutstandingAsync(db, admission.Id, today.AddDays(-1), includeSameDayStay: false, ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                failed = true;
                logger.LogError(ex, "Care bed charges failed for admission {AdmissionId}", admission.Id);
            }
        }
        if (!failed)
            await db.ClinicProfiles.Where(p => p.Id == profile.Id && (p.LastBedChargeDay == null || p.LastBedChargeDay < today))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastBedChargeDay, today), ct);
        return charged;
    }

    // ---------- Low deposit ----------

    /// <summary>
    /// Ruling (Task 13): an admission's deposit left is its bill's credit less its balance (negative once the patient owes). When it is
    /// below the clinic's <see cref="ClinicProfile.LowDepositThresholdMinor"/> (0 by default: once the patient owes anything), the cashiers
    /// and the front desk are told once: a conditional update sets <see cref="Admission.LowDepositNotifiedAt"/> and only that run notifies.
    /// When a top-up lifts it back to the threshold, the marker clears, so the next crossing is told again. Admitted admissions with a bed
    /// only (the deposit is taken with the bed). Returns how many admissions were notified.
    /// </summary>
    public async Task<int> CheckDepositsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.ClinicProfiles.AsNoTracking().Select(p => new { p.LowDepositThresholdMinor }).FirstOrDefaultAsync(ct);
        if (profile is null) return 0;
        var threshold = profile.LowDepositThresholdMinor;
        var rows = await (from a in db.Admissions.AsNoTracking()
                          join b in db.PatientBills.AsNoTracking() on a.Id equals b.AdmissionId
                          join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                          where a.Status == AdmissionStatus.Admitted && a.BedId != null
                          select new { a.Id, a.LowDepositNotifiedAt, BillId = b.Id, BillNumber = b.Number, b.CreditMinor, b.BalanceMinor, p.Number })
            .ToListAsync(ct);
        List<Guid>? staff = null;
        var notified = 0;
        foreach (var row in rows)
        {
            var left = row.CreditMinor - row.BalanceMinor;
            var low = left < threshold;
            if (!low)
            {
                if (row.LowDepositNotifiedAt is not null)
                    await db.Admissions.Where(a => a.Id == row.Id && a.LowDepositNotifiedAt != null)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.LowDepositNotifiedAt, (DateTimeOffset?)null), ct);
                continue;
            }
            if (row.LowDepositNotifiedAt is not null) continue;

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var marked = await db.Admissions.Where(a => a.Id == row.Id && a.LowDepositNotifiedAt == null && a.Status == AdmissionStatus.Admitted)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.LowDepositNotifiedAt, (DateTimeOffset?)now), ct);
            if (marked == 0) continue;
            staff ??= await CashiersAndDeskAsync(ct);
            notices.LowDepositFor(staff, row.Number, row.BillNumber, left, row.BillId);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            notified++;
        }
        return notified;
    }

    /// <summary>The clinic's active cashiers (Accountant) and front desk staff.</summary>
    private async Task<List<Guid>> CashiersAndDeskAsync(CancellationToken ct)
    {
        var tenantId = TenantId;
        var cashiers = await db.Memberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active && m.Role == MemberRole.Accountant)
            .Select(m => m.UserId).ToListAsync(ct);
        var desk = await (from p in db.CareStaffProfiles.AsNoTracking()
                          join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
                          where m.TenantId == tenantId && m.Status == MembershipStatus.Active && p.ClinicalRole == ClinicalRole.FrontDesk
                          select p.UserId).ToListAsync(ct);
        return cashiers.Concat(desk).Distinct().ToList();
    }
}
