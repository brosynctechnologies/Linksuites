using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record PromotionStudentDto(Guid StudentId, string Name, string Action);
public sealed record PromotionClassDto(Guid FromClassId, string FromClassName, string ToClassName, IReadOnlyList<PromotionStudentDto> Students);
/// <summary><c>Action</c> is Promote, Repeat or Graduate. <c>ClassId</c> is a class of the current session to move the student to (the new session gets its copy).</summary>
public sealed record UnplacedStudentDto(Guid StudentId, string Name);
/// <summary><c>Unplaced</c> lists active students with no class this session; each must be placed (or graduated) by an override before the promotion can run.</summary>
public sealed record PromotionPreviewDto(IReadOnlyList<PromotionClassDto> Classes, IReadOnlyList<UnplacedStudentDto> Unplaced);
public sealed record PromotionOverride(Guid StudentId, string Action, Guid? ClassId);
public sealed record PromotionRequest(string SessionName, DateOnly Start, DateOnly End, IReadOnlyList<PromotionOverride>? Overrides);
public sealed record PromotionResult(Guid SessionId, string SessionName, int Promoted, int Repeated, int Graduated);

/// <summary>
/// "Start next session": proposes the next class for every class (next level by order, same arm; the final level graduates), lets the
/// principal hold back or move named students, then creates the new session, its terms, the copied classes and subjects and the
/// enrolments in one transaction. Leadership only.
/// </summary>
public sealed class PromotionService(IAppDbContext db, ITenantContext tenant, StudentService students, IDbErrors dbErrors, TimeProvider clock)
{
    public const string AlreadyStarted = "The next session has already been started.";
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    private enum PromotionAction { Promote, Repeat, Graduate }

    /// <summary>Where a student goes: a level and arm in the new session, or null to graduate.</summary>
    private sealed record Target(Level Level, string Arm, string Name);

    private sealed record Row(Student Student, SchoolClass? From, PromotionAction Action, Target? To);

    private sealed class Context
    {
        public required AcademicSession Session { get; init; }
        public required List<Level> Levels { get; init; }
        public required List<SchoolClass> Classes { get; init; }
        public required List<(Student Student, Guid ClassId)> Enrolled { get; init; }
        public required List<Student> Unplaced { get; init; }
    }

    private async Task<Context> LoadAsync(CancellationToken ct)
    {
        var session = await db.AcademicSessions.FirstOrDefaultAsync(s => s.IsCurrent, ct)
            ?? throw BadRequestException.Field("sessionId", "Set a current session first.");
        var levels = await db.Levels.OrderBy(l => l.Order).ThenBy(l => l.Name).ToListAsync(ct);
        var classes = await db.SchoolClasses.Where(c => c.SessionId == session.Id).ToListAsync(ct);
        var enrolled = await db.Enrolments.Where(e => e.SessionId == session.Id)
            .Join(db.Students.Where(s => s.Status == StudentStatus.Active), e => e.StudentId, s => s.Id, (e, s) => new { Student = s, e.ClassId })
            .ToListAsync(ct);
        var unplaced = await db.Students.Where(s => s.Status == StudentStatus.Active
            && !db.Enrolments.Any(e => e.StudentId == s.Id && e.SessionId == session.Id)).OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync(ct);
        return new Context { Session = session, Levels = levels, Classes = classes, Enrolled = enrolled.Select(x => (x.Student, x.ClassId)).ToList(), Unplaced = unplaced };
    }

    /// <summary>The default next class: the same arm in the next level, else that level's first arm, else the same arm (created). Null for the final level.</summary>
    private static Target? NextOf(Context ctx, SchoolClass from)
    {
        var index = ctx.Levels.FindIndex(l => l.Id == from.LevelId);
        if (index < 0 || index == ctx.Levels.Count - 1) return null;
        var next = ctx.Levels[index + 1];
        var arms = ctx.Classes.Where(c => c.LevelId == next.Id).OrderBy(c => c.Arm, StringComparer.OrdinalIgnoreCase).ToList();
        var match = arms.FirstOrDefault(c => string.Equals(c.Arm, from.Arm, StringComparison.OrdinalIgnoreCase)) ?? arms.FirstOrDefault();
        return match is null ? new Target(next, from.Arm, $"{next.Name}{from.Arm}") : new Target(next, match.Arm, match.Name);
    }

    private static Target SameAs(Context ctx, SchoolClass c) => new(ctx.Levels.First(l => l.Id == c.LevelId), c.Arm, c.Name);

    private static string FullName(Student s) => $"{s.FirstName} {s.LastName}";

