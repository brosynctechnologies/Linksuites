using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Infrastructure.Notifications;
using Saas.Infrastructure.Persistence;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 4 (PWA): Web Push subscriptions (/api/me/push) and the push outbox. The suite's own host has no VAPID keys (push off); these
/// tests use a second host on the same database with keys set, the dispatcher run by hand and a fake push service.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class WebPushTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static readonly object HostLock = new();
    private static (ApiFixture Fixture, WebApplicationFactory<Program> Host)? pushHost;
    private static readonly FakeWebPushSender Sender = new();
    private static readonly (string Public, string Private) Vapid = NewVapidKeys();

    private WebApplicationFactory<Program> Host
    {
        get
        {
            lock (HostLock)
            {
                if (pushHost is { } existing && ReferenceEquals(existing.Fixture, api)) return existing.Host;
                var host = api.WithWebHostBuilder(b =>
                {
                    b.UseSetting("WebPush:PublicKey", Vapid.Public);
                    b.UseSetting("WebPush:PrivateKey", Vapid.Private);
                    b.UseSetting("WebPush:DispatcherEnabled", "false"); // run by hand below
                    b.ConfigureTestServices(s => s.AddSingleton<IWebPushSender>(Sender));
                });
                pushHost = (api, host);
                return host;
            }
        }
    }

    private static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (string Public, string Private) NewVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        return (B64Url([0x04, .. p.Q.X!, .. p.Q.Y!]), B64Url(p.D!));
    }

    /// <summary>A browser subscription as PushSubscription.toJSON() gives it.</summary>
    private static object Subscription(string product, string? endpoint = null)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = key.ExportParameters(false).Q;
        return new
        {
            product,
            endpoint = endpoint ?? $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}",
            keys = new { p256dh = B64Url([0x04, .. q.X!, .. q.Y!]), auth = B64Url(RandomNumberGenerator.GetBytes(16)) },
        };
    }

    private static string EndpointOf(object subscription) => JsonSerializer.SerializeToElement(subscription).GetProperty("endpoint").GetString()!;

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<List<string>> EndpointsAsync(HttpClient client) =>
        (await Json(await client.GetAsync("/api/me/push", Ct))).GetProperty("subscriptions").EnumerateArray()
            .Select(s => s.GetProperty("endpoint").GetString()!).ToList();

    private async Task<(HttpClient Client, Guid UserId)> ParentAsync()
    {
        var (parent, parentId, _) = await kit.ParentAccountAsync();
        return (ApiFixture.On(Host, parent), parentId);
    }

    private Task<int> DispatchAsync() => ActivatorUtilities.CreateInstance<PushDispatcher>(Host.Services).SendBatchAsync(Ct);

    private async Task AddNotificationAsync(Guid userId, string product, string title, string body, string link)
    {
        using var scope = Host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<InAppNotifications>().Add(userId, product, "test.push", title, body, link);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Push_endpoints_need_a_signed_in_user()
    {
        var anonymous = Host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me/push", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/me/push/subscriptions", Subscription("school"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/me/push/subscriptions/remove", new { endpoint = "https://fcm.googleapis.com/fcm/send/x" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Without_vapid_keys_push_is_off_and_nothing_is_queued()
    {
        var (parent, parentId, _) = await kit.ParentAccountAsync();
        var status = await Json(await parent.GetAsync("/api/me/push", Ct));
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("publicKey").ValueKind);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await parent.PostAsJsonAsync("/api/me/push/subscriptions", Subscription("school"), Ct)).StatusCode);

        using (var scope = api.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<InAppNotifications>().Add(parentId, "school", "test.push", "Not pushed", "Body", "/parent");
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(Ct);
        }
        Assert.Equal(0, await api.WithDbAsync(db => db.WebPushMessages.CountAsync(m => m.UserId == parentId, Ct)));
    }

    [Fact]
    public async Task Subscriptions_are_per_user_and_removable_only_by_their_owner()
    {
        var (a, _) = await ParentAsync();
        var (b, _) = await ParentAsync();

        var status = await Json(await a.GetAsync("/api/me/push", Ct));
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(Vapid.Public, status.GetProperty("publicKey").GetString());

        var sub = Subscription("school");
        var saved = await Json(await a.PostAsJsonAsync("/api/me/push/subscriptions", sub, Ct));
        Assert.Equal("school", saved.GetProperty("product").GetString());
        Assert.Equal([EndpointOf(sub)], await EndpointsAsync(a));
        Assert.Empty(await EndpointsAsync(b));

        // Saving the same browser again is idempotent.
        await Json(await a.PostAsJsonAsync("/api/me/push/subscriptions", sub, Ct));
        Assert.Single(await EndpointsAsync(a));

        // Another user can't remove it: it looks like it does not exist.
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/me/push/subscriptions/remove", new { endpoint = EndpointOf(sub) }, Ct)).StatusCode);
        Assert.Single(await EndpointsAsync(a));

        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsJsonAsync("/api/me/push/subscriptions/remove", new { endpoint = EndpointOf(sub) }, Ct)).StatusCode);
        Assert.Empty(await EndpointsAsync(a));
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync("/api/me/push/subscriptions/remove", new { endpoint = EndpointOf(sub) }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_browser_belongs_to_the_last_user_who_turned_push_on()
    {
        var (a, _) = await ParentAsync();
        var (b, _) = await ParentAsync();
        var sub = Subscription("school");
        await Json(await a.PostAsJsonAsync("/api/me/push/subscriptions", sub, Ct));
        await Json(await b.PostAsJsonAsync("/api/me/push/subscriptions", sub, Ct));
        Assert.Empty(await EndpointsAsync(a));
        Assert.Equal([EndpointOf(sub)], await EndpointsAsync(b));
    }

    [Fact]
    public async Task Invalid_subscriptions_are_refused()
    {
        var (a, _) = await ParentAsync();
        var good = JsonSerializer.SerializeToElement(Subscription("school"));
        var keys = good.GetProperty("keys");
        string p256dh = keys.GetProperty("p256dh").GetString()!, auth = keys.GetProperty("auth").GetString()!;
        const string fcm = "https://fcm.googleapis.com/fcm/send/abc";

        async Task RefusedAsync(object body, string field)
        {
            var r = await a.PostAsJsonAsync("/api/me/push/subscriptions", body, Ct);
            var text = await r.Content.ReadAsStringAsync(Ct);
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{field}: {(int)r.StatusCode} {text}");
            Assert.True(JsonDocument.Parse(text).RootElement.GetProperty("errors").TryGetProperty(field, out _), $"{field}: {text}");
        }

        await RefusedAsync(new { product = "nope", endpoint = fcm, keys = new { p256dh, auth } }, "product");
        await RefusedAsync(new { endpoint = fcm, keys = new { p256dh, auth } }, "product");
        // Only https push service hosts: the API calls the endpoint, so anything else could reach internal addresses.
        await RefusedAsync(new { product = "school", endpoint = "http://fcm.googleapis.com/fcm/send/abc", keys = new { p256dh, auth } }, "endpoint");
        await RefusedAsync(new { product = "school", endpoint = "https://evil.example.com/push", keys = new { p256dh, auth } }, "endpoint");
        await RefusedAsync(new { product = "school", endpoint = "https://127.0.0.1/push", keys = new { p256dh, auth } }, "endpoint");
        await RefusedAsync(new { product = "school", endpoint = "https://fcm.googleapis.com:8443/x", keys = new { p256dh, auth } }, "endpoint");
        await RefusedAsync(new { product = "school", endpoint = "not a url", keys = new { p256dh, auth } }, "endpoint");
        await RefusedAsync(new { product = "school", keys = new { p256dh, auth } }, "endpoint");
        await RefusedAsync(new { product = "school", endpoint = fcm }, "keys.p256dh");
        await RefusedAsync(new { product = "school", endpoint = fcm, keys = new { p256dh = "abc", auth } }, "keys.p256dh");
        await RefusedAsync(new { product = "school", endpoint = fcm, keys = new { p256dh, auth = B64Url(new byte[8]) } }, "keys.auth");
        Assert.Empty(await EndpointsAsync(a));
    }

    [Fact]
    public async Task An_in_app_notification_is_pushed_to_the_users_browsers_for_that_product()
    {
        await DispatchAsync(); // anything left by other tests
        var (parent, parentId) = await ParentAsync();
        var (other, otherId) = await ParentAsync();
        var phone = Subscription("school");
        var laptop = Subscription("school");
        await Json(await parent.PostAsJsonAsync("/api/me/push/subscriptions", phone, Ct));
        await Json(await parent.PostAsJsonAsync("/api/me/push/subscriptions", laptop, Ct));
        await Json(await other.PostAsJsonAsync("/api/me/push/subscriptions", Subscription("care"), Ct));

        await AddNotificationAsync(parentId, "school", "Results are out", "First term results for Ada.", "/parent/results");
        await AddNotificationAsync(parentId, "care", "Not subscribed in CareLink", "Body", "/portal");
        await AddNotificationAsync(otherId, "school", "Other parent, no school subscription", "Body", "/parent");
        Sender.Clear();

        Assert.True(await DispatchAsync() >= 3);
        var sent = Sender.Sent.ToList();
        Assert.Equal(2, sent.Count);
        Assert.Equal(new[] { EndpointOf(phone), EndpointOf(laptop) }.Order(), sent.Select(s => s.Target.Endpoint).Order());
        Assert.All(sent, s =>
        {
            Assert.Equal("Results are out", s.Payload.Title);
            Assert.Equal("First term results for Ada.", s.Payload.Body);
            Assert.Equal("/parent/results", s.Payload.Link);
        });

        var statuses = await api.WithDbAsync(db => db.WebPushMessages.Where(m => m.UserId == parentId || m.UserId == otherId)
            .Select(m => new { m.Title, m.Status }).ToListAsync(Ct));
        Assert.Equal(WebPushStatus.Sent, statuses.Single(m => m.Title == "Results are out").Status);
        Assert.Equal(WebPushStatus.Skipped, statuses.Single(m => m.Title == "Not subscribed in CareLink").Status);
        Assert.Equal(WebPushStatus.Skipped, statuses.Single(m => m.Title.StartsWith("Other parent")).Status);
        Assert.NotNull(await api.WithDbAsync(db => db.WebPushSubscriptions.Where(s => s.Endpoint == EndpointOf(phone)).Select(s => s.LastSentAt).SingleAsync(Ct)));

        // Sent once: a second run sends nothing more.
        Sender.Clear();
        await DispatchAsync();
        Assert.Empty(Sender.Sent);
    }

    [Fact]
    public async Task CareLink_pushes_keep_health_details_off_the_lock_screen()
    {
        await DispatchAsync();
        var (patient, patientId) = await ParentAsync();
        await Json(await patient.PostAsJsonAsync("/api/me/push/subscriptions", Subscription("care"), Ct));
        await AddNotificationAsync(patientId, "care", "Result ready", "Your HIV test result is ready.", "/portal/results");
        Sender.Clear();
        await DispatchAsync();
        var push = Assert.Single(Sender.Sent);
        Assert.Equal("Result ready", push.Payload.Title);
        Assert.Equal(WebPushRules.CareBody, push.Payload.Body);
        Assert.DoesNotContain("HIV", await api.WithDbAsync(db => db.WebPushMessages.Where(m => m.UserId == patientId).Select(m => m.Body).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_subscription_the_push_service_says_is_gone_is_deleted()
    {
        await DispatchAsync();
        var (parent, parentId) = await ParentAsync();
        var sub = Subscription("school");
        await Json(await parent.PostAsJsonAsync("/api/me/push/subscriptions", sub, Ct));
        Sender.Gone.Add(EndpointOf(sub));
        await AddNotificationAsync(parentId, "school", "Fees reminder", "Body", "/parent/fees");
        await DispatchAsync();
        Assert.Empty(await EndpointsAsync(parent));
        Assert.Equal(WebPushStatus.Skipped, await api.WithDbAsync(db => db.WebPushMessages.Where(m => m.UserId == parentId).Select(m => m.Status).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_failing_push_service_is_retried_then_given_up()
    {
        await DispatchAsync();
        var (parent, parentId) = await ParentAsync();
        var sub = Subscription("school");
        await Json(await parent.PostAsJsonAsync("/api/me/push/subscriptions", sub, Ct));
        Sender.Failing.Add(EndpointOf(sub));
        await AddNotificationAsync(parentId, "school", "Attendance", "Body", "/parent/attendance");
        await DispatchAsync();
        var row = await api.WithDbAsync(db => db.WebPushMessages.AsNoTracking().SingleAsync(m => m.UserId == parentId, Ct));
        Assert.Equal(WebPushStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        for (var i = 0; i < 4; i++) await DispatchAsync();
        Assert.Equal(WebPushStatus.Failed, await api.WithDbAsync(db => db.WebPushMessages.Where(m => m.UserId == parentId).Select(m => m.Status).SingleAsync(Ct)));
        Sender.Failing.Remove(EndpointOf(sub));
    }

    [Fact]
    public async Task OrderLink_owners_get_a_push_for_a_new_order()
    {
        await DispatchAsync();
        var (seller, auth) = await api.SignupAsync("Pusha");
        var slug = auth.GetProperty("tenant").GetProperty("slug").GetString();
        var ownerId = auth.GetProperty("user").GetProperty("id").GetGuid();
        var owner = ApiFixture.On(Host, seller);
        await Json(await owner.PostAsJsonAsync("/api/me/push/subscriptions", Subscription("whatsapp-orders"), Ct));
        var rice = await Json(await seller.PostAsJsonAsync("/api/products", new { name = "Jollof rice", priceMinor = 300_000, inStock = true, isActive = true }, Ct));

        var order = await Json(await Host.CreateClient().PostAsJsonAsync($"/api/public/stores/{slug}/orders", new
        {
            items = new[] { new { productId = rice.GetProperty("id").GetGuid(), quantity = 1 } },
            fulfilment = "Pickup",
            customerName = "Bola",
            phone = "0803 000 0000",
        }, Ct));

        Sender.Clear();
        await DispatchAsync();
        var push = Assert.Single(Sender.Sent);
        Assert.Equal($"New order #{order.GetProperty("number").GetInt64()}", push.Payload.Title);
        Assert.Contains("Bola", push.Payload.Body);
        Assert.StartsWith("/dashboard/orders/", push.Payload.Link);
        Assert.Equal(ownerId, await api.WithDbAsync(db => db.WebPushMessages.Where(m => m.Title == push.Payload.Title && m.Status == WebPushStatus.Sent)
            .OrderByDescending(m => m.CreatedAt).Select(m => m.UserId).FirstAsync(Ct)));
    }
}

/// <summary>Stands in for the browsers' push services: records each send; endpoints in <see cref="Gone"/> answer 410, <see cref="Failing"/> fail.</summary>
public sealed class FakeWebPushSender : IWebPushSender
{
    public ConcurrentQueue<(PushTarget Target, PushPayload Payload)> Sent { get; } = new();
    public HashSet<string> Gone { get; } = [];
    public HashSet<string> Failing { get; } = [];

    public void Clear() => Sent.Clear();

    public Task<PushSendResult> SendAsync(PushTarget target, PushPayload payload, CancellationToken ct)
    {
        if (Gone.Contains(target.Endpoint)) return Task.FromResult(PushSendResult.Gone);
        if (Failing.Contains(target.Endpoint)) throw new HttpRequestException("push service unavailable");
        Sent.Enqueue((target, payload));
        return Task.FromResult(PushSendResult.Sent);
    }
}
