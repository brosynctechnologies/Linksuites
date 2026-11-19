using Saas.Api.Infrastructure;
using Saas.Application.Orders;
using Saas.Domain.Orders;

namespace Saas.Api.Endpoints;

/// <summary>WhatsApp Order System: seller dashboard and public storefront.</summary>
public static class OrderSystemEndpoints
{
    public static void MapOrderSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Policies.OrdersProduct);

        var store = api.MapGroup("/store").WithTags("Store");
        store.MapGet("/", (StoreSettingsService s, CancellationToken ct) => s.GetAsync(ct));
        store.MapPut("/", (UpdateStoreRequest r, StoreSettingsService s, CancellationToken ct) => s.UpdateAsync(r, ct))
            .Validate<UpdateStoreRequest>().RequireAuthorization(Policies.Admin);
        store.MapPost("/logo", (IFormFile file, StoreSettingsService s, CancellationToken ct) => s.UploadImageAsync(file.ToUpload(), false, ct))
            .DisableAntiforgery().RequireAuthorization(Policies.Admin);
        store.MapPost("/cover", (IFormFile file, StoreSettingsService s, CancellationToken ct) => s.UploadImageAsync(file.ToUpload(), true, ct))
            .DisableAntiforgery().RequireAuthorization(Policies.Admin);

        // Custom domain (Wave 3, plans with custom_domain): the owner sets or removes it; owner and admins can ask for a DNS check now.
        var domain = store.MapGroup("/domain").WithTags("Custom domain");
        domain.MapGet("/", (CustomDomainService s, CancellationToken ct) => s.GetAsync(ct));
        domain.MapPut("/", (SetCustomDomainRequest r, CustomDomainService s, CancellationToken ct) => s.SetAsync(r, ct))
            .RequireAuthorization(Policies.Owner).RequireRateLimiting(RateLimits.Admin);
        domain.MapDelete("/", async (CustomDomainService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Owner);
        domain.MapPost("/check", (CustomDomainService s, CancellationToken ct) => s.CheckNowAsync(ct))
            .RequireAuthorization(Policies.Admin).RequireRateLimiting(RateLimits.Admin);

        var categories = api.MapGroup("/categories").WithTags("Catalogue");
        categories.MapGet("/", (CatalogService s, CancellationToken ct) => s.ListCategoriesAsync(ct));
        categories.MapPost("/", (SaveCategoryRequest r, CatalogService s, CancellationToken ct) => s.SaveCategoryAsync(null, r, ct))
            .Validate<SaveCategoryRequest>();
        categories.MapPut("/{id:guid}", (Guid id, SaveCategoryRequest r, CatalogService s, CancellationToken ct) => s.SaveCategoryAsync(id, r, ct))
            .Validate<SaveCategoryRequest>();
        categories.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteCategoryAsync(id, ct);
            return Results.NoContent();
        });

        var products = api.MapGroup("/products").WithTags("Catalogue");
        products.MapGet("/", (string? q, Guid? categoryId, CatalogService s, CancellationToken ct) => s.ListProductsAsync(q, categoryId, ct));
        products.MapGet("/{id:guid}", (Guid id, CatalogService s, CancellationToken ct) => s.GetProductAsync(id, ct));
        products.MapPost("/", (SaveProductRequest r, CatalogService s, CancellationToken ct) => s.SaveProductAsync(null, r, ct))
            .Validate<SaveProductRequest>();
        products.MapPut("/{id:guid}", (Guid id, SaveProductRequest r, CatalogService s, CancellationToken ct) => s.SaveProductAsync(id, r, ct))
            .Validate<SaveProductRequest>();
        products.MapDelete("/{id:guid}", async (Guid id, CatalogService s, CancellationToken ct) =>
        {
            await s.DeleteProductAsync(id, ct);
            return Results.NoContent();
        });
        products.MapPost("/{id:guid}/images", (Guid id, IFormFile file, CatalogService s, CancellationToken ct) =>
            s.AddImageAsync(id, file.ToUpload(), ct)).DisableAntiforgery();
        products.MapDelete("/{id:guid}/images/{imageId:guid}", (Guid id, Guid imageId, CatalogService s, CancellationToken ct) =>
            s.DeleteImageAsync(id, imageId, ct));

        var zones = api.MapGroup("/delivery-zones").WithTags("Checkout settings");
        zones.MapGet("/", (CheckoutSettingsService s, CancellationToken ct) => s.ListZonesAsync(ct));
        zones.MapPost("/", (SaveDeliveryZoneRequest r, CheckoutSettingsService s, CancellationToken ct) => s.SaveZoneAsync(null, r, ct))
            .Validate<SaveDeliveryZoneRequest>();
        zones.MapPut("/{id:guid}", (Guid id, SaveDeliveryZoneRequest r, CheckoutSettingsService s, CancellationToken ct) => s.SaveZoneAsync(id, r, ct))
            .Validate<SaveDeliveryZoneRequest>();
        zones.MapDelete("/{id:guid}", async (Guid id, CheckoutSettingsService s, CancellationToken ct) =>
        {
            await s.DeleteZoneAsync(id, ct);
            return Results.NoContent();
        });

        var discounts = api.MapGroup("/discount-codes").WithTags("Checkout settings");
        discounts.MapGet("/", (CheckoutSettingsService s, CancellationToken ct) => s.ListDiscountsAsync(ct));
        discounts.MapPost("/", (SaveDiscountCodeRequest r, CheckoutSettingsService s, CancellationToken ct) => s.SaveDiscountAsync(null, r, ct))
            .Validate<SaveDiscountCodeRequest>();
        discounts.MapPut("/{id:guid}", (Guid id, SaveDiscountCodeRequest r, CheckoutSettingsService s, CancellationToken ct) => s.SaveDiscountAsync(id, r, ct))
            .Validate<SaveDiscountCodeRequest>();
        discounts.MapDelete("/{id:guid}", async (Guid id, CheckoutSettingsService s, CancellationToken ct) =>
        {
            await s.DeleteDiscountAsync(id, ct);
            return Results.NoContent();
        });

        var orders = api.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", (OrderStatus? status, OrderPaymentStatus? paymentStatus, string? q, int? page, int? pageSize,
            OrderAdminService s, CancellationToken ct) => s.ListAsync(status, q, page ?? 1, pageSize ?? 20, ct, paymentStatus));
        orders.MapGet("/{id:guid}", (Guid id, OrderAdminService s, CancellationToken ct) => s.GetAsync(id, ct));
        orders.MapPatch("/{id:guid}/status", (Guid id, UpdateOrderStatusRequest r, OrderAdminService s, CancellationToken ct) =>
            s.UpdateStatusAsync(id, r, ct));
        orders.MapPatch("/{id:guid}/payment", (Guid id, UpdateOrderPaymentRequest r, OrderAdminService s, CancellationToken ct) =>
            s.UpdatePaymentAsync(id, r, ct));
        api.MapGet("/dashboard", (OrderAdminService s, CancellationToken ct) => s.DashboardAsync(ct)).WithTags("Orders");

        var pub = app.MapGroup("/api/public").WithTags("Storefront");
        // Store slugs for the sitemap (active businesses with something for sale).
        pub.MapGet("/stores", (int? page, int? pageSize, StorefrontService s, CancellationToken ct) =>
            s.ListStoresAsync(page ?? 1, pageSize ?? 500, ct));
        pub.MapGet("/stores/{slug}", (string slug, StorefrontService s, CancellationToken ct) => s.GetStoreAsync(slug, ct));
        pub.MapGet("/stores/{slug}/products", (string slug, StorefrontService s, CancellationToken ct) => s.GetProductsAsync(slug, ct));
        pub.MapGet("/stores/{slug}/products/{productSlug}", (string slug, string productSlug, StorefrontService s, CancellationToken ct) =>
            s.GetProductAsync(slug, productSlug, ct));
        pub.MapPost("/stores/{slug}/quote", (string slug, QuoteRequest r, StorefrontService s, CancellationToken ct) =>
            s.QuoteAsync(slug, r, ct)).Validate<QuoteRequest>();
        pub.MapPost("/stores/{slug}/orders", (string slug, PlaceOrderRequest r, StorefrontService s, CancellationToken ct) =>
            s.PlaceOrderAsync(slug, r, ct)).Validate<PlaceOrderRequest>().RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapGet("/orders/{code}", (string code, StorefrontService s, CancellationToken ct) => s.GetOrderAsync(code, ct));
        // The OrderLink web app asks which store a request's Host serves: only live domains on a plan that includes them.
        pub.MapGet("/domains/{host}", async (string host, CustomDomainCache cache, CancellationToken ct) =>
            await cache.SlugForAsync(host, ct) is { } slug ? Results.Ok(new PublicDomainDto(slug)) : Results.NotFound());

        // For deploy/custom-domains.sh on the host only: the shared secret in X-Host-Secret, compared in constant time.
        var host = app.MapGroup("/api/internal/custom-domains").WithTags("Internal").ExcludeFromDescription().AllowAnonymous()
            .AddEndpointFilter(RequireHostSecret).RequireRateLimiting(RateLimits.Webhook);
        // JSON, or ?format=text for the bash script: "<domain> <status>" lines closed by "# end" (so a cut-off answer is detectable).
        host.MapGet("/", async (string? format, CustomDomainService s, CancellationToken ct) =>
        {
            var list = await s.HostListAsync(ct);
            return format == "text"
                ? Results.Text(string.Concat(list.Domains.Select(d => $"{d.Domain} {d.Status}\n")) + "# end\n", "text/plain")
                : Results.Ok(list);
        });
        host.MapPost("/report", async (HostDomainReport r, CustomDomainService s, CancellationToken ct) =>
        {
            await s.ReportAsync(r, ct);
            return Results.NoContent();
        });
    }

    public const string HostSecretHeader = "X-Host-Secret";

    private static async ValueTask<object?> RequireHostSecret(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<CustomDomainOptions>().HostSecret;
        var sent = context.HttpContext.Request.Headers[HostSecretHeader].ToString();
        if (string.IsNullOrEmpty(expected) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(sent), System.Text.Encoding.UTF8.GetBytes(expected)))
            return Results.Unauthorized();
        return await next(context);
    }
}