    private static List<Row> Decide(Context ctx, IReadOnlyList<PromotionOverride> overrides)
    {
        var byStudent = new Dictionary<Guid, PromotionOverride>();
        foreach (var o in overrides)
        {
            if (!Enum.TryParse<PromotionAction>(o.Action, true, out _))
                throw BadRequestException.Field("overrides", "Use Promote, Repeat or Graduate for each student.");
            if (!byStudent.TryAdd(o.StudentId, o))
                throw BadRequestException.Field("overrides", "Each student can have only one change.");
            if (ctx.Enrolled.All(e => e.Student.Id != o.StudentId) && ctx.Unplaced.All(s => s.Id != o.StudentId))
                throw BadRequestException.Field("overrides", "One of the students is not an active student of this session. Reload the page and try again.");
            if (o.ClassId is { } cid && ctx.Classes.All(c => c.Id != cid))
                throw BadRequestException.Field("overrides", "Choose classes of the current session.");
        }

        var rows = new List<Row>();
        foreach (var (student, classId) in ctx.Enrolled)
        {
            var from = ctx.Classes.First(c => c.Id == classId);
            var next = NextOf(ctx, from);
            var (action, to) = (next is null ? PromotionAction.Graduate : PromotionAction.Promote, next);
            if (byStudent.TryGetValue(student.Id, out var o))
            {
                var wanted = Enum.Parse<PromotionAction>(o.Action, true);
                var chosen = o.ClassId is { } cid ? ctx.Classes.First(c => c.Id == cid) : null;
                switch (wanted)
                {
                    case PromotionAction.Graduate:
                        (action, to) = (PromotionAction.Graduate, null);
                        break;
                    case PromotionAction.Repeat:
                        (action, to) = (PromotionAction.Repeat, SameAs(ctx, chosen ?? from));
                        break;
                    default:
                        if (chosen is not null) (action, to) = (PromotionAction.Promote, SameAs(ctx, chosen));
                        else if (next is null) throw BadRequestException.Field("overrides", $"Choose a class for {FullName(student)}, or graduate them.");
                        break;
                }
            }
            rows.Add(new Row(student, from, action, to));
        }

        // Active students with no class this session are placed only by an override: into a chosen class's copy, or graduated.
        foreach (var student in ctx.Unplaced)
        {
            if (!byStudent.TryGetValue(student.Id, out var o)) continue;
            if (Enum.Parse<PromotionAction>(o.Action, true) == PromotionAction.Graduate)
            {
                rows.Add(new Row(student, null, PromotionAction.Graduate, null));
                continue;
            }
            if (o.ClassId is not { } cid)
                throw BadRequestException.Field("overrides", $"Choose a class for {FullName(student)}, or graduate them.");
            rows.Add(new Row(student, null, PromotionAction.Promote, SameAs(ctx, ctx.Classes.First(c => c.Id == cid))));
        }
        return rows;
    }

    public async Task<PromotionPreviewDto> PreviewAsync(CancellationToken ct)
    {
        var ctx = await LoadAsync(ct);
        var rows = Decide(ctx, []);
        var classes = ctx.Classes
            .OrderBy(c => ctx.Levels.FindIndex(l => l.Id == c.LevelId)).ThenBy(c => c.Arm, StringComparer.OrdinalIgnoreCase)
            .Select(c => new PromotionClassDto(c.Id, c.Name, NextOf(ctx, c)?.Name ?? "Graduate",
                rows.Where(r => r.From?.Id == c.Id).OrderBy(r => r.Student.LastName).ThenBy(r => r.Student.FirstName)
                    .Select(r => new PromotionStudentDto(r.Student.Id, FullName(r.Student), r.Action.ToString())).ToList()))
            .ToList();
        return new PromotionPreviewDto(classes, ctx.Unplaced.Select(s => new UnplacedStudentDto(s.Id, FullName(s))).ToList());
    }

