using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>The doctor admits: the patient, optionally the consultation admitted from, the reason and the admitting diagnosis.</summary>
public sealed record AdmitRequest(Guid? PatientId, Guid? ConsultationId, string? Reason, string? AdmittingDiagnosis);

/// <summary>
/// The front desk assigns a bed and records the deposit (a desk payment on the admission bill). A deposit below the ward's minimum (or the
/// clinic default) needs <see cref="OverrideByOwner"/> from the medical director.
/// </summary>
public sealed record AssignBedRequest(Guid? BedId, long? DepositMinor, PayMethod? Method, string? Reference, bool OverrideByOwner = false);

public sealed record TransferRequest(Guid? BedId);

/// <summary>Task 16: the doctor to add to an admission's care team as a covering doctor.</summary>
public sealed record CareTeamRequest(Guid? UserId);

/// <summary>One doctor on an admission's care team (names and ids only), with when they joined and when their access ends, if set.</summary>
public sealed record CareTeamMemberDto(Guid UserId, string Name, CareAssignmentKind Kind, DateTimeOffset Since, DateTimeOffset? EndsAt);

/// <summary>An admission as every staff member sees it (bed board, ward lists, the desk): demographics, bed and status. Nothing clinical.</summary>
public sealed record AdmissionSummaryDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, PatientSex Sex, int AgeYears, AdmissionStatus Status, Guid? BedId,
    string? BedLabel, Guid? WardId, string? WardName, Guid AdmittingDoctorUserId, string? DoctorName, DateTimeOffset AdmittedAt, Guid? BillId);

/// <summary>An admission with its clinical fields (reason and admitting diagnosis, decrypted), for clinical staff only.</summary>
public sealed record AdmissionDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, PatientSex Sex, int AgeYears, AdmissionStatus Status, Guid? BedId,
    string? BedLabel, Guid? WardId, string? WardName, Guid AdmittingDoctorUserId, string? DoctorName, DateTimeOffset AdmittedAt, Guid? BillId,
    Guid? ConsultationId, string Reason, string AdmittingDiagnosis, Guid? DepositOverrideBy, DischargeType? DischargeType, DateTimeOffset? DischargedAt,
    string? AmaReason = null, DateTimeOffset? DeathTime = null, string? DeathCause = null, Guid? DischargedByUserId = null);

/// <summary>The bed assignment's answer: the admission (demographics only) and the deposit payment with its receipt number, if one was taken.</summary>
public sealed record BedAssignedDto(AdmissionSummaryDto Admission, CarePaymentDto? Deposit);

