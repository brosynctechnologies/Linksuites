using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>The discharge summary as the doctor writes it (a full replace of the draft). Every text field is optional until signing.</summary>
public sealed record SaveDischargeSummaryRequest(string? Diagnoses, string? Treatment, string? Condition, string? TakeHome, DateOnly? FollowUp);

/// <summary>
/// Discharging a patient: how they leave; <see cref="AmaReason"/> for a discharge against advice; <see cref="DeathTime"/> and
/// <see cref="DeathCause"/> when the patient died. <see cref="AcknowledgeUnsignedNotes"/> discharges although ward-round drafts are still
/// unsigned (they stay drafts; the acknowledgement is recorded).
/// </summary>
public sealed record DischargeRequest(DischargeType? Type, string? AmaReason, DateTimeOffset? DeathTime, string? DeathCause, bool AcknowledgeUnsignedNotes = false);

/// <summary>The discharge summary with its addenda (oldest first), for clinical staff only.</summary>
public sealed record DischargeSummaryDto(
    Guid Id, Guid AdmissionId, Guid PatientId, Guid DoctorUserId, string DoctorName, string? Diagnoses, string? Treatment, string? Condition,
    string? TakeHome, DateOnly? FollowUp, NoteStatus Status, DateTimeOffset? SignedAt, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    IReadOnlyList<AddendumDto> Addenda);

/// <summary>An unsigned ward-round draft named in the discharge 409: who started it and when. Never its text.</summary>
public sealed record UnsignedRoundDto(Guid Id, Guid DoctorUserId, string DoctorName, DateTimeOffset CreatedAt);

/// <summary>The discharge's answer: the admission (demographics, bed, status), how and when the patient left, and the bill left open.</summary>
public sealed record DischargedDto(
    AdmissionSummaryDto Admission, DischargeType Type, DateTimeOffset DischargedAt, Guid BillId, long BalanceMinor, int StoppedDoses, int MissedDoses,
    int UnsignedRoundsAcknowledged);

/// <summary>
/// One row of the admissions list (current and recent). Everyone allowed to list sees demographics, bed and status; the discharge type
/// and the discharge summary's status are filled for clinical staff only (left out of the JSON otherwise).
/// </summary>
public sealed record AdmissionListItemDto(
    Guid Id, Guid PatientId, string PatientNumber, string PatientName, PatientSex Sex, int AgeYears, AdmissionStatus Status, Guid? BedId,
    string? BedLabel, Guid? WardId, string? WardName, Guid AdmittingDoctorUserId, string? DoctorName, DateTimeOffset AdmittedAt, Guid? BillId,
    DateTimeOffset? DischargedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DischargeType? DischargeType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NoteStatus? DischargeSummaryStatus = null);

/// <summary>What the discharge PDF prints: the clinic letterhead, the patient and stay, the signed summary and the doctor.</summary>
public sealed record DischargePdfData(
    string ClinicName, string? Address, string? Phone, byte[]? Logo, string PatientName, string PatientNumber, string Sex, int AgeYears,
    string AdmittedOn, string? DischargedOn, string? WardName, string? DischargeType, string? DeathTime, string? DeathCause,
    string Diagnoses, string Treatment, string Condition, string TakeHome, string? FollowUp, string DoctorName, string? DoctorTitle,
    string? LicenceNumber, string SignedOn, bool ShowBranding);

