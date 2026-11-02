using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Classes of the current session, one per row (kind <c>classes</c>). Leadership only. A level and arm the session already has updates that
/// class's class teacher (named by staff email; blank keeps the one set); a new level and arm adds the class through
/// <see cref="AcademicService.CreateClassAsync"/>. The class teacher is set through <see cref="StaffService.SetClassTeacherAsync"/>.
/// </summary>
public sealed class ClassImporter(IAppDbContext db, SchoolAccess access, AcademicService academic, StaffService staffService) : IImporter, IImportNotice
{
    public const string KindName = "classes";
    public const string LevelMissing = "This level was not found.";
    public const string Twice = "This class appears more than once in the file.";

    public string Kind => KindName;
    public string SheetName => "Classes";
    public string NoticeTitle => "Class import finished";
    public string NoticeLink => "/app/setup/classes";

    private static readonly ImportColumns AllColumns = new(
    [
        new("level", "level", true, "A level of the school, for example JSS 1."),
        new("arm", "arm", true, "The arm, for example A or Gold. Up to 20 characters."),
        new("class_teacher", "class_teacher", false, "Optional. The email of the class teacher, who must already be on the staff list. Blank keeps the class teacher set."),
    ]);

    private static readonly string[] Notes =
    [
        "One class per row on the Classes sheet. Classes are added to the current session.",
        "level and arm are required. A level and arm the session already has changes that class's class teacher.",
        "class_teacher: optional, the staff member's email. Invite new staff first; a pending invitation is not on the staff list yet.",
        "Each class can appear once in the file.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private sealed record Plan(Guid? ClassId, SaveClassRequest Request);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => SetupImport.EnsureLeadership(access, "classes");

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sessionId = await db.AcademicSessions.AsNoTracking().Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw BadRequestException.Field("sessionId", "Set a current session first.");
        var levels = await db.Levels.AsNoTracking().Select(l => new { l.Id, l.Name }).ToListAsync(ct);
        var classes = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == sessionId)
            .Select(c => new { c.Id, c.LevelId, c.Arm, c.Name, c.ClassTeacherStaffId }).ToListAsync(ct);
        var staff = await SetupImport.StaffByEmailAsync(db, ct);

        var checkedRows = rows.Select(row =>
        {
            var levelText = row.Cells.GetValueOrDefault("level") ?? "";
            var armText = row.Cells.GetValueOrDefault("arm") ?? "";
            var label = $"{levelText} {armText}".Trim();
            try
            {
                if (levelText.Length == 0) throw BadRequestException.Field("level", "Choose a level.");
                var arm = AcademicService.CleanName(armText, "arm", "arm", 20);
                var key = StudentImport.ClassKey(levelText);
                var level = levels.FirstOrDefault(l => StudentImport.ClassKey(l.Name) == key) ?? throw BadRequestException.Field("level", LevelMissing);
                var name = $"{level.Name}{arm}";
                var teacher = SetupImport.Staff(staff, row.Cells.GetValueOrDefault("class_teacher") ?? "", out var staffError);
                if (staffError is not null) throw BadRequestException.Field("class_teacher", staffError);
                return (Row: row, Label: name, Key: StudentImport.ClassKey(name), Level: (Id: level.Id, Name: level.Name), Arm: arm, Teacher: teacher, Error: (string?)null);
            }
            catch (BadRequestException ex)
            {
                return (Row: row, Label: label, Key: "", Level: (Id: Guid.Empty, Name: ""), Arm: "", Teacher: (Guid?)null, Error: (string?)ex.Message);
            }
        }).ToList();

        var twice = checkedRows.Where(r => r.Error is null).GroupBy(r => r.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        return checkedRows.Select(r =>
        {
            var error = r.Error ?? (twice.Contains(r.Key) ? Twice : null);
            if (error is not null) return new ImportRowResult(r.Row.Row, ImportRowStatus.Error, r.Label, error, null);

            var match = classes.FirstOrDefault(c => c.LevelId == r.Level.Id && c.Arm.Equals(r.Arm, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                // The class screen's rule: no other class of the session with this name.
                if (classes.Any(c => c.Name.Equals(r.Label, StringComparison.OrdinalIgnoreCase)))
                    return new ImportRowResult(r.Row.Row, ImportRowStatus.Error, r.Label, $"{r.Label} already exists in this session.", null);
                return new ImportRowResult(r.Row.Row, ImportRowStatus.New, r.Label, null,
                    new Plan(null, new SaveClassRequest(r.Level.Id, r.Arm, sessionId, r.Teacher)));
            }
            return r.Teacher is null || r.Teacher == match.ClassTeacherStaffId
                ? new ImportRowResult(r.Row.Row, ImportRowStatus.Unchanged, match.Name, null, null)
                : new ImportRowResult(r.Row.Row, ImportRowStatus.Update, match.Name, null,
                    new Plan(match.Id, new SaveClassRequest(r.Level.Id, match.Arm, sessionId, r.Teacher)));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Plan is not Plan plan) continue;
            if (plan.ClassId is { } id)
            {
                await staffService.SetClassTeacherAsync(id, new SetClassTeacherRequest(plan.Request.ClassTeacherStaffId), ct);
                updated++;
            }
            else
            {
                await academic.CreateClassAsync(plan.Request, ct);
                created++;
            }
        }
        return new ImportApplied(created, updated);
    }
}
