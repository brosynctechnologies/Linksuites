using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Saas.Application.Platform;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Infrastructure;

/// <summary>
/// Admin-panel and support-view tokens are backed by a server-side session. Once that session ends (sign-out, "end view",
/// sign out everywhere, password reset, admin removal), the token gets 401 on its next request even though it hasn't expired.
/// </summary>
public sealed class SessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuthSessions sessions, IProblemDetailsService problems)
    {
        var user = context.User;
        var tracked = user.FindFirstValue(AppClaims.Amr) == "otp" || user.FindFirstValue(AppClaims.ViewAs) is not null;
        if (user.Identity?.IsAuthenticated == true && tracked
            && !(Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Jti), out var id) && await sessions.IsOpenAsync(id, context.RequestAborted)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = "Your session has ended. Sign in again.", Extensions = { ["code"] = "session_ended" } },
            });
            return;
        }
        await next(context);
    }
}
