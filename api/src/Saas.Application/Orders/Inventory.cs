using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

/// <summary>The Pro plan's feature lines for the wave 4 OrderLink items (the seed and the data migration use the same words).</summary>
public static class OrderPlanFeatures
{
    public const string Stock = "Stock tracking and low-stock alerts";
    public const string Payments = "Online payments to your Paystack";
    public const string Team = "Team of up to 3";
}

public enum StockStatus { InStock, Low, SoldOut }

public sealed record InventoryItemDto(Guid ProductId, Guid? VariantId, string ProductName, string? VariantTitle, int StockQty, StockStatus Status);
public sealed record InventoryDto(bool Enabled, int LowStockThreshold, IReadOnlyList<InventoryItemDto> Items);
public sealed record InventorySettingsRequest(int LowStockThreshold);
public sealed record StockAdjustmentRequest(Guid ProductId, Guid? VariantId, int Delta, string Reason);
public sealed record StockAdjustmentDto(
    Guid Id, Guid ProductId, Guid? VariantId, string ProductName, string? VariantTitle, int Delta, int QuantityAfter,
    StockChangeKind Kind, string Reason, string? ByName, Guid? OrderId, long? OrderNumber, DateTimeOffset CreatedAt);

public sealed class InventorySettingsRequestValidator : AbstractValidator<InventorySettingsRequest>
{
    public InventorySettingsRequestValidator() =>
        RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 10_000).WithMessage("Enter a number from 0 to 10,000.");
}

public sealed class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("Enter how many to add or remove.")
            .InclusiveBetween(-100_000, 100_000).WithMessage("Enter a change from -100,000 to 100,000.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Say why the stock changed.").MaximumLength(200);
    }
}