    public async Task<PromotionResult> PromoteAsync(PromotionRequest r, CancellationToken ct)
    {
        var name = (r.SessionName ?? "").Trim();
        if (name.Length == 0) throw BadRequestException.Field("sessionName", "Enter the name of the new session, such as 2027/2028.");
        if (name.Length > 20) throw BadRequestException.Field("sessionName", "The session name can have at most 20 characters.");
        if (r.End <= r.Start) throw BadRequestException.Field("end", "The session must end after it starts.");
        if (r.End.DayNumber - r.Start.DayNumber > 800) throw BadRequestException.Field("end", "A session can run for at most 800 days.");

        var tenantId = TenantId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // One promotion at a time per school, so two clicks cannot both start the next session.
        await db.LockAsync(BitConverter.ToInt64(tenantId.ToByteArray(), 0) ^ 0x5C0001, ct);

        var ctx = await LoadAsync(ct);
        if (await db.AcademicSessions.AnyAsync(s => s.StartDate > ctx.Session.StartDate, ct)) throw new ConflictException(AlreadyStarted, "session_started");
        if (await db.AcademicSessions.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("A session with that name already exists. Enter a different name.", "duplicate_session");
        if (r.Start <= ctx.Session.StartDate) throw BadRequestException.Field("start", "The new session must start after the current one.");

        var rows = Decide(ctx, r.Overrides ?? []);
        if (ctx.Unplaced.Any(s => rows.All(x => x.Student.Id != s.Id)))
            throw new ConflictException("Some active students have no class this session. Place them in a class first.", "students_unplaced");

        // The old session and its current term stop being current first: only one of each may be current at a time.
        ctx.Session.IsCurrent = false;
        foreach (var term in await db.Terms.Where(t => t.IsCurrent).ToListAsync(ct)) term.IsCurrent = false;
        await db.SaveChangesAsync(ct);

        var session = new AcademicSession { TenantId = tenantId, Name = name, StartDate = r.Start, EndDate = r.End, IsCurrent = true };
        db.AcademicSessions.Add(session);
        AddTerms(session, tenantId);

        // Every class is copied with its class teacher and subjects; promotion targets that don't exist yet are created.
        var copies = new Dictionary<(Guid LevelId, string Arm), SchoolClass>();
        var oldClassIds = ctx.Classes.Select(c => c.Id).ToList();
        var subjects = await db.ClassSubjects.Where(cs => oldClassIds.Contains(cs.ClassId)).ToListAsync(ct);
        foreach (var c in ctx.Classes)
        {
            var copy = new SchoolClass { TenantId = tenantId, SessionId = session.Id, LevelId = c.LevelId, Arm = c.Arm, Name = c.Name, ClassTeacherStaffId = c.ClassTeacherStaffId };
            db.SchoolClasses.Add(copy);
            copies[(c.LevelId, c.Arm.ToLowerInvariant())] = copy;
            foreach (var cs in subjects.Where(x => x.ClassId == c.Id))
                db.ClassSubjects.Add(new ClassSubject { TenantId = tenantId, ClassId = copy.Id, SubjectId = cs.SubjectId, TeacherStaffId = cs.TeacherStaffId });
        }

        int promoted = 0, repeated = 0, graduated = 0;
        var today = SchoolCalendar.Today(clock.GetUtcNow());
        var graduates = new List<Student>();
        foreach (var row in rows)
        {
            if (row.To is null)
            {
                row.Student.Status = StudentStatus.Graduated;
                row.Student.StatusDate = today;
                row.Student.StatusReason = $"Graduated at the end of {ctx.Session.Name}";
                graduates.Add(row.Student);
                graduated++;
                continue;
            }
            if (!copies.TryGetValue((row.To.Level.Id, row.To.Arm.ToLowerInvariant()), out var target))
            {
                target = new SchoolClass { TenantId = tenantId, SessionId = session.Id, LevelId = row.To.Level.Id, Arm = row.To.Arm, Name = row.To.Name };
                db.SchoolClasses.Add(target);
                copies[(row.To.Level.Id, row.To.Arm.ToLowerInvariant())] = target;
            }
            db.Enrolments.Add(new Enrolment { TenantId = tenantId, StudentId = row.Student.Id, SessionId = session.Id, ClassId = target.Id });
            if (row.Action == PromotionAction.Repeat) repeated++;
            else promoted++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            // The session name was checked under the lock, so any unique violation here means the data changed underneath us.
            throw new ConflictException("The promotion couldn't be completed because the data changed. Refresh and try again.", "promotion_conflict");
        }
        // Graduates are signed out like any student who leaves.
        foreach (var g in graduates) await students.EndStudentSessionsAsync(g, ct);
        await tx.CommitAsync(ct);
        return new PromotionResult(session.Id, session.Name, promoted, repeated, graduated);
    }

    /// <summary>The signup rule (Sep-Dec, Jan-Apr, Apr-Jul) moved to start on the given date and never running past the session end.</summary>
    private void AddTerms(AcademicSession session, Guid tenantId)
    {
        var anchor = new DateOnly(session.StartDate.Year, 9, 1);
        var shift = session.StartDate.DayNumber - anchor.DayNumber;
        DateOnly Move(DateOnly d) => d.AddDays(shift) > session.EndDate ? session.EndDate : d.AddDays(shift);
        foreach (var t in SchoolCalendar.Terms(session.StartDate.Year))
            db.Terms.Add(new Term
            {
                TenantId = tenantId, SessionId = session.Id, Number = t.Number, Name = t.Name, StartDate = Move(t.Start), EndDate = Move(t.End),
                NextTermStarts = t.NextStarts is { } n ? Move(n) : null, IsCurrent = t.Number == 1,
            });
    }
}
