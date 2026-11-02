using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>What a student's own account is made of. The email is synthetic: it is never delivered to, and never used to sign in.</summary>
public static class StudentLogins
{
    public const string EmailDomain = "@students.schoollink.invalid";
    public const int MinPasswordLength = 8;

    /// <summary>True for a student's synthetic email. Such accounts get no email, no email code and no email sign-in.</summary>
    public static bool IsStudentEmail(string? email) => email is not null && email.EndsWith(EmailDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>{admissionNo}.{slug}@students.schoollink.invalid</c>, lower case. Anything in the admission number other than a letter, digit or
    /// hyphen is written as <c>+</c> and its hex code, so two different admission numbers never share an address.
    /// </summary>
    public static string EmailFor(string admissionNo, string slug)
    {
        var local = new System.Text.StringBuilder();
        foreach (var c in admissionNo.Trim().ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-') local.Append(c);
            else local.Append('+').Append(((int)c).ToString("x"));
        }
        return $"{local}.{slug.Trim().ToLowerInvariant()}{EmailDomain}";
    }
}

public sealed record StudentSelf(Guid StudentId, Guid TenantId, string Name, string AdmissionNo, string ClassName, string SchoolName);
public sealed record SchoolPublicDto(string Name, string? LogoUrl, string? Motto);

/// <summary>
/// The one gate of the student portal, as <see cref="ParentAccess"/> is for parents. A student is a customer account (no school in the
/// token) linked to one student record. A student reaches only themselves, and only while they are active in an active school.
/// </summary>
public sealed class StudentAccess(IAppDbContext db, ITenantContext tenant, ICurrentUser user)
{
    public const string NotActive = "Your account isn't active. Please see the school office.";

    /// <summary>The signed-in student; then the request acts as their school. 403 for anyone else, for leavers and for a suspended school.</summary>
    public async Task<StudentSelf> RequireSelfAsync(CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenException("Sign in with your student account first.");
        if (user.Role is not null || user.IsSupportView) throw new ForbiddenException("Sign in with your student account to use the student portal.");

        var link = await (from s in db.Students.IgnoreQueryFilters()
                          where s.UserId == userId
                          join t in db.Tenants on s.TenantId equals t.Id
                          where t.Product == Products.School
                          select new { s.Id, s.TenantId, s.Status, TenantStatus = t.Status })
            .FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("This account is not a student account.");
        if (link.TenantStatus != TenantStatus.Active) throw new ForbiddenException(ParentAccess.PortalUnavailable);
        if (link.Status != StudentStatus.Active) throw new ForbiddenException(NotActive);

        if (tenant.TenantId != link.TenantId) tenant.SetTenant(link.TenantId);

        // From here on the tenant filter applies.
        var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == link.Id, ct);
        var profile = await db.SchoolProfiles.AsNoTracking().FirstAsync(ct);
        var className = await db.Enrolments.Where(e => e.StudentId == student.Id && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Join(db.SchoolClasses, e => e.ClassId, c => c.Id, (e, c) => c.Name).FirstOrDefaultAsync(ct);
        return new StudentSelf(student.Id, student.TenantId, ParentAccess.FullName(student.FirstName, student.MiddleName, student.LastName),
            student.AdmissionNo, className ?? "", profile.Name);
    }
}

/// <summary>What the school's sign-in page shows before anyone signs in.</summary>
public sealed class SchoolPublicService(IAppDbContext db, SchoolPhotoSigner signer, IFileStorage storage)
{
    public async Task<SchoolPublicDto> GetAsync(string slug, CancellationToken ct)
    {
        var key = (slug ?? "").Trim().ToLowerInvariant();
        var row = await (from p in db.SchoolProfiles.IgnoreQueryFilters()
                         where p.Slug == key
                         join t in db.Tenants on p.TenantId equals t.Id
                         where t.Product == Products.School && t.Status == TenantStatus.Active
                         select new { p.Name, p.Motto, p.LogoKey, p.TenantId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("School");
        // The logo is private storage; the page gets a short-lived signed link.
        return new SchoolPublicDto(row.Name, row.LogoKey is null ? null : signer.LogoUrl(row.TenantId), row.Motto);
    }

    /// <summary>The logo behind a valid signed link, or null. The signature is the only gate.</summary>
    public async Task<StudentPhotoFile?> LogoByLinkAsync(Guid tenantId, long expires, string signature, CancellationToken ct)
    {
        if (!signer.IsLogoValid(tenantId, expires, signature)) throw new ForbiddenException("This logo link has expired.");
        var key = await db.SchoolProfiles.IgnoreQueryFilters().AsNoTracking().Where(p => p.TenantId == tenantId).Select(p => p.LogoKey).FirstOrDefaultAsync(ct);
        if (key is null) return null;
        if (storage.TemporaryUrl(key, SchoolPhotoSigner.Lifetime) is { } url) return new StudentPhotoFile(null, url);
        var stream = await storage.OpenReadAsync(key, ct);
        return stream is null ? null : new StudentPhotoFile(stream, null);
    }
}
