using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

public sealed record BoostOptionDto(string Code, BoostKind Kind, int Days, long PriceMinor, string Currency, string Name);
public sealed record BuyBoostRequest(string Code);
public sealed record BoostPurchaseDto(Guid BoostId, string AuthorizationUrl, long AmountMinor);
public sealed record VerifyBoostRequest(string Reference);
public sealed record BoostDto(Guid Id, Guid ListingId, string ListingTitle, string Code, BoostKind Kind, int Days, long AmountMinor, BoostStatus Status,
    DateTimeOffset? PaidAt, DateTimeOffset? EndsAt, DateTimeOffset CreatedAt);

/// <summary>Paid placement: featured for 7 or 30 days, or top of search for 7 days. Paid to the platform's Paystack.</summary>
public sealed class BoostService(IAppDbContext db, ICurrentUser user, PropertyCore core, IPaymentGateway gateway, PaymentSettings payments, ILogger<BoostService> logger)
{
    public static readonly IReadOnlyList<BoostOptionDto> Options =
    [
        new("featured-7", BoostKind.Featured, 7, 300_000, "NGN", "Featured for 7 days"),
        new("featured-30", BoostKind.Featured, 30, 1_000_000, "NGN", "Featured for 30 days"),
        new("top-7", BoostKind.Top, 7, 500_000, "NGN", "Top of search for 7 days"),
    ];

    public async Task<BoostPurchaseDto> BuyAsync(Guid listingId, BuyBoostRequest r, CancellationToken ct)
    {
        var option = Options.FirstOrDefault(o => o.Code == r.Code) ?? throw BadRequestException.Field("code", "Choose a boost.");
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, ct) ?? throw new NotFoundException("Listing");
        if (listing.Status != ListingStatus.Active) throw new ConflictException("Only live listings can be boosted.");
        // Online payment switched off in the admin console reads exactly like a gateway with no key: nothing reaches the gateway.
        if (!gateway.IsConfigured || !await payments.OnlinePaymentsEnabledAsync(ct)) throw new ServiceUnavailableException("Online payments aren't set up yet. Please try again later.");
        var me = user.UserId ?? throw new ForbiddenException();
        var boost = new Boost
        {
            ListingId = listing.Id, Code = option.Code, Kind = option.Kind, Days = option.Days, AmountMinor = option.PriceMinor, Currency = option.Currency,
            BuyerUserId = me, Reference = "",
        };
        boost.Reference = $"bst_{boost.Id:N}";
        db.Boosts.Add(boost);
        await db.SaveChangesAsync(ct);
        var email = await db.Users.Where(u => u.Id == me).Select(u => u.Email).FirstAsync(ct);
        var url = await gateway.InitializeAsync(new PaymentInit(email!, boost.AmountMinor, boost.Currency, boost.Reference,
            $"{core.Web}/studio/boosts/{boost.Id}", new() { ["boost"] = option.Name, ["listing"] = listing.Title, ["tenantId"] = listing.TenantId.ToString() }), ct);
        return new BoostPurchaseDto(boost.Id, url, boost.AmountMinor);
    }

    public async Task<BoostDto> VerifyAsync(Guid boostId, VerifyBoostRequest r, CancellationToken ct)
    {
        var boost = await db.Boosts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == boostId, ct) ?? throw new NotFoundException("Boost");
        if (boost.Status == BoostStatus.Pending && r.Reference == boost.Reference && await gateway.VerifyAsync(boost.Reference, ct) is { } charge)
            await ConfirmAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
        return (await ListAsync(ct)).First(b => b.Id == boostId);
    }

    /// <summary>
    /// Activates a paid boost (return page or webhook). The status flip is a single conditional update, so a race between
    /// the two extends the listing only once. The amount and currency must match.
    /// </summary>
    public async Task ConfirmAsync(string reference, long amountMinor, string currency, CancellationToken ct)
    {
        var boost = await db.Boosts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(b => b.Reference == reference, ct);
        if (boost is null || boost.Status != BoostStatus.Pending) return;
        if (amountMinor != boost.AmountMinor || !string.Equals(currency, boost.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Boost {Reference}: paid {Amount} {Currency}, expected {Total} {Expected}", reference, amountMinor, currency, boost.AmountMinor, boost.Currency);
            return;
        }
        core.UseTenantOf(boost.TenantId);
        var listing = await db.Listings.FirstAsync(l => l.Id == boost.ListingId, ct);
        var now = core.Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Boosts.Where(b => b.Id == boost.Id && b.Status == BoostStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BoostStatus.Active).SetProperty(b => b.PaidAt, now), ct);
        if (claimed == 0) return;
        // Extend from whatever the listing has at this moment (in SQL, row-locked), so two boosts confirming at once both count.
        var days = boost.Days;
        var row = db.Listings.Where(l => l.Id == listing.Id);
        if (boost.Kind == BoostKind.Top)
            await row.ExecuteUpdateAsync(s => s.SetProperty(l => l.TopUntil, l => (l.TopUntil > now ? l.TopUntil!.Value : now).AddDays(days)), ct);
        else
            await row.ExecuteUpdateAsync(s => s.SetProperty(l => l.FeaturedUntil, l => (l.FeaturedUntil > now ? l.FeaturedUntil!.Value : now).AddDays(days)), ct);
        var endsAt = await row.Select(l => boost.Kind == BoostKind.Top ? l.TopUntil : l.FeaturedUntil).FirstAsync(ct);
        await db.Boosts.Where(b => b.Id == boost.Id).ExecuteUpdateAsync(s => s.SetProperty(b => b.EndsAt, endsAt), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<BoostDto>> ListAsync(CancellationToken ct) =>
        await (from b in db.Boosts.AsNoTracking()
               join l in db.Listings on b.ListingId equals l.Id
               orderby b.CreatedAt descending
               select new BoostDto(b.Id, l.Id, l.Title, b.Code, b.Kind, b.Days, b.AmountMinor, b.Status, b.PaidAt, b.EndsAt, b.CreatedAt)).Take(200).ToListAsync(ct);
}
