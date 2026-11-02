using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Students and their guardians, one student per row, matched by admission number (a known number updates the student). Leadership only.
/// The create and update rules are the ones in <see cref="StudentService"/>; new students must fit the plan.
/// </summary>
public sealed class StudentImporter(
    IAppDbContext db, ITenantContext tenant, SchoolAccess access, PlanService plans, StudentService students, TimeProvider clock)
    : IImporter, IImportWarnings, IImportNotice
{
    public const string KindName = "students";
    public string Kind => KindName;
    public string SheetName => "Students";
    public string NoticeTitle => "Student import finished";
    public string NoticeLink => "/app/students";

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    private static readonly Dictionary<string, string> Help = new()
    {
        ["admission_no"] = "The school's admission number. A number already in the school updates that student.",
        ["first_name"] = "The student's first name.",
        ["middle_name"] = "Optional.",
        ["last_name"] = "The student's surname.",
        ["gender"] = "M or F (Male or Female also work).",
        ["date_of_birth"] = "Optional. 2012-03-04 or 04/03/2012 (day, month, year).",
        ["class"] = "The name of a class in the current session, for example JSS 1A.",
        ["guardian1_name"] = "Optional. Required when you give the guardian's phone number or email.",
        ["guardian1_phone"] = "Optional. Siblings share one guardian when the phone number is the same.",
        ["guardian1_email"] = "Optional.",
        ["guardian1_relationship"] = "Optional, for example Mother.",
        ["guardian2_name"] = "Optional. Required when you give the guardian's phone number or email.",
        ["guardian2_phone"] = "Optional.",
        ["guardian2_email"] = "Optional.",
        ["guardian2_relationship"] = "Optional, for example Father.",
    };

    private static readonly ImportColumns AllColumns = new(StudentImportColumns.All
        .Select(c => new ImportColumn(c, c, StudentImportColumns.Required.Contains(c), Help.GetValueOrDefault(c, ""))).ToList());

    private static readonly string[] Notes =
    [
        "One student per row on the Students sheet. Delete nothing from the heading row.",
        "Required: admission_no, first_name, last_name, gender and class.",
        "gender: M or F (Male or Female also work).",
        "date_of_birth: 2012-03-04 or 04/03/2012 (day, month, year).",
        "class: the name of a class in the current session, for example JSS 1A.",
        "Guardians are optional. A guardian name is required when you give a phone number or email.",
        "Siblings share one guardian when the phone number (or email) is the same.",
        "A student whose admission_no is already in the school is updated instead of added.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) =>
        access.IsLeadership ? Task.CompletedTask : throw new ForbiddenException("Only the proprietor or principal can import students.");

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    // ---------- Checking rows ----------

    private sealed record GuardianInput(string Name, string? Phone, string? Email, string Relationship);
    private sealed record Parsed(Guid SessionId, StudentService.Clean Student, Gender Gender, Guid ClassId, List<GuardianInput> Guardians, Guid? ExistingId);

    /// <summary>The row in the older student preview's shape, and what to save (null on an error row).</summary>
    private sealed record Plan(ImportRowDto Dto, Parsed? Parsed);

    /// <summary>The older <c>/students/import</c> row shape of an evaluated row.</summary>
    internal static ImportRowDto Dto(ImportRowResult row) => ((Plan)row.Plan!).Dto;

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sessionId = await students.CurrentSessionIdAsync(ct);
        var classes = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == sessionId).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var classByKey = new Dictionary<string, (Guid Id, string Name)>();
        foreach (var c in classes) classByKey.TryAdd(StudentImport.ClassKey(c.Name), (c.Id, c.Name));

        var numbers = rows.Select(r => r.Cells["admission_no"].ToLowerInvariant()).Where(n => n.Length > 0).Distinct().ToList();
        var existingRows = await db.Students.AsNoTracking().Where(s => numbers.Contains(s.AdmissionNo.ToLower()))
            .Select(s => new { s.Id, s.AdmissionNo, s.Status }).ToListAsync(ct);
        var existing = existingRows.GroupBy(s => s.AdmissionNo.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var existingIds = existing.Values.Select(s => s.Id).ToList();
        var currentClass = (await db.Enrolments.AsNoTracking().Where(e => e.SessionId == sessionId && existingIds.Contains(e.StudentId))
            .Select(e => new { e.StudentId, e.ClassId }).ToListAsync(ct)).ToDictionary(e => e.StudentId, e => e.ClassId);

        var rowsByNumber = rows.Where(r => r.Cells["admission_no"].Length > 0).GroupBy(r => r.Cells["admission_no"].ToLowerInvariant())
            .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Select(r => r.Row).OrderBy(n => n).ToList());

        var evaluated = new List<ImportRowResult>();
        foreach (var row in rows)
        {
            var c = row.Cells;
            var errors = new List<string>();
            string[] nameParts = [c["first_name"], c["middle_name"], c["last_name"]];
            var display = string.Join(' ', nameParts.Where(n => n.Length > 0));
            var className = c["class"];

            var number = c["admission_no"];
            if (rowsByNumber.TryGetValue(number.ToLowerInvariant(), out var sameRows))
                errors.Add($"admission number {number} is on rows {string.Join(", ", sameRows.Take(sameRows.Count - 1))} and {sameRows[^1]}. Each student needs their own admission number.");

            DateOnly? dob = null;
            if (!StudentImport.TryParseDate(c["date_of_birth"], out dob))
                errors.Add("the date of birth must look like 2012-03-04 or 04/03/2012.");

            StudentService.Clean? clean = null;
            try
            {
                clean = students.Validate(number, c["first_name"], c["middle_name"], c["last_name"], dob);
            }
            catch (BadRequestException ex)
            {
                errors.Add(Lower(ex.Message));
            }

            if (!StudentImport.TryParseGender(c["gender"], out var gender)) errors.Add("enter the gender as M or F.");

            Guid? classId = null;
            if (className.Length == 0) errors.Add("enter the class.");
            else if (classByKey.TryGetValue(StudentImport.ClassKey(className), out var found)) { classId = found.Id; className = found.Name; }
            else errors.Add($"class '{className}' doesn't exist.");

            var guardians = new List<GuardianInput>();
            for (var g = 1; g <= 2; g++)
            {
                var name = c[$"guardian{g}_name"];
                var phone = c[$"guardian{g}_phone"];
                var email = c[$"guardian{g}_email"];
                var relationship = c[$"guardian{g}_relationship"];
                if (name.Length == 0 && phone.Length == 0 && email.Length == 0 && relationship.Length == 0) continue;
                try
                {
                    var cleaned = StudentService.CleanGuardian(new SaveGuardianRequest(name, phone, email, relationship, null));
                    guardians.Add(new GuardianInput(cleaned.Name, cleaned.Phone, cleaned.Email, cleaned.Relationship));
                }
                catch (BadRequestException ex)
                {
                    errors.Add($"guardian {g}: {Lower(ex.Message)}");
                }
            }

            Guid? existingId = null;
            if (clean is not null && existing.TryGetValue(clean.AdmissionNo.ToLowerInvariant(), out var match))
            {
                existingId = match.Id;
                if (match.Status != StudentStatus.Active && classId is { } wanted && currentClass.GetValueOrDefault(match.Id) != wanted)
                    errors.Add($"{clean.AdmissionNo} is not an active student, so they can't move class. Set them to active first.");
            }

            if (errors.Count > 0)
            {
                var message = $"Row {row.Row}: {string.Join(" ", errors.Select(Sentence))}";
                evaluated.Add(new ImportRowResult(row.Row, ImportRowStatus.Error, Label(number, display), message,
                    new Plan(new ImportRowDto(row.Row, "Error", number, display, className, message), null)));
                continue;
            }
            var status = existingId is null ? ImportRowStatus.New : ImportRowStatus.Update;
            evaluated.Add(new ImportRowResult(row.Row, status, Label(clean!.AdmissionNo, display), null,
                new Plan(new ImportRowDto(row.Row, status.ToString(), clean.AdmissionNo, display, className, null),
                    new Parsed(sessionId, clean, gender, classId!.Value, guardians, existingId))));
        }
        return evaluated;
    }

    private static string Label(string admissionNo, string name) => $"{admissionNo} {name}".Trim();

    private static string Lower(string message) => message.Length > 0 ? char.ToLowerInvariant(message[0]) + message[1..] : message;

    /// <summary>"Row 5: " is added once, in front of the first error; each error then reads as a sentence.</summary>
    private static string Sentence(string error) => error.Length > 0 ? char.ToUpperInvariant(error[0]) + error[1..] : error;

    // ---------- Plan room ----------

    /// <summary>How many more active students the plan allows, or null when it has no limit.</summary>
    internal async Task<int?> RoomAsync(CancellationToken ct)
    {
        var limit = (await plans.GetCurrentPlanAsync(ct)).Limit(LimitKeys.Students);
        if (limit < 0) return null;
        var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        return (int)Math.Max(0, limit - active);
    }

    private static string OverPlan(int room, int adds) =>
        $"Your plan has room for {room} more students and this file adds {adds}. Upgrade your plan or remove rows, then upload the file again.";

    public async Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var adds = rows.Count(r => r.Status == ImportRowStatus.New);
        return await RoomAsync(ct) is { } room && adds > room ? [OverPlan(room, adds)] : [];
    }

    // ---------- Saving rows ----------

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var adds = rows.Count(r => r.Status == ImportRowStatus.New);
        if (await RoomAsync(ct) is { } room && adds > room) throw new ConflictException(OverPlan(room, adds), "import_over_plan");

        var tenantId = TenantId;
        var today = SchoolCalendar.Today(clock.GetUtcNow());
        var parsedRows = rows.Select(r => ((Plan)r.Plan!).Parsed!).ToList();
        var updateIds = parsedRows.Where(p => p.ExistingId is not null).Select(p => p.ExistingId!.Value).ToList();
        var tracked = await db.Students.Where(s => updateIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var sessionIds = parsedRows.Select(p => p.SessionId).Distinct().ToList();
        var enrolments = await db.Enrolments.Where(e => sessionIds.Contains(e.SessionId) && updateIds.Contains(e.StudentId)).ToDictionaryAsync(e => e.StudentId, ct);
        var links = await db.StudentGuardians.Where(l => updateIds.Contains(l.StudentId)).ToListAsync(ct);
        var linked = links.Select(l => (l.StudentId, l.GuardianId)).ToHashSet();
        var hasPrimary = links.Where(l => l.IsPrimary).Select(l => l.StudentId).ToHashSet();

        // Guardians created or found earlier in this file, so siblings in later rows share them.
        var byPhone = new Dictionary<string, Guardian>();
        var byEmail = new Dictionary<string, Guardian>();

        async Task<Guardian> ResolveAsync(GuardianInput input)
        {
            Guardian? guardian = null;
            if (input.Phone is not null) byPhone.TryGetValue(input.Phone, out guardian);
            if (guardian is null && input.Email is not null) byEmail.TryGetValue(input.Email, out guardian);
            guardian ??= await students.GuardianMatchAsync(input.Phone, input.Email, ct);
            if (guardian is null)
            {
                guardian = new Guardian { TenantId = tenantId, FullName = input.Name, Phone = input.Phone, Email = input.Email };
                db.Guardians.Add(guardian);
            }
            else
            {
                guardian.Phone ??= input.Phone; // a sibling's guardian gains the contact we just learned
                guardian.Email ??= input.Email;
            }
            if (guardian.Phone is not null) byPhone[guardian.Phone] = guardian;
            if (guardian.Email is not null) byEmail[guardian.Email] = guardian;
            return guardian;
        }

        int created = 0, updated = 0;
        foreach (var p in parsedRows)
        {
            Guid studentId;
            if (p.ExistingId is { } id)
            {
                var student = tracked[id];
                student.FirstName = p.Student.FirstName;
                student.MiddleName = p.Student.MiddleName;
                student.LastName = p.Student.LastName;
                student.Gender = p.Gender;
                student.DateOfBirth = p.Student.DateOfBirth;
                if (enrolments.TryGetValue(id, out var enrolment)) enrolment.ClassId = p.ClassId;
                else db.Enrolments.Add(new Enrolment { TenantId = tenantId, StudentId = id, SessionId = p.SessionId, ClassId = p.ClassId });
                studentId = id;
                updated++;
            }
            else
            {
                var student = new Student
                {
                    TenantId = tenantId, AdmissionNo = p.Student.AdmissionNo, FirstName = p.Student.FirstName, MiddleName = p.Student.MiddleName,
                    LastName = p.Student.LastName, Gender = p.Gender, DateOfBirth = p.Student.DateOfBirth, AdmittedAt = today,
                };
                db.Students.Add(student);
                db.Enrolments.Add(new Enrolment { TenantId = tenantId, StudentId = student.Id, SessionId = p.SessionId, ClassId = p.ClassId });
                studentId = student.Id;
                created++;
            }

            foreach (var input in p.Guardians)
            {
                var guardian = await ResolveAsync(input);
                if (!linked.Add((studentId, guardian.Id))) continue;
                var primary = hasPrimary.Add(studentId);
                db.StudentGuardians.Add(new StudentGuardian
                {
                    TenantId = tenantId, StudentId = studentId, GuardianId = guardian.Id, Relationship = input.Relationship, IsPrimary = primary,
                });
            }
        }
        await students.SaveAsync(ct);
        return new ImportApplied(created, updated);
    }
}
