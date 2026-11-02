using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// A class's weekly timetable, from a roster of its current lessons: one row per lesson with the day, period, start, end and subject (name or
/// code of a subject taught in the class), prefilled from the saved week. The file is the whole week: on confirm it replaces the class's
/// timetable through the timetable screen's own save (<see cref="TimetableService.SaveAsync"/>), so a saved lesson missing from the file is
/// removed (named in a warning on the preview). This is the one import that removes anything (ruling in the SDD ledger). A file equal to the
/// saved week is a no-op and saves nothing. Rows are keyed by day and period: a blank start, end or subject keeps the saved lesson's value.
/// Each lesson is checked with the screen's sentences; the same day and period twice is an error on both rows. A teacher who also teaches
/// another class at that day and period is a warning, as on the screen. Leadership only.
/// </summary>
public sealed class TimetableImporter(IAppDbContext db, SchoolAccess access, TimetableService timetable)
    : IImporter, IImportNotice, IImportWarnings, IImportRemovals
{
    public const string KindName = "timetable";
    public const string Sheet = "Timetable";
    public const string DownloadFor = "this class";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Timetable import finished";
    public string NoticeLink => "/app/timetable";

    private static readonly ImportColumns AllColumns = new(
    [
        new("day", "day", true, "Monday to Saturday (or Mon to Sat)."),
        new("period", "period", true, $"The period number, from 1 to {TimetableService.MaxPeriod}."),
        new("start", "start", true, "When the lesson starts, as HH:mm, for example 08:00."),
        new("end", "end", true, "When the lesson ends, as HH:mm, for example 08:40."),
        new("subject", "subject", true, "The subject's name or code. It must be taught in the class."),
        SheetFor.Column,
        RowVersion.Column,
    ]);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException(TimetableService.PrincipalOnly);
        var classId = scope.RequireGuid("classId");
        if (!await db.SchoolClasses.AnyAsync(c => c.Id == classId, ct)) throw new NotFoundException("Class");
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var week = await LoadAsync(scope, ct);
        var rows = week.Slots.Values.OrderBy(s => s.Day).ThenBy(s => s.Period).Select(s => (IReadOnlyList<string>)
        [
            TimetableService.DayName(s.Day), s.Period.ToString(CultureInfo.InvariantCulture), Time(s.Start), Time(s.End),
            week.Subjects.GetValueOrDefault(s.ClassSubjectId)?.Name ?? "", week.SheetFor, Version(s),
        ]).ToList();
        var notes = new List<string>
        {
            $"Session: {week.SessionName}. Class: {week.Class.Name}. Upload this file only on that class.",
            "The file is the class's whole week: one row per lesson. Saving it replaces the timetable, so a lesson you delete from the file is removed.",
            "Keep sheet_for as it is. day is Monday to Saturday; period is 1 to 12; start and end are HH:mm; subject is a subject taught in the class.",
            "A blank start, end or subject keeps the lesson already saved at that day and period.",
            "A teacher who teaches another class at the same day and period is shown as a warning, not refused.",
            "The hidden row_version column records each lesson as saved when you downloaded this file. If a lesson is changed on the timetable screen after that, " +
            "its row is refused: download the template again. Leave row_version blank on a row you add for a new lesson.",
        };
        return new ImportTemplate(Sheet, AllColumns.All, rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var week = await LoadAsync(scope, ct);
        var subjectIds = week.Subjects.Keys.ToHashSet();
        var checkedRows = rows.Select(row =>
        {
            var c = row.Cells;
            var dayText = c.GetValueOrDefault("day") ?? "";
            var periodText = c.GetValueOrDefault("period") ?? "";
            var day = TimetableService.ParseDay(dayText);
            var period = decimal.TryParse(periodText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) && p == decimal.Truncate(p) && p <= int.MaxValue
                ? (int)p : 0;
            var label = day is { } d && period > 0 ? $"{TimetableService.DayName(d)}, period {period}" : $"{dayText} {periodText}".Trim();
            // The day and period the row is about, even when it has an error, so that lesson is never listed as removed.
            (SchoolDay, int)? key = day is { } k && period > 0 ? (k, period) : null;
            if (SheetFor.Check(row, week.SheetFor, DownloadFor) is { } wrongSheet) return Error(row.Row, label, wrongSheet, key);

            var stored = key is { } at && week.Slots.TryGetValue(at, out var slot) ? slot : null;
            var startText = c.GetValueOrDefault("start") ?? "";
            var endText = c.GetValueOrDefault("end") ?? "";
            var subjectText = (c.GetValueOrDefault("subject") ?? "").Trim();
            var start = startText.Length == 0 && stored is not null ? Time(stored.Start) : ImportTime.Normalise(startText);
            var end = endText.Length == 0 && stored is not null ? Time(stored.End) : ImportTime.Normalise(endText);
            var classSubjectId = subjectText.Length == 0
                ? stored?.ClassSubjectId ?? Guid.Empty
                : week.Subjects.Values.FirstOrDefault(s => string.Equals(s.Name, subjectText, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Code, subjectText, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty;

            var input = new TimetableSlotInput(dayText, period, start, end, classSubjectId);
            if (TimetableService.CheckSlot(input, subjectIds, out var parsedDay, out var from, out var to) is { } problem) return Error(row.Row, label, problem.Message, key);

            var plan = new TimetableSlotInput(parsedDay.ToString(), period, Time(from), Time(to), classSubjectId);
            var status = stored is null ? ImportRowStatus.New
                : stored.Start == from && stored.End == to && stored.ClassSubjectId == classSubjectId ? ImportRowStatus.Unchanged : ImportRowStatus.Update;
            // The version is per lesson (day and period): a lesson changed on the screen since the download is refused (below, after the
            // same-slot check, which names the clearer problem); a new lesson's version is blank.
            var stale = status != ImportRowStatus.Unchanged && !RowVersion.Matches(row, stored is null ? "" : Version(stored));
            return new ImportRowResult(row.Row, status, label, null, new SlotPlan((parsedDay, period), plan, stale));
        }).ToList();

        // The same day and period twice is an error on every such row (never "last row wins").
        var twice = checkedRows.Where(r => r.Plan is SlotPlan).GroupBy(r => ((SlotPlan)r.Plan!).Key).Where(g => g.Count() > 1)
            .SelectMany(g => g).Select(r => r.Row).ToHashSet();
        checkedRows = checkedRows.Select(r => twice.Contains(r.Row) ? Error(r.Row, r.Label, TimetableService.SameSlot, ((SlotPlan)r.Plan!).Key)
            : r.Plan is SlotPlan { Stale: true } p ? Error(r.Row, r.Label, RowVersion.Stale, p.Key) : r).ToList();

        // A teacher clash is a warning, worded as on the timetable screen.
        return checkedRows.Select(r => r.Plan is SlotPlan plan && TeacherClash(week, plan) is { } clash ? r with { Message = clash } : r).ToList();
    }

    /// <summary>Teacher clashes, one line per row, and the saved lessons the file leaves out (they are removed on confirm).</summary>
    public async Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var warnings = rows.Where(r => r.Status != ImportRowStatus.Error && r.Message is not null).Select(r => $"Row {r.Row}: {r.Message}").ToList();
        var week = await LoadAsync(scope, ct);
        var removed = Removed(week, rows).Select(s => $"{TimetableService.DayName(s.Day)}, period {s.Period} ({week.Subjects.GetValueOrDefault(s.ClassSubjectId)?.Name ?? "a subject"})").ToList();
        if (removed.Count == 1) warnings.Add($"This lesson is not in the file and will be removed: {removed[0]}.");
        else if (removed.Count > 1)
            warnings.Add($"These lessons are not in the file and will be removed: {string.Join(", ", removed.Take(removed.Count - 1))} and {removed[^1]}.");
        return warnings;
    }

    /// <summary>The saved lessons the file leaves out: the preview's <c>remove</c> count.</summary>
    public async Task<int> RemovalsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct) =>
        Removed(await LoadAsync(scope, ct), rows).Count;

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var week = await LoadAsync(scope, ct);
        var created = rows.Count(r => r.Status == ImportRowStatus.New);
        var updated = rows.Count(r => r.Status == ImportRowStatus.Update);
        // The saved week, unchanged: nothing to save (an unchanged template is a no-op).
        var removed = Removed(week, rows).Count;
        if (created == 0 && updated == 0 && removed == 0) return new ImportApplied(0, 0);
        // The timetable screen's own save replaces the week. It joins the engine's transaction.
        await timetable.SaveAsync(week.Class.Id, rows.Select(r => ((SlotPlan)r.Plan!).Input).ToList(), ct);
        return new ImportApplied(created, updated, removed);
    }

    // ---------- Week ----------

    /// <summary>A lesson to save. <see cref="Stale"/>: it would change a lesson that was changed on the screen after the file was made.</summary>
    private sealed record SlotPlan((SchoolDay Day, int Period) Key, TimetableSlotInput Input, bool Stale = false);
    private sealed record SubjectRow(Guid Id, string Name, string Code, Guid? TeacherStaffId);
    private sealed record OtherSlot(SchoolDay Day, int Period, Guid TeacherStaffId, string ClassName);
    private sealed record Week(
        SchoolClass Class, string SessionName, IReadOnlyDictionary<Guid, SubjectRow> Subjects, IReadOnlyDictionary<(SchoolDay, int), TimetableSlot> Slots,
        IReadOnlyList<OtherSlot> Others, IReadOnlyDictionary<Guid, string> TeacherNames, string SheetFor);

    /// <summary>The day and period of a row with an error. Nothing is applied from it; it only keeps that lesson out of the removal warning.</summary>
    private sealed record ErrorKey((SchoolDay Day, int Period) Key);

    private static ImportRowResult Error(int row, string label, string message, (SchoolDay, int)? key) =>
        new(row, ImportRowStatus.Error, label, message, key is { } k ? new ErrorKey(k) : null);

    private static string Time(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A saved lesson's row_version: its start, end and class subject.</summary>
    private static string Version(TimetableSlot slot) => RowVersion.Of(Time(slot.Start), Time(slot.End), slot.ClassSubjectId.ToString("N"));

    private static List<TimetableSlot> Removed(Week week, IReadOnlyList<ImportRowResult> rows)
    {
        // A lesson whose day and period has a row in the file is not removed, also when that row has an error (confirm is blocked then anyway).
        var kept = rows.Select(r => r.Plan switch { SlotPlan p => p.Key, ErrorKey e => e.Key, _ => ((SchoolDay, int)?)null })
            .OfType<(SchoolDay, int)>().ToHashSet();
        return week.Slots.Values.Where(s => !kept.Contains((s.Day, s.Period))).OrderBy(s => s.Day).ThenBy(s => s.Period).ToList();
    }

    private static string? TeacherClash(Week week, SlotPlan plan)
    {
        if (week.Subjects.GetValueOrDefault(plan.Input.ClassSubjectId)?.TeacherStaffId is not { } teacher) return null;
        var others = week.Others.Where(o => o.TeacherStaffId == teacher && o.Day == plan.Key.Day && o.Period == plan.Key.Period)
            .Select(o => o.ClassName).Distinct().Order().ToList();
        if (others.Count == 0) return null;
        var name = week.TeacherNames.GetValueOrDefault(teacher) ?? "A teacher";
        return string.Join(" ", others.Select(o => $"{name} also teaches {o} on {TimetableService.DayName(plan.Key.Day)}, period {plan.Key.Period}."));
    }

    private async Task<Week> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var classId = scope.RequireGuid("classId");
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        var session = await db.AcademicSessions.AsNoTracking().Where(s => s.Id == cls.SessionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var subjects = await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == classId)
            .Join(db.Subjects.AsNoTracking(), c => c.SubjectId, s => s.Id, (c, s) => new SubjectRow(c.Id, s.Name, s.Code, c.TeacherStaffId))
            .ToListAsync(ct);
        var slots = await db.TimetableSlots.AsNoTracking().Where(s => s.ClassId == classId).ToListAsync(ct);
        var teachers = subjects.Where(s => s.TeacherStaffId is not null).Select(s => s.TeacherStaffId!.Value).Distinct().ToList();
        var others = teachers.Count == 0
            ? []
            : await (from s in db.TimetableSlots.AsNoTracking()
                     where s.ClassId != classId
                     join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                     join c in db.SchoolClasses.AsNoTracking() on s.ClassId equals c.Id
                     where c.SessionId == cls.SessionId && cs.TeacherStaffId != null && teachers.Contains(cs.TeacherStaffId!.Value)
                     select new OtherSlot(s.Day, s.Period, cs.TeacherStaffId!.Value, c.Name)).ToListAsync(ct);
        var names = teachers.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.StaffProfiles.AsNoTracking().Where(p => teachers.Contains(p.Id))
                .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.Id, u.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return new Week(cls, session, subjects.ToDictionary(s => s.Id), slots.GroupBy(s => (s.Day, s.Period)).ToDictionary(g => g.Key, g => g.First()),
            others, names, SheetFor.Label(session, cls.Name));
    }
}
