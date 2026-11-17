using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Application.Billing;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Auth;

public sealed record ParentProfileDto(string FullName, string Email, string? Phone);
public sealed record ParentPhoneRequest(string? Phone);
public sealed record ParentEmailRequest(string? Email);
public sealed record ParentEmailConfirmRequest(string? Email, string? Code);

/// <summary>
/// The parent's own phone and email (the parent portal's profile). Parent sessions only: <see cref="ParentAccess.CustomerUserId"/> refuses
/// business tokens. The phone changes at once. The email changes only once a 6-digit code sent to the new address is entered, so nobody
/// can move the account to a mailbox they don't hold; the old address is told. Each guardian record linked to the account, in every school,
/// takes the new phone or email too, unless another guardian of that school already has it (the school's record then stays as it is).
/// </summary>
public sealed class ParentProfileService(
    AppDbContext db, UserManager<AppUser> users, ParentAccess parents, NotificationQueue notifications, TimeProvider clock)
{
    public const string BadPhone = "Enter a phone number such as 0803 123 4567 or +234 803 123 4567.";
    public const string BadCode = "That code is wrong or has expired. Request a new one.";
    public const string EmailTaken = "Another account already uses this email address.";
    public const int CodesPer10Minutes = 3;

    private static string Purpose(Guid userId) => $"email-change:{userId:N}";
    private static string Hash(string email, string code) => Codes.Sha256($"{email}:{code}");

    public async Task<ParentProfileDto> GetAsync() => ToDto(await MeAsync());

    public async Task<ParentProfileDto> SetPhoneAsync(ParentPhoneRequest r, CancellationToken ct)
    {
        var user = await MeAsync();
        if (string.IsNullOrWhiteSpace(r.Phone) || !StudentService.TryNormalisePhone(r.Phone, out var phone) || phone is null)
            throw BadRequestException.Field("phone", BadPhone);
        user.PhoneNumber = phone;
        await users.UpdateAsync(user);

        var linked = await db.Guardians.IgnoreQueryFilters().Where(g => g.UserId == user.Id && g.Phone != phone).ToListAsync(ct);
        foreach (var guardian in linked)
        {
            if (await db.Guardians.IgnoreQueryFilters().AnyAsync(g => g.TenantId == guardian.TenantId && g.Id != guardian.Id && g.Phone == phone, ct)) continue;
            guardian.Phone = phone;
        }
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    /// <summary>Sends a 6-digit code to the new address. Nothing changes until it is entered at <see cref="ConfirmEmailAsync"/>.</summary>
    public async Task RequestEmailChangeAsync(ParentEmailRequest r, CancellationToken ct)
    {
        var user = await MeAsync();
        var email = CleanEmail(r.Email);
        if (string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
            throw BadRequestException.Field("email", "This is already your email address.");
        if (await users.FindByEmailAsync(email) is { } other && other.Id != user.Id) throw new ConflictException(EmailTaken, "email_taken");

        var now = clock.GetUtcNow();
        var purpose = Purpose(user.Id);
        if (await db.OtpCodes.CountAsync(o => o.Purpose == purpose && o.CreatedAt > now.AddMinutes(-10), ct) >= CodesPer10Minutes)
            throw new BadRequestException("We have sent several codes already. Wait a few minutes, then ask for a new one.");
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.OtpCodes.Add(new OtpCode { Target = email, Purpose = purpose, CodeHash = Hash(email, code), ExpiresAt = now.AddMinutes(10) });
        notifications.Email(email, "email.change_code",
            new Dictionary<string, string> { ["name"] = user.FullName, ["code"] = code, ["product"] = Products.School }, userId: user.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Checks the code sent to the new address, then moves the account (and its linked guardian records) to it.</summary>
    public async Task<ParentProfileDto> ConfirmEmailAsync(ParentEmailConfirmRequest r, CancellationToken ct)
    {
        var user = await MeAsync();
        var email = CleanEmail(r.Email);
        var code = (r.Code ?? "").Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) throw BadRequestException.Field("code", "Enter the 6-digit code.");

        var now = clock.GetUtcNow();
        var purpose = Purpose(user.Id);
        var otp = await db.OtpCodes.Where(o => o.Target == email && o.Purpose == purpose && o.UsedAt == null && o.ExpiresAt > now)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (otp is null || otp.Attempts >= 5) throw new BadRequestException(BadCode);
        otp.Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(Hash(email, code))))
        {
            await db.SaveChangesAsync(ct);
            throw new BadRequestException(BadCode);
        }
        otp.UsedAt = now;
        // Taken since the code was sent: the code is spent, the account stays as it is.
        if (await users.FindByEmailAsync(email) is { } other && other.Id != user.Id)
        {
            await db.SaveChangesAsync(ct);
            throw new ConflictException(EmailTaken, "email_taken");
        }

        var oldEmail = user.Email!;
        user.Email = email;
        user.NormalizedEmail = users.NormalizeEmail(email);
        user.UserName = email;
        user.NormalizedUserName = users.NormalizeName(email);
        user.EmailConfirmed = true;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) throw new ConflictException(EmailTaken, "email_taken");

        var linked = await db.Guardians.IgnoreQueryFilters().Where(g => g.UserId == user.Id && g.Email != email).ToListAsync(ct);
        foreach (var guardian in linked)
        {
            if (await db.Guardians.IgnoreQueryFilters().AnyAsync(g => g.TenantId == guardian.TenantId && g.Id != guardian.Id && g.Email == email, ct)) continue;
            guardian.Email = email;
        }
        notifications.Email(oldEmail, "email.changed",
            new Dictionary<string, string> { ["name"] = user.FullName, ["email"] = email, ["product"] = Products.School }, userId: user.Id);
        db.AuditLogs.Add(new AuditLog { ActorId = user.Id, Action = "user.email_change", EntityType = "User", EntityId = user.Id.ToString(), CreatedAt = now });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    private async Task<AppUser> MeAsync()
    {
        var userId = parents.CustomerUserId();
        var user = await users.FindByIdAsync(userId.ToString());
        // Student logins are customer sessions too, but the school owns their sign-in; they have no profile to change here.
        if (user is null || StudentLogins.IsStudentEmail(user.Email)) throw new ForbiddenException("Sign in to your parent account first.");
        return user;
    }

    private static string CleanEmail(string? raw)
    {
        var email = StudentService.NormaliseEmail(raw);
        if (email is null) throw BadRequestException.Field("email", "Enter your new email address.");
        if (email.Length > 200 || !MailAddress.TryCreate(email, out var mail) || mail.Address != email || !email.Split('@')[^1].Contains('.')
            || StudentLogins.IsStudentEmail(email))
            throw BadRequestException.Field("email", "Enter a valid email address.");
        return email;
    }

    private static ParentProfileDto ToDto(AppUser user) => new(user.FullName, user.Email!, user.PhoneNumber);
}
