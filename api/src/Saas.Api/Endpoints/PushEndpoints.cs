using Saas.Application.Common;

namespace Saas.Api.Endpoints;

/// <summary>
/// Wave 4 (PWA): the signed-in user's Web Push subscriptions, in every product's app. A subscription is per browser and product; the
/// in-app notifications of that product (and OrderLink's new orders) are pushed to it.
/// </summary>
public static class PushEndpoints
{
    public static void MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var push = app.MapGroup("/api/me/push").WithTags("Notifications").RequireAuthorization();
        // Whether push is set up, the VAPID public key to subscribe with, and this user's subscribed browsers.
        push.MapGet("/", (PushSubscriptions s, CancellationToken ct) => s.GetAsync(ct));
        push.MapPost("/subscriptions", (SubscribePushRequest r, PushSubscriptions s, CancellationToken ct) => s.SubscribeAsync(r, ct));
        // By endpoint, in the body: the browser knows its endpoint, and an endpoint is not something to put in a URL (logs).
        push.MapPost("/subscriptions/remove", async (RemovePushRequest r, PushSubscriptions s, CancellationToken ct) =>
        {
            await s.RemoveAsync(r, ct);
            return Results.NoContent();
        });
    }
}
