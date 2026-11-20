using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;

namespace Saas.Infrastructure.Notifications;

/// <summary>
/// Web Push (VAPID) settings, section <c>WebPush</c>. Push is on when both keys are set; they come from the environment
/// (WebPush__PublicKey, WebPush__PrivateKey), never from code. Generate a pair once with <c>npx web-push generate-vapid-keys</c>.
/// </summary>
public sealed class WebPushOptions
{
    public const string Section = "WebPush";
    /// <summary>The VAPID public key, base64url (65-byte uncompressed P-256 point). Browsers subscribe with it.</summary>
    public string? PublicKey { get; set; }
    /// <summary>The VAPID private key, base64url (32 bytes). Secret.</summary>
    public string? PrivateKey { get; set; }
    /// <summary>Who push services contact about this sender: a mailto: or https: URL.</summary>
    public string Subject { get; set; } = "mailto:support@brosync.ng";
    public bool DispatcherEnabled { get; set; } = true;

    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

    /// <summary>Checked at startup: half a key pair, or a malformed key, is a deploy mistake that should stop the API.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PublicKey) && string.IsNullOrWhiteSpace(PrivateKey)) return;
        if (!Enabled) throw new InvalidOperationException("WebPush: set both PublicKey and PrivateKey, or neither.");
        if (WebPushRules.FromBase64Url(PublicKey) is not { Length: 65 } pub || pub[0] != 0x04)
            throw new InvalidOperationException("WebPush:PublicKey must be a base64url P-256 public key (65 bytes).");
        if (WebPushRules.FromBase64Url(PrivateKey) is not { Length: 32 })
            throw new InvalidOperationException("WebPush:PrivateKey must be a base64url P-256 private key (32 bytes).");
        if (!Subject.StartsWith("mailto:", StringComparison.Ordinal) && !Subject.StartsWith("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("WebPush:Subject must be a mailto: or https: URL.");
    }
}

/// <summary>Sends through the browser's push service with Lib.Net.Http.WebPush (RFC 8291 payload encryption, RFC 8292 VAPID).</summary>
public sealed class WebPushSender : IWebPushSender
{
    /// <summary>A notification older than a day is not worth showing.</summary>
    private const int TimeToLiveSeconds = 24 * 60 * 60;
    private readonly PushServiceClient client;

    public WebPushSender(HttpClient http, WebPushOptions options)
    {
        http.Timeout = TimeSpan.FromSeconds(10);
        client = new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(options.PublicKey!.Trim(), options.PrivateKey!.Trim()) { Subject = options.Subject },
            DefaultTimeToLive = TimeToLiveSeconds,
            AutoRetryAfter = false,
        };
    }

    public async Task<PushSendResult> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct)
    {
        var subscription = new PushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);
        var message = new PushMessage(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web))
        {
            TimeToLive = TimeToLiveSeconds,
            Urgency = PushMessageUrgency.Normal,
        };
        try
        {
            await client.RequestPushMessageDeliveryAsync(subscription, message, ct);
            return PushSendResult.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushSendResult.Gone;
        }
    }
}

/// <summary>
/// Sends the push outbox (<see cref="WebPushMessage"/>) every few seconds, after the events that queued it have committed. A message
/// goes to every subscription the user has for its product; one with none is skipped. Subscriptions the push service says are gone
/// are deleted, as are ones that keep failing. Sent and skipped rows are purged after a few days.
/// </summary>
public sealed class PushDispatcher(IServiceScopeFactory scopes, ILogger<PushDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 50;
    private const int MaxAttempts = 5;
    private const int MaxSubscriptionFailures = 10;
    /// <summary>A message still unsent after this is dropped: a late "new order" push is noise.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    public static readonly TimeSpan Retention = TimeSpan.FromDays(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var nextPurge = DateTimeOffset.MinValue;
        do
        {
            try
            {
                // Another batch straight away only after a full batch all finished; failures wait for the next tick.
                await Jobs.JobRuns.TrackAsync(scopes, "push", async () => { while (await SendBatchAsync(stoppingToken) == BatchSize) { } }, stoppingToken);
                if (DateTimeOffset.UtcNow >= nextPurge)
                {
                    await PurgeAsync(DateTimeOffset.UtcNow, stoppingToken);
                    nextPurge = DateTimeOffset.UtcNow.AddHours(1);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Push dispatch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Sends one batch; returns how many messages were finished (sent or skipped).</summary>
    public async Task<int> SendBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.WebPushMessages
            .FromSql($"SELECT * FROM web_push_messages WHERE status = 'Pending' ORDER BY created_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
        if (batch.Count == 0) return 0;

        var userIds = batch.Select(m => m.UserId).Distinct().ToList();
        var subscriptions = await db.WebPushSubscriptions.Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);
        var removed = new HashSet<Guid>();
        var finished = 0;
        foreach (var message in batch)
        {
            var targets = subscriptions.Where(s => s.UserId == message.UserId && s.Product == message.Product && !removed.Contains(s.Id)).ToList();
            if (targets.Count == 0 || now - message.CreatedAt > MaxAge)
            {
                message.Status = WebPushStatus.Skipped;
                message.Error = targets.Count == 0 ? null : "Too old to send.";
                finished++;
                continue;
            }

            var payload = new PushPayload(message.Title, message.Body, message.Link, message.Id.ToString("N"));
            int sent = 0, failed = 0;
            string? error = null;
            foreach (var target in targets)
            {
                PushSendResult result;
                try
                {
                    result = await sender.SendAsync(new PushTarget(target.Endpoint, target.P256dh, target.Auth), payload, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Push {Id} to subscription {Subscription} failed", message.Id, target.Id);
                    error = ex.Message;
                    result = PushSendResult.Failed;
                }
                switch (result)
                {
                    case PushSendResult.Sent:
                        sent++;
                        target.LastSentAt = now;
                        target.Failures = 0;
                        break;
                    case PushSendResult.Gone:
                        db.WebPushSubscriptions.Remove(target);
                        removed.Add(target.Id);
                        break;
                    default:
                        failed++;
                        if (++target.Failures >= MaxSubscriptionFailures)
                        {
                            db.WebPushSubscriptions.Remove(target);
                            removed.Add(target.Id);
                        }
                        break;
                }
            }

            if (sent > 0 || failed == 0)
            {
                message.Status = sent > 0 ? WebPushStatus.Sent : WebPushStatus.Skipped;
                message.SentAt = sent > 0 ? now : null;
                message.Error = null;
                finished++;
            }
            else
            {
                message.Attempts++;
                message.Error = error is null ? "The push service refused it." : error.Length > 500 ? error[..500] : error;
                if (message.Attempts >= MaxAttempts) message.Status = WebPushStatus.Failed;
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return finished;
    }

    /// <summary>Deletes outbox rows older than <see cref="Retention"/>, whatever their status.</summary>
    public async Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = now - Retention;
        return await db.WebPushMessages.Where(m => m.CreatedAt < before).ExecuteDeleteAsync(ct);
    }
}
