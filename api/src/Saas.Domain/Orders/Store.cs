using Saas.Domain.Common;

namespace Saas.Domain.Orders;

[Audited]
public class Store : TenantEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
    /// <summary>International format digits only, e.g. 2348031234567, as wa.me expects.</summary>
    public required string WhatsappNumber { get; set; }
    public string Currency { get; set; } = "NGN";
    public string ThemeColor { get; set; } = "#16a34a";
    public bool IsOpen { get; set; } = true;
    public string? OpeningHours { get; set; }
    public bool AcceptsOnlinePayment { get; set; }
    /// <summary>"Only N left" shows, and the low-stock alert fires, at or under this count (Pro: stock tracking).</summary>
    public int LowStockThreshold { get; set; } = 5;
    /// <summary>The store's own Paystack secret key, encrypted with <c>ISecretProtector</c>. Online order payments go straight to the store.</summary>
    [AuditIgnore]
    public string? PaystackSecretKeyProtected { get; set; }
    /// <summary>The key replaced or removed last (encrypted), still used to confirm checkouts until <see cref="PreviousKeyUntil"/>.</summary>
    [AuditIgnore]
    public string? PaystackPreviousKeyProtected { get; set; }
    public DateTimeOffset? PreviousKeyUntil { get; set; }
    /// <summary>Next human-facing order number; incremented atomically in SQL.</summary>
    public long NextOrderNumber { get; set; } = 1001;
}

public class Category : TenantEntity
{
    public Guid StoreId { get; set; }
    public required string Name { get; set; }
    public int Position { get; set; }
}

[Audited]
public class Product : TenantEntity
{
    public Guid StoreId { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public long PriceMinor { get; set; }
    public long? CompareAtPriceMinor { get; set; }
    public bool InStock { get; set; } = true;
    /// <summary>Null means stock is not tracked.</summary>
    public int? StockQty { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
    public List<ProductImage> Images { get; set; } = [];
    public List<ProductOption> Options { get; set; } = [];
    public List<ProductVariant> Variants { get; set; } = [];
    /// <summary>Set when the low-stock alert was sent for the current dip; cleared when the count goes back above the threshold.</summary>
    public bool LowStockAlerted { get; set; }

    public bool IsAvailable => IsActive && InStock && StockQty is null or > 0;
}

public class ProductImage : TenantEntity
{
    public Guid ProductId { get; set; }
    public required string Url { get; set; }
    public string? StorageKey { get; set; }
    public int Position { get; set; }
}

public class ProductOption : TenantEntity
{
    public Guid ProductId { get; set; }
    public required string Name { get; set; }
    public List<string> Values { get; set; } = [];
    public int Position { get; set; }
}

public class ProductVariant : TenantEntity
{
    public Guid ProductId { get; set; }
    public List<string> OptionValues { get; set; } = [];
    /// <summary>Null means use the product price.</summary>
    public long? PriceMinor { get; set; }
    public bool InStock { get; set; } = true;
    public string? Sku { get; set; }
    /// <summary>Null means this variant's own stock is not tracked (the product's count, if any, still applies).</summary>
    public int? StockQty { get; set; }
    public bool LowStockAlerted { get; set; }

    public string Title => string.Join(" / ", OptionValues);
}

[Audited]
public class DeliveryZone : TenantEntity
{
    public Guid StoreId { get; set; }
    public required string Name { get; set; }
    public long FeeMinor { get; set; }
    public string? EtaText { get; set; }
    public bool IsActive { get; set; } = true;
}

[Audited]
public class DiscountCode : TenantEntity
{
    public Guid StoreId { get; set; }
    public required string Code { get; set; }
    public DiscountType Type { get; set; }
    /// <summary>Percent (1-100) or a fixed amount in minor units.</summary>
    public long Value { get; set; }
    public long MinOrderMinor { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum DiscountType { Percent, Fixed }

/// <summary>One change to a stock count: a sale, a cancellation giving stock back, a manual adjustment or a count set in the product form.</summary>
public class StockAdjustment : TenantEntity
{
    public Guid StoreId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public required string ProductName { get; set; }
    public string? VariantTitle { get; set; }
    public int Delta { get; set; }
    public int QuantityAfter { get; set; }
    public StockChangeKind Kind { get; set; }
    public required string Reason { get; set; }
    /// <summary>The team member who made the change; null for sales and cancellations.</summary>
    public Guid? ChangedBy { get; set; }
    public Guid? OrderId { get; set; }
    public long? OrderNumber { get; set; }
}

public enum StockChangeKind { Sale, Cancellation, Adjustment, Count }
