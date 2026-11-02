using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>Check a patient in, optionally against a booked appointment.</summary>
public sealed record CheckInRequest(Guid? PatientId, Guid? AppointmentId);

/// <summary>
/// A visit as every staff member sees it (the queue, the desk): demographics, stage, urgency and minutes waited. Nothing clinical:
/// vital signs are read through <c>GET /visits/{id}/vitals</c> by clinical staff only.
/// </summary>
public sealed record VisitDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, PatientSex Sex, int AgeYears, VisitStage Stage, Urgency Urgency,
    Guid? DoctorUserId, string? DoctorName, Guid? AppointmentId, Guid? BillId, DateTimeOffset ArrivedAt, int MinutesWaited);

public sealed record QueueStageDto(VisitStage Stage, IReadOnlyList<VisitDto> Visits);

/// <summary>One Lagos day's visits, grouped by stage in flow order (every stage present). <see cref="Mine"/>: only the caller's patients.</summary>
public sealed record QueueDto(DateOnly Date, bool Mine, IReadOnlyList<QueueStageDto> Stages);

/// <summary>The readings taken at triage. At least one of temperature, blood pressure, pulse, respiratory rate, SpO2, weight or height.</summary>
public sealed record VitalsInput(
    decimal? TemperatureC, int? Systolic, int? Diastolic, int? Pulse, int? RespRate, int? SpO2, decimal? WeightKg, decimal? HeightCm);

/// <summary>Triage: the readings, how urgent, and the doctor who will see the patient (defaults to the appointment's doctor).</summary>
public sealed record TriageRequest(VitalsInput? Vitals, Urgency? Urgency, Guid? DoctorUserId);

/// <summary>A set of vital signs, for clinical staff only.</summary>
public sealed record VitalSignsDto(
    Guid Id, Guid PatientId, Guid? VisitId, Guid TakenBy, string TakenByName, DateTimeOffset TakenAt, decimal? TemperatureC, int? Systolic,
    int? Diastolic, int? Pulse, int? RespRate, int? SpO2, decimal? WeightKg, decimal? HeightCm, decimal? Bmi, IReadOnlyList<string> Flags,
    bool Critical);

/// <summary>The triage answer: the visit and the readings the caller just recorded (an echo of their own input, with its flags).</summary>
public sealed record TriageResultDto(VisitDto Visit, VitalSignsDto Vitals);

public sealed record StageRequest(VisitStage? Stage);

