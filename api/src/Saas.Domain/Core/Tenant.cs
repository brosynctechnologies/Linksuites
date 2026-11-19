using Microsoft.AspNetCore.Identity;
using Saas.Domain.Common;

namespace Saas.Domain.Core;

public class Tenant : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    /// <summary>OrderLink (Wave 3): the store's own domain, lower case (shop.example.com). Served only while the plan includes custom_domain.</summary>
    public string? CustomDomain { get; set; }
    public CustomDomainStatus? CustomDomainStatus { get; set; }
    /// <summary>Why the domain is still pending or failed, as the owner reads it. Null when all is well.</summary>
    public string? CustomDomainError { get; set; }
    public DateTimeOffset? CustomDomainCheckedAt { get; set; }
    public DateTimeOffset? CustomDomainVerifiedAt { get; set; }
    /// <summary>When the host script reported the certificate issued and the site configured.</summary>
    public DateTimeOffset? CustomDomainLiveAt { get; set; }
    /// <summary>The host script's last certificate failure; the domain isn't handed out for another try until an hour after it.</summary>
    public DateTimeOffset? CustomDomainCertFailedAt { get; set; }
    public string? LogoUrl { get; set; }
    public string? BrandColor { get; set; }
    public string Country { get; set; } = "NG";
    public string Currency { get; set; } = "NGN";
    public string Timezone { get; set; } = "Africa/Lagos";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    /// <summary>Which product this tenant signed up for, e.g. "whatsapp-orders".</summary>
    public required string Product { get; set; }
}

public enum TenantStatus { Active, Suspended, Closed }

/// <summary>
/// A custom domain's progress: <c>PendingDns</c> until its DNS points at LinkSuite, <c>Verified</c> once it does (the host script then
/// issues a certificate), <c>Live</c> once the host reports it served, <c>Failed</c> when the DNS points elsewhere or the certificate failed.
/// </summary>
public enum CustomDomainStatus { PendingDns, Verified, Live, Failed }

public class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    public required string FullName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the email was first confirmed (an emailed code, or a team invite to the address). Access tokens issued before it no
    /// longer reach the patient portal (Task 15 fix round 2, R1). Null for accounts confirmed before this was recorded.</summary>
    public DateTimeOffset? EmailConfirmedAt { get; set; }

    /// <summary>Admin console sign-in failures, counted apart from customer sign-in so neither can lock the other out.</summary>
    public int ConsoleFailedCount { get; set; }
    public DateTimeOffset? ConsoleLockoutEnd { get; set; }
}

[Audited]
public class Membership : TenantEntity
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Tenant? Tenant { get; set; }
    public MemberRole Role { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public Guid? InvitedBy { get; set; }
}

/// <summary>Accountant: read-only access to finances plus reports and exports.</summary>
public enum MemberRole { Owner, Admin, Staff, Accountant }
public enum MembershipStatus { Invited, Active, Disabled }

public class RolePermission : TenantEntity
{
    public MemberRole Role { get; set; }
    public required string PermissionKey { get; set; }
}

/// <summary>An emailed invitation to join a business. Only a hash of the token is stored.</summary>
public class Invitation : TenantEntity
{
    public required string Email { get; set; }
    public MemberRole Role { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? InvitedBy { get; set; }
    /// <summary>CareLink only: the clinical role the invitee's staff profile starts with when they accept. Null elsewhere.</summary>
    public Saas.Domain.Care.ClinicalRole? ClinicalRole { get; set; }
}

/// <summary>Daily rates from the provider, all quoted against USD; cross rates are derived.</summary>
public class ExchangeRate
{
    public long Id { get; set; }
    public DateOnly AsOf { get; set; }
    public required string Currency { get; set; }
    /// <summary>Units of <see cref="Currency"/> per 1 USD.</summary>
    public decimal PerUsd { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