/// <summary>
/// Discharge (Task 10).
/// <list type="bullet">
/// <item><b>The discharge summary</b> (one per admission): doctors and the director on the admission's care team (a live care assignment
/// for this admission: the admitting doctor keeps one for 30 days after discharge) write and sign it; the author only, as consultations;
/// its text is stored through <see cref="ClinicalText"/>; reads are clinical opens through <see cref="RecordGuard"/> (area discharge; the
/// PDF area pdf).</item>
/// <item><b>Discharging</b> takes the patient's admission lock (<see cref="AdmissionService.PatientLockKey"/>), so it is serialised with
/// ward-care writes and admission orders. Home and transferred-out need a signed summary; against advice needs the reason; died needs the
/// time and cause and sets the patient Deceased. Unsigned ward-round drafts give 409 with the list unless acknowledged. Then the care
/// assignments end (<see cref="AdmissionService.EndAssignmentsAtDischargeAsync"/>), future doses stop
/// (<see cref="MedicationChart.StopFutureDosesAsync"/>), the bed goes to Cleaning, the outstanding bed days are charged
/// (<see cref="BedCharges.ChargeOutstandingAsync"/>) and the bill becomes final (no new lines; the
/// balance stays open as a debt).</item>
/// <item><b>The admissions list</b> (current and recent) is for leadership and clinical staff, demographics, bed and status only.</item>
/// </list>
/// </summary>
public sealed class DischargeService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, BillingService billing, IFileStorage storage,
    ICarePdf pdf, PlanService plans, ICurrentUser user, TimeProvider clock, CareNotices notices)
{
    public const string DoctorsOnly = "Only doctors can discharge patients.";
    public const string SummaryDoctorsOnly = "Only doctors can write discharge summaries.";
    public const string SummaryUnsigned = "Sign the discharge summary before discharging the patient.";
    public const string UnsignedRounds = "Sign or discard the unsigned ward-round notes first.";
    public const string EmptySummary = "Write the diagnoses and the condition at discharge before signing.";
    public const string NoSignedSummary = "The discharge summary isn't signed yet.";
    public const int MaxDiagnosesLength = 2_000;
    public const int MaxTreatmentLength = 10_000;
    public const int MaxConditionLength = 1_000;
    public const int MaxTakeHomeLength = 4_000;
    public const int MaxAmaReasonLength = 1_000;
    public const int MaxDeathCauseLength = 500;
    /// <summary>Discharged admissions stay on the list this long.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);
    public const int MaxListed = 500;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Discharge summary ----------

    /// <summary>The admission's discharge summary: a clinical open (area discharge). 404 when there is none yet (after the open is logged).</summary>
    public async Task<DischargeSummaryDto> GetSummaryAsync(Guid admissionId, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await PatientOfAsync(admissionId, ct);
        await guard.OpenAsync(patientId, RecordGuard.Areas.Discharge, reason, ct);
        return (await ReadManyAsync(db, text, db.DischargeSummaries.AsNoTracking().Where(s => s.AdmissionId == admissionId), ct)).SingleOrDefault()
            ?? throw new NotFoundException("Discharge summary");
    }

    /// <summary>
    /// Writes the draft (a full replace; the first save creates it and makes the caller its author): doctors and the director (403
    /// "Only doctors can write discharge summaries."). 400 for a field over its length or a follow-up date in the past; 404; 403 "This
    /// patient isn't on your ward." without a live care assignment for this admission (the director is exempt); 409 "This note is
    /// signed. Add an addendum instead."; 403 "Only the doctor who wrote this note can change it.". A write: the answer echoes it.
    /// </summary>
    public async Task<DischargeSummaryDto> SaveSummaryAsync(Guid admissionId, SaveDischargeSummaryRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, SummaryDoctorsOnly);
        var diagnoses = CareOrders.Optional("diagnoses", r.Diagnoses, MaxDiagnosesLength);
        var treatment = CareOrders.Optional("treatment", r.Treatment, MaxTreatmentLength);
        var condition = CareOrders.Optional("condition", r.Condition, MaxConditionLength);
        var takeHome = CareOrders.Optional("takeHome", r.TakeHome, MaxTakeHomeLength);
        var now = Now();
        if (r.FollowUp is { } followUp && followUp < CareCalendar.Today(now))
            throw BadRequestException.Field("followUp", "Choose a follow-up date from today on.");

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForTeamAsync(caller, admissionId, now, ct);
        var summary = await db.DischargeSummaries.FirstOrDefaultAsync(s => s.AdmissionId == admissionId, ct);
        if (summary is null)
        {
            summary = new DischargeSummary { TenantId = TenantId, AdmissionId = admissionId, PatientId = patientId, DoctorUserId = caller.UserId };
            db.DischargeSummaries.Add(summary);
        }
        else
        {
            ConsultationService.EnsureDraft(summary.Status);
            if (summary.DoctorUserId != caller.UserId) throw new ForbiddenException(ConsultationService.AuthorOnly);
            summary.UpdatedAt = now;
        }
        summary.Diagnoses = text.Protect(diagnoses);
        summary.Treatment = text.Protect(treatment);
        summary.Condition = text.Protect(condition);
        summary.TakeHome = text.Protect(takeHome);
        summary.FollowUp = r.FollowUp;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(admissionId, ct);
    }

    /// <summary>
    /// Signs the summary: final from now on (corrections are addenda, <c>/notes/discharge/{id}/addenda</c>). The same checks as
    /// <see cref="SaveSummaryAsync"/>, 404 when there is no draft, then 409 "Write the diagnoses and the condition at discharge before
    /// signing.". Writes an audit row with no clinical text.
    /// </summary>
    public async Task<DischargeSummaryDto> SignSummaryAsync(Guid admissionId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, SummaryDoctorsOnly);
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForTeamAsync(caller, admissionId, now, ct);
        var summary = await db.DischargeSummaries.FirstOrDefaultAsync(s => s.AdmissionId == admissionId, ct) ?? throw new NotFoundException("Discharge summary");
        ConsultationService.EnsureDraft(summary.Status);
        if (summary.DoctorUserId != caller.UserId) throw new ForbiddenException(ConsultationService.AuthorOnly);
        if (string.IsNullOrEmpty(summary.Diagnoses) || string.IsNullOrEmpty(summary.Condition)) throw new ConflictException(EmptySummary, "note_empty");
        summary.Status = NoteStatus.Signed;
        summary.SignedAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = TenantId, ActorId = caller.UserId, Action = "care.note_signed", EntityType = nameof(DischargeSummary), EntityId = summary.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new { noteKind = nameof(NoteKind.Discharge), admissionId, patientId }),
            Ip = user.IpAddress, CreatedAt = now,
        });
        // Signing makes the summary readable in the portal: "{First}'s discharge summary is ready in your CareLink portal." (Tasks 14, 15).
        await notices.SummaryReadyAsync(patientId, ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(admissionId, ct);
    }

    /// <summary>
    /// Ruling (Task 10 I1): the medical director takes over an unsigned draft (the author was away, left, or can't finish it). The author
    /// becomes the director; the text is kept; audited with ids only. 403 for anyone but the director (support's view included); 404 for
    /// an unknown admission or no draft; 409 for a signed summary.
    /// </summary>
    public async Task<DischargeSummaryDto> TakeOverSummaryAsync(Guid admissionId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        if (caller.MemberRole != MemberRole.Owner) throw new ForbiddenException();
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForTeamAsync(caller, admissionId, now, ct);
        var summary = await db.DischargeSummaries.FirstOrDefaultAsync(s => s.AdmissionId == admissionId, ct) ?? throw new NotFoundException("Discharge summary");
        ConsultationService.EnsureDraft(summary.Status);
        if (summary.DoctorUserId != caller.UserId)
        {
            var from = summary.DoctorUserId;
            summary.DoctorUserId = caller.UserId;
            summary.UpdatedAt = now;
            db.AuditLogs.Add(Audit(caller, "care.note_taken_over", nameof(DischargeSummary), summary.Id,
                new { noteKind = nameof(NoteKind.Discharge), admissionId, patientId, fromDoctorUserId = from, toDoctorUserId = caller.UserId }, now));
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(admissionId, ct);
    }

    /// <summary>
    /// Discards an unsigned draft (Task 10 I1): its author or the director. 403 clinical / doctors only / care team; 404 no draft; 409 a
    /// signed summary (final; corrections are addenda); 403 "Only the doctor who wrote this note can change it." for another doctor.
    /// Audited with ids only.
    /// </summary>
    public async Task DiscardSummaryAsync(Guid admissionId, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, SummaryDoctorsOnly);
        var now = Now();
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await LockForTeamAsync(caller, admissionId, now, ct);
        var summary = await db.DischargeSummaries.FirstOrDefaultAsync(s => s.AdmissionId == admissionId, ct) ?? throw new NotFoundException("Discharge summary");
        ConsultationService.EnsureDraft(summary.Status);
        if (summary.DoctorUserId != caller.UserId && caller.MemberRole != MemberRole.Owner) throw new ForbiddenException(ConsultationService.AuthorOnly);
        db.DischargeSummaries.Remove(summary);
        db.AuditLogs.Add(Audit(caller, "care.note_discarded", nameof(DischargeSummary), summary.Id,
            new { noteKind = nameof(NoteKind.Discharge), admissionId, patientId, authorUserId = summary.DoctorUserId }, now));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    // ---------- Discharge ----------

    /// <summary>
    /// Discharges the patient: doctors and the director (403 "Only doctors can discharge patients."). In order: 400 <c>type</c>,
    /// <c>amaReason</c> (against advice), <c>deathTime</c> and <c>deathCause</c> (died; the time not in the future); 404; under the
    /// patient's admission lock: 409 "This patient has been discharged."; 403 "This patient isn't on your ward." without a live care
    /// assignment for this admission (the director is exempt); 400 <c>deathTime</c> before the admission; 409 "Sign the discharge summary
    /// before discharging the patient." (home, transferred out); 409 "Sign or discard the unsigned ward-round notes first." with
    /// <c>unsignedWardRounds</c> unless <see cref="DischargeRequest.AcknowledgeUnsignedNotes"/> (then recorded and audited).
    /// </summary>
    public async Task<DischargedDto> DischargeAsync(Guid admissionId, DischargeRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly);
        var now = Now();
        if (r.Type is not { } type || !Enum.IsDefined(type)) throw BadRequestException.Field("type", "Choose how the patient is leaving.");
        string? amaReason = null, deathCause = null;
        DateTimeOffset? deathTime = null;
        if (type == DischargeType.AgainstAdvice)
            amaReason = CareOrders.Required("amaReason", r.AmaReason, MaxAmaReasonLength, "Write why the patient is leaving against advice.");
        if (type == DischargeType.Died)
        {
            if (r.DeathTime is not { } died) throw BadRequestException.Field("deathTime", "Enter the time of death.");
            if (died > now.AddMinutes(5)) throw BadRequestException.Field("deathTime", "The time of death can't be in the future.");
            deathTime = died.ToUniversalTime();
            deathTime = deathTime.Value.AddTicks(-(deathTime.Value.Ticks % 10));
            deathCause = CareOrders.Required("deathCause", r.DeathCause, MaxDeathCauseLength, "Write the cause of death.");
        }

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var patientId = await PatientOfAsync(admissionId, ct);
        await db.LockAsync(AdmissionService.PatientLockKey(TenantId, patientId), ct);
        var admission = await db.Admissions.FirstAsync(a => a.Id == admissionId, ct);
        if (admission.Status != AdmissionStatus.Admitted) throw new ConflictException(AdmissionService.Discharged, "admission_discharged");
        await EnsureTeamAsync(caller, admissionId, now, ct);
        if (deathTime < admission.AdmittedAt) throw BadRequestException.Field("deathTime", "The time of death can't be before the admission.");
        if (type is DischargeType.Home or DischargeType.TransferredOut
            && !await db.DischargeSummaries.AnyAsync(s => s.AdmissionId == admissionId && s.Status == NoteStatus.Signed, ct))
            throw new ConflictException(SummaryUnsigned, "summary_unsigned");

        var drafts = await db.WardRounds.AsNoTracking().Where(w => w.AdmissionId == admissionId && w.Status == NoteStatus.Draft)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).Select(w => new { w.Id, w.DoctorUserId, w.CreatedAt }).ToListAsync(ct);
        if (drafts.Count > 0 && !r.AcknowledgeUnsignedNotes)
        {
            var names = await ConsultationService.NamesAsync(db, drafts.Select(d => d.DoctorUserId), ct);
            throw new ConflictException(UnsignedRounds, "unsigned_ward_rounds")
            {
                Extensions = new Dictionary<string, object?>
                {
                    ["unsignedWardRounds"] = drafts.Select(d => new UnsignedRoundDto(d.Id, d.DoctorUserId, names.GetValueOrDefault(d.DoctorUserId, ""), d.CreatedAt)).ToList(),
                },
            };
        }

        admission.Status = AdmissionStatus.Discharged;
        admission.DischargeType = type;
        admission.DischargedAt = now;
        admission.DischargedByUserId = caller.UserId;
        admission.AmaReason = amaReason is null ? null : text.Protect(amaReason);
        admission.DeathTime = deathTime;
        admission.DeathCause = deathCause is null ? null : text.Protect(deathCause);
        admission.UnsignedRoundsAtDischarge = drafts.Count;
        admission.UpdatedAt = now;
        await AdmissionService.EndAssignmentsAtDischargeAsync(db, admissionId, now, ct);
        if (admission.BedId is { } bedId)
        {
            // The bed is freed for cleaning; the admission keeps its last bed for the record (the per-bed index covers Admitted only).
            var bed = await db.Beds.FirstAsync(b => b.Id == bedId, ct);
            bed.Status = BedStatus.Cleaning;
        }
        if (type == DischargeType.Died)
        {
            var patient = await db.Patients.FirstAsync(p => p.Id == patientId, ct);
            patient.Status = PatientStatus.Deceased;
            // Carry (Task 10 → 14): every appointment still booked is cancelled, and nobody is told: no message reaches the patient or
            // their portal users (CareNotices also never writes to a deceased patient's portal users). One conditional update, so a
            // concurrent move keeps its new time but stays cancelled.
            var cancelled = await db.Appointments.Where(a => a.PatientId == patientId && a.Status == AppointmentStatus.Booked)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.Cancelled)
                    .SetProperty(a => a.CancelledAt, (DateTimeOffset?)now).SetProperty(a => a.UpdatedAt, (DateTimeOffset?)now), ct);
            // Patient isn't [Audited]: an explicit row with ids only (never the cause).
            db.AuditLogs.Add(Audit(caller, "care.patient_deceased", nameof(Patient), patientId, new { admissionId, cancelledAppointments = cancelled }, now));
        }
        if (drafts.Count > 0)
            db.AuditLogs.Add(Audit(caller, "care.unsigned_notes_acknowledged", nameof(Admission), admissionId,
                new { patientId, wardRoundIds = drafts.Select(d => d.Id).ToList() }, now));
        db.AuditLogs.Add(Audit(caller, "care.discharged", nameof(Admission), admissionId,
            new { patientId, type = type.ToString(), bedId = admission.BedId, unsignedWardRounds = drafts.Count }, now));
        await db.SaveChangesAsync(ct);

        var stopped = await MedicationChart.StopFutureDosesAsync(db, admissionId, now, ct);
        var missed = await MedicationChart.MissPastDosesAsync(db, admissionId, now, ct);
        // Carry (Task 10): the outstanding bed days go on the bill before it is final. The admission day is charged, the discharge day is
        // not, and a stay that starts and ends today is charged its one day. Still under the patient lock; the helper takes the bill lock.
        await billing.OpenBillForAdmissionAsync(patientId, admissionId, ct);
        await BedCharges.ChargeOutstandingAsync(db, admissionId, CareCalendar.Today(now).AddDays(-1), includeSameDayStay: true, ct);
        var bill = await billing.FinaliseAdmissionBillAsync(patientId, admissionId, now, ct);
        // Ruling (Task 14): money left on the final bill is due; the portal users hear the bill number and balance (never its lines).
        // A Died discharge tells no one (the patient is Deceased, so CareNotices writes nothing).
        if (type != DischargeType.Died)
        {
            var due = await db.PatientBills.AsNoTracking().Where(b => b.Id == bill.Id).Select(b => new { b.Number, b.BalanceMinor }).FirstAsync(ct);
            await notices.BillDueAsync(patientId, due.Number, due.BalanceMinor, ct);
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);

        var summary = (await AdmissionService.SummariesAsync(db, db.Admissions.AsNoTracking().Where(a => a.Id == admissionId), clock, ct)).Single();
        var balance = await db.PatientBills.AsNoTracking().Where(b => b.Id == bill.Id).Select(b => b.BalanceMinor).FirstAsync(ct);
        return new DischargedDto(summary, type, now, bill.Id, balance, stopped, missed, drafts.Count);
    }

    // ---------- Admissions list ----------

    /// <summary>
    /// Current admissions and those discharged in the last 30 days (<paramref name="status"/> narrows it), newest admission first, at
    /// most 500: leadership and clinical staff (403 otherwise). Demographics, bed and status for everyone allowed; the discharge type and
    /// the summary's status only for clinical staff. Not a record open (nothing clinical).
    /// </summary>
    public async Task<IReadOnlyList<AdmissionListItemDto>> ListAsync(AdmissionStatus? status, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        // Ruling (Task 10 I3): the desk rule — front desk, leadership and clinical staff; the cashier is refused.
        PatientService.EnsureDesk(caller);
        var since = clock.GetUtcNow() - RecentWindow;
        var query = db.Admissions.AsNoTracking()
            .Where(a => a.Status == AdmissionStatus.Admitted || a.DischargedAt >= since)
            .Where(a => status == null || a.Status == status)
            .OrderByDescending(a => a.AdmittedAt).ThenBy(a => a.Id).Take(MaxListed);
        var summaries = await AdmissionService.SummariesAsync(db, query, clock, ct);
        var ids = summaries.Select(s => s.Id).ToList();
        var extra = await db.Admissions.AsNoTracking().Where(a => ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id, a.DischargedAt, a.DischargeType,
                SummaryStatus = db.DischargeSummaries.Where(s => s.AdmissionId == a.Id).Select(s => (NoteStatus?)s.Status).FirstOrDefault(),
            })
            .ToDictionaryAsync(a => a.Id, ct);
        var clinical = caller.IsClinical;
        return summaries.Select(s =>
        {
            var e = extra[s.Id];
            return new AdmissionListItemDto(s.Id, s.PatientId, s.PatientNumber, s.PatientName, s.Sex, s.AgeYears, s.Status, s.BedId, s.BedLabel,
                s.WardId, s.WardName, s.AdmittingDoctorUserId, s.DoctorName, s.AdmittedAt, s.BillId, e.DischargedAt,
                clinical ? e.DischargeType : null, clinical ? e.SummaryStatus : null);
        }).ToList();
    }

    // ---------- PDF ----------

    /// <summary>
    /// The signed discharge summary as a PDF on the clinic's letterhead. A clinical read: 403 without clinical access before anything is
    /// looked up, 404 for an unknown admission, then <see cref="RecordGuard.OpenAsync"/> (area pdf), then 409 "The discharge summary
    /// isn't signed yet." (code <c>summary_unsigned</c>) when there is no signed summary.
    /// </summary>
    public async Task<(byte[] Pdf, string FileName)> PdfAsync(Guid admissionId, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await PatientOfAsync(admissionId, ct);
        await guard.OpenAsync(patientId, RecordGuard.Areas.Pdf, reason, ct);

        var summary = (await ReadManyAsync(db, text, db.DischargeSummaries.AsNoTracking().Where(s => s.AdmissionId == admissionId), ct)).SingleOrDefault();
        if (summary is not { Status: NoteStatus.Signed, SignedAt: { } signedAt }) throw new ConflictException(NoSignedSummary, "summary_unsigned");
        var admission = (await AdmissionService.ReadManyAsync(db, text, db.Admissions.AsNoTracking().Where(a => a.Id == admissionId), clock, ct)).Single();
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);
        var clinic = await db.ClinicProfiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");
        var staff = await db.CareStaffProfiles.AsNoTracking().Where(s => s.UserId == summary.DoctorUserId)
            .Select(s => new { s.Title, s.LicenceNumber }).FirstOrDefaultAsync(ct);

        static string Day(DateTimeOffset at) => CareCalendar.Today(at).ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var data = new DischargePdfData(
            clinic.Name, clinic.Address, clinic.Phone, await PrescriptionService.LogoAsync(storage, clinic.LogoKey, ct),
            admission.PatientName, admission.PatientNumber, admission.Sex.ToString(), admission.AgeYears,
            Day(admission.AdmittedAt), admission.DischargedAt is { } left ? Day(left) : null, admission.WardName, Describe(admission.DischargeType),
            admission.DeathTime is { } died ? died.ToOffset(CareCalendar.LagosOffset).ToString("d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture) : null,
            admission.DeathCause, summary.Diagnoses ?? "", summary.Treatment ?? "", summary.Condition ?? "", summary.TakeHome ?? "",
            summary.FollowUp?.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), summary.DoctorName, staff?.Title, staff?.LicenceNumber,
            Day(signedAt), !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct));
        var stamp = CareCalendar.Today(admission.DischargedAt ?? signedAt);
        return (await pdf.DischargeAsync(data, ct), $"discharge-{patient.Number}-{stamp:yyyyMMdd}.pdf");
    }

    /// <summary>How the patient left, in words for the PDF.</summary>
    public static string? Describe(DischargeType? type) => type switch
    {
        DischargeType.Home => "Discharged home",
        DischargeType.AgainstAdvice => "Left against medical advice",
        DischargeType.TransferredOut => "Transferred to another facility",
        DischargeType.Died => "Died",
        _ => null,
    };

    // ---------- Shared reads ----------

    /// <summary>Discharge summaries as DTOs, newest first, each with its addenda (oldest first), decrypted. No access check: the caller has opened the record.</summary>
    internal static async Task<List<DischargeSummaryDto>> ReadManyAsync(IAppDbContext db, ClinicalText text, IQueryable<DischargeSummary> query, CancellationToken ct)
    {
        var rows = await query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var ids = rows.Select(s => s.Id).ToList();
        var addenda = await db.Addenda.AsNoTracking().Where(a => a.NoteKind == NoteKind.Discharge && ids.Contains(a.NoteId))
            .OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync(ct);
        var names = await ConsultationService.NamesAsync(db, rows.Select(s => s.DoctorUserId).Concat(addenda.Select(a => a.AuthorUserId)), ct);
        return rows.Select(s => new DischargeSummaryDto(s.Id, s.AdmissionId, s.PatientId, s.DoctorUserId, names.GetValueOrDefault(s.DoctorUserId, ""),
                text.Read(s.Diagnoses), text.Read(s.Treatment), text.Read(s.Condition), text.Read(s.TakeHome), s.FollowUp, s.Status, s.SignedAt,
                s.CreatedAt, s.UpdatedAt, addenda.Where(a => a.NoteId == s.Id).Select(a => ConsultationService.ToDto(a, names, text)).ToList()))
            .ToList();
    }

    // ---------- Internals ----------

    private async Task<DischargeSummaryDto> ReadAsync(Guid admissionId, CancellationToken ct) =>
        (await ReadManyAsync(db, text, db.DischargeSummaries.AsNoTracking().Where(s => s.AdmissionId == admissionId), ct)).Single();

    private async Task<Guid> PatientOfAsync(Guid admissionId, CancellationToken ct) =>
        await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => (Guid?)a.PatientId).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Admission");

    /// <summary>404; the patient's admission lock (in the caller's transaction); then the care-team check. Returns the patient.</summary>
    private async Task<Guid> LockForTeamAsync(CareCaller caller, Guid admissionId, DateTimeOffset now, CancellationToken ct)
    {
        var patientId = await PatientOfAsync(admissionId, ct);
        await db.LockAsync(AdmissionService.PatientLockKey(TenantId, patientId), ct);
        await EnsureTeamAsync(caller, admissionId, now, ct);
        return patientId;
    }

    /// <summary>403 "This patient isn't on your ward." unless the caller is the director or has a live care assignment for this admission.</summary>
    private async Task EnsureTeamAsync(CareCaller caller, Guid admissionId, DateTimeOffset now, CancellationToken ct)
    {
        if (caller.MemberRole == MemberRole.Owner) return;
        if (!await db.CareAssignments.AnyAsync(a => a.AdmissionId == admissionId && a.StaffUserId == caller.UserId && (a.EndedAt == null || a.EndedAt > now), ct))
            throw new ForbiddenException(WardCareService.NotOnWard);
    }

    private AuditLog Audit(CareCaller caller, string action, string entityType, Guid entityId, object after, DateTimeOffset now) => new()
    {
        TenantId = TenantId, ActorId = caller.UserId, Action = action, EntityType = entityType, EntityId = entityId.ToString(),
        AfterJson = JsonSerializer.Serialize(after), Ip = user.IpAddress, CreatedAt = now,
    };

    /// <summary>Now, truncated to microseconds (Postgres precision), so an answer's time is the time stored.</summary>
    private DateTimeOffset Now()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }
}
