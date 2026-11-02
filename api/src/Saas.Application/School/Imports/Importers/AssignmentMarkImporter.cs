using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Marks for one assignment, from a roster template: one row per student of the class, the stored marks and feedback already in the cells.
/// A numeric class has <c>mark</c> and <c>feedback</c>; a nursery or creche class (developmental assessment) has <c>received</c> (yes or no)
/// and <c>feedback</c>. A blank cell keeps what is saved; a cell equal to it is unchanged and is not sent. Each changed student goes through the
/// marking screen's own save (<see cref="AssignmentService.MarkAsync"/>), so a counted mark writes the score through the single score writer and
/// a locked term refuses it with the sheet's <c>term_locked</c> 409 (on preview and again on confirm). The subject teacher or leadership only:
/// anyone else, and an assignment that doesn't exist, get the screen's "This assignment was not found."; the bursar is refused first.
/// </summary>
public sealed class AssignmentMarkImporter(IAppDbContext db, SchoolAccess access, AssignmentService assignments)
    : IImporter, IImportNotice, IImportScopeColumns
{
    public const string KindName = "assignment-marks";
    public const string Sheet = "Marks";
    public const string MarkColumn = "mark";
    public const string ReceivedColumn = "received";
    public const string FeedbackColumn = "feedback";
    public const string DownloadFor = "this assignment";
    public const string NotANumber = "Enter a mark such as 8 or 8.5.";
    public const string YesOrNo = "Enter yes or no for received.";
    public const string StaysReceived = "The student sent this work, so it stays received.";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Assignment mark import finished";
    public string NoticeLink => "/app/assignments";

    private static readonly ImportColumn Feedback =
        new(FeedbackColumn, FeedbackColumn, false, $"Optional, up to {AssignmentService.MaxFeedbackLength} characters. Leave blank to keep the feedback already saved.");
    private static readonly ImportColumn Mark =
        new(MarkColumn, MarkColumn, false, "The mark, with at most one decimal place. Leave blank to keep the mark already saved.");
    private static readonly ImportColumn Received =
        new(ReceivedColumn, ReceivedColumn, false, "yes when the work came in, no when it did not.");

    private static readonly ImportColumns FixedColumns = new([ClassRoster.AdmissionColumn, ClassRoster.NameColumn, Feedback, SheetFor.Column, RowVersion.Column]);

    public ImportColumns Columns(ImportScope scope) => FixedColumns;

    public async Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct) =>
        [(await LoadAsync(scope, ct)).Developmental ? Received : Mark];

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not set assignments.");
        await FindAsync(scope, ct);
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Roster.Students.Select(s =>
        {
            var stored = sheet.Submissions.GetValueOrDefault(s.EnrolmentId);
            var value = sheet.Developmental
                ? (IsReceived(stored) ? "yes" : "no")
                : stored?.Mark is { } mark ? mark.ToString("0.#", CultureInfo.InvariantCulture) : "";
            return (IReadOnlyList<string>)[s.AdmissionNo, s.Name, value, stored?.Feedback ?? "", sheet.SheetFor, RowVersion.Of(Stored(sheet, stored))];
        }).ToList();
        var notes = new List<string>
        {
            $"Assignment: {sheet.Assignment.Title}. Session: {sheet.Roster.SessionName}. Class: {sheet.Roster.Class.Name}. Subject: {sheet.SubjectName}. Term: {sheet.Roster.Term.Name}. Upload this file only on that assignment.",
            "One row per student. Keep admission_no and sheet_for as they are; the student column is only there to help you.",
            $"sheet_for says which assignment the file is for ({sheet.SheetFor}). A file uploaded on another assignment is refused.",
            sheet.Developmental
                ? "received: yes when the work came in, no when it did not. feedback is optional."
                : $"mark: from 0 to {sheet.Limit.ToString("0.#", CultureInfo.InvariantCulture)}, with at most one decimal place. feedback is optional."
                  + (sheet.Assignment.AssessmentComponentId is null ? "" : " The mark is written onto the score sheet, as when you mark on the screen."),
            "A blank cell keeps what is already saved. A row you did not change is left as it is.",
            "The hidden row_version column records each student's saved mark and feedback when you downloaded this file. If they are changed on the marking screen after that, the student's row is refused: download the template again.",
        };
        return new ImportTemplate(Sheet,
            [ClassRoster.AdmissionColumn, ClassRoster.NameColumn, sheet.Developmental ? Received : Mark, Feedback, SheetFor.Column, RowVersion.Column], rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var counted = sheet.Assignment.AssessmentComponentId is not null;
        var results = RosterRows.Match(sheet.Roster.ByAdmissionNo, rows).Select(match =>
        {
            var wrongSheet = SheetFor.Check(match.Row, sheet.SheetFor, DownloadFor);
            if (wrongSheet is not null || match.Enrolment is not { } student) return ClassRoster.ErrorRow(match.Row, wrongSheet ?? match.Error!);
            var stored = sheet.Submissions.GetValueOrDefault(student.EnrolmentId);
            var cells = match.Row.Cells;
            var updates = false;

            decimal? mark = null;
            bool? received = null;
            string? feedback = null;
            string? error = null;

            if (sheet.Developmental)
            {
                var text = (cells.GetValueOrDefault(ReceivedColumn) ?? "").Trim().ToLowerInvariant();
                if (text.Length > 0)
                {
                    bool? value = text switch { "yes" or "y" or "true" or "1" => true, "no" or "n" or "false" or "0" => false, _ => null };
                    if (value is null) error = YesOrNo;
                    else if (value != IsReceived(stored))
                    {
                        if (value is false && HasWork(stored)) error = StaysReceived;
                        else
                        {
                            received = value;
                            updates |= IsReceived(stored);
                        }
                    }
                }
            }
            else
            {
                var text = (cells.GetValueOrDefault(MarkColumn) ?? "").Trim();
                if (text.Length > 0)
                {
                    if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                        error = NotANumber;
                    else if (AssignmentService.MarkError(value, sheet.Limit) is { } refused) error = refused;
                    else if (value != stored?.Mark)
                    {
                        mark = value;
                        updates |= stored?.Mark is not null;
                    }
                }
            }

            var feedbackText = (cells.GetValueOrDefault(FeedbackColumn) ?? "").Trim();
            if (error is null && feedbackText.Length > 0)
            {
                if (feedbackText.Length > AssignmentService.MaxFeedbackLength)
                    error = $"Use {AssignmentService.MaxFeedbackLength} characters or fewer for the feedback.";
                else if (!string.Equals(feedbackText, stored?.Feedback, StringComparison.Ordinal))
                {
                    feedback = feedbackText;
                    updates |= stored?.Feedback is not null;
                }
            }

            if (error is not null) return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label, error, null);
            if (mark is null && received is null && feedback is null)
                return new ImportRowResult(match.Row.Row, ImportRowStatus.Unchanged, student.Label, null, null);
            return RowVersion.Guard(new ImportRowResult(match.Row.Row, updates ? ImportRowStatus.Update : ImportRowStatus.New, student.Label, null,
                new MarkPlan(student.EnrolmentId, new MarkAssignmentRequest(mark, feedback, null, received))), match.Row, RowVersion.Of(Stored(sheet, stored)));
        }).ToList();

        // A counted mark writes the score, so a locked term refuses the file, as the screen refuses the mark. Feedback alone and practice
        // marks write no score and go through, as on the screen.
        if (counted && sheet.Locked && results.Any(r => r.Plan is MarkPlan { Request.Mark: not null }))
            throw new ConflictException(ScoreService.TermLockedMessage, "term_locked");
        return results;
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var assignmentId = scope.RequireGuid("assignmentId");
        // The marking screen's own save, one student at a time. It joins the engine's transaction, so every mark lands or none does.
        foreach (var row in rows.Where(r => r.Status is ImportRowStatus.New or ImportRowStatus.Update))
        {
            var plan = (MarkPlan)row.Plan!;
            await assignments.MarkAsync(assignmentId, plan.EnrolmentId, plan.Request, ct);
        }
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record MarkPlan(Guid EnrolmentId, MarkAssignmentRequest Request);

    private sealed record MarkSheet(
        Assignment Assignment, ClassTermRoster Roster, string SubjectName, bool Developmental, decimal Limit, bool Locked,
        IReadOnlyDictionary<Guid, AssignmentSubmission> Submissions, string SheetFor);

    /// <summary>The assignment, when this user may mark it (the subject teacher or leadership), else "This assignment was not found."</summary>
    private async Task<Assignment> FindAsync(ImportScope scope, CancellationToken ct)
    {
        var id = scope.RequireGuid("assignmentId");
        var assignment = await db.Assignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("This assignment");
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(assignment.ClassSubjectId, ct)) throw new NotFoundException("This assignment");
        return assignment;
    }

    private async Task<MarkSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var assignment = await FindAsync(scope, ct);
        var link = await db.ClassSubjects.AsNoTracking().Where(c => c.Id == assignment.ClassSubjectId)
            .Join(db.Subjects.AsNoTracking(), c => c.SubjectId, s => s.Id, (c, s) => new { c.ClassId, s.Name }).FirstAsync(ct);
        var roster = await ClassRoster.LoadAsync(db, link.ClassId, assignment.TermId, ct);
        var developmental = await db.Levels.AsNoTracking().Where(l => l.Id == roster.Class.LevelId).Select(l => l.AssessmentMode).FirstAsync(ct)
            == AssessmentMode.Developmental;
        var limit = developmental ? 0 : await assignments.MarkLimitAsync(assignment, roster.Class, ct);
        var locked = await db.TermResultLocks.AnyAsync(l => l.ClassId == roster.Class.Id && l.TermId == assignment.TermId, ct);
        var submissions = await db.AssignmentSubmissions.AsNoTracking().Where(s => s.AssignmentId == assignment.Id).ToListAsync(ct);
        return new MarkSheet(assignment, roster, link.Name, developmental, limit, locked,
            submissions.GroupBy(s => s.EnrolmentId).ToDictionary(g => g.Key, g => g.First()),
            SheetFor.Label(roster.SessionName, roster.Class.Name, link.Name, roster.Term.Name, assignment.Title,
                $"due {DateOnly.FromDateTime(assignment.DueAt.ToOffset(SchoolCalendar.LagosOffset).DateTime):yyyy-MM-dd}",
                $"#{assignment.Id.ToString("N")[^8..]}"));
    }

    /// <summary>As the assignment screen shows it: received or marked, or sent by the student.</summary>
    private static bool IsReceived(AssignmentSubmission? s) =>
        s is not null && (s.Status is AssignmentSubmissionStatus.Received or AssignmentSubmissionStatus.Returned || s.SubmittedAt is not null);

    /// <summary>
    /// What the row can change, as saved: the mark (or "yes" when received, blank when not, on a developmental class) and the feedback. Blank
    /// when nothing is saved. The row_version's input.
    /// </summary>
    private static IEnumerable<string> Stored(MarkSheet sheet, AssignmentSubmission? stored) =>
    [
        sheet.Developmental ? (IsReceived(stored) ? "yes" : "") : stored?.Mark is { } mark ? mark.ToString("0.#", CultureInfo.InvariantCulture) : "",
        stored?.Feedback ?? "",
    ];

    /// <summary>The student sent something, so "not received" would not stick on the screen either.</summary>
    private static bool HasWork(AssignmentSubmission? s) => s is not null && (s.SubmittedAt is not null || s.FileKey is not null || !string.IsNullOrEmpty(s.Body));
}
