using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// Who the signed-in member is to the clinic. "Clinical access" means a Doctor or Nurse clinical role; the medical director (Owner)
/// always has it. A cashier (Accountant) never does, and support's read-only view never does.
/// </summary>
public sealed record CareCaller(Guid UserId, MemberRole MemberRole, ClinicalRole ClinicalRole, bool IsSupportView)
{
    /// <summary>Owner or Admin: staff, settings, wards, prices, reports, waivers.</summary>
    public bool IsLeadership => MemberRole is MemberRole.Owner or MemberRole.Admin;

    /// <summary>Accountant: bills, deposits, payments, receipts.</summary>
    public bool IsCashier => MemberRole == MemberRole.Accountant;

    /// <summary>May see clinical notes, diagnoses, prescriptions, results and the medication chart.</summary>
    public bool IsClinical => !IsSupportView
        && (MemberRole == MemberRole.Owner || (!IsCashier && ClinicalRole is ClinicalRole.Doctor or ClinicalRole.Nurse));
}

/// <summary>Every CareLink role check asks this one place. The caller is cached for the request.</summary>
public sealed class CareAccess(IAppDbContext db, ICurrentUser user)
{
    public const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";

    private CareCaller? caller;

    public async Task<CareCaller> CallerAsync(CancellationToken ct)
    {
        if (caller is not null) return caller;
        var userId = user.UserId ?? throw new ForbiddenException();
        var role = user.Role ?? throw new ForbiddenException();
        var clinicalRole = await db.CareStaffProfiles.AsNoTracking()
            .Where(p => p.UserId == userId && db.Memberships.Any(m => m.Id == p.MembershipId && m.Status == MembershipStatus.Active))
            .Select(p => (ClinicalRole?)p.ClinicalRole).FirstOrDefaultAsync(ct);
        caller = new CareCaller(userId, role, clinicalRole ?? CareStaff.DefaultClinicalRole(role, null), user.IsSupportView);
        return caller;
    }

    /// <summary>403 "Only doctors and nurses can see clinical notes." unless the caller has clinical access.</summary>
    public void EnsureClinical(CareCaller c)
    {
        if (!c.IsClinical) throw new ForbiddenException(ClinicalRefused);
    }

    /// <summary>403 unless the caller is the Owner or an Admin.</summary>
    public void EnsureLeadership(CareCaller c)
    {
        if (!c.IsLeadership) throw new ForbiddenException();
    }
}
