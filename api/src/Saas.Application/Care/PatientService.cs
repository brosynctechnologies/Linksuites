using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// A patient as staff see them. Demographics go to every staff member; <see cref="BloodGroup"/>, <see cref="Genotype"/>,
/// <see cref="Allergies"/> and <see cref="Conditions"/> are filled only for clinical callers and left out of the JSON otherwise.
/// </summary>
public sealed record PatientDto(
    Guid Id, string Number, string FirstName, string? MiddleName, string LastName, string FullName, PatientSex Sex, DateOnly DateOfBirth,
    string? Phone, string? Email, string? Address, string? NextOfKinName, string? NextOfKinPhone, string? NextOfKinRelationship,
    PatientStatus Status, DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BloodGroup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Genotype = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Allergies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Conditions = null);

public sealed record PatientSummaryDto(
    Guid Id, string Number, string FullName, PatientSex Sex, DateOnly DateOfBirth, string? Phone, PatientStatus Status);

public sealed record RegisterPatientResult(PatientDto Patient, IReadOnlyList<PatientSummaryDto> PossibleDuplicates);

public sealed record SavePatientRequest(
    string? FirstName, string? MiddleName, string? LastName, PatientSex? Sex, DateOnly? DateOfBirth, string? Phone, string? Email, string? Address,
    string? NextOfKinName, string? NextOfKinPhone, string? NextOfKinRelationship);

public sealed record SaveClinicalFactsRequest(IReadOnlyList<string>? Allergies, IReadOnlyList<string>? Conditions, string? BloodGroup, string? Genotype);

/// <summary>The unprotected clinical facts, for services that already checked clinical access (the allergy check in Task 7).</summary>
public sealed record PatientClinicalFacts(IReadOnlyList<string> Allergies, IReadOnlyList<string> Conditions, string? BloodGroup, string? Genotype);

