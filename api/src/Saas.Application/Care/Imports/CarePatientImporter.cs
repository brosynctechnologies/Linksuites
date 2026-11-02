using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Application.School.Imports;
using Saas.Domain.Care;

namespace Saas.Application.Care.Imports;

/// <summary>
/// Patients, one per row (kind <c>care-patients</c>), with the registration screen's access (front desk and above; the cashier gets 403) and
/// its field rules (<see cref="PatientService.Apply"/>). Demographics only: any other column, clinical or not, is dropped before the rows are
/// held. Saved through <see cref="PatientService.RegisterAsync"/> and <see cref="PatientService.UpdateAsync"/>, so patient numbers come from
/// the clinic's counter (never from the file) and the audit rows hold field names only.
/// <list type="bullet">
/// <item>A row with a <c>patient_number</c> updates that patient: a blank cell keeps what is saved and an equal row is Unchanged. An unknown
/// number is an error (a file can't choose a number).</item>
/// <item>A row without one registers a new patient, unless a patient with the same name and date of birth is already registered: then a row
/// that matches them exactly is Unchanged (so uploading a file twice registers nobody twice), and any other is an error naming their number.
/// The screen only warns about that duplicate, but a preview row can't ask "register anyway?", so the import refuses it.</item>
/// <item>A new patient who shares a phone number with a registered one (siblings on a parent's phone) is registered, with a preview note.</item>
/// </list>
/// </summary>
public sealed class CarePatientImporter(IAppDbContext db, CareAccess access, PatientService patients)
    : IImporter, IImportWarnings, IImportNotice, IImportDeclaredColumnsOnly
{
    public const string KindName = "care-patients";
    public const string Twice = "This patient appears more than once in the file.";
    public const string UnknownNumber = "No patient has this number. Leave patient_number blank to register a new patient.";
    public const string BadSex = "Enter the sex as F or M (Female or Male also work).";
    public const string BadDate = "Enter the date of birth as 1990-04-12 or 12/04/1990 (day, month, year).";

    public string Kind => KindName;
    public string Product => Billing.Products.Care;
    public string SheetName => "Patients";
    public string NoticeTitle => "Patient import finished";
    public string NoticeLink => "/app/patients";

    private static readonly ImportColumns AllColumns = new(
    [
        new("patient_number", "patient_number", false, "Leave blank for a new patient (the clinic gives the number). Fill it in to update that patient."),
        new("first_name", "first_name", true, "Required for a new patient."),
        new("middle_name", "middle_name", false, "Optional."),
        new("last_name", "last_name", true, "Required for a new patient."),
        new("sex", "sex", true, "F or M (Female or Male also work). Required for a new patient."),
        new("date_of_birth", "date_of_birth", true, "1990-04-12 or 12/04/1990 (day, month, year). Required for a new patient."),
        new("phone", "phone", false, "Optional, for example 0803 123 4567."),
        new("email", "email", false, "Optional."),
        new("address", "address", false, "Optional. Up to 300 characters."),
        new("next_of_kin_name", "next_of_kin_name", false, "Optional."),
        new("next_of_kin_phone", "next_of_kin_phone", false, "Optional."),
        new("next_of_kin_relationship", "next_of_kin_relationship", false, "Optional, for example Mother."),
    ]);

    private static readonly string[] Notes =
    [
        "One patient per row on the Patients sheet. Delete nothing from the heading row.",
        "Demographics only. Clinical details (allergies, conditions, diagnoses, medicines, notes) are never imported: any other column is ignored.",
        "patient_number: leave it blank to register a new patient; the clinic gives the number. Fill it in to update that patient.",
        "When you update a patient, a blank cell keeps what is saved.",
        "A patient with the same name and date of birth as a registered patient is not registered twice: put their patient number in the row to update them.",
        "sex: F or M. date_of_birth: 1990-04-12 or 12/04/1990 (day, month, year).",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private sealed record Stored(
        Guid Id, string Number, string FirstName, string? MiddleName, string LastName, PatientSex Sex, DateOnly DateOfBirth, string? Phone,
        string? Email, string? Address, string? NextOfKinName, string? NextOfKinPhone, string? NextOfKinRelationship);

    private sealed record Plan(Guid? Id, SavePatientRequest Request, bool SharesPhone);

    private sealed record Checked(ImportRowData Row, string Label, string? Error, ImportRowStatus Status, Plan? Plan, string? FileKey);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => PatientService.EnsureDesk(await access.CallerAsync(ct));

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public IReadOnlyList<string> DroppedColumnWarnings(IReadOnlyList<string> dropped) => CareImports.Warnings(dropped);

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        // Demographics only: the clinical columns are never read.
        var stored = await db.Patients.AsNoTracking()
            .Select(p => new Stored(p.Id, p.Number, p.FirstName, p.MiddleName, p.LastName, p.Sex, p.DateOfBirth, p.Phone, p.Email, p.Address,
                p.NextOfKinName, p.NextOfKinPhone, p.NextOfKinRelationship))
            .ToListAsync(ct);
        var byNumber = stored.GroupBy(p => p.Number.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var byNameAndBirth = stored.ToLookup(p => NameKey(p.FirstName, p.LastName, p.DateOfBirth));
        var phones = stored.Where(p => p.Phone is not null).Select(p => p.Phone!).ToHashSet();

        var checkedRows = rows.Select(row => Check(row, byNumber, byNameAndBirth, phones)).ToList();
        var repeated = checkedRows.Where(c => c.FileKey is not null).GroupBy(c => c.FileKey).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return checkedRows.Select(c =>
            c.Error is null && c.FileKey is not null && repeated.Contains(c.FileKey)
                ? new ImportRowResult(c.Row.Row, ImportRowStatus.Error, c.Label, Twice, null)
                : new ImportRowResult(c.Row.Row, c.Error is null ? c.Status : ImportRowStatus.Error, c.Label, c.Error, c.Error is null ? c.Plan : null))
            .ToList();
    }

    private Checked Check(ImportRowData row, Dictionary<string, Stored> byNumber, ILookup<string, Stored> byNameAndBirth, HashSet<string> phones)
    {
        string Cell(string key) => row.Cells.GetValueOrDefault(key) ?? "";
        string? Keep(string key, string? saved) => Cell(key).Length > 0 ? Cell(key) : saved;
        var number = Cell("patient_number");
        var label = string.Join(' ', new[] { number, Cell("first_name"), Cell("last_name") }.Where(s => s.Length > 0));

        Stored? existing = null;
        if (number.Length > 0 && !byNumber.TryGetValue(number.ToLowerInvariant(), out existing))
            return new Checked(row, label, UnknownNumber, ImportRowStatus.Error, null, null);

        PatientSex? sex = existing?.Sex;
        if (Cell("sex").Length > 0)
        {
            sex = Cell("sex").ToLowerInvariant() switch { "f" or "female" => PatientSex.Female, "m" or "male" => PatientSex.Male, _ => null };
            if (sex is null) return new Checked(row, label, BadSex, ImportRowStatus.Error, null, null);
        }
        DateOnly? dob = existing?.DateOfBirth;
        if (Cell("date_of_birth").Length > 0)
        {
            if (!StudentImport.TryParseDate(Cell("date_of_birth"), out dob) || dob is null) return new Checked(row, label, BadDate, ImportRowStatus.Error, null, null);
        }

        // Phones are compared as stored (+234...): a cell is cleaned by the screen's rule below before comparing.
        var request = new SavePatientRequest(
            Keep("first_name", existing?.FirstName), Keep("middle_name", existing?.MiddleName), Keep("last_name", existing?.LastName), sex, dob,
            Keep("phone", existing?.Phone), Keep("email", existing?.Email), Keep("address", existing?.Address),
            Keep("next_of_kin_name", existing?.NextOfKinName), Keep("next_of_kin_phone", existing?.NextOfKinPhone),
            Keep("next_of_kin_relationship", existing?.NextOfKinRelationship));
        var clean = new Patient { Number = "", FirstName = "", LastName = "" };
        try
        {
            patients.Apply(clean, request);
        }
        catch (BadRequestException ex)
        {
            return new Checked(row, label, ex.Message, ImportRowStatus.Error, null, null);
        }
        label = string.Join(' ', new[] { existing?.Number, clean.FirstName, clean.LastName }.Where(s => !string.IsNullOrEmpty(s)));
        var cleanRequest = Request(clean);

        if (existing is not null)
        {
            var status = Same(existing, clean) ? ImportRowStatus.Unchanged : ImportRowStatus.Update;
            return new Checked(row, label, null, status, status == ImportRowStatus.Update ? new Plan(existing.Id, cleanRequest, false) : null,
                "#" + existing.Number.ToLowerInvariant());
        }

        var key = NameKey(clean.FirstName, clean.LastName, clean.DateOfBirth);
        var namesakes = byNameAndBirth[key].ToList();
        if (namesakes.Count > 0)
        {
            if (namesakes.Any(p => Same(p, clean))) return new Checked(row, label, null, ImportRowStatus.Unchanged, null, key);
            var known = namesakes.OrderBy(p => p.Number).First().Number;
            return new Checked(row, label,
                $"{clean.FirstName} {clean.LastName}, born {clean.DateOfBirth.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}, is already registered as " +
                $"{known}. Put {known} in patient_number to update them, or register a different patient with the same name on the patients page.",
                ImportRowStatus.Error, null, key);
        }
        return new Checked(row, label, null, ImportRowStatus.New, new Plan(null, cleanRequest, clean.Phone is not null && phones.Contains(clean.Phone)), key);
    }

    private static string NameKey(string first, string last, DateOnly dob) =>
        $"{CareImports.Key(first)}|{CareImports.Key(last)}|{dob:yyyy-MM-dd}";

    private static SavePatientRequest Request(Patient p) => new(p.FirstName, p.MiddleName, p.LastName, p.Sex, p.DateOfBirth, p.Phone, p.Email, p.Address,
        p.NextOfKinName, p.NextOfKinPhone, p.NextOfKinRelationship);

    private static bool Same(Stored s, Patient p) =>
        s.FirstName == p.FirstName && s.MiddleName == p.MiddleName && s.LastName == p.LastName && s.Sex == p.Sex && s.DateOfBirth == p.DateOfBirth
        && s.Phone == p.Phone && s.Email == p.Email && s.Address == p.Address && s.NextOfKinName == p.NextOfKinName
        && s.NextOfKinPhone == p.NextOfKinPhone && s.NextOfKinRelationship == p.NextOfKinRelationship;

    public Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var shared = rows.Select(r => r.Plan).OfType<Plan>().Count(p => p.SharesPhone);
        IReadOnlyList<string> warnings = shared == 0
            ? []
            : [shared == 1
                ? "1 new patient shares a phone number with a registered patient. Check they are not registered already."
                : $"{shared} new patients share a phone number with a registered patient. Check they are not registered already."];
        return Task.FromResult(warnings);
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Status == ImportRowStatus.Error || row.Plan is not Plan plan) continue;
            if (plan.Id is { } id)
            {
                await patients.UpdateAsync(id, plan.Request, ct);
                updated++;
            }
            else
            {
                await patients.RegisterAsync(plan.Request, ct);
                created++;
            }
        }
        return new ImportApplied(created, updated);
    }
}
