using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.School;

/// <summary>
/// Queues SchoolLink in-app notifications for the current school. Rows are written by the caller's <c>SaveChangesAsync</c>.
/// </summary>
public sealed class SchoolFeed(IAppDbContext db, ITenantContext tenant, InAppNotifications feed)
{
    /// <summary>A parent portal page for one child: the portal selects that child when the parent opens the link (<c>?child=</c>).</summary>
    public static string ParentLink(string page, Guid studentId) => $"/parent/{page}?child={studentId}";

    public void Notify(Guid userId, string kind, string title, string body, string link) =>
        feed.Add(userId, Products.School, kind, title, body, link, tenant.TenantId);

    public void Notify(IEnumerable<Guid> userIds, string kind, string title, string body, string link)
    {
        foreach (var userId in userIds.Distinct())
            Notify(userId, kind, title, body, link);
    }

    /// <summary>The accounts of the guardians linked to a student, optionally only the primary guardian.</summary>
    public async Task<List<Guid>> GuardianUsersAsync(Guid studentId, CancellationToken ct, bool primaryOnly = false) =>
        await db.StudentGuardians.Where(l => l.StudentId == studentId && (!primaryOnly || l.IsPrimary))
            .Join(db.Guardians.Where(g => g.UserId != null), l => l.GuardianId, g => g.Id, (l, g) => g.UserId!.Value)
            .Distinct().ToListAsync(ct);

    /// <summary>The active members of the school holding any of <paramref name="roles"/>.</summary>
    public async Task<List<Guid>> MembersAsync(CancellationToken ct, params MemberRole[] roles) =>
        await db.Memberships.Where(m => m.Status == MembershipStatus.Active && roles.Contains(m.Role))
            .Select(m => m.UserId).Distinct().ToListAsync(ct);
}
