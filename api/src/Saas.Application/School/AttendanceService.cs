using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record RegisterMarkDto(Guid EnrolmentId, Guid StudentId, string StudentName, string AdmissionNo, AttendanceStatus Status, string? Note);
/// <summary>One line of the "Message parents" list shown to the teacher after a save: a student newly marked absent, with a ready WhatsApp message.</summary>
public sealed record MessageParentDto(string StudentName, string GuardianName, string WhatsAppUrl);
/// <summary><c>Taken</c> is false for a register nobody has saved yet (every mark is then Present). <c>MessageParents</c> is only filled on a save.</summary>
public sealed record RegisterDto(
    Guid ClassId, string ClassName, DateOnly Date, bool Taken, string? ClientId, IReadOnlyList<RegisterMarkDto> Marks, IReadOnlyList<MessageParentDto> MessageParents);
public sealed record RegisterMarkInput(Guid EnrolmentId, string? Status, string? Note);
/// <summary>
/// <c>BaseClientId</c> is the version of the register the teacher's marks were based on: the stored register's client id when it was
/// loaded, or "" when it hadn't been taken. <c>PriorClientId</c> is an unsent save on the same phone that this one replaces (it may have
/// reached the server without the phone hearing back). When <c>BaseClientId</c> is given and the stored register is neither of them nor this
/// save, someone else changed it since: 409 <c>register_changed</c>. Without <c>BaseClientId</c> the last save wins, as before.
/// </summary>
public sealed record SaveRegisterRequest(string? ClientId, IReadOnlyList<RegisterMarkInput>? Marks, string? BaseClientId = null, string? PriorClientId = null);
public sealed record ClassAttendanceDto(Guid ClassId, string ClassName, bool Taken, int Present, int Absent, int Late, int Excused, double? Percent);
public sealed record StudentAttendanceDto(int DaysOpen, int Present, int Absent, int Late, int Excused);

