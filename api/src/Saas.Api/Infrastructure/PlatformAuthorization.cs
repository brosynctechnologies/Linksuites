using Microsoft.AspNetCore.Authorization;
using Saas.Application.Common;
using Saas.Application.Platform;
using Saas.Domain.Core;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Infrastructure;

/// <summary>Needs an active platform admin with one of <see cref="Roles"/> (any role when empty). SuperAdmin always passes.</summary>
public sealed class PlatformRoleRequirement(params PlatformRole[] roles) : IAuthorizationRequirement
{
    public IReadOnlyCollection<PlatformRole> Roles { get; } = roles;
}

/// <summary>
/// Checks the admin's role in the database on every request, so removing an admin or changing their role takes effect
/// immediately rather than when their token expires. The <c>padm</c> claim only tells the web apps what to show.
/// </summary>
public sealed class PlatformRoleHandler(PlatformAccess access, ICurrentUser user) : AuthorizationHandler<PlatformRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformRoleRequirement requirement)
    {
        if (user.UserId is not { } userId || context.User.FindFirst(AppClaims.PlatformAdmin) is null) return;
        if (await access.ActiveRoleAsync(userId, CancellationToken.None) is { } role && PlatformAccess.Allows(role, requirement.Roles))
            context.Succeed(requirement);
    }
}
