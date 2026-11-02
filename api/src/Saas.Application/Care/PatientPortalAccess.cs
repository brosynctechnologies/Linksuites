using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>A patient as the portal lists them: who, at which clinic, and how the account stands to them.</summary>
public sealed record PortalPatientRef(
    Guid Id, Guid ClinicId, string ClinicName, string ClinicSlug, string Number, string FirstName, string LastName, string FullName,
    PatientLinkRelationship Relationship, PatientStatus Status);

/// <summary>
/// The one gate of the patient portal (Task 15), as SchoolLink's <c>ParentAccess</c> is for parents. A portal user is a customer account
/// (no clinic in the token) linked to patients through <see cref="PatientLink"/>, in any number of clinics. Every portal route asks
/// <see cref="RequirePatientAsync"/> for the patient first:
/// <list type="number">
/// <item>404 for a patient the account has no active link to, so ids of other families' patients and other clinics' records reveal nothing;</item>
/// <item>then <see cref="CarePortalGate.EnsureOpenAsync"/>: 403 "This clinic's portal is unavailable." when the clinic is suspended;</item>
/// <item>then the request acts as the patient's clinic, so every later query runs under that clinic's tenant filter.</item>
/// </list>
/// Portal reads never write <see cref="RecordAccess"/>: a patient reading their own record is not staff access.
/// </summary>
public sealed class PatientPortalAccess(IAppDbContext db, ITenantContext tenant, ICurrentUser user, CarePortalGate gate)
{
    public const string SignInFirst = "Sign in to your patient portal account first.";
    public const string CustomerOnly = "Sign in to your patient portal account to use the patient portal.";
    public const string OnePatientAtATime = "Open one patient at a time.";

    /// <summary>
    /// The signed-in customer account. Business tokens (staff, and support's view-as) are refused: they carry a clinic of their own, and
    /// support's view covers the business, never a family. A staff member who is also a patient uses a personal account: accepting an
    /// invite refuses an account that is a member of the clinic (fix round 1).
    /// </summary>
    public Guid CustomerUserId()
    {
        var userId = user.UserId ?? throw new ForbiddenException(SignInFirst);
        if (user.Role is not null || user.IsSupportView) throw new ForbiddenException(CustomerOnly);
        return userId;
    }

    /// <summary>
    /// True when the caller's access token was issued before the account's email was first confirmed (R1, fix round 2): a session of whoever
    /// set the account up before the mailbox's owner proved it. Compared in whole seconds, the precision of the token's <c>iat</c>.
    /// </summary>
    public async Task<bool> SessionPredatesConfirmationAsync(Guid userId, CancellationToken ct)
    {
        var confirmedAt = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmailConfirmedAt).FirstOrDefaultAsync(ct);
        if (confirmedAt is null) return false;
        return user.TokenIssuedAt is not { } issued || issued.ToUnixTimeSeconds() < confirmedAt.Value.ToUnixTimeSeconds();
    }

    /// <summary>401 "Please sign in again." for a session older than the account's email confirmation.</summary>
    public async Task EnsureFreshSessionAsync(Guid userId, CancellationToken ct)
    {
        if (await SessionPredatesConfirmationAsync(userId, ct)) throw new SignInAgainException();
    }

    /// <summary>Every patient the account is actively linked to, across clinics. Suspended or closed clinics are left out.</summary>
    public async Task<IReadOnlyList<PortalPatientRef>> PatientsAsync(CancellationToken ct)
    {
        var userId = CustomerUserId();
        // A session from before the email was confirmed reaches nobody (R1).
        if (await SessionPredatesConfirmationAsync(userId, ct)) return [];
        var rows = await Linked(userId).Where(x => x.Tenant.Status == TenantStatus.Active).ToListAsync(ct);
        return rows.Select(ToRef).OrderBy(p => p.ClinicName).ThenBy(p => p.FirstName).ThenBy(p => p.LastName).ToList();
    }

    /// <summary>
    /// The patient, when the account has an active link to them; then the request acts as the patient's clinic. 404 when not linked,
    /// then 403 when the clinic's portal is closed. One request serves one clinic: asking for a patient of another clinic in the same
    /// request is refused.
    /// </summary>
    public async Task<PortalPatientRef> RequirePatientAsync(Guid patientId, CancellationToken ct)
    {
        var userId = CustomerUserId();
        if (await SessionPredatesConfirmationAsync(userId, ct)) throw new NotFoundException("Patient"); // R1: as if not linked
        var row = await Linked(userId).Where(x => x.Patient.Id == patientId).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Patient");
        await gate.EnsureOpenAsync(row.Patient.TenantId, ct);

        // A portal token carries no clinic, so the tenant is still free. A route that asked for patients of two clinics would otherwise read
        // the second under the first clinic's filter.
        if (tenant.TenantId is { } current && current != row.Patient.TenantId) throw new ForbiddenException(OnePatientAtATime);
        if (tenant.TenantId != row.Patient.TenantId) tenant.SetTenant(row.Patient.TenantId);
        return ToRef(row);
    }

    private sealed class LinkRow
    {
        public required PatientLink Link { get; init; }
        public required Patient Patient { get; init; }
        public required Tenant Tenant { get; init; }
        public required ClinicProfile Clinic { get; init; }
    }

    /// <summary>
    /// The account's active links, in any clinic. Crosses tenants on purpose, so every join also matches the tenant: a link can only connect
    /// an account to a patient of the link's own clinic, and only CareLink clinics count.
    /// </summary>
    private IQueryable<LinkRow> Linked(Guid userId) =>
        from l in db.PatientLinks.IgnoreQueryFilters()
        where l.UserId == userId && l.Active
        join p in db.Patients.IgnoreQueryFilters() on new { P = l.PatientId, T = l.TenantId } equals new { P = p.Id, T = p.TenantId }
        join t in db.Tenants on p.TenantId equals t.Id
        join c in db.ClinicProfiles.IgnoreQueryFilters() on p.TenantId equals c.TenantId
        where t.Product == Products.Care
        select new LinkRow { Link = l, Patient = p, Tenant = t, Clinic = c };

    private static PortalPatientRef ToRef(LinkRow r) => new(
        r.Patient.Id, r.Patient.TenantId, r.Clinic.Name, r.Clinic.Slug, r.Patient.Number, r.Patient.FirstName, r.Patient.LastName,
        $"{r.Patient.FirstName} {r.Patient.LastName}", r.Link.Relationship, r.Patient.Status);
}

/// <summary>401 "Please sign in again.": the session predates the account's email confirmation (Task 15 fix round 2, R1).</summary>
public sealed class SignInAgainException() : AppException("Please sign in again.", 401, "sign_in_again");

/// <summary>
/// Who hears a patient notice (Task 15 replaces Task 7's placeholder): the portal accounts with an active link to the patient, in the
/// current clinic. Nobody for a deceased patient (ruling, Task 14; <see cref="CareNotices"/> checks too).
/// </summary>
public sealed class PatientLinkRecipients(IAppDbContext db) : ICarePortalRecipients
{
    public async Task<IReadOnlyList<Guid>> ForPatientAsync(Guid patientId, CancellationToken ct) =>
        await db.PatientLinks.AsNoTracking()
            .Where(l => l.PatientId == patientId && l.Active
                && db.Patients.Any(p => p.Id == patientId && p.Status != PatientStatus.Deceased))
            .Select(l => l.UserId).Distinct().ToListAsync(ct);
}
