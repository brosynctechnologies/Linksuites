using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Courses;
using Saas.Domain.Payouts;

namespace Saas.Application.Payouts;

/// <summary>Emails Finance (and super admins); with nobody in Finance yet, the configured platform addresses, so it is never missed.</summary>
public sealed class FinanceAlerts(IAppDbContext db, NotificationQueue notifications, Saas.Application.Auth.PlatformOptions platform)
{
    /// <summary>Queues <paramref name="template"/> to every Finance address; saved with the caller's next SaveChanges.</summary>
    public async Task QueueAsync(string template, Dictionary<string, string> payload, CancellationToken ct)
    {
        var finance = await (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                             where a.RemovedAt == null && (a.Role == PlatformRole.Finance || a.Role == PlatformRole.SuperAdmin) && u.Email != null
                             select u.Email!).Distinct().ToListAsync(ct);
        if (finance.Count == 0)
            finance = platform.AdminEmails.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var email in finance) notifications.Email(email, template, new Dictionary<string, string>(payload));
    }
}

/// <summary>
/// Refunds of sales Paystack settled straight to the seller (a subaccount split). The seller already has their share, so the refund comes
/// out of the platform's balance: sellers can't refund those sales themselves, and when the platform does, the share is taken back from
/// the seller's next payouts with a negative earning (a clawback) and Finance is told the amount to recover.
/// </summary>
public sealed class Clawbacks(IAppDbContext db, FinanceAlerts finance, AppUrls urls, TimeProvider clock, ILogger<Clawbacks> logger)
{
    public const string SellerRefusal = "This sale was paid to you directly by Paystack. Ask LinkSuite support to refund it.";

    /// <summary>
    /// Posts the clawback of <paramref name="amountMinor"/> (the seller's settled share less what they keep) for the sale
    /// <paramref name="saleId"/> and queues Finance's email. Saved with the caller's SaveChanges. Nothing for a zero amount.
    /// </summary>
    public async Task AddAsync(Guid tenantId, EarningSource source, Guid saleId, string description, Guid buyerId, long amountMinor, string currency,
        string reference, CancellationToken ct, Guid? courseId = null, SaleChannel? channel = null)
    {
        if (amountMinor <= 0) return;
        var now = clock.GetUtcNow();
        db.Earnings.Add(new Earning
        {
            TenantId = tenantId, Source = source, SourceId = saleId, Description = $"Refund taken back: {description}", UserId = buyerId,
            CourseId = courseId, Channel = channel, GrossMinor = 0, FeeMinor = 0, NetMinor = -amountMinor, AvailableAt = now,
        });
        var business = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";
        logger.LogWarning("Refund of directly settled sale {Reference}: clawing back {Amount} from {Business}", reference, amountMinor, business);
        await finance.QueueAsync("payout.clawback", new()
        {
            ["business"] = business, ["amount"] = Money.Format(amountMinor, currency), ["reference"] = reference, ["sale"] = description,
            ["link"] = $"{urls.Web("admin")}/money/payouts",
        }, ct);
    }
}
