using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Registers for one class over a range of dates (at most 31 days, none after today) from a roster template: one row per student, one
/// column per date (headed yyyy-MM-dd; a heading Excel turned into a date, a date serial or day/month/year also reads as that day) with P, A,
/// L or E in any case. A blank cell means "not taken" and keeps the stored mark; a cell equal to it is unchanged. Each date with a change is
/// saved through the register's own save (<see cref="AttendanceService"/>), so one register per class and day, the latest marks win and an
/// absence alert is never repeated. The template ends with a <c>register_version</c> row holding each day's stored register version; that is
/// the base of each day's save, and a day whose register was saved after the file was made is refused (error rows on preview, 409 on confirm),
/// so a stale file never lays its old letters over newer marks. A register not taken yet marks the students left blank present, as the register does (a warning says so).
/// Only today's date messages parents; earlier dates record the alert without sending anything. Access follows the register: the bursar is
/// refused first, a teacher reaches only classes they teach ("Class was not found." otherwise) and the register's date window.
/// </summary>
public sealed class AttendanceImporter(IAppDbContext db, SchoolAccess access, AttendanceService attendance, TimeProvider clock)
    : IImporter, IImportNotice, IImportScopeColumns, IImportWarnings
{
    public const string KindName = "attendance";
    public const string Sheet = "Attendance";
    public const int MaxDays = 31;
    public const string NoBursar = "Attendance is for the school's leadership and teachers.";
    private const string DownloadFor = "this class and these dates";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Attendance import finished";
    public string NoticeLink => "/app/attendance";

    private static readonly ImportColumns FixedColumns = new([ClassRoster.AdmissionColumn, ClassRoster.NameColumn, SheetFor.Column]);

    private DateOnly Today => SchoolCalendar.Today(clock.GetUtcNow());

    public ImportColumns Columns(ImportScope scope) => FixedColumns;

    /// <summary>
    /// One column per date of the range, plus the other ways a spreadsheet may write that heading (day/month/year, an Excel date serial), so
    /// the engine does not call a retyped date heading "ignored".
    /// </summary>
    public Task<IReadOnlyList<ImportColumn>> ScopeColumnsAsync(ImportScope scope, CancellationToken ct)
    {
        var (from, to) = Range(scope);
        var columns = new List<ImportColumn>();
        foreach (var date in Dates(from, to))
        {
            var column = DateColumn(date);
            columns.Add(column);
            foreach (var alias in new[]
                     {
                         date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), date.ToString("d/M/yyyy", CultureInfo.InvariantCulture),
                         date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString("F0", CultureInfo.InvariantCulture),
                     })
                columns.Add(column with { Key = ImportColumns.KeyOf(alias) });
        }
        return Task.FromResult<IReadOnlyList<ImportColumn>>(columns);
    }

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException(NoBursar);
        var classId = scope.RequireGuid("classId");
        var (from, to) = Range(scope);
        if (!await db.SchoolClasses.AnyAsync(c => c.Id == classId, ct)) throw new NotFoundException("Class");
        if (!access.IsLeadership && !await access.TeachesClassAsync(classId, ct)) throw new NotFoundException("Class");
        // The register's own rule for the first and last date covers the range: it is one unbroken window.
        await attendance.EnsureCanEditAsync(from, ct);
        await attendance.EnsureCanEditAsync(to, ct);
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var rows = sheet.Students.Select(s => (IReadOnlyList<string>)
        [
            s.AdmissionNo, s.Name,
            .. sheet.Dates.Select(d => sheet.Marks.TryGetValue((d, s.EnrolmentId), out var m) ? Letter(m.Status) : ""),
            sheet.SheetFor,
        ]).ToList();
        // After the students: the version of each day's register this file was made from (blank when not taken).
        rows.Add([VersionRowKey, "Register versions (do not change this row)", .. sheet.Dates.Select(d => VersionOf(sheet, d)), sheet.SheetFor]);
        var notes = new List<string>
        {
            $"Session: {sheet.SessionName}. Class: {sheet.Class.Name}. Dates: {Day(sheet.From)} to {Day(sheet.To)}. Upload this file only on that class and those dates.",
            "One row per student. Keep admission_no and sheet_for as they are; the student column is only there to help you.",
            $"sheet_for says which session, class and dates the file is for ({sheet.SheetFor}). A file uploaded on another class or other dates is refused.",
            "One column per date, headed year-month-day (for example 2026-10-26). Mark each day P (present), A (absent), L (late) or E (excused).",
            "A blank cell means not taken: the mark already saved stays. On a day nobody has taken the register yet, students left blank are marked present, as on the register.",
            "Absences on today's date message the parents, as on the register. Earlier dates send no message.",
            "The last row, register_version, records which version of each day's register this file was made from. Do not change this row. " +
            "If a day's register is saved after you download the file, that day is refused when you upload it: download the template again.",
        };
        return new ImportTemplate(Sheet, [ClassRoster.AdmissionColumn, ClassRoster.NameColumn, .. sheet.Dates.Select(DateColumn), SheetFor.Column], rows, notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var sheet = await LoadAsync(scope, ct);
        var columns = DateColumnsOf(rows, sheet.From, sheet.To);

        // The register_version row: the version of each day the file was made from. A day saved since then is refused, so a stale file
        // never lays its old letters over newer marks (on preview, and again on confirm, which evaluates afresh).
        var versionRows = rows.Where(IsVersionRow).ToList();
        if (versionRows.Count == 0) throw BadRequestException.Field("file", MissingVersions);
        if (versionRows.Count > 1) throw BadRequestException.Field("file", "The file has more than one register_version row. Download the template again.");
        var bases = columns.ToDictionary(c => c.Date, c => (versionRows[0].Cells.GetValueOrDefault(c.Key) ?? "").Trim());
        var stale = columns.Where(c => bases[c.Date] != VersionOf(sheet, c.Date)).Select(c => c.Date).ToHashSet();

        return RosterRows.Match(sheet.ByAdmissionNo, rows.Where(r => !IsVersionRow(r)).ToList()).Select(match =>
        {
            var wrongSheet = SheetFor.Check(match.Row, sheet.SheetFor, DownloadFor);
            if (wrongSheet is not null || match.Enrolment is not { } student) return ClassRoster.ErrorRow(match.Row, wrongSheet ?? match.Error!);

            var changes = new List<DayMark>();
            var updates = false;
            foreach (var (key, date) in columns)
            {
                var text = (match.Row.Cells.GetValueOrDefault(key) ?? "").Trim();
                if (text.Length == 0) continue; // not taken: the stored mark stays
                if (stale.Contains(date))
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label,
                        $"{Day(date)}: {AttendanceService.RegisterChanged} Download the template again.", null);
                if (ParseLetter(text) is not { } status)
                    return new ImportRowResult(match.Row.Row, ImportRowStatus.Error, student.Label,
                        $"Mark {Day(date)} with P, A, L or E (present, absent, late or excused).", null);
                var stored = sheet.Marks.TryGetValue((date, student.EnrolmentId), out var m) ? m : null;
                if (stored?.Status == status) continue;
                if (stored is not null) updates = true;
                changes.Add(new DayMark(date, status));
            }
            var result = changes.Count == 0 ? ImportRowStatus.Unchanged : updates ? ImportRowStatus.Update : ImportRowStatus.New;
            return new ImportRowResult(match.Row.Row, result, student.Label, null, new AttendancePlan(student.EnrolmentId, changes, bases));
        }).ToList();
    }

    public async Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var plans = Plans(rows);
        if (plans.Count == 0) return [];
        var sheet = await LoadAsync(scope, ct);
        var warnings = new List<string>();
        foreach (var date in plans.SelectMany(p => p.Changes).Select(c => c.Date).Distinct().Order())
        {
            if (sheet.Taken.ContainsKey(date)) continue;
            var marked = plans.Where(p => p.Changes.Any(c => c.Date == date)).Select(p => p.EnrolmentId).ToHashSet();
            // The weekday makes a stray letter on a Saturday or a holiday stand out.
            var blank = sheet.Students.Count(s => !marked.Contains(s.EnrolmentId));
            if (blank > 0)
                warnings.Add($"No register was taken on {date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}. " +
                    $"{blank} {(blank == 1 ? "student" : "students")} left blank that day will be marked present, as on the register.");
        }
        if (plans.Any(p => p.Changes.Any(c => c.Date == Today && c.Status == AttendanceStatus.Absent)))
            warnings.Add("Parents of students marked absent today get the absence message, as on the register. Earlier dates send no message.");
        return warnings;
    }

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var plans = Plans(rows);
        if (plans.Count > 0)
        {
            var sheet = await LoadAsync(scope, ct);
            var today = Today;
            // The register's own save, one date at a time, inside the engine's transaction: every date lands or none does.
            foreach (var date in plans.SelectMany(p => p.Changes).Select(c => c.Date).Distinct().Order())
            {
                var wanted = plans.SelectMany(p => p.Changes.Where(c => c.Date == date).Select(c => (p.EnrolmentId, c.Status)))
                    .ToDictionary(x => x.EnrolmentId, x => x.Status);
                // Every student of the register: the file's mark, else the stored one (with its note), else present as on a fresh register.
                var marks = sheet.Students.Select(s =>
                {
                    var stored = sheet.Marks.TryGetValue((date, s.EnrolmentId), out var m) ? m : null;
                    var status = wanted.TryGetValue(s.EnrolmentId, out var w) ? w : stored?.Status ?? AttendanceStatus.Present;
                    var note = status == AttendanceStatus.Present ? null : stored?.Note;
                    return new RegisterMarkInput(s.EnrolmentId, status.ToString(), note);
                }).ToList();
                // A new client id, and as the base the version the file was made from (its register_version row, which the evaluation
                // matched against the stored register): a register saved since, even between that evaluation and this locked save, is
                // a 409 and the whole import rolls back. A phone still holding an older copy is refused when it sends it, never silently
                // laid over the import.
                var fileBase = plans[0].Bases[date];
                var request = new SaveRegisterRequest($"import-{Guid.NewGuid():N}", marks, fileBase == TakenWithoutVersion ? "" : fileBase);
                await attendance.SaveAsync(sheet.Class.Id, date, request, messageParents: date == today, ct);
            }
        }
        return new ImportApplied(rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update));
    }

    // ---------- Sheet ----------

    private sealed record DayMark(DateOnly Date, AttendanceStatus Status);
    /// <summary>A student's changed days, and the file's register version of every date (the base each day's save is checked against).</summary>
    private sealed record AttendancePlan(Guid EnrolmentId, IReadOnlyList<DayMark> Changes, IReadOnlyDictionary<DateOnly, string> Bases);

    public const string VersionRowKey = "register_version";
    public const string MissingVersions = "Download the template again; this file is missing its register_version row.";
    /// <summary>The version written for a register taken without a client id (an older save): blank would read as "not taken".</summary>
    private const string TakenWithoutVersion = "taken";

    private static bool IsVersionRow(ImportRowData row) =>
        string.Equals((row.Cells.GetValueOrDefault(RosterRows.AdmissionColumn) ?? "").Trim(), VersionRowKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>The day's stored register version as the template writes it: its client id, "taken" without one, blank when not taken.</summary>
    private static string VersionOf(AttendanceSheet sheet, DateOnly date) =>
        sheet.Taken.TryGetValue(date, out var clientId) ? clientId ?? TakenWithoutVersion : "";
    private sealed record StoredMark(AttendanceStatus Status, string? Note);

    private sealed record AttendanceSheet(
        SchoolClass Class, string SessionName, DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Dates, IReadOnlyList<RosterStudent> Students,
        IReadOnlyDictionary<DateOnly, string?> Taken, IReadOnlyDictionary<(DateOnly, Guid), StoredMark> Marks, string SheetFor)
    {
        public IReadOnlyDictionary<string, RosterStudent> ByAdmissionNo =>
            Students.GroupBy(s => s.AdmissionNo, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First());
    }

    private static List<AttendancePlan> Plans(IReadOnlyList<ImportRowResult> rows) =>
        rows.Where(r => r.Status is ImportRowStatus.New or ImportRowStatus.Update).Select(r => (AttendancePlan)r.Plan!).ToList();

    /// <summary>The class, its session's name, its active students (as the register lists them) and the registers taken in the range.</summary>
    private async Task<AttendanceSheet> LoadAsync(ImportScope scope, CancellationToken ct)
    {
        var (from, to) = Range(scope);
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope.RequireGuid("classId"), ct) ?? throw new NotFoundException("Class");
        var session = await db.AcademicSessions.AsNoTracking().Where(s => s.Id == cls.SessionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        var students = (await db.Enrolments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.SessionId == cls.SessionId)
                .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id,
                    (e, s) => new { s.Id, EnrolmentId = e.Id, s.FirstName, s.LastName, s.AdmissionNo })
                .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct))
            .Select(x => new RosterStudent(x.Id, x.EnrolmentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();

        var registers = await db.AttendanceRegisters.AsNoTracking().Where(r => r.ClassId == cls.Id && r.Date >= from && r.Date <= to)
            .Select(r => new { r.Id, r.Date, r.ClientId }).ToListAsync(ct);
        var registerIds = registers.Select(r => r.Id).ToList();
        var marks = await db.AttendanceMarks.AsNoTracking().Where(m => registerIds.Contains(m.RegisterId))
            .Select(m => new { m.RegisterId, m.EnrolmentId, m.Status, m.Note }).ToListAsync(ct);
        var dateOf = registers.ToDictionary(r => r.Id, r => r.Date);

        return new AttendanceSheet(cls, session, from, to, Dates(from, to), students,
            registers.ToDictionary(r => r.Date, r => r.ClientId),
            marks.ToDictionary(m => (dateOf[m.RegisterId], m.EnrolmentId), m => new StoredMark(m.Status, m.Note)),
            SheetFor.Label(session, cls.Name, $"{Iso(from)} to {Iso(to)}"));
    }

    /// <summary>
    /// The file's date columns, by heading: yyyy-MM-dd as the template writes it, day/month/year, or an Excel date serial (2000 or later). A
    /// date outside the range, or two columns for one date, is refused (400) rather than skipped; a file with no date of the range is 400.
    /// </summary>
    private static List<(string Key, DateOnly Date)> DateColumnsOf(IReadOnlyList<ImportRowData> rows, DateOnly from, DateOnly to)
    {
        var found = new List<(string Key, DateOnly Date)>();
        foreach (var key in rows.Count == 0 ? Enumerable.Empty<string>() : rows[0].Cells.Keys)
        {
            if (key is RosterRows.AdmissionColumn or ClassRoster.StudentColumn or SheetFor.ColumnKey) continue;
            if (HeadingDate(key) is not { } date) continue;
            if (date < from || date > to)
                throw BadRequestException.Field("file",
                    $"The file has a column for {Day(date)}, which is outside the dates you chose ({Day(from)} to {Day(to)}). Choose those dates or remove the column.");
            if (found.Any(f => f.Date == date))
                throw BadRequestException.Field("file", $"The file has two columns for {Day(date)}. Keep one and upload it again.");
            found.Add((key, date));
        }
        if (found.Count == 0)
            throw BadRequestException.Field("file", $"The file has no column for any date from {Day(from)} to {Day(to)}. Download the template for these dates.");
        return found.OrderBy(f => f.Date).ToList();
    }

    /// <summary>The date a column heading names, as the reader keys it ("2026_10_26", "26/10/2026", "46321"), or null.</summary>
    private static DateOnly? HeadingDate(string key)
    {
        var text = key.Replace('_', '-');
        // A plain number is a date only as a whole Excel serial of 2000 or later; "2.5" or "7" is not a date, so the engine names the
        // column as ignored instead of it reading as a day in 1900.
        if (double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var serial))
            return text.All(char.IsAsciiDigit) && serial is >= 36526 and < 2958466 ? DateOnly.FromDateTime(DateTime.FromOADate(serial)) : null;
        return StudentImport.TryParseDate(text, out var date) ? date : null;
    }

    /// <summary>
    /// The scope's dates: both given, from on or before to, to not after today, and at most 31 days. The term and the teacher's window are
    /// the register's own check (<see cref="AttendanceService.EnsureCanEditAsync"/>), in <see cref="EnsureAccessAsync"/>.
    /// </summary>
    private (DateOnly From, DateOnly To) Range(ImportScope scope)
    {
        var from = scope.RequireDate("from");
        var to = scope.RequireDate("to");
        if (from > to) throw BadRequestException.Field("from", "Choose a start date on or before the end date.");
        if (to > Today) throw BadRequestException.Field("to", "Choose today or an earlier date.");
        if (to.DayNumber - from.DayNumber + 1 > MaxDays) throw BadRequestException.Field("to", "Choose at most 31 days.");
        return (from, to);
    }

    private static List<DateOnly> Dates(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

    private static ImportColumn DateColumn(DateOnly date) =>
        new(ImportColumns.KeyOf(Iso(date)), Iso(date), false, $"{Day(date)}: P, A, L or E. Leave blank if the register was not taken.");

    private static AttendanceStatus? ParseLetter(string text) => text.ToUpperInvariant() switch
    {
        "P" => AttendanceStatus.Present,
        "A" => AttendanceStatus.Absent,
        "L" => AttendanceStatus.Late,
        "E" => AttendanceStatus.Excused,
        _ => null,
    };

    private static string Letter(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Absent => "A",
        AttendanceStatus.Late => "L",
        AttendanceStatus.Excused => "E",
        _ => "P",
    };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Day(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
}
