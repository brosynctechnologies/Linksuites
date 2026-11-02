using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Saas.Application.Common;
using Saas.Api.Infrastructure;
using Saas.Application.Platform;
using Saas.Infrastructure.Auth;

namespace Saas.Api.Endpoints;

/// <summary>The platform admin panel (admin-web): sign-in, businesses, users, view-as, money, plans, audit and admins.</summary>
public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/platform/auth").WithTags("Platform: auth").RequireRateLimiting(RateLimits.Auth);
        auth.MapPost("/login", (PlatformLoginRequest r, PlatformAuthService s, CancellationToken ct) => s.LoginAsync(r, ct));
        auth.MapPost("/verify", (PlatformVerifyRequest r, PlatformAuthService s, CancellationToken ct) => s.VerifyAsync(r, ct));

        app.MapPost("/api/platform/auth/logout", async (ClaimsPrincipal principal, ICurrentUser user, PlatformAuthService s, CancellationToken ct) =>
        {
            if (Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Jti), out var id)) await s.LogoutAsync(id, user.UserId!.Value, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSession).WithTags("Platform: auth");

        // Passwords, for every product and the admin console.
        var password = app.MapGroup("/api/auth/password").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
        password.MapPost("/forgot", async (ForgotPasswordRequest r, PasswordService s, CancellationToken ct) =>
        {
            await s.ForgotAsync(r, ct);
            return Results.NoContent();
        });
        password.MapPost("/reset", async (ResetPasswordRequest r, PasswordService s, CancellationToken ct) =>
        {
            await s.ResetAsync(r, ct);
            return Results.NoContent();
        });
        password.MapPost("/change", async (ChangePasswordRequest r, PasswordService s, CancellationToken ct) =>
        {
            await s.ChangeAsync(r, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // The support view's "End view" button (the only write a support-view token may make).
        app.MapPost("/api/support-view/end", async (ClaimsPrincipal principal, AuthSessions sessions, CancellationToken ct) =>
        {
            if (principal.FindFirstValue(AppClaims.ViewAs) is null || !Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Jti), out var id))
                return Results.Forbid();
            await sessions.EndAsync(id, null, ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Account");

        // Every other platform route needs an admin-panel session (password + code) and an active admin role.
        var platform = app.MapGroup("/api/platform").WithTags("Platform").RequireAuthorization(Policies.PlatformSession, Policies.PlatformAdmin)
            .RequireRateLimiting(RateLimits.Admin);
        platform.MapGet("/health", (HealthService s, CancellationToken ct) => s.GetAsync(ct));
        platform.MapGet("/me", (ClaimsPrincipal principal, PlatformMeService s, CancellationToken ct) => s.GetAsync(principal, ct));
        platform.MapPost("/view-as/{sessionId:guid}/end", async (Guid sessionId, ICurrentUser user, AuthSessions sessions, CancellationToken ct) =>
        {
            return await sessions.EndSupportViewAsync(sessionId, user.UserId, ct) == 0
                ? Results.Problem(statusCode: 404, title: "Not found", detail: "That support view has already ended or doesn't exist.")
                : Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSupport);

        var businesses = platform.MapGroup("/businesses");
        businesses.MapGet("/", (string? q, string? product, Saas.Domain.Core.TenantStatus? status, string? plan, int? page, int? pageSize,
            PlatformBusinessService s, CancellationToken ct) => s.SearchAsync(q, product, status, plan, page ?? 1, pageSize ?? 25, ct))
            .RequireAuthorization(Policies.PlatformSupportOrFinance);
        businesses.MapGet("/{id:guid}", (Guid id, PlatformBusinessService s, CancellationToken ct) => s.DetailAsync(id, ct))
            .RequireAuthorization(Policies.PlatformSupportOrFinance);
        businesses.MapPost("/{id:guid}/suspend", async (Guid id, ReasonRequest r, PlatformBusinessService s, CancellationToken ct) =>
        {
            await s.SuspendAsync(id, r, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSupport);
        businesses.MapPost("/{id:guid}/reactivate", async (Guid id, ReasonRequest r, PlatformBusinessService s, CancellationToken ct) =>
        {
            await s.ReactivateAsync(id, r, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformSupport);

        businesses.MapPost("/{id:guid}/view-as", (Guid id, ReasonRequest r, ViewAsService s, CancellationToken ct) => s.StartAsync(id, r, ct))
            .RequireAuthorization(Policies.PlatformSupport);

        // Any member of a business sees when support viewed it.
        app.MapGet("/api/account/activity", (AccountActivityService s, CancellationToken ct) => s.ListAsync(ct)).RequireAuthorization().WithTags("Account");

        businesses.MapPost("/{id:guid}/comp", async (Guid id, CompRequest r, PlatformPlansService s, CancellationToken ct) =>
        {
            await s.CompAsync(id, r, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.PlatformFinance);

        platform.MapGet("/overview", (PlatformOverviewService s, CancellationToken ct) => s.GetAsync(ct));

        var plans = platform.MapGroup("/plans").RequireAuthorization(Policies.PlatformFinance);
        plans.MapGet("/", (PlatformPlansService s, CancellationToken ct) => s.ListAsync(ct));
        plans.MapPost("/", (SavePlanRequest r, PlatformPlansService s, CancellationToken ct) => s.CreateAsync(r, ct));
        plans.MapPut("/{id:guid}", (Guid id, SavePlanRequest r, PlatformPlansService s, CancellationToken ct) => s.UpdateAsync(id, r, ct));

        platform.MapGet("/payments", (string? product, string? status, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
            PlatformMoneyService s, CancellationToken ct) => s.PaymentsAsync(product, status, from, to, page ?? 1, pageSize ?? 50, ct))
            .RequireAuthorization(Policies.PlatformFinance);
        platform.MapGet("/payments.csv", async (string? product, string? status, DateTimeOffset? from, DateTimeOffset? to, PlatformMoneyService s, CancellationToken ct) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(await s.CsvAsync(product, status, from, to, ct)), "text/csv", "payments.csv"))
            .RequireAuthorization(Policies.PlatformFinance);

        platform.MapGet("/audit", (Guid? tenantId, Guid? actorId, string? entityType, string? action, string? product, DateTimeOffset? from, DateTimeOffset? to,
            int? page, int? pageSize, PlatformAuditService s, CancellationToken ct) =>
            s.QueryAsync(tenantId, actorId, entityType, action, from, to, page ?? 1, pageSize ?? 50, ct, product)).RequireAuthorization(Policies.PlatformSupport);

        var admins = platform.MapGroup("/admins").RequireAuthorization(Policies.PlatformSuper);
        admins.MapGet("/", (PlatformAdminsService s, CancellationToken ct) => s.ListAsync(ct));
        admins.MapPost("/", (AddAdminRequest r, PlatformAdminsService s, CancellationToken ct) => s.AddAsync(r, ct));
        admins.MapPut("/{id:guid}", (Guid id, ChangeRoleRequest r, PlatformAdminsService s, CancellationToken ct) => s.ChangeRoleAsync(id, r, ct));
        admins.MapDelete("/{id:guid}", async (Guid id, PlatformAdminsService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(id, ct);
            return Results.NoContent();
        });

        var users = platform.MapGroup("/users").RequireAuthorization(Policies.PlatformSupport);
        users.MapGet("/", (string? q, PlatformUserService s, CancellationToken ct) => s.SearchAsync(q, ct));
        users.MapGet("/{id:guid}", (Guid id, PlatformUserService s, CancellationToken ct) => s.DetailAsync(id, ct));
        users.MapPost("/{id:guid}/unlock", async (Guid id, PlatformUserService s, CancellationToken ct) =>
        {
            await s.UnlockAsync(id, ct);
            return Results.NoContent();
        });
        users.MapPost("/{id:guid}/send-login-code", async (Guid id, PlatformUserService s, AuthService auth, CancellationToken ct) =>
        {
            await s.SendLoginCodeAsync(id, (email, c) => auth.RequestOtpAsync(new Saas.Application.Auth.OtpRequest(email), c), ct);
            return Results.NoContent();
        });
        users.MapPost("/{id:guid}/send-password-reset", async (Guid id, PlatformUserService s, PasswordService passwords, CancellationToken ct) =>
        {
            await s.SendPasswordResetAsync(id, (email, product, c) => passwords.ForgotAsync(new ForgotPasswordRequest(email, product), c), ct);
            return Results.NoContent();
        });
        users.MapPost("/{id:guid}/sign-out-everywhere", async (Guid id, PlatformUserService s, CancellationToken ct) =>
        {
            await s.SignOutEverywhereAsync(id, ct);
            return Results.NoContent();
        });
    }
}
