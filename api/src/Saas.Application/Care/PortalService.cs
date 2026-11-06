using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

// ---------- The record, as the patient sees it ----------
// Portal DTOs are built here on purpose rather than reusing the staff DTOs: they carry no staff ids, no draft notes, no review flags,
// no allergy overrides, no reasons written by staff (emergency reasons, bill-line and reversal reasons, against-advice reasons) and no
// ward-care charts (nursing notes, the medication chart, fluids).

public sealed record PortalProfileDto(
    Guid Id, string Number, string FirstName, string? MiddleName, string LastName, PatientSex Sex, DateOnly DateOfBirth, string? Phone,
    string? Email, string? Address, string? NextOfKinName, string? NextOfKinPhone, string? NextOfKinRelationship, PatientStatus Status,
    string? BloodGroup, string? Genotype, IReadOnlyList<string> Allergies, IReadOnlyList<string> Conditions);

public sealed record PortalDiagnosisDto(string Text, string? Code, bool Primary);

public sealed record PortalAddendumDto(string AuthorName, string Text, DateTimeOffset At);

/// <summary>A signed consultation note with its diagnoses and addenda.</summary>
public sealed record PortalConsultationDto(
    Guid Id, string DoctorName, DateTimeOffset SignedAt, string? Complaint, string? History, string? Examination, string? Plan,
    IReadOnlyList<PortalDiagnosisDto> Diagnoses, IReadOnlyList<PortalAddendumDto> Addenda);

public sealed record PortalPrescriptionItemDto(
    string Drug, string? Strength, string Dose, DoseFrequency Frequency, int? DurationDays, int? Quantity, string? Route, string? Instructions);

public sealed record PortalPrescriptionDto(Guid Id, string DoctorName, DateTimeOffset CreatedAt, IReadOnlyList<PortalPrescriptionItemDto> Items);

/// <summary>A test with its result. Results on file are seen at the clinic (no file link in the portal at this stage).</summary>
public sealed record PortalResultDto(Guid Id, TestKind Kind, string Description, string? ResultText, bool HasFile, DateTimeOffset? ResultedAt);

public sealed record PortalVitalsDto(
    DateTimeOffset TakenAt, decimal? TemperatureC, int? Systolic, int? Diastolic, int? Pulse, int? RespRate, int? SpO2, decimal? WeightKg,
    decimal? HeightCm, decimal? Bmi, IReadOnlyList<string> Flags);

public sealed record PortalAdmissionDto(
    Guid Id, DateTimeOffset AdmittedAt, string? WardName, string? DoctorName, string Reason, string AdmittingDiagnosis, AdmissionStatus Status,
    DischargeType? DischargeType, DateTimeOffset? DischargedAt);

public sealed record PortalWardRoundDto(Guid Id, Guid AdmissionId, string DoctorName, DateTimeOffset SignedAt, string? Note, IReadOnlyList<PortalAddendumDto> Addenda);

public sealed record PortalDischargeSummaryDto(
    Guid Id, Guid AdmissionId, string DoctorName, DateTimeOffset SignedAt, string? Diagnoses, string? Treatment, string? Condition, string? TakeHome,
    DateOnly? FollowUp, IReadOnlyList<PortalAddendumDto> Addenda);

/// <summary>The patient's own record: signed notes and their addenda only (never a draft), prescriptions, results, vitals and admissions.</summary>
public sealed record PortalRecordDto(
    PortalProfileDto Patient,
    IReadOnlyList<PortalConsultationDto> Consultations,
    IReadOnlyList<PortalPrescriptionDto> Prescriptions,
    IReadOnlyList<PortalResultDto> Results,
    IReadOnlyList<PortalVitalsDto> Vitals,
    IReadOnlyList<PortalAdmissionDto> Admissions,
    IReadOnlyList<PortalWardRoundDto> WardRounds,
    IReadOnlyList<PortalDischargeSummaryDto> DischargeSummaries);

