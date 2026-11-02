using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Psychomotor or affective ratings for one class in one term, from a roster template: one row per student, one column per item on the
/// class's list (1 to 5), the stored ratings already in the cells. A blank cell keeps the stored rating; a cell equal to it is unchanged and is
/// not sent. Each changed student is saved through the child page's own save (<see cref="TraitService.SaveRatingsAsync"/>), so its access,
/// range and list checks apply. The class teacher or leadership only (anyone else gets "Class was not found."); the bursar is refused before
/// anything is looked up. Like the child page, a lock or publish does not freeze ratings.
/// </summary>
public sealed class TraitImporter(IAppDbContext db, SchoolAccess access, TraitService traits) : IImporter, IImportNotice, IImportScopeColumns
{
    public const string KindName = "traits";
    public const string Sheet = "Traits";
    public const string RateOneToFive = "Rate each item from 1 to 5.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Trait rating import finished";
    public string NoticeLink => "/app/results";

    private static readonly ImportColumns FixedColumns = new([ClassRoster.AdmissionColumn, ClassRoster.NameColumn, SheetFor.Column, RowVersion.Column]);

    public ImportColumns Columns(ImportScope scope) => FixedColumns;

    public async Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct) =>
        (await LoadAsync(scope, ct)).Columns.Select(c => c.Column).ToList();

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not rate traits.");
        var classId = scope.RequireGuid("classId");
        scope.RequireGuid("termId");
        RequireKind(scope);
        await ClassRoster.EnsureClassTeacherAsync(db, access, classId, ct);
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Roster.Students.Select(s => (IReadOnlyList<string>)
        [
            s.AdmissionNo, s.Name,
            .. Stored(sheet, s),
            sheet.SheetFor,
            RowVersion.Of(Stored(sheet, s)),
        ]).ToList();
        var kind = KindLabel(sheet.Kind);
        var notes = new List<string>
        {
            $"Session: {sheet.Roster.SessionName}. Class: {sheet.Roster.Class.Name}. List: {kind}. Term: {sheet.Roster.Term.Name}. Upload this file only on that class, list and term.",
            "One row per student. Keep admission_no and sheet_for as they are; the student column is only there to help you.",
            $"sheet_for says which session, class, list and term the file is for ({sheet.SheetFor}). A file uploaded on another session, class, list or term is refused.",
            $"Rate each item from 1 to 5: {string.Join(", ", sheet.Columns.Select(c => c.Column.Heading))}.",
            "A blank cell keeps the rating already saved. A row you did not change is left as it is.",
            "The hidden row_version column records each student's saved ratings when you downloaded this file. If a student's ratings are changed on the screen after that, their row is refused: download the template again.",
        };
        return new ImportTemplate(Sheet, [ClassRoster.AdmissionColumn, ClassRoster.NameColumn, .. sheet.Columns.Select(c => c.Column), SheetFor.Column, RowVersion.Column], rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        ClassRoster.EnsureAnyColumn(rows, sheet.Columns.Select(c => c.Column).ToList());

        return RosterRows.Match(sheet.Roster.ByAdmissionNo, rows).Select(match =>
        {
            var wrongSheet = SheetFor.Check(match.Row, sheet.SheetFor, ClassRoster.DownloadFor);
            if (wrongSheet is not null || match.Enrolment is not { } student) return ClassRoster.ErrorRow(match.Row, wrongSheet ?? match.Error!);

            var ratings = new List<SaveTraitRating>();
            var updates = false;
            foreach (var column in sheet.Columns)
            {
                var text = (match.Row.Cells.GetValueOrDefault(column.Column.Key) ?? "").Trim();
                if (text.Length == 0) continue; // blank keeps the stored rating
                if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                    || number != decimal.Truncate(number) || number is < 1 or > 5)
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label, RateOneToFive, null);
                var value = (int)number;
                var stored = sheet.Stored.TryGetValue((student.EnrolmentId, column.CriterionId), out var v) ? v : (int?)null;
                if (stored == value) continue;
                if (stored is not null) updates = true;
                ratings.Add(new SaveTraitRating(column.CriterionId, value));
            }
            var status = ratings.Count == 0 ? ImportRowStatus.Unchanged : updates ? ImportRowStatus.Update : ImportRowStatus.New;
            return RowVersion.Guard(new ImportRowResult(match.Row.Row, status, student.Label, null, new TraitPlan(student.StudentId, ratings)),
                match.Row, RowVersion.Of(Stored(sheet, student)));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var termId = scope.RequireGuid("termId");
        var kind = RequireKind(scope);
        // The child page's own save, one student at a time. It joins the engine's transaction, so every student lands or none does.
        foreach (var row in rows.Where(r => r.Status is ImportRowStatus.New or ImportRowStatus.Update))
        {
            var plan = (TraitPlan)row.Plan!;
            await traits.SaveRatingsAsync(plan.StudentId, termId, kind, new SaveTraitRatingsRequest(plan.Ratings), ct);
        }
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record TraitPlan(Guid StudentId, IReadOnlyList<SaveTraitRating> Ratings);
    private sealed record CriterionColumn(ImportColumn Column, Guid CriterionId);
    private sealed record TraitSheet(ClassTermRoster Roster, TraitKind Kind, IReadOnlyList<CriterionColumn> Columns, IReadOnlyDictionary<(Guid, Guid), int> Stored, string SheetFor);

    /// <summary>The class's list for the kind (its level's own, or the school's), the roster and the stored ratings.</summary>
    private async Task<TraitSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var kind = RequireKind(scope);
        var roster = await ClassRoster.LoadAsync(db, scope.RequireGuid("classId"), scope.RequireGuid("termId"), ct);
        var criteria = (await traits.GetCriteriaAsync(kind, roster.Class.LevelId, ct)).Criteria;

        var columns = new List<CriterionColumn>();
        foreach (var c in criteria)
        {
            var key = ImportColumns.KeyOf(c.Name);
            if (!ClassRoster.IsFree(key, columns.Select(x => x.Column.Key))) continue;
            columns.Add(new CriterionColumn(new ImportColumn(key, c.Name, false, "A whole number from 1 to 5. Leave blank to keep the rating already saved."), c.Id));
        }

        var enrolmentIds = roster.Students.Select(s => s.EnrolmentId).ToList();
        var criterionIds = columns.Select(c => c.CriterionId).ToList();
        var stored = await db.TraitRatings.AsNoTracking()
            .Where(r => r.TermId == roster.Term.Id && enrolmentIds.Contains(r.EnrolmentId) && criterionIds.Contains(r.CriterionId))
            .Select(r => new { r.EnrolmentId, r.CriterionId, r.Value }).ToListAsync(ct);
        return new TraitSheet(roster, kind, columns, stored.ToDictionary(r => (r.EnrolmentId, r.CriterionId), r => r.Value),
            SheetFor.Label(roster.SessionName, roster.Class.Name, KindLabel(kind), roster.Term.Name));
    }

    private static TraitKind RequireKind(ImportScope scope) => scope.Get("kind") switch
    {
        { } k when k.Equals(nameof(TraitKind.Psychomotor), StringComparison.OrdinalIgnoreCase) => TraitKind.Psychomotor,
        { } k when k.Equals(nameof(TraitKind.Affective), StringComparison.OrdinalIgnoreCase) => TraitKind.Affective,
        _ => throw BadRequestException.Field("kind", "Choose Psychomotor or Affective."),
    };

    private static string KindLabel(TraitKind kind) => kind == TraitKind.Psychomotor ? "Psychomotor" : "Affective";

    /// <summary>A student's stored ratings in column order, as the template writes them (blank when not rated): the row_version's input.</summary>
    private static IEnumerable<string> Stored(TraitSheet sheet, RosterStudent student) =>
        sheet.Columns.Select(c => sheet.Stored.TryGetValue((student.EnrolmentId, c.CriterionId), out var v) ? v.ToString(CultureInfo.InvariantCulture) : "");
}
