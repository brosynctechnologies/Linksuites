using Saas.Domain.Common;

namespace Saas.Domain.Core;

public enum PlatformRole { SuperAdmin, Moderator, Finance, Support }

/// <summary>Someone who runs the platform. Not tenant-owned. A removed admin keeps their row (with <see cref="RemovedAt"/>) for the record.</summary>
[Audited]
public class PlatformAdmin : Entity
{
    public Guid UserId { get; set; }
    public PlatformRole Role { get; set; }
    public Guid? AddedBy { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }
}

/// <summary>The last run of each background job, for the admin health page.</summary>
public class JobRun
{
    public required string Name { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public string? LastError { get; set; }
}

public enum AuthSessionKind { AdminPanel, SupportView }

/// <summary>
/// A server-side record of an admin-panel or support-view token (keyed by the token's id), so either can be ended before it
/// expires: sign-out, "end view", "sign out everywhere", password reset or admin removal.
/// </summary>
public class AuthSession : Entity
{
    public Guid UserId { get; set; }
    public AuthSessionKind Kind { get; set; }
    public Guid? TenantId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public Guid? EndedBy { get; set; }
}
