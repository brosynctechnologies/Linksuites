using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

/// <summary>Server-side sessions for admin-panel and support-view tokens. A token whose session has ended stops working at once.</summary>
public sealed class AuthSessions(IAppDbContext db, TimeProvider clock)
{
    public AuthSession Start(Guid id, Guid userId, AuthSessionKind kind, DateTimeOffset expiresAt, Guid? tenantId = null)
    {
        var session = new AuthSession { Id = id, UserId = userId, Kind = kind, ExpiresAt = expiresAt, TenantId = tenantId };
        db.AuthSessions.Add(session);
        return session;
    }

    public async Task<bool> IsOpenAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.AuthSessions.AnyAsync(s => s.Id == id && s.EndedAt == null && s.ExpiresAt > now, ct);
    }

    public Task<int> EndAsync(Guid id, Guid? endedBy, CancellationToken ct) =>
        db.AuthSessions.Where(s => s.Id == id && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, clock.GetUtcNow()).SetProperty(x => x.EndedBy, endedBy), ct);

    /// <summary>Ends a support view by id; other kinds of session are left alone. Returns 0 when there was no open view.</summary>
    public Task<int> EndSupportViewAsync(Guid id, Guid? endedBy, CancellationToken ct) =>
        db.AuthSessions.Where(s => s.Id == id && s.Kind == AuthSessionKind.SupportView && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, clock.GetUtcNow()).SetProperty(x => x.EndedBy, endedBy), ct);

    /// <summary>
    /// Ends every open session a user holds: admin-panel sign-ins and the support views they started (sign out everywhere,
    /// password change, admin removal).
    /// </summary>
    public Task<int> EndAllForUserAsync(Guid userId, Guid? endedBy, CancellationToken ct) =>
        db.AuthSessions.Where(s => s.UserId == userId && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, clock.GetUtcNow()).SetProperty(x => x.EndedBy, endedBy), ct);
}

public static class AuthSessionsExtensions
{
    /// <summary>Ends the support views a user started, for when they lose the role that allowed them.</summary>
    public static Task<int> EndSupportViewsForUserAsync(this IAppDbContext db, Guid userId, Guid? endedBy, DateTimeOffset now, CancellationToken ct) =>
        db.AuthSessions.Where(s => s.UserId == userId && s.Kind == AuthSessionKind.SupportView && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now).SetProperty(x => x.EndedBy, endedBy), ct);
}
