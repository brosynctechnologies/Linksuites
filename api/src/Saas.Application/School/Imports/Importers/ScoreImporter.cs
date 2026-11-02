using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Scores for one class subject in one term, from a roster template: one row per student, one column per assessment component, the stored
/// scores already in the cells. A blank cell keeps the stored score; a cell equal to it is unchanged and is not sent. The changed cells are
/// saved through the score sheet's own save (<see cref="ScoreService.SaveAsync"/>), so its maximums, roster, lock and staff stamp apply.
/// The bursar is refused before anything is looked up; a teacher reaches only the class subjects they teach (others are 404). A locked term
/// refuses the whole import with the sheet's <c>term_locked</c> 409, on preview and again on confirm; a developmental class is a 400.
/// </summary>
public sealed class ScoreImporter(IAppDbContext db, SchoolAccess access, ScoreService scores) : IImporter, IImportNotice, IImportScopeColumns
{
    public const string KindName = "scores";
    public const string Sheet = "Scores";
    public const string StudentColumn = "student";
    public const string Developmental = "This class uses developmental assessment.";
    public const string Decimals = "Scores can have at most one decimal place.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Score import finished";
    public string NoticeLink => "/app/scores";

    private const NumberStyles ScoreStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private static readonly ImportColumn AdmissionColumn =
        new(RosterRows.AdmissionColumn, RosterRows.AdmissionColumn, true, "The student's admission number, as in the template. Do not change it.");
    private static readonly ImportColumn NameColumn = new(StudentColumn, StudentColumn, false, "The student's name, to help you find the row. It is not imported.");

    /// <summary>The fixed columns. The component columns depend on the class and are read by their headings.</summary>
    private static readonly ImportColumns FixedColumns = new([AdmissionColumn, NameColumn, SheetFor.Column, RowVersion.Column]);

    public ImportColumns Columns(ImportScope scope) => FixedColumns;

