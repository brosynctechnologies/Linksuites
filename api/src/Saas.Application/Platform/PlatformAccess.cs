using Microsoft.EntityFrameworkCore;
using Saas.Application.Auth;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

/// <summary>Who is a platform admin, and with which role. Checked on every admin request, so removals take effect at once.</summary>
public sealed class PlatformAccess(IAppDbContext db, PlatformOptions options)
{
    /// <summary>The user's active admin role, or null. Never creates anything.</summary>
    public async Task<PlatformRole?> ActiveRoleAsync(Guid userId, CancellationToken ct)
    {
        var row = await db.PlatformAdmins.AsNoTracking().Where(a => a.UserId == userId).Select(a => new { a.Role, a.RemovedAt }).FirstOrDefaultAsync(ct);
        return row is { RemovedAt: null } ? row.Role : null;
    }

    /// <summary>
    /// An email in <c>Platform:AdminEmails</c> that has never had an admin row may become the first SuperAdmin, but only once it
    /// proves the mailbox with an admin-panel sign-in code (so registering that address first gains nothing).
    /// </summary>
    public async Task<bool> CanBootstrapAsync(Guid userId, string? email, CancellationToken ct) =>
        options.IsAdmin(email) && !await db.PlatformAdmins.AnyAsync(a => a.UserId == userId, ct);

    /// <summary>Creates the SuperAdmin row for a bootstrap email. Call only after the emailed code has been checked.</summary>
    public async Task<PlatformRole> BootstrapAsync(Guid userId, CancellationToken ct)
    {
        db.PlatformAdmins.Add(new PlatformAdmin { UserId = userId, Role = PlatformRole.SuperAdmin });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request may have bootstrapped the same admin at the same moment; anything else is a real failure.
            db.ChangeTracker.Clear();
            if (!await db.PlatformAdmins.AsNoTracking().AnyAsync(a => a.UserId == userId, ct)) throw;
        }
        return await ActiveRoleAsync(userId, ct) ?? throw new ForbiddenException();
    }

    /// <summary>Whether a role may use something that needs one of <paramref name="allowed"/>. SuperAdmin may use everything.</summary>
    public static bool Allows(PlatformRole role, IReadOnlyCollection<PlatformRole> allowed) =>
        role == PlatformRole.SuperAdmin || allowed.Count == 0 || allowed.Contains(role);
}

/// <summary>
/// What the signed-in admin may see of customers' personal data. Emails and phone numbers are for Support and Super admin only;
/// other roles see names and roles, and masked addresses where an address is needed to recognise a row.
/// </summary>
public sealed class PlatformViewer(ICurrentUser user, PlatformAccess access)
{
    private PlatformRole? role;
    private bool loaded;

    public async Task<bool> SeesContactDetailsAsync(CancellationToken ct)
    {
        if (!loaded)
        {
            role = user.UserId is { } id ? await access.ActiveRoleAsync(id, ct) : null;
            loaded = true;
        }
        return role is PlatformRole.SuperAdmin or PlatformRole.Support;
    }

    /// <summary><c>ada@example.com</c> becomes <c>a…@example.com</c>; anything else keeps only its last two characters.</summary>
    public static string Mask(string value)
    {
        var at = value.IndexOf('@');
        if (at > 0) return string.Concat(value.AsSpan(0, 1), "…", value.AsSpan(at));
        return value.Length <= 2 ? "…" : string.Concat("…", value.AsSpan(value.Length - 2));
    }
}
