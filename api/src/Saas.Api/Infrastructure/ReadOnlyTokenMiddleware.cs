using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Infrastructure;

/// <summary>
/// Support's view-as tokens are read only: any request that could change something (anything but GET, HEAD and OPTIONS) is
/// refused here, before routing reaches a handler, so new write endpoints are covered without opting in.
/// </summary>
public sealed class ReadOnlyTokenMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problems)
    {
        if (context.User.FindFirstValue(AppClaims.ReadOnly) == "1" && !context.Request.Path.Equals("/api/support-view/end")
            && !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = "Support view is read only.", Extensions = { ["code"] = "read_only" } },
            });
            return;
        }
        // A support view shows the business, not the owner's own life as a learner, home-seeker or parent.
        if (context.User.FindFirstValue(AppClaims.ViewAs) is not null
            && (context.Request.Path.StartsWithSegments("/api/learn") || context.Request.Path.StartsWithSegments("/api/parent")
                // The bell is part of the business's screens, so a support view may read it (writes are already refused above).
                || context.Request.Path.StartsWithSegments("/api/me", out var rest) && rest.HasValue && rest.Value != "/"
                    && !context.Request.Path.StartsWithSegments("/api/me/notifications")))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = "Support view only covers the business.", Extensions = { ["code"] = "read_only" } },
            });
            return;
        }
        await next(context);
    }
}
