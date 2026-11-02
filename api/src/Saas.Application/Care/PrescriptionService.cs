using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>One drug as the doctor writes it. Drug, dose and frequency are required; the rest is optional.</summary>
public sealed record PrescriptionItemInput(
    string? Drug, string? Strength, string? Dose, DoseFrequency? Frequency, int? DurationDays, int? Quantity, string? Route, string? Instructions,
    Guid? PriceItemId);

/// <summary>
/// A prescription from a consultation (outpatient) or an admission (Task 8). <see cref="AllergyAcknowledged"/> must be true when a drug
/// matches one of the patient's recorded allergies.
/// </summary>
public sealed record CreatePrescriptionRequest(
    Guid? PatientId, Guid? ConsultationId, Guid? AdmissionId, bool AllergyAcknowledged, IReadOnlyList<PrescriptionItemInput>? Items);

/// <summary>A prescribed drug, for clinical staff only.</summary>
public sealed record PrescriptionItemDto(
    Guid Id, string Drug, string? Strength, string Dose, DoseFrequency Frequency, int? DurationDays, int? Quantity, string? Route, string? Instructions,
    Guid? PriceItemId);

/// <summary>
/// A prescription with its drugs (in the order written), for clinical staff only. <see cref="OverriddenAllergens"/> are the recorded
/// allergies the doctor acknowledged and prescribed over (empty without an override).
/// </summary>
public sealed record PrescriptionDto(
    Guid Id, Guid PatientId, Guid? ConsultationId, Guid? AdmissionId, Guid DoctorUserId, string DoctorName, bool AllergyAcknowledged,
    IReadOnlyList<string> OverriddenAllergens, DateTimeOffset CreatedAt, IReadOnlyList<PrescriptionItemDto> Items);

/// <summary>What the prescription PDF prints: the clinic letterhead, the patient, the drugs and the prescriber.</summary>
public sealed record PrescriptionPdfData(
    string ClinicName, string? Address, string? Phone, byte[]? Logo, string PatientName, string PatientNumber, string Sex, int AgeYears,
    string Date, string DoctorName, string? DoctorTitle, string? LicenceNumber, bool AllergyAcknowledged, IReadOnlyList<PrescriptionItemDto> Items,
    bool ShowBranding);

/// <summary>CareLink's PDFs (QuestPDF, in Infrastructure).</summary>
public interface ICarePdf
{
    Task<byte[]> PrescriptionAsync(PrescriptionPdfData data, CancellationToken ct);

    /// <summary>Task 10: the signed discharge summary on the clinic's letterhead.</summary>
    Task<byte[]> DischargeAsync(DischargePdfData data, CancellationToken ct);

    /// <summary>Task 11: a payment receipt (no bill lines, nothing clinical).</summary>
    Task<byte[]> ReceiptAsync(CareReceiptData data, CancellationToken ct);
}

/// <summary>Where a prescription or test request was written: the patient and the consultation (with its visit) or the admission.</summary>
internal sealed record OrderContext(Guid PatientId, Guid? ConsultationId, Guid? AdmissionId, Guid? VisitId);

/// <summary>Rules shared by prescriptions and test requests.</summary>
internal static class CareOrders
{
    /// <summary>
    /// Doctors (and the medical director) only: 403 "Only doctors and nurses can see clinical notes." without clinical access, then
    /// 403 <paramref name="doctorsOnly"/> for a nurse.
    /// </summary>
    public static void EnsureDoctor(CareAccess access, CareCaller caller, string doctorsOnly)
    {
        access.EnsureClinical(caller);
        if (caller.MemberRole != MemberRole.Owner && caller.ClinicalRole != ClinicalRole.Doctor) throw new ForbiddenException(doctorsOnly);
    }

