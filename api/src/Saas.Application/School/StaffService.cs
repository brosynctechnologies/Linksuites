using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

public sealed record StaffDto(
    Guid StaffId, Guid MembershipId, Guid UserId, string FullName, string Email, MemberRole Role,
    string JobTitle, string? StaffNo, string? Phone, bool IsYou);
public sealed record SaveStaffRequest(string JobTitle, string? StaffNo, string? Phone);
public sealed record SetClassTeacherRequest(Guid? StaffId);
public sealed record SetSubjectTeacherRequest(Guid? StaffId);
public sealed record MyClassDto(Guid ClassId, string ClassName);
public sealed record MyClassSubjectDto(Guid ClassSubjectId, Guid ClassId, string ClassName, Guid SubjectId, string SubjectName, string SubjectCode);
public sealed record MyClassesDto(IReadOnlyList<MyClassDto> ClassTeacherOf, IReadOnlyList<MyClassSubjectDto> Subjects);
/// <summary>The signed-in member: role, job title and staff profile id (null when they have no profile).</summary>
public sealed record StaffMeDto(MemberRole Role, string JobTitle, Guid? StaffId);

/// <summary>
/// Staff profiles and teaching assignments. Every member of the school has exactly one profile, created when the membership
/// starts, with a job title that follows their role.
/// </summary>
public sealed class StaffService(IAppDbContext db, ITenantContext tenant, ICurrentUser user, IDbErrors dbErrors)
{
    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public const string StaffNoTaken = "Another staff member has that staff number. Enter a different one.";

    /// <summary>The staff screen's rules for the profile fields, shared with the staff import: trimmed, blanks as null, length limits.</summary>
    internal static (string JobTitle, string? StaffNo, string? Phone) CleanProfile(SaveStaffRequest r)
    {
        var jobTitle = (r.JobTitle ?? "").Trim();
        if (jobTitle.Length == 0) throw BadRequestException.Field("jobTitle", "Enter the job title, such as Teacher or Bursar.");
        if (jobTitle.Length > 80) throw BadRequestException.Field("jobTitle", "The job title can have at most 80 characters.");
        var staffNo = string.IsNullOrWhiteSpace(r.StaffNo) ? null : r.StaffNo.Trim();
        if (staffNo is { Length: > 30 }) throw BadRequestException.Field("staffNo", "The staff number can have at most 30 characters.");
        var phone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim();
        if (phone is { Length: > 30 }) throw BadRequestException.Field("phone", "The phone number can have at most 30 characters.");
        return (jobTitle, staffNo, phone);
    }

    public static string DefaultJobTitle(MemberRole role) => role switch
    {
        MemberRole.Owner => "Proprietor",
        MemberRole.Admin => "School admin",
        MemberRole.Accountant => "Bursar",
        _ => "Teacher",
    };

    /// <summary>
    /// Safety net for the staff screens only (never the permission path): creates profiles for members that somehow have none.
    /// Profiles normally exist already: signup and invite accept create them, and a migration backfilled older schools.
    /// </summary>
    public async Task EnsureProfilesAsync(CancellationToken ct)
    {
        var missing = await db.Memberships.AsNoTracking()
            .Where(m => !db.StaffProfiles.Any(p => p.MembershipId == m.Id))
            .Select(m => new { m.Id, m.UserId, m.Role }).ToListAsync(ct);
        if (missing.Count == 0) return;
        var tenantId = TenantId;
        foreach (var m in missing)
            db.StaffProfiles.Add(new StaffProfile { TenantId = tenantId, MembershipId = m.Id, UserId = m.UserId, JobTitle = DefaultJobTitle(m.Role) });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            // A concurrent request created them first. Drop ours and use theirs.
            foreach (var entry in db.ChangeTracker.Entries<StaffProfile>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
    }

    /// <summary>Read only: the profile of an active member. Profiles are created when the membership starts (signup and invite accept).</summary>
    public async Task<Guid?> StaffIdForUserAsync(Guid userId, CancellationToken ct) =>
        await db.StaffProfiles.AsNoTracking()
            .Where(p => p.UserId == userId && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active))
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<StaffDto>> ListAsync(CancellationToken ct)
    {
        await EnsureProfilesAsync(ct);
        var rows = await db.Memberships.AsNoTracking().Include(m => m.User).Where(m => m.Status == MembershipStatus.Active)
            .Join(db.StaffProfiles.AsNoTracking(), m => m.Id, p => p.MembershipId, (m, p) => new { Member = m, Profile = p })
            .OrderBy(x => x.Member.CreatedAt).ToListAsync(ct);
        return rows.Select(x => ToDto(x.Member, x.Profile)).ToList();
    }

    public async Task<StaffDto> UpdateAsync(Guid membershipId, SaveStaffRequest r, CancellationToken ct)
    {
        await EnsureProfilesAsync(ct);
        var member = await db.Memberships.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == membershipId, ct) ?? throw new NotFoundException("Staff member");
        var profile = await db.StaffProfiles.FirstAsync(p => p.MembershipId == membershipId, ct);

        var (jobTitle, staffNo, phone) = CleanProfile(r);

