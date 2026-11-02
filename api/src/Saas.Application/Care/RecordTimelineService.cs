using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

/// <summary>A visit on the record timeline: when, where it got to, who saw the patient, and its note and bill.</summary>
public sealed record RecordVisitDto(
    Guid Id, DateTimeOffset ArrivedAt, VisitStage Stage, Urgency Urgency, Guid? DoctorUserId, string? DoctorName, DateTimeOffset? DoneAt,
    Guid? ConsultationId, Guid? BillId);

/// <summary>
/// A patient's whole clinical record, for clinical staff: the patient with clinical facts, then visits, consultations (with diagnoses
/// and addenda), the vital-signs series (oldest first, for the chart), all diagnoses and bills; lists newest first otherwise.
/// Prescriptions (newest first, with their drugs) and tests with their results (newest first) come from Task 7; admissions (newest
/// first, with reason, admitting diagnosis and how the patient left) from Task 8; ward care from Task 9 (nursing notes and ward rounds
/// newest first, the medication chart summarised per admission drug); discharge summaries (with addenda) from Task 10.
/// </summary>
public sealed record PatientRecordDto(
    PatientDto Patient,
    IReadOnlyList<RecordVisitDto> Visits,
    IReadOnlyList<ConsultationDto> Consultations,
    IReadOnlyList<VitalSignsDto> Vitals,
    IReadOnlyList<DiagnosisDto> Diagnoses,
    IReadOnlyList<PrescriptionDto> Prescriptions,
    IReadOnlyList<TestRequestDto> Results,
    IReadOnlyList<AdmissionDto> Admissions,
    IReadOnlyList<NursingNoteDto> NursingNotes,
    IReadOnlyList<WardRoundDto> WardRounds,
    IReadOnlyList<MedicationSummaryDto> MedicationSummary,
    IReadOnlyList<DischargeSummaryDto> DischargeSummaries,
    IReadOnlyList<BillSummaryDto> Bills);

/// <summary>
/// The patient record page. Opens the record through <see cref="RecordGuard"/> (area record) first: clinical staff only (403
/// "Only doctors and nurses can see clinical notes."), logged, and an unrelated caller needs the <c>X-Care-Reason</c> header.
/// </summary>
public sealed class RecordTimelineService(IAppDbContext db, RecordGuard guard, PatientService patients, ClinicalText text, TimeProvider clock)
{
    public async Task<PatientRecordDto> GetAsync(Guid patientId, string? reason, CancellationToken ct)
    {
        await guard.OpenAsync(patientId, RecordGuard.Areas.Record, reason, ct);

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct) ?? throw new NotFoundException("Patient");
        var visits = await db.Visits.AsNoTracking().Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.ArrivedAt).ThenByDescending(v => v.Id)
            .Select(v => new
            {
                Visit = v,
                ConsultationId = db.Consultations.Where(c => c.VisitId == v.Id).Select(c => (Guid?)c.Id).FirstOrDefault(),
                BillId = db.PatientBills.Where(b => b.VisitId == v.Id).Select(b => (Guid?)b.Id).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var consultations = await ConsultationService.ReadManyAsync(db, text, db.Consultations.AsNoTracking().Where(c => c.PatientId == patientId), ct);
        var vitals = await db.VitalSigns.AsNoTracking().Where(v => v.PatientId == patientId).OrderBy(v => v.TakenAt).ThenBy(v => v.Id).ToListAsync(ct);
        var diagnoses = await db.Diagnoses.AsNoTracking().Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Primary).ThenBy(d => d.Id).ToListAsync(ct);
        var bills = await db.PatientBills.AsNoTracking().Where(b => b.PatientId == patientId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BillSummaryDto(b.Id, b.Number, b.PatientId, patient.Number, patient.FirstName + " " + patient.LastName, b.VisitId,
                b.AdmissionId, b.Status, b.TotalMinor, b.PaidMinor, b.BalanceMinor, b.CreatedAt, b.FinalisedAt, b.CreditMinor, b.NeedsReview))
            .ToListAsync(ct);
        var prescriptions = await PrescriptionService.ReadManyAsync(db, text, db.Prescriptions.AsNoTracking().Where(p => p.PatientId == patientId), ct);
        var tests = await TestService.ReadManyAsync(db, text, db.TestRequests.AsNoTracking().Where(t => t.PatientId == patientId), ct);
        var names = await ConsultationService.NamesAsync(db,
            visits.Select(v => v.Visit.DoctorUserId).OfType<Guid>().Concat(vitals.Select(v => v.TakenBy)), ct);

        return new PatientRecordDto(
            patients.ToDto(patient, clinical: true),
            visits.Select(v => new RecordVisitDto(v.Visit.Id, v.Visit.ArrivedAt, v.Visit.Stage, v.Visit.Urgency, v.Visit.DoctorUserId,
                v.Visit.DoctorUserId is { } d ? names.GetValueOrDefault(d) : null, v.Visit.DoneAt, v.ConsultationId, v.BillId)).ToList(),
            consultations,
            vitals.Select(v => VisitService.ToDto(v, names.GetValueOrDefault(v.TakenBy, ""))).ToList(),
            diagnoses.Select(d => ConsultationService.ToDto(d, text)).ToList(),
            prescriptions, tests,
            Admissions: await AdmissionService.ReadManyAsync(db, text, db.Admissions.AsNoTracking().Where(a => a.PatientId == patientId), clock, ct),
            NursingNotes: await WardCareService.ReadNursingNotesAsync(db, text, db.NursingNotes.AsNoTracking().Where(n => n.PatientId == patientId), ct),
            WardRounds: await WardCareService.ReadRoundsAsync(db, text, db.WardRounds.AsNoTracking().Where(w => w.PatientId == patientId), ct),
            MedicationSummary: await WardCareService.MedicationSummaryAsync(db, text, patientId, ct),
            DischargeSummaries: await DischargeService.ReadManyAsync(db, text, db.DischargeSummaries.AsNoTracking().Where(s => s.PatientId == patientId), ct),
            bills);
    }
}
