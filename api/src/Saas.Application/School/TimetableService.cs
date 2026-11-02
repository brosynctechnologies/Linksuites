using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>One lesson as the school sends it: day (Mon to Sat), period (1 to 12), start and end as HH:mm, and a subject of the class.</summary>
public sealed record TimetableSlotInput(string? Day, int Period, string? Start, string? End, Guid ClassSubjectId);
public sealed record TimetableSlotDto(Guid Id, string Day, int Period, string Start, string End, Guid ClassSubjectId, string Subject, string SubjectCode, string? Teacher);
/// <summary>The teacher of a lesson also teaches <c>OtherClass</c> at the same day and period.</summary>
public sealed record TimetableClashDto(string Teacher, string Day, int Period, string OtherClass);
public sealed record TimetableDto(Guid ClassId, string ClassName, IReadOnlyList<TimetableSlotDto> Slots, IReadOnlyList<TimetableClashDto> Warnings);
/// <summary>What the parent and student portals show: the lessons only.</summary>
public sealed record PortalTimetableDto(Guid ClassId, string ClassName, IReadOnlyList<TimetableSlotDto> Slots);

/// <summary>
/// The weekly timetable of each class (day × period). Leadership saves a class's whole timetable at once; leadership and the staff who teach the class
/// read it; the portals read the child's current class through <see cref="ForClassAsync"/>. A teacher booked
/// in two classes at the same day and period is a warning, never a refusal.
/// </summary>
public sealed class TimetableService(IAppDbContext db, SchoolAccess access, IDbErrors dbErrors)
{
    public const int MaxPeriod = 12;
    public const string CannotRead = "You can only see the timetables of classes you teach.";
    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    public async Task<TimetableDto> GetAsync(Guid classId, CancellationToken ct)
    {
        var cls = await ClassAsync(classId, ct);
        if (!access.IsLeadership && !await access.TeachesClassAsync(classId, ct)) throw new ForbiddenException(CannotRead);
        return await ViewAsync(cls, ct);
    }

    /// <summary>Replaces the class's timetable with <paramref name="slots"/> (empty clears it). All or nothing.</summary>
    public async Task<TimetableDto> SaveAsync(Guid classId, IReadOnlyList<TimetableSlotInput>? slots, CancellationToken ct)
    {
        var cls = await ClassAsync(classId, ct);
        if (!access.IsLeadership) throw new ForbiddenException(PrincipalOnly);
        // A missing body is a mistake, never "clear the week": an explicit empty list clears it.
        var input = slots ?? throw BadRequestException.Field("slots", "Send the timetable slots.");
        var subjectIds = (await db.ClassSubjects.AsNoTracking().Where(c => c.ClassId == classId).Select(c => c.Id).ToListAsync(ct)).ToHashSet();

        var clean = new List<TimetableSlot>(input.Count);
        var taken = new HashSet<(SchoolDay, int)>();
        for (var i = 0; i < input.Count; i++)
        {
            var r = input[i] ?? throw BadRequestException.Field($"slots[{i}]", "Enter the lesson.");
            if (CheckSlot(r, subjectIds, out var day, out var start, out var end) is { } problem)
                throw BadRequestException.Field($"slots[{i}].{problem.Field}", problem.Message);
            if (!taken.Add((day, r.Period))) throw BadRequestException.Field($"slots[{i}].period", SameSlot);
            clean.Add(new TimetableSlot { TenantId = cls.TenantId, ClassId = classId, Day = day, Period = r.Period, Start = start, End = end, ClassSubjectId = r.ClassSubjectId });
        }

        // Joins an import's transaction when one is open.
        await using (var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await db.LockAsync(LockKey(classId), ct);
            var existing = await db.TimetableSlots.Where(s => s.ClassId == classId).ToListAsync(ct);
            db.TimetableSlots.RemoveRange(existing);
            await db.SaveChangesAsync(ct); // the unique day and period index is checked per row, so the old rows go first
            db.TimetableSlots.AddRange(clean);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
            {
                throw new ConflictException("The timetable was changed from another device at the same moment. Reload and try again.", "timetable_conflict");
            }
            if (tx is not null) await tx.CommitAsync(ct);
        }
        return await ViewAsync(cls, ct);
    }

