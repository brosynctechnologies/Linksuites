using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Application.Courses;
using Saas.Application.Property;
using Saas.Domain.Courses;
using Saas.Domain.Property;

namespace Saas.Application.Billing;

/// <summary>
/// The platform's own sales: course orders (<c>crs_</c>), short-let stays (<c>stay_</c>) and listing boosts (<c>bst_</c>). A new checkout
/// uses the provider chosen in Payment settings (Paystack or OPay); everything after it (the return check, callbacks, the re-check and
/// refunds) uses the provider stored on the record, so payments taken before a switch keep working.
/// </summary>
public sealed class SalesGateways(PlanGateways gateways, PaymentSettings settings, ILogger<SalesGateways> logger)
{
    public const string OfflineMessage = "Online payments aren't set up yet. Please try again later.";

    /// <summary>
    /// The gateway for a new checkout: the provider chosen in Payment settings. Online payment off, or the chosen provider without its keys,
    /// is refused (503) before anything is created.
    /// </summary>
    public async Task<IPlanPaymentGateway> ForCheckoutAsync(CancellationToken ct)
    {
        var current = await settings.GetAsync(ct);
        if (!current.OnlinePaymentsEnabled) throw new ServiceUnavailableException(OfflineMessage);
        var gateway = gateways.For(current.PlanProvider);
        if (gateway?.IsConfigured != true)
        {
            logger.LogWarning("Checkout refused: the payment provider {Provider} is not configured", current.PlanProvider);
            throw new ServiceUnavailableException(OfflineMessage);
        }
        return gateway;
    }

    /// <summary>
    /// The page the payer comes back to. OPay doesn't add the reference to it (Paystack adds <c>?reference=</c> itself), so for OPay it is
    /// added here; OPay's cancel link adds <c>cancelled=1</c> after it.
    /// </summary>
    public static string ReturnUrl(IPaymentGateway gateway, string page, string reference) =>
        gateway.Name == BillingOptions.Opay ? $"{page}{(page.Contains('?') ? '&' : '?')}reference={Uri.EscapeDataString(reference)}" : page;

    /// <summary>
    /// Asks the provider that took <paramref name="reference"/> whether it was paid. Null when it wasn't (yet), when the provider can't
    /// answer, or when this server doesn't have that provider. A paid answer carries the provider's amount and currency, never the payer's.
    /// </summary>
    public async Task<VerifiedCharge?> PaidAsync(string provider, string reference, CancellationToken ct)
    {
        var gateway = gateways.For(provider);
        if (gateway is null)
        {
            logger.LogWarning("Payment {Reference} was taken by {Provider}, which this server doesn't have", reference, provider);
            return null;
        }
        try
        {
            var status = await gateway.GetStatusAsync(reference, ct);
            return status.State == ProviderChargeState.Success && !string.IsNullOrWhiteSpace(status.Currency)
                ? new VerifiedCharge(reference, status.AmountMinor, status.Currency, DateTimeOffset.UtcNow) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Payment {Reference}: {Provider} could not be asked ({Error})", reference, provider, ex.Message);
            return null;
        }
    }

    /// <summary>Refunds through the provider that took the payment. Throws (503) when it refuses or isn't there.</summary>
    public Task RefundAsync(string provider, string reference, long amountMinor, CancellationToken ct) =>
        gateways.For(provider) is { } gateway
            ? gateway.RefundAsync(reference, amountMinor, ct)
            : throw new ServiceUnavailableException($"This payment was taken by {provider}, which isn't set up on this server, so it can't be refunded here.");
}