/// <summary>
/// Every change to a stock count goes through here: the change itself is a conditional SQL update (so concurrent orders never take
/// more than is left), each change is written to the adjustments log, and crossing the store's low-stock threshold alerts the owner
/// once per dip (a flag on the product or variant, set by a conditional update, cleared when the count goes back above it).
/// </summary>
public sealed class StockLedger(
    IAppDbContext db, ICurrentUser user, PlanService plans, NotificationQueue notifications, InAppNotifications feed, AppUrls urls)
{
    public const string PlanNeeded = "Stock tracking is on the Pro plan. Upgrade to track stock and get low-stock alerts.";
    public const string LowStockKind = "order.low_stock";

    /// <summary>
    /// Wave 4 review: the one order every multi-row stock change takes its rows in (product id, then variant id, a product's own count
    /// before its variants'), so two transactions touching the same rows always lock them in the same order and can't deadlock.
    /// </summary>
    public static IEnumerable<T> InLockOrder<T>(IEnumerable<T> changes, Func<T, Guid> productId, Func<T, Guid?> variantId) =>
        changes.OrderBy(productId).ThenBy(c => variantId(c) is null ? 0 : 1).ThenBy(c => variantId(c) ?? Guid.Empty);

    /// <summary>Stock counts apply only on a plan with stock tracking. On Free, saved counts are kept but ignored.</summary>
    public Task<bool> TrackingAsync(CancellationToken ct) => plans.HasFeatureAsync(LimitKeys.Inventory, ct);

    /// <summary>
    /// Changes a product's (or, with <paramref name="variantId"/>, a variant's) tracked count by <paramref name="delta"/>, never below zero.
    /// Returns the new count, or null when the item isn't tracked or there isn't enough. Call inside a transaction: the row stays
    /// locked until commit, so the count read back is this change's result.
    /// </summary>
    public async Task<int?> ChangeAsync(Guid productId, Guid? variantId, int delta, CancellationToken ct)
    {
        if (variantId is { } vid)
        {
            var changed = await db.ProductVariants.Where(v => v.Id == vid && v.ProductId == productId && v.StockQty != null && v.StockQty + delta >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockQty, v => v.StockQty + delta), ct);
            if (changed == 0) return null;
            return await db.ProductVariants.AsNoTracking().Where(v => v.Id == vid).Select(v => v.StockQty).FirstAsync(ct);
        }
        var updated = await db.Products.Where(p => p.Id == productId && p.StockQty != null && p.StockQty + delta >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, p => p.StockQty + delta), ct);
        if (updated == 0) return null;
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockQty).FirstAsync(ct);
    }

    /// <summary>Adds a log row (saved with the caller's SaveChanges).</summary>
    public void Log(Store store, Guid productId, Guid? variantId, string productName, string? variantTitle, int delta, int after,
        StockChangeKind kind, string reason, Order? order = null) =>
        db.StockAdjustments.Add(new StockAdjustment
        {
            StoreId = store.Id, ProductId = productId, VariantId = variantId, ProductName = productName, VariantTitle = variantTitle,
            Delta = delta, QuantityAfter = after, Kind = kind, Reason = reason,
            ChangedBy = kind is StockChangeKind.Sale or StockChangeKind.Cancellation ? null : user.UserId,
            OrderId = order?.Id, OrderNumber = order?.Number,
        });

    /// <summary>
    /// After a count changed: at or under the threshold, claims the alert flag and (when this call claimed it) tells the owners by
    /// email and in the app; above it, clears the flag so the next dip alerts again. Notifications save with the caller's SaveChanges.
    /// </summary>
    public async Task AfterChangeAsync(Store store, Guid productId, Guid? variantId, string name, int count, CancellationToken ct)
    {
        var threshold = store.LowStockThreshold;
        int claimed;
        if (count > threshold)
        {
            if (variantId is { } v)
                await db.ProductVariants.Where(x => x.Id == v && x.LowStockAlerted).ExecuteUpdateAsync(s => s.SetProperty(x => x.LowStockAlerted, false), ct);
            else
                await db.Products.Where(x => x.Id == productId && x.LowStockAlerted).ExecuteUpdateAsync(s => s.SetProperty(x => x.LowStockAlerted, false), ct);
            return;
        }
        if (variantId is { } vid)
            claimed = await db.ProductVariants.Where(x => x.Id == vid && !x.LowStockAlerted)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LowStockAlerted, true), ct);
        else
            claimed = await db.Products.Where(x => x.Id == productId && !x.LowStockAlerted)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LowStockAlerted, true), ct);
        if (claimed == 0) return;

        var title = count == 0 ? $"{name} is sold out" : $"Only {count} {name} left";
        var body = count == 0
            ? $"{name} is sold out on {store.Name}. Customers can't order it until you add stock."
            : $"{name} is down to {count} on {store.Name}. Add stock before it sells out.";
        var owners = await db.Memberships.AsNoTracking()
            .Where(m => m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, m.User!.Email }).ToListAsync(ct);
        foreach (var owner in owners)
        {
            feed.Add(owner.UserId, Products.WhatsAppOrders, LowStockKind, title, body, "/app/inventory?filter=low", store.TenantId);
            if (!string.IsNullOrWhiteSpace(owner.Email))
                notifications.Email(owner.Email, LowStockKind, new()
                {
                    ["storeName"] = store.Name,
                    ["item"] = name,
                    ["count"] = count.ToString(),
                    ["link"] = $"{urls.WebBaseUrl.TrimEnd('/')}/dashboard/inventory?filter=low",
                }, store.TenantId, owner.UserId);
        }
    }

    public static string ItemName(string product, string? variant) => variant is { Length: > 0 } v ? $"{product} ({v})" : product;
}

