using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.School;

/// <summary>
/// Who the signed-in member is to the school, and which classes they teach. Every later teacher check (registers, scores, remarks,
/// class announcements, message threads) asks this one place. Results are cached for the request.
/// </summary>
public sealed class SchoolAccess(IAppDbContext db, ICurrentUser user, StaffService staff)
{
    private Guid? staffId;
    private bool staffIdLoaded;

    /// <summary>Owner or Admin: sees and edits the whole school.</summary>
    public bool IsLeadership => user.Role is MemberRole.Owner or MemberRole.Admin;

    /// <summary>Accountant: the school's bursar.</summary>
    public bool IsBursar => user.Role == MemberRole.Accountant;

    /// <summary>The signed-in member's staff profile id, or null when they have none (not a school member).</summary>
    public async Task<Guid?> StaffIdAsync(CancellationToken ct)
    {
        if (staffIdLoaded) return staffId;
        staffId = user.UserId is { } userId ? await staff.StaffIdForUserAsync(userId, ct) : null;
        staffIdLoaded = true;
        return staffId;
    }

    /// <summary>True when the member is the class teacher of the class or teaches any subject in it.</summary>
    public async Task<bool> TeachesClassAsync(Guid classId, CancellationToken ct)
    {
        if (await StaffIdAsync(ct) is not { } id) return false;
        return await db.SchoolClasses.AnyAsync(c => c.Id == classId && c.ClassTeacherStaffId == id, ct)
            || await db.ClassSubjects.AnyAsync(cs => cs.ClassId == classId && cs.TeacherStaffId == id, ct);
    }

    /// <summary>Every class the member is class teacher of or teaches a subject in. Empty when they have no staff profile.</summary>
    public async Task<HashSet<Guid>> TaughtClassIdsAsync(CancellationToken ct)
    {
        if (await StaffIdAsync(ct) is not { } id) return [];
        var own = await db.SchoolClasses.Where(c => c.ClassTeacherStaffId == id).Select(c => c.Id).ToListAsync(ct);
        var subjects = await db.ClassSubjects.Where(cs => cs.TeacherStaffId == id).Select(cs => cs.ClassId).ToListAsync(ct);
        return own.Concat(subjects).ToHashSet();
    }

    /// <summary>True when the member is the teacher assigned to this subject in this class.</summary>
    public async Task<bool> TeachesClassSubjectAsync(Guid classSubjectId, CancellationToken ct)
    {
        if (await StaffIdAsync(ct) is not { } id) return false;
        return await db.ClassSubjects.AnyAsync(cs => cs.Id == classSubjectId && cs.TeacherStaffId == id, ct);
    }
}
