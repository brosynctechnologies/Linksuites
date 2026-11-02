using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Billing;

public sealed record PaymentInit(string Email, long AmountMinor, string Currency, string Reference, string CallbackUrl, Dictionary<string, string> Metadata);

/// <summary>A payment provider (Paystack today; Flutterwave or Stripe can implement the same interface).</summary>
public interface IPaymentGateway
{
    string Name { get; }
    bool IsConfigured { get; }
    /// <summary>Starts a hosted checkout and returns the URL to send the payer to.</summary>
    Task<string> InitializeAsync(PaymentInit init, CancellationToken ct);
    /// <summary>Asks the provider whether a charge succeeded.</summary>
    Task<VerifiedCharge?> VerifyAsync(string reference, CancellationToken ct);
    Task RefundAsync(string reference, long amountMinor, CancellationToken ct);
}

public sealed record PlanDto(
    string Code, string Name, long PriceMinor, string Currency, BillingInterval Interval,
    IReadOnlyDictionary<string, long> Limits, IReadOnlyList<string> Features);

/// <summary>
/// The current period. Nothing renews automatically: the plan ends at <see cref="CurrentPeriodEnd"/> unless the business pays again.
/// <see cref="Complimentary"/> marks a comp given by the platform, which a payment never extends.
/// </summary>
public sealed record SubscriptionDto(SubscriptionStatus Status, DateTimeOffset? CurrentPeriodEnd, bool Complimentary);
public sealed record UsageItemDto(string Key, string Label, long Used, long Limit);

/// <summary><c>Products</c>/<c>MaxProducts</c> are kept for the WhatsApp Order System web app; <c>Items</c> works for every product.</summary>
public sealed record UsageDto(int Products, long MaxProducts, IReadOnlyList<UsageItemDto> Items);

/// <summary>Reports what a tenant has used against its plan limits, per product.</summary>
public interface IUsageProvider
{
    string Product { get; }
    Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct);
}

public sealed record VerifiedCharge(string Reference, long AmountMinor, string Currency, DateTimeOffset PaidAt);

/// <summary>
/// What the provider says about a reference: the successful <see cref="Charge"/>; or that it knows the reference but it wasn't paid
/// (<see cref="UnpaidStatus"/>, e.g. "abandoned" or "failed"); or neither, when the account doesn't know the reference at all.
/// </summary>
public sealed record ChargeLookup(VerifiedCharge? Charge, string? UnpaidStatus)
{
    public static readonly ChargeLookup NotFound = new(null, null);
    public bool KnownUnpaid => Charge is null && UnpaidStatus is not null;
}

/// <summary>
/// The checks every product applies to a merchant's Paystack secret key before saving it. Keys starting with
/// <see cref="LocalFixtureKeyPrefix"/> confirm payments in-process without Paystack, so they are only accepted where
/// <c>Paystack:AllowLocalFixtureKeys</c> is on (appsettings.Development.json); anywhere else they are refused at save.
/// </summary>
public sealed class MerchantKeyRules(bool allowLocalFixtureKeys)
{
    public const string LocalFixtureKeyPrefix = "sk_test_schoollink";
    public const string FixtureKeyRefused = "This key only works on a development machine. Paste the secret key from your Paystack dashboard.";

    public bool AllowLocalFixtureKeys { get; } = allowLocalFixtureKeys;

    public static bool IsLocalFixtureKey(string secretKey) => secretKey.StartsWith(LocalFixtureKeyPrefix, StringComparison.Ordinal);

    /// <summary>Returns the trimmed key, or throws a 400 field error on <c>secretKey</c>.</summary>
    public string Validate(string? secretKey)
    {
        var key = secretKey?.Trim() ?? "";
        if (!(key.StartsWith("sk_test_", StringComparison.Ordinal) || key.StartsWith("sk_live_", StringComparison.Ordinal)) || key.Length < 20)
            throw BadRequestException.Field("secretKey", "Paste your Paystack secret key. It starts with sk_live_ or sk_test_.");
        if (!AllowLocalFixtureKeys && IsLocalFixtureKey(key))
            throw BadRequestException.Field("secretKey", FixtureKeyRefused);
        return key;
    }
}

/// <summary>Payments collected for a business with that business's own provider key (money goes straight to them).</summary>
public interface IMerchantPayments
{
    Task<string> InitializeAsync(string secretKey, PaymentInit init, CancellationToken ct);
    /// <summary>Returns the charge if the provider confirms it succeeded, otherwise null.</summary>
    Task<VerifiedCharge?> VerifyAsync(string secretKey, string reference, CancellationToken ct);
    /// <summary>
    /// Like <see cref="VerifyAsync"/>, but also tells "known but not paid" apart from "not found". Throws <see cref="HttpRequestException"/>
    /// when the provider can't answer (5xx, 429, network, timeout).
    /// </summary>
    Task<ChargeLookup> LookupAsync(string secretKey, string reference, CancellationToken ct);
    /// <summary>Refunds all or part of a successful charge.</summary>
    Task RefundAsync(string secretKey, string reference, long amountMinor, CancellationToken ct);
}
public sealed record BillingDto(PlanDto CurrentPlan, SubscriptionDto? Subscription, IReadOnlyList<PlanDto> Plans, UsageDto Usage, bool PaymentsEnabled);
public sealed record CheckoutRequest(string PlanCode);
public sealed record CheckoutResult(string AuthorizationUrl);

