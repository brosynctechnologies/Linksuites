using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SaveExamPeriodRequest(Guid TermId, string? Name, ExamPeriodKind Kind, DateOnly Start, DateOnly End);

public sealed record SaveExamSittingRequest(
    Guid ClassSubjectId, DateOnly Date, string? Start, string? End, int DurationMinutes, string? Venue, string? Instructions, Guid? InvigilatorStaffId);

public sealed record ExamWarningDto(Guid SittingId, string Detail);

public sealed record ExamPeriodDto(Guid Id, Guid TermId, string Name, ExamPeriodKind Kind, DateOnly Start, DateOnly End, DateTimeOffset? PublishedAt);

public sealed record ExamSittingDto(
    Guid Id, Guid PeriodId, Guid ClassSubjectId, string Subject, string SubjectCode, string ClassName,
    DateOnly Date, string Start, string End, int DurationMinutes, string Venue, string Instructions,
    Guid? InvigilatorStaffId, string? Invigilator, IReadOnlyList<ExamWarningDto> Warnings);

public sealed record ExamPeriodViewDto(
    Guid Id, Guid TermId, string Name, ExamPeriodKind Kind, DateOnly Start, DateOnly End, DateTimeOffset? PublishedAt,
    IReadOnlyList<ExamSittingDto> Sittings);

public sealed record PortalExamSittingDto(
    Guid Id, string Subject, string SubjectCode, DateOnly Date, string Start, string End, int DurationMinutes,
    string Venue, string Instructions, string? Invigilator);

public sealed record PortalExamPeriodDto(Guid Id, string Name, ExamPeriodKind Kind, DateOnly Start, DateOnly End, IReadOnlyList<PortalExamSittingDto> Sittings);

public sealed record PortalExamTimetableDto(IReadOnlyList<PortalExamPeriodDto> Periods);

