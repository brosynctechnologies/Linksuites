using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

/// <summary>
/// Business status, read from the database on every request (a primary-key lookup) so a suspension made on any API server
/// applies on every server at once. Deliberately not cached.
/// </summary>
public sealed class TenantStatusCache
{
    public async Task<TenantStatus> GetAsync(IAppDbContext db, Guid tenantId, CancellationToken ct) =>
        await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (TenantStatus?)t.Status).FirstOrDefaultAsync(ct) ?? TenantStatus.Closed;

    /// <summary>Kept for callers; nothing is cached.</summary>
    public void Invalidate(Guid tenantId) { }
}

/// <summary>Writes an admin action to the audit log, with the admin as actor.</summary>
public sealed class PlatformAudit(IAppDbContext db, ICurrentUser user, TimeProvider clock)
{
    public void Log(string action, string entityType, string entityId, Guid? tenantId = null, object? before = null, object? after = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId, ActorId = user.UserId, Action = action, EntityType = entityType, EntityId = entityId, Ip = user.IpAddress,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            CreatedAt = clock.GetUtcNow(),
        });

    public Guid ActorId => user.UserId ?? throw new ForbiddenException();
}

public sealed record ReasonRequest(string Reason);

public static class Reasons
{
    /// <summary>Admin actions that affect customers need a real reason, for the record and for the customer email.</summary>
    public static string Require(string? reason)
    {
        var r = reason?.Trim() ?? "";
        if (r.Length is < 10 or > 300) throw BadRequestException.Field("reason", "Give a reason of 10 to 300 characters.");
        return r;
    }
}