/// <summary>
/// One entry of "who viewed my record": the staff member's name and clinic role, what they opened, and when. A portal read (final review
/// C1) has <see cref="Portal"/> set, the account's short name and its relationship as the role; <see cref="Who"/> reads
/// "Nkem W. (parent), patient portal" (staff: "{name}, {role}").
/// </summary>
public sealed record PortalAccessDto(string StaffName, string Role, string What, DateTimeOffset At, bool Portal, string Who);

// ---------- Bills, payments and appointments ----------

public sealed record PortalBillLineDto(string Description, int Quantity, long UnitPriceMinor, long AmountMinor);

/// <summary>A payment with a receipt (succeeded, or reversed later). Download the receipt at <c>.../receipts/{id}.pdf</c>.</summary>
public sealed record PortalPaymentDto(Guid Id, long AmountMinor, PayMethod Method, string? ReceiptNumber, DateTimeOffset PaidAt, CarePaymentStatus Status);

public sealed record PortalBillDto(
    Guid Id, string Number, BillStatus Status, long TotalMinor, long AdjustedMinor, long PaidMinor, long BalanceMinor, long CreditMinor,
    DateTimeOffset CreatedAt, DateTimeOffset? FinalisedAt, IReadOnlyList<PortalBillLineDto> Lines, IReadOnlyList<PortalPaymentDto> Payments);

/// <summary><c>AmountMinor</c>: the balance when left out, or a part of at least ₦1,000.</summary>
public sealed record PortalPayRequest(long? AmountMinor);

public sealed record PortalBookRequest(Guid? DoctorUserId, DateTimeOffset? Start, string? Reason);

/// <summary>An appointment as the patient sees it. <c>CanCancel</c>: booked and at least 2 hours away.</summary>
public sealed record PortalAppointmentDto(
    Guid Id, Guid DoctorUserId, string DoctorName, DateTimeOffset Start, DateTimeOffset End, string? Reason, AppointmentStatus Status,
    AppointmentSource Source, bool CanCancel);

/// <summary>The patient's appointments, with the label the booking reason is shown under ("Reason for visit (no medical details)").</summary>
public sealed record PortalAppointmentsDto(string ReasonLabel, IReadOnlyList<PortalAppointmentDto> Appointments);

/// <summary>The patient's own contact details. Fields left out are cleared, as on the patient screen.</summary>
public sealed record PortalContactRequest(string? Phone, string? Email, string? Address);

public sealed record PortalContactDto(string? Phone, string? Email, string? Address);

