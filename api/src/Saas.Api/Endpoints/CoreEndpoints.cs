using Saas.Api.Infrastructure;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Invoices;
using Saas.Application.Team;
using Saas.Infrastructure.Auth;
using Saas.Infrastructure.Payments;

namespace Saas.Api.Endpoints;

/// <summary>Shared core used by every product: auth, account, billing, payment webhooks.</summary>
public static class CoreEndpoints
{
    public static void MapCoreEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);
        auth.MapPost("/signup", (SignupRequest r, AuthService s, CancellationToken ct) => s.SignupAsync(r, ct)).Validate<SignupRequest>();
        auth.MapPost("/login", (LoginRequest r, AuthService s, CancellationToken ct) => s.LoginAsync(r, ct)).Validate<LoginRequest>();
        auth.MapPost("/refresh", (RefreshRequest r, AuthService s, CancellationToken ct) => s.RefreshAsync(r, ct));
        auth.MapPost("/logout", async (RefreshRequest r, AuthService s, CancellationToken ct) =>
        {
            await s.LogoutAsync(r, ct);
            return Results.NoContent();
        });
        auth.MapPost("/otp/request", async (OtpRequest r, AuthService s, CancellationToken ct) =>
        {
            await s.RequestOtpAsync(r, ct);
            return Results.NoContent();
        }).Validate<OtpRequest>().RequireRateLimiting(RateLimits.Otp);
        auth.MapPost("/otp/verify", (OtpVerifyRequest r, AuthService s, CancellationToken ct) => s.VerifyOtpAsync(r, ct))
            .Validate<OtpVerifyRequest>().RequireRateLimiting(RateLimits.Otp);

        app.MapGet("/api/me", (ICurrentUser user, AuthService s, CancellationToken ct) => s.MeAsync(user.UserId!.Value, ct))
            .RequireAuthorization().WithTags("Auth");

        app.MapGet("/api/me/businesses", (ICurrentUser user, AuthService s, CancellationToken ct) => s.BusinessesAsync(user.UserId!.Value, ct))
            .RequireAuthorization().WithTags("Auth");
        var notes = app.MapGroup("/api/me/notifications").WithTags("Notifications").RequireAuthorization();
        notes.MapGet("/", (bool? unread, int? page, InAppNotifications s, CancellationToken ct) => s.ListAsync(unread == true, page ?? 1, ct));
        notes.MapGet("/unread-count", (InAppNotifications s, CancellationToken ct) => s.UnreadCountAsync(ct));
        notes.MapPost("/{id:guid}/read", async (Guid id, InAppNotifications s, CancellationToken ct) =>
        {
            await s.MarkReadAsync(id, ct);
            return Results.NoContent();
        });
        notes.MapPost("/read-all", async (InAppNotifications s, CancellationToken ct) =>
        {
            await s.MarkAllReadAsync(ct);
            return Results.NoContent();
        });

        auth.MapPost("/switch", (SwitchRequest r, ICurrentUser user, AuthService s, CancellationToken ct) => s.SwitchAsync(user.UserId!.Value, r, ct))
            .RequireAuthorization();
        auth.MapPost("/invites/{token}/accept", (string token, AcceptInviteRequest r, AuthService s, CancellationToken ct) =>
            s.AcceptInviteAsync(token, r, ct)).Validate<AcceptInviteRequest>();
        app.MapGet("/api/public/invites/{token}", (string token, AuthService s, CancellationToken ct) => s.GetInviteAsync(token, ct))
            .WithTags("Team");

        var team = app.MapGroup("/api/team").WithTags("Team").RequireAuthorization(Policies.Admin);
        team.MapGet("/", (TeamService s, CancellationToken ct) => s.GetAsync(ct));
        team.MapPost("/invites", (InviteRequest r, TeamService s, CancellationToken ct) => s.InviteAsync(r, ct)).Validate<InviteRequest>();
        team.MapPost("/invites/{id:guid}/resend", (Guid id, TeamService s, CancellationToken ct) => s.ResendAsync(id, ct));
        team.MapDelete("/invites/{id:guid}", async (Guid id, TeamService s, CancellationToken ct) =>
        {
            await s.CancelInviteAsync(id, ct);
            return Results.NoContent();
        });
        team.MapPatch("/members/{id:guid}", (Guid id, ChangeRoleRequest r, TeamService s, CancellationToken ct) => s.ChangeRoleAsync(id, r, ct))
            .Validate<ChangeRoleRequest>();
        team.MapDelete("/members/{id:guid}", async (Guid id, TeamService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(id, ct);
            return Results.NoContent();
        });

        var billing = app.MapGroup("/api/billing").WithTags("Billing").RequireAuthorization();
        billing.MapGet("/", (BillingService s, CancellationToken ct) => s.GetAsync(ct));
        billing.MapPost("/checkout", (CheckoutRequest r, BillingService s, CancellationToken ct) => s.CheckoutAsync(r, ct))
            .RequireAuthorization(Policies.Owner);

        app.MapPost("/api/webhooks/paystack", async (HttpRequest request, PaystackGateway paystack, BillingService billingService,
            ILogger<PaystackGateway> logger, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            if (!paystack.IsValidSignature(body, request.Headers["x-paystack-signature"]))
            {
                logger.LogWarning("Rejected Paystack webhook with an invalid signature");
                return Results.Unauthorized();
            }
            if (PaystackClient.ParseChargeSuccess(body) is { } charge)
            {
                // Course purchases (crs_...) and plan subscriptions both arrive on the platform account.
                if (charge.Reference.StartsWith("crs_", StringComparison.Ordinal))
                    await request.HttpContext.RequestServices.GetRequiredService<Saas.Application.Courses.CourseCore>()
                        .ConfirmOrderAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
                else if (charge.Reference.StartsWith("bst_", StringComparison.Ordinal))
                    await request.HttpContext.RequestServices.GetRequiredService<Saas.Application.Property.BoostService>()
                        .ConfirmAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
                else if (charge.Reference.StartsWith("stay_", StringComparison.Ordinal))
                    await request.HttpContext.RequestServices.GetRequiredService<Saas.Application.Property.StayService>()
                        .ConfirmAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
                else
                    await billingService.ConfirmPaymentAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
            }
            return Results.Ok();
        }).WithTags("Webhooks").ExcludeFromDescription();

        // Each invoices business points its own Paystack webhook here; it is signed with that business's key.
        app.MapPost("/api/webhooks/paystack/invoices/{tenantId:guid}", async (Guid tenantId, HttpRequest request,
            PublicDocumentsService documents, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            var ok = await documents.HandleWebhookAsync(tenantId, body, request.Headers["x-paystack-signature"],
                PaystackClient.IsValidSignature, PaystackClient.ParseChargeSuccess, ct);
            return ok ? Results.Ok() : Results.Unauthorized();
        }).WithTags("Webhooks").ExcludeFromDescription();
    }
}

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Otp = "otp";
    public const string PublicWrite = "public-write";
    /// <summary>Payment provider webhooks: per IP but high, because one provider sends every business's webhooks from a few IPs.</summary>
    public const string Webhook = "webhook";
    public const string Preview = "preview";
    public const string Admin = "admin";
}

public static class Policies
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    /// <summary>Profit report and CSV exports: Owner, Admin, Accountant.</summary>
    public const string Reports = "reports";
    public const string OrdersProduct = "product:whatsapp-orders";
    public const string InvoicesProduct = "product:invoices";
    public const string BookingsProduct = "product:bookings";
    public const string CoursesProduct = "product:courses";
    public const string PropertyProduct = "product:property";
    public const string SchoolProduct = "product:school";
    public const string CareProduct = "product:care";
    public const string PlatformAdmin = "platform-admin"; // any admin role
    public const string PlatformModerator = "platform:moderator";
    public const string PlatformFinance = "platform:finance";
    public const string PlatformSupport = "platform:support";
    public const string PlatformSuper = "platform:super";
    /// <summary>Signed in through the admin panel (password + emailed code).</summary>
    public const string PlatformSession = "platform:session";
    public const string PlatformSupportOrFinance = "platform:support-finance";
}
