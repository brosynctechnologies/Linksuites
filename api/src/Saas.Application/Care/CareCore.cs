using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>Creates the clinic profile and the medical director's staff profile (Doctor) when a clinic signs up.</summary>
public sealed class CareProvisioner(IAppDbContext db) : Auth.IProductProvisioner
{
    public string Product => Products.Care;

    public Task ProvisionAsync(Tenant tenant, Auth.SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        db.ClinicProfiles.Add(new ClinicProfile { TenantId = tenant.Id, Name = tenant.Name, Slug = tenant.Slug });

        // The owner's membership was added just before this hook runs; give it its staff profile.
        var ownerMembership = db.ChangeTracker.Entries<Membership>().Select(e => e.Entity).First(m => m.UserId == ownerUserId);
        db.CareStaffProfiles.Add(new CareStaffProfile
        {
            TenantId = tenant.Id, MembershipId = ownerMembership.Id, UserId = ownerUserId, ClinicalRole = CareStaff.DefaultClinicalRole(MemberRole.Owner, null),
        });
        return Task.CompletedTask;
    }
}

/// <summary>Staff profile rules shared by signup, invite accept and the staff screen.</summary>
public static class CareStaff
{
    public const string AdminRoleRefused = "Only the medical director can change an admin's clinical role.";
    public const string CashierRefused = "A cashier can't have a clinical role.";

    /// <summary>
    /// The care rule for an invite's clinical role, given the inviter's role. Returns the role to store; throws 400 when an Admin
    /// invites an Admin with a clinical role (only the medical director grants an admin clinical access).
    /// </summary>
    public static ClinicalRole? CheckInvite(MemberRole inviter, MemberRole invitee, ClinicalRole? clinicalRole)
    {
        if (invitee == MemberRole.Admin && inviter != MemberRole.Owner && clinicalRole is not (null or ClinicalRole.None))
            throw BadRequestException.Field("clinicalRole", AdminRoleRefused);
        return clinicalRole;
    }

    public const string SelfRoleRefused = "Ask the medical director to change your role.";

    /// <summary>
    /// The clinic's word for a role (global constraints' role table), used by the team invite email and the public invite preview:
    /// Doctor, Nurse, Front desk, Cashier, Admin, Admin (doctor), Admin (nurse); the Owner is the Medical director; Staff with no clinical role
    /// stays "Staff".
    /// </summary>
    public static string RoleLabel(MemberRole role, ClinicalRole? clinicalRole) => role switch
    {
        MemberRole.Owner => "Medical director",
        MemberRole.Admin => clinicalRole switch { ClinicalRole.Doctor => "Admin (doctor)", ClinicalRole.Nurse => "Admin (nurse)", _ => "Admin" },
        MemberRole.Accountant => "Cashier",
        MemberRole.Staff => clinicalRole switch
        {
            ClinicalRole.Doctor => "Doctor", ClinicalRole.Nurse => "Nurse", ClinicalRole.FrontDesk => "Front desk", _ => "Staff",
        },
        _ => role.ToString(),
    };

    /// <summary>
    /// The care hook at a member-role change, called after <paramref name="membership"/>.Role is set and before the save (CareLink
    /// clinics only; other products return at once):
    /// <list type="bullet">
    /// <item>only the medical director (Owner) changes their own team's roles for themselves: a non-Owner changing their own role gets 403;</item>
    /// <item>a member moved to Accountant (cashier) loses their clinical role, so moving them back doesn't silently restore it;</item>
    /// <item>a member a non-Owner promotes to Admin loses their clinical role, so an admin can't hand an admin clinical access.</item>
    /// </list>
    /// Adds to the context only; the caller saves.
    /// </summary>
    public static async Task OnRoleChangedAsync(
        IAppDbContext db, Membership membership, MemberRole previousRole, MemberRole? callerRole, Guid? callerUserId, CancellationToken ct)
    {
        var product = await db.Tenants.Where(t => t.Id == membership.TenantId).Select(t => t.Product).FirstOrDefaultAsync(ct);
        if (product != Products.Care) return;
        var byOwner = callerRole == MemberRole.Owner;
        if (!byOwner && membership.UserId == callerUserId) throw new ForbiddenException(SelfRoleRefused);

        var clear = membership.Role == MemberRole.Accountant
            || (membership.Role == MemberRole.Admin && previousRole != MemberRole.Admin && !byOwner);
        if (!clear) return;
        var profile = await db.CareStaffProfiles.FirstOrDefaultAsync(p => p.MembershipId == membership.Id, ct);
        if (profile is not null) profile.ClinicalRole = ClinicalRole.None;
    }

    /// <summary>
    /// The clinical role a new member starts with: the medical director (Owner) is a Doctor unless told otherwise; a cashier
    /// (Accountant) never has one; everyone else takes the role their invite carried, else none.
    /// </summary>
    public static ClinicalRole DefaultClinicalRole(MemberRole role, ClinicalRole? invited) => role switch
    {
        MemberRole.Owner => invited ?? ClinicalRole.Doctor,
        MemberRole.Accountant => ClinicalRole.None,
        _ => invited ?? ClinicalRole.None,
    };

    /// <summary>
    /// The care hook at invite accept: gives the joining member their staff profile with the invite's clinical role
    /// (mirrors SchoolLink's staff profile backfill). Adds to the context only; the caller saves. A member who already has a profile keeps it.
    /// </summary>
    public static async Task AddProfileOnJoinAsync(IAppDbContext db, Guid tenantId, Membership membership, ClinicalRole? invited, CancellationToken ct)
    {
        if (await db.CareStaffProfiles.AnyAsync(p => p.MembershipId == membership.Id, ct)) return;
        db.CareStaffProfiles.Add(new CareStaffProfile
        {
            TenantId = tenantId, MembershipId = membership.Id, UserId = membership.UserId, ClinicalRole = DefaultClinicalRole(membership.Role, invited),
        });
    }
}

/// <summary>CareLink billing usage: beds against the plan. Team seats come from the shared <see cref="Team.TeamUsage"/>.</summary>
public sealed class CareUsage(IAppDbContext db) : IUsageProvider
{
    public string Product => Products.Care;

    /// <summary>Usage key for the console's patient count (no plan limits patients).</summary>
    public const string PatientsKey = "patients";

    /// <summary>
    /// Active beds (inactive beds keep their history but don't count) and, for the admin console (Task 16), every registered patient
    /// (inactive and deceased included: the records the clinic holds), shown without a limit (-1). Staff come from the shared team-seat usage.
    /// </summary>
    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        long activeBeds = await db.Beds.CountAsync(b => b.Active, ct);
        long patients = await db.Patients.LongCountAsync(ct);
        return [new(LimitKeys.Beds, "Beds", activeBeds, plan.Limit(LimitKeys.Beds)), new(PatientsKey, "Patients", patients, -1)];
    }
}

/// <summary>Clinic dates are Africa/Lagos calendar days (UTC+1, no daylight saving).</summary>
public static class CareCalendar
{
    public static readonly TimeSpan LagosOffset = TimeSpan.FromHours(1);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(LagosOffset).DateTime);
}