    /// <summary>The class's lessons for a portal. The caller has already checked who may see the class (the child's or student's own current class).</summary>
    public async Task<PortalTimetableDto?> ForClassAsync(Guid? classId, CancellationToken ct)
    {
        if (classId is not { } id) return null;
        var cls = await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cls is null) return null;
        return new PortalTimetableDto(cls.Id, cls.Name, await SlotsAsync(cls.Id, ct));
    }

    internal static long LockKey(Guid classId) => BitConverter.ToInt64(classId.ToByteArray(), 8) ^ 0x54494D45;

    private async Task<SchoolClass> ClassAsync(Guid classId, CancellationToken ct) =>
        await db.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");

    internal const string PrincipalOnly = "Only the principal can change the timetable.";
    internal const string SameSlot = "The class already has a lesson at this day and period. Choose another period.";

    /// <summary>
    /// The first thing wrong with one lesson (its field and the sentence), or null with the lesson's day and times. Shared by the save and the
    /// timetable import, so both refuse a lesson with the same words. The same day and period twice is checked by the caller.
    /// </summary>
    internal static (string Field, string Message)? CheckSlot(TimetableSlotInput r, IReadOnlySet<Guid> subjectIds, out SchoolDay day, out TimeOnly start, out TimeOnly end)
    {
        start = end = default;
        if (ParseDay(r.Day) is not { } parsedDay)
        {
            day = default;
            return ("day", "Choose a day from Monday to Saturday.");
        }
        day = parsedDay;
        if (r.Period is < 1 or > MaxPeriod) return ("period", $"Choose a period from 1 to {MaxPeriod}.");
        if (ParseTime(r.Start) is not { } s) return ("start", "Enter the start time as HH:mm, such as 08:00.");
        if (ParseTime(r.End) is not { } e) return ("end", "Enter the end time as HH:mm, such as 08:40.");
        (start, end) = (s, e);
        if (start >= end) return ("end", "The lesson must end after it starts.");
        if (!subjectIds.Contains(r.ClassSubjectId)) return ("classSubjectId", "Choose a subject taught in this class.");
        return null;
    }

    /// <summary>"Monday" for <see cref="SchoolDay.Mon"/>.</summary>
    internal static string DayName(SchoolDay day) => ((DayOfWeek)(((int)day + 1) % 7)).ToString();

    internal static SchoolDay? ParseDay(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length < 3 || !text.All(char.IsLetter)) return null;
        foreach (var day in Enum.GetValues<SchoolDay>())
        {
            var full = ((DayOfWeek)(((int)day + 1) % 7)).ToString(); // Mon is DayOfWeek.Monday
            if (string.Equals(text, day.ToString(), StringComparison.OrdinalIgnoreCase) || string.Equals(text, full, StringComparison.OrdinalIgnoreCase))
                return day;
        }
        return null;
    }

    internal static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParseExact((value ?? "").Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static string Format(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    private async Task<TimetableDto> ViewAsync(SchoolClass cls, CancellationToken ct)
    {
        var slots = await SlotsAsync(cls.Id, ct);
        return new TimetableDto(cls.Id, cls.Name, slots, await ClashesAsync(cls, ct));
    }

    private async Task<List<TimetableSlotDto>> SlotsAsync(Guid classId, CancellationToken ct)
    {
        var rows = await (from s in db.TimetableSlots.AsNoTracking()
                          where s.ClassId == classId
                          join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                          join subject in db.Subjects.AsNoTracking() on cs.SubjectId equals subject.Id
                          select new { Slot = s, subject.Name, subject.Code, cs.TeacherStaffId }).ToListAsync(ct);
        var names = await TeacherNamesAsync(rows.Where(r => r.TeacherStaffId != null).Select(r => r.TeacherStaffId!.Value).Distinct().ToList(), ct);
        return rows.OrderBy(r => r.Slot.Day).ThenBy(r => r.Slot.Period)
            .Select(r => new TimetableSlotDto(r.Slot.Id, r.Slot.Day.ToString(), r.Slot.Period, Format(r.Slot.Start), Format(r.Slot.End), r.Slot.ClassSubjectId,
                r.Name, r.Code, r.TeacherStaffId is { } t ? names.GetValueOrDefault(t) : null))
            .ToList();
    }

    /// <summary>Lessons of this class whose teacher also has a lesson in another class of the same session at the same day and period.</summary>
    private async Task<List<TimetableClashDto>> ClashesAsync(SchoolClass cls, CancellationToken ct)
    {
        var mine = await (from s in db.TimetableSlots.AsNoTracking()
                          where s.ClassId == cls.Id
                          join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                          where cs.TeacherStaffId != null
                          select new { s.Day, s.Period, Teacher = cs.TeacherStaffId!.Value }).ToListAsync(ct);
        if (mine.Count == 0) return [];
        var teachers = mine.Select(m => m.Teacher).Distinct().ToList();
        var others = await (from s in db.TimetableSlots.AsNoTracking()
                            where s.ClassId != cls.Id
                            join cs in db.ClassSubjects.AsNoTracking() on s.ClassSubjectId equals cs.Id
                            join c in db.SchoolClasses.AsNoTracking() on s.ClassId equals c.Id
                            where c.SessionId == cls.SessionId && cs.TeacherStaffId != null && teachers.Contains(cs.TeacherStaffId!.Value)
                            select new { s.Day, s.Period, Teacher = cs.TeacherStaffId!.Value, c.Name }).ToListAsync(ct);
        if (others.Count == 0) return [];
        var names = await TeacherNamesAsync(teachers, ct);
        return (from m in mine
                join o in others on new { m.Day, m.Period, m.Teacher } equals new { o.Day, o.Period, o.Teacher }
                orderby m.Day, m.Period, o.Name
                select new TimetableClashDto(names.GetValueOrDefault(m.Teacher) ?? "A teacher", m.Day.ToString(), m.Period, o.Name)).ToList();
    }

    private async Task<Dictionary<Guid, string>> TeacherNamesAsync(List<Guid> staffIds, CancellationToken ct)
    {
        if (staffIds.Count == 0) return [];
        return await db.StaffProfiles.AsNoTracking().Where(p => staffIds.Contains(p.Id))
            .Join(db.Users.AsNoTracking(), p => p.UserId, u => u.Id, (p, u) => new { p.Id, u.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }
}
