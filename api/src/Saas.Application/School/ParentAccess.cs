using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.Application.School;

/// <summary>A child as the parent portal lists it. <c>ClassName</c> is empty when the child has no class in the current session.</summary>
public sealed record ChildRef(Guid StudentId, Guid TenantId, string SchoolName, string SchoolSlug, string Name, string ClassName);

/// <summary>
/// The one gate of the parent portal. A parent is a customer account (no business in the token) linked to guardian records, in any
/// number of schools. Every parent route asks this class for the child first: it answers 404 for any child the account isn't linked to
/// (so ids of other families' children reveal nothing), 403 when the child's school is suspended, and then scopes the request to that
/// school so every later query in the request runs under the school's tenant filter.
/// </summary>
public sealed class ParentAccess(IAppDbContext db, ITenantContext tenant, ICurrentUser user)
{
    public const string PortalUnavailable = "This school's portal is unavailable.";

    /// <summary>
    /// The signed-in customer account. Business tokens (staff, and support's view-as) are refused: they carry a school of their own, and
    /// support's view covers the business, never a family. A teacher who is also a parent signs in to their account as a parent.
    /// </summary>
    public Guid CustomerUserId()
    {
        var userId = user.UserId ?? throw new ForbiddenException("Sign in to your parent account first.");
        if (user.Role is not null || user.IsSupportView)
            throw new ForbiddenException("Sign in to your parent account to use the parent portal.");
        return userId;
    }

    /// <summary>Children of every guardian record linked to the account, across schools. Leavers and suspended or closed schools are left out.</summary>
    public async Task<IReadOnlyList<ChildRef>> ChildrenAsync(CancellationToken ct)
    {
        var userId = CustomerUserId();
        var rows = await Linked(userId)
            .Where(x => x.Student.Status == StudentStatus.Active && x.Tenant.Status == TenantStatus.Active)
            .Select(x => new
            {
                x.Student.Id, x.Student.TenantId, x.Profile.Name, x.Profile.Slug, x.Student.FirstName, x.Student.MiddleName, x.Student.LastName,
                ClassName = db.Enrolments.IgnoreQueryFilters()
                    .Where(e => e.StudentId == x.Student.Id && e.TenantId == x.Student.TenantId
                        && db.AcademicSessions.IgnoreQueryFilters().Any(s => s.Id == e.SessionId && s.TenantId == e.TenantId && s.IsCurrent))
                    .Join(db.SchoolClasses.IgnoreQueryFilters(), e => e.ClassId, c => c.Id, (e, c) => c.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows
            .DistinctBy(r => r.Id) // two guardian records of one account can share a child
            .Select(r => new ChildRef(r.Id, r.TenantId, r.Name, r.Slug, FullName(r.FirstName, r.MiddleName, r.LastName), r.ClassName ?? ""))
            .OrderBy(c => c.SchoolName).ThenBy(c => c.Name)
            .ToList();
    }

    /// <summary>
    /// The child, when the account is linked to them; then the request acts as the child's school. 404 when not linked (or the child
    /// has left the school), 403 when the school is suspended. A request serves one child's school: asking for a child of another
    /// school in the same request is refused.
    /// </summary>
    public async Task<ChildRef> RequireChildAsync(Guid studentId, CancellationToken ct)
    {
        var userId = CustomerUserId();
        var link = await Linked(userId).Where(x => x.Student.Id == studentId)
            .Select(x => new { x.Student.TenantId, x.Student.Status, TenantStatus = x.Tenant.Status })
            .FirstOrDefaultAsync(ct);
        if (link is null || link.Status != StudentStatus.Active) throw new NotFoundException("Child");
        if (link.TenantStatus != TenantStatus.Active) throw new ForbiddenException(PortalUnavailable);

        // A parent token carries no tenant, so the tenant is still free. One request serves one school: a route that asks for children of
        // two schools would otherwise read the second under the first school's filter.
        if (tenant.TenantId is { } current && current != link.TenantId) throw new ForbiddenException("Open one child at a time.");
        if (tenant.TenantId != link.TenantId) tenant.SetTenant(link.TenantId); // UseTenantOf

        // From here on the tenant filter applies: the reads below only see this school.
        var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == studentId, ct);
        var profile = await db.SchoolProfiles.AsNoTracking().FirstAsync(ct);
        var className = await db.Enrolments.Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Join(db.SchoolClasses, e => e.ClassId, c => c.Id, (e, c) => c.Name).FirstOrDefaultAsync(ct);
        return new ChildRef(student.Id, student.TenantId, profile.Name, profile.Slug, FullName(student.FirstName, student.MiddleName, student.LastName), className ?? "");
    }

    private sealed class LinkRow
    {
        public required Student Student { get; init; }
        public required Tenant Tenant { get; init; }
        public required SchoolProfile Profile { get; init; }
    }

    /// <summary>
    /// Students linked to the account through any guardian record, in any school. Crosses tenants on purpose, so every join also
    /// matches the tenant: a link row can only connect a guardian and a student of the same school.
    /// </summary>
    private IQueryable<LinkRow> Linked(Guid userId) =>
        from g in db.Guardians.IgnoreQueryFilters()
        where g.UserId == userId
        join l in db.StudentGuardians.IgnoreQueryFilters() on new { G = g.Id, T = g.TenantId } equals new { G = l.GuardianId, T = l.TenantId }
        join s in db.Students.IgnoreQueryFilters() on new { S = l.StudentId, T = l.TenantId } equals new { S = s.Id, T = s.TenantId }
        join t in db.Tenants on s.TenantId equals t.Id
        join p in db.SchoolProfiles.IgnoreQueryFilters() on s.TenantId equals p.TenantId
        where t.Product == Products.School
        select new LinkRow { Student = s, Tenant = t, Profile = p };

    internal static string FullName(string first, string? middle, string last) =>
        string.Join(' ', new[] { first, middle, last }.Where(n => !string.IsNullOrWhiteSpace(n)));
}