public sealed record PortalNoticeDto(Guid Id, string Kind, string Title, string Body, string Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>
/// The patient portal's routes (Task 15). Every method asks <see cref="PatientPortalAccess.RequirePatientAsync"/> first (404 unlinked, then
/// 403 for a suspended clinic, then the patient's clinic as the tenant), and only then calls the shared services, which trust the tenant:
/// <see cref="AppointmentService"/> for slots, bookings (source Portal) and cancels, <see cref="OnlinePaymentService"/> for checkout and
/// verify, <see cref="PaymentService"/> for receipts. Final review C1 (ruling): reading the record or the bills (whose lines name drugs and
/// tests) writes a <see cref="RecordAccess"/> row marked as a portal read, so a staff member who linked a personal account shows up on both
/// access logs.
/// </summary>
public sealed class PortalService(
    IAppDbContext db, ITenantContext tenant, ICurrentUser user, PatientPortalAccess portal, ClinicalText text, AppointmentService appointments,
    OnlinePaymentService online, PaymentService payments, TimeProvider clock)
{
    /// <summary>What each access-log area reads as for the patient. Every area the staff log writes has one (triage included).</summary>
    public static readonly IReadOnlyDictionary<string, string> AreaLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RecordGuard.Areas.Record] = "Your record",
        [RecordGuard.Areas.Consultation] = "A consultation note",
        [RecordGuard.Areas.Admission] = "An admission",
        [RecordGuard.Areas.Prescription] = "Prescriptions",
        [RecordGuard.Areas.Result] = "Test results",
        [RecordGuard.Areas.Discharge] = "A discharge summary",
        [RecordGuard.Areas.Pdf] = "A printed document",
        [RecordGuard.Areas.File] = "A result file",
        [RecordGuard.Areas.Triage] = "Vital signs at triage",
        [RecordGuard.Areas.NursingNotes] = "Nursing notes",
        [RecordGuard.Areas.Fluids] = "The fluid chart",
        [RecordGuard.Areas.WardRounds] = "Ward rounds",
        [RecordGuard.Areas.MedicationChart] = "The medication chart",
        [RecordGuard.Areas.Bills] = "Your bills",
    };

    /// <summary>The patient-facing label of an access-log area; an area added later without a label reads as the record.</summary>
    public static string AreaLabel(string area) => AreaLabels.GetValueOrDefault(area, AreaLabels[RecordGuard.Areas.Record]);

    // ---------- Record and access log ----------

    public async Task<PortalRecordDto> RecordAsync(Guid patientId, CancellationToken ct)
    {
        var who = await portal.RequirePatientAsync(patientId, ct);
        await RecordGuard.LogPortalReadAsync(db, who, portal.CustomerUserId(), RecordGuard.Areas.Record, clock.GetUtcNow(), ct);
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);

        // Signed notes only; a draft (and its diagnoses) never leaves the clinic.
        var consultations = await ConsultationService.ReadManyAsync(db, text,
            db.Consultations.AsNoTracking().Where(c => c.PatientId == patientId && c.Status == NoteStatus.Signed), ct);
        // Prescriptions from an admission, or from a signed consultation.
        var prescriptions = await PrescriptionService.ReadManyAsync(db, text, db.Prescriptions.AsNoTracking().Where(p => p.PatientId == patientId
            && (p.ConsultationId == null || db.Consultations.Any(c => c.Id == p.ConsultationId && c.Status == NoteStatus.Signed))), ct);
        // Results once a doctor has reviewed them (post-build user decision; the patient was told "a new result is ready" at the review).
        var tests = await TestService.ReadManyAsync(db, text,
            db.TestRequests.AsNoTracking().Where(t => t.PatientId == patientId && t.Status == TestStatus.Resulted && t.ReviewedAt != null), ct);
        var vitals = await db.VitalSigns.AsNoTracking().Where(v => v.PatientId == patientId).OrderBy(v => v.TakenAt).ThenBy(v => v.Id).ToListAsync(ct);
        var admissions = await AdmissionService.ReadManyAsync(db, text, db.Admissions.AsNoTracking().Where(a => a.PatientId == patientId), clock, ct);
        var rounds = await WardCareService.ReadRoundsAsync(db, text,
            db.WardRounds.AsNoTracking().Where(w => w.PatientId == patientId && w.Status == NoteStatus.Signed), ct);
        var summaries = await DischargeService.ReadManyAsync(db, text,
            db.DischargeSummaries.AsNoTracking().Where(s => s.PatientId == patientId && s.Status == NoteStatus.Signed), ct);

        return new PortalRecordDto(
            new PortalProfileDto(patient.Id, patient.Number, patient.FirstName, patient.MiddleName, patient.LastName, patient.Sex, patient.DateOfBirth,
                patient.Phone, patient.Email, patient.Address, patient.NextOfKinName, patient.NextOfKinPhone, patient.NextOfKinRelationship, patient.Status,
                patient.BloodGroup, patient.Genotype, text.ReadAll(patient.Allergies), text.ReadAll(patient.Conditions)),
            consultations.Where(c => c.Status == NoteStatus.Signed && c.SignedAt is not null).OrderByDescending(c => c.SignedAt)
                .Select(c => new PortalConsultationDto(c.Id, c.DoctorName, c.SignedAt!.Value, c.Complaint, c.History, c.Examination, c.Plan,
                    c.Diagnoses.Select(d => new PortalDiagnosisDto(d.Text, d.Code, d.Primary)).ToList(), Addenda(c.Addenda))).ToList(),
            prescriptions.Select(p => new PortalPrescriptionDto(p.Id, p.DoctorName, p.CreatedAt,
                p.Items.Select(i => new PortalPrescriptionItemDto(i.Drug, i.Strength, i.Dose, i.Frequency, i.DurationDays, i.Quantity, i.Route, i.Instructions)).ToList()))
                .ToList(),
            tests.Select(t => new PortalResultDto(t.Id, t.Kind, t.Description, t.ResultText, t.HasFile, t.ResultedAt)).ToList(),
            vitals.Select(v => new PortalVitalsDto(v.TakenAt, v.TemperatureC, v.Systolic, v.Diastolic, v.Pulse, v.RespRate, v.SpO2, v.WeightKg, v.HeightCm,
                v.Bmi, v.Flags)).ToList(),
            admissions.Select(a => new PortalAdmissionDto(a.Id, a.AdmittedAt, a.WardName, a.DoctorName, a.Reason, a.AdmittingDiagnosis, a.Status,
                a.DischargeType, a.DischargedAt)).ToList(),
            rounds.Where(r => r.Status == NoteStatus.Signed && r.SignedAt is not null)
                .Select(r => new PortalWardRoundDto(r.Id, r.AdmissionId, r.DoctorName, r.SignedAt!.Value, r.Note, Addenda(r.Addenda))).ToList(),
            summaries.Where(s => s.Status == NoteStatus.Signed && s.SignedAt is not null)
                .Select(s => new PortalDischargeSummaryDto(s.Id, s.AdmissionId, s.DoctorName, s.SignedAt!.Value, s.Diagnoses, s.Treatment, s.Condition,
                    s.TakeHome, s.FollowUp, Addenda(s.Addenda))).ToList());
    }

    private static List<PortalAddendumDto> Addenda(IEnumerable<AddendumDto> addenda) =>
        addenda.Select(a => new PortalAddendumDto(a.AuthorName, a.Text, a.At)).ToList();

    /// <summary>
    /// Who opened the patient's record: the staff member's name and clinic role (or a portal account's short name and relationship), what
    /// they opened and when. Never the reason or an id.
    /// </summary>
    public async Task<IReadOnlyList<PortalAccessDto>> AccessLogAsync(Guid patientId, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        var rows = await db.RecordAccesses.AsNoTracking().Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(500)
            .Select(a => new { StaffName = db.Users.Where(u => u.Id == a.StaffUserId).Select(u => u.FullName).FirstOrDefault(), a.Role, a.Area, a.At, a.Portal })
            .ToListAsync(ct);
        return rows.Select(a =>
            {
                var name = a.Portal ? RecordGuard.PortalName(a.StaffName) : a.StaffName ?? "";
                return new PortalAccessDto(name, a.Role, AreaLabel(a.Area), a.At, a.Portal, RecordGuard.Who(name, a.Role, a.Portal));
            })
            .ToList();
    }

    // ---------- Bills and payments ----------

    /// <summary>The patient's bills with their item names (the patient's own); logged as a portal read (final review C1).</summary>
    public async Task<IReadOnlyList<PortalBillDto>> BillsAsync(Guid patientId, CancellationToken ct)
    {
        var who = await portal.RequirePatientAsync(patientId, ct);
        await RecordGuard.LogPortalReadAsync(db, who, portal.CustomerUserId(), RecordGuard.Areas.Bills, clock.GetUtcNow(), ct);
        var bills = await db.PatientBills.AsNoTracking().Where(b => b.PatientId == patientId).OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync(ct);
        var ids = bills.Select(b => b.Id).ToList();
        var lines = (await db.BillLines.AsNoTracking().Where(l => ids.Contains(l.BillId) && l.RemovedAt == null).OrderBy(l => l.CreatedAt).ToListAsync(ct))
            .ToLookup(l => l.BillId);
        var paid = (await db.CarePayments.AsNoTracking()
                .Where(p => ids.Contains(p.BillId) && p.ReceiptNumber != null && (p.Status == CarePaymentStatus.Succeeded || p.Status == CarePaymentStatus.Reversed))
                .OrderBy(p => p.PaidAt).ToListAsync(ct))
            .ToLookup(p => p.BillId);
        return bills.Select(b => new PortalBillDto(b.Id, b.Number, b.Status, b.TotalMinor, b.AdjustedMinor, b.PaidMinor, b.BalanceMinor, b.CreditMinor,
                b.CreatedAt, b.FinalisedAt,
                lines[b.Id].Select(l => new PortalBillLineDto(l.Description, l.Quantity, l.UnitPriceMinor, l.AmountMinor)).ToList(),
                paid[b.Id].Select(p => new PortalPaymentDto(p.Id, p.AmountMinor, p.Method, p.ReceiptNumber, p.PaidAt, p.Status)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Starts a Paystack checkout for the patient's bill (Task 11's <see cref="OnlinePaymentService.StartCheckoutAsync"/>, which trusts the
    /// tenant): the account must be linked to the patient and the bill must be the patient's (404 otherwise). Returns to
    /// <c>/portal/pay/return?reference=</c>.
    /// </summary>
    public async Task<CareCheckoutResult> PayAsync(Guid patientId, Guid billId, PortalPayRequest r, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        var payer = portal.CustomerUserId();
        if (!await db.PatientBills.AnyAsync(b => b.Id == billId && b.PatientId == patientId, ct)) throw new NotFoundException("Bill");
        return await online.StartCheckoutAsync(billId, payer, r.AmountMinor, ct);
    }

    /// <summary>
    /// The return verify (<c>/portal/pay/return?reference=</c> calls this). The reference must be an online checkout for a patient the
    /// account is linked to, on that patient's own bill (404 otherwise); then the clinic is the tenant and Task 11's verify runs.
    /// </summary>
    public async Task<CareOnlineStatusDto> VerifyAsync(string reference, CancellationToken ct)
    {
        var userId = portal.CustomerUserId();
        var found = await (from p in db.CarePayments.IgnoreQueryFilters()
                           where p.ProviderRef == reference && p.Method == PayMethod.Online
                           join b in db.PatientBills.IgnoreQueryFilters() on new { B = p.BillId, T = p.TenantId } equals new { B = b.Id, T = b.TenantId }
                           where b.PatientId == p.PatientId
                               && db.PatientLinks.IgnoreQueryFilters().Any(l => l.TenantId == p.TenantId && l.PatientId == p.PatientId && l.UserId == userId && l.Active)
                           select new { p.PatientId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payment");
        await portal.RequirePatientAsync(found.PatientId, ct); // the clinic's tenant from here on; 403 when suspended
        return await online.VerifyAsync(reference, ct);
    }

    /// <summary>The receipt PDF of one of the patient's payments (on the patient's own bill); 404 for anyone else's.</summary>
    public async Task<(byte[] Pdf, string FileName)> ReceiptAsync(Guid patientId, Guid paymentId, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        var mine = await (from p in db.CarePayments.AsNoTracking()
                          join b in db.PatientBills.AsNoTracking() on p.BillId equals b.Id
                          where p.Id == paymentId && p.PatientId == patientId && b.PatientId == patientId
                          select p.Id).AnyAsync(ct);
        if (!mine) throw new NotFoundException("Receipt");
        return await payments.ReceiptPdfAsync(paymentId, ct);
    }

    // ---------- Appointments ----------

    public async Task<IReadOnlyList<DoctorDto>> DoctorsAsync(Guid patientId, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        return await appointments.BookableDoctorsAsync(ct);
    }

    public async Task<IReadOnlyList<SlotDto>> SlotsAsync(Guid patientId, Guid doctorUserId, DateOnly? date, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        if (date is not { } day) throw BadRequestException.Field("date", "Choose a date.");
        return await appointments.SlotsAsync(doctorUserId, day, ct);
    }

    public async Task<PortalAppointmentsDto> AppointmentsAsync(Guid patientId, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        var list = await appointments.ForPatientAsync(patientId, ct);
        return new PortalAppointmentsDto(AppointmentService.ReasonLabel, list.Select(ToDto).ToList());
    }

    /// <summary>Books through <see cref="AppointmentService.BookAsync"/> with source Portal, after the link check (a deceased patient gets 409).</summary>
    public async Task<PortalAppointmentDto> BookAsync(Guid patientId, PortalBookRequest r, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        var booked = await appointments.BookAsync(new BookRequest(patientId, r.DoctorUserId, r.Start, r.Reason), AppointmentSource.Portal, ct);
        return ToDto(booked);
    }

    /// <summary>Cancels the patient's own appointment, at least 2 hours before it starts (409 "This appointment starts soon. Call the clinic to change it.").</summary>
    public async Task<PortalAppointmentDto> CancelAsync(Guid patientId, Guid appointmentId, CancellationToken ct)
    {
        await portal.RequirePatientAsync(patientId, ct);
        return ToDto(await appointments.CancelForPortalAsync(patientId, appointmentId, ct));
    }

    private PortalAppointmentDto ToDto(AppointmentDto a) =>
        new(a.Id, a.DoctorUserId, a.DoctorName, a.Start, a.End, a.Reason, a.Status, a.Source, appointments.PortalCanCancel(a.Status, a.Start));

    // ---------- Contact details ----------

    /// <summary>
    /// Replaces the patient's own phone, email and address, validated as on the patient screen. Nothing else on the patient changes. Audited
    /// with ids only (the portal account, the link), never the values.
    /// </summary>
    public async Task<PortalContactDto> UpdateContactAsync(Guid patientId, PortalContactRequest r, CancellationToken ct)
    {
        var who = await portal.RequirePatientAsync(patientId, ct);
        if (!StudentService.TryNormalisePhone(r.Phone, out var phone)) throw BadRequestException.Field("phone", "Enter a valid phone number.");
        var email = StudentService.NormaliseEmail(r.Email);
        if (email is not null && (email.Length > 200 || !email.Contains('@'))) throw BadRequestException.Field("email", "Enter a valid email address.");
        var address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim();
        if (address is { Length: > 300 }) throw BadRequestException.Field("address", "The address can have at most 300 characters.");

        var patient = await db.Patients.FirstAsync(p => p.Id == patientId, ct);
        if (patient.Phone != phone || patient.Email != email || patient.Address != address)
        {
            patient.Phone = phone;
            patient.Email = email;
            patient.Address = address;
            var userId = portal.CustomerUserId();
            var linkId = await db.PatientLinks.Where(l => l.PatientId == patientId && l.UserId == userId).Select(l => l.Id).FirstAsync(ct);
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenant.TenantId, ActorId = userId, Action = "patient.contact_updated", EntityType = nameof(Patient), EntityId = patientId.ToString(),
                AfterJson = JsonSerializer.Serialize(new { portalUserId = userId, linkId, clinicId = who.ClinicId }), Ip = user.IpAddress,
                CreatedAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }
        return new PortalContactDto(patient.Phone, patient.Email, patient.Address);
    }

    // ---------- Notices ----------

    /// <summary>
    /// The account's care notices for this patient at this clinic, newest first (the portal feed is scoped by clinic and patient: a portal
    /// token carries no clinic, Task 14 carry). Notices mark their patient in the link (<see cref="CareNotices.PatientPage"/>).
    /// </summary>
    public async Task<IReadOnlyList<PortalNoticeDto>> NoticesAsync(Guid patientId, CancellationToken ct)
    {
        var who = await portal.RequirePatientAsync(patientId, ct);
        var userId = portal.CustomerUserId();
        var marker = $"?patient={patientId}";
        return await db.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Product == Products.Care && n.TenantId == who.ClinicId && n.Link.EndsWith(marker))
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(50)
            .Select(n => new PortalNoticeDto(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);
    }
}
