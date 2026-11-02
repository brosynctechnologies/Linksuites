using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed record OrderSummaryDto(
    Guid Id, long Number, string Code, string CustomerName, string Phone, Fulfilment Fulfilment, long TotalMinor,
    string Currency, OrderStatus Status, OrderPaymentStatus PaymentStatus, int ItemCount, DateTimeOffset CreatedAt);

public sealed record OrderItemDto(
    Guid? ProductId, Guid? VariantId, string Name, string? Variant, long UnitPriceMinor, int Quantity, long LineTotalMinor);

public sealed record OrderHistoryDto(OrderStatus Status, string? Note, DateTimeOffset CreatedAt);

public sealed record OrderDto(
    Guid Id, long Number, string Code, string CustomerName, string Phone, Fulfilment Fulfilment, long TotalMinor,
    string Currency, OrderStatus Status, OrderPaymentStatus PaymentStatus, int ItemCount, DateTimeOffset CreatedAt,
    string? Address, string? Note, string? DeliveryZoneName, long SubtotalMinor, long DeliveryFeeMinor,
    long DiscountMinor, string? DiscountCode, IReadOnlyList<OrderItemDto> Items, IReadOnlyList<OrderHistoryDto> History,
    string? WhatsappMessage, string CustomerWhatsappUrl);

public sealed record UpdateOrderStatusRequest(OrderStatus Status, string? Note);
public sealed record UpdateOrderPaymentRequest(OrderPaymentStatus PaymentStatus);

public sealed record DashboardDto(
    string Currency, int TodayOrders, long TodaySalesMinor, long MonthSalesMinor, int NewOrders, int ProductCount,
    IReadOnlyList<OrderSummaryDto> RecentOrders);

