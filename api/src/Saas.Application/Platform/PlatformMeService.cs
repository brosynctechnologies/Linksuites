using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Platform;

public sealed record PlatformMeDto(Guid Id, string FullName, string Email, PlatformRole PlatformRole, DateTimeOffset? SessionExpiresAt);

/// <summary>The signed-in admin as the console should show them now: current role (it may have changed) and session expiry.</summary>
public sealed class PlatformMeService(IAppDbContext db, ICurrentUser user, PlatformAccess access)
{
    public async Task<PlatformMeDto> GetAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = user.UserId ?? throw new ForbiddenException();
        var role = await access.ActiveRoleAsync(userId, ct) ?? throw new ForbiddenException();
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        DateTimeOffset? expires = long.TryParse(principal.FindFirst("exp")?.Value, out var exp) ? DateTimeOffset.FromUnixTimeSeconds(exp) : null;
        return new PlatformMeDto(u.Id, u.FullName, u.Email!, role, expires);
    }
}
