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

    /// <summary>Wave 4: a lab scientist (never a cashier, never the support view). No clinical access beyond the lab's own orders.</summary>
    public bool IsLabScientist => !IsSupportView && !IsCashier && ClinicalRole == ClinicalRole.LabScientist;

    /// <summary>Wave 4: a pharmacist (never a cashier, never the support view). No clinical access beyond the prescriptions they dispense.</summary>
    public bool IsPharmacist => !IsSupportView && !IsCashier && ClinicalRole == ClinicalRole.Pharmacist;

    /// <summary>Wave 4: works the lab (the worklist, samples, labels and results of lab orders): clinical staff and lab scientists.</summary>
    public bool CanWorkLab => IsClinical || IsLabScientist;

    /// <summary>Wave 4: edits the lab catalogue: leadership and lab scientists.</summary>
    public bool CanEditLabCatalogue => IsLeadership || IsLabScientist;

    /// <summary>Wave 4: dispenses prescriptions (and reads them to do so): clinical staff and pharmacists.</summary>
    public bool CanDispense => IsClinical || IsPharmacist;

    /// <summary>Wave 4: manages pharmacy stock (items, receipts, counts) and reads its reports and alerts: leadership and pharmacists.</summary>
    public bool CanManageStock => IsLeadership || IsPharmacist;
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

    public const string LabRefused = "Only lab scientists, doctors and nurses can work the lab.";
    public const string PharmacyRefused = "Only pharmacists, doctors and nurses can dispense.";
    public const string StockRefused = "Only pharmacists and the clinic's leadership can manage pharmacy stock.";

    /// <summary>Wave 4: 403 <see cref="LabRefused"/> unless the caller works the lab.</summary>
    public void EnsureLab(CareCaller c)
    {
        if (!c.CanWorkLab) throw new ForbiddenException(LabRefused);
    }

    /// <summary>Wave 4: 403 <see cref="PharmacyRefused"/> unless the caller dispenses.</summary>
    public void EnsureDispenser(CareCaller c)
    {
        if (!c.CanDispense) throw new ForbiddenException(PharmacyRefused);
    }

    /// <summary>Wave 4: 403 <see cref="StockRefused"/> unless the caller manages pharmacy stock.</summary>
    public void EnsureStock(CareCaller c)
    {
        if (!c.CanManageStock) throw new ForbiddenException(StockRefused);
    }

    /// <summary>403 unless the caller is the Owner or an Admin.</summary>
    public void EnsureLeadership(CareCaller c)
    {
        if (!c.IsLeadership) throw new ForbiddenException();
    }
}
