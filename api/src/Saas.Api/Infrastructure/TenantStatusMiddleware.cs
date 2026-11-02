using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Saas.Application.Common;
using Saas.Application.Platform;
using Saas.Domain.Core;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Infrastructure;

/// <summary>
/// Refuses requests made with a business token once that business is suspended, without waiting for the token to expire.
/// The status is read from the database on every request, so a suspension applies on every server at once.
/// </summary>
public sealed class TenantStatusMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAppDbContext db, TenantStatusCache statuses, IProblemDetailsService problems)
    {
        // Support may look (read only) at a suspended business; that is how they investigate it.
        if (context.User.Identity?.IsAuthenticated == true && context.User.FindFirstValue(AppClaims.ViewAs) is null && Guid.TryParse(context.User.FindFirstValue(AppClaims.Tenant), out var tenantId)
            && await statuses.GetAsync(db, tenantId, context.RequestAborted) != TenantStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = "This business is suspended. Contact support.", Extensions = { ["code"] = "suspended" } },
            });
            return;
        }
        await next(context);
    }
}