/// <summary>
/// Outpatient visits: check-in (charges registration on the first visit and the consultation every visit, and checks the appointment in),
/// the day's queue, triage with vital signs, and stage changes. The queue and visit reads are demographics only; vital signs are clinical
/// and are read through <see cref="RecordGuard"/>. Triage writes the care relationships (triage nurse, visit doctor) that let those staff
/// open the record, and a critical reading notifies the doctor in-app without any value.
/// </summary>
public sealed class VisitService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, BillingService billing, CareNotices notices,
    ICurrentUser user, TimeProvider clock)
{
    public const string AlreadyCheckedIn = "This patient is already checked in.";
    public const string AppointmentNotBooked = "Only a booked appointment can be checked in.";
    public const string VisitClosed = "This visit is closed.";
    public const string TriageNotWaiting = "Triage is only for patients waiting to be seen.";
    public const string BillNotPaid = "This bill isn't paid yet.";
    /// <summary>How long a visit's care relationships (triage nurse, visit doctor) last after the visit is Done: follow-up review.</summary>
    public static readonly TimeSpan FollowUpWindow = TimeSpan.FromDays(30);
    public const string CriticalNotice = CareNotices.CriticalVitalText;
    public const string CriticalNoticeKind = CareNotices.CriticalVital;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>Serialises check-ins of one patient, so two desks can't open two visits (and two registration charges) at once.</summary>
    private static long CheckInLockKey(Guid tenantId, Guid patientId) =>
        BitConverter.ToInt64(patientId.ToByteArray(), 0) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0CA5E715;

    private static (DateTimeOffset From, DateTimeOffset To) LagosDay(DateOnly day)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), CareCalendar.LagosOffset).ToUniversalTime();
        return (from, from.AddDays(1));
    }

    // ---------- Check-in ----------

    /// <summary>
    /// Checks the patient in: a new visit (WaitingVitals, Routine), its bill with the registration line on the patient's first visit and
    /// the consultation line (from the clinic profile; an unset or inactive item is skipped), and, with an appointment, the appointment
    /// set to CheckedIn under the doctor's booking lock. Front desk and above (the cashier gets 403).
    /// 400 <c>patientId</c>/<c>appointmentId</c>; 404 for another clinic's patient or appointment; 409 for a deceased or inactive patient,
    /// a patient already in today's queue, or an appointment that isn't Booked.
    /// </summary>
    public async Task<VisitDto> CheckInAsync(CheckInRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PatientService.EnsureDesk(caller);
        if (r.PatientId is not { } patientId) throw BadRequestException.Field("patientId", "Choose a patient.");
        var status = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => (PatientStatus?)p.Status).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Patient");
        if (status == PatientStatus.Deceased) throw new ConflictException(AppointmentService.PatientDeceased, "patient_deceased");
        if (status == PatientStatus.Inactive) throw new ConflictException(AppointmentService.PatientInactive, "patient_inactive");

        Guid? appointmentDoctor = null;
        if (r.AppointmentId is { } appointmentId)
        {
            var booked = await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId)
                .Select(a => new { a.PatientId, a.DoctorUserId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Appointment");
            if (booked.PatientId != patientId) throw BadRequestException.Field("appointmentId", "That appointment is for another patient.");
            appointmentDoctor = booked.DoctorUserId;
        }

        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Lock order: the doctor's booking lock, then the patient's check-in lock, then the patient's admission lock, then (inside
        // BillingService) the clinic's bill lock. Nothing takes them the other way round (the discharge holds only the patient lock and then
        // the bill lock; booking takes the doctor lock and then the patient lock).
        if (appointmentDoctor is { } doctorId)
        {
            await db.LockAsync(AppointmentService.LockKey(TenantId, doctorId), ct);
            var appointment = await db.Appointments.FirstAsync(a => a.Id == r.AppointmentId, ct);
            // Only from Booked (a cancelled, no-show, done or already checked-in appointment can't be checked in).
            if (appointment.Status != AppointmentStatus.Booked) throw new ConflictException(AppointmentNotBooked, "appointment_not_booked");
            appointment.Status = AppointmentStatus.CheckedIn;
            appointmentDoctor = appointment.DoctorUserId; // the doctor as stored now, in case a reschedule moved it before the lock
        }
        await db.LockAsync(CheckInLockKey(TenantId, patientId), ct);
        // Task 16 (Task 14 carry): a Died discharge holds the patient lock while it records the death, so a check-in that passed the first
        // check waits for it here, re-reads the status and gets 409; no visit or bill is opened for a patient who has died.
        await db.LockAsync(AdmissionService.PatientLockKey(TenantId, patientId), ct);
        var current = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => p.Status).FirstAsync(ct);
        if (current == PatientStatus.Deceased) throw new ConflictException(AppointmentService.PatientDeceased, "patient_deceased");
        if (current == PatientStatus.Inactive) throw new ConflictException(AppointmentService.PatientInactive, "patient_inactive");
        var (from, to) = LagosDay(CareCalendar.Today(now));
        if (await db.Visits.AnyAsync(v => v.PatientId == patientId && v.Stage != VisitStage.Done && v.ArrivedAt >= from && v.ArrivedAt < to, ct))
            throw new ConflictException(AlreadyCheckedIn, "already_checked_in");
        var firstVisit = !await db.Visits.AnyAsync(v => v.PatientId == patientId, ct);

        var visit = new Visit
        {
            TenantId = TenantId, PatientId = patientId, ArrivedAt = now, Stage = VisitStage.WaitingVitals, Urgency = Urgency.Routine,
            DoctorUserId = appointmentDoctor, AppointmentId = r.AppointmentId, CheckedInByUserId = caller.UserId,
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);

        var bill = await billing.OpenBillForVisitAsync(patientId, visit.Id, ct);
        var profile = await db.ClinicProfiles.AsNoTracking()
            .Select(p => new { p.RegistrationPriceItemId, p.ConsultationPriceItemId }).FirstAsync(ct);
        if (firstVisit && await ActiveItemAsync(profile.RegistrationPriceItemId, ct) is { } registration)
            await billing.AddLineAsync(bill.Id, registration, 1, null, ct);
        if (await ActiveItemAsync(profile.ConsultationPriceItemId, ct) is { } consultation)
            await billing.AddLineAsync(bill.Id, consultation, 1, null, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(visit.Id, ct);
    }

    /// <summary>The item id when it is set and still on the active price list (a deactivated item is not charged, and doesn't block check-in).</summary>
    private async Task<Guid?> ActiveItemAsync(Guid? itemId, CancellationToken ct) =>
        itemId is { } id && await db.PriceItems.AnyAsync(i => i.Id == id && i.Active, ct) ? id : null;

    // ---------- Queue and visit reads (demographics only) ----------

    /// <summary>
    /// The day's visits (a Lagos date, default today) by stage, Emergency first then longest waiting. A doctor (other than the medical
    /// director) sees only their own patients unless <paramref name="mine"/> is false. Every member, including the cashier, who takes
    /// payments from the WaitingPayment column.
    /// </summary>
    public async Task<QueueDto> QueueAsync(DateOnly? date, bool? mine, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var now = clock.GetUtcNow();
        var day = date ?? CareCalendar.Today(now);
        var onlyMine = mine ?? (caller.ClinicalRole == ClinicalRole.Doctor && caller.MemberRole != MemberRole.Owner);
        var (from, to) = LagosDay(day);
        var visits = db.Visits.AsNoTracking().Where(v => v.ArrivedAt >= from && v.ArrivedAt < to);
        if (onlyMine) visits = visits.Where(v => v.DoctorUserId == caller.UserId);
        var rows = await DtosAsync(visits, ct);
        var stages = Enum.GetValues<VisitStage>()
            .Select(s => new QueueStageDto(s, rows.Where(v => v.Stage == s)
                .OrderByDescending(v => v.Urgency).ThenBy(v => v.ArrivedAt).ThenBy(v => v.Id).ToList()))
            .ToList();
        return new QueueDto(day, onlyMine, stages);
    }

    /// <summary>One visit, demographics only. Every member.</summary>
    public async Task<VisitDto> GetAsync(Guid id, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        return await ReadAsync(id, ct);
    }

    // ---------- Vital signs (clinical) ----------

    /// <summary>
    /// The visit's vital signs, oldest first. Clinical staff only: the read goes through <see cref="RecordGuard.OpenAsync"/> (area
    /// record; logged; an unrelated caller needs the <c>X-Care-Reason</c> header). 403 "Only doctors and nurses can see clinical notes."
    /// </summary>
    public async Task<IReadOnlyList<VitalSignsDto>> VitalsAsync(Guid visitId, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await db.Visits.AsNoTracking().Where(v => v.Id == visitId).Select(v => (Guid?)v.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Visit");
        await guard.OpenAsync(patientId, RecordGuard.Areas.Record, reason, ct);
        var rows = await (from v in db.VitalSigns.AsNoTracking()
                          join u in db.Users.AsNoTracking() on v.TakenBy equals u.Id into taken
                          from u in taken.DefaultIfEmpty()
                          where v.VisitId == visitId
                          orderby v.TakenAt, v.Id
                          select new { Vitals = v, Name = u == null ? null : u.FullName }).ToListAsync(ct);
        return rows.Select(r => ToDto(r.Vitals, r.Name ?? "")).ToList();
    }

    /// <summary>
    /// Records the triage: vital signs (flagged against <see cref="Vitals"/>), urgency, and the doctor. Doctors and nurses (and the
    /// medical director) only. Writes the care relationships (the caller as triage nurse, the doctor as visit doctor; a replaced doctor's
    /// relationship to this visit ends), moves WaitingVitals to WaitingDoctor, and on a critical reading tells the doctor in-app
    /// ("A patient you are seeing has a critical vital sign.", no value). A write: it answers with the caller's own readings and is not
    /// logged as a record open, like saving clinical facts.
    /// </summary>
    public async Task<TriageResultDto> TriageAsync(Guid visitId, TriageRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var input = ValidateVitals(r.Vitals);
        if (r.Urgency is { } u && !Enum.IsDefined(u)) throw BadRequestException.Field("urgency", "Choose an urgency.");
        // The consultation open takes the same per-visit lock, so the visit's doctor can't change between its doctor check and the draft.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(ConsultationService.VisitLockKey(TenantId, visitId), ct);
        var visit = await db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct) ?? throw new NotFoundException("Visit");
        // Only a patient still waiting to be seen is triaged, so triage can't be used to join the care of a patient further along.
        if (visit.Stage is not (VisitStage.WaitingVitals or VisitStage.WaitingDoctor))
            throw new ConflictException(TriageNotWaiting, "triage_not_waiting");
        var doctorId = r.DoctorUserId ?? visit.DoctorUserId ?? throw BadRequestException.Field("doctorUserId", "Choose a doctor.");
        if (!await IsActiveDoctorAsync(doctorId, ct)) throw BadRequestException.Field("doctorUserId", "Choose a doctor.");

        var now = clock.GetUtcNow();
        var vitals = new VitalSigns
        {
            TenantId = TenantId, PatientId = visit.PatientId, VisitId = visit.Id, TakenBy = caller.UserId, TakenAt = now,
            TemperatureC = input.TemperatureC, Systolic = input.Systolic, Diastolic = input.Diastolic, Pulse = input.Pulse,
            RespRate = input.RespRate, SpO2 = input.SpO2, WeightKg = input.WeightKg, HeightCm = input.HeightCm,
            Bmi = Vitals.Bmi(input.WeightKg, input.HeightCm),
        };
        vitals.Flags = Vitals.Flag(vitals);

        // The consultation draft follows the visit's doctor (re-triage to another doctor hands it over, audited).
        if (visit.DoctorUserId != doctorId)
            await ConsultationService.HandDraftToAsync(db, TenantId, visit.Id, doctorId, caller.UserId, user.IpAddress, now, ct);
        var assignments = await db.CareAssignments.Where(a => a.VisitId == visit.Id && a.EndedAt == null).ToListAsync(ct);
        if (!assignments.Any(a => a.StaffUserId == caller.UserId && a.Kind == CareAssignmentKind.TriageNurse))
        {
            // A caller who had no care relationship with this patient gains one here: that goes in the access log (area triage),
            // so self-assignment through triage is as visible as an open.
            if (!await RecordGuard.HasRelationshipAsync(db, visit.PatientId, caller.UserId, now, ct))
                db.RecordAccesses.Add(new RecordAccess
                {
                    TenantId = TenantId, StaffUserId = caller.UserId, Role = RecordGuard.RoleLabel(caller), PatientId = visit.PatientId,
                    Area = RecordGuard.Areas.Triage, Emergency = false, At = now,
                });
            db.CareAssignments.Add(new CareAssignment
            {
                TenantId = TenantId, PatientId = visit.PatientId, StaffUserId = caller.UserId, Kind = CareAssignmentKind.TriageNurse, VisitId = visit.Id,
            });
        }
        foreach (var replaced in assignments.Where(a => a.Kind == CareAssignmentKind.VisitDoctor && a.StaffUserId != doctorId))
            replaced.EndedAt = now;
        if (!assignments.Any(a => a.StaffUserId == doctorId && a.Kind == CareAssignmentKind.VisitDoctor))
            db.CareAssignments.Add(new CareAssignment
            {
                TenantId = TenantId, PatientId = visit.PatientId, StaffUserId = doctorId, Kind = CareAssignmentKind.VisitDoctor, VisitId = visit.Id,
            });

        db.VitalSigns.Add(vitals);
        visit.Urgency = r.Urgency ?? visit.Urgency;
        // Ruling (Task 14): the doctor hears "Patient {number} is waiting to see you." when the patient joins their queue: the first
        // triage, or a re-triage to another doctor. Not when they triaged the patient themself.
        var joinsQueue = visit.Stage == VisitStage.WaitingVitals || visit.DoctorUserId != doctorId;
        visit.DoctorUserId = doctorId;
        if (visit.Stage == VisitStage.WaitingVitals) visit.Stage = VisitStage.WaitingDoctor;
        if (joinsQueue && doctorId != caller.UserId)
            notices.PatientWaitingFor(doctorId, await db.Patients.AsNoTracking().Where(p => p.Id == visit.PatientId).Select(p => p.Number).FirstAsync(ct));
        // The doctor who took the readings themself already knows; anyone else's critical reading reaches them in-app, with no value.
        var critical = Vitals.IsCritical(vitals.Flags);
        if (critical && doctorId != caller.UserId) notices.CriticalVitalFor(doctorId);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        var name = await db.Users.AsNoTracking().Where(u => u.Id == caller.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return new TriageResultDto(await ReadAsync(visit.Id, ct), ToDto(vitals, name ?? ""));
    }

    // ---------- Stage ----------

    /// <summary>
    /// Moves the visit to another stage. Front desk and above; the cashier only closes a visit that is waiting for payment. A Done visit
    /// is closed (409). Done stops the wait and completes a checked-in appointment (under the doctor's booking lock).
    /// </summary>
    public async Task<VisitDto> SetStageAsync(Guid visitId, StageRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (r.Stage is not { } stage || !Enum.IsDefined(stage)) throw BadRequestException.Field("stage", "Choose a stage.");
        var visit = await db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct) ?? throw new NotFoundException("Visit");
        if (caller.IsCashier)
        {
            if (visit.Stage != VisitStage.WaitingPayment || stage != VisitStage.Done) throw new ForbiddenException();
        }
        else PatientService.EnsureDesk(caller);
        if (visit.Stage == VisitStage.Done) throw new ConflictException(VisitClosed, "visit_closed");
        if (caller.IsCashier)
        {
            // The cashier closes a visit only when its bill is settled: nothing owed, or waived.
            var bill = await db.PatientBills.AsNoTracking().Where(b => b.VisitId == visit.Id)
                .Select(b => new { b.BalanceMinor, b.Status }).FirstOrDefaultAsync(ct);
            if (bill is not null && bill.BalanceMinor > 0 && bill.Status != BillStatus.Waived)
                throw new ConflictException(BillNotPaid, "bill_not_paid");
        }

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Ruling (Task 14): a visit sent to payment with money owed tells the patient's portal users the bill number and balance.
        if (stage == VisitStage.WaitingPayment && visit.Stage != VisitStage.WaitingPayment
            && await db.PatientBills.AsNoTracking().Where(b => b.VisitId == visit.Id).Select(b => new { b.Number, b.BalanceMinor }).FirstOrDefaultAsync(ct) is { } due)
            await notices.BillDueAsync(visit.PatientId, due.Number, due.BalanceMinor, ct);
        visit.Stage = stage;
        if (stage == VisitStage.Done)
        {
            visit.DoneAt = clock.GetUtcNow();
            // The visit's care relationships last for a follow-up window after Done, then opening the record needs a reason.
            var relationships = await db.CareAssignments.Where(a => a.VisitId == visit.Id && a.EndedAt == null
                && (a.Kind == CareAssignmentKind.TriageNurse || a.Kind == CareAssignmentKind.VisitDoctor)).ToListAsync(ct);
            foreach (var a in relationships) a.EndedAt = visit.DoneAt + FollowUpWindow;
            if (visit.AppointmentId is { } appointmentId
                && await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => (Guid?)a.DoctorUserId).FirstOrDefaultAsync(ct) is { } doctor)
            {
                await db.LockAsync(AppointmentService.LockKey(TenantId, doctor), ct);
                var appointment = await db.Appointments.FirstAsync(a => a.Id == appointmentId, ct);
                if (appointment.Status == AppointmentStatus.CheckedIn) appointment.Status = AppointmentStatus.Done;
            }
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(visit.Id, ct);
    }

    // ---------- Internals ----------

    private async Task<bool> IsActiveDoctorAsync(Guid userId, CancellationToken ct) =>
        await (from p in db.CareStaffProfiles.AsNoTracking()
               join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
               where p.UserId == userId && p.ClinicalRole == ClinicalRole.Doctor && m.Status == MembershipStatus.Active
               select p.Id).AnyAsync(ct);

    /// <summary>Checks the readings are present and physically plausible (field <c>vitals.{name}</c>).</summary>
    private static VitalsInput ValidateVitals(VitalsInput? v)
    {
        if (v is null || (v.TemperatureC is null && v.Systolic is null && v.Diastolic is null && v.Pulse is null && v.RespRate is null
            && v.SpO2 is null && v.WeightKg is null && v.HeightCm is null))
            throw BadRequestException.Field("vitals", "Record at least one vital sign.");
        Check("vitals.temperatureC", v.TemperatureC, 25m, 45m, "a temperature from 25 to 45 °C");
        Check("vitals.systolic", v.Systolic, 40, 300, "a systolic pressure from 40 to 300");
        Check("vitals.diastolic", v.Diastolic, 20, 200, "a diastolic pressure from 20 to 200");
        Check("vitals.pulse", v.Pulse, 10, 300, "a pulse from 10 to 300");
        Check("vitals.respRate", v.RespRate, 2, 80, "a respiratory rate from 2 to 80");
        Check("vitals.spO2", v.SpO2, 30, 100, "an SpO2 from 30 to 100");
        Check("vitals.weightKg", v.WeightKg, 0.3m, 400m, "a weight from 0.3 to 400 kg");
        Check("vitals.heightCm", v.HeightCm, 20m, 250m, "a height from 20 to 250 cm");
        if (v.Systolic is { } s && v.Diastolic is { } d && d >= s)
            throw BadRequestException.Field("vitals.diastolic", "The diastolic pressure must be lower than the systolic.");
        return v with
        {
            TemperatureC = v.TemperatureC is { } t ? Math.Round(t, 1, MidpointRounding.AwayFromZero) : null,
            WeightKg = v.WeightKg is { } w ? Math.Round(w, 1, MidpointRounding.AwayFromZero) : null,
            HeightCm = v.HeightCm is { } h ? Math.Round(h, 1, MidpointRounding.AwayFromZero) : null,
        };
    }

    private static void Check(string field, decimal? value, decimal min, decimal max, string expected)
    {
        if (value is { } x && (x < min || x > max)) throw BadRequestException.Field(field, $"Enter {expected}.");
    }

    internal static VitalSignsDto ToDto(VitalSigns v, string takenByName) =>
        new(v.Id, v.PatientId, v.VisitId, v.TakenBy, takenByName, v.TakenAt, v.TemperatureC, v.Systolic, v.Diastolic, v.Pulse, v.RespRate,
            v.SpO2, v.WeightKg, v.HeightCm, v.Bmi, v.Flags, Vitals.IsCritical(v.Flags));

    private async Task<VisitDto> ReadAsync(Guid id, CancellationToken ct) =>
        (await DtosAsync(db.Visits.AsNoTracking().Where(v => v.Id == id), ct)).FirstOrDefault() ?? throw new NotFoundException("Visit");

    /// <summary>Joins visits to their patient (demographics), doctor name and bill. No clinical field is reachable from here.</summary>
    private async Task<List<VisitDto>> DtosAsync(IQueryable<Visit> visits, CancellationToken ct)
    {
        var rows = await (from v in visits
                          join p in db.Patients.AsNoTracking() on v.PatientId equals p.Id
                          join u in db.Users.AsNoTracking() on v.DoctorUserId equals u.Id into doctors
                          from u in doctors.DefaultIfEmpty()
                          select new
                          {
                              Visit = v, p.Number, Name = p.FirstName + " " + p.LastName, p.Sex, p.DateOfBirth,
                              DoctorName = u == null ? null : u.FullName,
                              BillId = db.PatientBills.Where(b => b.VisitId == v.Id).Select(b => (Guid?)b.Id).FirstOrDefault(),
                          }).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var today = CareCalendar.Today(now);
        return rows.Select(r =>
        {
            var v = r.Visit;
            var age = today.Year - r.DateOfBirth.Year - (today < r.DateOfBirth.AddYears(today.Year - r.DateOfBirth.Year) ? 1 : 0);
            var waited = (int)Math.Max(0, Math.Floor(((v.DoneAt ?? now) - v.ArrivedAt).TotalMinutes));
            return new VisitDto(v.Id, v.PatientId, r.Number, r.Name, r.Sex, age, v.Stage, v.Urgency, v.DoctorUserId, r.DoctorName, v.AppointmentId,
                r.BillId, v.ArrivedAt, waited);
        }).ToList();
    }
}