    public async Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct) =>
        (await LoadAsync(scope, ct)).Columns.Select(c => c.Column).ToList();

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("You can only enter scores for subjects you teach.");
        var classSubjectId = scope.RequireGuid("classSubjectId");
        scope.RequireGuid("termId");
        // Leadership enters every subject; a teacher only the subjects they teach. Another subject is "not found", never "forbidden".
        if (!await db.ClassSubjects.AsNoTracking().AnyAsync(c => c.Id == classSubjectId, ct)
            || (!access.IsLeadership && !await access.TeachesClassSubjectAsync(classSubjectId, ct)))
            throw new NotFoundException("Class subject");
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Grid.Students.Select(s => (IReadOnlyList<string>)
            [s.AdmissionNo, s.Name, .. Stored(sheet, s), sheet.SheetFor, RowVersion.Of(Stored(sheet, s))]).ToList();
        var notes = new List<string>
        {
            $"Session: {sheet.SessionName}. Class: {sheet.ClassName}. Subject: {sheet.SubjectName}. Term: {sheet.TermName}. Upload this file only on that class, subject and term.",
            "One row per student. Keep admission_no and sheet_for as they are; the student column is only there to help you.",
            $"sheet_for says which session, class, subject and term the file is for ({sheet.SheetFor}). A file uploaded on another session, class, subject or term is refused.",
            $"Enter each score as a number with at most one decimal place: {string.Join(", ", sheet.Grid.Components.Select(c => $"{c.Name} out of {Format(c.MaxScore)}"))}.",
            "A blank cell keeps the score already saved. To clear a score, use the score sheet.",
            "A row you did not change is left as it is.",
            "The hidden row_version column records each student's saved scores when you downloaded this file. If a student's scores are changed on the score sheet after that, their row is refused: download the template again.",
        };
        if (sheet.Grid.Locked) notes.Add("Results are locked for this class this term, so this file can't be uploaded until the principal unlocks them.");
        return new ImportTemplate(Sheet, [AdmissionColumn, NameColumn, .. sheet.Columns.Select(c => c.Column), SheetFor.Column, RowVersion.Column], rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        if (sheet.Grid.Locked) throw new ConflictException(ScoreService.TermLockedMessage, "term_locked");
        if (rows.Count > 0 && !sheet.Columns.Any(c => rows[0].Cells.ContainsKey(c.Column.Key)))
        {
            var names = sheet.Columns.Select(c => c.Column.Heading).ToList();
            throw BadRequestException.Field("file", names.Count == 1
                ? $"The file is missing the column {names[0]}. Download the template to see every column."
                : $"The file is missing the columns {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}. Download the template to see every column.");
        }

        var roster = sheet.Grid.Students.GroupBy(s => s.AdmissionNo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First());
        return RosterRows.Match(roster, rows).Select(match =>
        {
            var cells = match.Row.Cells;
            var wrongSheet = SheetFor.Check(match.Row, sheet.SheetFor);
            if (wrongSheet is not null || match.Enrolment is not { } student)
                return new ImportRowResult(match.Row.Row, ImportRowStatus.Error,
                    $"{cells.GetValueOrDefault(RosterRows.AdmissionColumn)} {cells.GetValueOrDefault(StudentColumn)}".Trim(), wrongSheet ?? match.Error, null);

            var label = $"{student.AdmissionNo} {student.Name}".Trim();
            var entries = new List<ScoreEntryInput>();
            var updates = false;
            foreach (var (column, component) in sheet.Columns)
            {
                var text = (cells.GetValueOrDefault(column.Key) ?? "").Trim();
                if (text.Length == 0) continue; // blank keeps the stored score
                if (!decimal.TryParse(text, ScoreStyle, CultureInfo.InvariantCulture, out var value))
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, label, $"Enter a number for {component.Name}, such as 12 or 12.5.", null);
                if (value < 0 || value > component.MaxScore)
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, label, $"{component.Name} scores must be between 0 and {component.MaxScore:0.#}.", null);
                if (decimal.Round(value, 1) != value)
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, label, Decimals, null);

                var stored = student.Values.GetValueOrDefault(component.Id.ToString());
                if (stored == value) continue;
                if (stored is not null) updates = true;
                entries.Add(new ScoreEntryInput(student.EnrolmentId, component.Id, value));
            }
            var status = entries.Count == 0 ? ImportRowStatus.Unchanged : updates ? ImportRowStatus.Update : ImportRowStatus.New;
            return RowVersion.Guard(new ImportRowResult(match.Row.Row, status, label, null, entries), match.Row, RowVersion.Of(Stored(sheet, student)));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var entries = rows.Where(r => r.Status is ImportRowStatus.New or ImportRowStatus.Update)
            .SelectMany(r => (IReadOnlyList<ScoreEntryInput>)r.Plan!).ToList();
        if (entries.Count > 0)
        {
            var classSubjectId = scope.RequireGuid("classSubjectId");
            var termId = scope.RequireGuid("termId");
            // The sheet's own save. It joins the engine's transaction, so every chunk lands or none does.
            foreach (var chunk in entries.Chunk(ScoreService.MaxEntriesPerSave))
                await scores.SaveAsync(new SaveScoresRequest(classSubjectId, termId, chunk), ct);
        }
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record ComponentColumn(ImportColumn Column, ScoreComponentDto Component);
    private sealed record ScoreSheet(ScoreGridDto Grid, IReadOnlyList<ComponentColumn> Columns, string SessionName, string ClassName, string SubjectName, string TermName, string SheetFor);

    /// <summary>The score sheet as the screen shows it (term checks included), refusing a developmental class.</summary>
    private async Task<ScoreSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var classSubjectId = scope.RequireGuid("classSubjectId");
        var termId = scope.RequireGuid("termId");
        var grid = await scores.GetAsync(classSubjectId, termId, ct);
        var cls = await db.SchoolClasses.AsNoTracking().Where(c => c.Id == grid.ClassId)
            .Select(c => new
            {
                c.Name,
                Mode = db.Levels.Where(l => l.Id == c.LevelId).Select(l => l.AssessmentMode).FirstOrDefault(),
                Session = db.AcademicSessions.Where(s => s.Id == c.SessionId).Select(s => s.Name).FirstOrDefault(),
            }).FirstAsync(ct);
        if (cls.Mode == AssessmentMode.Developmental) throw new BadRequestException(Developmental);
        var subjectName = await db.ClassSubjects.AsNoTracking().Where(c => c.Id == classSubjectId)
            .Select(c => db.Subjects.Where(s => s.Id == c.SubjectId).Select(s => s.Name).FirstOrDefault()).FirstOrDefaultAsync(ct) ?? "";
        var termName = await db.Terms.AsNoTracking().Where(t => t.Id == termId).Select(t => t.Name).FirstAsync(ct);

        var columns = new List<ComponentColumn>();
        foreach (var c in grid.Components)
        {
            var key = ImportColumns.KeyOf(c.Name);
            if (key.Length == 0 || key is RosterRows.AdmissionColumn or StudentColumn or SheetFor.ColumnKey or RowVersion.ColumnKey || columns.Any(x => x.Column.Key == key)) continue;
            columns.Add(new ComponentColumn(new ImportColumn(key, c.Name, false,
                $"Out of {Format(c.MaxScore)}, with at most one decimal place. Leave blank to keep the score already saved."), c));
        }
        return new ScoreSheet(grid, columns, cls.Session ?? "", cls.Name, subjectName, termName, SheetFor.Label(cls.Session ?? "", cls.Name, subjectName, termName));
    }

    private static string Format(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A student's stored scores in column order, as the template writes them (blank when not entered): the row_version's input.</summary>
    private static IEnumerable<string> Stored(ScoreSheet sheet, ScoreStudentDto student) =>
        sheet.Columns.Select(c => student.Values.GetValueOrDefault(c.Component.Id.ToString()) is { } v ? Format(v) : "");
}
