using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Developmental ratings for one nursery or creche class in one term, from a roster template: one row per student, one column per area
/// holding a scale label (Excellent … Needs Support) and an "&lt;area&gt; comment" column after it, the stored ratings already in the cells.
/// A blank rating or comment keeps what is saved; equal values are unchanged and are not sent. A comment needs a rating, in the file or saved.
/// Each changed student is saved through the child page's own save (<see cref="DevelopmentalService.SaveRatingsAsync"/>), so its access,
/// scale and lock checks apply. The class teacher or leadership only (anyone else gets "Class was not found."); the bursar is refused before
/// anything is looked up. A numeric class is a 400 and a locked term refuses the whole import with the sheet's <c>term_locked</c> 409, on
/// preview and again on confirm.
/// </summary>
public sealed class DevelopmentalImporter(IAppDbContext db, SchoolAccess access, DevelopmentalService developmental)
    : IImporter, IImportNotice, IImportScopeColumns
{
    public const string KindName = "developmental";
    public const string Sheet = "Developmental";
    public const string Numeric = "This class uses numeric assessment.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Developmental rating import finished";
    public string NoticeLink => "/app/results";

    private static readonly ImportColumns FixedColumns = new([ClassRoster.AdmissionColumn, ClassRoster.NameColumn, SheetFor.Column, RowVersion.Column]);

    public ImportColumns Columns(ImportScope scope) => FixedColumns;

    public async Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct) =>
        (await LoadAsync(scope, ct)).Areas.SelectMany(a => new[] { a.Rating, a.Comment }).ToList();

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not rate developmental areas.");
        var classId = scope.RequireGuid("classId");
        scope.RequireGuid("termId");
        await ClassRoster.EnsureClassTeacherAsync(db, access, classId, ct);
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Roster.Students.Select(s => (IReadOnlyList<string>)
        [
            s.AdmissionNo, s.Name,
            .. sheet.Areas.SelectMany(a => sheet.Stored.TryGetValue((s.EnrolmentId, a.AreaId), out var r)
                ? new[] { sheet.Scale.FirstOrDefault(p => p.Id == r.ScaleId)?.Label ?? "", r.Comment ?? "" }
                : ["", ""]),
            sheet.SheetFor,
            RowVersion.Of(Stored(sheet, s)),
        ]).ToList();
        var notes = new List<string>
        {
            $"Session: {sheet.Roster.SessionName}. Class: {sheet.Roster.Class.Name}. Term: {sheet.Roster.Term.Name}. Upload this file only on that class and term.",
            "One row per student. Keep admission_no and sheet_for as they are; the student column is only there to help you.",
            $"sheet_for says which session, class and term the file is for ({sheet.SheetFor}). A file uploaded on another session, class or term is refused.",
            $"Rate each area with one of: {ClassRoster.OrList(sheet.Scale.Select(p => p.Label).ToList())}. Capitals and extra spaces don't matter.",
            $"You can type the scale's code instead of the label: {string.Join(", ", sheet.Scale.Select(p => $"{p.Code} for {p.Label}"))}.",
            "The comment column after each area is optional. A comment needs a rating for that area, in the file or already saved.",
            "A blank cell keeps the rating or comment already saved. To clear one, use the child's results page. A row you did not change is left as it is.",
            "The hidden row_version column records each child's saved ratings and comments when you downloaded this file. If they are changed on the screen after that, the child's row is refused: download the template again.",
        };
        if (sheet.Locked) notes.Add("Results are locked for this class this term, so this file can't be uploaded until the principal unlocks them.");
        return new ImportTemplate(Sheet,
            [ClassRoster.AdmissionColumn, ClassRoster.NameColumn, .. sheet.Areas.SelectMany(a => new[] { a.Rating, a.Comment }), SheetFor.Column, RowVersion.Column], rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        if (sheet.Locked) throw new ConflictException(ScoreService.TermLockedMessage, "term_locked");
        ClassRoster.EnsureAnyColumn(rows, sheet.Areas.Select(a => a.Rating).ToList());
        var labels = ClassRoster.OrList(sheet.Scale.Select(p => p.Label).ToList());

        return RosterRows.Match(sheet.Roster.ByAdmissionNo, rows).Select(match =>
        {
            var wrongSheet = SheetFor.Check(match.Row, sheet.SheetFor, ClassRoster.DownloadFor);
            if (wrongSheet is not null || match.Enrolment is not { } student) return ClassRoster.ErrorRow(match.Row, wrongSheet ?? match.Error!);

            var ratings = new List<SaveDevelopmentalRating>();
            var updates = false;
            foreach (var area in sheet.Areas)
            {
                var ratingText = Squash(match.Row.Cells.GetValueOrDefault(area.Rating.Key));
                var commentText = (match.Row.Cells.GetValueOrDefault(area.Comment.Key) ?? "").Trim();
                if (ratingText.Length == 0 && commentText.Length == 0) continue; // blank keeps what is saved
                var stored = sheet.Stored.TryGetValue((student.EnrolmentId, area.AreaId), out var s) ? s : null;

                Guid scaleId;
                if (ratingText.Length > 0)
                {
                    var point = sheet.Scale.FirstOrDefault(p => string.Equals(Squash(p.Label), ratingText, StringComparison.OrdinalIgnoreCase))
                        ?? sheet.Scale.FirstOrDefault(p => string.Equals(p.Code, ratingText, StringComparison.OrdinalIgnoreCase));
                    if (point is null)
                        return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label, $"{area.Name} must be one of {labels}.", null);
                    scaleId = point.Id;
                }
                else if (stored is not null) scaleId = stored.ScaleId;
                else return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label, $"Choose a rating for {area.Name} before the comment.", null);

                if (commentText.Length > DevelopmentalService.MaxCommentLength)
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label,
                        $"The {area.Name} comment can have at most {DevelopmentalService.MaxCommentLength} characters.", null);
                // The save replaces the comment with what is sent, so a blank comment cell sends the saved one back.
                var comment = commentText.Length > 0 ? commentText : stored?.Comment;
                if (stored is not null && stored.ScaleId == scaleId && stored.Comment == comment) continue;
                if (stored is not null) updates = true;
                ratings.Add(new SaveDevelopmentalRating(area.AreaId, scaleId, comment));
            }
            var status = ratings.Count == 0 ? ImportRowStatus.Unchanged : updates ? ImportRowStatus.Update : ImportRowStatus.New;
            return RowVersion.Guard(new ImportRowResult(match.Row.Row, status, student.Label, null, new DevelopmentalPlan(student.StudentId, ratings)),
                match.Row, RowVersion.Of(Stored(sheet, student)));
        }).ToList();
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var termId = scope.RequireGuid("termId");
        // The child page's own save, one student at a time (it checks the lock again under the class-term lock). It joins the engine's
        // transaction, so every student lands or none does.
        foreach (var row in rows.Where(r => r.Status is ImportRowStatus.New or ImportRowStatus.Update))
        {
            var plan = (DevelopmentalPlan)row.Plan!;
            await developmental.SaveRatingsAsync(plan.StudentId, termId, new SaveDevelopmentalRatingsRequest(plan.Ratings), ct);
        }
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record DevelopmentalPlan(Guid StudentId, IReadOnlyList<SaveDevelopmentalRating> Ratings);
    private sealed record AreaColumns(Guid AreaId, string Name, ImportColumn Rating, ImportColumn Comment);
    private sealed record StoredRating(Guid ScaleId, string? Comment);
    private sealed record DevelopmentalSheet(
        ClassTermRoster Roster, IReadOnlyList<AreaColumns> Areas, IReadOnlyList<DevelopmentalScaleItemDto> Scale,
        IReadOnlyDictionary<(Guid, Guid), StoredRating> Stored, bool Locked, string SheetFor);

    /// <summary>The class's areas and scale (its level's own, or the school's), the roster, the stored ratings and the lock. Numeric classes are a 400.</summary>
    private async Task<DevelopmentalSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var roster = await ClassRoster.LoadAsync(db, scope.RequireGuid("classId"), scope.RequireGuid("termId"), ct);
        var mode = await db.Levels.AsNoTracking().Where(l => l.Id == roster.Class.LevelId).Select(l => l.AssessmentMode).FirstOrDefaultAsync(ct);
        if (mode != AssessmentMode.Developmental) throw new BadRequestException(Numeric);

        var areas = await developmental.AreasForAsync(roster.Class.LevelId, ct);
        var scale = (await developmental.GetScaleAsync(roster.Class.LevelId, ct)).Scale;
        var columns = new List<AreaColumns>();
        var taken = new List<string>();
        foreach (var a in areas)
        {
            var (key, commentKey) = (ImportColumns.KeyOf(a.Name), ImportColumns.KeyOf($"{a.Name} comment"));
            if (!ClassRoster.IsFree(key, taken) || !ClassRoster.IsFree(commentKey, taken) || key == commentKey) continue;
            taken.Add(key);
            taken.Add(commentKey);
            columns.Add(new AreaColumns(a.Id, a.Name,
                new ImportColumn(key, a.Name, false, $"One of: {ClassRoster.OrList(scale.Select(p => p.Label).ToList())}, or its code. Leave blank to keep the rating already saved."),
                new ImportColumn(commentKey, $"{a.Name} comment", false,
                    $"Optional, up to {DevelopmentalService.MaxCommentLength} characters. Needs a rating for {a.Name}. Leave blank to keep the comment already saved.")));
        }

        var enrolmentIds = roster.Students.Select(s => s.EnrolmentId).ToList();
        var areaIds = columns.Select(c => c.AreaId).ToList();
        var stored = await db.DevelopmentalRatings.AsNoTracking()
            .Where(r => r.TermId == roster.Term.Id && enrolmentIds.Contains(r.EnrolmentId) && areaIds.Contains(r.AreaId))
            .Select(r => new { r.EnrolmentId, r.AreaId, r.ScaleId, r.Comment }).ToListAsync(ct);
        var locked = await db.TermResultLocks.AsNoTracking().AnyAsync(l => l.ClassId == roster.Class.Id && l.TermId == roster.Term.Id, ct);
        return new DevelopmentalSheet(roster, columns, scale,
            stored.ToDictionary(r => (r.EnrolmentId, r.AreaId), r => new StoredRating(r.ScaleId, r.Comment)), locked,
            SheetFor.Label(roster.SessionName, roster.Class.Name, roster.Term.Name));
    }

    private static string Squash(string? value) => string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A child's stored rating (scale point id) and comment for each area, in column order (blank when not rated): the row_version's input.</summary>
    private static IEnumerable<string> Stored(DevelopmentalSheet sheet, RosterStudent student) =>
        sheet.Areas.SelectMany(a => sheet.Stored.TryGetValue((student.EnrolmentId, a.AreaId), out var r)
            ? new[] { r.ScaleId.ToString("N"), r.Comment ?? "" }
            : ["", ""]);
}
