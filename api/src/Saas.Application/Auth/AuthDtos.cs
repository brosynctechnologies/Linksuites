using FluentValidation;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Auth;

/// <summary>
/// One signup for every product. <c>Product</c> defaults to the WhatsApp Order System; each product
/// reads the fields it needs (orders: <c>StoreName</c> + <c>WhatsappNumber</c>, invoices: <c>BusinessName</c>).
/// </summary>
public sealed record SignupRequest(
    string FullName, string Email, string Password, string Country,
    string? Product = null, string? BusinessName = null, string? StoreName = null, string? WhatsappNumber = null,
    string? AccountKind = null, string? Slug = null)
{
    /// <summary>The link the business asked for, if any (otherwise one is made from the name).</summary>
    public string? SlugKey => string.IsNullOrWhiteSpace(Slug) ? null : Slug.Trim().ToLowerInvariant();
    public string ProductKey => string.IsNullOrWhiteSpace(Product) ? Products.WhatsAppOrders : Product.Trim().ToLowerInvariant();
    public string Name => (BusinessName ?? StoreName ?? "").Trim();
}

public sealed record LoginRequest(string Email, string Password, string? Product = null);
public sealed record RefreshRequest(string RefreshToken);
/// <param name="Product">The product of the page asking (optional): the code email speaks its name. Without it, the account's own product.</param>
public sealed record OtpRequest(string Email, string? Product = null);
public sealed record OtpVerifyRequest(string Email, string Code, string? Product = null);

public sealed record UserDto(Guid Id, string FullName, string Email, bool IsPlatformAdmin = false, string? PlatformRole = null);
public sealed record TenantDto(Guid Id, string Name, string Slug, MemberRole Role, string Product);
/// <summary><c>Tenant</c> is null for learner sessions (course students with no business).</summary>
public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, UserDto User, TenantDto? Tenant);
public sealed record MeResponse(UserDto User, TenantDto? Tenant);
public sealed record LearnerSignupRequest(string FullName, string Email, string Password);

/// <summary>
/// Final fix pass B (user decision I3): a password sign-up's answer until the emailed 6-digit code is entered. No session is issued; the
/// web asks for the code and posts it, with the same password, to the sign-up's <c>/confirm</c> route.
/// </summary>
public sealed record SignupPending(string Email, bool ConfirmationRequired = true);

/// <summary>A business sign-up's answer: a session (a confirmed account adding a product with its password) or <see cref="SignupPending"/>.</summary>
public sealed record SignupOutcome(AuthResponse? Session, SignupPending? Pending);

/// <summary>
/// Finishes a password sign-up: the emailed code proves the mailbox, and whoever proves it sets the password (the one typed at sign-up, or
/// at the login page). <c>Product</c> picks the business on the business route.
/// </summary>
public sealed record ConfirmSignupRequest(string Email, string Code, string Password, string? Product = null);

public sealed class ConfirmSignupRequestValidator : AbstractValidator<ConfirmSignupRequest>
{
    public ConfirmSignupRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Enter the 6-digit code.");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

/// <summary>Emails of the people who run the platform (course reviews, payouts). From configuration.</summary>
public sealed class PlatformOptions
{
    public List<string> AdminEmails { get; set; } = [];
    public bool IsAdmin(string? email) =>
        email is not null && AdminEmails.Any(a => string.Equals(a.Trim(), email, StringComparison.OrdinalIgnoreCase));
}

public sealed class LearnerSignupRequestValidator : AbstractValidator<LearnerSignupRequest>
{
    public LearnerSignupRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
    }
}

/// <summary>A team invite as the accept page shows it. <c>RoleLabel</c> is the clinic role for a CareLink clinic ("Nurse"), else null.</summary>
public sealed record InvitePreview(string BusinessName, string Email, MemberRole Role, bool HasAccount, bool Expired, string? RoleLabel = null);
public sealed record AcceptInviteRequest(string? FullName, string Password);
public sealed record BusinessChoice(Guid TenantId, string Name, MemberRole Role, bool Current);
public sealed record SwitchRequest(Guid TenantId);

/// <summary>Creates a product's starting data (a store, a business profile, ...) when a tenant signs up.</summary>
public interface IProductProvisioner
{
    string Product { get; }
    Task ProvisionAsync(Tenant tenant, SignupRequest request, Guid ownerUserId, CancellationToken ct);
}

public sealed class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    public SignupRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.Country).Must(Countries.IsSupported).WithMessage("Choose a supported country.");
        RuleFor(x => x.ProductKey).Must(Products.All.Contains).WithName("product").WithMessage("Unknown product.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).WithName("businessName").WithMessage("Enter your business name.");
        RuleFor(x => x.StoreName).NotEmpty().MaximumLength(80).When(x => x.ProductKey == Products.WhatsAppOrders);
        RuleFor(x => x.WhatsappNumber).NotEmpty().MaximumLength(30).When(x => x.ProductKey == Products.WhatsAppOrders);
        // Only a school picks its link at signup (its sign-in page lives at /{slug}); other products derive theirs from the name.
        RuleFor(x => x.SlugKey).Null().When(x => x.ProductKey != Products.School).OverridePropertyName("slug")
            .WithMessage("Leave the link out. It is made from your business name.");
        RuleFor(x => x.SlugKey).Must(s => Common.Slugs.IsValid(s!)).When(x => x.SlugKey is not null && x.ProductKey == Products.School)
            .OverridePropertyName("slug").WithMessage(Common.Slugs.Rule);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class AcceptInviteRequestValidator : AbstractValidator<AcceptInviteRequest>
{
    public AcceptInviteRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);
        RuleFor(x => x.FullName).MaximumLength(100);
    }
}

public sealed class OtpRequestValidator : AbstractValidator<OtpRequest>
{
    public OtpRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
}

public sealed class OtpVerifyRequestValidator : AbstractValidator<OtpVerifyRequest>
{
    public OtpVerifyRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Matches("^[0-9]{6}$").WithMessage("Enter the 6-digit code.");
    }
}
