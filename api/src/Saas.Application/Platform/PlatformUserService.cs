using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

public sealed record UserRowDto(Guid Id, string Name, string Email, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, int Businesses);
public sealed record UserMembershipDto(Guid TenantId, string Name, string Product, MemberRole Role, MembershipStatus Status, TenantStatus BusinessStatus);
public sealed record UserActivityDto(int Courses, int Stays, int Enquiries, int Viewings, int SavedSearches);
public sealed record UserDetailDto(Guid Id, string Name, string Email, string? Phone, bool EmailConfirmed, DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt, bool LockedOut, DateTimeOffset? LockoutEnd, PlatformRole? AdminRole, IReadOnlyList<UserMembershipDto> Memberships,
    UserActivityDto Activity);

/// <summary>Any account in any product: lookup, unlock, a sign-in code, and signing out everywhere. Admins never see or set passwords.</summary>
public sealed class PlatformUserService(IAppDbContext db, PlatformAudit audit, AuthSessions sessions, TimeProvider clock)
{
    public async Task<IReadOnlyList<UserRowDto>> SearchAsync(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        var term = q.Trim().ToLowerInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.Email!.ToLower().Contains(term) || u.FullName.ToLower().Contains(term) || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)))
            .OrderByDescending(u => u.CreatedAt).Take(50)
            .Select(u => new UserRowDto(u.Id, u.FullName, u.Email!, u.CreatedAt, u.LastLoginAt,
                db.Memberships.IgnoreQueryFilters().Count(m => m.UserId == u.Id)))
            .ToListAsync(ct);
    }

    public async Task<UserDetailDto> DetailAsync(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User");
        var memberships = await db.Memberships.IgnoreQueryFilters().AsNoTracking().Where(m => m.UserId == id).OrderBy(m => m.CreatedAt)
            .Select(m => new UserMembershipDto(m.TenantId, m.Tenant!.Name, m.Tenant.Product, m.Role, m.Status, m.Tenant.Status)).ToListAsync(ct);
        var activity = new UserActivityDto(
            await db.Enrollments.IgnoreQueryFilters().CountAsync(e => e.UserId == id, ct),
            await db.Stays.IgnoreQueryFilters().CountAsync(s => s.GuestUserId == id, ct),
            await db.Enquiries.IgnoreQueryFilters().CountAsync(e => e.UserId == id, ct),
            await db.Inspections.IgnoreQueryFilters().CountAsync(i => i.SeekerUserId == id, ct),
            await db.SavedSearches.CountAsync(s => s.UserId == id, ct));
        var lockedOut = u.LockoutEnd is { } end && end > clock.GetUtcNow();
        var role = await db.PlatformAdmins.Where(a => a.UserId == id && a.RemovedAt == null).Select(a => (PlatformRole?)a.Role).FirstOrDefaultAsync(ct);
        return new UserDetailDto(u.Id, u.FullName, u.Email!, u.PhoneNumber, u.EmailConfirmed, u.CreatedAt, u.LastLoginAt, lockedOut, u.LockoutEnd, role,
            memberships, activity);
    }

    public async Task UnlockAsync(Guid id, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User");
        u.LockoutEnd = null;
        u.AccessFailedCount = 0;
        audit.Log("user.unlock", "User", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Support sends a one-time sign-in code, and the user signs in with it (an alternative to a password reset).</summary>
    public async Task SendLoginCodeAsync(Guid id, Func<string, CancellationToken, Task> requestOtp, CancellationToken ct)
    {
        var email = await db.Users.Where(x => x.Id == id).Select(x => x.Email).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("User");
        await requestOtp(email, ct);
        audit.Log("user.send_login_code", "User", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sends the user a password reset link, audited: to the admin console for an active platform admin, otherwise to the product
    /// they use (their first business). Says so when the hourly limit stopped the email.
    /// </summary>
    public async Task SendPasswordResetAsync(Guid id, Func<string, string?, CancellationToken, Task<bool>> sendReset, CancellationToken ct)
    {
        var email = await db.Users.Where(x => x.Id == id).Select(x => x.Email).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("User");
        var product = await db.PlatformAdmins.AnyAsync(a => a.UserId == id && a.RemovedAt == null, ct)
            ? "admin"
            : await db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == id).OrderBy(m => m.CreatedAt).Select(m => m.Tenant!.Product).FirstOrDefaultAsync(ct);
        if (!await sendReset(email, product, ct))
            throw new ConflictException($"This user has had 3 reset emails in the last hour. Try again later, or send a sign-in code.");
        audit.Log("user.send_password_reset", "User", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task SignOutEverywhereAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("User");
        await db.RefreshTokens.Where(x => x.UserId == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow()), ct);
        await sessions.EndAllForUserAsync(id, audit.ActorId, ct);
        audit.Log("user.sign_out_everywhere", "User", id.ToString());
        await db.SaveChangesAsync(ct);
    }
}
