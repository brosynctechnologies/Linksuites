using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Payouts;
using Saas.Domain.Courses;
using Saas.Domain.Payouts;

namespace Saas.Application.Courses;

/// <summary>
/// Refunding a course order, for the tutor (within the refund window) and for Finance (any time). The order is claimed first with a
/// conditional update, so a double click refunds once. Only a definite refusal from the provider puts the order back; no clear answer
/// leaves it refunded and pending until the sales re-check confirms it. A sale Paystack settled to the tutor is Finance's to refund, and
/// its refund claws the tutor's share back.
/// </summary>
public sealed class CourseRefunds(IAppDbContext db, CourseCore core, SalesGateways gateways, Clawbacks clawbacks)
{
    /// <summary>Finance's refund from the admin console. True when the refund is pending (no clear answer from the provider).</summary>
    public async Task<bool> AdminRefundAsync(Guid orderId, CancellationToken ct)
    {
        var tenantId = await db.CourseOrders.IgnoreQueryFilters().Where(o => o.Id == orderId).Select(o => (Guid?)o.TenantId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Order");
        core.UseTenantOf(tenantId);
        var order = await db.CourseOrders.FirstAsync(o => o.Id == orderId, ct);
        if (order.Status != OrderState.Paid) throw new ConflictException(order.Status == OrderState.Refunded ? "This order has already been refunded." : "Only paid orders can be refunded.");
        return await RefundAsync(order, byPlatform: true, ct);
    }

    /// <summary>Refunds <paramref name="order"/> (tracked, its business's tenant in use). True when the refund is pending.</summary>
    public async Task<bool> RefundAsync(CourseOrder order, bool byPlatform, CancellationToken ct)
    {
        var earning = await db.Earnings.AsNoTracking().FirstOrDefaultAsync(e => e.Source == EarningSource.CourseOrder && e.SourceId == order.Id, ct);
        var settled = earning?.SettledByProvider == true;
        if (settled && !byPlatform) throw new ConflictException(Clawbacks.SellerRefusal);
        var now = core.Now;
        var attempts = order.RefundAttempts;
        var attempt = attempts + 1;
        var claimed = await db.CourseOrders.Where(o => o.Id == order.Id && o.Status == OrderState.Paid && o.RefundAttempts == attempts)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderState.Refunded).SetProperty(o => o.RefundedAt, (DateTimeOffset?)now)
                .SetProperty(o => o.RefundAttempts, attempt), ct);
        if (claimed == 0) throw new ConflictException("This order has already been refunded.");
        var sent = true;
        if (order.TotalMinor > 0)
        {
            try
            {
                sent = await gateways.RefundAsync(order.Provider, order.Reference, order.TotalMinor, attempt, ct);
            }
            catch (RefundRefusedException)
            {
                // The provider definitely didn't take it: the order is Paid again. The attempt stays counted, so a retry is a new refund.
                await db.CourseOrders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderState.Paid)
                    .SetProperty(o => o.RefundedAt, (DateTimeOffset?)null), CancellationToken.None);
                throw;
            }
        }
        // From here the refund stands (sent, or maybe sent): nothing below may be skipped because the caller went away.
        ct = CancellationToken.None;
        if (!sent)
            await db.CourseOrders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.RefundPendingSince, (DateTimeOffset?)now), ct);
        await db.Earnings.Where(e => e.Source == EarningSource.CourseOrder && e.SourceId == order.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Reversed, true), ct);
        if (settled)
            await clawbacks.AddAsync(order.TenantId, EarningSource.CourseClawback, order.Id, earning!.Description, order.UserId, earning.NetMinor, order.Currency,
                order.Reference, ct, earning.CourseId, earning.Channel);
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.CourseId == order.CourseId && e.UserId == order.UserId, ct);
        if (enrollment is not null && !enrollment.Revoked)
        {
            enrollment.Revoked = true;
            await db.Courses.Where(c => c.Id == order.CourseId).ExecuteUpdateAsync(s => s.SetProperty(c => c.StudentsCount, c => c.StudentsCount - 1), ct);
        }
        await db.SaveChangesAsync(ct);
        return !sent;
    }
}
