using System.Text;
using Saas.Application.Common;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed record PricedLine(Guid ProductId, Guid? VariantId, string Name, string? Variant, long UnitPriceMinor, int Quantity, int? StockQty = null)
{
    public long LineTotalMinor => UnitPriceMinor * Quantity;
}

public sealed record DiscountResult(long AmountMinor, string? AppliedCode, string? Message)
{
    public static readonly DiscountResult None = new(0, null, null);
}

/// <summary>Pure pricing rules, kept free of I/O so they are easy to unit test.</summary>
public static class OrderPricing
{
    public static DiscountResult ApplyDiscount(
        DiscountCode? code, string? enteredCode, long subtotalMinor, string currency, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(enteredCode)) return DiscountResult.None;
        if (code is null || !code.IsActive) return new(0, null, "This discount code is not valid.");
        if (code.ExpiresAt is { } expires && expires <= now) return new(0, null, "This discount code has expired.");
        if (code.MaxUses is { } max && code.Uses >= max) return new(0, null, "This discount code has been used up.");
        if (subtotalMinor < code.MinOrderMinor)
            return new(0, null, $"Spend at least {Money.Format(code.MinOrderMinor, currency)} to use this code.");

        var amount = code.Type switch
        {
            DiscountType.Percent => subtotalMinor * Math.Clamp(code.Value, 0, 100) / 100,
            DiscountType.Fixed => code.Value,
            _ => 0,
        };
        amount = Math.Clamp(amount, 0, subtotalMinor);
        return new(amount, code.Code, null);
    }

    public static long Total(long subtotalMinor, long deliveryFeeMinor, long discountMinor) =>
        Math.Max(0, subtotalMinor - discountMinor) + deliveryFeeMinor;
}

public static class WhatsAppMessage
{
    /// <summary>The pre-filled message the customer sends to the seller.</summary>
    public static string Build(Order order, string orderUrl)
    {
        var c = order.Currency;
        var sb = new StringBuilder();
        sb.AppendLine($"New order #{order.Number}");
        foreach (var item in order.Items)
        {
            var name = item.VariantSnapshot is { Length: > 0 } v ? $"{item.NameSnapshot} ({v})" : item.NameSnapshot;
            sb.AppendLine($"{item.Quantity} x {name} - {Money.Format(item.LineTotalMinor, c)}");
        }
        if (order.Fulfilment == Fulfilment.Delivery)
        {
            var zone = order.DeliveryZoneName is { Length: > 0 } z ? $" ({z})" : "";
            sb.AppendLine($"Delivery{zone} - {Money.Format(order.DeliveryFeeMinor, c)}");
        }
        else
        {
            sb.AppendLine("Pickup");
        }
        if (order.DiscountMinor > 0)
            sb.AppendLine($"Discount ({order.DiscountCode}) - -{Money.Format(order.DiscountMinor, c)}");
        sb.AppendLine($"Total: {Money.Format(order.TotalMinor, c)}");
        sb.AppendLine($"Name: {order.CustomerName}, {order.Phone}");
        if (order.Fulfilment == Fulfilment.Delivery && !string.IsNullOrWhiteSpace(order.Address))
            sb.AppendLine($"Address: {order.Address}");
        if (!string.IsNullOrWhiteSpace(order.Note)) sb.AppendLine($"Note: {order.Note}");
        sb.Append($"Order link: {orderUrl}");
        return sb.ToString();
    }

    public static string Link(string phoneDigits, string? text = null) =>
        text is null ? $"https://wa.me/{phoneDigits}" : $"https://wa.me/{phoneDigits}?text={Uri.EscapeDataString(text)}";
}
