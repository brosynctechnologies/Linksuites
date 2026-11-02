using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Platform;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Auth;

public sealed record PlatformLoginRequest(string Email, string Password);
public sealed record PlatformChallengeDto(Guid ChallengeId);
public sealed record PlatformVerifyRequest(Guid ChallengeId, string Code);

/// <summary>
/// Admin-panel sign-in: password, then a 6-digit code emailed every time. The resulting token lasts 8 hours, has no refresh
/// token, and carries <c>amr=otp</c>, which every <c>/api/platform</c> route requires.
/// </summary>
public sealed class PlatformAuthService(AppDbContext db, UserManager<AppUser> users, PlatformAccess access, AuthSessions sessions, NotificationQueue notifications,
    IOptions<JwtOptions> jwtOptions, TimeProvider clock)
{
    public const string OtpPurpose = "platform-login";
    public static readonly TimeSpan SessionLength = TimeSpan.FromHours(8);
    public const int MaxFailedSignIns = 5;
    public static readonly TimeSpan LockoutLength = TimeSpan.FromMinutes(10);
    private readonly JwtOptions _jwt = jwtOptions.Value;

    /// <summary>Same error for a wrong password, an unknown email and a non-admin, so the response reveals nothing.</summary>
    private static BadRequestException Invalid() => new("Email or password is incorrect, or this account can't use the admin panel.");

    public async Task<PlatformChallengeDto> LoginAsync(PlatformLoginRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email);
        var now = clock.GetUtcNow();
        // The console keeps its own failure count (not Identity's lockout), so bad customer sign-ins against an admin's email
        // can't lock them out of the console, and bad console attempts don't lock their customer account.
        if (user is null || user.ConsoleLockoutEnd > now) throw Invalid();
        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            if (++user.ConsoleFailedCount >= MaxFailedSignIns)
            {
                user.ConsoleFailedCount = 0;
                user.ConsoleLockoutEnd = now + LockoutLength;
            }
            await users.UpdateAsync(user);
            throw Invalid();
        }
        if (await access.ActiveRoleAsync(user.Id, ct) is null && !await access.CanBootstrapAsync(user.Id, user.Email, ct)) throw Invalid();
        if (user.ConsoleFailedCount != 0 || user.ConsoleLockoutEnd is not null)
        {
            user.ConsoleFailedCount = 0;
            user.ConsoleLockoutEnd = null;
            await users.UpdateAsync(user);
        }

        if (await db.OtpCodes.CountAsync(o => o.Target == email && o.Purpose == OtpPurpose && o.CreatedAt > now.AddMinutes(-10), ct) >= 3)
            throw new BadRequestException("Too many codes requested. Wait a few minutes and try again.");
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otp = new OtpCode { Target = email, Purpose = OtpPurpose, CodeHash = Hash(email, code), ExpiresAt = now.AddMinutes(10) };
        db.OtpCodes.Add(otp);
        notifications.Email(email, "admin.code", new() { ["name"] = user.FullName, ["code"] = code }, userId: user.Id);
        await db.SaveChangesAsync(ct);
        return new PlatformChallengeDto(otp.Id);
    }

    public async Task<AuthResponse> VerifyAsync(PlatformVerifyRequest r, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var invalid = new BadRequestException("That code is wrong or has expired. Sign in again to get a new one.");
        var otp = await db.OtpCodes.FirstOrDefaultAsync(o => o.Id == r.ChallengeId && o.Purpose == OtpPurpose, ct) ?? throw invalid;
        if (otp.UsedAt is not null || otp.ExpiresAt <= now || otp.Attempts >= 5) throw invalid;
        otp.Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(Hash(otp.Target, r.Code.Trim())));
        if (!ok)
        {
            await db.SaveChangesAsync(ct);
            throw invalid;
        }
        otp.UsedAt = now;
        await db.SaveChangesAsync(ct); // the code is spent before anything else can fail
        var user = await users.FindByEmailAsync(otp.Target) ?? throw invalid;
        var role = await access.ActiveRoleAsync(user.Id, ct)
            ?? (await access.CanBootstrapAsync(user.Id, user.Email, ct) ? await access.BootstrapAsync(user.Id, ct) : throw invalid);
        user.LastLoginAt = now;
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = user.Id, Action = "admin.login", EntityType = nameof(PlatformAdmin), EntityId = user.Id.ToString(), CreatedAt = now,
            AfterJson = $$"""{"role":"{{role}}"}""",
        });
        var expires = now + SessionLength;
        var session = sessions.Start(Guid.CreateVersion7(), user.Id, AuthSessionKind.AdminPanel, expires);
        await db.SaveChangesAsync(ct);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_jwt.Key(), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Name, user.FullName), new Claim(AppClaims.PlatformAdmin, role.ToString()),
                new Claim(AppClaims.Amr, "otp"), new Claim(JwtRegisteredClaimNames.Jti, session.Id.ToString()),
            ]),
        });
        return new AuthResponse(token, "", expires, new UserDto(user.Id, user.FullName, user.Email!, true, role.ToString()), null);
    }

    /// <summary>Ends the current admin-panel session (sign out).</summary>
    public Task LogoutAsync(Guid sessionId, Guid userId, CancellationToken ct) => sessions.EndAsync(sessionId, userId, ct);

    private static string Hash(string target, string code) => Codes.Sha256($"{OtpPurpose}:{target}:{code}");
}
