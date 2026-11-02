using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

public sealed record CareStaffDto(
    Guid UserId, Guid MembershipId, string FullName, string Email, MemberRole MemberRole, ClinicalRole ClinicalRole, string? Title,
    string? LicenceNumber, bool HasSignature, bool IsClinical, bool IsYou);
public sealed record CareStaffMeDto(
    Guid UserId, string FullName, MemberRole MemberRole, ClinicalRole ClinicalRole, string? Title, string? LicenceNumber,
    bool IsClinical, bool IsLeadership, bool IsCashier);
public sealed record SaveCareStaffRequest(ClinicalRole ClinicalRole, string? Title, string? LicenceNumber);

/// <summary>Clinic staff as the clinic sees them: clinical role, title and licence. Leadership manages them; everyone reads their own.</summary>
public sealed class CareStaffService(IAppDbContext db, CareAccess access, ICurrentUser user)
{
    public async Task<IReadOnlyList<CareStaffDto>> ListAsync(CancellationToken ct)
    {
        access.EnsureLeadership(await access.CallerAsync(ct));
        var rows = await db.Memberships.AsNoTracking().Include(m => m.User).Where(m => m.Status == MembershipStatus.Active)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var profiles = await db.CareStaffProfiles.AsNoTracking().ToDictionaryAsync(p => p.MembershipId, ct);
        return rows.Select(m => ToDto(m, profiles.GetValueOrDefault(m.Id))).ToList();
    }

    public async Task<CareStaffDto> UpdateAsync(Guid userId, SaveCareStaffRequest r, CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        access.EnsureLeadership(caller);
        // Nobody but the medical director edits their own clinic profile, so a demoted admin's stale token can't restore a role.
        if (userId == caller.UserId && caller.MemberRole != MemberRole.Owner) throw new ForbiddenException(CareStaff.SelfRoleRefused);
        var member =await db.Memberships.Include(m => m.User).FirstOrDefaultAsync(m => m.UserId == userId && m.Status == MembershipStatus.Active, ct)
            ?? throw new NotFoundException("Staff member");

        if (!Enum.IsDefined(r.ClinicalRole)) throw BadRequestException.Field("clinicalRole", "Choose Doctor, Nurse, Front desk or none.");
        if (member.Role == MemberRole.Accountant && r.ClinicalRole != ClinicalRole.None)
            throw BadRequestException.Field("clinicalRole", CareStaff.CashierRefused);
        var title = Clean(r.Title);
        if (title is { Length: > 80 }) throw BadRequestException.Field("title", "The title can have at most 80 characters.");
        var licence = Clean(r.LicenceNumber);
        if (licence is { Length: > 40 }) throw BadRequestException.Field("licenceNumber", "The licence number can have at most 40 characters.");

        var profile = await db.CareStaffProfiles.FirstOrDefaultAsync(p => p.MembershipId == member.Id, ct);
        // Only the medical director gives or changes an admin's clinical role, so an admin can't grant themselves clinical access.
        var current = profile?.ClinicalRole ?? CareStaff.DefaultClinicalRole(member.Role, null);
        if (member.Role == MemberRole.Admin && caller.MemberRole != MemberRole.Owner && r.ClinicalRole != current)
            throw new ForbiddenException(CareStaff.AdminRoleRefused);
        if (profile is null)
        {
            profile = new CareStaffProfile { TenantId = member.TenantId, MembershipId = member.Id, UserId = member.UserId };
            db.CareStaffProfiles.Add(profile);
        }
        profile.ClinicalRole = r.ClinicalRole;
        profile.Title = title;
        profile.LicenceNumber = licence;
        await db.SaveChangesAsync(ct);
        return ToDto(member, profile);
    }

    public async Task<CareStaffMeDto> MeAsync(CancellationToken ct)
    {
        var caller = await access.CallerAsync(ct);
        var profile = await db.CareStaffProfiles.AsNoTracking()
            .Where(p => p.UserId == caller.UserId && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active))
            .Select(p => new { p.Title, p.LicenceNumber }).FirstOrDefaultAsync(ct);
        var fullName = await db.Users.Where(u => u.Id == caller.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "";
        return new CareStaffMeDto(caller.UserId, fullName, caller.MemberRole, caller.ClinicalRole, profile?.Title, profile?.LicenceNumber,
            caller.IsClinical, caller.IsLeadership, caller.IsCashier);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private CareStaffDto ToDto(Membership m, CareStaffProfile? p)
    {
        var clinicalRole = p?.ClinicalRole ?? CareStaff.DefaultClinicalRole(m.Role, null);
        var caller = new CareCaller(m.UserId, m.Role, clinicalRole, false);
        return new CareStaffDto(m.UserId, m.Id, m.User!.FullName, m.User.Email!, m.Role, clinicalRole, p?.Title, p?.LicenceNumber,
            p?.SignatureKey is not null, caller.IsClinical, m.UserId == user.UserId);
    }
}
