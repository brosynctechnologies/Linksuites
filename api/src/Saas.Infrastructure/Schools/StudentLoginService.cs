using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.School;
using Saas.Domain.Core;
using Saas.Domain.School;
using Saas.Infrastructure.Auth;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Schools;

public sealed record SetStudentPasswordRequest(string? Password);
public sealed record StudentLoginRequest(string? AdmissionNo, string? Password);
public sealed record StudentLoginStatusDto(Guid StudentId, string AdmissionNo, bool HasLogin);

/// <summary>
/// Student accounts for levels with logins (JSS, SSS). The school sets and resets the password; the student signs in on the school's page
/// with the admission number. The account is an ordinary customer account with a synthetic email that is never delivered to.
/// </summary>
public sealed class StudentLoginService(AppDbContext db, UserManager<AppUser> users, AuthService auth, ITenantContext tenant, ICurrentUser current, TimeProvider clock)
{
    private const string Wrong = "Admission number or password is incorrect.";

    /// <summary>Creates the student's account, or resets its password. Ends the student's sessions and clears any lockout.</summary>
    public async Task<StudentLoginStatusDto> SetPasswordAsync(Guid studentId, SetStudentPasswordRequest r, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student");

        var hasLogins = await db.Enrolments.Where(e => e.StudentId == studentId && db.AcademicSessions.Any(s => s.Id == e.SessionId && s.IsCurrent))
            .Join(db.SchoolClasses, e => e.ClassId, c => c.Id, (e, c) => c.LevelId)
            .Join(db.Levels, levelId => levelId, l => l.Id, (levelId, l) => l.HasStudentLogins)
            .FirstOrDefaultAsync(ct);
        if (!hasLogins)
            throw BadRequestException.Field("level", "Students in this level don't sign in. Logins are for levels such as JSS and SSS, which you can switch on under Levels.");
        if (student.Status != StudentStatus.Active)
            throw new ConflictException("Only active students can have a login. Set the student back to active first.", "student_not_active");
        var password = r.Password ?? "";
        if (password.Length < StudentLogins.MinPasswordLength)
            throw BadRequestException.Field("password", $"The password must have at least {StudentLogins.MinPasswordLength} characters.");

        var slug = await db.SchoolProfiles.Select(p => p.Slug).FirstAsync(ct);
        var fullName = ParentAccessName(student);
        var user = student.UserId is { } existingId ? await users.FindByIdAsync(existingId.ToString()) : null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (user is null)
        {
            var email = StudentLogins.EmailFor(student.AdmissionNo, slug);
            // A customer who signed up with this address first must not take over the student's account.
            if (await users.FindByEmailAsync(email) is not null)
                throw new ConflictException("This admission number can't get a login. Change the admission number or contact support.", "student_login_taken");
            user = new AppUser { FullName = fullName, Email = email, UserName = email, EmailConfirmed = false };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) throw PasswordErrors(created);
            student.UserId = user.Id;
        }
        else
        {
            user.FullName = fullName;
            var reset = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password);
            if (!reset.Succeeded) throw PasswordErrors(reset);
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
        }

        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId, ActorId = current.UserId, Action = "student.login_password_set", EntityType = "Student", EntityId = student.Id.ToString(),
            Ip = current.IpAddress, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new StudentLoginStatusDto(student.Id, student.AdmissionNo, true);
    }

    /// <summary>
    /// Signs a student in with the school's slug, their admission number and password. A wrong school, an unknown admission number and a
    /// wrong password all answer the same way. Five wrong passwords lock the account for 10 minutes (the shared Identity lockout).
    /// </summary>
    public async Task<AuthResponse> LoginAsync(string slug, StudentLoginRequest r, CancellationToken ct)
    {
        var key = (slug ?? "").Trim().ToLowerInvariant();
        var admissionNo = (r.AdmissionNo ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0 || admissionNo.Length == 0 || string.IsNullOrEmpty(r.Password)) throw new BadRequestException(Wrong);

        var school = await (from p in db.SchoolProfiles.IgnoreQueryFilters()
                            where p.Slug == key
                            join t in db.Tenants on p.TenantId equals t.Id
                            where t.Product == Products.School
                            select new { t.Id, t.Status }).FirstOrDefaultAsync(ct)
            ?? throw new BadRequestException(Wrong);
        if (school.Status != TenantStatus.Active) throw new ForbiddenException(ParentAccess.PortalUnavailable);

        var student = await db.Students.IgnoreQueryFilters()
            .Where(s => s.TenantId == school.Id && s.AdmissionNo.ToLower() == admissionNo && s.UserId != null)
            .FirstOrDefaultAsync(ct);
        var user = student?.UserId is { } userId ? await users.FindByIdAsync(userId.ToString()) : null;
        if (student is null || user is null) throw new BadRequestException(Wrong);

        if (await users.IsLockedOutAsync(user))
            throw new BadRequestException("Too many failed attempts. Try again in 10 minutes, or ask the school office to reset your password.");
        if (!await users.CheckPasswordAsync(user, r.Password))
        {
            await users.AccessFailedAsync(user);
            throw new BadRequestException(Wrong);
        }
        await users.ResetAccessFailedCountAsync(user);
        if (student.Status != StudentStatus.Active) throw new ForbiddenException(StudentAccess.NotActive);
        return await auth.IssueCustomerAsync(user, ct);
    }

    private static string ParentAccessName(Student s) =>
        string.Join(' ', new[] { s.FirstName, s.MiddleName, s.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    private static BadRequestException PasswordErrors(IdentityResult result)
    {
        var errors = result.Errors.Select(e => e.Description).ToArray();
        return new BadRequestException(errors.FirstOrDefault() ?? "Choose a different password.", new Dictionary<string, string[]> { ["password"] = errors });
    }
}