/// <summary>
/// Admissions. A doctor admits (reason and admitting diagnosis stored through <see cref="ClinicalText"/>; the admitting-doctor care
/// assignment; the admission bill opened). The front desk then assigns a bed with the deposit: the bed claim is a conditional update on a
/// Free bed (two racing claims: one gets 409 "That bed is occupied."), the deposit is a real desk payment on the admission bill
/// (<see cref="PaymentService.RecordDeskAsync(Guid, long, PayMethod, string?, bool, CancellationToken)"/>), and the ward's nurses get
/// ward-nurse assignments. A transfer claims the new bed the same way, sends the old one to Cleaning and moves the ward-nurse
/// assignments. Discharge (and ending the assignments 30 days after it) is Task 10's.
/// </summary>
public sealed class AdmissionService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, BillingService billing, PaymentService payments,
    PlanService plans,
    ICurrentUser user, TimeProvider clock)
{
    public const string DoctorsOnly = "Only doctors can admit patients.";
    public const string AlreadyAdmitted = "This patient is already admitted.";
    public const string Discharged = "This patient has been discharged.";
    public const string HasBed = "This patient already has a bed. Use a transfer to move them.";
    public const string NoBed = "Assign a bed first.";
    public const string BedCleaning = "That bed is being cleaned.";
    public const string NoBedsOnPlan = "Your plan doesn't include beds. Upgrade to admit patients.";
    public const string CareTeamManagers = "Only the admitting doctor or the medical director can change the care team.";
    public const int MaxReasonLength = 1000;
    public const int MaxDiagnosisLength = 500;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>Serialises admission changes for one patient: admitting, bed assignment and transfer. Lock order: this, then a bed row, then the bill lock.</summary>
    public static long PatientLockKey(Guid tenantId, Guid patientId) =>
        BitConverter.ToInt64(patientId.ToByteArray(), 0) ^ BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0CA5AD17;

    /// <summary>The minimum deposit sentence: "A deposit of at least ₦50,000 is needed for this ward."</summary>
    public static string DepositTooLow(long minimumMinor) => $"A deposit of at least {Money.Format(minimumMinor, "NGN")} is needed for this ward.";

    // ---------- Admit ----------

    /// <summary>
    /// Admits a patient: doctors and the medical director only. 400 <c>patientId</c>/<c>reason</c>/<c>admittingDiagnosis</c>/<c>consultationId</c>;
    /// 402 "Your plan doesn't include beds. Upgrade to admit patients."; 404 for another clinic's patient or consultation; 409 for a
    /// deceased or inactive patient or one already admitted. With a live care relationship with the patient
    /// (<see cref="RecordGuard.HasRelationshipAsync"/>) no reason is needed; otherwise (not the director) <paramref name="emergencyReason"/>
    /// (the X-Care-Reason header) is required and the admit goes through RecordGuard's emergency path (409
    /// <c>emergency_reason_required</c> without it). Writes the admitting-doctor
    /// assignment. A write: the answer echoes what the doctor wrote.
    /// </summary>
    public async Task<AdmissionDto> AdmitAsync(AdmitRequest r, string? emergencyReason, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly);
        if (r.PatientId is not { } patientId) throw BadRequestException.Field("patientId", "Choose the patient.");
        var reason = CareOrders.Required("reason", r.Reason, MaxReasonLength, "Write the reason for admission.");
        var diagnosis = CareOrders.Required("admittingDiagnosis", r.AdmittingDiagnosis, MaxDiagnosisLength, "Write the admitting diagnosis.");
        var status = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => (PatientStatus?)p.Status).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Patient");
        if (status == PatientStatus.Deceased) throw new ConflictException(AppointmentService.PatientDeceased, "patient_deceased");
        if (status == PatientStatus.Inactive) throw new ConflictException(AppointmentService.PatientInactive, "patient_inactive");
        // A plan without beds can't admit (ruling): the admission would wait for a bed forever.
        if ((await plans.GetCurrentPlanAsync(ct)).Limit(LimitKeys.Beds) == 0) throw new PlanLimitException(NoBedsOnPlan);
        if (r.ConsultationId is { } consultationId)
        {
            var consultationPatient = await db.Consultations.AsNoTracking().Where(c => c.Id == consultationId).Select(c => (Guid?)c.PatientId)
                .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Consultation");
            if (consultationPatient != patientId) throw BadRequestException.Field("consultationId", "That consultation is for another patient.");
        }

        // Ruling (Task 8, supersedes I2): admitting creates a care relationship, so it is free only for a caller who already has a live
        // one with the patient (their own live consultation, triage, an earlier admission…). Anyone else goes through emergency access:
        // the X-Care-Reason header, an Emergency access-log row (area admission) and the director's notice. A consultation that is the
        // caller's own but whose follow-up window has ended counts for nothing. The director is exempt. Logged before the admission is written.
        var now = clock.GetUtcNow();
        var emergency = caller.MemberRole != MemberRole.Owner && !await RecordGuard.HasRelationshipAsync(db, patientId, caller.UserId, now, ct);
        if (emergency) await guard.OpenAsync(patientId, RecordGuard.Areas.Admission, emergencyReason, emergencyUnlessDirector: true, ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(PatientLockKey(TenantId, patientId), ct);
        if (await db.Admissions.AnyAsync(a => a.PatientId == patientId && a.Status == AdmissionStatus.Admitted, ct))
            throw new ConflictException(AlreadyAdmitted, "already_admitted");

        var admission = new Admission
        {
            TenantId = TenantId, PatientId = patientId, AdmittingDoctorUserId = caller.UserId, ConsultationId = r.ConsultationId,
            Reason = text.Protect(reason), AdmittingDiagnosis = text.Protect(diagnosis), AdmittedAt = now, Status = AdmissionStatus.Admitted,
        };
        db.Admissions.Add(admission);
        db.CareAssignments.Add(new CareAssignment
        {
            TenantId = TenantId, PatientId = patientId, StaffUserId = caller.UserId, Kind = CareAssignmentKind.AdmittingDoctor, AdmissionId = admission.Id,
        });
        await db.SaveChangesAsync(ct);
        // The admission bill exists from the start, so the deposit and the first orders have somewhere to go.
        await billing.OpenBillForAdmissionAsync(patientId, admission.Id, ct);
        if (tx is not null) await tx.CommitAsync(ct);

        var summary = (await SummariesAsync(db, db.Admissions.AsNoTracking().Where(a => a.Id == admission.Id), clock, ct)).Single();
        return WithClinical(summary, admission, reason, diagnosis);
    }

    // ---------- Bed and deposit ----------

    /// <summary>
    /// Assigns the admission's first bed and records the deposit. Any staff member at the desk (front desk, cashier, leadership, clinical
    /// staff). 400 <c>bedId</c>/<c>depositMinor</c>/<c>method</c>/<c>reference</c>; 404 for an unknown admission or bed; 409 for a
    /// discharged admission, one that already has a bed, a bed being cleaned, "That bed is occupied." (the conditional claim lost), or
    /// "A deposit of at least {amount} is needed for this ward." unless the medical director sends <c>overrideByOwner</c> (audited).
    /// </summary>
    public async Task<BedAssignedDto> AssignBedAsync(Guid admissionId, AssignBedRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (r.BedId is not { } bedId) throw BadRequestException.Field("bedId", "Choose a bed.");
        var deposit = r.DepositMinor ?? 0;
        if (deposit < 0) throw BadRequestException.Field("depositMinor", "The deposit can't be negative.");
        if (deposit > 0) PaymentService.ValidateDesk(deposit, r.Method, r.Reference, "depositMinor");

        var patientId = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Admission");
        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(PatientLockKey(TenantId, patientId), ct);
        var admission = await db.Admissions.FirstAsync(a => a.Id == admissionId, ct);
        if (admission.Status != AdmissionStatus.Admitted) throw new ConflictException(Discharged, "admission_discharged");
        if (admission.BedId is not null) throw new ConflictException(HasBed, "admission_has_bed");
        var (bed, ward) = await BedAsync(bedId, ct);

        var minimum = ward.MinDepositMinor ?? await db.ClinicProfiles.AsNoTracking().Select(c => c.MinDepositMinor).FirstOrDefaultAsync(ct);
        var overridden = false;
        if (deposit < minimum)
        {
            if (!(r.OverrideByOwner && caller.MemberRole == MemberRole.Owner && !caller.IsSupportView))
                throw new ConflictException(DepositTooLow(minimum), "deposit_below_minimum");
            overridden = true;
        }

        await ClaimAsync(bed, ct);
        admission.BedId = bed.Id;
        if (overridden) admission.DepositOverrideBy = caller.UserId;
        db.BedMoves.Add(new BedMove { TenantId = TenantId, AdmissionId = admission.Id, FromBedId = null, ToBedId = bed.Id, At = now, ByUserId = caller.UserId });
        await WardService.SyncWardNursesAsync(db, TenantId, admission, ward, now, ct);
        if (overridden)
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = TenantId, ActorId = caller.UserId, Action = "care.deposit_override", EntityType = nameof(Admission),
                EntityId = admission.Id.ToString(),
                AfterJson = JsonSerializer.Serialize(new { patientId, wardId = ward.Id, bedId = bed.Id, depositMinor = deposit, minimumMinor = minimum }),
                Ip = user.IpAddress, CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);

        CarePaymentDto? payment = null;
        if (deposit > 0)
        {
            var bill = await billing.OpenBillForAdmissionAsync(patientId, admission.Id, ct);
            payment = await payments.RecordDeskAsync(bill.Id, deposit, r.Method!.Value, r.Reference, deposit: true, ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        var summary = (await SummariesAsync(db, db.Admissions.AsNoTracking().Where(a => a.Id == admission.Id), clock, ct)).Single();
        return new BedAssignedDto(summary, payment);
    }

    // ---------- Transfer ----------

    /// <summary>
    /// Moves the patient to another bed (in the same or another ward): front desk and clinical staff and above (the cashier gets 403).
    /// The new bed is claimed like a first bed; the old one goes to Cleaning; a bed move is recorded (the bed charge follows the ward from
    /// that day, Task 13); the ward-nurse assignments move to the new ward's nurses.
    /// </summary>
    public async Task<AdmissionSummaryDto> TransferAsync(Guid admissionId, TransferRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        PatientService.EnsureDesk(caller);
        if (r.BedId is not { } bedId) throw BadRequestException.Field("bedId", "Choose a bed.");
        var patientId = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Admission");

        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(PatientLockKey(TenantId, patientId), ct);
        var admission = await db.Admissions.FirstAsync(a => a.Id == admissionId, ct);
        if (admission.Status != AdmissionStatus.Admitted) throw new ConflictException(Discharged, "admission_discharged");
        if (admission.BedId is not { } fromBedId) throw new ConflictException(NoBed, "admission_no_bed");
        if (fromBedId == bedId) throw BadRequestException.Field("bedId", "The patient is already in that bed.");
        var (bed, ward) = await BedAsync(bedId, ct);

        await ClaimAsync(bed, ct);
        var oldBed = await db.Beds.FirstAsync(b => b.Id == fromBedId, ct);
        oldBed.Status = BedStatus.Cleaning;
        admission.BedId = bed.Id;
        db.BedMoves.Add(new BedMove { TenantId = TenantId, AdmissionId = admission.Id, FromBedId = fromBedId, ToBedId = bed.Id, At = now, ByUserId = caller.UserId });
        await WardService.SyncWardNursesAsync(db, TenantId, admission, ward, now, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (await SummariesAsync(db, db.Admissions.AsNoTracking().Where(a => a.Id == admission.Id), clock, ct)).Single();
    }

    // ---------- Clinical read ----------

    /// <summary>
    /// One admission with its reason and admitting diagnosis: a clinical read through <see cref="RecordGuard.OpenAsync"/> (area admission;
    /// 403 for non-clinical staff before anything is looked up; the <c>X-Care-Reason</c> header for emergency access).
    /// </summary>
    public async Task<AdmissionDto> GetAsync(Guid id, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await db.Admissions.AsNoTracking().Where(a => a.Id == id).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Admission");
        await guard.OpenAsync(patientId, RecordGuard.Areas.Admission, reason, ct);
        return (await ReadManyAsync(db, text, db.Admissions.AsNoTracking().Where(a => a.Id == id), clock, ct)).Single();
    }

    // ---------- Care team (Task 16: the covering doctor) ----------

    /// <summary>
    /// Adds a covering doctor to the admission's care team: the admitting doctor or the medical director only (403 "Only the admitting
    /// doctor or the medical director can change the care team."). The doctor must be one of the clinic's active doctors (400
    /// <c>userId</c>); 404 for an unknown admission; 409 "This patient has been discharged." The doctor gets a CoveringDoctor care
    /// assignment, a live care relationship that ends like the admitting doctor's (discharge + 30 days), so they read the record without a
    /// reason and write ward care, ward rounds and the discharge. Adding a doctor already on the team changes nothing. Audited with ids
    /// only. Returns the team (admitting and covering doctors with live rows).
    /// </summary>
    public async Task<IReadOnlyList<CareTeamMemberDto>> AddCoveringDoctorAsync(Guid admissionId, CareTeamRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var patientId = await EnsureTeamManagerAsync(caller, admissionId, ct);
        if (r.UserId is not { } doctorId) throw BadRequestException.Field("userId", "Choose a doctor.");
        if (!await IsActiveDoctorAsync(doctorId, ct)) throw BadRequestException.Field("userId", "Choose one of the clinic's doctors.");

        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(PatientLockKey(TenantId, patientId), ct);
        var status = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => a.Status).FirstAsync(ct);
        if (status != AdmissionStatus.Admitted) throw new ConflictException(Discharged, "admission_discharged");
        var onTeam = await db.CareAssignments.AnyAsync(a => a.AdmissionId == admissionId && a.StaffUserId == doctorId
            && (a.Kind == CareAssignmentKind.AdmittingDoctor || a.Kind == CareAssignmentKind.CoveringDoctor) && (a.EndedAt == null || a.EndedAt > now), ct);
        if (!onTeam)
        {
            db.CareAssignments.Add(new CareAssignment
            {
                TenantId = TenantId, PatientId = patientId, StaffUserId = doctorId, Kind = CareAssignmentKind.CoveringDoctor, AdmissionId = admissionId,
            });
            db.AuditLogs.Add(TeamAudit(caller, "care.care_team_added", admissionId, patientId, doctorId, now));
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return await TeamAsync(admissionId, now, ct);
    }

    /// <summary>
    /// Ends a covering doctor's place on the care team (the row's end is set to now): the admitting doctor or the director. 404 when the
    /// doctor has no live covering row on this admission (the admitting doctor can't be removed). Audited with ids only. Allowed after
    /// discharge too (it cuts the follow-up window short).
    /// </summary>
    public async Task RemoveCoveringDoctorAsync(Guid admissionId, Guid doctorId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var patientId = await EnsureTeamManagerAsync(caller, admissionId, ct);
        var now = clock.GetUtcNow();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(PatientLockKey(TenantId, patientId), ct);
        var rows = await db.CareAssignments.Where(a => a.AdmissionId == admissionId && a.StaffUserId == doctorId
            && a.Kind == CareAssignmentKind.CoveringDoctor && (a.EndedAt == null || a.EndedAt > now)).ToListAsync(ct);
        if (rows.Count == 0) throw new NotFoundException("Covering doctor");
        foreach (var row in rows) row.EndedAt = now;
        db.AuditLogs.Add(TeamAudit(caller, "care.care_team_removed", admissionId, patientId, doctorId, now));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>403 unless a doctor (or the director) with clinical access; 404 for an unknown admission; 403 unless the admitting doctor or the director. Returns the patient.</summary>
    private async Task<Guid> EnsureTeamManagerAsync(CareCaller caller, Guid admissionId, CancellationToken ct)
    {
        CareOrders.EnsureDoctor(access, caller, CareTeamManagers);
        var admission = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => new { a.PatientId, a.AdmittingDoctorUserId })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Admission");
        if (caller.MemberRole != MemberRole.Owner && caller.UserId != admission.AdmittingDoctorUserId) throw new ForbiddenException(CareTeamManagers);
        return admission.PatientId;
    }

    private async Task<bool> IsActiveDoctorAsync(Guid userId, CancellationToken ct) =>
        await (from p in db.CareStaffProfiles.AsNoTracking()
               join m in db.Memberships.AsNoTracking() on p.MembershipId equals m.Id
               where p.UserId == userId && p.ClinicalRole == ClinicalRole.Doctor && m.Status == MembershipStatus.Active && m.Role != MemberRole.Accountant
               select p.Id).AnyAsync(ct);

    /// <summary>The admission's doctors with a live care assignment (admitting and covering), earliest first.</summary>
    private async Task<IReadOnlyList<CareTeamMemberDto>> TeamAsync(Guid admissionId, DateTimeOffset now, CancellationToken ct) =>
        await (from a in db.CareAssignments.AsNoTracking()
               join u in db.Users.AsNoTracking() on a.StaffUserId equals u.Id
               where a.AdmissionId == admissionId && (a.Kind == CareAssignmentKind.AdmittingDoctor || a.Kind == CareAssignmentKind.CoveringDoctor)
                   && (a.EndedAt == null || a.EndedAt > now)
               orderby a.CreatedAt, a.Id
               select new CareTeamMemberDto(a.StaffUserId, u.FullName, a.Kind, a.CreatedAt, a.EndedAt)).ToListAsync(ct);

    private AuditLog TeamAudit(CareCaller caller, string action, Guid admissionId, Guid patientId, Guid doctorId, DateTimeOffset now) => new()
    {
        TenantId = TenantId, ActorId = caller.UserId, Action = action, EntityType = nameof(Admission), EntityId = admissionId.ToString(),
        AfterJson = JsonSerializer.Serialize(new { admissionId, patientId, doctorUserId = doctorId }), Ip = user.IpAddress, CreatedAt = now,
    };

    // ---------- Discharge (Task 10 calls this) ----------

    /// <summary>
    /// Ends the admission's open care assignments at discharge (ruling): ward-nurse rows at <paramref name="dischargedAt"/> (ward nurses
    /// don't do follow-up), the admitting doctor's at <paramref name="dischargedAt"/> + 30 days (the follow-up window, as for visits).
    /// Rows that already ended (a nurse who left the ward) keep their end. Adds to the context only; the caller saves (Task 10's discharge).
    /// </summary>
    public static async Task EndAssignmentsAtDischargeAsync(IAppDbContext db, Guid admissionId, DateTimeOffset dischargedAt, CancellationToken ct)
    {
        var open = await db.CareAssignments.Where(a => a.AdmissionId == admissionId && a.EndedAt == null).ToListAsync(ct);
        foreach (var row in open)
            row.EndedAt = row.Kind == CareAssignmentKind.WardNurse ? dischargedAt : dischargedAt + VisitService.FollowUpWindow;
    }

    // ---------- Shared reads ----------

    /// <summary>Admissions as demographic summaries (patient, bed, ward, doctor, bill), newest first. No clinical field is reachable from here.</summary>
    internal static async Task<List<AdmissionSummaryDto>> SummariesAsync(IAppDbContext db, IQueryable<Admission> admissions, TimeProvider clock, CancellationToken ct)
    {
        var rows = await (from a in admissions
                          join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                          join b in db.Beds.AsNoTracking() on a.BedId equals b.Id into beds
                          from b in beds.DefaultIfEmpty()
                          join w in db.Wards.AsNoTracking() on b.WardId equals w.Id into wards
                          from w in wards.DefaultIfEmpty()
                          join u in db.Users.AsNoTracking() on a.AdmittingDoctorUserId equals u.Id into doctors
                          from u in doctors.DefaultIfEmpty()
                          orderby a.AdmittedAt descending, a.Id
                          select new
                          {
                              a.Id, a.PatientId, p.Number, Name = p.FirstName + " " + p.LastName, p.Sex, p.DateOfBirth, a.Status, a.BedId,
                              BedLabel = b == null ? null : b.Label, WardId = w == null ? (Guid?)null : w.Id, WardName = w == null ? null : w.Name,
                              a.AdmittingDoctorUserId, DoctorName = u == null ? null : u.FullName, a.AdmittedAt,
                              BillId = db.PatientBills.Where(x => x.AdmissionId == a.Id).Select(x => (Guid?)x.Id).FirstOrDefault(),
                          }).ToListAsync(ct);
        var today = CareCalendar.Today(clock.GetUtcNow());
        return rows.Select(r => new AdmissionSummaryDto(r.Id, r.PatientId, r.Number, r.Name, r.Sex, Age(r.DateOfBirth, today), r.Status, r.BedId,
            r.BedLabel, r.WardId, r.WardName, r.AdmittingDoctorUserId, r.DoctorName, r.AdmittedAt, r.BillId)).ToList();
    }

    /// <summary>Admissions with their clinical fields decrypted, newest first. No access check: the caller has opened the record.</summary>
    internal static async Task<List<AdmissionDto>> ReadManyAsync(
        IAppDbContext db, ClinicalText text, IQueryable<Admission> admissions, TimeProvider clock, CancellationToken ct)
    {
        var summaries = await SummariesAsync(db, admissions, clock, ct);
        if (summaries.Count == 0) return [];
        var ids = summaries.Select(s => s.Id).ToList();
        var rows = await db.Admissions.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return summaries.Select(s =>
        {
            var a = rows[s.Id];
            return WithClinical(s, a, text.Read(a.Reason) ?? "", text.Read(a.AdmittingDiagnosis) ?? "") with
            {
                AmaReason = text.Read(a.AmaReason), DeathTime = a.DeathTime, DeathCause = text.Read(a.DeathCause), DischargedByUserId = a.DischargedByUserId,
            };
        }).ToList();
    }

    private static AdmissionDto WithClinical(AdmissionSummaryDto s, Admission a, string reason, string diagnosis) =>
        new(s.Id, s.PatientId, s.PatientNumber, s.PatientName, s.Sex, s.AgeYears, s.Status, s.BedId, s.BedLabel, s.WardId, s.WardName,
            s.AdmittingDoctorUserId, s.DoctorName, s.AdmittedAt, s.BillId, a.ConsultationId, reason, diagnosis, a.DepositOverrideBy, a.DischargeType,
            a.DischargedAt);

    private static int Age(DateOnly dob, DateOnly today) =>
        Math.Max(0, today.Year - dob.Year - (today < dob.AddYears(today.Year - dob.Year) ? 1 : 0));

    // ---------- Internals ----------

    /// <summary>The bed and its ward: 404 for an unknown bed, 400 for an inactive one, 409 for one being cleaned.</summary>
    private async Task<(Bed Bed, Ward Ward)> BedAsync(Guid bedId, CancellationToken ct)
    {
        var bed = await db.Beds.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bedId, ct) ?? throw new NotFoundException("Bed");
        if (!bed.Active) throw BadRequestException.Field("bedId", "That bed is out of use. Choose another.");
        if (bed.Status == BedStatus.Cleaning) throw new ConflictException(BedCleaning, "bed_cleaning");
        var ward = await db.Wards.AsNoTracking().FirstAsync(w => w.Id == bed.WardId, ct);
        return (bed, ward);
    }

    /// <summary>
    /// The conditional bed claim: Free (and active) to Occupied in one statement. Of two claims racing for one bed, the second waits on the
    /// row lock, re-checks the condition after the first commits, matches nothing and gets 409 "That bed is occupied."
    /// </summary>
    private async Task ClaimAsync(Bed bed, CancellationToken ct)
    {
        var claimed = await db.Beds.Where(b => b.Id == bed.Id && b.Status == BedStatus.Free && b.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BedStatus.Occupied), ct);
        if (claimed == 0) throw new ConflictException(WardService.BedOccupied, "bed_occupied");
    }
}