/// <summary>
/// Exam sittings for a term. Leadership creates the period and the sittings, then publishes the period. A class cannot sit two
/// exams at the same time, and a venue cannot be booked twice. An invigilator who is already in another room is saved and warned
/// about. Teachers read published sittings for subjects they teach. Students and parents read the published sittings of their class.
/// The bursar is refused before any period is looked up.
/// </summary>
public sealed class ExamTimetableService(
    IAppDbContext db, ITenantContext tenant, SchoolAccess access, ParentAccess parents, StudentAccess students, TimeProvider clock)
{
    public const int MaxNameLength = 120;
    public const int MaxVenueLength = 200;
    public const int MaxInstructionsLength = 4000;
    public const string ClassOverlap = "This sitting overlaps another exam for the class.";
    public const string VenueOverlap = "This venue is already booked at that time.";
    public const string InvigilatorOverlap = "This teacher is already invigilating another exam at that time.";
    internal const string PrincipalOnly = "Only the principal can set the exam timetable.";

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH:mm:ss"];

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public async Task<IReadOnlyList<ExamPeriodDto>> ListAsync(Guid termId, CancellationToken ct)
    {
        RefuseBursar();
        if (!await db.Terms.AnyAsync(t => t.Id == termId, ct)) throw new NotFoundException("Term");
        var periods = await db.ExamPeriods.AsNoTracking().Where(p => p.TermId == termId).OrderBy(p => p.Start).ThenBy(p => p.Name).ToListAsync(ct);
        if (access.IsLeadership) return periods.Select(ToDto).ToList();

        var taught = await TaughtSubjectIdsAsync(ct);
        if (taught.Count == 0) return [];
        var publishedIds = periods.Where(p => p.PublishedAt is not null).Select(p => p.Id).ToList();
        if (publishedIds.Count == 0) return [];
        var visible = await db.ExamSittings.AsNoTracking()
            .Where(s => publishedIds.Contains(s.PeriodId) && taught.Contains(s.ClassSubjectId))
            .Select(s => s.PeriodId).Distinct().ToListAsync(ct);
        return periods.Where(p => visible.Contains(p.Id)).Select(ToDto).ToList();
    }

    public async Task<ExamPeriodDto> CreatePeriodAsync(SaveExamPeriodRequest r, CancellationToken ct)
    {
        RequireLeadership();
        var name = Required(r.Name, "name", "Enter a name for the exam period.", MaxNameLength);
        if (r.Start == default) throw BadRequestException.Field("start", "Choose the start date.");
        if (r.End == default) throw BadRequestException.Field("end", "Choose the end date.");
        if (r.End < r.Start) throw BadRequestException.Field("end", "The exam period must end on or after it starts.");
        if (!await db.Terms.AnyAsync(t => t.Id == r.TermId, ct)) throw new NotFoundException("Term");
        var period = new ExamPeriod
        {
            TenantId = TenantId, TermId = r.TermId, Name = name, Kind = r.Kind, Start = r.Start, End = r.End,
        };
        db.ExamPeriods.Add(period);
        await db.SaveChangesAsync(ct);
        return ToDto(period);
    }

    public async Task<ExamPeriodViewDto> GetAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var period = await FindPeriodAsync(id, ct);
        var only = await VisibleSubjectsAsync(period, ct);
        var sittings = await SittingsAsync(period.Id, only, null, ct);
        if (!access.IsLeadership && sittings.Count == 0) throw new NotFoundException("This timetable");
        return ToView(period, sittings);
    }

    public async Task<ExamSittingDto> GetSittingAsync(Guid id, CancellationToken ct)
    {
        RefuseBursar();
        var sitting = await db.ExamSittings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("This timetable");
        var period = await FindPeriodAsync(sitting.PeriodId, ct);
        if (!access.IsLeadership)
        {
            if (period.PublishedAt is null || !await access.TeachesClassSubjectAsync(sitting.ClassSubjectId, ct))
                throw new NotFoundException("This timetable");
        }
        return (await SittingsAsync(period.Id, null, sitting.Id, ct)).Single();
    }

    public async Task<ExamSittingDto> AddSittingAsync(Guid periodId, SaveExamSittingRequest r, CancellationToken ct)
    {
        RequireLeadership();
        var period = await FindPeriodAsync(periodId, ct);
        var input = await ReadSittingAsync(period, r, ct);
        var sitting = new ExamSitting { TenantId = TenantId, PeriodId = period.Id, Venue = input.Venue, Instructions = input.Instructions };

        // Joins an import's transaction when one is open, so a file of sittings lands whole or not at all.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.LockAsync(LockKey(TenantId), ct);
        var warnings = await CheckClashesAsync(input, null, ct);
        input.ApplyTo(sitting);
        db.ExamSittings.Add(sitting);
        await db.SaveChangesAsync(ct);
        var saved = (await SittingsAsync(period.Id, null, sitting.Id, ct)).Single() with { Warnings = warnings };
        if (tx is not null) await tx.CommitAsync(ct);
        return saved;
    }

    /// <summary>What <see cref="AddSittingAsync"/> would say about one sitting of an import, without saving it.</summary>
    internal sealed record SittingCheck(string? Error, string? Warning);

    /// <summary>
    /// For the sittings import: checks each request as <see cref="AddSittingAsync"/> would (same fields, same sentences), against the saved
    /// sittings and against the valid requests above it in the same file, so two rows of one file that clash are caught like a saved clash.
    /// A null request (the importer already refused the row) is skipped. Saves nothing; the caller has checked leadership and the period.
    /// </summary>
    internal async Task<IReadOnlyList<SittingCheck?>> CheckSittingsAsync(Guid periodId, IReadOnlyList<SaveExamSittingRequest?> requests, CancellationToken ct)
    {
        var period = await FindPeriodAsync(periodId, ct);
        var saved = new Dictionary<DateOnly, List<Busy>>();
        var planned = new List<Busy>();
        var results = new List<SittingCheck?>(requests.Count);
        foreach (var r in requests)
        {
            if (r is null)
            {
                results.Add(null);
                continue;
            }
            SittingInput input;
            try
            {
                input = await ReadSittingAsync(period, r, ct);
            }
            catch (AppException ex) when (ex is BadRequestException or NotFoundException)
            {
                results.Add(new SittingCheck(ex.Message, null));
                continue;
            }
            if (!saved.TryGetValue(input.Date, out var busy)) saved[input.Date] = busy = await BusyAsync(input.Date, ct);
            var (error, warnings) = Clashes(input, busy.Concat(planned.Where(p => p.Date == input.Date)).ToList());
            if (error is not null)
            {
                results.Add(new SittingCheck(error.Message, null));
                continue;
            }
            planned.Add(new Busy(Guid.NewGuid(), input.ClassId, input.Venue, input.InvigilatorStaffId, input.Start, input.End, input.Date));
            results.Add(new SittingCheck(null, warnings.Count > 0 ? InvigilatorOverlap : null));
        }
        return results;
    }

    /// <summary>
    /// Replaces a sitting (same body as adding one), within its own period. The class and venue clash checks and the invigilator
    /// warning leave the sitting itself out, so moving it frees its old slot. A CBT paper tied to the sitting that nobody has
    /// started follows the new time; one tied to it for another class subject is untied and keeps its window.
    /// </summary>
    public async Task<ExamSittingDto> UpdateSittingAsync(Guid id, SaveExamSittingRequest r, CancellationToken ct)
    {
        RequireLeadership();
        var existing = await db.ExamSittings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("This timetable");
        var period = await FindPeriodAsync(existing.PeriodId, ct);
        var input = await ReadSittingAsync(period, r, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(LockKey(TenantId), ct);
        var sitting = await db.ExamSittings.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("This timetable");
        var warnings = await CheckClashesAsync(input, sitting.Id, ct);
        input.ApplyTo(sitting);

        var papers = await db.CbtPapers.Where(p => p.ExamSittingId == sitting.Id).ToListAsync(ct);
        var paperIds = papers.Select(p => p.Id).ToList();
        var started = paperIds.Count == 0
            ? []
            : (await db.CbtAttempts.Where(a => paperIds.Contains(a.PaperId)).Select(a => a.PaperId).Distinct().ToListAsync(ct)).ToHashSet();
        foreach (var paper in papers)
        {
            if (paper.ClassSubjectId != sitting.ClassSubjectId) paper.ExamSittingId = null;
            else if (!started.Contains(paper.Id))
            {
                paper.OpenAt = At(sitting.Date, sitting.Start);
                paper.CloseAt = At(sitting.Date, sitting.End);
            }
        }
        await db.SaveChangesAsync(ct);
        var saved = (await SittingsAsync(period.Id, null, sitting.Id, ct)).Single() with { Warnings = warnings };
        await tx.CommitAsync(ct);
        return saved;
    }

    /// <summary>Removes a sitting. A CBT paper tied to it keeps its own window.</summary>
    public async Task DeleteSittingAsync(Guid id, CancellationToken ct)
    {
        RequireLeadership();
        var sitting = await db.ExamSittings.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("This timetable");
        db.ExamSittings.Remove(sitting);
        await db.SaveChangesAsync(ct);
    }

    private sealed record SittingInput(
        Guid ClassSubjectId, Guid ClassId, DateOnly Date, TimeOnly Start, TimeOnly End, int Minutes, string Venue, string Instructions, Guid? InvigilatorStaffId)
    {
        public void ApplyTo(ExamSitting s)
        {
            s.ClassSubjectId = ClassSubjectId;
            s.Date = Date;
            s.Start = Start;
            s.End = End;
            s.DurationMinutes = Minutes;
            s.Venue = Venue;
            s.Instructions = Instructions;
            s.InvigilatorStaffId = InvigilatorStaffId;
        }
    }

    private async Task<SittingInput> ReadSittingAsync(ExamPeriod period, SaveExamSittingRequest r, CancellationToken ct)
    {
        var start = ParseTime(r.Start, "start", "Enter the start time as HH:mm, such as 09:00.");
        var end = ParseTime(r.End, "end", "Enter the end time as HH:mm, such as 11:00.");
        if (start >= end) throw BadRequestException.Field("end", "The exam must end after it starts.");
        var minutes = (int)(end - start).TotalMinutes;
        if (r.DurationMinutes != minutes) throw BadRequestException.Field("durationMinutes", "Duration must match the start and end.");
        if (r.Date == default) throw BadRequestException.Field("date", "Choose the date of the exam.");
        if (r.Date < period.Start || r.Date > period.End) throw BadRequestException.Field("date", "Choose a date inside the exam period.");
        var venue = Required(r.Venue, "venue", "Enter the venue.", MaxVenueLength);
        var instructions = Required(r.Instructions, "instructions", "Write the instructions for the sitting.", MaxInstructionsLength);
        await EnsureInvigilatorAsync(r.InvigilatorStaffId, ct);

        var subject = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClassSubjectId, ct)
            ?? throw new NotFoundException("Class subject");
        var classSession = await db.SchoolClasses.Where(c => c.Id == subject.ClassId).Select(c => c.SessionId).FirstAsync(ct);
        var termSession = await db.Terms.Where(t => t.Id == period.TermId).Select(t => t.SessionId).FirstAsync(ct);
        if (classSession != termSession) throw BadRequestException.Field("classSubjectId", "Choose a subject in this exam period's session.");
        return new SittingInput(subject.Id, subject.ClassId, r.Date, start, end, minutes, venue, instructions, r.InvigilatorStaffId);
    }

    /// <summary>Refuses a class or venue clash and returns the invigilator warnings. Called under the timetable lock; <paramref name="self"/> is left out.</summary>
    private async Task<List<ExamWarningDto>> CheckClashesAsync(SittingInput s, Guid? self, CancellationToken ct)
    {
        var busy = (await BusyAsync(s.Date, ct)).Where(b => b.SittingId != self).ToList();
        var (error, warnings) = Clashes(s, busy);
        if (error is not null) throw error;
        return warnings;
    }

    /// <summary>The class or venue clash (as the 409 the screen returns), else the invigilator warnings. <paramref name="busy"/> is the same day's sittings.</summary>
    private static (ConflictException? Error, List<ExamWarningDto> Warnings) Clashes(SittingInput s, IReadOnlyList<Busy> busy)
    {
        if (busy.Any(b => b.ClassId == s.ClassId && Overlaps(s.Start, s.End, b.Start, b.End)))
            return (new ConflictException(ClassOverlap, "exam_class_overlap"), []);
        var venueKey = VenueKey(s.Venue);
        if (busy.Any(b => VenueKey(b.Venue) == venueKey && Overlaps(s.Start, s.End, b.Start, b.End)))
            return (new ConflictException(VenueOverlap, "exam_venue_overlap"), []);
        return (null, s.InvigilatorStaffId is { } staff
            ? busy.Where(b => b.InvigilatorStaffId == staff && Overlaps(s.Start, s.End, b.Start, b.End))
                .OrderBy(b => b.Start).ThenBy(b => b.SittingId)
                .Select(b => new ExamWarningDto(b.SittingId, InvigilatorOverlap)).ToList()
            : []);
    }

    public async Task<ExamPeriodDto> PublishAsync(Guid id, CancellationToken ct)
    {
        RequireLeadership();
        var period = await FindPeriodAsync(id, ct, track: true);
        period.PublishedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToDto(period);
    }

    public async Task<PortalExamTimetableDto> ForSelfAsync(CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        var classId = await CurrentClassIdAsync(me.StudentId, ct);
        return classId is { } id ? await ForClassAsync(id, ct) : new PortalExamTimetableDto([]);
    }

    public async Task<PortalExamTimetableDto> ForChildAsync(Guid studentId, CancellationToken ct)
    {
        await parents.RequireChildAsync(studentId, ct);
        var classId = await CurrentClassIdAsync(studentId, ct);
        return classId is { } id ? await ForClassAsync(id, ct) : new PortalExamTimetableDto([]);
    }

    internal static long LockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x4558414D;

    private async Task<PortalExamTimetableDto> ForClassAsync(Guid classId, CancellationToken ct)
    {
        var rows = await (from s in db.ExamSittings.AsNoTracking()
                          join p in db.ExamPeriods.AsNoTracking() on s.PeriodId equals p.Id
                          where p.PublishedAt != null
                          join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                          where cs.ClassId == classId
                          join subject in db.Subjects.AsNoTracking() on cs.SubjectId equals subject.Id
                          select new { Period = p, Sitting = s, subject.Name, subject.Code }).ToListAsync(ct);
        var names = await InvigilatorNamesAsync(rows.Where(r => r.Sitting.InvigilatorStaffId != null).Select(r => r.Sitting.InvigilatorStaffId!.Value).Distinct().ToList(), ct);
        var periods = rows.GroupBy(r => r.Period.Id).Select(g =>
        {
            var period = g.First().Period;
            var sittings = g.OrderBy(r => r.Sitting.Date).ThenBy(r => r.Sitting.Start).ThenBy(r => r.Name)
                .Select(r => new PortalExamSittingDto(r.Sitting.Id, r.Name, r.Code, r.Sitting.Date, Format(r.Sitting.Start), Format(r.Sitting.End),
                    r.Sitting.DurationMinutes, r.Sitting.Venue, r.Sitting.Instructions,
                    r.Sitting.InvigilatorStaffId is { } staff ? names.GetValueOrDefault(staff) : null)).ToList();
            return new PortalExamPeriodDto(period.Id, period.Name, period.Kind, period.Start, period.End, sittings);
        }).OrderBy(p => p.Start).ThenBy(p => p.Name).ToList();
        return new PortalExamTimetableDto(periods);
    }

    /// <summary>Null when leadership may see every sitting. Otherwise the class subjects this teacher teaches, after the period is published.</summary>
    private async Task<List<Guid>?> VisibleSubjectsAsync(ExamPeriod period, CancellationToken ct)
    {
        if (access.IsLeadership) return null;
        if (period.PublishedAt is null) throw new NotFoundException("This timetable");
        return await TaughtSubjectIdsAsync(ct);
    }

    private async Task<List<Guid>> TaughtSubjectIdsAsync(CancellationToken ct)
    {
        if (await access.StaffIdAsync(ct) is not { } id) return [];
        return await db.ClassSubjects.AsNoTracking().Where(cs => cs.TeacherStaffId == id).Select(cs => cs.Id).ToListAsync(ct);
    }

    private async Task<ExamPeriod> FindPeriodAsync(Guid id, CancellationToken ct, bool track = false)
    {
        var query = track ? db.ExamPeriods : db.ExamPeriods.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("This timetable");
    }

    private async Task EnsureInvigilatorAsync(Guid? staffId, CancellationToken ct)
    {
        if (staffId is { } id && !await db.StaffProfiles.AnyAsync(
                p => p.Id == id && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active), ct))
            throw BadRequestException.Field("invigilatorStaffId", "Choose an active staff member from this school.");
    }

    private async Task<List<Busy>> BusyAsync(DateOnly date, CancellationToken ct) =>
        await (from s in db.ExamSittings
               where s.Date == date
               join cs in db.ClassSubjects on s.ClassSubjectId equals cs.Id
               select new Busy(s.Id, cs.ClassId, s.Venue, s.InvigilatorStaffId, s.Start, s.End, s.Date)).ToListAsync(ct);

    private async Task<List<ExamSittingDto>> SittingsAsync(Guid periodId, IReadOnlyCollection<Guid>? classSubjectIds, Guid? sittingId, CancellationToken ct)
    {
        var rows = await (from s in db.ExamSittings.AsNoTracking()
                          where s.PeriodId == periodId && (sittingId == null || s.Id == sittingId)
                          join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                          join subject in db.Subjects.AsNoTracking() on cs.SubjectId equals subject.Id
                          join cls in db.SchoolClasses.AsNoTracking() on cs.ClassId equals cls.Id
                          select new { Sitting = s, subject.Name, subject.Code, ClassName = cls.Name }).ToListAsync(ct);
        if (classSubjectIds is not null) rows = rows.Where(r => classSubjectIds.Contains(r.Sitting.ClassSubjectId)).ToList();
        var names = await InvigilatorNamesAsync(rows.Where(r => r.Sitting.InvigilatorStaffId != null).Select(r => r.Sitting.InvigilatorStaffId!.Value).Distinct().ToList(), ct);
        return rows.OrderBy(r => r.Sitting.Date).ThenBy(r => r.Sitting.Start).ThenBy(r => r.Name)
            .Select(r => new ExamSittingDto(r.Sitting.Id, r.Sitting.PeriodId, r.Sitting.ClassSubjectId, r.Name, r.Code, r.ClassName,
                r.Sitting.Date, Format(r.Sitting.Start), Format(r.Sitting.End), r.Sitting.DurationMinutes, r.Sitting.Venue, r.Sitting.Instructions,
                r.Sitting.InvigilatorStaffId, r.Sitting.InvigilatorStaffId is { } staff ? names.GetValueOrDefault(staff) : null, [])).ToList();
    }

    private async Task<Dictionary<Guid, string>> InvigilatorNamesAsync(List<Guid> staffIds, CancellationToken ct)
    {
        if (staffIds.Count == 0) return [];
        return await db.StaffProfiles.AsNoTracking().Where(p => staffIds.Contains(p.Id))
            .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.Id, u.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    private static bool Overlaps(TimeOnly start, TimeOnly end, TimeOnly otherStart, TimeOnly otherEnd) =>
        start < otherEnd && otherStart < end;

    private static string VenueKey(string venue) => venue.Trim().ToLowerInvariant();

    private static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A sitting's clock time is Lagos local; a CBT window is stored as the UTC instant.</summary>
    private static DateTimeOffset At(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), SchoolCalendar.LagosOffset).ToUniversalTime();

    private static TimeOnly ParseTime(string? value, string field, string empty)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw BadRequestException.Field(field, empty);
        if (!TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw BadRequestException.Field(field, "Enter the time as HH:mm, such as 09:00.");
        return time;
    }

    private static string Required(string? value, string field, string empty, int max)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) throw BadRequestException.Field(field, empty);
        if (text.Length > max) throw BadRequestException.Field(field, $"Use {max} characters or fewer.");
        return text;
    }

    private void RefuseBursar()
    {
        if (access.IsBursar) throw new ForbiddenException("The bursar does not manage the exam timetable.");
    }

    private void RequireLeadership()
    {
        if (!access.IsLeadership) throw new ForbiddenException(PrincipalOnly);
    }

    private static ExamPeriodDto ToDto(ExamPeriod period) =>
        new(period.Id, period.TermId, period.Name, period.Kind, period.Start, period.End, period.PublishedAt);

    private static ExamPeriodViewDto ToView(ExamPeriod period, IReadOnlyList<ExamSittingDto> sittings) =>
        new(period.Id, period.TermId, period.Name, period.Kind, period.Start, period.End, period.PublishedAt, sittings);

    private sealed record Busy(Guid SittingId, Guid ClassId, string Venue, Guid? InvigilatorStaffId, TimeOnly Start, TimeOnly End, DateOnly Date);

    private async Task<Guid?> CurrentClassIdAsync(Guid studentId, CancellationToken ct) =>
        await db.Enrolments.AsNoTracking()
            .Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Select(e => (Guid?)e.ClassId).FirstOrDefaultAsync(ct);
}
