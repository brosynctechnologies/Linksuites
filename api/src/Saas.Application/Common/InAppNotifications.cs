using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.Application.Common;

public sealed record UserNotificationDto(Guid Id, string Product, string Kind, string Title, string Body, string Link,
    Guid? TenantId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record UnreadCountDto(int Count);

/// <summary>
/// The in-app notification feed (the bell). Products call <see cref="Add"/> next to the email they queue; the row is saved with the
/// caller's SaveChanges. Signed-in users read and mark their own notifications through the /api/me/notifications endpoints.
/// </summary>
public sealed class InAppNotifications(IAppDbContext db, ITenantContext tenant, ICurrentUser user, TimeProvider clock)
{
    public const int PageSize = 20;
    /// <summary>The API stops counting here; the UI shows 9+.</summary>
    public const int UnreadCap = 99;
    public static readonly TimeSpan ReadRetention = TimeSpan.FromDays(90);
    public static readonly TimeSpan UnreadRetention = TimeSpan.FromDays(180);

    /// <summary>Adds one notification for one user. Saved by the caller's SaveChangesAsync, in the same transaction as the event.</summary>
    public void Add(Guid userId, string product, string kind, string title, string body, string link, Guid? tenantId = null) =>
        db.UserNotifications.Add(new UserNotification
        {
            UserId = userId, TenantId = tenantId, Product = product, Kind = kind, Title = title, Body = body, Link = link,
        });

    /// <summary>
    /// The signed-in user's notifications. A business token (support view included, which carries the owner's user id)
    /// sees only that business's rows; a customer (tenantless) token sees all of its own.
    /// </summary>
    private IQueryable<UserNotification> Mine()
    {
        var userId = user.UserId ?? throw new ForbiddenException("Sign in to see your notifications.");
        var query = db.UserNotifications.Where(n => n.UserId == userId);
        if (tenant.TenantId is { } tenantId) query = query.Where(n => n.TenantId == tenantId);
        return query;
    }

    public async Task<Paged<UserNotificationDto>> ListAsync(bool unreadOnly, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var query = Mine();
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);
        var total = await query.CountAsync(ct);
        var items = await query.AsNoTracking().OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(n => new UserNotificationDto(n.Id, n.Product, n.Kind, n.Title, n.Body, n.Link, n.TenantId, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);
        return new Paged<UserNotificationDto>(items, page, PageSize, total);
    }

    public async Task<UnreadCountDto> UnreadCountAsync(CancellationToken ct) =>
        new(await Mine().Where(n => n.ReadAt == null).Take(UnreadCap).CountAsync(ct));

    /// <summary>Marks one of the user's own notifications read. Someone else's looks like it does not exist.</summary>
    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var row = await Mine().FirstOrDefaultAsync(n => n.Id == id, ct) ?? throw new NotFoundException("Notification");
        if (row.ReadAt is not null) return;
        row.ReadAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await Mine().Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    /// <summary>Deletes read notifications older than 90 days and unread ones older than 180 days. Called by the daily job.</summary>
    public Task<int> CleanupAsync(DateTimeOffset now, CancellationToken ct)
    {
        var readBefore = now - ReadRetention;
        var unreadBefore = now - UnreadRetention;
        return db.UserNotifications
            .Where(n => n.ReadAt != null ? n.CreatedAt < readBefore : n.CreatedAt < unreadBefore)
            .ExecuteDeleteAsync(ct);
    }
}
