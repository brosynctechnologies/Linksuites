using Saas.Domain.Common;

namespace Saas.Domain.Core;

/// <summary>
/// Wave 4 (PWA): one browser's Web Push subscription for one signed-in user in one product's web app. A browser endpoint is unique: when
/// another account turns push on in the same browser, the row moves to that account, so a shared phone only ever gets the last
/// account's notifications.
/// </summary>
public class WebPushSubscription : Entity
{
    public Guid UserId { get; set; }
    /// <summary>The product whose web app subscribed (Products codes: "school", "care", "whatsapp-orders"...).</summary>
    public required string Product { get; set; }
    /// <summary>The push service URL (FCM, Mozilla, Apple, Windows). Only known push service hosts are accepted.</summary>
    public required string Endpoint { get; set; }
    /// <summary>The browser's P-256 public key (base64url), used to encrypt the payload.</summary>
    public required string P256dh { get; set; }
    /// <summary>The browser's auth secret (base64url).</summary>
    public required string Auth { get; set; }
    public DateTimeOffset? LastSentAt { get; set; }
    /// <summary>Failed sends in a row; the subscription is dropped after too many.</summary>
    public int Failures { get; set; }
}

/// <summary>
/// The push outbox: one notification for one user and product, sent to each of that user's subscriptions for the product by the push
/// dispatcher after the event's transaction commits. Queued only when Web Push is configured; old rows are purged.
/// </summary>
public class WebPushMessage : Entity
{
    public Guid UserId { get; set; }
    public required string Product { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    /// <summary>A path inside the product's web app, opened when the notification is tapped.</summary>
    public required string Link { get; set; }
    public WebPushStatus Status { get; set; } = WebPushStatus.Pending;
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>Skipped: the user had no subscription for the product, or the message went stale before it could be sent.</summary>
public enum WebPushStatus { Pending, Sent, Skipped, Failed }
