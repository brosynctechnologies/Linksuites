using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

/// <summary>
/// The medication chart's schedule: an admission prescription item becomes scheduled doses (<see cref="MedicationDose"/>), written in
/// the prescription's own transaction (<see cref="PrescriptionService.CreateAsync"/>).
/// </summary>
public static class MedicationChart
{
    /// <summary>Days scheduled when the doctor gave no duration.</summary>
    public const int DefaultDays = 3;

    /// <summary>The Lagos hour a night-time (Nocte) dose is given.</summary>
    public const int NocteHour = 22;

    /// <summary>
    /// When the item's doses are due. Doses are spaced evenly across 24 hours (<see cref="FrequencyDosesPerDay.For"/> a day) from
    /// <paramref name="start"/> rounded up to the next whole hour (never before the prescription was written), for
    /// <see cref="PrescriptionItem.DurationDays"/> days, or <see cref="DefaultDays"/> when that is null. TDS for 3 days is 9 doses, 8
    /// hours apart. Stat is one dose at <paramref name="start"/>; PRN (as needed) schedules nothing. Nocte is one dose a night at
    /// 22:00 Lagos time, from the first such time at or after the rounded start. All times are UTC.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> Schedule(PrescriptionItem item, DateTimeOffset start)
    {
        start = start.ToUniversalTime();
        if (FrequencyDosesPerDay.IsAsNeeded(item.Frequency)) return [];
        // Postgres keeps microseconds: truncate, so the stored time is the time computed here.
        if (FrequencyDosesPerDay.IsOnce(item.Frequency)) return [start.AddTicks(-(start.Ticks % 10))];

        var perDay = FrequencyDosesPerDay.For(item.Frequency);
        var days = item.DurationDays ?? DefaultDays;
        var first = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, TimeSpan.Zero);
        if (first < start) first = first.AddHours(1);
        if (item.Frequency == DoseFrequency.Nocte)
        {
            var lagos = first.ToOffset(CareCalendar.LagosOffset);
            var night = new DateTimeOffset(lagos.Year, lagos.Month, lagos.Day, NocteHour, 0, 0, CareCalendar.LagosOffset);
            if (night < first) night = night.AddDays(1);
            first = night.ToUniversalTime();
        }
        var step = TimeSpan.FromHours(24.0 / perDay);
        return Enumerable.Range(0, perDay * days).Select(i => first + step * i).ToList();
    }

    /// <summary>
    /// Ruling (Task 10 M4): at discharge, every Due dose due at or before <paramref name="at"/> was never given and never will be, so it
    /// becomes Missed (no notice is sent: the patient has left). One conditional update in the caller's transaction; returns how many.
    /// </summary>
    public static Task<int> MissPastDosesAsync(IAppDbContext db, Guid admissionId, DateTimeOffset at, CancellationToken ct)
    {
        var utc = at.ToUniversalTime();
        return db.MedicationDoses.Where(d => d.AdmissionId == admissionId && d.Status == DoseStatus.Due && d.DueAt <= utc)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DoseStatus.Missed).SetProperty(d => d.UpdatedAt, (DateTimeOffset?)utc), ct);
    }

    /// <summary>
    /// Ruling (Task 9 I2): stops the rest of an admission's course. Every Due dose due after <paramref name="at"/> becomes Stopped, so
    /// Task 13's missed-dose job (which flips only Due doses) never flags it and a nurse can't record it (409 "This dose was stopped.").
    /// Doses due at or before <paramref name="at"/>, and doses already recorded or Missed, are left alone. Runs at once as one conditional
    /// update (inside the caller's transaction, if any); calling it again changes nothing. Returns how many doses it stopped. Task 10's
    /// discharge calls it with the discharge time, under <see cref="AdmissionService.PatientLockKey"/>.
    /// </summary>
    public static Task<int> StopFutureDosesAsync(IAppDbContext db, Guid admissionId, DateTimeOffset at, CancellationToken ct)
    {
        var utc = at.ToUniversalTime();
        return db.MedicationDoses.Where(d => d.AdmissionId == admissionId && d.Status == DoseStatus.Due && d.DueAt > utc)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DoseStatus.Stopped).SetProperty(d => d.UpdatedAt, (DateTimeOffset?)DateTimeOffset.UtcNow), ct);
    }

    /// <summary>
    /// Adds the scheduled doses of an admission prescription to the context (nothing for an outpatient prescription). The caller saves,
    /// inside the prescription's transaction.
    /// </summary>
    internal static int AddDoses(IAppDbContext db, Prescription prescription, DateTimeOffset start)
    {
        if (prescription.AdmissionId is not { } admissionId) return 0;
        var count = 0;
        foreach (var item in prescription.Items)
            foreach (var due in Schedule(item, start))
            {
                db.MedicationDoses.Add(new MedicationDose
                {
                    TenantId = prescription.TenantId, AdmissionId = admissionId, PatientId = prescription.PatientId, PrescriptionId = prescription.Id,
                    PrescriptionItemId = item.Id, DueAt = due, Status = DoseStatus.Due,
                });
                count++;
            }
        return count;
    }
}
