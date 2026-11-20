using Saas.Domain.Common;

namespace Saas.Domain.Orders;

public class Customer : TenantEntity
{
    public Guid StoreId { get; set; }
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Address { get; set; }
    public int OrdersCount { get; set; }
}

[Audited]
public class Order : TenantEntity
{
    public Guid StoreId { get; set; }
    public long Number { get; set; }
    /// <summary>Short public code used in links, e.g. AB12CD.</summary>
    public required string Code { get; set; }
    public Guid? CustomerId { get; set; }
    public required string CustomerName { get; set; }
    public required string Phone { get; set; }
    public Fulfilment Fulfilment { get; set; }
    public Guid? DeliveryZoneId { get; set; }
    public string? DeliveryZoneName { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
    public long SubtotalMinor { get; set; }
    public long DeliveryFeeMinor { get; set; }
    public long DiscountMinor { get; set; }
    public string? DiscountCode { get; set; }
    public long TotalMinor { get; set; }
    public required string Currency { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.New;
    public OrderPaymentStatus PaymentStatus { get; set; } = OrderPaymentStatus.Unpaid;
    public string? WhatsappMessage { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<OrderStatusHistory> History { get; set; } = [];
}

public class OrderItem : TenantEntity
{
    public Guid OrderId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? VariantId { get; set; }
    public required string NameSnapshot { get; set; }
    public string? VariantSnapshot { get; set; }
    public long UnitPriceMinor { get; set; }
    public int Quantity { get; set; }
    public long LineTotalMinor { get; set; }
    /// <summary>The product's count was lowered for this line, so cancelling gives it back.</summary>
    public bool ProductStockTaken { get; set; }
    /// <summary>The variant's count was lowered for this line, so cancelling gives it back.</summary>
    public bool VariantStockTaken { get; set; }
}

/// <summary>An online checkout for an order through the store's own Paystack account (reference ol_{orderId}_{random}).</summary>
public class OrderPayment : TenantEntity
{
    public Guid OrderId { get; set; }
    public required string Reference { get; set; }
    public long AmountMinor { get; set; }
    public required string Currency { get; set; }
    public OrderPaymentState Status { get; set; } = OrderPaymentState.Pending;
    /// <summary>What Paystack reported when it didn't match (Mismatch).</summary>
    public long? ChargedAmountMinor { get; set; }
    public string? ChargedCurrency { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    /// <summary>When the background check last asked Paystack about this checkout.</summary>
    public DateTimeOffset? RecheckedAt { get; set; }
}

public enum OrderPaymentState { Pending, Succeeded, Mismatch, Abandoned }

public class OrderStatusHistory : TenantEntity
{
    public Guid OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid? ChangedBy { get; set; }
}

public enum Fulfilment { Delivery, Pickup }
public enum OrderStatus { New, Confirmed, Paid, Delivered, Cancelled }
public enum OrderPaymentStatus { Unpaid, Paid }
