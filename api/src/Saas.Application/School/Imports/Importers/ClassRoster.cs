using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>One active student of a class's roster, as a class-scoped roster template lists them.</summary>
internal sealed record RosterStudent(Guid StudentId, Guid EnrolmentId, string Name, string AdmissionNo)
{
    public string Label => $"{AdmissionNo} {Name}".Trim();
}

/// <summary>A class, one term of its session, the session's name and the class's active students.</summary>
internal sealed record ClassTermRoster(SchoolClass Class, Term Term, string SessionName, IReadOnlyList<RosterStudent> Students)
{
    /// <summary>Roster keyed by admission number (any case), for <see cref="RosterRows.Match{T}"/>.</summary>
    public IReadOnlyDictionary<string, RosterStudent> ByAdmissionNo =>
        Students.GroupBy(s => s.AdmissionNo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First());
}

/// <summary>
/// Shared by the roster imports scoped to a class and a term (trait and developmental ratings): the "class teacher or leadership" check the
/// child page makes, the class, term and roster, the fixed columns and the missing-columns message.
/// </summary>
internal static class ClassRoster
{
    public const string StudentColumn = "student";
    public const string DownloadFor = "this class and term";

    public static readonly ImportColumn AdmissionColumn =
        new(RosterRows.AdmissionColumn, RosterRows.AdmissionColumn, true, "The student's admission number, as in the template. Do not change it.");
    public static readonly ImportColumn NameColumn = new(StudentColumn, StudentColumn, false, "The student's name, to help you find the row. It is not imported.");

    /// <summary>The class teacher of the class, or leadership. Anyone else, and a class that doesn't exist, is "Class was not found."</summary>
    public static async Task EnsureClassTeacherAsync(IAppDbContext db, SchoolAccess access, Guid classId, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.AsNoTracking().Where(c => c.Id == classId).Select(c => new { c.ClassTeacherStaffId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Class");
        if (access.IsLeadership) return;
        var staffId = await access.StaffIdAsync(ct);
        if (staffId is null || cls.ClassTeacherStaffId != staffId) throw new NotFoundException("Class");
    }

    /// <summary>The class, the term (which must be of the class's session, as on the score sheet) and the class's active students.</summary>
    public static async Task<ClassTermRoster> LoadAsync(IAppDbContext db, Guid classId, Guid termId, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");
        var session = await db.AcademicSessions.AsNoTracking().Where(s => s.Id == cls.SessionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var students = await db.Enrolments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.SessionId == cls.SessionId)
            .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id,
                (e, s) => new { s.Id, EnrolmentId = e.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        return new ClassTermRoster(cls, term, session,
            students.Select(x => new RosterStudent(x.Id, x.EnrolmentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList());
    }

    /// <summary>A column key is free when it is not a fixed column and not already taken by an earlier column.</summary>
    public static bool IsFree(string key, IEnumerable<string> taken) =>
        key.Length > 0 && key is not (RosterRows.AdmissionColumn or StudentColumn or SheetFor.ColumnKey or RowVersion.ColumnKey) && !taken.Contains(key);

    /// <summary>400 when the file has none of the scope's columns (for example no trait at all), naming them.</summary>
    public static void EnsureAnyColumn(IReadOnlyList<ImportRowData> rows, IReadOnlyList<ImportColumn> columns)
    {
        if (rows.Count == 0 || columns.Count == 0 || columns.Any(c => rows[0].Cells.ContainsKey(c.Key))) return;
        var names = columns.Select(c => c.Heading).ToList();
        throw BadRequestException.Field("file", names.Count == 1
            ? $"The file is missing the column {names[0]}. Download the template to see every column."
            : $"The file is missing the columns {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}. Download the template to see every column.");
    }

    /// <summary>"a, b or c".</summary>
    public static string OrList(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join("", items) : $"{string.Join(", ", items.Take(items.Count - 1))} or {items[^1]}";

    /// <summary>The error result for a row that is not on the roster or names another sheet.</summary>
    public static ImportRowResult ErrorRow(ImportRowData row, string message) =>
        new(row.Row, ImportRowStatus.Error,
            $"{row.Cells.GetValueOrDefault(RosterRows.AdmissionColumn)} {row.Cells.GetValueOrDefault(StudentColumn)}".Trim(), message, null);
}