/// <summary>The Stock page: tracked items, the low-stock threshold, manual adjustments and the adjustments log.</summary>
public sealed class InventoryService(IAppDbContext db, StoreSettingsService stores, StockLedger ledger)
{
    public async Task<InventoryDto> GetAsync(string? filter, string? q, CancellationToken ct)
    {
        var store = await stores.CurrentStoreAsync(ct);
        if (!await ledger.TrackingAsync(ct)) return new InventoryDto(false, store.LowStockThreshold, []);
        var query = db.Products.AsNoTracking().Include(p => p.Variants).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }
        var products = await query.Where(p => p.StockQty != null || p.Variants.Any(v => v.StockQty != null))
            .OrderBy(p => p.Name).ToListAsync(ct);
        var threshold = store.LowStockThreshold;
        StockStatus StatusOf(int n) => n <= 0 ? StockStatus.SoldOut : n <= threshold ? StockStatus.Low : StockStatus.InStock;
        var items = new List<InventoryItemDto>();
        foreach (var p in products)
        {
            if (p.StockQty is { } pq) items.Add(new(p.Id, null, p.Name, null, pq, StatusOf(pq)));
            foreach (var v in p.Variants.Where(v => v.StockQty != null).OrderBy(v => v.Title))
                items.Add(new(p.Id, v.Id, p.Name, v.Title, v.StockQty!.Value, StatusOf(v.StockQty!.Value)));
        }
        if (filter == "low") items = items.Where(i => i.Status != StockStatus.InStock).ToList();
        return new InventoryDto(true, threshold, items);
    }

    public async Task<InventoryDto> SaveSettingsAsync(InventorySettingsRequest r, CancellationToken ct)
    {
        if (!await ledger.TrackingAsync(ct)) throw new PlanLimitException(StockLedger.PlanNeeded);
        var store = await stores.CurrentStoreAsync(ct);
        store.LowStockThreshold = r.LowStockThreshold;
        await db.SaveChangesAsync(ct);
        return new InventoryDto(true, store.LowStockThreshold, []);
    }

    public async Task<StockAdjustmentDto> AdjustAsync(StockAdjustmentRequest r, CancellationToken ct)
    {
        if (!await ledger.TrackingAsync(ct)) throw new PlanLimitException(StockLedger.PlanNeeded);
        var store = await stores.CurrentStoreAsync(ct);
        var product = await db.Products.AsNoTracking().Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == r.ProductId, ct)
            ?? throw new NotFoundException("Product");
        ProductVariant? variant = null;
        if (r.VariantId is { } vid)
            variant = product.Variants.FirstOrDefault(v => v.Id == vid) ?? throw new NotFoundException("Variant");
        var tracked = variant is null ? product.StockQty is not null : variant.StockQty is not null;
        if (!tracked)
            throw new ConflictException("Stock isn't tracked for this item. Set a stock count in the product first.", "not_tracked");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var after = await ledger.ChangeAsync(product.Id, variant?.Id, r.Delta, ct)
            ?? throw new ConflictException("That would take the stock below zero.", "not_enough_stock");
        ledger.Log(store, product.Id, variant?.Id, product.Name, variant?.Title, r.Delta, after, StockChangeKind.Adjustment, r.Reason.Trim());
        await ledger.AfterChangeAsync(store, product.Id, variant?.Id, StockLedger.ItemName(product.Name, variant?.Title), after, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var row = await db.StockAdjustments.AsNoTracking().Where(a => a.ProductId == product.Id).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .FirstAsync(ct);
        return (await ToDtosAsync([row], ct))[0];
    }

    public async Task<Paged<StockAdjustmentDto>> HistoryAsync(Guid? productId, Guid? variantId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.StockAdjustments.AsNoTracking();
        if (productId is { } pid) query = query.Where(a => a.ProductId == pid);
        if (variantId is { } vid) query = query.Where(a => a.VariantId == vid);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<StockAdjustmentDto>(await ToDtosAsync(rows, ct), page, pageSize, total);
    }

    private async Task<List<StockAdjustmentDto>> ToDtosAsync(List<StockAdjustment> rows, CancellationToken ct)
    {
        var userIds = rows.Where(r => r.ChangedBy != null).Select(r => r.ChangedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(a => new StockAdjustmentDto(a.Id, a.ProductId, a.VariantId, a.ProductName, a.VariantTitle, a.Delta, a.QuantityAfter,
            a.Kind, a.Reason, a.ChangedBy is { } by ? names.GetValueOrDefault(by) : null, a.OrderId, a.OrderNumber, a.CreatedAt)).ToList();
    }
}