        if (staffNo is not null)
        {
            var lower = staffNo.ToLower();
            if (await db.StaffProfiles.AnyAsync(p => p.Id != profile.Id && p.StaffNo != null && p.StaffNo.ToLower() == lower, ct))
                throw new ConflictException(StaffNoTaken, "duplicate_staff_no");
        }
        profile.JobTitle = jobTitle;
        profile.StaffNo = staffNo;
        profile.Phone = phone;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex))
        {
            throw new ConflictException(StaffNoTaken, "duplicate_staff_no");
        }
        return ToDto(member, profile);
    }

    // ---------- Teaching assignments ----------

    public async Task<ClassDto> SetClassTeacherAsync(Guid classId, SetClassTeacherRequest r, CancellationToken ct)
    {
        var cls = await db.SchoolClasses.FirstOrDefaultAsync(c => c.Id == classId, ct) ?? throw new NotFoundException("Class");
        await EnsureStaffAsync(db, r.StaffId, ct);
        cls.ClassTeacherStaffId = r.StaffId;
        await db.SaveChangesAsync(ct);
        var levelName = await db.Levels.AsNoTracking().Where(l => l.Id == cls.LevelId).Select(l => l.Name).FirstAsync(ct);
        return new ClassDto(cls.Id, cls.SessionId, cls.LevelId, levelName, cls.Arm, cls.Name, cls.ClassTeacherStaffId);
    }

    public async Task<ClassSubjectDto> SetSubjectTeacherAsync(Guid classSubjectId, SetSubjectTeacherRequest r, CancellationToken ct)
    {
        var link = await db.ClassSubjects.FirstOrDefaultAsync(cs => cs.Id == classSubjectId, ct) ?? throw new NotFoundException("Class subject");
        await EnsureStaffAsync(db, r.StaffId, ct);
        link.TeacherStaffId = r.StaffId;
        await db.SaveChangesAsync(ct);
        var subject = await db.Subjects.AsNoTracking().FirstAsync(s => s.Id == link.SubjectId, ct);
        return new ClassSubjectDto(link.Id, link.ClassId, link.SubjectId, subject.Name, subject.Code, link.TeacherStaffId);
    }

    /// <summary>A staff id set on a class or class subject must be a staff profile of this school (the tenant filter scopes the lookup).</summary>
    public static async Task EnsureStaffAsync(IAppDbContext db, Guid? staffId, CancellationToken ct)
    {
        if (staffId is { } id && !await db.StaffProfiles.AnyAsync(
                p => p.Id == id && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active), ct))
            throw BadRequestException.Field("staffId", "Choose an active staff member from this school.");
    }

    /// <summary>Who the signed-in member is to the school, so the staff screens can label them and show what their role allows.</summary>
    public async Task<StaffMeDto> MeAsync(CancellationToken ct)
    {
        var role = user.Role ?? throw new ForbiddenException("Sign in with your school account first.");
        var userId = user.UserId ?? throw new ForbiddenException("Sign in with your school account first.");
        var profile = await db.StaffProfiles.AsNoTracking()
            .Where(p => p.UserId == userId && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active))
            .Select(p => new { p.Id, p.JobTitle }).FirstOrDefaultAsync(ct);
        return new StaffMeDto(role, profile?.JobTitle ?? DefaultJobTitle(role), profile?.Id);
    }

    public async Task<MyClassesDto> MyClassesAsync(CancellationToken ct)
    {
        var staffId = user.UserId is { } userId ? await StaffIdForUserAsync(userId, ct) : null;
        if (staffId is null) return new MyClassesDto([], []);
        var sessionId = await db.AcademicSessions.AsNoTracking().Where(s => s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);

        var classes = await db.SchoolClasses.AsNoTracking().Where(c => c.SessionId == sessionId && c.ClassTeacherStaffId == staffId)
            .Join(db.Levels, c => c.LevelId, l => l.Id, (c, l) => new { c.Id, c.Name, c.Arm, l.Order })
            .OrderBy(x => x.Order).ThenBy(x => x.Arm).ToListAsync(ct);
        var subjects = await db.ClassSubjects.AsNoTracking().Where(cs => cs.TeacherStaffId == staffId)
            .Join(db.SchoolClasses, cs => cs.ClassId, c => c.Id, (cs, c) => new { Link = cs, Class = c })
            .Where(x => x.Class.SessionId == sessionId)
            .Join(db.Levels, x => x.Class.LevelId, l => l.Id, (x, l) => new { x.Link, x.Class, l.Order })
            .Join(db.Subjects, x => x.Link.SubjectId, s => s.Id, (x, s) => new { x.Link, x.Class, x.Order, Subject = s })
            .OrderBy(x => x.Order).ThenBy(x => x.Class.Arm).ThenBy(x => x.Subject.Name).ToListAsync(ct);

        return new MyClassesDto(
            classes.Select(c => new MyClassDto(c.Id, c.Name)).ToList(),
            subjects.Select(x => new MyClassSubjectDto(x.Link.Id, x.Class.Id, x.Class.Name, x.Subject.Id, x.Subject.Name, x.Subject.Code)).ToList());
    }

    private StaffDto ToDto(Membership m, StaffProfile p) =>
        new(p.Id, m.Id, m.UserId, m.User!.FullName, m.User.Email!, m.Role, p.JobTitle, p.StaffNo, p.Phone, m.UserId == user.UserId);
}