    /// <summary>
    /// Resolves and checks where the order is written, under the same rule as the consultation note: the caller must be the consultation's
    /// visit's current doctor (the medical director only on a visit that is theirs), else 403 "This visit is assigned to another doctor.";
    /// and must still have a live care relationship with the patient (ruling M2: a visit's relationships end 30 days after it is Done),
    /// else 403 "Your care of this patient has ended. Ask the medical director." (the director always has one). 404 for another clinic's
    /// consultation; 400 <c>consultationId</c> for a missing one or another patient's.
    /// From an admission instead (Task 8): 400 <c>consultationId</c> when both are given; 404 for an unknown or another clinic's admission;
    /// 400 <c>admissionId</c> for another patient's; 409 "This patient has been discharged."; then the same live care relationship
    /// (or the medical director), else 403. Nothing here reads clinical data, so it runs before the allergy check.
    /// </summary>
    public static async Task<OrderContext> ResolveAsync(
        IAppDbContext db, CareCaller caller, Guid? patientId, Guid? consultationId, Guid? admissionId, DateTimeOffset now, CancellationToken ct)
    {
        if (patientId is not { } pid) throw BadRequestException.Field("patientId", "Choose the patient.");
        if (admissionId is { } aid)
        {
            if (consultationId is not null)
                throw BadRequestException.Field("consultationId", "Write this from the consultation or from the admission, not both.");
            var admission = await db.Admissions.AsNoTracking().Where(a => a.Id == aid).Select(a => new { a.PatientId, a.Status })
                .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Admission");
            if (admission.PatientId != pid) throw BadRequestException.Field("admissionId", "That admission is for another patient.");
            if (admission.Status != AdmissionStatus.Admitted) throw new ConflictException(AdmissionService.Discharged, "admission_discharged");
            if (caller.MemberRole != MemberRole.Owner && !await RecordGuard.HasRelationshipAsync(db, pid, caller.UserId, now, ct))
                throw new ForbiddenException(CareEnded);
            return new OrderContext(pid, null, aid, null);
        }
        if (consultationId is not { } cid) throw BadRequestException.Field("consultationId", "Write this from the patient's consultation.");

        var c = await db.Consultations.AsNoTracking().Where(x => x.Id == cid)
            .Select(x => new { x.PatientId, x.VisitId, VisitDoctor = db.Visits.Where(v => v.Id == x.VisitId).Select(v => v.DoctorUserId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Consultation");
        if (c.PatientId != pid) throw BadRequestException.Field("consultationId", "That consultation is for another patient.");
        if (c.VisitDoctor != caller.UserId) throw new ForbiddenException(ConsultationService.AssignedToAnother);
        if (caller.MemberRole != MemberRole.Owner && !await RecordGuard.HasRelationshipAsync(db, pid, caller.UserId, now, ct))
            throw new ForbiddenException(CareEnded);
        return new OrderContext(pid, cid, null, c.VisitId);
    }

    public const string CareEnded = "Your care of this patient has ended. Ask the medical director.";

    /// <summary>
    /// For an order from an admission, inside the caller's transaction: takes the patient's admission lock
    /// (<see cref="AdmissionService.PatientLockKey"/>, the lock discharge takes) and re-reads the admission's status under it, so a
    /// discharge can't land between <see cref="ResolveAsync"/>'s check and the write: 409 "This patient has been discharged.". Nothing for a
    /// consultation order. Lock order: this, then the bill lock.
    /// </summary>
    public static async Task LockAdmissionAsync(IAppDbContext db, Guid tenantId, OrderContext context, CancellationToken ct)
    {
        if (context.AdmissionId is not { } admissionId) return;
        await db.LockAsync(AdmissionService.PatientLockKey(tenantId, context.PatientId), ct);
        var status = await db.Admissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => a.Status).FirstAsync(ct);
        if (status != AdmissionStatus.Admitted) throw new ConflictException(AdmissionService.Discharged, "admission_discharged");
    }

    /// <summary>400 <c>{field}</c> unless the price item is on this clinic's active price list.</summary>
    public static async Task EnsurePriceItemAsync(IAppDbContext db, Guid? priceItemId, string field, CancellationToken ct)
    {
        if (priceItemId is not { } id) return;
        if (!await db.PriceItems.AnyAsync(i => i.Id == id && i.Active, ct))
            throw BadRequestException.Field(field, "That item isn't on the active price list.");
    }

    /// <summary>The bill the order is charged to: the visit's or the admission's (opened on first use).</summary>
    public static async Task<Guid?> BillAsync(BillingService billing, OrderContext context, CancellationToken ct) =>
        context.VisitId is { } visitId ? (await billing.OpenBillForVisitAsync(context.PatientId, visitId, ct)).Id
        : context.AdmissionId is { } admissionId ? (await billing.OpenBillForAdmissionAsync(context.PatientId, admissionId, ct)).Id
        : null;

    public static string? Optional(string field, string? value, int max)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (v is { Length: var n } && n > max) throw BadRequestException.Field(field, $"This can have at most {max} characters.");
        return v;
    }

    public static string Required(string field, string? value, int max, string missing) =>
        Optional(field, value, max) ?? throw BadRequestException.Field(field, missing);
}

/// <summary>
/// Prescriptions: the visit's doctor prescribes from the consultation, or a doctor caring for an admitted patient from the admission. Drugs that match the patient's
/// recorded allergies (<see cref="AllergyMatcher"/>) need <c>allergyAcknowledged</c>, else 409 naming every matched allergen; an
/// acknowledged override stores those allergens (encrypted) and is audited (ids only). Drug text is stored through
/// <see cref="ClinicalText"/>. Drugs with a price item go on the visit's bill when the clinic dispenses. Writing is not a record open (no
/// access-log row); the PDF is (area pdf). Admission prescriptions are expanded into medication doses (<see cref="MedicationChart"/>)
/// in the same transaction.
/// </summary>
public sealed class PrescriptionService(
    IAppDbContext db, ITenantContext tenant, CareAccess access, RecordGuard guard, ClinicalText text, PatientService patients, BillingService billing,
    IFileStorage storage, ICarePdf pdf, PlanService plans, ICurrentUser user, TimeProvider clock)
{
    public const string DoctorsOnly = "Only doctors can prescribe.";
    public const int MaxItems = 20;

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    /// <summary>
    /// The exact allergy sentence naming every matched allergen (ruling I2): "{Patient} is recorded as allergic to {A}.", "… to {A} and
    /// {B}.", "… to {A}, {B} and {C}."
    /// </summary>
    public static string AllergyWarning(string patientName, IReadOnlyList<string> allergens)
    {
        var list = allergens.Count <= 1 ? string.Join("", allergens) : $"{string.Join(", ", allergens.Take(allergens.Count - 1))} and {allergens[^1]}";
        return $"{patientName} is recorded as allergic to {list}.";
    }

    /// <summary>Every recorded allergy that matches one of the drugs, distinct and in recorded order (<see cref="AllergyMatcher"/>).</summary>
    public static IReadOnlyList<string> AllergyMatches(IEnumerable<string> drugs, IEnumerable<string> allergies) => AllergyMatcher.Matches(drugs, allergies);

    public async Task<PrescriptionDto> CreateAsync(CreatePrescriptionRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        CareOrders.EnsureDoctor(access, caller, DoctorsOnly);
        var items = Validate(r.Items);
        var now = clock.GetUtcNow();
        var context = await CareOrders.ResolveAsync(db, caller, r.PatientId, r.ConsultationId, r.AdmissionId, now, ct);
        for (var i = 0; i < items.Count; i++) await CareOrders.EnsurePriceItemAsync(db, items[i].PriceItemId, $"items[{i}].priceItemId", ct);

        // The allergy check reads the patient's allergies only after the caller is known to be this patient's doctor.
        var facts = await patients.ClinicalFactsAsync(context.PatientId, ct);
        var allergens = AllergyMatches(items.Select(i => i.Drug), facts.Allergies);
        if (allergens.Count > 0 && !r.AllergyAcknowledged)
        {
            var name = await db.Patients.AsNoTracking().Where(p => p.Id == context.PatientId).Select(p => p.FirstName + " " + p.LastName).FirstAsync(ct);
            throw new ConflictException(AllergyWarning(name, allergens), "allergy_unacknowledged");
        }
        var overridden = r.AllergyAcknowledged ? allergens : [];

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Task 10 carry: an admission prescription is serialised with discharge (patient lock, then "still admitted" re-read).
        await CareOrders.LockAdmissionAsync(db, TenantId, context, ct);
        var prescription = new Prescription
        {
            TenantId = TenantId, PatientId = context.PatientId, ConsultationId = context.ConsultationId, AdmissionId = context.AdmissionId,
            DoctorUserId = caller.UserId, AllergyAcknowledged = overridden.Count > 0, OverriddenAllergens = text.ProtectAll(overridden),
            Items = items.Select((i, n) => new PrescriptionItem
            {
                TenantId = TenantId, Position = n, Drug = text.Protect(i.Drug), Strength = Stored(i.Strength), Dose = text.Protect(i.Dose),
                Frequency = i.Frequency, DurationDays = i.DurationDays, Quantity = i.Quantity, Route = Stored(i.Route), Instructions = Stored(i.Instructions),
                PriceItemId = i.PriceItemId,
            }).ToList(),
        };
        db.Prescriptions.Add(prescription);
        if (prescription.AllergyAcknowledged)
            db.AuditLogs.Add(new AuditLog
            {
                TenantId = TenantId, ActorId = caller.UserId, Action = "care.allergy_override", EntityType = nameof(Prescription),
                EntityId = prescription.Id.ToString(),
                AfterJson = JsonSerializer.Serialize(new
                {
                    patientId = context.PatientId, consultationId = context.ConsultationId, admissionId = context.AdmissionId, items = items.Count,
                }),
                Ip = user.IpAddress, CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);

        // Drug lines go on the bill only when the clinic dispenses; the line shows the price item's name, the item id is the clinical reference.
        var dispenses = await db.ClinicProfiles.AsNoTracking().Select(c => c.DispensesDrugs).FirstOrDefaultAsync(ct);
        if (dispenses && prescription.Items.Any(i => i.PriceItemId is not null) && await CareOrders.BillAsync(billing, context, ct) is { } billId)
            foreach (var item in prescription.Items.Where(i => i.PriceItemId is not null))
                await billing.AddLineAsync(billId, item.PriceItemId!.Value, item.Quantity ?? 1, item.Id.ToString(), ct);
        // Task 9: an admission prescription becomes the medication chart's scheduled doses, in the same transaction.
        if (MedicationChart.AddDoses(db, prescription, now) > 0) await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        // The answer echoes what the doctor just wrote (a write, not a record open).
        var doctorName = await db.Users.AsNoTracking().Where(u => u.Id == caller.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        return new PrescriptionDto(prescription.Id, prescription.PatientId, prescription.ConsultationId, prescription.AdmissionId, caller.UserId,
            doctorName, prescription.AllergyAcknowledged, overridden, prescription.CreatedAt,
            prescription.Items.Select((item, n) => new PrescriptionItemDto(item.Id, items[n].Drug, items[n].Strength, items[n].Dose, item.Frequency,
                item.DurationDays, item.Quantity, items[n].Route, items[n].Instructions, item.PriceItemId)).ToList());
    }

    /// <summary>
    /// The prescription as a PDF on the clinic's letterhead (logo when there is one). A clinical read: 403 without clinical access before
    /// anything is looked up, 404 for an unknown or another clinic's prescription, then <see cref="RecordGuard.OpenAsync"/> (area pdf).
    /// </summary>
    public async Task<(byte[] Pdf, string FileName)> PdfAsync(Guid id, string? reason, CancellationToken ct)
    {
        access.EnsureClinical(await access.CallerAsync(ct));
        var patientId = await db.Prescriptions.AsNoTracking().Where(p => p.Id == id).Select(p => (Guid?)p.PatientId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Prescription");
        await guard.OpenAsync(patientId, RecordGuard.Areas.Pdf, reason, ct);

        var dto = (await ReadManyAsync(db, text, db.Prescriptions.AsNoTracking().Where(p => p.Id == id), ct)).Single();
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == patientId, ct);
        var clinic = await db.ClinicProfiles.AsNoTracking().FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");
        var staff = await db.CareStaffProfiles.AsNoTracking().Where(s => s.UserId == dto.DoctorUserId)
            .Select(s => new { s.Title, s.LicenceNumber }).FirstOrDefaultAsync(ct);
        var logo = await LogoAsync(storage, clinic.LogoKey, ct);

        var written = CareCalendar.Today(dto.CreatedAt);
        var today = CareCalendar.Today(clock.GetUtcNow());
        var dob = patient.DateOfBirth;
        var age = today.Year - dob.Year - (today < dob.AddYears(today.Year - dob.Year) ? 1 : 0);
        var data = new PrescriptionPdfData(
            clinic.Name, clinic.Address, clinic.Phone, logo, $"{patient.FirstName} {patient.LastName}", patient.Number, patient.Sex.ToString(), Math.Max(0, age),
            written.ToString("d MMMM yyyy", CultureInfo.InvariantCulture), dto.DoctorName, staff?.Title, staff?.LicenceNumber, dto.AllergyAcknowledged,
            dto.Items, !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct));
        return (await pdf.PrescriptionAsync(data, ct), $"prescription-{patient.Number}-{written:yyyyMMdd}.pdf");
    }

    /// <summary>The clinic logo's stored bytes for a letterhead, or null when there is none (the PDF decides whether they are a usable image).</summary>
    internal static async Task<byte[]?> LogoAsync(IFileStorage storage, string? key, CancellationToken ct)
    {
        if (key is null) return null;
        await using var stream = await storage.OpenReadAsync(key, ct);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    /// <summary>Prescriptions as DTOs, newest first, items in the order written, decrypted. No access check: the caller has opened the record.</summary>
    internal static async Task<List<PrescriptionDto>> ReadManyAsync(IAppDbContext db, ClinicalText text, IQueryable<Prescription> query, CancellationToken ct)
    {
        var rows = await query.Include(p => p.Items).OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var names = await ConsultationService.NamesAsync(db, rows.Select(p => p.DoctorUserId), ct);
        return rows.Select(p => new PrescriptionDto(p.Id, p.PatientId, p.ConsultationId, p.AdmissionId, p.DoctorUserId,
                names.GetValueOrDefault(p.DoctorUserId, ""), p.AllergyAcknowledged, text.ReadAll(p.OverriddenAllergens), p.CreatedAt,
                p.Items.OrderBy(i => i.Position).Select(i => new PrescriptionItemDto(i.Id, text.Read(i.Drug) ?? "", text.Read(i.Strength), text.Read(i.Dose) ?? "",
                    i.Frequency, i.DurationDays, i.Quantity, text.Read(i.Route), text.Read(i.Instructions), i.PriceItemId)).ToList()))
            .ToList();
    }

    private string? Stored(string? plain) => plain is null ? null : text.Protect(plain);

    private sealed record ValidItem(
        string Drug, string? Strength, string Dose, DoseFrequency Frequency, int? DurationDays, int? Quantity, string? Route, string? Instructions,
        Guid? PriceItemId);

    private static List<ValidItem> Validate(IReadOnlyList<PrescriptionItemInput>? input)
    {
        if (input is null || input.Count == 0) throw BadRequestException.Field("items", "Add at least one drug.");
        if (input.Count > MaxItems) throw BadRequestException.Field("items", $"A prescription can have at most {MaxItems} drugs.");
        var result = new List<ValidItem>();
        for (var i = 0; i < input.Count; i++)
        {
            var item = input[i] ?? throw BadRequestException.Field($"items[{i}].drug", "Write the drug.");
            var drug = CareOrders.Required($"items[{i}].drug", item.Drug, 200, "Write the drug.");
            var strength = CareOrders.Optional($"items[{i}].strength", item.Strength, 60);
            var dose = CareOrders.Required($"items[{i}].dose", item.Dose, 100, "Write the dose.");
            if (item.Frequency is not { } frequency || !Enum.IsDefined(frequency))
                throw BadRequestException.Field($"items[{i}].frequency", "Choose how often: OD, BD, TDS, QDS, Q4H, Q6H, Q8H, Nocte, Stat or PRN.");
            if (item.DurationDays is < 1 or > 365) throw BadRequestException.Field($"items[{i}].durationDays", "Enter a duration from 1 to 365 days.");
            if (item.Quantity is < 1 or > BillingService.MaxQuantity) throw BadRequestException.Field($"items[{i}].quantity", "Enter a quantity from 1 to 10,000.");
            var route = CareOrders.Optional($"items[{i}].route", item.Route, 60);
            var instructions = CareOrders.Optional($"items[{i}].instructions", item.Instructions, 500);
            result.Add(new ValidItem(drug, strength, dose, frequency, item.DurationDays, item.Quantity, route, instructions, item.PriceItemId));
        }
        return result;
    }
}
