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
using Saas.Application.School;
using Saas.Application.Team;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "saas-api";
    public string Audience { get; set; } = "saas-web";
    /// <summary>At least 32 characters. Set via configuration/secrets, never commit a production key.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class AppClaims
{
    public const string Tenant = "tid";
    public const string Role = "role";
    public const string Product = "prd";
    public const string PlatformAdmin = "padm";
    /// <summary>"otp" on admin-panel tokens (password + emailed code).</summary>
    public const string Amr = "amr";
    /// <summary>On a support view-as token: the admin's user id.</summary>
    public const string ViewAs = "vas";
    /// <summary>"1" on read-only tokens: every write is refused.</summary>
    public const string ReadOnly = "ro";
}

public sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITenantContext tenant,
    NotificationQueue notifications,
    IEnumerable<IProductProvisioner> provisioners,
    Saas.Application.Platform.PlatformAccess access,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock,
    IDbErrors dbErrors)
{
    private const string OtpPurpose = "login";
    private readonly JwtOptions _jwt = jwtOptions.Value;

    /// <summary>Password login's answer for an account whose sign-up was never confirmed with the emailed code (403 <c>email_unconfirmed</c>).</summary>
    public const string ConfirmEmailFirst = "Confirm your email first. Enter the 6-digit code we emailed you, or ask for a new one.";

    /// <summary>
    /// Final fix pass B (user decision I3): a password sign-up issues no session. It emails a 6-digit code at once and answers
    /// <see cref="SignupPending"/>; the account is usable only once the code is entered at <see cref="ConfirmSignupAsync"/>, which sets the
    /// password typed at sign-up. Until then the account has no password at all, so password login answers <see cref="ConfirmEmailFirst"/>.
    /// A confirmed account adding a product with its password is signed in at once, as before.
    /// </summary>
    public async Task<SignupOutcome> SignupAsync(SignupRequest r, CancellationToken ct)
    {
        var product = r.ProductKey;
        var provisioner = provisioners.FirstOrDefault(p => p.Product == product)
            ?? throw BadRequestException.Field("product", "Unknown product.");
        var email = r.Email.Trim().ToLowerInvariant();
        if (StudentLogins.IsStudentEmail(email)) throw BadRequestException.Field("email", "Enter a valid email address.");
        var country = r.Country.ToUpperInvariant();
        // A link the business asked for (already checked against the reserved words) must be free.
        if (r.SlugKey is { } wanted && await SlugInUseAsync(wanted, ct))
            throw BadRequestException.Field("slug", "That link is taken. Choose another.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var user = await users.FindByEmailAsync(email);
        var addBusiness = true;
        if (user is { EmailConfirmed: true })
        {
            // One login across products: a confirmed account adds this product by proving its password.
            if (await users.IsLockedOutAsync(user) || !await users.CheckPasswordAsync(user, r.Password))
                throw BadRequestException.Field("email",
                    "An account with this email already exists. Log in, or enter your existing password to add this product.");
            if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == user.Id && m.Tenant!.Product == product, ct))
                throw BadRequestException.Field("email", "You already have an account for this product. Log in instead.");
        }
        else if (user is not null)
        {
            // Ruling (final fix pass B, I3 point 5): the address has an account that was never confirmed. Nothing here proves who is asking,
            // so the answer is the same as for a new sign-up and a new code goes to the mailbox; whoever enters it sets the password
            // (ConfirmSignupAsync), so neither the earlier sign-up nor this one gets the account without the mailbox. The business is added,
            // unless the account already has one for this product, or it is an account from before codes at sign-up (it has a password and
            // can still sign in with it) and this is not its password.
            var otherThanLegacyOwner = await users.HasPasswordAsync(user)
                && (await users.IsLockedOutAsync(user) || !await users.CheckPasswordAsync(user, r.Password));
            addBusiness = !otherThanLegacyOwner
                && !await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == user.Id && m.Tenant!.Product == product, ct);
        }
        else
        {
            user = await CreateUnconfirmedAsync(r.FullName, email, r.Password);
        }

        Tenant? t = null;
        if (addBusiness)
        {
            t = new Tenant
            {
                Name = r.Name,
                Slug = r.SlugKey ?? await AvailableSlugAsync(r.Name, ct),
                Country = country,
                Currency = Countries.Currency(country),
                Timezone = Countries.Timezone(country),
                Product = product,
            };
            db.Tenants.Add(t);
            tenant.SetTenant(t.Id);
            db.Memberships.Add(new Membership { UserId = user.Id, Role = MemberRole.Owner });
            await provisioner.ProvisionAsync(t, r, user.Id, ct);
            // An unconfirmed sign-up is welcomed once its code is entered (ConfirmSignupAsync), not before.
            if (user.EmailConfirmed) Welcome(user, t);
        }
        if (!user.EmailConfirmed) await AddCodeAsync(user, email, product, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dbErrors.IsUniqueViolation(ex) && IsSlugIndex(ex))
        {
            // Another business holds the link (taken between the check above and this insert, or held by a product profile whose link
            // differs from its tenant's); the transaction rolls back. Only schools choose a link, so others are told to change the name.
            throw SlugTaken(r);
        }

        if (!user.EmailConfirmed)
        {
            await tx.CommitAsync(ct);
            return new SignupOutcome(null, new SignupPending(email));
        }
        var response = await IssueAsync(user, t!, MemberRole.Owner, ct);
        await tx.CommitAsync(ct);
        return new SignupOutcome(response, null);
    }

    private void Welcome(AppUser user, Tenant t) =>
        notifications.Email(user.Email!, "welcome", new() { ["name"] = user.FullName, ["storeName"] = t.Name, ["slug"] = t.Slug }, t.Id, user.Id);

    /// <summary>The welcome held back at an unconfirmed sign-up: one per business the new owner signed up, sent once the code proves the mailbox.</summary>
    private async Task WelcomeOwnedBusinessesAsync(AppUser user, CancellationToken ct)
    {
        var owned = await db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == user.Id && m.Role == MemberRole.Owner)
            .Select(m => m.Tenant!).ToListAsync(ct);
        foreach (var t in owned) Welcome(user, t);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A new account for a password sign-up: checked against the password rules (so the sign-up form shows them), but created with no
    /// password and an unconfirmed email. <see cref="ConfirmSignupAsync"/> sets the password once the emailed code is entered.
    /// </summary>
    private async Task<AppUser> CreateUnconfirmedAsync(string fullName, string email, string password)
    {
        var user = new AppUser { FullName = fullName.Trim(), Email = email, UserName = email };
        await ValidateNewPasswordAsync(user, password);
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            var errors = created.Errors.Select(e => e.Description).ToArray();
            throw new BadRequestException(errors.FirstOrDefault() ?? "Could not create the account.",
                new Dictionary<string, string[]> { ["email"] = errors });
        }
        return user;
    }

    /// <summary>400 on <c>password</c> with the password rules' sentences, without setting anything.</summary>
    private async Task ValidateNewPasswordAsync(AppUser user, string password)
    {
        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            if (!result.Succeeded) errors.AddRange(result.Errors.Select(e => e.Description));
        }
        if (errors.Count > 0)
            throw new BadRequestException(errors[0], new Dictionary<string, string[]> { ["password"] = [.. errors] });
    }

    /// <summary>True when the account has a password and it is this one.</summary>
    private async Task<bool> PasswordIsAsync(AppUser user, string password) =>
        await users.HasPasswordAsync(user) && await users.CheckPasswordAsync(user, password);

    /// <summary>Whoever proved the mailbox sets the password: the account's password becomes <paramref name="password"/> (kept when it already is).</summary>
    private async Task SetProvenPasswordAsync(AppUser user, string password)
    {
        if (await PasswordIsAsync(user, password)) return;
        await ValidateNewPasswordAsync(user, password);
        if (await users.HasPasswordAsync(user)) await users.RemovePasswordAsync(user);
        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            var errors = added.Errors.Select(e => e.Description).ToArray();
            throw new BadRequestException(errors.FirstOrDefault() ?? "Choose a different password.", new Dictionary<string, string[]> { ["password"] = errors });
        }
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
    }

    /// <summary>Ends everything issued before the mailbox was proved: rotates the security stamp and revokes every refresh token.</summary>
    private async Task EndEarlierSessionsAsync(AppUser user, CancellationToken ct)
    {
        await users.UpdateSecurityStampAsync(user);
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    /// <summary>
    /// Finishes a password sign-up (<c>POST /api/auth/signup/confirm</c>, and each customer route's <c>/signup/confirm</c>). The password is
    /// checked against the rules before the code is used, so a refused password leaves the code to try again. The code proves the mailbox:
    /// a never-confirmed account takes this password (whoever proves the mailbox sets it), is confirmed, and anything issued before it ends.
    /// An account that is already confirmed keeps its password; the code only signs it in. Then a session: the business of <c>Product</c>, or
    /// a customer session (<paramref name="customer"/>).
    /// </summary>
    public async Task<AuthResponse> ConfirmSignupAsync(ConfirmSignupRequest r, bool customer, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is { EmailConfirmed: false } pending && !await PasswordIsAsync(pending, r.Password))
            await ValidateNewPasswordAsync(pending, r.Password);
        var user = await ConsumeCodeAsync(new OtpVerifyRequest(email, r.Code), ct);
        if (!user.EmailConfirmed)
        {
            await SetProvenPasswordAsync(user, r.Password);
            await ConfirmEmailAsync(user);
            await EndEarlierSessionsAsync(user, ct);
            if (!customer) await WelcomeOwnedBusinessesAsync(user, ct);
        }
        if (!customer) return await LoginUserAsync(user, r.Product, ct);
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return await IssueAsync(user, null, null, ct);
    }

    /// <summary>
    /// 403 <see cref="ConfirmEmailFirst"/> for a sign-up that was never confirmed (it has no password yet). Accounts from before codes at
    /// sign-up (a password, never confirmed) keep password login (ruling, final fix pass B).
    /// </summary>
    private async Task EnsureSignupConfirmedAsync(AppUser user)
    {
        if (!user.EmailConfirmed && !await users.HasPasswordAsync(user)) throw new EmailUnconfirmedException();
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest r, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant());
        if (user is null || StudentLogins.IsStudentEmail(user.Email)) throw InvalidLogin(); // students sign in on their school's page
        await EnsureSignupConfirmedAsync(user);
        if (await users.IsLockedOutAsync(user))
            throw new BadRequestException("Too many failed attempts. Try again in a few minutes or log in with an email code.");
        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            await users.AccessFailedAsync(user);
            throw InvalidLogin();
        }
        await users.ResetAccessFailedCountAsync(user);
        return await LoginUserAsync(user, r.Product, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest r, CancellationToken ct)
    {
        var hash = Codes.Sha256(r.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw Unauthorized();
        var now = clock.GetUtcNow();
        if (token.RevokedAt is not null)
        {
            // A rotated token was used again: assume it was stolen and end every session for this user.
            await db.RefreshTokens.Where(t => t.UserId == token.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            throw Unauthorized();
        }
        if (token.ExpiresAt <= now) throw Unauthorized();

        var user = await users.FindByIdAsync(token.UserId.ToString()) ?? throw Unauthorized();
        if (token.TenantId == Guid.Empty)
        {
            token.RevokedAt = now;
            return await IssueAsync(user, null, null, ct, replacing: token); // learner session
        }
        var membership = await db.Memberships.IgnoreQueryFilters().Include(m => m.Tenant)
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == token.TenantId && m.Status == MembershipStatus.Active, ct)
            ?? throw Unauthorized();
        if (membership.Tenant!.Status != TenantStatus.Active) throw Suspended();

        token.RevokedAt = now;
        var response = await IssueAsync(user, membership.Tenant!, membership.Role, ct, replacing: token);
        return response;
    }

    public async Task LogoutAsync(RefreshRequest r, CancellationToken ct)
    {
        var hash = Codes.Sha256(r.RefreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
    }

    /// <summary>Emails a 6-digit login code. Always succeeds so the response never reveals whether an email is registered.</summary>
    public async Task RequestOtpAsync(OtpRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email);
        if (user is null || StudentLogins.IsStudentEmail(user.Email)) return; // a student's address is never emailed

        if (!await AddCodeAsync(user, email, r.Product, ct)) return;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds a 6-digit code for <paramref name="email"/> and queues the "otp" email (saved with the caller's next save). The code serves both
    /// a code sign-in and a sign-up's confirmation. False, and nothing added, when 3 codes went out in the last 10 minutes.
    /// </summary>
    private async Task<bool> AddCodeAsync(AppUser user, string email, string? product, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var recent = await db.OtpCodes.CountAsync(o => o.Target == email && o.Purpose == OtpPurpose && o.CreatedAt > now.AddMinutes(-10), ct);
        if (recent >= 3) return false;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.OtpCodes.Add(new OtpCode { Target = email, Purpose = OtpPurpose, CodeHash = HashOtp(email, code), ExpiresAt = now.AddMinutes(10) });
        var payload = new Dictionary<string, string> { ["name"] = user.FullName, ["code"] = code };
        if ((Known(product) ?? await AccountProductAsync(db, user.Id, ct)) is { } p) payload["product"] = p;
        notifications.Email(email, "otp", payload, userId: user.Id);
        return true;
    }

    private static string? Known(string? product) =>
        product?.Trim().ToLowerInvariant() is { } p && Products.All.Contains(p) ? p : null;

    /// <summary>
    /// The product an account's emails speak for when the page that asked didn't say: its only business product, else the customer portal it
    /// is linked to (CareLink patient, SchoolLink parent). Null for an account on several products or none (the email says LinkSuite).
    /// </summary>
    internal static async Task<string?> AccountProductAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var products = await db.Memberships.IgnoreQueryFilters().Where(m => m.UserId == userId).Select(m => m.Tenant!.Product).Distinct().Take(2).ToListAsync(ct);
        if (products.Count > 0) return products.Count == 1 ? products[0] : null;
        if (await db.PatientLinks.IgnoreQueryFilters().AnyAsync(l => l.UserId == userId, ct)) return Products.Care;
        if (await db.Guardians.IgnoreQueryFilters().AnyAsync(g => g.UserId == userId, ct)) return Products.School;
        return null;
    }

    public async Task<AuthResponse> VerifyOtpAsync(OtpVerifyRequest r, CancellationToken ct)
    {
        var user = await ConsumeCodeAsync(r, ct);
        // Rulings (Task 15 fix rounds 3 and 4, R2 and R3): the email is confirmed only once a membership is found (a refused business sign-in
        // leaves the account unconfirmed), and a first confirmation of an account from before codes at sign-up takes it over here too
        // (password removed, stamp rotated, refresh tokens revoked) before the session is issued (final fix pass B: legacy accounts only).
        return await LoginUserAsync(user, r.Product, ct, beforeIssue: async () =>
        {
            var legacy = await IsLegacyUnconfirmedAsync(user);
            if (await ConfirmEmailAsync(user) && legacy) await TakeOverForMailboxOwnerAsync(user, ct);
        });
    }

    /// <summary>Signs in with an emailed code to a customer session (no business attached), as parents and learners use.</summary>
    public async Task<AuthResponse> CustomerVerifyOtpAsync(OtpVerifyRequest r, CancellationToken ct)
    {
        var user = await ConsumeCodeAsync(r, ct);
        var legacy = await IsLegacyUnconfirmedAsync(user);
        if (await ConfirmEmailAsync(user) && legacy) await TakeOverForMailboxOwnerAsync(user, ct);
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return await IssueAsync(user, null, null, ct);
    }

    /// <summary>
    /// An account from before codes at sign-up (final fix pass B): never confirmed, yet it has a password. Since then a sign-up has no
    /// password until its code is entered, and every route that sets one (sign-up confirm, reset, team invite) confirms the email too.
    /// </summary>
    private async Task<bool> IsLegacyUnconfirmedAsync(AppUser user) => !user.EmailConfirmed && await users.HasPasswordAsync(user);

    /// <summary>
    /// Ruling (Task 15 fix round 2, R1; scoped by final fix pass B to accounts that existed unconfirmed before codes at sign-up): the first
    /// code sign-in proves the mailbox, so whoever set up the account before (perhaps someone who only knew the address) loses it. The
    /// password is removed, the security stamp rotated and every refresh token revoked. The mailbox's owner can set a password again (a reset).
    /// Access tokens issued before <see cref="AppUser.EmailConfirmedAt"/> are refused by the patient portal. A sign-up made since then has no
    /// password before its code, so this never fires on it, and a confirmed account's password is never removed.
    /// </summary>
    private async Task TakeOverForMailboxOwnerAsync(AppUser user, CancellationToken ct)
    {
        if (await users.HasPasswordAsync(user)) await users.RemovePasswordAsync(user);
        await EndEarlierSessionsAsync(user, ct);
    }

    /// <summary>Checks and uses the emailed code; the account it was sent to. Confirms nothing (see <see cref="ConfirmEmailAsync"/>).</summary>
    private async Task<AppUser> ConsumeCodeAsync(OtpVerifyRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow();
        var otp = await db.OtpCodes
            .Where(o => o.Target == email && o.Purpose == OtpPurpose && o.UsedAt == null && o.ExpiresAt > now)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        var invalid = new BadRequestException("That code is wrong or has expired. Request a new one.");
        if (otp is null || otp.Attempts >= 5) throw invalid;

        otp.Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(HashOtp(email, r.Code.Trim())));
        if (!ok)
        {
            await db.SaveChangesAsync(ct);
            throw invalid;
        }
        otp.UsedAt = now;
        await db.SaveChangesAsync(ct);

        return await users.FindByEmailAsync(email) ?? throw invalid;
    }

    /// <summary>
    /// Marks the email confirmed (a used code proves the mailbox) and records <see cref="AppUser.EmailConfirmedAt"/> when it has none yet.
    /// True when this is the first confirmation.
    /// </summary>
    private async Task<bool> ConfirmEmailAsync(AppUser user)
    {
        var first = !user.EmailConfirmed;
        if (first || user.EmailConfirmedAt is null)
        {
            user.EmailConfirmed = true;
            user.EmailConfirmedAt ??= clock.GetUtcNow();
            await users.UpdateAsync(user);
        }
        return first;
    }

    public async Task<MeResponse> MeAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw Unauthorized();
        var role = await access.ActiveRoleAsync(user.Id, ct);
        if (tenant.TenantId is not { } tenantId) return new MeResponse(ToDto(user, role), null);
        var m = await db.Memberships.Include(x => x.Tenant).FirstOrDefaultAsync(x => x.UserId == userId && x.TenantId == tenantId, ct)
            ?? throw Unauthorized();
        return new MeResponse(ToDto(user, role), new TenantDto(m.TenantId, m.Tenant!.Name, m.Tenant.Slug, m.Role, m.Tenant.Product));
    }

    // Team invitations

    public async Task<InvitePreview> GetInviteAsync(string token, CancellationToken ct)
    {
        var invite = await FindInviteAsync(token, ct);
        var business = await db.Tenants.Where(t => t.Id == invite.TenantId).Select(t => new { t.Name, t.Product }).FirstAsync(ct);
        var name = business.Name;
        var roleLabel = business.Product == Products.Care ? Saas.Application.Care.CareStaff.RoleLabel(invite.Role, invite.ClinicalRole) : null;
        var expired = invite.AcceptedAt is not null || invite.ExpiresAt <= clock.GetUtcNow();
        // An unfinished sign-up (never confirmed, no password yet) is not an account to prove with a password: the page asks for a name and
        // a new password, as for someone new.
        var account = await users.FindByEmailAsync(invite.Email);
        var hasAccount = account is not null && (account.EmailConfirmed || await users.HasPasswordAsync(account));
        return new InvitePreview(name, invite.Email, invite.Role, hasAccount, expired, roleLabel);
    }

    /// <summary>
    /// Joins the business the invite is for. Someone with a confirmed account proves it with their password; anyone else creates an
    /// account, and clicking the emailed link confirms their email. Final fix pass B: the emailed link proves the mailbox, so an address with
    /// an unconfirmed account (an unfinished sign-up, or one from before codes at sign-up) is confirmed here and takes the invitee's password;
    /// anything issued to it before ends.
    /// </summary>
    public async Task<AuthResponse> AcceptInviteAsync(string token, AcceptInviteRequest r, CancellationToken ct)
    {
        var invite = await FindInviteAsync(token, ct);
        if (invite.AcceptedAt is not null || invite.ExpiresAt <= clock.GetUtcNow())
            throw new BadRequestException("This invitation has expired or was already used. Ask for a new one.");
        if (!await db.Tenants.AnyAsync(t => t.Id == invite.TenantId && t.Status == TenantStatus.Active, ct)) throw Suspended();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await users.FindByEmailAsync(invite.Email);
        if (user is { EmailConfirmed: true })
        {
            if (await users.IsLockedOutAsync(user) || !await users.CheckPasswordAsync(user, r.Password))
            {
                if (!await users.IsLockedOutAsync(user)) await users.AccessFailedAsync(user);
                throw BadRequestException.Field("password", "That password is not right for this email.");
            }
        }
        else if (user is not null)
        {
            await SetProvenPasswordAsync(user, r.Password);
            if (!string.IsNullOrWhiteSpace(r.FullName)) user.FullName = r.FullName.Trim();
            await ConfirmEmailAsync(user);
            await EndEarlierSessionsAsync(user, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(r.FullName)) throw BadRequestException.Field("fullName", "Enter your name.");
            user = new AppUser { FullName = r.FullName.Trim(), Email = invite.Email, UserName = invite.Email, EmailConfirmed = true, EmailConfirmedAt = clock.GetUtcNow() };
            var created = await users.CreateAsync(user, r.Password);
            if (!created.Succeeded)
            {
                var errors = created.Errors.Select(e => e.Description).ToArray();
                throw new BadRequestException(errors.FirstOrDefault() ?? "Could not create the account.",
                    new Dictionary<string, string[]> { ["password"] = errors });
            }
        }

        tenant.SetTenant(invite.TenantId);
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == user.Id, ct);
        if (membership is null)
        {
            membership = new Membership { UserId = user.Id, Role = invite.Role, InvitedBy = invite.InvitedBy };
            db.Memberships.Add(membership);
        }
        membership.Status = MembershipStatus.Active;
        invite.AcceptedAt = clock.GetUtcNow();
        var t = await db.Tenants.FirstAsync(x => x.Id == invite.TenantId, ct);
        // A school member gets their staff profile as soon as they join.
        if (t.Product == Saas.Application.Billing.Products.School && !await db.StaffProfiles.AnyAsync(p => p.MembershipId == membership.Id, ct))
            db.StaffProfiles.Add(new Saas.Domain.School.StaffProfile
            {
                TenantId = t.Id, MembershipId = membership.Id, UserId = user.Id,
                JobTitle = Saas.Application.School.StaffService.DefaultJobTitle(membership.Role),
            });
        // A clinic member gets their staff profile, with the clinical role the invite carried, as soon as they join.
        if (t.Product == Saas.Application.Billing.Products.Care)
            await Saas.Application.Care.CareStaff.AddProfileOnJoinAsync(db, t.Id, membership, invite.ClinicalRole, ct);
        await db.SaveChangesAsync(ct);

        var response = await IssueAsync(user, t, membership.Role, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    private async Task<Invitation> FindInviteAsync(string token, CancellationToken ct)
    {
        var hash = TeamService.HashToken(token);
        return await db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, ct)
            ?? throw new NotFoundException("Invitation");
    }

    // Several businesses per user

    public async Task<IReadOnlyList<BusinessChoice>> BusinessesAsync(Guid userId, CancellationToken ct)
    {
        var currentId = tenant.TenantId ?? throw Unauthorized();
        var product = await db.Tenants.Where(t => t.Id == currentId).Select(t => t.Product).FirstAsync(ct);
        return await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active && m.Tenant!.Product == product
                && m.Tenant.Status == TenantStatus.Active)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new BusinessChoice(m.TenantId, m.Tenant!.Name, m.Role, m.TenantId == currentId))
            .ToListAsync(ct);
    }

    public async Task<AuthResponse> SwitchAsync(Guid userId, SwitchRequest r, CancellationToken ct)
    {
        var choices = await BusinessesAsync(userId, ct);
        if (choices.All(c => c.TenantId != r.TenantId)) throw new ForbiddenException("You are not a member of that business.");
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw Unauthorized();
        var membership = await db.Memberships.IgnoreQueryFilters().Include(m => m.Tenant)
            .FirstAsync(m => m.UserId == userId && m.TenantId == r.TenantId, ct);
        if (membership.Tenant!.Status != TenantStatus.Active) throw Suspended();
        return await IssueAsync(user, membership.Tenant!, membership.Role, ct);
    }

    /// <summary>
    /// Signs in to the user's business. <paramref name="beforeIssue"/> runs once a membership is found and before the session is issued
    /// (the code sign-in confirms the email and, on a first confirmation, takes the account over there).
    /// </summary>
    private async Task<AuthResponse> LoginUserAsync(AppUser user, string? product, CancellationToken ct, Func<Task>? beforeIssue = null)
    {
        var query = db.Memberships.IgnoreQueryFilters().Include(m => m.Tenant)
            .Where(m => m.UserId == user.Id && m.Status == MembershipStatus.Active && m.Tenant!.Status == TenantStatus.Active);
        if (!string.IsNullOrWhiteSpace(product))
        {
            var key = product.Trim().ToLowerInvariant();
            query = query.Where(m => m.Tenant!.Product == key);
        }
        var membership = await query.OrderBy(m => m.CreatedAt).FirstOrDefaultAsync(ct);
        if (membership is null && await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.UserId == user.Id && m.Status == MembershipStatus.Active
                && m.Tenant!.Status == TenantStatus.Suspended && (string.IsNullOrWhiteSpace(product) || m.Tenant.Product == product.Trim().ToLowerInvariant()), ct))
            throw Suspended();
        if (membership is null)
            throw new BadRequestException(string.IsNullOrWhiteSpace(product)
                ? "This account is not linked to an active business."
                : "You don't have an account for this product yet. Sign up with the same email to add it.");
        if (beforeIssue is not null) await beforeIssue();
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return await IssueAsync(user, membership.Tenant!, membership.Role, ct);
    }

    // Learners (courses): accounts with no business attached

    /// <summary>
    /// A customer sign-up (learners, parents, patients, home-seekers). Final fix pass B: no session; a 6-digit code is emailed at once and the
    /// account is usable once it is entered at the route's <c>/signup/confirm</c> (<see cref="ConfirmSignupAsync"/>). An address whose account
    /// was never confirmed gets the same answer and a new code (whoever enters it sets the password); a confirmed one is told to log in.
    /// </summary>
    public async Task<SignupPending> LearnerSignupAsync(LearnerSignupRequest r, string product, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (StudentLogins.IsStudentEmail(email)) throw BadRequestException.Field("email", "Enter a valid email address.");
        var user = await users.FindByEmailAsync(email);
        if (user is { EmailConfirmed: true })
            throw BadRequestException.Field("email", "You already have an account with this email. Log in instead.");
        user ??= await CreateUnconfirmedAsync(r.FullName, email, r.Password);
        if (await AddCodeAsync(user, email, product, ct)) await db.SaveChangesAsync(ct);
        return new SignupPending(email);
    }

    public async Task<AuthResponse> LearnerLoginAsync(LoginRequest r, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(r.Email.Trim().ToLowerInvariant());
        if (user is null || StudentLogins.IsStudentEmail(user.Email)) throw InvalidLogin(); // students sign in on their school's page
        await EnsureSignupConfirmedAsync(user);
        if (await users.IsLockedOutAsync(user))
            throw new BadRequestException("Too many failed attempts. Try again in a few minutes.");
        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            await users.AccessFailedAsync(user);
            throw InvalidLogin();
        }
        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, null, null, ct);
    }

    /// <summary>Signs a verified customer in (no business in the token). Used by the student sign-in, which checks the password itself.</summary>
    public async Task<AuthResponse> IssueCustomerAsync(AppUser user, CancellationToken ct)
    {
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return await IssueAsync(user, null, null, ct);
    }

    private static UserDto ToDto(AppUser user, PlatformRole? role) => new(user.Id, user.FullName, user.Email!, role is not null, role?.ToString());

    /// <summary>Issues tokens for a business (tenant) session, or a learner session when <paramref name="t"/> is null.</summary>
    private async Task<AuthResponse> IssueAsync(AppUser user, Tenant? t, MemberRole? role, CancellationToken ct, RefreshToken? replacing = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Name, user.FullName),
        };
        if (t is not null)
        {
            claims.Add(new Claim(AppClaims.Tenant, t.Id.ToString()));
            claims.Add(new Claim(AppClaims.Role, role!.Value.ToString()));
            claims.Add(new Claim(AppClaims.Product, t.Product));
        }
        // Customer sessions never carry admin rights; admins use the admin-panel sign-in (password + emailed code).
        var platformRole = await access.ActiveRoleAsync(user.Id, ct);
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_jwt.Key(), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims),
        });

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var stored = new RefreshToken
        {
            UserId = user.Id,
            TenantId = t?.Id ?? Guid.Empty,
            TokenHash = Codes.Sha256(refreshToken),
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        };
        db.RefreshTokens.Add(stored);
        if (replacing is not null) replacing.ReplacedById = stored.Id;
        await db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, refreshToken, expires, ToDto(user, platformRole),
            t is null ? null : new TenantDto(t.Id, t.Name, t.Slug, role!.Value, t.Product));
    }

    private async Task<string> AvailableSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slugs.From(name);
        if (baseSlug.Length < 3) baseSlug = $"{baseSlug}-store";
        var slug = baseSlug;
        while (!Slugs.IsValid(slug) || await SlugInUseAsync(slug, ct))
            slug = $"{baseSlug}-{Codes.Random(4).ToLowerInvariant()}";
        return slug;
    }

    /// <summary>
    /// Every product profile is created with the tenant's slug, but a store, booking business, academy or agency can later change its own,
    /// so a slug is free only when no tenant and none of those tables hold it.
    /// </summary>
    private async Task<bool> SlugInUseAsync(string slug, CancellationToken ct) =>
        await db.Tenants.AnyAsync(x => x.Slug == slug, ct)
        || await db.Stores.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug, ct)
        || await db.BookingBusinesses.IgnoreQueryFilters().AnyAsync(b => b.Slug == slug, ct)
        || await db.Academies.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug, ct)
        || await db.AgencyProfiles.IgnoreQueryFilters().AnyAsync(a => a.Slug == slug, ct)
        || await db.SchoolProfiles.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug, ct);

    /// <summary>The signup's link was taken at insert: schools chose it, so they're asked for another; everyone else changes the name it came from.</summary>
    internal static BadRequestException SlugTaken(SignupRequest r) => r.SlugKey is not null
        ? BadRequestException.Field("slug", "That link is taken. Choose another.")
        : BadRequestException.Field(r.BusinessName is not null || r.StoreName is null ? "businessName" : "storeName",
            "We couldn't set up your account with that business name. Try a slightly different name.");

    /// <summary>True when the unique index that failed is a slug index (tenants, stores, school profiles), not some other one.</summary>
    private static bool IsSlugIndex(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is Npgsql.PostgresException p) return p.ConstraintName?.Contains("slug", StringComparison.OrdinalIgnoreCase) == true;
        return false;
    }

    private static string HashOtp(string target, string code) => Codes.Sha256($"{target}:{code}");
    private static BadRequestException InvalidLogin() => new("Email or password is incorrect.");
    private static UnauthorizedException Unauthorized() => new();
    public static ForbiddenException Suspended() => new("This business is suspended. Contact support.");
}

public sealed class UnauthorizedException() : AppException("Your session has expired. Please log in again.", 401, "unauthorized");

/// <summary>Final fix pass B: password login for a sign-up whose emailed code was never entered (403 <c>email_unconfirmed</c>).</summary>
public sealed class EmailUnconfirmedException() : AppException(AuthService.ConfirmEmailFirst, 403, "email_unconfirmed");
