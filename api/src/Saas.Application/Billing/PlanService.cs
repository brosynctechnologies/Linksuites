using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Billing;

public static class Products
{
    public const string WhatsAppOrders = "whatsapp-orders";
    public const string Invoices = "invoices";
    public const string Bookings = "bookings";
    public const string Courses = "courses";
    public const string Property = "property";
    public const string School = "school";
    public const string Care = "care";

    public static readonly HashSet<string> All = [WhatsAppOrders, Invoices, Bookings, Courses, Property, School, Care];

    /// <summary>The name shown for platform emails that belong to no single product (admin console, an account on several products).</summary>
    public const string PlatformBrand = "LinkSuite";

    private static readonly Dictionary<string, string> Brands = new()
    {
        [WhatsAppOrders] = "OrderLink", [Invoices] = "InvoiceLink", [Bookings] = "BookLink", [Courses] = "LearnLink",
        [Property] = "PropertyLink", [School] = "SchoolLink", [Care] = "CareLink",
    };

    /// <summary>The product's own name (CareLink, InvoiceLink, ...), or <see cref="PlatformBrand"/> for none or an unknown code.</summary>
    public static string Brand(string? product) =>
        product is not null && Brands.TryGetValue(product.Trim().ToLowerInvariant(), out var name) ? name : PlatformBrand;
}

public static class LimitKeys
{
    public const string RemoveBranding = "remove_branding";
    public const string CustomDomain = "custom_domain";

    // WhatsApp Order System
    public const string Products = "products";
    public const string DeliveryZones = "delivery_zones";
    public const string DiscountCodes = "discount_codes";

    // Invoice Generator
    public const string InvoicesPerMonth = "invoices_per_month";
    public const string PremiumTemplates = "premium_templates";
    public const string Reminders = "reminders";
    public const string Recurring = "recurring";
    public const string PaymentLinks = "payment_links";
    public const string Expenses = "expenses";
    public const string Reports = "reports";
    public const string TeamSeats = "team_seats";

    // Booking System
    public const string StaffMembers = "staff_members";
    public const string BookingsPerMonth = "bookings_per_month";
    public const string Deposits = "deposits";
    public const string BookingReminders = "reminders";

    // Property Listing Website
    public const string Listings = "listings";
    public const string ListingExpiryDays = "listing_expiry_days";
    public const string ListingStats = "listing_stats";

    // Online Course Platform
    public const string Courses = "courses";
    public const string VideoStorageMb = "video_storage_mb";
    public const string AcademyFeePercent = "academy_fee_pct";
    public const string Coupons = "coupons";
    public const string Certificates = "certificates";

    // SchoolLink
    public const string Students = "students";
    public const string OnlineFees = "online_fees";

    // CareLink
    public const string Beds = "beds";
    public const string OnlinePayments = "online_payments";
}

/// <summary>
/// The one rule for which subscription a business is on, used by billing, entitlements and the admin console alike: of the
/// subscriptions whose period is running now, the one whose period ends last (an open-ended one first). None running means the
/// product's free plan.
/// </summary>
public static class CurrentSubscription
{
    public const string CompProvider = "comp";

    /// <summary>
    /// Active subscriptions whose period hasn't ended, the current one first: the highest plan in force wins (so a
    /// comp of a lower plan never takes away a plan the business paid for), then the one that runs longest.
    /// </summary>
    public static IOrderedQueryable<Subscription> Current(this IQueryable<Subscription> subscriptions, DateTimeOffset now) =>
        subscriptions.Where(s => s.Status == SubscriptionStatus.Active && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now))
            .OrderByDescending(s => s.Plan!.SortOrder).ThenByDescending(s => s.Plan!.PriceMinor)
            .ThenByDescending(s => s.CurrentPeriodEnd == null).ThenByDescending(s => s.CurrentPeriodEnd).ThenByDescending(s => s.CurrentPeriodStart);
}

public sealed class PlanService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>The current tenant's current subscription (see <see cref="CurrentSubscription"/>), with its plan; null on the free plan.</summary>
    public async Task<Subscription?> GetCurrentSubscriptionAsync(CancellationToken ct = default) => await CurrentSubscriptionAsync(await ProductAsync(ct), ct);

    private async Task<string> ProductAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        return await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Product).FirstAsync(ct);
    }

    private Task<Subscription?> CurrentSubscriptionAsync(string product, CancellationToken ct) =>
        db.Subscriptions.AsNoTracking().Include(s => s.Plan).Where(s => s.Plan!.Product == product)
            .Current(clock.GetUtcNow()).FirstOrDefaultAsync(ct);

    /// <summary>The plan the current tenant is on: its current subscription, or the product's free plan.</summary>
    public async Task<Plan> GetCurrentPlanAsync(CancellationToken ct = default)
    {
        var product = await ProductAsync(ct);
        var sub = await CurrentSubscriptionAsync(product, ct);
        if (sub?.Plan is { } plan)
            // The business gets what it paid for this period; later plan edits apply when it next pays (there is no automatic renewal).
            return new Plan
            {
                Id = plan.Id, Product = plan.Product, Code = plan.Code, Name = plan.Name, Interval = plan.Interval, IsDefault = plan.IsDefault,
                IsHidden = plan.IsHidden, SortOrder = plan.SortOrder, CreatedAt = plan.CreatedAt,
                PriceMinor = sub.PriceMinor ?? plan.PriceMinor, Currency = sub.Currency ?? plan.Currency,
                Limits = sub.Limits ?? plan.Limits, Features = sub.Features ?? plan.Features,
            };

        return await db.Plans.AsNoTracking().FirstAsync(p => p.Product == product && p.IsDefault, ct);
    }

    /// <summary>Throws a 402 when adding one more item would exceed the plan's limit.</summary>
    public async Task EnsureCanAddAsync(string limitKey, int currentCount, string itemName, CancellationToken ct = default)
    {
        var plan = await GetCurrentPlanAsync(ct);
        var limit = plan.Limit(limitKey);
        if (limit < 0 || currentCount < limit) return;
        throw new PlanLimitException(limit == 0
            ? $"{itemName} are not included in the {plan.Name} plan. Upgrade to use them."
            : $"The {plan.Name} plan allows up to {limit} {itemName.ToLowerInvariant()}. Upgrade to add more.");
    }

    public async Task<bool> HasFeatureAsync(string limitKey, CancellationToken ct = default) =>
        (await GetCurrentPlanAsync(ct)).Limit(limitKey) != 0;
}

/// <summary>Adds outbox rows; they are saved with the caller's SaveChanges and sent by the dispatcher.</summary>
public sealed class NotificationQueue(IAppDbContext db)
{
    public void Email(string to, string templateKey, Dictionary<string, string> payload, Guid? tenantId = null, Guid? userId = null) =>
        db.Notifications.Add(new Notification
        {
            TenantId = tenantId,
            UserId = userId,
            Channel = NotificationChannel.Email,
            Recipient = to,
            TemplateKey = templateKey,
            Payload = payload,
        });
}
