using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;

namespace Saas.Application.School.Imports.Importers;

/// <summary>
/// Exam sittings for one exam period, from a list template: one row per sitting with the class (by name, ignoring spaces and case), the
/// subject (name or code), the date, start and end, venue, instructions and an optional invigilator (staff email). Each row is checked as the
/// add-sitting screen checks it (<see cref="ExamTimetableService.CheckSittingsAsync"/>), with its sentences, against the saved sittings and the
/// rows above it in the file, so a class or venue clash between two rows of one file is an error like a saved clash. An invigilator clash is a
/// warning. Every valid row is new and is saved through <see cref="ExamTimetableService.AddSittingAsync"/>. Leadership only.
/// </summary>
public sealed class ExamSittingImporter(IAppDbContext db, SchoolAccess access, ExamTimetableService exams) : IImporter, IImportNotice, IImportWarnings
{
    public const string KindName = "exam-sittings";
    public const string Sheet = "Sittings";

    public string Kind => KindName;
    public string SheetName => Sheet;
    public string NoticeTitle => "Exam sitting import finished";
    public string NoticeLink => "/app/exams";

    private static readonly ImportColumns AllColumns = new(
    [
        new("class", "class", true, "The class's name, for example JSS 1A."),
        new("subject", "subject", true, "The subject's name or code, for example Mathematics or MTH. It must be taught in the class."),
        new("date", "date", true, "The day of the exam, inside the exam period, for example 2026-11-16 or 16/11/2026."),
        new("start", "start", true, "When it starts, as HH:mm, for example 09:00."),
        new("end", "end", true, "When it ends, as HH:mm, for example 11:00."),
        new("venue", "venue", true, $"Where it is held, up to {ExamTimetableService.MaxVenueLength} characters."),
        new("instructions", "instructions", true, $"What the students should know, up to {ExamTimetableService.MaxInstructionsLength} characters."),
        new("invigilator", "invigilator", false, "Optional. The invigilator's email, as on the staff list."),
    ]);

    public ImportColumns Columns(ImportScope scope) => AllColumns;

    public async Task EnsureAccessAsync(ImportScope scope, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException(ExamTimetableService.PrincipalOnly);
        var periodId = scope.RequireGuid("periodId");
        if (!await db.ExamPeriods.AnyAsync(p => p.Id == periodId, ct)) throw new NotFoundException("This timetable");
    }

    public async Task<ImportTemplate> TemplateAsync(ImportScope scope, CancellationToken ct)
    {
        var periodId = scope.RequireGuid("periodId");
        var period = await db.ExamPeriods.AsNoTracking().FirstAsync(p => p.Id == periodId, ct);
        var notes = new List<string>
        {
            $"Exam period: {period.Name}, {period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd}. Upload this file on that exam period.",
            "One row per sitting. Required: class, subject, date, start, end, venue and instructions. invigilator is optional.",
            "A class can't sit two exams at the same time, and a venue can't be booked twice, also between rows of this file.",
            "An invigilator who is already in another room at that time is saved and shown as a warning.",
            "Every row adds a sitting. To change a sitting, edit it on the screen.",
        };
        return new ImportTemplate(Sheet, AllColumns.All, [], notes);
    }

    public async Task<IReadOnlyList<ImportRowResult>> EvaluateAsync(ImportScope scope, IReadOnlyList<ImportRowData> rows, CancellationToken ct)
    {
        var periodId = scope.RequireGuid("periodId");
        var period = await db.ExamPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, ct) ?? throw new NotFoundException("This timetable");
        var sessionId = await db.Terms.AsNoTracking().Where(t => t.Id == period.TermId).Select(t => t.SessionId).FirstAsync(ct);

