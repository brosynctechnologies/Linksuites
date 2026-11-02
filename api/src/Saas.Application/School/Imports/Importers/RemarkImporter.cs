using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Subject comments and overall remarks for one class in one term, from a roster template: one row per student in the class's results, one
/// column per subject the caller sees, then <c>class_teacher_remark</c> and <c>principal_remark</c> as the caller may write them, the saved
/// text already in the cells. The rules are the results screens': a subject teacher sees and writes only the subjects they teach; leadership
/// and the class teacher see every subject, but only leadership and the subject's own teacher change its comment; the class teacher's remark
/// is the class teacher's or leadership's; the principal's remark is leadership's only, and a filled <c>principal_remark</c> in anyone else's
/// file is an error row. Text is trimmed as the screens trim it and limited to <see cref="ResultService.MaxRemarkLength"/> characters. A blank
/// cell keeps what is saved and an equal value is unchanged. Published results refuse the import (preview and confirm) with the screens' 409.
/// Saves go through <see cref="ResultService.SetSubjectCommentAsync"/> and the remark save, which check access, the publish freeze and the
/// lock again and join the engine's transaction. The bursar is refused before anything is looked up; a teacher with nothing in the class gets
/// "Class was not found.".
/// </summary>
public sealed class RemarkImporter(IAppDbContext db, SchoolAccess access, ResultService results)
    : IImporter, IImportNotice, IImportScopeColumns
{
    public const string KindName = "remarks";
    public const string Sheet = "Remarks";
    public const string TeacherKey = "class_teacher_remark";
    public const string PrincipalKey = "principal_remark";
    public const string PrincipalOnly = "Only the principal writes the principal's remark.";
    public const string ClassTeacherOnly = "Only the class teacher writes the class teacher's remark.";
    private const string ListName = "Comments and remarks";
    private static readonly int Max = ResultService.MaxRemarkLength;

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Comments and remarks import finished";
    public string NoticeLink => "/app/results";

    private static readonly ImportColumns FixedColumns = new([ClassRoster.AdmissionColumn, ClassRoster.NameColumn, SheetFor.Column, RowVersion.Column]);

    private static readonly ImportColumn TeacherColumn = new(TeacherKey, TeacherKey, false,
        $"The class teacher's remark, up to {Max} characters. The class teacher or the principal writes it. Leave blank to keep the remark already saved.");
    private static readonly ImportColumn PrincipalColumn = new(PrincipalKey, PrincipalKey, false,
        $"The principal's remark, up to {Max} characters. Only the principal writes it. Leave blank to keep the remark already saved.");

    public ImportColumns Columns(ImportScope scope) => FixedColumns;

    /// <summary>Both remark columns are always read, so a hand-added one the caller may not write is an error row, not an ignored column.</summary>
    public async Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct) =>
        [.. (await LoadAsync(scope, ct)).Subjects.Select(s => s.Column), TeacherColumn, PrincipalColumn];

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not read results.");
        var classId = scope.RequireGuid("classId");
        scope.RequireGuid("termId");
        if (!await db.SchoolClasses.AsNoTracking().AnyAsync(c => c.Id == classId, ct)) throw new NotFoundException("Class");
        if (access.IsLeadership) return;
        // The class teacher, or a teacher of one of the class's subjects, as on the results page; anyone else is told the class doesn't exist.
        if (!await access.TeachesClassAsync(classId, ct)) throw new NotFoundException("Class");
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Students.Select(s => (IReadOnlyList<string>)
        [
            s.AdmissionNo, s.Name,
            .. sheet.Subjects.Select(c => s.Comments.GetValueOrDefault(c.ClassSubjectId) ?? ""),
            .. sheet.CanWriteTeacher ? new[] { s.TeacherRemark ?? "" } : [],
            .. sheet.Leadership ? new[] { s.PrincipalRemark ?? "" } : [],
            sheet.SheetFor,
            RowVersion.Of(Stored(sheet, s)),
        ]).ToList();

        var notes = new List<string>
        {
            $"Session: {sheet.SessionName}. Class: {sheet.ClassName}. Term: {sheet.TermName}. Upload this file only on that class and term.",
            "One row per student in the class's results. Keep admission_no and sheet_for as they are; the student column is only there to help you.",
            $"sheet_for says which session, class and term the file is for ({sheet.SheetFor}). A file uploaded on another session, class or term is refused.",
        };
        if (sheet.Subjects.Count > 0)
        {
            notes.Add(sheet.Leadership
                ? "There is a column for each subject, holding its comment."
                : sheet.Subjects.All(s => s.Writable)
                    ? "There is a column for each subject you teach in this class, holding its comment."
                    : "There is a column for every subject. You change the comment only for subjects you teach; leave the other subjects as they are.");
            notes.Add("A subject comment needs the student's result in that subject, so enter their scores first.");
        }
        notes.Add($"Each comment or remark can have at most {Max} characters. Spaces at the start and end are dropped, as on the results page.");
        notes.Add("A blank cell keeps the comment or remark already saved. To clear one, use the results page. A row you did not change is left as it is.");
        notes.Add("The hidden row_version column records the saved comments and remarks you can change for each student when you downloaded this file. If they are changed on the results page after that, the student's row is refused: download the template again.");
        if (!sheet.Leadership) notes.Add("Only the principal writes the principal's remark, so this file has no principal_remark column.");
        if (sheet.Published) notes.Add("These results are published, so this file can't be uploaded until the principal unpublishes them.");

        return new ImportTemplate(Sheet,
            [
                ClassRoster.AdmissionColumn, ClassRoster.NameColumn, .. sheet.Subjects.Select(s => s.Column),
                .. sheet.CanWriteTeacher ? new[] { TeacherColumn } : [], .. sheet.Leadership ? new[] { PrincipalColumn } : [], SheetFor.Column, RowVersion.Column,
            ],
            rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        if (sheet.Published) throw new ConflictException(ResultService.PublishedMessage, "results_published");
        ClassRoster.EnsureAnyColumn(rows,
            [.. sheet.Subjects.Select(s => s.Column), .. sheet.CanWriteTeacher ? new[] { TeacherColumn } : [], .. sheet.Leadership ? new[] { PrincipalColumn } : []]);
        var roster = sheet.Students.GroupBy(s => s.AdmissionNo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First());

        return RosterRows.Match(roster, rows).Select(match =>
        {
            var wrongSheet = SheetFor.Check(match.Row, sheet.SheetFor, ClassRoster.DownloadFor);
            if (wrongSheet is not null || match.Enrolment is not { } student) return ClassRoster.ErrorRow(match.Row, wrongSheet ?? match.Error!);
            ImportRowResult Error(string message) => new(match.Row.Row, ImportRowStatus.Error, student.Label, message, null);

            // A filled cell in a column the screen keeps closed to this person is refused, whatever it holds.
            if (!sheet.CanWriteTeacher && Text(match.Row, TeacherKey).Length > 0) return Error(ClassTeacherOnly);
            if (!sheet.Leadership && Text(match.Row, PrincipalKey).Length > 0) return Error(PrincipalOnly);

            var comments = new List<(Guid ClassSubjectId, string Comment)>();
            var updates = false;
            foreach (var subject in sheet.Subjects)
            {
                var text = Text(match.Row, subject.Column.Key);
                if (text.Length == 0) continue; // blank keeps what is saved
                if (text.Length > Max) return Error($"The {subject.Name} comment can have at most {Max} characters.");
                if (!student.Comments.TryGetValue(subject.ClassSubjectId, out var stored))
                    return Error($"This student has no {subject.Name} result yet. Enter their scores first.");
                if (string.Equals(stored, text, StringComparison.Ordinal)) continue;
                if (!subject.Writable) return Error($"The subject teacher writes the {subject.Name} comment.");
                if (stored is not null) updates = true;
                comments.Add((subject.ClassSubjectId, text));
            }

            string? teacher = null, principal = null;
            if (sheet.CanWriteTeacher && Text(match.Row, TeacherKey) is { Length: > 0 } t && !string.Equals(t, student.TeacherRemark, StringComparison.Ordinal))
            {
                if (t.Length > Max) return Error($"The class teacher's remark can have at most {Max} characters.");
                if (student.TeacherRemark is not null) updates = true;
                teacher = t;
            }
            if (sheet.Leadership && Text(match.Row, PrincipalKey) is { Length: > 0 } p && !string.Equals(p, student.PrincipalRemark, StringComparison.Ordinal))
            {
                if (p.Length > Max) return Error($"The principal's remark can have at most {Max} characters.");
                if (student.PrincipalRemark is not null) updates = true;
                principal = p;
            }

            var status = comments.Count == 0 && teacher is null && principal is null ? ImportRowStatus.Unchanged
                : updates ? ImportRowStatus.Update : ImportRowStatus.New;
            return RowVersion.Guard(new ImportRowResult(match.Row.Row, status, student.Label, null, new RemarkPlan(student.EnrolmentId, comments, teacher, principal)),
                match.Row, RowVersion.Of(Stored(sheet, student)));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var classId = scope.RequireGuid("classId");
        var termId = scope.RequireGuid("termId");
        // The screens' own saves, one comment and one remark save per student. Each checks access and the publish freeze again under the
        // class-term lock and joins the engine's transaction, so every row lands or none does. Neither sends a message.
        foreach (var row in rows.Where(r => r.Status is ImportRowStatus.New or ImportRowStatus.Update))
        {
            var plan = (RemarkPlan)row.Plan!;
            foreach (var (classSubjectId, comment) in plan.Comments)
                await results.SetSubjectCommentAsync(classId, termId, new SetSubjectCommentRequest(plan.EnrolmentId, classSubjectId, comment), ct);
            if (plan.TeacherRemark is not null || plan.PrincipalRemark is not null)
                await results.SaveRemarksAsync(classId, termId, new SetRemarksRequest(plan.EnrolmentId, plan.TeacherRemark, plan.PrincipalRemark), ct);
        }
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record RemarkPlan(Guid EnrolmentId, IReadOnlyList<(Guid ClassSubjectId, string Comment)> Comments, string? TeacherRemark, string? PrincipalRemark);

    private sealed record SubjectColumn(Guid ClassSubjectId, string Name, ImportColumn Column, bool Writable);

    /// <summary>A student in the class's results. <see cref="Comments"/> holds a key for each subject they have a result in (value null when no comment).</summary>
    private sealed record ResultStudent(Guid EnrolmentId, string Name, string AdmissionNo, string? TeacherRemark, string? PrincipalRemark,
        IReadOnlyDictionary<Guid, string?> Comments)
    {
        public string Label => $"{AdmissionNo} {Name}".Trim();
    }

    private sealed record RemarkSheet(
        string SessionName, string ClassName, string TermName, bool Leadership, bool CanWriteTeacher, bool Published,
        IReadOnlyList<SubjectColumn> Subjects, IReadOnlyList<ResultStudent> Students, string SheetFor);

    /// <summary>
    /// The class's results as the results page shows them to this person (recomputed first when stale, as the page does; the term must be of the
    /// class's session), the subjects they see and which of those they write, and whether they write the class teacher's remark.
    /// </summary>
    private async Task<RemarkSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var classId = scope.RequireGuid("classId");
        var termId = scope.RequireGuid("termId");
        var view = await results.GetAsync(classId, termId, ct);
        var cls = await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == classId, ct);
        var session = await db.AcademicSessions.AsNoTracking().Where(s => s.Id == cls.SessionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";

        var leadership = access.IsLeadership;
        var staffId = leadership ? null : await access.StaffIdAsync(ct);
        var classTeacher = staffId is not null && cls.ClassTeacherStaffId == staffId;

        var subjects = new List<SubjectColumn>();
        if (view.Mode != AssessmentMode.Developmental)
        {
            var classSubjects = await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == classId)
                .Join(db.Subjects.AsNoTracking(), c => c.SubjectId, s => s.Id, (c, s) => new { c.Id, c.TeacherStaffId, s.Name })
                .OrderBy(x => x.Name).ToListAsync(ct);
            var taken = new List<string> { TeacherKey, PrincipalKey };
            foreach (var cs in classSubjects)
            {
                var teaches = staffId is not null && cs.TeacherStaffId == staffId;
                if (!leadership && !classTeacher && !teaches) continue; // a subject teacher sees only their own subjects
                var key = ImportColumns.KeyOf(cs.Name);
                if (!ClassRoster.IsFree(key, taken)) continue;
                taken.Add(key);
                var writable = leadership || teaches;
                subjects.Add(new SubjectColumn(cs.Id, cs.Name, new ImportColumn(key, cs.Name, false, writable
                        ? $"The {cs.Name} comment, up to {Max} characters. Leave blank to keep the comment already saved."
                        : $"The {cs.Name} comment. The subject teacher writes it; leave it as it is."),
                    writable));
            }
        }

        var students = view.Students
            .Select(s => new ResultStudent(s.EnrolmentId, s.Name, s.AdmissionNo, s.TeacherRemark, s.PrincipalRemark,
                s.Subjects.GroupBy(x => x.ClassSubjectId).ToDictionary(g => g.Key, g => g.First().Remark)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.AdmissionNo, StringComparer.OrdinalIgnoreCase).ToList();
        return new RemarkSheet(session, view.ClassName, view.TermName, leadership, leadership || classTeacher, view.PublishedAt is not null,
            subjects, students, SheetFor.Label(session, view.ClassName, ListName, view.TermName));
    }

    /// <summary>
    /// What this person can change for a student, as saved: the comments of the subjects they write, then the class teacher's remark and the
    /// principal's remark when they write them (blank when not saved). The row_version's input.
    /// </summary>
    private static IEnumerable<string> Stored(RemarkSheet sheet, ResultStudent student) =>
    [
        .. sheet.Subjects.Where(c => c.Writable).Select(c => student.Comments.GetValueOrDefault(c.ClassSubjectId) ?? ""),
        .. sheet.CanWriteTeacher ? new[] { student.TeacherRemark ?? "" } : [],
        .. sheet.Leadership ? new[] { student.PrincipalRemark ?? "" } : [],
    ];

    /// <summary>The cell trimmed, as the screens trim a comment or remark before saving it.</summary>
    private static string Text(ImportRowData row, string key) => (row.Cells.GetValueOrDefault(key) ?? "").Trim();
}