/// <summary>
/// The one "ask the provider, then confirm" step for course orders, stays and boosts, behind the OPay callback and the re-check job.
/// The callback's body is never trusted: only the provider's answer (status, amount, currency) confirms anything, through the same
/// confirm paths as the return check. Each record's <c>RecheckedAt</c> is claimed with a conditional update first, so a flood of
/// callbacks (or two job runs) can't drive the provider's API.
/// </summary>
public sealed class SalesPaymentChecks(IAppDbContext db, SalesGateways gateways, CourseCore courses, StayService stays, BoostService boosts,
    TimeProvider clock, ILogger<SalesPaymentChecks> logger)
{
    /// <summary>How often a callback may ask the provider about one record.</summary>
    public static readonly TimeSpan CallbackCooldown = BillingService.StatusCooldown;

    public static bool IsSale(string reference) =>
        reference.StartsWith("crs_", StringComparison.Ordinal) || reference.StartsWith("stay_", StringComparison.Ordinal)
        || reference.StartsWith("bst_", StringComparison.Ordinal);

    /// <summary>
    /// Asks the record's provider about <paramref name="reference"/> unless it was asked within <paramref name="cooldown"/>, and confirms it
    /// when paid. True when the provider was asked and said paid. Unknown references, and records already settled, are left alone.
    /// </summary>
    public async Task<bool> CheckAsync(string reference, TimeSpan cooldown, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var asked = now - cooldown;
        string? provider;
        if (reference.StartsWith("crs_", StringComparison.Ordinal))
        {
            var row = await db.CourseOrders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Reference == reference)
                .Select(o => new { o.Provider, o.Status, o.RecheckedAt }).FirstOrDefaultAsync(ct);
            if (row is null || row.Status != OrderState.Pending || row.RecheckedAt > asked) return Unknown(row is null, reference);
            var previous = row.RecheckedAt;
            if (await db.CourseOrders.IgnoreQueryFilters().Where(o => o.Reference == reference && o.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.RecheckedAt, (DateTimeOffset?)now), ct) == 0) return false;
            provider = row.Provider;
            if (await gateways.PaidAsync(provider, reference, ct) is not { } charge) return false;
            await courses.ConfirmOrderAsync(reference, charge.AmountMinor, charge.Currency, ct);
            return true;
        }
        if (reference.StartsWith("stay_", StringComparison.Ordinal))
        {
            var row = await db.Stays.IgnoreQueryFilters().AsNoTracking().Where(s => s.Reference == reference)
                .Select(s => new { s.Provider, s.Status, s.PaidAt, s.RefundMinor, s.RecheckedAt }).FirstOrDefaultAsync(ct);
            // Unpaid stays: waiting for payment, past their hold, or cancelled before the payment arrived (confirming refunds those).
            var open = row is not null && row.PaidAt == null
                && (row.Status is StayStatus.Pending or StayStatus.Expired || (row.Status == StayStatus.Cancelled && row.RefundMinor == 0));
            if (!open || row!.RecheckedAt > asked) return Unknown(row is null, reference);
            var previous = row.RecheckedAt;
            if (await db.Stays.IgnoreQueryFilters().Where(s => s.Reference == reference && s.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecheckedAt, (DateTimeOffset?)now), ct) == 0) return false;
            if (await gateways.PaidAsync(row.Provider, reference, ct) is not { } charge) return false;
            await stays.ConfirmAsync(reference, charge.AmountMinor, charge.Currency, ct);
            return true;
        }
        if (reference.StartsWith("bst_", StringComparison.Ordinal))
        {
            var row = await db.Boosts.IgnoreQueryFilters().AsNoTracking().Where(b => b.Reference == reference)
                .Select(b => new { b.Provider, b.Status, b.RecheckedAt }).FirstOrDefaultAsync(ct);
            if (row is null || row.Status != BoostStatus.Pending || row.RecheckedAt > asked) return Unknown(row is null, reference);
            var previous = row.RecheckedAt;
            if (await db.Boosts.IgnoreQueryFilters().Where(b => b.Reference == reference && b.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.RecheckedAt, (DateTimeOffset?)now), ct) == 0) return false;
            if (await gateways.PaidAsync(row.Provider, reference, ct) is not { } charge) return false;
            await boosts.ConfirmAsync(reference, charge.AmountMinor, charge.Currency, ct);
            return true;
        }
        return Unknown(true, reference);
    }

    private bool Unknown(bool missing, string reference)
    {
        if (missing) logger.LogWarning("Payment callback for unknown reference {Reference}", reference);
        return false;
    }
}

/// <summary>
/// Re-checks course orders, stays and boosts whose callback or return never came (like <see cref="PlanPaymentJobs"/> for plans): each
/// unpaid record created between <see cref="PlanPaymentJobs.RecheckAfter"/> and <see cref="PlanPaymentJobs.RecheckWindow"/> ago is asked
/// about at most every <see cref="PlanPaymentJobs.RecheckEvery"/>, each in its own scope, within <see cref="PlanPaymentJobs.RecheckBudget"/>.
/// </summary>
public sealed class SalesPaymentJobs(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SalesPaymentJobs> logger)
{
    public const int BatchSize = 300;

    /// <summary>Returns how many records the provider said were paid.</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var newest = now - PlanPaymentJobs.RecheckAfter;
        var oldest = now - PlanPaymentJobs.RecheckWindow;
        var lastAsked = now - PlanPaymentJobs.RecheckEvery;
        var due = new List<string>();
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            due.AddRange(await db.CourseOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Status == OrderState.Pending && o.TotalMinor > 0 && o.CreatedAt <= newest && o.CreatedAt > oldest
                            && (o.RecheckedAt == null || o.RecheckedAt <= lastAsked))
                .OrderBy(o => o.CreatedAt).Take(BatchSize).Select(o => o.Reference).ToListAsync(ct));
            due.AddRange(await db.Stays.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.PaidAt == null && (s.Status == StayStatus.Pending || s.Status == StayStatus.Expired
                                                 || (s.Status == StayStatus.Cancelled && s.RefundMinor == 0))
                            && s.CreatedAt <= newest && s.CreatedAt > oldest && (s.RecheckedAt == null || s.RecheckedAt <= lastAsked))
                .OrderBy(s => s.CreatedAt).Take(BatchSize).Select(s => s.Reference).ToListAsync(ct));
            due.AddRange(await db.Boosts.IgnoreQueryFilters().AsNoTracking()
                .Where(b => b.Status == BoostStatus.Pending && b.CreatedAt <= newest && b.CreatedAt > oldest
                            && (b.RecheckedAt == null || b.RecheckedAt <= lastAsked))
                .OrderBy(b => b.CreatedAt).Take(BatchSize).Select(b => b.Reference).ToListAsync(ct));
        }
        var paid = 0;
        var started = clock.GetTimestamp();
        foreach (var reference in due)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) > PlanPaymentJobs.RecheckBudget)
            {
                logger.LogWarning("Sales payments: the re-check ran out of time; the rest are checked on the next run");
                break;
            }
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<SalesPaymentChecks>().CheckAsync(reference, PlanPaymentJobs.RecheckEvery, ct)) paid++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Sales payment {Reference}: the re-check failed", reference);
            }
        }
        return paid;
    }
}