        var classes = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == sessionId).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var classByKey = new Dictionary<string, (Guid Id, string Name)>();
        foreach (var c in classes) classByKey.TryAdd(StudentImport.ClassKey(c.Name), (c.Id, c.Name));
        var classIds = classes.Select(c => c.Id).ToList();
        var subjects = await db.ClassSubjects.AsNoTracking().Where(cs => classIds.Contains(cs.ClassId))
            .Join(db.Subjects.AsNoTracking(), cs => cs.SubjectId, s => s.Id, (cs, s) => new { cs.Id, cs.ClassId, s.Name, s.Code }).ToListAsync(ct);
        var staff = await db.StaffProfiles.AsNoTracking()
            .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.Id, u.Email }).ToListAsync(ct);
        var staffByEmail = new Dictionary<string, Guid>();
        foreach (var s in staff) staffByEmail.TryAdd(s.Email.Trim().ToLowerInvariant(), s.Id);

        var early = new List<(ImportRowData Row, string Label, string? Error, SaveExamSittingRequest? Request)>();
        foreach (var row in rows)
        {
            var c = row.Cells;
            var classText = c.GetValueOrDefault("class") ?? "";
            var subjectText = c.GetValueOrDefault("subject") ?? "";
            string? error = null;
            (Guid Id, string Name) cls = default;
            Guid? classSubjectId = null;
            var subjectName = subjectText;

            if (classText.Length == 0) error = "Enter the class.";
            else if (!classByKey.TryGetValue(StudentImport.ClassKey(classText), out cls)) error = $"There is no class called {classText} this session.";
            else if (subjectText.Length == 0) error = "Enter the subject.";
            else
            {
                var key = subjectText.Trim();
                var match = subjects.FirstOrDefault(s => s.ClassId == cls.Id && (string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Code, key, StringComparison.OrdinalIgnoreCase)));
                if (match is null) error = $"{subjectText} is not a subject of {cls.Name}.";
                else (classSubjectId, subjectName) = (match.Id, match.Name);
            }

            var label = $"{(subjectName.Length > 0 ? subjectName : "Sitting")}, {(cls.Name ?? classText)}".Trim(' ', ',');
            DateOnly? date = null;
            var dateText = c.GetValueOrDefault("date") ?? "";
            if (error is null && dateText.Length == 0) error = "Choose the date of the exam.";
            if (error is null && !StudentImport.TryParseDate(dateText, out date)) error = "Enter the date as 2026-11-16 or 16/11/2026.";

            Guid? invigilator = null;
            var email = (c.GetValueOrDefault("invigilator") ?? "").Trim().ToLowerInvariant();
            if (error is null && email.Length > 0)
            {
                if (staffByEmail.TryGetValue(email, out var staffId)) invigilator = staffId;
                else error = "Choose an active staff member from this school.";
            }

            if (error is not null)
            {
                early.Add((row, label, error, null));
                continue;
            }
            var start = ImportTime.Normalise(c.GetValueOrDefault("start"));
            var end = ImportTime.Normalise(c.GetValueOrDefault("end"));
            var minutes = TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s0)
                && TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e0)
                ? (int)(e0 - s0).TotalMinutes : 0;
            early.Add((row, label, null, new SaveExamSittingRequest(classSubjectId!.Value, date!.Value, start, end, minutes,
                c.GetValueOrDefault("venue"), c.GetValueOrDefault("instructions"), invigilator)));
        }

        // The add-sitting screen's own checks, in file order, so a row that clashes with one above it is refused like a saved clash.
        var checks = await exams.CheckSittingsAsync(period.Id, early.Select(e => e.Request).ToList(), ct);
        return early.Select((e, i) =>
        {
            var error = e.Error ?? checks[i]?.Error;
            return error is not null
                ? new ImportRowResult(e.Row.Row, ImportRowStatus.Error, e.Label, error, null)
                : new ImportRowResult(e.Row.Row, ImportRowStatus.New, e.Label, checks[i]?.Warning, e.Request);
        }).ToList();
    }

    /// <summary>Invigilator clashes, one line per row: they are saved, as on the screen, but worth a look.</summary>
    public Task<IReadOnlyList<string>> WarningsAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(rows.Where(r => r.Status != ImportRowStatus.Error && r.Message is not null).Select(r => $"Row {r.Row}: {r.Message}").ToList());

    public async Task<ImportApplied> ApplyAsync(ImportScope scope, IReadOnlyList<ImportRowResult> rows, CancellationToken ct)
    {
        var periodId = scope.RequireGuid("periodId");
        var added = 0;
        // The add-sitting screen's own save, row by row in file order. It joins the engine's transaction and checks clashes again under its lock.
        foreach (var row in rows.Where(r => r.Status == ImportRowStatus.New))
        {
            await exams.AddSittingAsync(periodId, (SaveExamSittingRequest)row.Plan!, ct);
            added++;
        }
        return new ImportApplied(added, 0);
    }
}
