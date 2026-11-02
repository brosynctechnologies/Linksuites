using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
    public MemberRole? Role => Enum.TryParse<MemberRole>(Principal?.FindFirstValue(AppClaims.Role), out var r) ? r : null;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public bool IsSupportView => Principal?.FindFirstValue(AppClaims.ViewAs) is not null;
    public DateTimeOffset? TokenIssuedAt =>
        long.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Iat), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}

/// <summary>Scopes signed-in requests to the tenant in their token.</summary>
public sealed class TenantMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(AppClaims.Tenant), out var tenantId))
            tenant.SetTenant(tenantId);
        return next(context);
    }
}

/// <summary>Turns business and validation exceptions into ProblemDetails; everything else is a logged 500.</summary>
public sealed class AppExceptionHandler(IProblemDetailsService problems, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case AppException app:
                problem = new ProblemDetails { Status = app.StatusCode, Title = Title(app.StatusCode), Detail = app.Message };
                if (app.Code is not null) problem.Extensions["code"] = app.Code;
                if (app is BadRequestException { Errors: { } errors }) problem.Extensions["errors"] = errors;
                if (app is ImportErrorsException import) problem.Extensions["rows"] = import.Rows;
                if (app.Extensions is { } extra)
                    foreach (var (key, value) in extra) problem.Extensions[key] = value;
                break;
            case ValidationException v:
                problem = new ProblemDetails
                {
                    Status = 400,
                    Title = "Please check the form.",
                    Detail = v.Errors.FirstOrDefault()?.ErrorMessage,
                    Extensions = { ["errors"] = ToErrors(v), ["code"] = "validation" },
                };
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = "The request body could not be read." };
                break;
            default:
                logger.LogError(exception, "Unhandled error on {Method} {Path}", http.Request.Method, http.Request.Path);
                problem = new ProblemDetails { Status = 500, Title = "Something went wrong", Detail = "Please try again." };
                break;
        }
        http.Response.StatusCode = problem.Status ?? 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }

    private static Dictionary<string, string[]> ToErrors(ValidationException v) =>
        v.Errors.GroupBy(e => ToCamel(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

    private static string ToCamel(string name) =>
        string.Join('.', name.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));

    private static string Title(int status) => status switch
    {
        400 => "Please check the form.",
        401 => "Unauthorized",
        402 => "Upgrade required",
        403 => "Forbidden",
        404 => "Not found",
        409 => "Conflict",
        503 => "Unavailable",
        _ => "Error",
    };
}

/// <summary>Runs the FluentValidation validator for the request body type before the handler.</summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var validator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        var body = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (validator is not null && body is not null)
            await validator.ValidateAndThrowAsync(body, ctx.HttpContext.RequestAborted);
        return await next(ctx);
    }
}

/// <summary>Lets every member read (GET) but only the given roles change anything.</summary>
public sealed class WriteRolesFilter(MemberRole[] roles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)) return await next(ctx);
        var role = http.RequestServices.GetRequiredService<ICurrentUser>().Role;
        if (role is { } r && roles.Contains(r)) return await next(ctx);
        throw new ForbiddenException("Your role can view this but not change it.");
    }
}

public static class EndpointExtensions
{
    public static RouteGroupBuilder WritableBy(this RouteGroupBuilder group, params MemberRole[] roles)
    {
        group.AddEndpointFilter(new WriteRolesFilter(roles));
        return group;
    }

    public static RouteHandlerBuilder Validate<T>(this RouteHandlerBuilder builder) where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>();

    public static Upload ToUpload(this IFormFile file) =>
        new(file.OpenReadStream(), file.FileName, file.ContentType, file.Length);
}
