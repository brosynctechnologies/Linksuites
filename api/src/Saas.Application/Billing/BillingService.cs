using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// <summary>
/// <see cref="BankTransfer"/>: the company account to pay a plan into, offered only while online payment is off and all three details are
/// set. <see cref="PendingTransfer"/>: this business's transfer claim waiting for a platform admin, if any.
/// </summary>
public sealed record BillingDto(PlanDto CurrentPlan, SubscriptionDto? Subscription, IReadOnlyList<PlanDto> Plans, UsageDto Usage, bool PaymentsEnabled,
    BankTransferDto? BankTransfer = null, PendingTransferDto? PendingTransfer = null);
/// <summary>
/// <see cref="Reference"/>: what the business writes in the transfer note. It is the reference its next claim gets (the same until a claim
/// is made, so it survives a reload), so the note and the claim the admin sees match.
/// </summary>
public sealed record BankTransferDto(string BankName, string AccountNumber, string AccountName, string Reference);
public sealed record PendingTransferDto(string Reference, string PlanCode, string PlanName, long AmountMinor, string Currency, DateTimeOffset CreatedAt);
/// <summary><see cref="Reference"/>: the reference the page showed (`bankTransfer.reference`); a different one is refused, so the note and the claim match.</summary>
public sealed record TransferRequest(string PlanCode, string? Reference = null);
public sealed record CheckoutRequest(string PlanCode);
public sealed record CheckoutResult(string AuthorizationUrl);
public sealed record VerifyPaymentRequest(string Reference);
public enum PlanPaymentResult { Paid, Pending, Failed }
/// <summary>The return check's answer: whether the payment went through, and the plan it pays for.</summary>
public sealed record PlanPaymentCheckDto(PlanPaymentResult Status, PlanDto? Plan);

/// <summary>
/// Re-checks stranded plan payments (callback missed, payer never came back), like CareLink's online-payment recheck: each Pending plan
/// payment older than <see cref="RecheckAfter"/> is asked about at most every <see cref="RecheckEvery"/>; one still unconfirmed after
/// <see cref="OpenWindow"/> is marked Abandoned (soft: a later confirmation still applies); none older than <see cref="RecheckWindow"/>
/// is looked at. Each payment runs in its own scope.
/// </summary>
public sealed class PlanPaymentJobs(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PlanPaymentJobs> logger)
{
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OpenWindow = TimeSpan.FromHours(24);
    /// <summary>
    /// Only plan payments created in the last 48 hours are re-checked (fix round I1). Older Pending ones are left alone: no sweep of
    /// historical checkouts against the provider and no retroactive activation; a callback or return check still confirms them.
    /// </summary>
    public static readonly TimeSpan RecheckWindow = TimeSpan.FromHours(48);
    public static readonly TimeSpan RecheckBudget = TimeSpan.FromMinutes(2);
    public const int BatchSize = 500;

    /// <summary>Returns how many payments changed (confirmed, failed or abandoned).</summary>
    public async Task<int> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        List<Guid> due;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var newest = now - RecheckAfter;
            var oldest = now - RecheckWindow;
            var lastAsked = now - RecheckEvery;
            due = await db.Payments.IgnoreQueryFilters().AsNoTracking()
                // Bank transfers have no provider to ask: a platform admin confirms them, however long that takes.
                .Where(p => p.Purpose == "subscription" && p.Status == PaymentStatus.Pending && p.Provider != PaymentSettingsRules.TransferProvider
                            && p.CreatedAt <= newest && p.CreatedAt > oldest
                            && (p.RecheckedAt == null || p.RecheckedAt <= lastAsked))
                .OrderBy(p => p.CreatedAt).Take(BatchSize).Select(p => p.Id).ToListAsync(ct);
        }
        var changed = 0;
        var started = clock.GetTimestamp();
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(started) > RecheckBudget)
            {
                logger.LogWarning("Plan payments: the re-check ran out of time; the rest are checked on the next run");
                break;
            }
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<BillingService>().RecheckAsync(id, now, ct)) changed++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Plan payment {PaymentId}: the re-check failed", id);
            }
        }
        return changed;
    }
}