/// <summary>
/// Class registers. A class has one register per date; saving replaces its marks (last write wins), and the same save sent twice is
/// harmless. A save that names the copy it was based on (<see cref="SaveRegisterRequest.BaseClientId"/>) is refused with 409 when the
/// stored register changed since. Teachers take registers for classes they teach within the last 7 days; leadership any date of the current term.
/// The first Absent mark for a student on a date emails the primary guardian once.
/// </summary>
public sealed class AttendanceService(
    IAppDbContext db, ITenantContext tenant, SchoolAccess access, NotificationQueue notifications, SchoolFeed feed, IDbErrors dbErrors, TimeProvider clock)
{
    public const int TeacherWindowDays = 7;
    public const string CannotChange = "You can't change registers from that date.";
    public const string RegisterChanged = "Someone else updated this register. Open it to review your marks.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    private DateOnly Today => SchoolCalendar.Today(clock.GetUtcNow());

    // ---------- Register ----------

    public async Task<RegisterDto> GetAsync(Guid classId, DateOnly date, CancellationToken ct)
    {
        var cls = await ClassForAsync(classId, ct);
        if (date > Today) throw BadRequestException.Field("date", "Choose today or an earlier date.");
        var register = await db.AttendanceRegisters.AsNoTracking().FirstOrDefaultAsync(r => r.ClassId == classId && r.Date == date, ct);
        return await ToDtoAsync(cls, date, register, [], ct);
    }

    public Task<RegisterDto> SaveAsync(Guid classId, DateOnly date, SaveRegisterRequest request, CancellationToken ct) =>
        SaveAsync(classId, date, request, messageParents: true, ct);

    /// <summary>
    /// The register save. With <paramref name="messageParents"/> false (the attendance import, for a date before today) a fresh absence is
    /// still recorded as alerted, so it is never messaged later, but no email, app notice or WhatsApp line is made. Joins an ambient
    /// transaction (the import's) when one is open; otherwise it opens its own, as before.
    /// </summary>
    internal async Task<RegisterDto> SaveAsync(Guid classId, DateOnly date, SaveRegisterRequest request, bool messageParents, CancellationToken ct)
    {
        var cls = await ClassForAsync(classId, ct);
        var staffId = await access.StaffIdAsync(ct);
        await EnsureCanEditAsync(date, ct);

        var roster = await RosterAsync(cls, ct);
        var wanted = ParseMarks(request.Marks ?? [], roster);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId.Trim();
        if (clientId is { Length: > 80 }) throw BadRequestException.Field("clientId", "The client id can have at most 80 characters.");
        var baseClientId = request.BaseClientId?.Trim();
        if (baseClientId is { Length: > 80 }) throw BadRequestException.Field("baseClientId", "The base client id can have at most 80 characters.");
        var priorClientId = string.IsNullOrWhiteSpace(request.PriorClientId) ? null : request.PriorClientId.Trim();
        if (priorClientId is { Length: > 80 }) throw BadRequestException.Field("priorClientId", "The prior client id can have at most 80 characters.");

        var tenantId = TenantId;
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        // Two saves for one class and date wait for each other, so each one is whole and the second wins. The unique index is the backstop.
        await db.LockAsync(BitConverter.ToInt64(classId.ToByteArray(), 8) ^ ((long)date.DayNumber << 20) ^ 0xA77E0001, ct);

        var register = await db.AttendanceRegisters.FirstOrDefaultAsync(r => r.ClassId == classId && r.Date == date, ct);
        var existing = register is null ? [] : await db.AttendanceMarks.Where(m => m.RegisterId == register.Id).ToListAsync(ct);

        // A resend of the save already stored changes nothing.
        if (register is not null && clientId is not null && register.ClientId == clientId
            && roster.All(r =>
            {
                var have = existing.FirstOrDefault(m => m.EnrolmentId == r.EnrolmentId);
                var (status, note) = wanted[r.EnrolmentId];
                return have is not null && have.Status == status && have.Note == note;
            }))
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            return await ToDtoAsync(cls, date, register, [], ct);
        }

        // A save made from an older copy (a phone sending hours later) must not silently replace marks someone saved since.
        if (register is not null && baseClientId is not null && register.ClientId != clientId)
        {
            var stored = register.ClientId ?? "";
            if (stored != baseClientId && (priorClientId is null || stored != priorClientId))
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw new ConflictException(RegisterChanged, "register_changed");
            }
        }

        if (register is null)
        {
            register = new AttendanceRegister { TenantId = tenantId, ClassId = classId, Date = date, TakenByStaffId = staffId, ClientId = clientId };
            db.AttendanceRegisters.Add(register);
        }
        else
        {
            register.TakenByStaffId = staffId ?? register.TakenByStaffId;
            register.ClientId = clientId;
        }

        // Marks of students no longer in the class (moved elsewhere) stay as they were.
        foreach (var r in roster)
        {
            var (status, note) = wanted[r.EnrolmentId];
            var mark = existing.FirstOrDefault(m => m.EnrolmentId == r.EnrolmentId);
            if (mark is null) db.AttendanceMarks.Add(new AttendanceMark { TenantId = tenantId, RegisterId = register.Id, EnrolmentId = r.EnrolmentId, Status = status, Note = note });
            else { mark.Status = status; mark.Note = note; }
        }

        var absent = roster.Where(r => wanted[r.EnrolmentId].Status == AttendanceStatus.Absent).Select(r => r.StudentId).ToList();
        var already = absent.Count == 0 ? [] : await db.AbsenceAlerts.Where(a => a.Date == date && absent.Contains(a.StudentId)).Select(a => a.StudentId).ToListAsync(ct);
        var fresh = absent.Except(already).ToList();
        foreach (var studentId in fresh) db.AbsenceAlerts.Add(new AbsenceAlert { TenantId = tenantId, StudentId = studentId, Date = date });

        IReadOnlyList<MessageParentDto> message = messageParents ? await QueueAlertsAsync(cls, date, roster.Where(r => fresh.Contains(r.StudentId)).ToList(), ct) : [];
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) || dbErrors.IsConcurrencyConflict(ex))
        {
            throw new ConflictException("The register was saved from another device at the same moment. Open it again to see the latest.", "register_conflict");
        }
        if (tx is not null) await tx.CommitAsync(ct);
        await ResultStaleness.MarkForDateAsync(db, classId, date, ct);
        return await ToDtoAsync(cls, date, register, message, ct);
    }

    /// <summary>
    /// Queues the absence email to each primary guardian who has an address, tells their account in the app,
    /// and builds the "Message parents" list. Saved by the caller.
    /// </summary>
    private async Task<IReadOnlyList<MessageParentDto>> QueueAlertsAsync(SchoolClass cls, DateOnly date, List<RosterRow> students, CancellationToken ct)
    {
        if (students.Count == 0) return [];
        var ids = students.Select(s => s.StudentId).ToList();
        var guardians = await db.StudentGuardians.AsNoTracking().Where(l => l.IsPrimary && ids.Contains(l.StudentId))
            .Join(db.Guardians.AsNoTracking(), l => l.GuardianId, g => g.Id, (l, g) => new { l.StudentId, Guardian = g }).ToListAsync(ct);
        var school = await db.SchoolProfiles.Select(p => p.Name).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("School");
        var dateText = date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        var message = new List<MessageParentDto>();
        foreach (var student in students)
        {
            var guardian = guardians.FirstOrDefault(g => g.StudentId == student.StudentId)?.Guardian;
            if (guardian is null) continue;
            if (guardian.Email is { } email)
                notifications.Email(email, "school.absent",
                    new() { ["guardian"] = guardian.FullName, ["child"] = student.Name, ["class"] = cls.Name, ["date"] = dateText, ["school"] = school }, TenantId);
            if (guardian.UserId is { } userId)
                feed.Notify(userId, "school.absent", $"{student.Name} was marked absent", $"{school} marked {student.Name} absent on {dateText}.", SchoolFeed.ParentLink("attendance", student.StudentId));
            var url = GuardianInviteService.WhatsAppUrl(guardian.Phone,
                $"Hello {guardian.FullName}, {student.Name} was marked absent at {school} today ({dateText}). Please contact the school if this is unexpected.");
            if (url is not null) message.Add(new MessageParentDto(student.Name, guardian.FullName, url));
        }
        return message;
    }

    // ---------- Rules ----------

    private async Task<SchoolClass> ClassForAsync(Guid classId, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        if (!access.IsLeadership && !await access.TeachesClassAsync(classId, ct))
            throw new ForbiddenException("You can only take registers for classes you teach.");
        return cls;
    }

    /// <summary>Today or earlier, inside the current term and, for a teacher, inside the last 7 days. The attendance import checks its range with it.</summary>
    internal async Task EnsureCanEditAsync(DateOnly date, CancellationToken ct)
    {
        var today = Today;
        if (date > today) throw BadRequestException.Field("date", "Choose today or an earlier date.");
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.IsCurrent, ct)
            ?? throw new ConflictException("Set the current term first.", "no_current_term");
        var earliest = access.IsLeadership ? term.StartDate : DateOnly.FromDayNumber(Math.Max(term.StartDate.DayNumber, today.DayNumber - TeacherWindowDays));
        if (date < earliest || date > term.EndDate) throw new ConflictException(CannotChange, "register_locked");
    }

    private sealed record RosterRow(Guid EnrolmentId, Guid StudentId, string Name, string AdmissionNo);

    /// <summary>The class's active students with their enrolment in the class's own session.</summary>
    private async Task<List<RosterRow>> RosterAsync(SchoolClass cls, CancellationToken ct)
    {
        var rows = await db.Enrolments.AsNoTracking().Where(e => e.ClassId == cls.Id && e.SessionId == cls.SessionId)
            .Join(db.Students.AsNoTracking().Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id,
                (e, s) => new { EnrolmentId = e.Id, StudentId = s.Id, s.FirstName, s.LastName, s.AdmissionNo })
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.AdmissionNo).ToListAsync(ct);
        return rows.Select(x => new RosterRow(x.EnrolmentId, x.StudentId, $"{x.FirstName} {x.LastName}", x.AdmissionNo)).ToList();
    }

    /// <summary>Every roster enrolment with its status and note; students left out are Present.</summary>
    private static Dictionary<Guid, (AttendanceStatus Status, string? Note)> ParseMarks(IReadOnlyList<RegisterMarkInput> marks, List<RosterRow> roster)
    {
        var result = roster.ToDictionary(r => r.EnrolmentId, _ => (AttendanceStatus.Present, (string?)null));
        var seen = new HashSet<Guid>();
        foreach (var m in marks)
        {
            if (!result.ContainsKey(m.EnrolmentId)) throw BadRequestException.Field("marks", "One of the students is not in this class. Reload the register and try again.");
            if (!seen.Add(m.EnrolmentId)) throw BadRequestException.Field("marks", "Each student can have only one mark.");
            if (m.Status is null || !Enum.GetNames<AttendanceStatus>().Contains(m.Status, StringComparer.OrdinalIgnoreCase))
                throw BadRequestException.Field("marks", "Use Present, Absent, Late or Excused for each student.");
            var note = string.IsNullOrWhiteSpace(m.Note) ? null : m.Note.Trim();
            if (note is { Length: > 200 }) throw BadRequestException.Field("marks", "A note can have at most 200 characters.");
            result[m.EnrolmentId] = (Enum.Parse<AttendanceStatus>(m.Status, true), note);
        }
        return result;
    }

    private async Task<RegisterDto> ToDtoAsync(SchoolClass cls, DateOnly date, AttendanceRegister? register, IReadOnlyList<MessageParentDto> message, CancellationToken ct)
    {
        var roster = await RosterAsync(cls, ct);
        var stored = register is null ? [] : await db.AttendanceMarks.AsNoTracking().Where(m => m.RegisterId == register.Id).ToListAsync(ct);
        var marks = roster.Select(r =>
        {
            var mark = stored.FirstOrDefault(m => m.EnrolmentId == r.EnrolmentId);
            return new RegisterMarkDto(r.EnrolmentId, r.StudentId, r.Name, r.AdmissionNo, mark?.Status ?? AttendanceStatus.Present, mark?.Note);
        }).ToList();
        return new RegisterDto(cls.Id, cls.Name, date, register is not null, register?.ClientId, marks, message);
    }

    // ---------- Reports ----------

    /// <summary>The day's attendance per class of the current session: counts and the share present or late of those marked.</summary>
    public async Task<IReadOnlyList<ClassAttendanceDto>> SummaryAsync(DateOnly? date, CancellationToken ct)
    {
        var day = date ?? Today;
        var sessionId = await db.AcademicSessions.Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        var classes = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == sessionId)
            .Join(db.Levels.AsNoTracking(), c => c.LevelId, l => l.Id, (c, l) => new { c.Id, c.Name, l.Order }).ToListAsync(ct);
        var counts = await db.AttendanceRegisters.AsNoTracking().Where(r => r.Date == day)
            .Join(db.AttendanceMarks.AsNoTracking(), r => r.Id, m => m.RegisterId, (r, m) => new { r.ClassId, m.Status })
            .GroupBy(x => new { x.ClassId, x.Status }).Select(g => new { g.Key.ClassId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        var taken = (await db.AttendanceRegisters.AsNoTracking().Where(r => r.Date == day).Select(r => r.ClassId).ToListAsync(ct)).ToHashSet();

        return classes.OrderBy(c => c.Order).ThenBy(c => c.Name).Select(c =>
        {
            int N(AttendanceStatus s) => counts.Where(x => x.ClassId == c.Id && x.Status == s).Sum(x => x.N);
            int present = N(AttendanceStatus.Present), absent = N(AttendanceStatus.Absent), late = N(AttendanceStatus.Late), excused = N(AttendanceStatus.Excused);
            var marked = present + absent + late + excused;
            var isTaken = taken.Contains(c.Id);
            double? percent = isTaken && marked > 0 ? Math.Round((present + late) * 100.0 / marked, 1, MidpointRounding.AwayFromZero) : null;
            return new ClassAttendanceDto(c.Id, c.Name, isTaken, present, absent, late, excused, percent);
        }).ToList();
    }

    /// <summary>One student's term: register days of their class, and their marks. Leadership and the student's teachers; not the bursar (spec role table).</summary>
    public async Task<StudentAttendanceDto> StudentAsync(Guid studentId, Guid? termId, CancellationToken ct)
    {
        if (access.IsBursar) throw new ForbiddenException("Attendance is for the school's leadership and teachers.");
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student");
        var term = termId is { } id
            ? await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Term")
            : await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.IsCurrent, ct) ?? throw new NotFoundException("Term");
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.StudentId == studentId && e.SessionId == term.SessionId, ct);
        if (enrolment is null)
        {
            if (!access.IsLeadership) throw new ForbiddenException("You can only see students of classes you teach.");
            return new StudentAttendanceDto(0, 0, 0, 0, 0);
        }
        if (!access.IsLeadership && !await access.TeachesClassAsync(enrolment.ClassId, ct))
            throw new ForbiddenException("You can only see students of classes you teach.");

        var open = await OpenDaysAsync(enrolment.ClassId, term, ct);
        var counts = await MarkCountsAsync(enrolment.Id, term, ct);
        int N(AttendanceStatus s) => counts.GetValueOrDefault(s);
        return new StudentAttendanceDto(open, N(AttendanceStatus.Present), N(AttendanceStatus.Absent), N(AttendanceStatus.Late), N(AttendanceStatus.Excused));
    }

    /// <summary>Days the student was marked present, and days the class register was taken in the term. Late is not included.</summary>
    public async Task<(int Present, int Open)> TermDaysAsync(Guid enrolmentId, Guid termId, CancellationToken ct)
    {
        var enrolment = await db.Enrolments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == enrolmentId, ct) ?? throw new NotFoundException("Enrolment");
        var term = await db.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, ct) ?? throw new NotFoundException("Term");
        var counts = await MarkCountsAsync(enrolmentId, term, ct);
        return (counts.GetValueOrDefault(AttendanceStatus.Present), await OpenDaysAsync(enrolment.ClassId, term, ct));
    }

    private Task<int> OpenDaysAsync(Guid classId, Term term, CancellationToken ct) =>
        db.AttendanceRegisters.AsNoTracking().Where(r => r.ClassId == classId && r.Date >= term.StartDate && r.Date <= term.EndDate).Select(r => r.Date).Distinct().CountAsync(ct);

    private async Task<Dictionary<AttendanceStatus, int>> MarkCountsAsync(Guid enrolmentId, Term term, CancellationToken ct) =>
        (await db.AttendanceMarks.AsNoTracking().Where(m => m.EnrolmentId == enrolmentId)
            .Join(db.AttendanceRegisters.AsNoTracking().Where(r => r.Date >= term.StartDate && r.Date <= term.EndDate), m => m.RegisterId, r => r.Id, (m, r) => m.Status)
            .GroupBy(s => s).Select(g => new { Status = g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Status, x => x.N);
}
