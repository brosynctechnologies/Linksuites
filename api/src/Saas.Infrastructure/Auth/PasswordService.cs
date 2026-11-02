using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Platform;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Auth;

public sealed record ForgotPasswordRequest(string Email, string? Product);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? RefreshToken);

/// <summary>
/// Forgot, reset and change password for every product and the admin console. A reset or change signs the user out
/// everywhere: refresh tokens are revoked and admin-panel sessions end. Customer access tokens expire within 15 minutes.
/// </summary>
public sealed class PasswordService(AppDbContext db, UserManager<AppUser> users, AuthSessions sessions, NotificationQueue notifications,
    AppUrls urls, ICurrentUser current, TimeProvider clock)
{
    public const int MaxEmailsPerHour = 3;
    private static readonly HashSet<string> Apps = [.. Products.All, "admin"];

    /// <summary>
    /// Emails a reset link when the account exists. The public route always looks the same from outside, whether or not it does;
    /// the result (false when there's no account or the hourly limit stopped it) is for the support console.
    /// </summary>
    public async Task<bool> ForgotAsync(ForgotPasswordRequest r, CancellationToken ct)
    {
        var email = RequireEmail(r.Email);
        var user = await users.FindByEmailAsync(email);
        if (user is null || StudentLogins.IsStudentEmail(user.Email)) return false; // the school resets a student's password
        var since = clock.GetUtcNow().AddHours(-1);
        if (await db.Notifications.CountAsync(n => n.TemplateKey == "password.reset" && n.Recipient == email && n.CreatedAt > since, ct) >= MaxEmailsPerHour) return false;

        var product = r.Product?.Trim().ToLowerInvariant() is { } p && Apps.Contains(p)
            ? p
            : await db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == user.Id).OrderBy(m => m.CreatedAt).Select(m => m.Tenant!.Product).FirstOrDefaultAsync(ct);
        var web = product is null ? urls.WebBaseUrl.TrimEnd('/') : urls.Web(product);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        // The token travels in the URL fragment, which browsers never send to servers or put in logs.
        var link = $"{web}/reset-password#email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        notifications.Email(email, "password.reset", new() { ["name"] = user.FullName, ["link"] = link }, userId: user.Id);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ResetAsync(ResetPasswordRequest r, CancellationToken ct)
    {
        var invalid = BadRequestException.Field("token", "This reset link is invalid or has expired. Ask for a new one.");
        var email = RequireEmail(r.Email);
        if (string.IsNullOrWhiteSpace(r.Token)) throw invalid;
        if (string.IsNullOrEmpty(r.NewPassword)) throw BadRequestException.Field("newPassword", "Enter a new password.");
        var user = await users.FindByEmailAsync(email) ?? throw invalid;
        var result = await users.ResetPasswordAsync(user, r.Token, r.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken")) throw invalid;
            throw PasswordErrors(result, "newPassword");
        }
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await SignOutEverywhereAsync(user, keepRefreshTokenHash: null, "user.password_reset", ct);
    }

    private static string RequireEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? throw BadRequestException.Field("email", "Enter your email address.") : email.Trim().ToLowerInvariant();

    public async Task ChangeAsync(ChangePasswordRequest r, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(r.CurrentPassword)) throw BadRequestException.Field("currentPassword", "Enter your current password.");
        if (string.IsNullOrEmpty(r.NewPassword)) throw BadRequestException.Field("newPassword", "Enter a new password.");
        var userId = current.UserId ?? throw new ForbiddenException();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException();
        var result = await users.ChangePasswordAsync(user, r.CurrentPassword, r.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch")) throw BadRequestException.Field("currentPassword", "Your current password is wrong.");
            throw PasswordErrors(result, "newPassword");
        }
        await SignOutEverywhereAsync(user, string.IsNullOrWhiteSpace(r.RefreshToken) ? null : Codes.Sha256(r.RefreshToken), "user.password_change", ct);
    }

    private async Task SignOutEverywhereAsync(AppUser user, string? keepRefreshTokenHash, string action, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null && t.TokenHash != keepRefreshTokenHash)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await sessions.EndAllForUserAsync(user.Id, user.Id, ct);
        db.AuditLogs.Add(new AuditLog { ActorId = current.UserId ?? user.Id, Action = action, EntityType = "User", EntityId = user.Id.ToString(), Ip = current.IpAddress, CreatedAt = now });
        if (!StudentLogins.IsStudentEmail(user.Email))
            notifications.Email(user.Email!, "password.changed", new() { ["name"] = user.FullName }, userId: user.Id);
        await db.SaveChangesAsync(ct);
    }

    private static BadRequestException PasswordErrors(IdentityResult result, string field)
    {
        var errors = result.Errors.Select(e => e.Description).ToArray();
        return new BadRequestException(errors.FirstOrDefault() ?? "Choose a different password.", new Dictionary<string, string[]> { [field] = errors });
    }
}
