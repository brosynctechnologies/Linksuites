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
        // Final fix pass B: a sign-up answers { confirmationRequired, email } and emails a code; /signup/confirm (code + the same password)
        // issues the session. A confirmed account adding a product with its password gets a session straight away.
        auth.MapPost("/signup", async (SignupRequest r, AuthService s, CancellationToken ct) =>
        {
            var outcome = await s.SignupAsync(r, ct);
            return outcome.Session is { } session ? Results.Ok(session) : Results.Ok(outcome.Pending);
        }).Validate<SignupRequest>();
        auth.MapPost("/signup/confirm", (ConfirmSignupRequest r, AuthService s, CancellationToken ct) => s.ConfirmSignupAsync(r, customer: false, ct))
            .Validate<ConfirmSignupRequest>().RequireRateLimiting(RateLimits.Otp);
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
        // The return check every billing page calls on ?ref=: any signed-in member, for their own business's payment only.
        // Rate limited per signed-in user (the per-user policy the admin routes use); the web page sends about 11 in 30 seconds.
        billing.MapPost("/verify", (VerifyPaymentRequest r, BillingService s, CancellationToken ct) => s.VerifyAsync(r, ct))
            .RequireRateLimiting(RateLimits.Admin);
        // Bank transfer for plans while online payment is off: "I've sent the transfer" and its cancel; a platform admin confirms it.
        billing.MapPost("/transfer", (TransferRequest r, BillingService s, CancellationToken ct) => s.ClaimTransferAsync(r, ct))
            .RequireAuthorization(Policies.Owner).RequireRateLimiting(RateLimits.TransferClaim);
        billing.MapDelete("/transfer", async (BillingService s, CancellationToken ct) =>
        {
            await s.CancelTransferAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Owner);

        // OPay's callback for plan payments. Only the reference is read: OPay is then asked for the status, so the body's status and
        // signature are never trusted. Always 200 (unknown or unreadable bodies are logged), so OPay doesn't retry what can't succeed.
        app.MapPost("/api/webhooks/opay", async (HttpRequest request, BillingService billingService, ILogger<BillingService> logger, CancellationToken ct) =>
        {
            // An anonymous body is read at most 64 KB; OPay's callbacks are under 2 KB.
            if (request.ContentLength > OpayCallbackMaxChars) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var reader = new StreamReader(request.Body);
            var buffer = new char[OpayCallbackMaxChars + 1];
            var read = 0;
            int n;
            while (read < buffer.Length && (n = await reader.ReadAsync(buffer.AsMemory(read), ct)) > 0) read += n;
            if (read > OpayCallbackMaxChars) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var body = new string(buffer, 0, read);
            var reference = OpayCallbackReference(body);
            if (reference is null)
            {
                logger.LogWarning("Ignored an OPay callback without a readable reference");
                return Results.Ok();
            }
            await billingService.ConfirmFromProviderAsync(reference, ct);
            return Results.Ok();
        }).AllowAnonymous().RequireRateLimiting(RateLimits.Webhook).WithTags("Webhooks").ExcludeFromDescription();

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

    /// <summary>The largest OPay callback body read (64 KB); bigger ones are answered 413 without asking OPay.</summary>
    public const int OpayCallbackMaxChars = 64 * 1024;

    /// <summary><c>payload.reference</c> from an OPay callback (or a top-level <c>reference</c>); null when the body can't be read.</summary>
    public static string? OpayCallbackReference(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            var holder = root.TryGetProperty("payload", out var payload) && payload.ValueKind == System.Text.Json.JsonValueKind.Object ? payload : root;
            var reference = holder.TryGetProperty("reference", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.String ? r.GetString()?.Trim() : null;
            return string.IsNullOrEmpty(reference) || reference.Length > 100 ? null : reference;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
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
    /// <summary>Bank transfer claims for plans: a few per business per hour (RateLimits:TransferClaimsPerHour, default 5).</summary>
    public const string TransferClaim = "transfer-claim";
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
