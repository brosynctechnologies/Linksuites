using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Which subjects each class of the current session takes, and who teaches them (kind <c>class-subjects</c>). Leadership only. A class and
/// subject pair the school already has updates its subject teacher (blank keeps the one set); a new pair is added through
/// <see cref="AcademicService.AddClassSubjectAsync"/>. The teacher is set through <see cref="AcademicService.SetTeacherAsync"/>.
/// </summary>
public sealed class ClassSubjectImporter(IAppDbContext db, SchoolAccess access, AcademicService academic) : IImporter, IImportNotice
{
    public const string KindName = "class-subjects";
    public const string ClassMissing = "This class was not found.";
    public const string SubjectMissing = "This subject was not found.";
    public const string Twice = "This class and subject appear more than once in the file.";

    public string Kind => KindName;
    public string SheetName => "Class subjects";
    public string NoticeTitle => "Class subject import finished";
    public string NoticeLink => "/app/setup/classes";

    private static readonly ImportColumns AllColumns = new(
    [
        new("class", "class", true, "A class of the current session, for example JSS 1A."),
        new("subject", "subject", true, "The subject's name or code, for example Mathematics or MTH."),
        new("teacher", "teacher", false, "Optional. The email of the subject teacher, who must already be on the staff list. Blank keeps the teacher set."),
    ]);

    private static readonly string[] Notes =
    [
        "One class and subject per row on the Class subjects sheet.",
        "class and subject are required. Add the classes and subjects first, on their pages or with their own imports.",
        "A class and subject the school already has changes that subject's teacher; a new pair adds the subject to the class.",
        "teacher: optional, the staff member's email. Invite new staff first; a pending invitation is not on the staff list yet.",
        "Each class and subject can appear once in the file.",
        "Up to 2,000 rows and 5 MB per file.",
    ];

    private sealed record Plan(Guid ClassId, Guid SubjectId, Guid? Teacher, bool Exists);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public Task EnsureAccessAsync(ImportScope scope, CancellationToken ct) => SetupImport.EnsureLeadership(access, "class subjects");

    public Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct) =>
        Task.FromResult(new ImportTemplate(SheetName, AllColumns.All, [], Notes));

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sessionId = await db.AcademicSessions.AsNoTracking().Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        var classes = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == sessionId).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().Select(s => new { s.Id, s.Name, s.Code }).ToListAsync(ct);
        var classIds = classes.Select(c => c.Id).ToList();
        var links = await db.ClassSubjects.AsNoTracking().Where(cs => classIds.Contains(cs.ClassId))
            .Select(cs => new { cs.ClassId, cs.SubjectId, cs.TeacherStaffId }).ToListAsync(ct);
        var staff = await SetupImport.StaffByEmailAsync(db, ct);

        var checkedRows = rows.Select(row =>
        {
            var classText = row.Cells.GetValueOrDefault("class") ?? "";
            var subjectText = row.Cells.GetValueOrDefault("subject") ?? "";
            var label = $"{classText} / {subjectText}";
            string? error = null;
            var cls = classes.FirstOrDefault(c => StudentImport.ClassKey(c.Name) == StudentImport.ClassKey(classText));
            var subject = subjects.FirstOrDefault(s => s.Name.Equals(subjectText, StringComparison.OrdinalIgnoreCase))
                ?? subjects.FirstOrDefault(s => s.Code.Equals(subjectText, StringComparison.OrdinalIgnoreCase));
            Guid? teacher = null;
            if (classText.Length == 0) error = "Enter the class.";
            else if (cls is null) error = ClassMissing;
            else if (subjectText.Length == 0) error = "Enter the subject.";
            else if (subject is null) error = SubjectMissing;
            else
            {
                label = $"{cls.Name} / {subject.Name}";
                teacher = SetupImport.Staff(staff, row.Cells.GetValueOrDefault("teacher") ?? "", out error);
            }
            return (Row: row, Label: label, ClassId: cls?.Id, SubjectId: subject?.Id, Teacher: teacher, Error: error);
        }).ToList();

        var twice = checkedRows.Where(r => r.Error is null).GroupBy(r => (r.ClassId, r.SubjectId)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

        return checkedRows.Select(r =>
        {
            var error = r.Error ?? (twice.Contains((r.ClassId, r.SubjectId)) ? Twice : null);
            if (error is not null) return new ImportRowResult(r.Row.Row, ImportRowStatus.Error, r.Label, error, null);
            var link = links.FirstOrDefault(l => l.ClassId == r.ClassId && l.SubjectId == r.SubjectId);
            if (link is null)
                return new ImportRowResult(r.Row.Row, ImportRowStatus.New, r.Label, null, new Plan(r.ClassId!.Value, r.SubjectId!.Value, r.Teacher, false));
            return r.Teacher is null || r.Teacher == link.TeacherStaffId
                ? new ImportRowResult(r.Row.Row, ImportRowStatus.Unchanged, r.Label, null, null)
                : new ImportRowResult(r.Row.Row, ImportRowStatus.Update, r.Label, null, new Plan(link.ClassId, link.SubjectId, r.Teacher, true));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        int created = 0, updated = 0;
        foreach (var row in rows)
        {
            if (row.Plan is not Plan plan) continue;
            if (plan.Exists)
            {
                await academic.SetTeacherAsync(plan.ClassId, plan.SubjectId, new SetTeacherRequest(plan.Teacher), ct);
                updated++;
            }
            else
            {
                await academic.AddClassSubjectAsync(plan.ClassId, new AddClassSubjectRequest(plan.SubjectId, plan.Teacher), ct);
                created++;
            }
        }
        return new ImportApplied(created, updated);
    }
}