public sealed class OrderAdminService(IAppDbContext db, ICurrentUser user, StoreSettingsService stores, TimeProvider clock)
{
    /// <summary>
    /// <paramref name="paymentStatus"/> filters on whether the order is paid, which "Mark paid" sets. The dashboard's
    /// "Paid" tab uses it, because an order is paid whatever stage of fulfilment it is at.
    /// </summary>
    public async Task<Paged<OrderSummaryDto>> ListAsync(OrderStatus? status, string? q, int page, int pageSize, CancellationToken ct,
        OrderPaymentStatus? paymentStatus = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Orders.AsNoTracking();
        if (status is { } s) query = query.Where(o => o.Status == s);
        if (paymentStatus is { } ps) query = query.Where(o => o.PaymentStatus == ps);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().TrimStart('#').ToLower();
            query = long.TryParse(term, out var number)
                ? query.Where(o => o.Number == number || o.Phone.Contains(term))
                : query.Where(o => o.CustomerName.ToLower().Contains(term) || o.Code.ToLower() == term);
        }
        var total = await query.CountAsync(ct);
        var items = await Summaries(query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(ct);
        return new Paged<OrderSummaryDto>(items, page, pageSize, total);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking().Include(x => x.Items).Include(x => x.History)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Order");
        return new OrderDto(o.Id, o.Number, o.Code, o.CustomerName, o.Phone, o.Fulfilment, o.TotalMinor, o.Currency,
            o.Status, o.PaymentStatus, o.Items.Sum(i => i.Quantity), o.CreatedAt, o.Address, o.Note, o.DeliveryZoneName,
            o.SubtotalMinor, o.DeliveryFeeMinor, o.DiscountMinor, o.DiscountCode,
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.VariantId, i.NameSnapshot, i.VariantSnapshot, i.UnitPriceMinor, i.Quantity, i.LineTotalMinor)).ToList(),
            o.History.OrderBy(h => h.CreatedAt).Select(h => new OrderHistoryDto(h.Status, h.Note, h.CreatedAt)).ToList(),
            o.WhatsappMessage, WhatsAppMessage.Link(o.Phone));
    }

    public async Task<OrderDto> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest r, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Order");
        if (order.Status == r.Status) return await GetAsync(id, ct);
        // Cancelling puts the stock back on sale, so a cancelled order is final: the customer orders again instead.
        if (order.Status == OrderStatus.Cancelled)
            throw new ConflictException("A cancelled order can't be reopened. Ask the customer to place a new order.", "order_cancelled");
        if (order.Status == OrderStatus.Delivered && r.Status == OrderStatus.New)
            throw new ConflictException("A finished order cannot go back to New.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (r.Status == OrderStatus.Cancelled)
        {
            // Claim the cancellation in the database first, so two cancels at once give the stock back only once.
            var claimed = await db.Orders.Where(o => o.Id == id && o.Status != OrderStatus.Cancelled)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Cancelled), ct);
            if (claimed == 0) return await GetAsync(id, ct);
            await RestockAsync(order.Id, ct);
            // Ruling: the discount use stays consumed. Giving it back would let a "first order" or limited code be reused.
        }

        order.Status = r.Status;
        if (r.Status == OrderStatus.Paid) order.PaymentStatus = OrderPaymentStatus.Paid;
        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = r.Status,
            Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(),
            ChangedBy = user.UserId,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Gives the order's quantities back to products that still track stock.</summary>
    private async Task RestockAsync(Guid orderId, CancellationToken ct)
    {
        var items = await db.Orders.AsNoTracking().Where(o => o.Id == orderId).SelectMany(o => o.Items).Where(i => i.ProductId != null)
            .GroupBy(i => i.ProductId!.Value).Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) }).ToListAsync(ct);
        foreach (var item in items)
        {
            var qty = item.Quantity;
            await db.Products.Where(p => p.Id == item.ProductId && p.StockQty != null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, p => p.StockQty + qty), ct);
        }
    }

    public async Task<OrderDto> UpdatePaymentAsync(Guid id, UpdateOrderPaymentRequest r, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Order");
        if (order.PaymentStatus != r.PaymentStatus)
        {
            order.PaymentStatus = r.PaymentStatus;
            // The Paid stage means "paid", so an unpaid order can't stay in it: step back to Confirmed.
            if (r.PaymentStatus == OrderPaymentStatus.Unpaid && order.Status == OrderStatus.Paid) order.Status = OrderStatus.Confirmed;
            db.OrderStatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Status = order.Status,
                Note = r.PaymentStatus == OrderPaymentStatus.Paid ? "Marked as paid" : "Marked as unpaid",
                ChangedBy = user.UserId,
            });
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<DashboardDto> DashboardAsync(CancellationToken ct)
    {
        var store = await stores.CurrentStoreAsync(ct);
        var timezone = await db.Tenants.Where(t => t.Id == store.TenantId).Select(t => t.Timezone).FirstAsync(ct);
        var (todayStart, monthStart) = PeriodStarts(clock.GetUtcNow(), timezone);

        var live = db.Orders.Where(o => o.Status != OrderStatus.Cancelled);
        var todayOrders = await live.CountAsync(o => o.CreatedAt >= todayStart, ct);
        var todaySales = await live.Where(o => o.CreatedAt >= todayStart).SumAsync(o => (long?)o.TotalMinor, ct) ?? 0;
        var monthSales = await live.Where(o => o.CreatedAt >= monthStart).SumAsync(o => (long?)o.TotalMinor, ct) ?? 0;
        var newOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.New, ct);
        var productCount = await db.Products.CountAsync(ct);
        var recent = await Summaries(db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(8)).ToListAsync(ct);
        return new DashboardDto(store.Currency, todayOrders, todaySales, monthSales, newOrders, productCount, recent);
    }

    /// <summary>Start of today and of this month in the store's time zone, as UTC instants.</summary>
    public static (DateTimeOffset Today, DateTimeOffset Month) PeriodStarts(DateTimeOffset nowUtc, string timezone)
    {
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var found) ? found : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(nowUtc, tz);
        var today = new DateTimeOffset(local.Date, tz.GetUtcOffset(local.Date)).ToUniversalTime();
        var monthDate = new DateTime(local.Year, local.Month, 1);
        var month = new DateTimeOffset(monthDate, tz.GetUtcOffset(monthDate)).ToUniversalTime();
        return (today, month);
    }

    private static IQueryable<OrderSummaryDto> Summaries(IQueryable<Order> orders) =>
        orders.Select(o => new OrderSummaryDto(o.Id, o.Number, o.Code, o.CustomerName, o.Phone, o.Fulfilment, o.TotalMinor,
            o.Currency, o.Status, o.PaymentStatus, o.Items.Sum(i => i.Quantity), o.CreatedAt));
}