public sealed class BillingService(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    PlanService plans,
    PlanGateways gateways,
    PaymentSettings settings,
    NotificationQueue notifications,
    Saas.Application.Auth.PlatformOptions platform,
    BillingOptions billingOptions,
    IDbErrors dbErrors,
    IEnumerable<IUsageProvider> usageProviders,
    AppUrls urls,
    TimeProvider clock,
    ILogger<BillingService> logger)
{
    public const string NotConfiguredMessage = "Online payments for plans aren't set up yet, so plans can't be bought online right now. Please try again later.";
    public const string OfflineMessage = "Online payment for plans is off right now. You can pay for your plan by bank transfer instead.";
    public const string Transfer = PaymentSettingsRules.TransferProvider;

    public async Task<BillingDto> GetAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var payments = await settings.GetAsync(ct);
        var paymentsEnabled = payments.OnlinePaymentsEnabled && gateways.For(payments.PlanProvider)?.IsConfigured == true;
        var bank = !payments.OnlinePaymentsEnabled && payments.HasBankDetails
            ? new BankTransferDto(payments.BankName!, payments.BankAccountNumber!, payments.BankAccountName!, await NextTransferReferenceAsync(tenantId, ct))
            : null;
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
            all.Select(ToDto).ToList(), new UsageDto(productCount, current.Limit(LimitKeys.Products), items), paymentsEnabled,
            bank, await PendingTransferAsync(ct));
    }

    /// <summary>This business's transfer claim waiting for a platform admin (the tenant filter keeps it to its own).</summary>
    private async Task<PendingTransferDto?> PendingTransferAsync(CancellationToken ct)
    {
        var claim = await db.Payments.AsNoTracking()
            .Where(p => p.Provider == Transfer && p.Purpose == "subscription" && p.Status == PaymentStatus.Pending)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);
        return claim is null ? null : ToPendingDto(claim);
    }

    private static PendingTransferDto ToPendingDto(Payment p) => new(p.ProviderRef, p.Metadata.GetValueOrDefault("planCode") ?? "",
        p.Metadata.GetValueOrDefault("planName") ?? "", p.AmountMinor, p.Currency, p.CreatedAt);

    /// <summary>A paid plan this business can buy (hidden plans only by their own subscribers), or 404; a free plan is a 400.</summary>
    private async Task<Plan> PaidPlanAsync(string product, string? planCode, CancellationToken ct)
    {
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Product == product && p.Code == planCode
            && (!p.IsHidden || db.Subscriptions.Any(s => s.PlanId == p.Id)), ct)
            ?? throw new NotFoundException("Plan");
        if (plan.PriceMinor <= 0) throw BadRequestException.Field("planCode", "Choose a paid plan.");
        return plan;
    }

    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest r, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var payments = await settings.GetAsync(ct);
        if (!payments.OnlinePaymentsEnabled)
        {
            logger.LogInformation("Plan checkout refused: online payment is switched off in the admin console");
            throw new ServiceUnavailableException(payments.HasBankDetails ? OfflineMessage : NotConfiguredMessage);
        }
        var gateway = gateways.For(payments.PlanProvider);
        if (gateway is null || !gateway.IsConfigured)
        {
            logger.LogWarning("Plan checkout refused: the plan provider {Provider} is not configured", payments.PlanProvider);
            throw new ServiceUnavailableException(NotConfiguredMessage);
        }
        var product = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Product).FirstAsync(ct);
        var plan = await PaidPlanAsync(product, r.PlanCode, ct);
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
            new() { ["tenantId"] = tenantId.ToString(), ["purpose"] = payment.Purpose, ["planName"] = plan.Name }), ct);
        return new CheckoutResult(url);
    }

    /// <summary>
    /// "I've sent the transfer": records a Pending plan payment with provider <see cref="Transfer"/>, the plan's own price and a short
    /// reference for the transfer note, and emails the platform's finance admins. One claim per business: a new one replaces (abandons)
    /// the pending one. Only while online payment is off and the bank details are set. Runs under the business's plan lock, so it never
    /// interleaves with an admin confirming the previous claim.
    /// </summary>
    public async Task<PendingTransferDto> ClaimTransferAsync(TransferRequest r, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var payments = await settings.GetAsync(ct);
        if (payments.OnlinePaymentsEnabled) throw new ConflictException("Online payment is available, so pay for your plan online instead.");
        if (!payments.HasBankDetails) throw new ConflictException("Paying by bank transfer isn't available right now. Please try again later.");
        var business = await db.Tenants.Where(t => t.Id == tenantId).Select(t => new { t.Name, t.Product }).FirstAsync(ct);
        var plan = await PaidPlanAsync(business.Product, r.PlanCode, ct);
        var claimant = await db.Users.Where(u => u.Id == user.UserId).Select(u => new { u.FullName, u.Email }).FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException();
        var shown = r.Reference?.Trim();

        // A reference taken by another payment between our check and the save (a unique index violation) is retried with a fresh one.
        for (var attempt = 0; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.LockAsync(PlanLockKey(tenantId), ct);
            var reference = attempt == 0 ? await NextTransferReferenceAsync(tenantId, ct) : await NewTransferReferenceAsync(ct);
            if (attempt == 0 && !string.IsNullOrEmpty(shown) && !string.Equals(shown, reference, StringComparison.Ordinal))
                throw new ConflictException(StaleReferenceMessage);
            var now = clock.GetUtcNow();
            // Admins are emailed about a business's claims at most once an hour; later claims are still listed in the console.
            var emailedRecently = await db.Payments.IgnoreQueryFilters()
                .AnyAsync(p => p.TenantId == tenantId && p.Provider == Transfer && p.CreatedAt > now - ClaimEmailEvery, ct);
            await db.Payments.Where(p => p.Provider == Transfer && p.Purpose == "subscription" && p.Status == PaymentStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Abandoned).SetProperty(p => p.UpdatedAt, now), ct);

            var payment = new Payment
            {
                PayerUserId = user.UserId,
                AmountMinor = plan.PriceMinor,
                Currency = plan.Currency,
                Provider = Transfer,
                ProviderRef = reference,
                Purpose = "subscription",
                Metadata = new() { ["planId"] = plan.Id.ToString(), ["planCode"] = plan.Code, ["planName"] = plan.Name },
            };
            db.Payments.Add(payment);
            if (!emailedRecently)
            {
                var payload = new Dictionary<string, string>
                {
                    ["business"] = business.Name, ["productName"] = Products.Brand(business.Product), ["plan"] = plan.Name,
                    ["amount"] = Money.Format(plan.PriceMinor, plan.Currency), ["reference"] = reference, ["claimedBy"] = claimant.FullName,
                    ["link"] = $"{urls.Web("admin")}/money/transfers",
                };
                foreach (var email in await FinanceAdminEmailsAsync(ct)) notifications.Email(email, "plan.transfer_claimed", new(payload));
            }
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (attempt < 2 && dbErrors.IsUniqueViolation(ex))
            {
                logger.LogWarning("Bank transfer reference {Reference} was taken while claiming; trying a fresh one", reference);
                db.ChangeTracker.Clear();
                continue; // the transaction rolls back on dispose
            }
            logger.LogInformation("Bank transfer claim {Reference} for the {Plan} plan", reference, plan.Code);
            return ToPendingDto(payment);
        }
    }

    public const string StaleReferenceMessage = "Refresh the page; the reference changed. Use the new reference in your transfer note.";
    /// <summary>The platform admins are emailed about one business's transfer claims at most this often.</summary>
    public static readonly TimeSpan ClaimEmailEvery = TimeSpan.FromHours(1);

    /// <summary>Withdraws this business's pending transfer claim (Abandoned). Nothing pending is not an error.</summary>
    public async Task CancelTransferAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(PlanLockKey(tenantId), ct);
        var now = clock.GetUtcNow();
        await db.Payments.Where(p => p.Provider == Transfer && p.Purpose == "subscription" && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Abandoned).SetProperty(p => p.UpdatedAt, now), ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// The reference this business's next transfer claim gets: <c>LS-</c> and six letters or digits that can't be misread (no 0, O, 1, I
    /// or L), an HMAC (keyed with <see cref="BillingOptions.TransferReferenceKey"/>, so it can't be worked out from outside) of the business
    /// and how many claims it has made. The billing page shows the same one until a claim is made and the claim then uses it. If another
    /// payment already has it, the next derivation is tried, so the page and the claim still agree.
    /// </summary>
    private async Task<string> NextTransferReferenceAsync(Guid tenantId, CancellationToken ct)
    {
        var made = await db.Payments.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId && p.Provider == Transfer, ct);
        var key = System.Text.Encoding.UTF8.GetBytes(billingOptions.TransferReferenceKey);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var hash = System.Security.Cryptography.HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes($"{tenantId:N}:{made}:{attempt}"));
            var reference = $"LS-{Codes.FromBytes(hash, 6)}";
            if (!await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.ProviderRef == reference, ct)) return reference;
        }
        return await NewTransferReferenceAsync(ct);
    }

    /// <summary>A random transfer reference not used by any payment yet; throws after ten taken ones rather than returning an unchecked one.</summary>
    private async Task<string> NewTransferReferenceAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var reference = $"LS-{Codes.Random(6)}";
            if (!await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.ProviderRef == reference, ct)) return reference;
        }
        throw new ServiceUnavailableException("We couldn't make a transfer reference just now. Please try again.");
    }

    /// <summary>
    /// Who is told about a transfer claim: every active super admin and finance admin. With none, the configured platform emails
    /// (<c>Platform:AdminEmails</c>), logged as an error, so a claim never goes unseen.
    /// </summary>
    private async Task<List<string>> FinanceAdminEmailsAsync(CancellationToken ct)
    {
        var team = await (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                          where a.RemovedAt == null && (a.Role == PlatformRole.SuperAdmin || a.Role == PlatformRole.Finance) && u.Email != null
                          select u.Email!).Distinct().ToListAsync(ct);
        if (team.Count > 0) return team;
        team = platform.AdminEmails.Select(e => e.Trim()).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        logger.LogError("No active super or finance admin to tell about a bank transfer claim. {Outcome}",
            team.Count > 0 ? $"Sent it to the {team.Count} address(es) in Platform:AdminEmails." : "Platform:AdminEmails is empty, so nobody was emailed.");
        return team;
    }

    /// <summary>
    /// Where each product's web app keeps its billing page, so the provider returns the payer there with <c>?ref=</c>; each of these
    /// pages runs the return check (<see cref="VerifyAsync"/>).
    /// </summary>
    public static string BillingPath(string product) => product switch
    {
        Products.WhatsAppOrders => "/dashboard/billing",
        Products.Invoices or Products.Bookings or Products.School => "/app/billing",
        Products.Courses or Products.Property => "/studio/billing",
        _ => "/app/settings", // CareLink: the plan card lives in settings
    };

    /// <summary>
    /// The return check: asks the provider that took the payment (not the configured one) and confirms on SUCCESS with a matching amount.
    /// Only the signed-in business's own payments are found (the tenant filter); anything else is 404.
    /// </summary>
    public async Task<PlanPaymentCheckDto> VerifyAsync(VerifyPaymentRequest r, CancellationToken ct)
    {
        if (tenant.TenantId is null) throw new ForbiddenException();
        var reference = r.Reference?.Trim() ?? "";
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.ProviderRef == reference && p.Purpose == "subscription", ct)
            ?? throw new NotFoundException("Payment");
        var status = await CheckWithProviderAsync(payment.Id, slotClaimed: false, ct);
        return new PlanPaymentCheckDto(status, await PlanForAsync(payment, ct));
    }

    /// <summary>
    /// How often the callback and the return check may ask a provider about one payment (fix round I3). Within it they answer from what
    /// is stored, so a flood of callbacks or return checks can't drive the provider's API. The re-check job claims the same slot.
    /// </summary>
    public static readonly TimeSpan StatusCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The per-business plan lock: every plan payment confirmation for one business runs under it, so two confirmations (of one payment,
    /// or of two payments) never interleave their read and change of the business's subscriptions.
    /// </summary>
    public static long PlanLockKey(Guid tenantId) => BitConverter.ToInt64(tenantId.ToByteArray(), 8) ^ 0x0B111_9001;

    /// <summary>
    /// A provider callback named <paramref name="reference"/>: the provider is asked for the status before anything changes, so the
    /// callback's own body is never trusted. Unknown references are logged and ignored.
    /// </summary>
    public async Task ConfirmFromProviderAsync(string reference, CancellationToken ct)
    {
        var paymentId = await db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference && p.Purpose == "subscription")
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (paymentId is null)
        {
            logger.LogWarning("Payment callback for unknown plan payment reference {Reference}", reference);
            return;
        }
        await CheckWithProviderAsync(paymentId.Value, slotClaimed: false, ct);
    }

    /// <summary>
    /// One step of <see cref="PlanPaymentJobs"/>: claims the payment's re-check slot (so a second instance doesn't ask at the same moment),
    /// asks its provider, and marks it Abandoned once it is over <see cref="PlanPaymentJobs.OpenWindow"/> old and still unconfirmed.
    /// True when the payment changed.
    /// </summary>
    public async Task<bool> RecheckAsync(Guid paymentId, DateTimeOffset now, CancellationToken ct)
    {
        var payment = await db.Payments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null || payment.Status != PaymentStatus.Pending || payment.Provider == Transfer) return false;
        var previous = payment.RecheckedAt;
        var claimed = await db.Payments.IgnoreQueryFilters()
            .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending && p.RecheckedAt == previous)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecheckedAt, (DateTimeOffset?)now), ct);
        if (claimed == 0) return false;

        var result = await CheckWithProviderAsync(paymentId, slotClaimed: true, ct);
        if (result != PlanPaymentResult.Pending) return true;
        if (payment.CreatedAt > now - PlanPaymentJobs.OpenWindow) return false;
        db.ChangeTracker.Clear();
        logger.LogInformation("Plan payment {Reference} was not confirmed within 24 hours; marked Abandoned", payment.ProviderRef);
        return await db.Payments.IgnoreQueryFilters().Where(p => p.Id == paymentId && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Abandoned).SetProperty(p => p.UpdatedAt, now), ct) > 0;
    }

    /// <summary>
    /// The one confirm path behind the callback, the return check and the re-check. Paid and Refunded payments aren't asked about again;
    /// Pending, Abandoned (soft) and Failed ones are. A provider that can't answer leaves the payment as it is (Pending reads "still confirming").
    /// The callback and the return check first claim the payment's status slot (<see cref="StatusCooldown"/>); the re-check job has
    /// already claimed it (<paramref name="slotClaimed"/>). Every decision reads the row fresh; nothing here writes a stale entity.
    /// </summary>
    private async Task<PlanPaymentResult> CheckWithProviderAsync(Guid paymentId, bool slotClaimed, CancellationToken ct)
    {
        var payment = await db.Payments.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == paymentId, ct);
        if (payment.Status == PaymentStatus.Succeeded) return PlanPaymentResult.Paid;
        if (payment.Status == PaymentStatus.Refunded) return PlanPaymentResult.Failed;
        if (payment.Provider == Transfer) return Unconfirmed(payment); // no provider to ask: an admin confirms it
        if (!slotClaimed)
        {
            var now = clock.GetUtcNow();
            var previous = payment.RecheckedAt;
            if (previous is { } asked && asked > now - StatusCooldown) return Unconfirmed(payment);
            var claimed = await db.Payments.IgnoreQueryFilters()
                .Where(p => p.Id == paymentId && p.RecheckedAt == previous && p.Status != PaymentStatus.Succeeded && p.Status != PaymentStatus.Refunded)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecheckedAt, (DateTimeOffset?)now), ct);
            if (claimed == 0) // someone else is asking right now (or it was just confirmed): answer from what is stored
                return await StoredResultAsync(paymentId, ct);
        }
        var gateway = gateways.For(payment.Provider);
        if (gateway is null)
        {
            logger.LogWarning("Plan payment {Reference} was taken by {Provider}, which this server doesn't have", payment.ProviderRef, payment.Provider);
            return Unconfirmed(payment);
        }
        ProviderChargeStatus status;
        try
        {
            status = await gateway.GetStatusAsync(payment.ProviderRef, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Plan payment {Reference}: {Provider} could not be asked ({Error})", payment.ProviderRef, payment.Provider, ex.Message);
            return Unconfirmed(payment);
        }

        switch (status.State)
        {
            case ProviderChargeState.Success:
                await ConfirmPaymentAsync(payment.ProviderRef, status.AmountMinor, status.Currency ?? "", ct);
                var after = await db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == payment.Id).Select(p => p.Status).FirstAsync(ct);
                return after == PaymentStatus.Succeeded ? PlanPaymentResult.Paid : PlanPaymentResult.Failed;
            case ProviderChargeState.Failed:
                // Conditional, so it can never overwrite a Succeeded that another confirmation just committed.
                var failedAt = clock.GetUtcNow();
                await db.Payments.IgnoreQueryFilters()
                    .Where(p => p.Id == paymentId && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Abandoned))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed).SetProperty(p => p.UpdatedAt, failedAt), ct);
                return await StoredResultAsync(paymentId, ct);
            default:
                return Unconfirmed(payment);
        }
    }

    /// <summary>Not confirmed (yet): Pending stays "still confirming"; a Failed or Abandoned payment reads as not gone through.</summary>
    private static PlanPaymentResult Unconfirmed(Payment payment) => payment.Status switch
    {
        PaymentStatus.Succeeded => PlanPaymentResult.Paid,
        PaymentStatus.Pending => PlanPaymentResult.Pending,
        _ => PlanPaymentResult.Failed,
    };

    private async Task<PlanPaymentResult> StoredResultAsync(Guid paymentId, CancellationToken ct) =>
        Unconfirmed(await db.Payments.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == paymentId, ct));

    private async Task<PlanDto?> PlanForAsync(Payment payment, CancellationToken ct)
    {
        if (!Guid.TryParse(payment.Metadata.GetValueOrDefault("planId"), out var planId)) return null;
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
        return plan is null ? null : ToDto(plan);
    }

    /// <summary>
    /// Applies a payment the provider confirmed (the Paystack webhook, or <see cref="CheckWithProviderAsync"/>), exactly once. It is the
    /// single writer for plan payments: inside a transaction under the business's <see cref="PlanLockKey"/>, the payment is claimed with a
    /// conditional update (Pending, Abandoned or Failed to Succeeded), and the subscription changes only when this call made the claim.
    /// A second confirmation (a provider retry, a callback racing the return check) finds nothing to claim and changes nothing.
    /// </summary>
    public async Task ConfirmPaymentAsync(string reference, long amountMinor, string currency, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // never decide on a stale tracked row
        var row = await db.Payments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.ProviderRef == reference, ct);
        if (row is null)
        {
            logger.LogWarning("Webhook for unknown payment reference {Reference}", reference);
            return;
        }
        if (row.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded) return;
        if (row.Provider == Transfer)
        {
            // A bank transfer is confirmed only by a platform admin (ConfirmTransferAsync), never by a provider's word.
            logger.LogWarning("Ignored a provider confirmation for bank transfer claim {Reference}", reference);
            return;
        }
        await ApplyConfirmationAsync(row, amountMinor, currency, pendingOnly: false, beforeCommit: null, ct);
    }

    /// <summary>
    /// A platform admin found the transfer in the company account: the claim is confirmed through the same path as a provider payment
    /// (plan lock, conditional claim), for its stored amount. Normally only from Pending, so a cancelled, replaced or rejected claim stays
    /// as it is; with <paramref name="anyway"/> (the admin's explicit "Confirm anyway", for money that arrived for a claim that was
    /// withdrawn or rejected) also from Abandoned or Failed. <paramref name="beforeCommit"/> runs with the payment as it was read (its
    /// earlier status) inside the same transaction, so its writes (audit, email) land with the confirmation or not at all. True when this
    /// call confirmed it.
    /// </summary>
    public async Task<bool> ConfirmTransferAsync(Guid paymentId, bool anyway, Func<Payment, CancellationToken, Task>? beforeCommit, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.Provider == Transfer && p.Purpose == "subscription", ct)
            ?? throw new NotFoundException("Transfer");
        if (row.Status is PaymentStatus.Succeeded or PaymentStatus.Refunded) return false;
        if (row.Status != PaymentStatus.Pending && !anyway) return false;
        return await ApplyConfirmationAsync(row, row.AmountMinor, row.Currency, pendingOnly: !anyway, beforeCommit, ct);
    }

    private async Task<bool> ApplyConfirmationAsync(Payment row, long amountMinor, string currency, bool pendingOnly, Func<Payment, CancellationToken, Task>? beforeCommit,
        CancellationToken ct)
    {
        var reference = row.ProviderRef;
        tenant.SetTenant(row.TenantId);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(PlanLockKey(row.TenantId), ct);
        var now = clock.GetUtcNow();
        var open = pendingOnly
            ? db.Payments.IgnoreQueryFilters().Where(p => p.Id == row.Id && p.Status == PaymentStatus.Pending)
            : db.Payments.IgnoreQueryFilters().Where(p => p.Id == row.Id
                && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Abandoned || p.Status == PaymentStatus.Failed));

        if (row.AmountMinor != amountMinor || !string.Equals(row.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Payment {Reference} amount mismatch: expected {Expected} {Currency}, got {Actual} {ActualCurrency}",
                reference, row.AmountMinor, row.Currency, amountMinor, currency);
            await open.ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Failed).SetProperty(p => p.UpdatedAt, now), ct);
            await tx.CommitAsync(ct);
            return false;
        }

        var claimed = await open.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.Status, PaymentStatus.Succeeded)
            .SetProperty(p => p.PaidAt, (DateTimeOffset?)now)
            .SetProperty(p => p.UpdatedAt, now), ct);
        if (claimed == 0) return false; // another confirmation got here first (the transaction rolls back on dispose)
        var payment = row;

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
                same.Provider = payment.Provider; // the period now runs on what was paid last (e.g. a bank transfer)
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
        if (beforeCommit is not null) await beforeCommit(payment, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// A platform admin couldn't find the transfer: the claim is marked Failed (only from Pending, under the plan lock) and the plan stays
    /// as it is. <paramref name="beforeCommit"/> runs with the tracked payment inside the transaction. False when it was no longer Pending.
    /// </summary>
    public async Task<bool> RejectTransferAsync(Guid paymentId, Func<Payment, CancellationToken, Task> beforeCommit, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.Provider == Transfer && p.Purpose == "subscription", ct)
            ?? throw new NotFoundException("Transfer");
        if (row.Status != PaymentStatus.Pending) return false;
        tenant.SetTenant(row.TenantId);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.LockAsync(PlanLockKey(row.TenantId), ct);
        // Every change to a transfer claim's status (claim, cancel, confirm, reject) holds this lock, so the row read here stays current.
        var payment = await db.Payments.IgnoreQueryFilters().FirstAsync(p => p.Id == paymentId, ct);
        if (payment.Status != PaymentStatus.Pending) return false;
        payment.Status = PaymentStatus.Failed;
        await beforeCommit(payment, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
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
