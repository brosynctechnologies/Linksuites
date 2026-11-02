using Saas.Domain.Common;

namespace Saas.Domain.Core;

public class StoredFile : TenantEntity
{
    public Guid? OwnerId { get; set; }
    public required string Key { get; set; }
    public required string Url { get; set; }
    public required string MimeType { get; set; }
    public long SizeBytes { get; set; }
    public required string Purpose { get; set; }
}

/// <summary>Outbox row: written in the same transaction as the business change, sent by a background worker.</summary>
public class Notification : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Recipient { get; set; }
    public required string TemplateKey { get; set; }
    public Dictionary<string, string> Payload { get; set; } = [];
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

public enum NotificationChannel { Email, Sms, WhatsApp, InApp }
public enum NotificationStatus { Pending, Sent, Failed }

public class AuditLog
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? ActorId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Ip { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OtpCode : Entity
{
    public required string Target { get; set; }
    public required string CodeHash { get; set; }
    public required string Purpose { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public int Attempts { get; set; }
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
}