/// <summary>
/// Patient registration, search and demographics (front desk and above; the cashier reads only), clinical facts (clinical staff only)
/// and going inactive. Patients are never deleted.
/// </summary>
public sealed class PatientService(
    IAppDbContext db, CareAccess access, RecordGuard guard, ClinicalText text, TimeProvider clock, ICurrentUser user, ITenantContext tenant)
{
    public static readonly string[] BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];
    public static readonly string[] Genotypes = ["AA", "AS", "AC", "SS", "SC", "CC"];
    private const int SearchLimit = 50;

    // ---------- Reads ----------

    public async Task<IReadOnlyList<PatientSummaryDto>> SearchAsync(string? q, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        var query = db.Patients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            var phone = StudentService.TryNormalisePhone(q, out var p) ? p : null;
            query = query.Where(x => x.Number.ToLower().Contains(term)
                || x.FirstName.ToLower().Contains(term) || x.LastName.ToLower().Contains(term)
                || (x.MiddleName != null && x.MiddleName.ToLower().Contains(term))
                || (x.FirstName + " " + x.LastName).ToLower().Contains(term)
                || (x.LastName + " " + x.FirstName).ToLower().Contains(term)
                || (phone != null && x.Phone == phone)
                || (x.Phone != null && x.Phone.Contains(term)));
        }
        return await query.OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Number).Take(SearchLimit)
            .Select(x => new PatientSummaryDto(x.Id, x.Number, x.FirstName + " " + x.LastName, x.Sex, x.DateOfBirth, x.Phone, x.Status))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The patient. Non-clinical staff get demographics only and nothing is logged. A clinical caller opens the record through
    /// <see cref="RecordGuard"/> (area <c>record</c>): logged, and <paramref name="reason"/> is required without a care relationship.
    /// </summary>
    public async Task<PatientDto> GetAsync(Guid id, string? reason, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        if (caller.IsClinical) await guard.OpenAsync(id, RecordGuard.Areas.Record, reason, ct);
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Patient");
        return ToDto(patient, caller.IsClinical);
    }

    /// <summary>The patient's clinical facts, unprotected. No access check: the caller must already have checked clinical access.</summary>
    public async Task<PatientClinicalFacts> ClinicalFactsAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct) ?? throw new NotFoundException("Patient");
        return new PatientClinicalFacts(Read(patient.Allergies), Read(patient.Conditions), patient.BloodGroup, patient.Genotype);
    }

    // ---------- Writes ----------

    public async Task<RegisterPatientResult> RegisterAsync(SavePatientRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        EnsureDesk(caller);
        var patient = new Patient { Number = "", FirstName = "", LastName = "" };
        Apply(patient, r);

        // Warn, don't block: the same name and date of birth, or the same phone, may be the same person registered twice.
        var first = patient.FirstName.ToLower();
        var last = patient.LastName.ToLower();
        var dob = patient.DateOfBirth;
        var phone = patient.Phone;
        var duplicates = await db.Patients.AsNoTracking()
            .Where(x => (x.FirstName.ToLower() == first && x.LastName.ToLower() == last && x.DateOfBirth == dob) || (phone != null && x.Phone == phone))
            .OrderBy(x => x.Number).Take(10)
            .Select(x => new PatientSummaryDto(x.Id, x.Number, x.FirstName + " " + x.LastName, x.Sex, x.DateOfBirth, x.Phone, x.Status))
            .ToListAsync(ct);

        var prefix = await db.ClinicProfiles.Select(c => c.PatientPrefix).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Clinic");
        var number = await db.NextDocumentNumberAsync(DocumentCounter.CarePatient, ct);
        patient.Number = $"{prefix}-{number.ToString("D6", CultureInfo.InvariantCulture)}";
        db.Patients.Add(patient);
        db.AuditLogs.Add(Audit("patient.registered", patient.Id, new { number = patient.Number, fields = Fields(patient) }));
        await db.SaveChangesAsync(ct);
        return new RegisterPatientResult(ToDto(patient, clinical: false), duplicates);
    }

    public async Task<PatientDto> UpdateAsync(Guid id, SavePatientRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        EnsureDesk(caller);
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Patient");
        var before = Fields(patient, values: true);
        Apply(patient, r);
        var after = Fields(patient, values: true);
        var changed = after.Where((v, i) => v != before[i]).Select(v => v.Split('=', 2)[0]).ToList();
        if (changed.Count > 0) db.AuditLogs.Add(Audit("patient.updated", patient.Id, new { changed }));
        await db.SaveChangesAsync(ct);
        // Demographic writes answer with demographics only: clinical facts are read through GET, which goes through RecordGuard.
        return ToDto(patient, clinical: false);
    }

    public async Task<PatientDto> SaveClinicalFactsAsync(Guid id, SaveClinicalFactsRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureClinical(caller);
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Patient");

        var allergies = CleanList(r.Allergies, "allergies", "allergy");
        var conditions = CleanList(r.Conditions, "conditions", "condition");
        var bloodGroup = Clean(r.BloodGroup)?.ToUpperInvariant();
        if (bloodGroup is not null && !BloodGroups.Contains(bloodGroup))
            throw BadRequestException.Field("bloodGroup", "Choose a blood group such as O+ or AB-.");
        var genotype = Clean(r.Genotype)?.ToUpperInvariant();
        if (genotype is not null && !Genotypes.Contains(genotype))
            throw BadRequestException.Field("genotype", "Choose a genotype: AA, AS, AC, SS, SC or CC.");

        // Clinical free text is stored protected through ClinicalText; each entry on its own so the list reads back.
        // A full replace: the response echoes only what this caller just wrote, so the write isn't a record open (no RecordGuard).
        patient.Allergies = text.ProtectAll(allergies);
        patient.Conditions = text.ProtectAll(conditions);
        patient.BloodGroup = bloodGroup;
        patient.Genotype = genotype;
        db.AuditLogs.Add(Audit("patient.clinical_facts", patient.Id, new { change = "clinical facts updated" }));
        await db.SaveChangesAsync(ct);
        return ToDto(patient, true);
    }

    public async Task<PatientDto> SetInactiveAsync(Guid id, bool inactive, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        EnsureDesk(caller);
        if (!await db.Patients.AnyAsync(p => p.Id == id, ct)) throw new NotFoundException("Patient");
        // Task 16 fix round 1 (M8): under the patient's admission lock (the lock a Died discharge holds while it records the death), so the
        // status is re-read after any death commits and a Deceased patient is never overwritten.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(AdmissionService.PatientLockKey(tenant.TenantId ?? throw new ForbiddenException(), id), ct);
        var patient = await db.Patients.FirstAsync(p => p.Id == id, ct);
        if (patient.Status == PatientStatus.Deceased) throw new ConflictException(AppointmentService.PatientDeceased, "patient_deceased");
        var was = patient.Status;
        patient.Status = inactive ? PatientStatus.Inactive : PatientStatus.Active;
        if (was != patient.Status)
            db.AuditLogs.Add(Audit(inactive ? "patient.inactive" : "patient.active", patient.Id, new { status = patient.Status.ToString() }, new { status = was.ToString() }));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return ToDto(patient, clinical: false);
    }

    // ---------- Helpers ----------

    /// <summary>Registration and demographics: everyone but the cashier (front desk, clinical staff, leadership).</summary>
    public static void EnsureDesk(CareCaller c)
    {
        if (c.IsCashier) throw new ForbiddenException();
    }

    /// <summary>
    /// Validates a registration or demographics request and copies the cleaned values onto <paramref name="p"/>; 400 with the field's sentence
    /// otherwise. The patient import checks each row with this, on a scratch patient, so a file follows the screen's rules exactly.
    /// </summary>
    internal void Apply(Patient p, SavePatientRequest r)
    {
        var first = Name(r.FirstName, "firstName", "Enter the first name.", required: true)!;
        var middle = Name(r.MiddleName, "middleName", null, required: false);
        var last = Name(r.LastName, "lastName", "Enter the last name.", required: true)!;
        if (r.Sex is not { } sex || !Enum.IsDefined(sex)) throw BadRequestException.Field("sex", "Choose Female or Male.");
        if (r.DateOfBirth is not { } dob) throw BadRequestException.Field("dateOfBirth", "Enter the date of birth.");
        if (dob > CareCalendar.Today(clock.GetUtcNow())) throw BadRequestException.Field("dateOfBirth", "The date of birth can't be in the future.");
        if (dob.Year < 1900) throw BadRequestException.Field("dateOfBirth", "Check the date of birth.");
        if (!StudentService.TryNormalisePhone(r.Phone, out var phone)) throw BadRequestException.Field("phone", "Enter a valid phone number.");
        var email = StudentService.NormaliseEmail(r.Email);
        if (email is not null && (email.Length > 200 || !email.Contains('@'))) throw BadRequestException.Field("email", "Enter a valid email address.");
        var address = Clean(r.Address);
        if (address is { Length: > 300 }) throw BadRequestException.Field("address", "The address can have at most 300 characters.");
        var kinName = Clean(r.NextOfKinName);
        if (kinName is { Length: > 120 }) throw BadRequestException.Field("nextOfKinName", "The next of kin's name can have at most 120 characters.");
        if (!StudentService.TryNormalisePhone(r.NextOfKinPhone, out var kinPhone))
            throw BadRequestException.Field("nextOfKinPhone", "Enter a valid phone number.");
        var kinRelationship = Clean(r.NextOfKinRelationship);
        if (kinRelationship is { Length: > 40 }) throw BadRequestException.Field("nextOfKinRelationship", "The relationship can have at most 40 characters.");

        p.FirstName = first;
        p.MiddleName = middle;
        p.LastName = last;
        p.Sex = sex;
        p.DateOfBirth = dob;
        p.Phone = phone;
        p.Email = email;
        p.Address = address;
        p.NextOfKinName = kinName;
        p.NextOfKinPhone = kinPhone;
        p.NextOfKinRelationship = kinRelationship;
    }

    private static string? Name(string? raw, string field, string? missing, bool required)
    {
        var s = Clean(raw);
        if (s is null)
        {
            if (required) throw BadRequestException.Field(field, missing!);
            return null;
        }
        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.Length > 60) throw BadRequestException.Field(field, "A name can have at most 60 characters.");
        return s;
    }

    private static List<string> CleanList(IReadOnlyList<string>? raw, string field, string what)
    {
        var list = new List<string>();
        foreach (var item in raw ?? [])
        {
            var s = Clean(item);
            if (s is null) continue;
            if (s.Length > 100) throw BadRequestException.Field(field, $"Each {what} can have at most 100 characters.");
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }
        if (list.Count > 30) throw BadRequestException.Field(field, $"Record at most 30 entries.");
        return list;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Demographic field names that hold a value (or "name=value" pairs when <paramref name="values"/>, for change detection only).
    /// Never includes clinical facts; values never leave this class.
    /// </summary>
    private static List<string> Fields(Patient p, bool values = false)
    {
        (string Name, string? Value)[] all =
        [
            ("firstName", p.FirstName), ("middleName", p.MiddleName), ("lastName", p.LastName), ("sex", p.Sex.ToString()),
            ("dateOfBirth", p.DateOfBirth.ToString("O", CultureInfo.InvariantCulture)), ("phone", p.Phone), ("email", p.Email), ("address", p.Address),
            ("nextOfKinName", p.NextOfKinName), ("nextOfKinPhone", p.NextOfKinPhone), ("nextOfKinRelationship", p.NextOfKinRelationship),
        ];
        return values ? all.Select(f => $"{f.Name}={f.Value}").ToList() : all.Where(f => f.Value is not null).Select(f => f.Name).ToList();
    }

    /// <summary>An explicit audit row for a patient (Patient isn't [Audited]): the action and field names, never clinical values.</summary>
    private AuditLog Audit(string action, Guid patientId, object after, object? before = null) => new()
    {
        TenantId = tenant.TenantId, ActorId = user.UserId, Action = action, EntityType = nameof(Patient), EntityId = patientId.ToString(),
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after),
        Ip = user.IpAddress, CreatedAt = clock.GetUtcNow(),
    };

    private List<string> Read(List<string> stored) => text.ReadAll(stored);

    internal PatientDto ToDto(Patient p, bool clinical) => new(
        p.Id, p.Number, p.FirstName, p.MiddleName, p.LastName, $"{p.FirstName} {p.LastName}", p.Sex, p.DateOfBirth, p.Phone, p.Email, p.Address,
        p.NextOfKinName, p.NextOfKinPhone, p.NextOfKinRelationship, p.Status, p.CreatedAt,
        clinical ? p.BloodGroup : null, clinical ? p.Genotype : null,
        clinical ? Read(p.Allergies) : null, clinical ? Read(p.Conditions) : null);
}
