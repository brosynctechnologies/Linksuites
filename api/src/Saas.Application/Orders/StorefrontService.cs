using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

/// <summary>Public storefront: browse, price a cart, place an order. No sign-in; the store slug picks the tenant.</summary>
public sealed class StorefrontService(
    IAppDbContext db,
    ITenantContext tenant,
    PlanService plans,
    NotificationQueue notifications,
    AppUrls urls,
    TimeProvider clock)
{
    public async Task<PublicStoreDto> GetStoreAsync(string slug, CancellationToken ct)
    {
        var store = await ResolveAsync(slug, ct);
        var zones = await db.DeliveryZones.AsNoTracking().Where(z => z.IsActive).OrderBy(z => z.FeeMinor)
            .Select(z => new PublicZoneDto(z.Id, z.Name, z.FeeMinor, z.EtaText)).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking()
            .Where(c => db.Products.Any(p => p.CategoryId == c.Id && p.IsActive))
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new PublicCategoryDto(c.Id, c.Name)).ToListAsync(ct);
        var hasDiscounts = await db.DiscountCodes.AnyAsync(d => d.IsActive, ct)
            && await plans.HasFeatureAsync(LimitKeys.DiscountCodes, ct);
        var showBranding = !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct);

        return new PublicStoreDto(store.Name, store.Slug, store.Description, store.LogoUrl, store.CoverUrl,
            store.Currency, store.ThemeColor, store.IsOpen, zones, categories, showBranding, hasDiscounts);
    }

    public async Task<IReadOnlyList<PublicProductDto>> GetProductsAsync(string slug, CancellationToken ct)
    {
        await ResolveAsync(slug, ct);
        var products = await ProductQuery().Where(p => p.IsActive)
            .OrderBy(p => p.Position).ThenByDescending(p => p.CreatedAt).ToListAsync(ct);
        return products.Select(ToPublic).ToList();
    }

    public async Task<PublicProductDto> GetProductAsync(string slug, string productSlug, CancellationToken ct)
    {
        await ResolveAsync(slug, ct);
        var product = await ProductQuery().FirstOrDefaultAsync(p => p.Slug == productSlug && p.IsActive, ct)
            ?? throw new NotFoundException("Product");
        return ToPublic(product);
    }

    public async Task<QuoteDto> QuoteAsync(string slug, QuoteRequest request, CancellationToken ct)
    {
        var store = await ResolveAsync(slug, ct);
        var priced = await PriceAsync(store, request, strict: false, ct);
        return priced.ToDto(store.Currency);
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string slug, PlaceOrderRequest request, CancellationToken ct)
    {
        var store = await ResolveAsync(slug, ct);
        if (!store.IsOpen) throw new ConflictException("This store is closed and not taking orders right now.", "store_closed");

        var country = await db.Tenants.Where(t => t.Id == store.TenantId).Select(t => t.Country).FirstAsync(ct);
        var phone = PhoneNumbers.Normalize(request.Phone, country)
            ?? throw BadRequestException.Field("phone", "Enter a valid phone number.");

        var priced = await PriceAsync(store, request, strict: true, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var line in priced.Lines)
        {
            var tracked = priced.Products[line.ProductId].StockQty is not null;
            if (!tracked) continue;
            var qty = line.Quantity;
            var updated = await db.Products
                .Where(p => p.Id == line.ProductId && p.StockQty >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, p => p.StockQty - qty), ct);
            if (updated == 0) throw new ConflictException($"Sorry, there is not enough {line.Name} left.", "out_of_stock");
        }

        if (priced.DiscountEntity is { } discount)
        {
            var updated = await db.DiscountCodes
                .Where(d => d.Id == discount.Id && (d.MaxUses == null || d.Uses < d.MaxUses))
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Uses, d => d.Uses + 1), ct);
            if (updated == 0) throw new ConflictException("This discount code has been used up.", "discount_used_up");
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.StoreId == store.Id && c.Phone == phone, ct);
        if (customer is null)
        {
            customer = new Customer { StoreId = store.Id, Name = request.CustomerName.Trim(), Phone = phone };
            db.Customers.Add(customer);
        }
        customer.Name = request.CustomerName.Trim();
        if (!string.IsNullOrWhiteSpace(request.Address)) customer.Address = request.Address.Trim();
        customer.OrdersCount++;

        var order = new Order
        {
            StoreId = store.Id,
            Number = await db.NextOrderNumberAsync(store.Id, ct),
            Code = await NewOrderCodeAsync(ct),
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            Phone = phone,
            Fulfilment = request.Fulfilment,
            DeliveryZoneId = priced.Zone?.Id,
            DeliveryZoneName = priced.Zone?.Name,
            Address = request.Fulfilment == Fulfilment.Delivery ? request.Address?.Trim() : null,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            SubtotalMinor = priced.SubtotalMinor,
            DeliveryFeeMinor = priced.DeliveryFeeMinor,
            DiscountMinor = priced.Discount.AmountMinor,
            DiscountCode = priced.Discount.AppliedCode,
            TotalMinor = priced.TotalMinor,
            Currency = store.Currency,
            Items = priced.Lines.Select(l => new OrderItem
            {
                ProductId = l.ProductId,
                VariantId = l.VariantId,
                NameSnapshot = l.Name,
                VariantSnapshot = l.Variant,
                UnitPriceMinor = l.UnitPriceMinor,
                Quantity = l.Quantity,
                LineTotalMinor = l.LineTotalMinor,
            }).ToList(),
            History = [new OrderStatusHistory { Status = OrderStatus.New, Note = "Order placed" }],
        };
        var orderUrl = urls.OrderUrl(order.Code);
        order.WhatsappMessage = WhatsAppMessage.Build(order, orderUrl);
        db.Orders.Add(order);

        var ownerEmails = await db.Memberships
            .Where(m => m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active && m.User!.Email != null)
            .Select(m => m.User!.Email!).ToListAsync(ct);
        foreach (var email in ownerEmails)
        {
            notifications.Email(email, "order.new", new()
            {
                ["storeName"] = store.Name,
                ["number"] = order.Number.ToString(),
                ["customer"] = order.CustomerName,
                ["total"] = Money.Format(order.TotalMinor, order.Currency),
                ["message"] = order.WhatsappMessage,
                ["link"] = $"{urls.WebBaseUrl.TrimEnd('/')}/dashboard/orders/{order.Id}",
            }, store.TenantId);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new PlaceOrderResult(order.Code, order.Number, order.TotalMinor, order.Currency,
            WhatsAppMessage.Link(store.WhatsappNumber, order.WhatsappMessage), orderUrl);
    }

    public async Task<PublicOrderDto> GetOrderAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Code == normalized, ct) ?? throw new NotFoundException("Order");
        var store = await db.Stores.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.Id == order.StoreId, ct);
        tenant.SetTenant(order.TenantId); // the plan check below reads the seller's subscription
        var showBranding = !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct);

        return new PublicOrderDto(order.Code, order.Number, store.Name, store.Slug, order.Status, order.PaymentStatus,
            order.Fulfilment,
            order.Items.Select(i => new PublicOrderItemDto(i.NameSnapshot, i.VariantSnapshot, i.Quantity, i.LineTotalMinor)).ToList(),
            order.SubtotalMinor, order.DeliveryFeeMinor, order.DiscountMinor, order.TotalMinor, order.Currency,
            order.CreatedAt, WhatsAppMessage.Link(store.WhatsappNumber, order.WhatsappMessage), showBranding);
    }

    /// <summary>Finds the store by slug and scopes the rest of the request to its tenant.</summary>
    private async Task<Store> ResolveAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var store = await db.Stores.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Slug == normalized && db.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Store");
        tenant.SetTenant(store.TenantId);
        return store;
    }

    private IQueryable<Product> ProductQuery() => db.Products.AsNoTracking()
        .Include(p => p.Images.OrderBy(i => i.Position))
        .Include(p => p.Options.OrderBy(o => o.Position))
        .Include(p => p.Variants);

    private sealed record Priced(
        List<PricedLine> Lines,
        Dictionary<Guid, Product> Products,
        DeliveryZone? Zone,
        DiscountCode? DiscountEntity,
        DiscountResult Discount,
        long SubtotalMinor,
        long DeliveryFeeMinor,
        long TotalMinor)
    {
        public QuoteDto ToDto(string currency) => new(
            Lines.Select(l => new QuoteLineDto(l.ProductId, l.VariantId, l.Name, l.Variant, l.UnitPriceMinor, l.Quantity, l.LineTotalMinor, l.StockQty)).ToList(),
            SubtotalMinor, DeliveryFeeMinor, Discount.AmountMinor, TotalMinor, currency, Discount.Message);
    }

    /// <summary>
    /// Prices the cart from the database. With <paramref name="strict"/> false (quotes), unavailable lines are
    /// dropped and a missing zone just means no fee yet; with it true (orders), any problem is an error.
    /// </summary>
    private async Task<Priced> PriceAsync(Store store, QuoteRequest request, bool strict, CancellationToken ct)
    {
        var ids = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.Variants)
            .Where(p => ids.Contains(p.Id) && p.StoreId == store.Id)
            .ToDictionaryAsync(p => p.Id, ct);

        var lines = new List<PricedLine>();
        // Stock is kept per product and shared by its variants, so count what earlier lines already took.
        var taken = new Dictionary<Guid, int>();
        foreach (var item in request.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
            {
                if (strict) throw new ConflictException("An item in your cart is no longer available.", "unavailable");
                continue;
            }
            var variant = item.VariantId is { } vid ? product.Variants.FirstOrDefault(v => v.Id == vid) : null;
            if (product.Variants.Count > 0 && variant is null)
            {
                if (strict) throw BadRequestException.Field("items", $"Choose an option for {product.Name}.");
                continue;
            }
            // What is still unclaimed for this line: the product's stock less what earlier lines of it took.
            int? remaining = product.StockQty is { } stock ? Math.Max(0, stock - taken.GetValueOrDefault(product.Id)) : null;
            var available = product.IsAvailable && (variant?.InStock ?? true)
                && (remaining is null || remaining >= item.Quantity);
            // A quote keeps a line asking for more than is left, priced at what can be sold, so the cart can lower it.
            var quantity = item.Quantity;
            if (!available && !strict && product.IsAvailable && (variant?.InStock ?? true) && remaining is { } left)
            {
                quantity = left;
                available = true;
            }
            if (!available)
            {
                if (strict) throw new ConflictException($"Sorry, {product.Name} is out of stock.", "out_of_stock");
                continue;
            }
            taken[product.Id] = taken.GetValueOrDefault(product.Id) + quantity;
            lines.Add(new PricedLine(product.Id, variant?.Id, product.Name, variant?.Title,
                variant?.PriceMinor ?? product.PriceMinor, quantity, remaining));
        }
        if (strict && lines.Count == 0) throw BadRequestException.Field("items", "Your cart is empty.");

        DeliveryZone? zone = null;
        if (request.Fulfilment == Fulfilment.Delivery)
        {
            if (request.DeliveryZoneId is { } zoneId)
                zone = await db.DeliveryZones.AsNoTracking().FirstOrDefaultAsync(z => z.Id == zoneId && z.IsActive, ct);
            if (strict && zone is null && await db.DeliveryZones.AnyAsync(z => z.IsActive, ct))
                throw BadRequestException.Field("deliveryZoneId", "Choose a delivery area.");
        }

        var subtotal = lines.Sum(l => l.LineTotalMinor);
        DiscountCode? discountEntity = null;
        var discount = DiscountResult.None;
        if (!string.IsNullOrWhiteSpace(request.DiscountCode))
        {
            var entered = request.DiscountCode.Trim().ToUpperInvariant();
            discountEntity = await db.DiscountCodes.AsNoTracking().FirstOrDefaultAsync(d => d.Code == entered, ct);
            if (discountEntity is not null && !await plans.HasFeatureAsync(LimitKeys.DiscountCodes, ct)) discountEntity = null;
            discount = OrderPricing.ApplyDiscount(discountEntity, entered, subtotal, store.Currency, clock.GetUtcNow());
            if (discount.AmountMinor == 0) discountEntity = null;
            if (strict && discount.Message is not null) throw BadRequestException.Field("discountCode", discount.Message);
        }

        var deliveryFee = zone?.FeeMinor ?? 0;
        return new Priced(lines, products, zone, discountEntity, discount, subtotal, deliveryFee,
            OrderPricing.Total(subtotal, deliveryFee, discount.AmountMinor));
    }

    private async Task<string> NewOrderCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = Codes.Random();
            if (!await db.Orders.IgnoreQueryFilters().AnyAsync(o => o.Code == code, ct)) return code;
        }
        return Codes.Random(8);
    }

    internal static PublicProductDto ToPublic(Product p) => new(
        p.Id, p.Slug, p.Name, p.Description, p.PriceMinor, p.CompareAtPriceMinor, p.IsAvailable, p.CategoryId,
        p.Images.OrderBy(i => i.Position).Select(i => new ImageUrlDto(i.Url)).ToList(),
        p.Options.OrderBy(o => o.Position).Select(o => new OptionDto(o.Name, o.Values)).ToList(),
        p.Variants.Select(v => new PublicVariantDto(v.Id, v.OptionValues, v.Title, v.PriceMinor ?? p.PriceMinor, v.InStock)).ToList());
}
