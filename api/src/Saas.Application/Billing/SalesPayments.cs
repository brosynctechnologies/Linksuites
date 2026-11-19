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

    /// <summary>
    /// Refunds through the provider that took the payment. True when the provider took it; false when its answer was unclear (a timeout,
    /// 5xx or network error: the refund may or may not have gone through, so the caller marks it pending for the re-check to settle).
    /// Throws <see cref="RefundRefusedException"/> (503) when the provider definitely didn't take it, or isn't on this server. The caller's
    /// token isn't passed on: a browser giving up mustn't turn a sent refund into an unknown one.
    /// </summary>
    public async Task<bool> RefundAsync(string provider, string reference, long amountMinor, int attempt, CancellationToken ct)
    {
        _ = ct;
        var gateway = gateways.For(provider)
            ?? throw new RefundRefusedException($"This payment was taken by {provider}, which isn't set up on this server, so it can't be refunded here.");
        try
        {
            await gateway.RefundAsync(reference, amountMinor, attempt, CancellationToken.None);
            return true;
        }
        catch (RefundRefusedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Refund {Attempt} of {Reference} with {Provider}: no clear answer; kept pending for the re-check", attempt, reference, provider);
            return false;
        }
    }

    /// <summary>The provider's word on refund <paramref name="attempt"/> of <paramref name="reference"/>; Unknown when it can't say.</summary>
    public async Task<RefundState> RefundStatusAsync(string provider, string reference, int attempt, CancellationToken ct)
    {
        if (gateways.For(provider) is not { } gateway) return RefundState.Unknown;
        try
        {
            return await gateway.RefundStatusAsync(reference, attempt, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Refund {Attempt} of {Reference}: {Provider} could not be asked", attempt, reference, provider);
            return RefundState.Unknown;
        }
    }
}