public sealed class BillingService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    PlanService plans,
    IPaymentGateway gateway,
    IEnumerable<IUsageProvider> usageProviders,
    AppUrls urls,
    TimeProvider clock,
    ILogger<BillingService> logger)
{
    public async Task<BillingDto> GetAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var current = await plans.GetCurrentPlanAsync(ct);
        var product = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Product).FirstAsync(ct);
        // Hidden plans aren't offered, but a business already on one still sees it.
        var all = await db.Plans.AsNoTracking().Where(p => p.Product == product && (!p.IsHidden || p.Id == current.Id)).OrderBy(p => p.SortOrder).ToListAsync(ct);
        var sub = await plans.GetCurrentSubscriptionAsync(ct); // the same rule that picked the current plan
        var productCount = product == Products.WhatsAppOrders ? await db.Products.CountAsync(ct) : 0;
        var items = new List<UsageItemDto>();
        foreach (var provider in usageProviders.Where(u => u.Product == product))
            items.AddRange(await provider.GetAsync(current, ct));
        return new BillingDto(ToDto(current), sub is null ? null : new SubscriptionDto(sub.Status, sub.CurrentPeriodEnd, sub.Provider == CurrentSubscription.CompProvider),
            all.Select(ToDto).ToList(), new UsageDto(productCount, current.Limit(LimitKeys.Products), items), gateway.IsConfigured);
    }

    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest r, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        if (!gateway.IsConfigured) throw new ServiceUnavailableException("Online payments are not configured yet.");
        var product = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Product).FirstAsync(ct);
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Product == product && p.Code == r.PlanCode
            && (!p.IsHidden || db.Subscriptions.Any(s => s.PlanId == p.Id)), ct)
            ?? throw new NotFoundException("Plan");
        if (plan.PriceMinor <= 0) throw BadRequestException.Field("planCode", "Choose a paid plan.");
        var email = await db.Users.Where(u => u.Id == user.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException();

        var payment = new Payment
        {
            PayerUserId = user.UserId,
            AmountMinor = plan.PriceMinor,
            Currency = plan.Currency,
            Provider = gateway.Name,
            ProviderRef = $"sub_{Codes.Random(16).ToLowerInvariant()}",
            Purpose = "subscription",
            Metadata = new() { ["planId"] = plan.Id.ToString(), ["planCode"] = plan.Code },
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var url = await gateway.InitializeAsync(new PaymentInit(email, payment.AmountMinor, payment.Currency, payment.ProviderRef,
            $"{urls.Web(product)}{BillingPath(product)}?ref={payment.ProviderRef}",
            new() { ["tenantId"] = tenantId.ToString(), ["purpose"] = payment.Purpose }), ct);
        return new CheckoutResult(url);
    }

    /// <summary>Where each product's web app keeps its billing page, so Paystack returns the payer there.</summary>
    public static string BillingPath(string product) => product switch
    {
        Products.WhatsAppOrders => "/dashboard/billing",
        Products.Invoices or Products.Bookings => "/app/billing",
        Products.Courses or Products.Property => "/studio/billing",
        _ => "/app", // SchoolLink has no billing page yet: back to the dashboard
    };

    /// <summary>
    /// Called from a verified provider webhook. Idempotent: a reference that is already marked paid is ignored,
    /// so provider retries are safe.
    /// </summary>
    public async Task ConfirmPaymentAsync(string reference, long amountMinor, string currency, CancellationToken ct)
    {
        var payment = await db.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.ProviderRef == reference, ct);
        if (payment is null)
        {
            logger.LogWarning("Webhook for unknown payment reference {Reference}", reference);
            return;
        }
        if (payment.Status == PaymentStatus.Succeeded) return;
        tenant.SetTenant(payment.TenantId);

        if (payment.AmountMinor != amountMinor || !string.Equals(payment.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Payment {Reference} amount mismatch: expected {Expected} {Currency}, got {Actual} {ActualCurrency}",
                reference, payment.AmountMinor, payment.Currency, amountMinor, currency);
            payment.Status = PaymentStatus.Failed;
            await db.SaveChangesAsync(ct);
            return;
        }

        var now = clock.GetUtcNow();
        payment.Status = PaymentStatus.Succeeded;
        payment.PaidAt = now;

        if (payment.Purpose == "subscription" && Guid.TryParse(payment.Metadata.GetValueOrDefault("planId"), out var planId))
        {
            var plan = await db.Plans.FirstAsync(p => p.Id == planId, ct);
            var active = await db.Subscriptions.Where(s => s.Status == SubscriptionStatus.Active).ToListAsync(ct);
            // A comp is never extended by a payment: paying starts (or extends) a real paid subscription.
            var same = active.FirstOrDefault(s => s.PlanId == planId && s.Provider != "comp");
            if (same is not null)
            {
                var from = same.CurrentPeriodEnd is { } end && end > now ? end : now;
                same.CurrentPeriodEnd = AddInterval(from, plan.Interval);
                same.ProviderRef = reference;
                same.Snapshot(plan, payment.AmountMinor, payment.Currency); // renewal picks up the plan as it is now
            }
            else
            {
                foreach (var s in active.Where(s => s.Provider != "comp")) { s.Status = SubscriptionStatus.Cancelled; s.CancelAt = now; }
                var subscription = new Subscription
                {
                    PlanId = planId,
                    Status = SubscriptionStatus.Active,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = AddInterval(now, plan.Interval),
                    Provider = payment.Provider,
                    ProviderRef = reference,
                };
                subscription.Snapshot(plan, payment.AmountMinor, payment.Currency);
                db.Subscriptions.Add(subscription);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset AddInterval(DateTimeOffset from, BillingInterval interval) =>
        interval switch
        {
            BillingInterval.Year => from.AddYears(1),
            BillingInterval.Term => from.AddMonths(4),
            _ => from.AddMonths(1),
        };

    private static PlanDto ToDto(Plan p) => new(p.Code, p.Name, p.PriceMinor, p.Currency, p.Interval, p.Limits, p.Features);
}
