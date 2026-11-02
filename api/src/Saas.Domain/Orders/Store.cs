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
