using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.Application.Common;

/// <summary>Whether Web Push is configured (VAPID keys set) and the public key browsers subscribe with. Registered by Infrastructure.</summary>
public sealed record PushSettings(bool Enabled, string? PublicKey)
{
    public static readonly PushSettings Disabled = new(false, null);
}

/// <summary>One browser subscription to send to.</summary>
public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

/// <summary>What the service worker shows: a title, a body and the app path the notification opens. <c>Tag</c> lets a resend replace.</summary>
public sealed record PushPayload(string Title, string Body, string Link, string Tag);

/// <summary>Gone: the push service says the subscription no longer exists (404/410); it is deleted.</summary>
public enum PushSendResult { Sent, Gone, Failed }

/// <summary>Sends one encrypted, VAPID-signed Web Push message. The real one is in Infrastructure; tests use a fake.</summary>
public interface IWebPushSender
{
    Task<PushSendResult> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct);
}

public static class WebPushRules
{
    public const int MaxEndpoint = 1000;
    /// <summary>Each user keeps at most this many browsers; subscribing another drops the oldest.</summary>
    public const int MaxPerUser = 10;
    /// <summary>
    /// Push services browsers use (Chrome/Android FCM, Firefox, Safari, Edge). The API makes a request to the endpoint, so only these
    /// hosts are accepted: an arbitrary URL would let a signed-in user make the server call any address.
    /// </summary>
    private static readonly string[] Hosts = ["fcm.googleapis.com", "updates.push.services.mozilla.com", "web.push.apple.com"];
    private static readonly string[] HostSuffixes = [".push.services.mozilla.com", ".notify.windows.com", ".push.apple.com"];

    public static bool IsPushServiceHost(string host)
    {
        host = host.ToLowerInvariant();
        return Hosts.Contains(host) || HostSuffixes.Any(host.EndsWith);
    }

    /// <summary>Health details stay off lock screens: a CareLink push says only that there is something to see.</summary>
    public const string CareBody = "Open CareLink to see the details.";

    public static byte[]? FromBase64Url(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200) return null;
        var s = value.Trim().Replace('-', '+').Replace('_', '/').TrimEnd('=');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}

public sealed record PushKeysRequest(string? P256dh, string? Auth);
public sealed record SubscribePushRequest(string? Product, string? Endpoint, PushKeysRequest? Keys);
public sealed record RemovePushRequest(string? Endpoint);
public sealed record PushSubscriptionDto(Guid Id, string Product, string Endpoint, DateTimeOffset CreatedAt, DateTimeOffset? LastSentAt);
public sealed record PushStatusDto(bool Enabled, string? PublicKey, IReadOnlyList<PushSubscriptionDto> Subscriptions);

/// <summary>
/// The signed-in user's Web Push subscriptions (/api/me/push): the VAPID public key, subscribe and remove. A subscription is the
/// user's own whatever business or portal they are in; support's view-as tokens can't subscribe a device to someone else's account.
/// </summary>
public sealed class PushSubscriptions(IAppDbContext db, ICurrentUser user, PushSettings settings)
{
    private Guid Me => user.UserId ?? throw new ForbiddenException("Sign in to manage notifications.");

    public async Task<PushStatusDto> GetAsync(CancellationToken ct)
    {
        var me = Me;
        var subs = await db.WebPushSubscriptions.AsNoTracking().Where(s => s.UserId == me).OrderByDescending(s => s.CreatedAt)
            .Select(s => new PushSubscriptionDto(s.Id, s.Product, s.Endpoint, s.CreatedAt, s.LastSentAt)).ToListAsync(ct);
        return new PushStatusDto(settings.Enabled, settings.PublicKey, subs);
    }

    public async Task<PushSubscriptionDto> SubscribeAsync(SubscribePushRequest r, CancellationToken ct)
    {
        var me = Me;
        if (user.IsSupportView) throw new ForbiddenException("A support view can't turn on notifications.");
        if (!settings.Enabled) throw new ServiceUnavailableException("Push notifications aren't set up yet.");

        var product = r.Product?.Trim().ToLowerInvariant() ?? "";
        if (!Products.All.Contains(product)) throw BadRequestException.Field("product", "Choose a product.");
        var endpoint = r.Endpoint?.Trim() ?? "";
        if (endpoint.Length is 0 or > WebPushRules.MaxEndpoint
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !WebPushRules.IsPushServiceHost(uri.Host))
            throw BadRequestException.Field("endpoint", "This browser's push address isn't one we can send to.");
        var p256dh = WebPushRules.FromBase64Url(r.Keys?.P256dh);
        if (p256dh is not { Length: 65 } || p256dh[0] != 0x04)
            throw BadRequestException.Field("keys.p256dh", "The browser's push key is not valid.");
        var auth = WebPushRules.FromBase64Url(r.Keys?.Auth);
        if (auth is not { Length: 16 }) throw BadRequestException.Field("keys.auth", "The browser's push secret is not valid.");

        var row = await db.WebPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (row is null)
        {
            // Keep the newest few browsers per user.
            var extra = await db.WebPushSubscriptions.Where(s => s.UserId == me).OrderByDescending(s => s.CreatedAt)
                .Skip(WebPushRules.MaxPerUser - 1).ToListAsync(ct);
            db.WebPushSubscriptions.RemoveRange(extra);
            row = new WebPushSubscription { UserId = me, Product = product, Endpoint = endpoint, P256dh = r.Keys!.P256dh!.Trim(), Auth = r.Keys.Auth!.Trim() };
            db.WebPushSubscriptions.Add(row);
        }
        else
        {
            // The same browser: it now belongs to whoever turned push on last.
            row.UserId = me;
            row.Product = product;
            row.P256dh = r.Keys!.P256dh!.Trim();
            row.Auth = r.Keys.Auth!.Trim();
            row.Failures = 0;
        }
        await db.SaveChangesAsync(ct);
        return new PushSubscriptionDto(row.Id, row.Product, row.Endpoint, row.CreatedAt, row.LastSentAt);
    }

    /// <summary>Removes one of the user's own subscriptions by its endpoint. Another user's looks like it does not exist.</summary>
    public async Task RemoveAsync(RemovePushRequest r, CancellationToken ct)
    {
        var me = Me;
        var endpoint = r.Endpoint?.Trim() ?? "";
        var row = endpoint.Length is 0 or > WebPushRules.MaxEndpoint ? null
            : await db.WebPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.UserId == me, ct);
        if (row is null) throw new NotFoundException("Push subscription");
        db.WebPushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
