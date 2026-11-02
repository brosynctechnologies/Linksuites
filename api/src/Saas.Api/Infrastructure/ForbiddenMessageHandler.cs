using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Saas.Domain.Core;

namespace Saas.Api.Infrastructure;

/// <summary>A 403 from a role check says which role is needed ("You need the Finance role for this."), for admins and business members.</summary>
public sealed class ForbiddenMessageHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Forbidden && result.AuthorizationFailure is { } failure && NeededRole(failure) is { } needed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            var problem = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = $"You need {needed} for this." };
            problem.Extensions["code"] = "forbidden";
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
            return;
        }
        await _fallback.HandleAsync(next, context, policy, result);
    }

    private static string? NeededRole(AuthorizationFailure failure)
    {
        foreach (var requirement in failure.FailedRequirements)
        {
            switch (requirement)
            {
                case PlatformRoleRequirement { Roles.Count: 0 }:
                    return "a platform admin role";
                case PlatformRoleRequirement p:
                    return $"the {string.Join(" or ", p.Roles.Select(Name))} role";
                case RolesAuthorizationRequirement r:
                    return $"the {string.Join(" or ", r.AllowedRoles)} role";
            }
        }
        return null;
    }

    private static string Name(PlatformRole role) => role == PlatformRole.SuperAdmin ? "Super admin" : role.ToString();
}
