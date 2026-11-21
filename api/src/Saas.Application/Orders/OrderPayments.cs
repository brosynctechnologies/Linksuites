using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

/// <summary><c>Force</c> changes the key even while customers are in the middle of paying.</summary>
public sealed record StorePaystackKeyRequest(string? SecretKey, bool Force = false);
/// <summary><c>Available</c>: the plan includes online payments. <c>Connected</c>: a key is saved (never returned).</summary>
public sealed record StorePaymentsDto(bool Connected, bool Available, string WebhookUrl);
public sealed record OrderPayRequest(string? Email);
public sealed record OrderPayResult(string AuthorizationUrl, string Reference);
public sealed record OrderReferenceRequest(string Reference);
public sealed record OrderPaymentDto(string Reference, long AmountMinor, string Currency, OrderPaymentState Status, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt);

public sealed class OrderPayRequestValidator : AbstractValidator<OrderPayRequest>
{
    public OrderPayRequestValidator() =>
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address, or leave it empty.");
}

/// <summary>
/// Online order payments into the store's own Paystack account (Pro), the way CareLink and SchoolLink take payments:
/// <list type="bullet">
/// <item>the owner saves the key (encrypted with <see cref="ISecretProtector"/>; 402 without the plan); a replaced key still confirms
/// checkouts for 7 days;</item>
/// <item>the customer starts a checkout from the order page: a Pending <see cref="OrderPayment"/> for the order total, saved before
/// Paystack is called, reference <c>ol_{orderId}_{random}</c>;</item>
/// <item>the per-store webhook, the return check and the background re-check all call <see cref="ConfirmAsync"/>, whose conditional
/// claim (Pending or Abandoned → Succeeded) applies a charge at most once;</item>
/// <item>a different amount or currency becomes Mismatch: the order stays unpaid and the owner is told;</item>
/// <item>a charge for an order that is already paid or cancelled is recorded and the owner is told to refund.</item>
/// </list>
/// </summary>
public sealed class OrderPaymentService(
    IAppDbContext db,
    ITenantContext tenant,
    PlanService plans,
    IMerchantPayments payments,
    ISecretProtector secrets,
    MerchantKeyRules keyRules,
    NotificationQueue notifications,
    InAppNotifications feed,
    AppUrls urls,
    TimeProvider clock,
    ILogger<OrderPaymentService> logger)
{
    public const string ReferencePrefix = "ol_";
    public const string PlanNeeded = "Online payments are on the Pro plan. Upgrade to let customers pay for orders online.";
    public const string NotSetUp = "This store doesn't take online payments.";
    public const string KeyChangeBusy = "Some customers are in the middle of paying. Try again in a few minutes, or choose to change it anyway.";
    public static readonly TimeSpan OpenCheckoutWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan PreviousKeyWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RecheckUntil = TimeSpan.FromDays(7);
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RecheckEveryLater = TimeSpan.FromHours(1);

    private Guid TenantId => tenant.TenantId ?? throw new ForbiddenException();

    public static string PayUrl(string orderUrl) => $"{orderUrl}?pay=1";

    /// <summary>No delivery zones and a delivery order: the fee is agreed on WhatsApp, so the total isn't final and can't be paid online.</summary>
    public static bool DeliveryToBeAgreed(Order o) => o.Fulfilment == Fulfilment.Delivery && o.DeliveryZoneId is null && o.DeliveryFeeMinor == 0;

    // ---------- Settings (owner sets the key; admins can look) ----------

    public async Task<StorePaymentsDto> GetSettingsAsync(CancellationToken ct) =>
        await SettingsAsync((await StoreAsync(ct)).PaystackSecretKeyProtected is not null, ct);

    public async Task<StorePaymentsDto> SetKeyAsync(StorePaystackKeyRequest r, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.OnlinePayments, ct)) throw new PlanLimitException(PlanNeeded);
        var key = keyRules.Validate(r.SecretKey);
        var store = await StoreAsync(ct);
        if (store.PaystackSecretKeyProtected is { } saved && secrets.Unprotect(saved) == key) return await SettingsAsync(true, ct);
        await EnsureNoOpenCheckoutsAsync(r.Force, ct);
        RetireCurrentKey(store);
        store.PaystackSecretKeyProtected = secrets.Protect(key);
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(true, ct);
    }

    public async Task<StorePaymentsDto> RemoveKeyAsync(bool force, CancellationToken ct)
    {
        var store = await StoreAsync(ct);
        if (store.PaystackSecretKeyProtected is not null)
        {
            await EnsureNoOpenCheckoutsAsync(force, ct);
            RetireCurrentKey(store);
            store.PaystackSecretKeyProtected = null;
            await db.SaveChangesAsync(ct);
        }
        return await SettingsAsync(false, ct);
    }

    private async Task EnsureNoOpenCheckoutsAsync(bool force, CancellationToken ct)
    {
        if (force) return;
        var since = clock.GetUtcNow() - OpenCheckoutWindow;
        if (await db.OrderPayments.AnyAsync(p => p.Status == OrderPaymentState.Pending && p.CreatedAt > since, ct))
            throw new ConflictException(KeyChangeBusy, "checkouts_open");
    }

    private void RetireCurrentKey(Store store)
    {
        if (store.PaystackSecretKeyProtected is null) return;
        store.PaystackPreviousKeyProtected = store.PaystackSecretKeyProtected;
        store.PreviousKeyUntil = clock.GetUtcNow() + PreviousKeyWindow;
    }

    private List<string> ConfirmationKeys(Store store)
    {
        var keys = new List<string>(2);
        if (store.PaystackSecretKeyProtected is { } current) keys.Add(secrets.Unprotect(current));
        if (store.PaystackPreviousKeyProtected is { } previous && store.PreviousKeyUntil > clock.GetUtcNow()) keys.Add(secrets.Unprotect(previous));
        return keys;
    }

    private async Task<StorePaymentsDto> SettingsAsync(bool connected, CancellationToken ct) =>
        new(connected, await plans.HasFeatureAsync(LimitKeys.OnlinePayments, ct), $"{urls.ApiBaseUrl.TrimEnd('/')}/api/webhooks/paystack/orders/{TenantId}");

    private async Task<Store> StoreAsync(CancellationToken ct)
    {
        _ = TenantId;
        return await db.Stores.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Store");
    }

    /// <summary>The store takes payments online: a Pro plan (online_payments) and a saved key. Call in the store's tenant.</summary>
    public async Task<bool> OnlineReadyAsync(Store store, CancellationToken ct) =>
        store.PaystackSecretKeyProtected is not null && await plans.HasFeatureAsync(LimitKeys.OnlinePayments, ct);

    /// <summary>Whether the order page offers "Pay online": the store takes payments, the business is active and the order is open, unpaid and final.</summary>
    public async Task<bool> CanPayAsync(Order order, Store store, CancellationToken ct) =>
        order.PaymentStatus == OrderPaymentStatus.Unpaid && order.Status != OrderStatus.Cancelled && order.TotalMinor > 0
        && !DeliveryToBeAgreed(order) && await OnlineReadyAsync(store, ct)
        && await db.Tenants.AnyAsync(t => t.Id == order.TenantId && t.Status == TenantStatus.Active, ct);

    // ---------- Checkout (the customer, from the order page) ----------

    public async Task<OrderPayResult> StartAsync(string code, OrderPayRequest r, CancellationToken ct)
    {
        var (order, store) = await ResolveAsync(code, ct);
        if (order.Status == OrderStatus.Cancelled) throw new ConflictException("This order was cancelled.", "order_cancelled");
        if (order.PaymentStatus == OrderPaymentStatus.Paid) throw new ConflictException("This order is already paid.", "order_paid");
        if (DeliveryToBeAgreed(order))
            throw new ConflictException("The delivery fee isn't agreed yet. Agree it with the store on WhatsApp first.", "delivery_fee_not_agreed");
        if (!await CanPayAsync(order, store, ct)) throw new ConflictException(NotSetUp, "online_payments_unavailable");

        var reference = $"{ReferencePrefix}{order.Id:N}_{Guid.NewGuid():N}"[..(ReferencePrefix.Length + 32 + 1 + 16)];
        db.OrderPayments.Add(new OrderPayment
        {
            TenantId = order.TenantId, OrderId = order.Id, Reference = reference, AmountMinor = order.TotalMinor, Currency = order.Currency,
        });
        // Saved before Paystack is called, so a webhook can never arrive for a reference we don't know.
        await db.SaveChangesAsync(ct);
        var email = string.IsNullOrWhiteSpace(r.Email) ? $"{order.Phone}@customers.orderlink.invalid" : r.Email.Trim();
        var url = await payments.InitializeAsync(secrets.Unprotect(store.PaystackSecretKeyProtected!), new PaymentInit(
            email, order.TotalMinor, order.Currency, reference, $"{urls.OrderUrl(order.Code)}?reference={Uri.EscapeDataString(reference)}",
            new() { ["tenantId"] = order.TenantId.ToString(), ["order"] = order.Number.ToString() }), ct);
        return new OrderPayResult(url, reference);
    }

    /// <summary>The return check: asks Paystack about one of this order's checkouts and confirms it as the webhook would. 404 for another order's reference.</summary>
    public async Task VerifyPublicAsync(string code, string reference, CancellationToken ct)
    {
        var (order, store) = await ResolveAsync(code, ct);
        await VerifyAsync(order.Id, store, reference, ct);
    }

    /// <summary>"Check with Paystack" on the order page in the dashboard.</summary>
    public async Task VerifyForStaffAsync(Guid orderId, string reference, CancellationToken ct)
    {
        if (!await db.Orders.AnyAsync(o => o.Id == orderId, ct)) throw new NotFoundException("Order");
        await VerifyAsync(orderId, await StoreAsync(ct), reference, ct);
    }

    private async Task VerifyAsync(Guid orderId, Store store, string reference, CancellationToken ct)
    {
        var payment = await db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Reference == reference && p.OrderId == orderId, ct)
            ?? throw new NotFoundException("Payment");
        if (!IsOpen(payment.Status)) return;
        var (charge, _, _) = await LookupAsync(ConfirmationKeys(store), reference, ct);
        if (charge is not null) await ConfirmAsync(payment.TenantId, charge, ct);
    }

    private static bool IsOpen(OrderPaymentState s) => s is OrderPaymentState.Pending or OrderPaymentState.Abandoned;

    private async Task<(VerifiedCharge? Charge, bool Unknown, bool KnownUnpaid)> LookupAsync(IEnumerable<string> keys, string reference, CancellationToken ct)
    {
        var unknown = false;
        var knownUnpaid = false;
        foreach (var key in keys)
        {
            try
            {
                var lookup = await payments.LookupAsync(key, reference, ct);
                if (lookup.Charge is { } c && c.Reference == reference) return (c, false, false);
                if (lookup.KnownUnpaid) knownUnpaid = true;
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Order payment {Reference}: Paystack was unavailable when verifying", reference);
                unknown = true;
            }
        }
        return (null, unknown, knownUnpaid);
    }

    private async Task<(Order, Store)> ResolveAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(o => o.Code == normalized, ct)
            ?? throw new NotFoundException("Order");
        tenant.SetTenant(order.TenantId);
        var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == order.StoreId, ct);
        return (order, store);
    }

    // ---------- Webhook ----------

    /// <summary>
    /// The store's own Paystack webhook. False (401) for an unknown store, no key or a bad signature. A signed event for an unknown
    /// reference is acknowledged and ignored. One signed only with the previous key is checked with Paystack before it is applied.
    /// </summary>
    public async Task<bool> HandleWebhookAsync(Guid tenantId, string rawBody, string? signature,
        Func<string, string, string?, bool> isValidSignature, Func<string, VerifiedCharge?> parse, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Product == Products.WhatsAppOrders, ct)) return false;
        tenant.SetTenant(tenantId);
        var store = await db.Stores.AsNoTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (store is null) return false;
        var keys = ConfirmationKeys(store);
        var current = store.PaystackSecretKeyProtected is { } protectedKey ? secrets.Unprotect(protectedKey) : null;
        var signedByCurrent = current is not null && isValidSignature(current, rawBody, signature);
        if (!signedByCurrent && !keys.Any(key => isValidSignature(key, rawBody, signature)))
        {
            logger.LogWarning("Rejected an order Paystack webhook for {TenantId}: no key or bad signature", tenantId);
            return false;
        }
        VerifiedCharge? charge;
        try { charge = parse(rawBody); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "Ignored an unreadable order Paystack webhook");
            return true;
        }
        if (charge is null || !charge.Reference.StartsWith(ReferencePrefix, StringComparison.Ordinal)) return true;
        if (signedByCurrent)
        {
            await ConfirmAsync(tenantId, charge, ct);
            return true;
        }
        if ((await LookupAsync(keys.AsEnumerable().Reverse(), charge.Reference, ct)).Charge is { } verified)
            await ConfirmAsync(tenantId, verified, ct);
        return true;
    }

    // ---------- Confirm ----------

    /// <summary>
    /// Applies a charge Paystack reports as successful, once: the checkout row is claimed with a conditional update, then the order
    /// is marked paid with another (Unpaid → Paid), so the webhook, the return check and the re-check arriving together apply it once.
    /// </summary>
    public async Task ConfirmAsync(Guid tenantId, VerifiedCharge charge, CancellationToken ct)
    {
        if (tenant.TenantId is { } current && current != tenantId) return; // never another store's reference
        if (tenant.TenantId is null) tenant.SetTenant(tenantId);
        var row = await db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Reference == charge.Reference, ct);
        if (row is null || !IsOpen(row.Status)) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var open = db.OrderPayments.Where(p => p.Id == row.Id && (p.Status == OrderPaymentState.Pending || p.Status == OrderPaymentState.Abandoned));
        var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == row.OrderId, ct);
        var store = await db.Stores.AsNoTracking().FirstAsync(s => s.Id == order.StoreId, ct);
        var orderLink = $"/app/orders/{order.Id}";

        if (charge.AmountMinor != row.AmountMinor || !string.Equals(charge.Currency, row.Currency, StringComparison.OrdinalIgnoreCase))
        {
            var currency = charge.Currency.ToUpperInvariant();
            var flagged = await open.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, OrderPaymentState.Mismatch)
                .SetProperty(p => p.ChargedAmountMinor, charge.AmountMinor)
                .SetProperty(p => p.ChargedCurrency, currency)
                .SetProperty(p => p.UpdatedAt, now), ct);
            if (flagged == 0) return;
            logger.LogWarning("Order payment {Reference}: Paystack reported {Amount} {Currency}, expected {Expected} {ExpectedCurrency}",
                charge.Reference, charge.AmountMinor, charge.Currency, row.AmountMinor, row.Currency);
            await TellOwnersAsync(store, order, "Online payment needs checking",
                $"Paystack reported {Money.Format(charge.AmountMinor, currency)} instead of {Money.Format(row.AmountMinor, row.Currency)} for order #{order.Number} (reference {charge.Reference}). The order was not marked paid.",
                orderLink, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        var claimed = await open.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, OrderPaymentState.Succeeded)
            .SetProperty(p => p.PaidAt, (DateTimeOffset?)now)
            .SetProperty(p => p.UpdatedAt, now), ct);
        if (claimed == 0) return; // the other caller got here first

        var first = await db.Orders.Where(o => o.Id == order.Id && o.PaymentStatus == OrderPaymentStatus.Unpaid)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentStatus, OrderPaymentStatus.Paid).SetProperty(o => o.UpdatedAt, now), ct) == 1;
        // Wave 4 review: a conditional update, never a save of a loaded order, so a cancel committed meanwhile is never undone.
        if (first)
            await db.Orders.Where(o => o.Id == order.Id && (o.Status == OrderStatus.New || o.Status == OrderStatus.Confirmed))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid), ct);
        var tracked = await db.Orders.AsNoTracking().Where(o => o.Id == order.Id).Select(o => new { o.Status }).FirstAsync(ct);
        var amount = Money.Format(row.AmountMinor, row.Currency);
        db.OrderStatusHistory.Add(new OrderStatusHistory { OrderId = order.Id, Status = tracked.Status, Note = $"Paid online, {amount} (Paystack {charge.Reference})" });

        if (!first)
            await TellOwnersAsync(store, order, $"Order #{order.Number} was paid twice",
                $"A second online payment of {amount} came in for order #{order.Number}, which was already paid. Refund one of them in your Paystack dashboard (reference {charge.Reference}).",
                orderLink, ct);
        else if (tracked.Status == OrderStatus.Cancelled)
            await TellOwnersAsync(store, order, $"Cancelled order #{order.Number} was paid",
                $"{order.CustomerName} paid {amount} online for order #{order.Number} after it was cancelled. Refund them in your Paystack dashboard, or agree a new order with them.",
                orderLink, ct);
        else
            await TellOwnersAsync(store, order, $"Order #{order.Number} paid online",
                $"{order.CustomerName} paid {amount} online for order #{order.Number}.", orderLink, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task TellOwnersAsync(Store store, Order order, string title, string body, string link, CancellationToken ct)
    {
        var owners = await db.Memberships.AsNoTracking()
            .Where(m => m.Role == MemberRole.Owner && m.Status == MembershipStatus.Active)
            .Select(m => new { m.UserId, m.User!.Email }).ToListAsync(ct);
        foreach (var owner in owners)
        {
            feed.Add(owner.UserId, Products.WhatsAppOrders, "order.payment", title, body, link, store.TenantId);
            if (!string.IsNullOrWhiteSpace(owner.Email))
                notifications.Email(owner.Email, "order.payment", new()
                {
                    ["storeName"] = store.Name,
                    ["title"] = title,
                    ["body"] = body,
                    ["link"] = $"{urls.WebBaseUrl.TrimEnd('/')}/dashboard/orders/{order.Id}",
                }, store.TenantId, owner.UserId);
        }
    }

    // ---------- Background upkeep (OrderJobs) ----------

    /// <summary>
    /// Re-checks stranded checkouts (webhook missed, customer never came back): Pending ones older than 15 minutes and younger than 7
    /// days, at most every 10 minutes in their first day and hourly after. A charge Paystack has is confirmed; one still not found after a
    /// day is marked Abandoned (soft: a later webhook or check still applies it). Runs in the store's tenant; returns how many changed.
    /// </summary>
    public async Task<int> RecheckPendingAsync(DateTimeOffset now, CancellationToken ct)
    {
        var store = await db.Stores.AsNoTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (store is null) return 0;
        var newest = now - RecheckAfter;
        var oldest = now - RecheckUntil;
        var dayOld = now - OpenCheckoutWindow;
        var soonDue = now - RecheckEvery;
        var lateDue = now - RecheckEveryLater;
        var pending = await db.OrderPayments.AsNoTracking()
            .Where(p => p.Status == OrderPaymentState.Pending && p.CreatedAt <= newest && p.CreatedAt > oldest)
            .Where(p => p.RecheckedAt == null || (p.CreatedAt > dayOld && p.RecheckedAt <= soonDue) || (p.CreatedAt <= dayOld && p.RecheckedAt <= lateDue))
            .OrderBy(p => p.CreatedAt).Take(200).ToListAsync(ct);
        var keys = ConfirmationKeys(store);
        var changed = 0;
        foreach (var payment in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var previous = payment.RecheckedAt;
                var claimed = await db.OrderPayments.Where(p => p.Id == payment.Id && p.Status == OrderPaymentState.Pending && p.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecheckedAt, (DateTimeOffset?)now), ct);
                if (claimed == 0) continue;
                var (charge, unknown, _) = await LookupAsync(keys, payment.Reference, ct);
                if (charge is not null)
                {
                    await ConfirmAsync(payment.TenantId, charge, ct);
                    changed++;
                }
                else if (!unknown && payment.CreatedAt <= dayOld)
                {
                    changed += await db.OrderPayments.Where(p => p.Id == payment.Id && p.Status == OrderPaymentState.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, OrderPaymentState.Abandoned).SetProperty(p => p.UpdatedAt, now), ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Order payment {Reference}: the stranded payment check failed", payment.Reference);
            }
        }
        return changed;
    }

    /// <summary>Forgets the previous Paystack key once its 7 days are over. Runs in the store's tenant.</summary>
    public async Task<bool> RetirePreviousKeyAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.Stores.Where(s => s.PreviousKeyUntil != null && s.PreviousKeyUntil <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.PaystackPreviousKeyProtected, (string?)null).SetProperty(x => x.PreviousKeyUntil, (DateTimeOffset?)null), ct) > 0;

    public async Task<IReadOnlyList<OrderPaymentDto>> ListForOrderAsync(Guid orderId, CancellationToken ct) =>
        await db.OrderPayments.AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.CreatedAt)
            .Select(p => new OrderPaymentDto(p.Reference, p.AmountMinor, p.Currency, p.Status, p.CreatedAt, p.PaidAt)).ToListAsync(ct);
}

