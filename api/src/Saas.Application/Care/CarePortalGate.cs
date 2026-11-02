using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Care;

/// <summary>
/// The patient portal's suspension check (Task 14). Staff tokens are refused for a suspended clinic by the shared tenant-status middleware;
/// portal tokens carry no clinic, so every portal route asks this gate once it knows the patient's clinic (Task 15's
/// <c>PatientPortalAccess.RequirePatientAsync</c> calls it after the link check, as SchoolLink's ParentAccess does). A clinic that isn't an
/// active CareLink business (suspended, closed, unknown, or another product's) gives 403 "This clinic's portal is unavailable."
/// </summary>
public sealed class CarePortalGate(IAppDbContext db)
{
    public const string PortalUnavailable = "This clinic's portal is unavailable.";

    public async Task EnsureOpenAsync(Guid tenantId, CancellationToken ct)
    {
        var open = await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Id == tenantId && t.Product == Products.Care && t.Status == TenantStatus.Active, ct);
        if (!open) throw new ForbiddenException(PortalUnavailable);
    }
}
