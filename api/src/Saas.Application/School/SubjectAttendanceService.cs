using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record SubjectAttendanceSettingsDto(bool Enabled);
public sealed record SubjectAttendanceSettingsRequest(bool Enabled);
/// <summary>One lesson of the day from the timetable, with its register's counts once taken.</summary>
public sealed record SubjectLessonDto(
    Guid ClassId, string ClassName, Guid ClassSubjectId, string Subject, string SubjectCode, int Period, string Start, string End,
    bool Taken, int Present, int Absent, int Late);
/// <summary><c>Lessons</c> is empty while subject attendance is off, and on a Sunday.</summary>
public sealed record SubjectLessonsDto(bool Enabled, DateOnly Date, IReadOnlyList<SubjectLessonDto> Lessons);
public sealed record SubjectMarkDto(Guid EnrolmentId, Guid StudentId, string StudentName, string AdmissionNo, AttendanceStatus Status);
/// <summary><c>Taken</c> is false until someone saves it (every mark is then Present). <c>Locked</c>: the class's term is locked, so saves are refused.</summary>
public sealed record SubjectRegisterDto(
    Guid ClassSubjectId, Guid ClassId, string ClassName, string Subject, DateOnly Date, int Period, string Start, string End,
    bool Taken, bool Locked, string? ClientId, IReadOnlyList<SubjectMarkDto> Marks);
public sealed record SubjectMarkInput(Guid EnrolmentId, string? Status);
public sealed record SaveSubjectRegisterRequest(string? ClientId, IReadOnlyList<SubjectMarkInput>? Marks);
/// <summary><c>Percent</c> is present or late of the lessons marked, or null with none.</summary>
public sealed record SubjectAttendanceRowDto(Guid EnrolmentId, Guid StudentId, string Name, string AdmissionNo, int Lessons, int Present, int Absent, int Late, double? Percent);
public sealed record SubjectAttendanceReportDto(
    Guid ClassSubjectId, Guid ClassId, string ClassName, string Subject, Guid TermId, string TermName, int Lessons, IReadOnlyList<SubjectAttendanceRowDto> Students);
public sealed record StudentSubjectDto(Guid ClassSubjectId, string Subject, string SubjectCode, int Lessons, int Present, int Absent, int Late, double? Percent);
/// <summary>One student's term, per subject. In the portals <c>Subjects</c> is empty while the school has subject attendance off.</summary>
public sealed record StudentSubjectAttendanceDto(bool Enabled, Guid TermId, string TermName, IReadOnlyList<StudentSubjectDto> Subjects);