/// <summary>
/// The one "ask the provider, then confirm" step for course orders, stays and boosts, behind the OPay callback and the re-check job.
/// The callback's body is never trusted: only the provider's answer (status, amount, currency) confirms anything, through the same
/// confirm paths as the return check. Each record's <c>RecheckedAt</c> is claimed with a conditional update first, so a flood of
/// callbacks (or two job runs) can't drive the provider's API.
/// </summary>
public sealed class SalesPaymentChecks(IAppDbContext db, SalesGateways gateways, CourseCore courses, StayService stays, BoostService boosts,
    TimeProvider clock, ILogger<SalesPaymentChecks> logger, Saas.Application.Payouts.FinanceAlerts finance)
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

    /// <summary>A refund still unconfirmed after this long is reported to Finance.</summary>
    public static readonly TimeSpan RefundDecideWithin = TimeSpan.FromHours(24);

    /// <summary>
    /// Settles a refund the provider gave no clear answer to (<c>RefundPendingSince</c> set): asks the provider about the refund's
    /// current attempt. Refunded clears the flag; a refund the provider reports failed, or one still unconfirmed after
    /// <see cref="RefundDecideWithin"/>, is reported to Finance once. Each record is asked at most every <paramref name="cooldown"/>.
    /// </summary>
    public async Task<RefundState?> ReconcileRefundAsync(string reference, DateTimeOffset now, TimeSpan cooldown, CancellationToken ct)
    {
        var asked = now - cooldown;
        if (reference.StartsWith("crs_", StringComparison.Ordinal))
        {
            var row = await db.CourseOrders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Reference == reference && o.RefundPendingSince != null)
                .Select(o => new { o.TenantId, o.Provider, o.RefundAttempts, o.RefundPendingSince, o.RefundAlertedAt, o.RecheckedAt, o.TotalMinor, o.Currency })
                .FirstOrDefaultAsync(ct);
            if (row is null || row.RecheckedAt > asked) return null;
            var previous = row.RecheckedAt;
            if (await db.CourseOrders.IgnoreQueryFilters().Where(o => o.Reference == reference && o.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.RecheckedAt, (DateTimeOffset?)now), ct) == 0) return null;
            var state = await gateways.RefundStatusAsync(row.Provider, reference, Math.Max(1, row.RefundAttempts), ct);
            var orders = db.CourseOrders.IgnoreQueryFilters().Where(o => o.Reference == reference && o.RefundPendingSince != null);
            if (state == RefundState.Refunded)
            {
                await orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.RefundPendingSince, (DateTimeOffset?)null), ct);
                logger.LogInformation("Refund of {Reference} confirmed by {Provider}", reference, row.Provider);
            }
            else if (row.RefundAlertedAt is null && (state == RefundState.Failed || now - row.RefundPendingSince!.Value >= RefundDecideWithin)
                     && await orders.Where(o => o.RefundAlertedAt == null).ExecuteUpdateAsync(s => s.SetProperty(o => o.RefundAlertedAt, (DateTimeOffset?)now), ct) == 1)
                await AlertUnconfirmedAsync("Course sale", row.TenantId, reference, row.Provider, row.TotalMinor, row.Currency, state, ct);
            return state;
        }
        if (reference.StartsWith("stay_", StringComparison.Ordinal))
        {
            var row = await db.Stays.IgnoreQueryFilters().AsNoTracking().Where(s => s.Reference == reference && s.RefundPendingSince != null)
                .Select(s => new { s.TenantId, s.Provider, s.RefundAttempts, s.RefundPendingSince, s.RefundAlertedAt, s.RecheckedAt, s.RefundMinor, s.Currency })
                .FirstOrDefaultAsync(ct);
            if (row is null || row.RecheckedAt > asked) return null;
            var previous = row.RecheckedAt;
            if (await db.Stays.IgnoreQueryFilters().Where(s => s.Reference == reference && s.RecheckedAt == previous)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecheckedAt, (DateTimeOffset?)now), ct) == 0) return null;
            var state = await gateways.RefundStatusAsync(row.Provider, reference, Math.Max(1, row.RefundAttempts), ct);
            var stays = db.Stays.IgnoreQueryFilters().Where(s => s.Reference == reference && s.RefundPendingSince != null);
            if (state == RefundState.Refunded)
            {
                await stays.ExecuteUpdateAsync(s => s.SetProperty(x => x.RefundPendingSince, (DateTimeOffset?)null), ct);
                logger.LogInformation("Refund of {Reference} confirmed by {Provider}", reference, row.Provider);
            }
            else if (row.RefundAlertedAt is null && (state == RefundState.Failed || now - row.RefundPendingSince!.Value >= RefundDecideWithin)
                     && await stays.Where(s => s.RefundAlertedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RefundAlertedAt, (DateTimeOffset?)now), ct) == 1)
                await AlertUnconfirmedAsync("Short-let stay", row.TenantId, reference, row.Provider, row.RefundMinor, row.Currency, state, ct);
            return state;
        }
        return null;
    }

    private async Task AlertUnconfirmedAsync(string kind, Guid tenantId, string reference, string provider, long amountMinor, string currency,
        RefundState state, CancellationToken ct)
    {
        var business = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";
        logger.LogWarning("Refund of {Reference} ({Provider}) unconfirmed: {State}; Finance alerted", reference, provider, state);
        await finance.QueueAsync("refund.unconfirmed", new()
        {
            ["kind"] = kind, ["business"] = business, ["reference"] = reference, ["provider"] = provider, ["amount"] = Money.Format(amountMinor, currency),
            ["status"] = state == RefundState.Failed ? $"{provider} reports the refund failed" : $"{provider} hasn't confirmed the refund for a day",
        }, ct);
        await db.SaveChangesAsync(ct);
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
        await ReconcileRefundsAsync(now, lastAsked, started, ct);
        return paid;
    }

    /// <summary>
    /// Refunds the provider gave no clear answer to (course orders and stays with <c>RefundPendingSince</c>): each is asked about at most
    /// every <see cref="PlanPaymentJobs.RecheckEvery"/>, in its own scope, until the provider confirms it (or Finance is told).
    /// </summary>
    private async Task ReconcileRefundsAsync(DateTimeOffset now, DateTimeOffset lastAsked, long started, CancellationToken ct)
    {
        var due = new List<string>();
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            due.AddRange(await db.CourseOrders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.RefundPendingSince != null && (o.RecheckedAt == null || o.RecheckedAt <= lastAsked))
                .OrderBy(o => o.RefundPendingSince).Take(BatchSize).Select(o => o.Reference).ToListAsync(ct));
            due.AddRange(await db.Stays.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.RefundPendingSince != null && (s.RecheckedAt == null || s.RecheckedAt <= lastAsked))
                .OrderBy(s => s.RefundPendingSince).Take(BatchSize).Select(s => s.Reference).ToListAsync(ct));
        }
        foreach (var reference in due)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) > PlanPaymentJobs.RecheckBudget) break;
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SalesPaymentChecks>().ReconcileRefundAsync(reference, now, PlanPaymentJobs.RecheckEvery, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Refund of {Reference}: the re-check failed", reference);
            }
        }
    }
}