/// <summary>
/// OrderLink's background upkeep, run every few minutes by the hosted job (and by tests by hand): for every active store with open
/// checkouts or a retiring key, the stranded payment re-check and retiring the previous Paystack key, each in its own scope with the
/// store's tenant set. A failure in one store is logged and the rest still run.
/// </summary>
public sealed class OrderJobs(IServiceScopeFactory scopes, ILogger<OrderJobs> logger)
{
    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> tenants;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var oldest = now - OrderPaymentService.RecheckUntil;
            tenants = await db.Tenants.AsNoTracking()
                .Where(t => t.Product == Products.WhatsAppOrders && t.Status == TenantStatus.Active)
                .Where(t => db.OrderPayments.IgnoreQueryFilters().Any(p => p.TenantId == t.Id && p.Status == OrderPaymentState.Pending && p.CreatedAt > oldest)
                    || db.Stores.IgnoreQueryFilters().Any(s => s.TenantId == t.Id && s.PreviousKeyUntil != null))
                .Select(t => t.Id).ToListAsync(ct);
        }
        foreach (var tenantId in tenants)
        {
            ct.ThrowIfCancellationRequested();
            await StepAsync(tenantId, "stranded payments", ct, sp => sp.GetRequiredService<OrderPaymentService>().RecheckPendingAsync(now, ct));
            await StepAsync(tenantId, "previous Paystack key", ct, sp => sp.GetRequiredService<OrderPaymentService>().RetirePreviousKeyAsync(now, ct));
        }
    }

    private async Task StepAsync(Guid tenantId, string step, CancellationToken ct, Func<IServiceProvider, Task> run)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
            await run(scope.ServiceProvider);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Order job step {Step} failed for store {TenantId}", step, tenantId);
        }
    }
}