/// <summary>
/// Attendance per subject, when the school turns it on: a register for each lesson on the timetable (a class subject at a period on a date),
/// taken by the subject teacher of that class subject or by leadership. Marks are Present, Absent or Late. The dates are the daily
/// register's (today or earlier, the current term, the last 7 days for a teacher) and a locked class term refuses saves. Daily registers,
/// their absence alerts and the results' days present are unchanged: subject registers feed only their own reports.
/// </summary>
public sealed class SubjectAttendanceService(
    IAppDbContext db, ITenantContext tenant, SchoolAccess access, AttendanceService attendance, ParentAccess parents, StudentAccess students,
    IDbErrors dbErrors, TimeProvider clock)
{
    public const string Off = "Subject attendance is off. The principal can turn it on in Settings.";
    public const string NotYourSubject = "You can only take registers for subjects you teach.";
    public const string TermLocked = "This term is locked for the class, so registers can't change. Ask the principal to unlock it.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    // ---------- Settings ----------

    public async Task<SubjectAttendanceSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can turn subject attendance on or off.");
        return new SubjectAttendanceSettingsDto(await EnabledAsync(ct));
    }

    public async Task<SubjectAttendanceSettingsDto> SetSettingsAsync(SubjectAttendanceSettingsRequest r, CancellationToken ct)
    {
        if (!access.IsLeadership) throw new ForbiddenException("Only the principal can turn subject attendance on or off.");
        var profile = await db.SchoolProfiles.FirstAsync(ct);
        profile.SubjectAttendanceEnabled = r.Enabled;
        await db.SaveChangesAsync(ct);
        return new SubjectAttendanceSettingsDto(profile.SubjectAttendanceEnabled);
    }

    // ---------- Lessons of a day ----------

    /// <summary>The day's lessons in classes of the current session: a teacher's own subjects, or every lesson for leadership (one class with <paramref name="classId"/>).</summary>
    public async Task<SubjectLessonsDto> LessonsAsync(DateOnly? date, Guid? classId, CancellationToken ct)
    {
        RefuseBursar();
        var day = date ?? SchoolCalendar.Today(clock.GetUtcNow());
        if (!await EnabledAsync(ct)) return new SubjectLessonsDto(false, day, []);
        if (DayOf(day) is not { } schoolDay) return new SubjectLessonsDto(true, day, []);

        var staffId = await access.StaffIdAsync(ct);
        if (!access.IsLeadership && staffId is null) return new SubjectLessonsDto(true, day, []);
        var query = from s in db.TimetableSlots.AsNoTracking()
                    where s.Day == schoolDay
                    join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                    join c in db.SchoolClasses.AsNoTracking() on s.ClassId equals c.Id
                    join subject in db.Subjects.AsNoTracking() on cs.SubjectId equals subject.Id
                    where db.AcademicSessions.Any(x => x.Id == c.SessionId && x.IsCurrent)
                    select new { Slot = s, cs.TeacherStaffId, ClassName = c.Name, subject.Name, subject.Code };
        if (!access.IsLeadership) query = query.Where(x => x.TeacherStaffId == staffId);
        if (classId is { } only) query = query.Where(x => x.Slot.ClassId == only);
        var rows = await query.ToListAsync(ct);

        var classSubjectIds = rows.Select(r => r.Slot.ClassSubjectId).Distinct().ToList();
        var registers = await db.SubjectRegisters.AsNoTracking().Where(r => r.Date == day && classSubjectIds.Contains(r.ClassSubjectId)).ToListAsync(ct);
        var registerIds = registers.Select(r => r.Id).ToList();
        var counts = await db.SubjectAttendanceMarks.AsNoTracking().Where(m => registerIds.Contains(m.RegisterId))
            .GroupBy(m => new { m.RegisterId, m.Status }).Select(g => new { g.Key.RegisterId, g.Key.Status, N = g.Count() }).ToListAsync(ct);

        var lessons = rows.OrderBy(r => r.Slot.Period).ThenBy(r => r.ClassName).Select(r =>
        {
            var register = registers.FirstOrDefault(x => x.ClassSubjectId == r.Slot.ClassSubjectId && x.Period == r.Slot.Period);
            int N(AttendanceStatus status) => register is null ? 0 : counts.Where(c => c.RegisterId == register.Id && c.Status == status).Sum(c => c.N);
            return new SubjectLessonDto(r.Slot.ClassId, r.ClassName, r.Slot.ClassSubjectId, r.Name, r.Code, r.Slot.Period, Time(r.Slot.Start), Time(r.Slot.End),
                register is not null, N(AttendanceStatus.Present), N(AttendanceStatus.Absent), N(AttendanceStatus.Late));
        }).ToList();
        return new SubjectLessonsDto(true, day, lessons);
    }

    // ---------- Register ----------

    public async Task<SubjectRegisterDto> GetAsync(Guid classSubjectId, DateOnly date, int period, CancellationToken ct)
    {
        RefuseBursar();
        await RequireEnabledAsync(ct);
        var (cs, cls, subject) = await LoadAsync(classSubjectId, ct);
        await EnsureSubjectTeacherAsync(cs.Id, ct);
        if (date > SchoolCalendar.Today(clock.GetUtcNow())) throw BadRequestException.Field("date", "Choose today or an earlier date.");
        var register = await db.SubjectRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.ClassSubjectId == cs.Id && r.Date == date && r.Period == period, ct);
        var (start, end) = register is not null ? (register.Start, register.End) : await LessonTimesAsync(cs, date, period, ct);
        return await ToDtoAsync(cs, cls, subject, date, period, start, end, register, ct);
    }

    public async Task<SubjectRegisterDto> SaveAsync(Guid classSubjectId, DateOnly date, int period, SaveSubjectRegisterRequest request, CancellationToken ct)
    {
        RefuseBursar();
        await RequireEnabledAsync(ct);
        var (cs, cls, subject) = await LoadAsync(classSubjectId, ct);
        await EnsureSubjectTeacherAsync(cs.Id, ct);
        await attendance.EnsureCanEditAsync(date, ct);
        var term = await db.Terms.AsNoTracking().FirstAsync(t => t.IsCurrent, ct);
        if (term.SessionId != cls.SessionId) throw new ConflictException(AttendanceService.CannotChange, "register_locked");

        var roster = await RosterAsync(cls, ct);
        var wanted = ParseMarks(request.Marks ?? [], roster);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId.Trim();
        if (clientId is { Length: > 80 }) throw BadRequestException.Field("clientId", "The client id can have at most 80 characters.");
        var staffId = await access.StaffIdAsync(ct);
        var tenantId = TenantId;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Two saves of one lesson wait for each other; the class term's lock and unlock take the same key as score saves.
        await db.LockAsync(LockKey(cs.Id, date, period), ct);
        await db.LockAsync(ScoreService.LockKey(cls.Id, term.Id), ct);
        if (await db.TermResultLocks.AnyAsync(l => l.ClassId == cls.Id && l.TermId == term.Id, ct)) throw new ConflictException(TermLocked, "term_locked");

        var register = await db.SubjectRegisters.FirstOrDefaultAsync(r => r.ClassSubjectId == cs.Id && r.Date == date && r.Period == period, ct);
        if (register is null)
        {
            var (start, end) = await LessonTimesAsync(cs, date, period, ct);
            register = new SubjectRegister
            {
                TenantId = tenantId, ClassId = cls.Id, ClassSubjectId = cs.Id, TermId = term.Id, Date = date, Period = period,
                Start = start, End = end, TakenByStaffId = staffId, ClientId = clientId,
            };
            db.SubjectRegisters.Add(register);
        }
        else
        {
            register.TakenByStaffId = staffId ?? register.TakenByStaffId;
            register.ClientId = clientId;
        }

        var existing = await db.SubjectAttendanceMarks.Where(m => m.RegisterId == register.Id).ToListAsync(ct);
        // Marks of students who have since left the class stay as they were.
        foreach (var r in roster)
        {
            var status = wanted[r.EnrolmentId];
            var mark = existing.FirstOrDefault(m => m.EnrolmentId == r.EnrolmentId);
            if (mark is null) db.SubjectAttendanceMarks.Add(new SubjectAttendanceMark { TenantId = tenantId, RegisterId = register.Id, EnrolmentId = r.EnrolmentId, Status = status });
            else mark.Status = status;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("The register was saved from another device at the same moment. Open it again to see the latest.", "register_conflict");
        }
        await tx.CommitAsync(ct);
        return await ToDtoAsync(cs, cls, subject, date, period, register.Start, register.End, register, ct);
    }

    // ---------- Reports ----------

    /// <summary>One class subject's term: each student on the roster with their lessons marked. The subject teacher, the class teacher and leadership.</summary>
    public async Task<SubjectAttendanceReportDto> ReportAsync(Guid classSubjectId, Guid? termId, CancellationToken ct)
    {
        RefuseBursar();
        var (cs, cls, subject) = await LoadAsync(classSubjectId, ct);
        if (!access.IsLeadership && !await access.TeachesClassSubjectAsync(cs.Id, ct)
            && !(await access.StaffIdAsync(ct) is { } staffId && cls.ClassTeacherStaffId == staffId))
            throw new ForbiddenException("You can only see attendance for subjects you teach, or for your own class.");
        var term = await TermAsync(termId, ct);
        if (term.SessionId != cls.SessionId) throw BadRequestException.Field("termId", "Choose a term of this class's session.");

        var lessons = await db.SubjectRegisters.AsNoTracking().CountAsync(r => r.ClassSubjectId == cs.Id && r.TermId == term.Id, ct);
        var counts = await (from m in db.SubjectAttendanceMarks.AsNoTracking()
                            join r in db.SubjectRegisters.AsNoTracking() on m.RegisterId equals r.Id
                            where r.ClassSubjectId == cs.Id && r.TermId == term.Id
                            group m by new { m.EnrolmentId, m.Status } into g
                            select new { g.Key.EnrolmentId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        var roster = await RosterAsync(cls, ct);
        var rows = roster.Select(r =>
        {
            int N(AttendanceStatus s) => counts.Where(c => c.EnrolmentId == r.EnrolmentId && c.Status == s).Sum(c => c.N);
            return Row(r, N(AttendanceStatus.Present), N(AttendanceStatus.Absent), N(AttendanceStatus.Late));
        }).ToList();
        return new SubjectAttendanceReportDto(cs.Id, cls.Id, cls.Name, subject.Name, term.Id, term.Name, lessons, rows);
    }

    /// <summary>One student's term, per subject. Leadership and the teachers of the student's class, as the daily attendance report.</summary>
    public async Task<StudentSubjectAttendanceDto> StudentAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        RefuseBursar();
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student");
        var term = await TermAsync(termId, ct);
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.StudentId == studentId && e.SessionId == term.SessionId, ct);
        if (enrolment is null)
        {
            if (!access.IsLeadership) throw new ForbiddenException("You can only see students of classes you teach.");
            return new StudentSubjectAttendanceDto(await EnabledAsync(ct), term.Id, term.Name, []);
        }
        if (!access.IsLeadership && !await access.TeachesClassAsync(enrolment.ClassId, ct))
            throw new ForbiddenException("You can only see students of classes you teach.");
        return new StudentSubjectAttendanceDto(await EnabledAsync(ct), term.Id, term.Name, await SubjectsAsync(enrolment.Id, term.Id, ct));
    }

    /// <summary>The parent portal: the child's own subjects for the term (the current one by default). Nothing while the school has it off.</summary>
    public async Task<StudentSubjectAttendanceDto> ForChildAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        var child = await parents.RequireChildAsync(studentId, ct);
        return await PortalAsync(child.StudentId, termId, ct);
    }

    /// <summary>The student portal: the student's own subjects for the term (the current one by default).</summary>
    public async Task<StudentSubjectAttendanceDto> ForSelfAsync(Guid? termId, CancellationToken ct)
    {
        var me = await students.RequireSelfAsync(ct);
        return await PortalAsync(me.StudentId, termId, ct);
    }

    private async Task<StudentSubjectAttendanceDto> PortalAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        var term = await TermAsync(termId, ct);
        var enrolmentId = await db.Enrolments.AsNoTracking().Where(e => e.StudentId == studentId && e.SessionId == term.SessionId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        // Only a term of a session the child was enrolled in; any other term id is unknown.
        if (termId is not null && enrolmentId is null) throw new NotFoundException("Term");
        if (!await EnabledAsync(ct) || enrolmentId is not { } id) return new StudentSubjectAttendanceDto(await EnabledAsync(ct), term.Id, term.Name, []);
        return new StudentSubjectAttendanceDto(true, term.Id, term.Name, await SubjectsAsync(id, term.Id, ct));
    }

    /// <summary>The enrolment's marks in the term, per class subject. A subject with no lesson marked for the student is left out.</summary>
    private async Task<List<StudentSubjectDto>> SubjectsAsync(Guid enrolmentId, Guid termId, CancellationToken ct)
    {
        var counts = await (from m in db.SubjectAttendanceMarks.AsNoTracking()
                            where m.EnrolmentId == enrolmentId
                            join r in db.SubjectRegisters.AsNoTracking() on m.RegisterId equals r.Id
                            where r.TermId == termId
                            join cs in db.ClassSubjects.AsNoTracking() on r.ClassSubjectId equals cs.Id
                            join s in db.Subjects.AsNoTracking() on cs.SubjectId equals s.Id
                            group m by new { r.ClassSubjectId, s.Name, s.Code, m.Status } into g
                            select new { g.Key.ClassSubjectId, g.Key.Name, g.Key.Code, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        return counts.GroupBy(c => new { c.ClassSubjectId, c.Name, c.Code }).OrderBy(g => g.Key.Name).Select(g =>
        {
            int N(AttendanceStatus s) => g.Where(c => c.Status == s).Sum(c => c.N);
            int present = N(AttendanceStatus.Present), absent = N(AttendanceStatus.Absent), late = N(AttendanceStatus.Late);
            var lessons = present + absent + late;
            return new StudentSubjectDto(g.Key.ClassSubjectId, g.Key.Name, g.Key.Code, lessons, present, absent, late, Percent(present, late, lessons));
        }).ToList();
    }

    // ---------- Rules ----------

    private void RefuseBursar()
    {
        if (access.IsBursar) throw new ForbiddenException("Attendance is for the school's leadership and teachers.");
    }

    private Task<bool> EnabledAsync(CancellationToken ct) => db.SchoolProfiles.AsNoTracking().Select(p => p.SubjectAttendanceEnabled).FirstOrDefaultAsync(ct);

    private async Task RequireEnabledAsync(CancellationToken ct)
    {
        if (!await EnabledAsync(ct)) throw new ConflictException(Off, "subject_attendance_off");
    }

    private async Task EnsureSubjectTeacherAsync(Guid classSubjectId, CancellationToken ct)
    {
        if (access.IsLeadership || await access.TeachesClassSubjectAsync(classSubjectId, ct)) return;
        throw new ForbiddenException(NotYourSubject);
    }

    private async Task<(ClassSubject Cs, SchoolClass Cls, Subject Subject)> LoadAsync(Guid classSubjectId, CancellationToken ct)
    {
        var cs = await db.ClassSubjects.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classSubjectId, ct) ?? throw new NotFoundException("Class subject");
        var cls = await db.SchoolClasses.AsNoTracking().FirstAsync(c => c.Id == cs.ClassId, ct);
        var subject = await db.Subjects.AsNoTracking().FirstAsync(s => s.Id == cs.SubjectId, ct);
        return (cs, cls, subject);
    }

    private async Task<Term> TermAsync(Guid? termId, CancellationToken ct) => termId is { } id
        ? await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Term")
        : await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.IsCurrent, ct) ?? throw new NotFoundException("Term");

    /// <summary>The lesson's times from the class's timetable: the class subject at that day and period. 404 when there is no such lesson.</summary>
    private async Task<(TimeOnly Start, TimeOnly End)> LessonTimesAsync(ClassSubject cs, DateOnly date, int period, CancellationToken ct)
    {
        if (DayOf(date) is not { } day) throw new NotFoundException("This lesson");
        var slot = await db.TimetableSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ClassId == cs.ClassId && s.ClassSubjectId == cs.Id && s.Day == day && s.Period == period, ct)
            ?? throw new NotFoundException("This lesson");
        return (slot.Start, slot.End);
    }

    /// <summary>Monday to Saturday; null on Sunday.</summary>
    internal static SchoolDay? DayOf(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? null : (SchoolDay)((int)date.DayOfWeek - 1);

    private static string Time(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static long LockKey(Guid classSubjectId, DateOnly date, int period) =>
        BitConverter.ToInt64(classSubjectId.ToByteArray(), 8) ^ ((long)date.DayNumber << 20) ^ ((long)period << 8) ^ 0x5B7A0001;

    private static double? Percent(int present, int late, int lessons) =>
        lessons > 0 ? Math.Round((present + late) * 100.0 / lessons, 1, MidpointRounding.AwayFromZero) : null;

    private static SubjectAttendanceRowDto Row(RosterRow r, int present, int absent, int late)
    {
        var lessons = present + absent + late;
        return new SubjectAttendanceRowDto(r.EnrolmentId, r.StudentId, r.Name, r.AdmissionNo, lessons, present, absent, late, Percent(present, late, lessons));
    }

    private sealed record RosterRow(Guid EnrolmentId, Guid StudentId, string Name, string AdmissionNo);

    private async Task<List<RosterRow>> RosterAsync(SchoolClass cls, CancellationToken ct)
    {
        var rows = await db.Enrolments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.SessionId == cls.SessionId)
            .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id,
                (e, s) => new { EnrolmentId = e.Id, StudentId = s.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        return rows.Select(x => new RosterRow(x.EnrolmentId, x.StudentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();
    }

    private static readonly AttendanceStatus[] Allowed = [AttendanceStatus.Present, AttendanceStatus.Absent, AttendanceStatus.Late];

    /// <summary>Every roster enrolment with its status; students left out are Present.</summary>
    private static Dictionary<Guid, AttendanceStatus> ParseMarks(IReadOnlyList<SubjectMarkInput> marks, List<RosterRow> roster)
    {
        var result = roster.ToDictionary(r => r.EnrolmentId, _ => AttendanceStatus.Present);
        var seen = new HashSet<Guid>();
        foreach (var m in marks)
        {
            if (!result.ContainsKey(m.EnrolmentId)) throw BadRequestException.Field("marks", "One of the students is not in this class. Reload the register and try again.");
            if (!seen.Add(m.EnrolmentId)) throw BadRequestException.Field("marks", "Each student can have only one mark.");
            if (m.Status is null || !Enum.TryParse<AttendanceStatus>(m.Status, true, out var status) || !Allowed.Contains(status) || int.TryParse(m.Status, out _))
                throw BadRequestException.Field("marks", "Use Present, Absent or Late for each student.");
            result[m.EnrolmentId] = status;
        }
        return result;
    }

    private async Task<SubjectRegisterDto> ToDtoAsync(
        ClassSubject cs, SchoolClass cls, Subject subject, DateOnly date, int period, TimeOnly start, TimeOnly end, SubjectRegister? register, CancellationToken ct)
    {
        var roster = await RosterAsync(cls, ct);
        var stored = register is null ? [] : await db.SubjectAttendanceMarks.AsNoTracking().Where(m => m.RegisterId == register.Id).ToListAsync(ct);
        var termId = register?.TermId ?? await db.Terms.AsNoTracking().Where(t => t.IsCurrent).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var locked = termId is { } t && await db.TermResultLocks.AsNoTracking().AnyAsync(l => l.ClassId == cls.Id && l.TermId == t, ct);
        var marks = roster.Select(r => new SubjectMarkDto(r.EnrolmentId, r.StudentId, r.Name, r.AdmissionNo,
            stored.FirstOrDefault(m => m.EnrolmentId == r.EnrolmentId)?.Status ?? AttendanceStatus.Present)).ToList();
        return new SubjectRegisterDto(cs.Id, cls.Id, cls.Name, subject.Name, date, period, Time(start), Time(end), register is not null, locked, register?.ClientId, marks);
    }
}
