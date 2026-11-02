using FluentValidation;
using Saas.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Payouts;
using Saas.Domain.Payouts;
using Saas.Domain.Property;

namespace Saas.Application.Property;

public sealed record StayRequest(Guid ListingId, DateOnly CheckIn, DateOnly CheckOut, int Guests, string? Phone);

public sealed class StayRequestValidator : AbstractValidator<StayRequest>
{
    public StayRequestValidator() => RuleFor(x => x.Phone).MaximumLength(30).WithMessage("Enter a phone number of up to 30 characters.");
}
public sealed record StayQuoteDto(int Nights, long NightlyMinor, long SubtotalMinor, long CleaningMinor, long TotalMinor, string Currency);
public sealed record DateRangeDto(DateOnly Start, DateOnly End);
public sealed record AvailabilityDto(IReadOnlyList<DateRangeDto> Booked, IReadOnlyList<DateRangeDto> Blocked);
public sealed record StayBookingDto(Guid StayId, string AuthorizationUrl, long TotalMinor, DateTimeOffset HoldExpiresAt);
public sealed record VerifyStayRequest(string Reference);
public sealed record MyStayDto(Guid Id, Guid ListingId, string ListingTitle, string ListingUrl, string? CoverUrl, string AreaName, DateOnly CheckIn,
    DateOnly CheckOut, int Guests, int Nights, long TotalMinor, string Currency, StayStatus Status, DateTimeOffset HoldExpiresAt, long RefundMinor,
    string AgentName, string? AgentPhone, string? CheckInTime, string? CheckOutTime, string? Address, string? HouseRules,
    bool Paid, long NightlyMinor, long CleaningMinor);
public sealed record StudioStayDto(Guid Id, Guid ListingId, string ListingTitle, string GuestName, string? GuestPhone, DateOnly CheckIn, DateOnly CheckOut,
    int Guests, int Nights, long TotalMinor, long CommissionMinor, StayStatus Status, long RefundMinor, StayCanceller? CancelledBy, DateTimeOffset CreatedAt,
    long HostNetMinor);
public sealed record BlockRequest(DateOnly Start, DateOnly End, string? Note);
public sealed record BlockDto(Guid Id, DateOnly Start, DateOnly End, string? Note);
public sealed record PropertyEarningDto(Guid Id, Guid StayId, string Description, long GrossMinor, long FeeMinor, long NetMinor,
    DateTimeOffset AvailableAt, DateTimeOffset CreatedAt, string Status);
public sealed record PropertyEarningsDto(string Currency, long AvailableMinor, long PendingMinor, long PaidOutMinor, IReadOnlyList<PropertyEarningDto> Entries);

/// <summary>
/// Short-let bookings for guests. Dates are held for 15 minutes while the guest pays the platform; a PostgreSQL exclusion
/// constraint makes sure two guests can never hold or confirm overlapping nights.
/// </summary>
public sealed class StayService(IAppDbContext db, ICurrentUser user, PropertyCore core, IPaymentGateway gateway, IDbErrors errors,
    NotificationQueue notifications, LocationTree locations, ILogger<StayService> logger)
{
    public const int MaxNights = 90;
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, InspectionService.Zone).DateTime);

    public async Task<AvailabilityDto> AvailabilityAsync(Guid listingId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var listing = await ShortLetAsync(listingId, ct);
        var start = from ?? Today(core.Now);
        var end = to ?? start.AddDays(365);
        var now = core.Now;
        // A signed-in guest's own unpaid hold isn't shown as taken to them: booking again releases it (see BookAsync).
        var me = user.UserId;
        var booked = await db.Stays.IgnoreQueryFilters().Where(s => s.ListingId == listing.Id && s.CheckOut > start && s.CheckIn < end
                && (s.Status == StayStatus.Confirmed || (s.Status == StayStatus.Pending && s.HoldExpiresAt > now && (me == null || s.GuestUserId != me))))
            .OrderBy(s => s.CheckIn).Select(s => new DateRangeDto(s.CheckIn, s.CheckOut)).ToListAsync(ct);
        var blocked = await db.StayBlocks.IgnoreQueryFilters().Where(b => b.ListingId == listing.Id && b.End > start && b.Start < end)
            .OrderBy(b => b.Start).Select(b => new DateRangeDto(b.Start, b.End)).ToListAsync(ct);
        return new AvailabilityDto(booked, blocked);
    }

    public async Task<StayQuoteDto> QuoteAsync(Guid listingId, StayRequest r, CancellationToken ct)
    {
        var listing = await ShortLetAsync(listingId, ct);
        await CheckAsync(listing, r, ct);
        var q = PropertyRules.Quote(listing.PriceMinor, listing.CleaningFeeMinor, r.CheckIn, r.CheckOut);
        return new StayQuoteDto(q.Nights, listing.PriceMinor, q.SubtotalMinor, q.CleaningMinor, q.TotalMinor, listing.Currency);
    }

    public async Task<StayBookingDto> BookAsync(StayRequest r, CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var listing = await ShortLetAsync(r.ListingId, ct);
        if (await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.TenantId == listing.TenantId && m.UserId == me, ct))
            throw new ForbiddenException("You can't book your own listing.");
        await CheckAsync(listing, r, ct);
        if (!gateway.IsConfigured) throw new ServiceUnavailableException("Online payments aren't set up yet. Please try again later.");

        // Unpaid holds that have run out no longer block the dates, and nor does this guest's own earlier hold on these nights (they
        // went back from Paystack and pressed "Reserve and pay" again): it is released, and the new hold replaces it. If the old
        // payment still lands, ConfirmAsync refunds it because the dates are taken by the new hold.
        var now = core.Now;
        await db.Stays.IgnoreQueryFilters().Where(s => s.ListingId == listing.Id && s.Status == StayStatus.Pending
                && (s.HoldExpiresAt <= now || (s.GuestUserId == me && s.CheckIn < r.CheckOut && s.CheckOut > r.CheckIn)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Expired), ct);

        core.UseTenantOf(listing.TenantId);
        var q = PropertyRules.Quote(listing.PriceMinor, listing.CleaningFeeMinor, r.CheckIn, r.CheckOut);
        var guest = await db.Users.Where(u => u.Id == me).Select(u => new { u.FullName, u.Email }).FirstAsync(ct);
        var stay = new Stay
        {
            ListingId = listing.Id, GuestUserId = me, GuestName = guest.FullName, GuestPhone = string.IsNullOrWhiteSpace(r.Phone) ? null : r.Phone.Trim(),
            CheckIn = r.CheckIn, CheckOut = r.CheckOut, Guests = r.Guests, Nights = q.Nights, NightlyMinor = listing.PriceMinor,
            CleaningMinor = q.CleaningMinor, TotalMinor = q.TotalMinor, CommissionMinor = PropertyRules.Commission(q.TotalMinor), Currency = listing.Currency,
            HoldExpiresAt = core.Now + PropertyRules.StayHold, Reference = "",
        };
        stay.Reference = $"stay_{stay.Id:N}";
        db.Stays.Add(stay);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (errors.IsExclusionViolation(ex) || errors.IsConcurrencyConflict(ex)) // a deadlock between two bookings is the same lost race
        {
            throw new ConflictException("Those dates were just booked. Choose other dates.");
        }
        var url = await gateway.InitializeAsync(new PaymentInit(guest.Email!, stay.TotalMinor, stay.Currency, stay.Reference,
            $"{core.Web}/trips/{stay.Id}", new() { ["listing"] = listing.Title, ["tenantId"] = listing.TenantId.ToString() }), ct);
        return new StayBookingDto(stay.Id, url, stay.TotalMinor, stay.HoldExpiresAt);
    }

    public async Task<MyStayDto> VerifyAsync(Guid stayId, VerifyStayRequest r, CancellationToken ct)
    {
        var stay = await MyStayAsync(stayId, ct);
        if (stay.Status is StayStatus.Pending or StayStatus.Expired && r.Reference == stay.Reference && await gateway.VerifyAsync(stay.Reference, ct) is { } charge)
            await ConfirmAsync(charge.Reference, charge.AmountMinor, charge.Currency, ct);
        return (await ToDtosAsync([await MyStayAsync(stayId, ct)], ct))[0];
    }

    /// <summary>
    /// Confirms a paid stay (from the return page or the webhook; safe to run twice). The amount and currency must match.
    /// A payment that lands after the hold ran out still confirms if the dates are free; otherwise it's refunded.
    /// </summary>
    public Task ConfirmAsync(string reference, long amountMinor, string currency, CancellationToken ct) =>
        ConfirmAsync(reference, amountMinor, currency, retried: false, ct);

    private async Task ConfirmAsync(string reference, long amountMinor, string currency, bool retried, CancellationToken ct)
    {
        var tenantId = await db.Stays.IgnoreQueryFilters().Where(s => s.Reference == reference).Select(s => (Guid?)s.TenantId).FirstOrDefaultAsync(ct);
        if (tenantId is null) return;
        core.UseTenantOf(tenantId.Value);
        var stay = await db.Stays.FirstAsync(s => s.Reference == reference, ct);
        if (stay.Status == StayStatus.Cancelled && stay.PaidAt is null && stay.RefundMinor == 0 && amountMinor > 0)
        {
            // The guest cancelled while the payment was still on its way. Give it all back (claimed first, so webhook and return page refund once).
            var now0 = core.Now;
            var claimed0 = await db.Stays.Where(s => s.Id == stay.Id && s.Status == StayStatus.Cancelled && s.PaidAt == null && s.RefundMinor == 0)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PaidAt, (DateTimeOffset?)now0).SetProperty(x => x.RefundMinor, amountMinor), ct);
            if (claimed0 == 0) return;
            await gateway.RefundAsync(reference, amountMinor, ct);
            logger.LogInformation("Stay {Reference} was cancelled before its payment arrived; refunded {Amount}", reference, amountMinor);
            await EmailRefundAsync(stay, amountMinor, "You cancelled this stay before your payment reached us", ct);
            await db.SaveChangesAsync(ct);
            return;
        }
        if (stay.Status is not (StayStatus.Pending or StayStatus.Expired)) return;
        if (amountMinor != stay.TotalMinor || !string.Equals(currency, stay.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Stay {Reference}: paid {Amount} {Currency}, expected {Total} {Expected}", reference, amountMinor, currency, stay.TotalMinor, stay.Currency);
            await AlertMismatchAsync(stay, amountMinor, currency, ct);
            return;
        }
        var listing = await db.Listings.FirstAsync(l => l.Id == stay.ListingId, ct);
        // Claim the stay with a conditional update, so a guest cancelling at the same moment can't be overwritten. If the guest got
        // there first, run again: the cancelled-before-payment branch above refunds the money.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = core.Now;
        int claimed;
        try
        {
            claimed = await db.Stays.Where(s => s.Id == stay.Id && (s.Status == StayStatus.Pending || s.Status == StayStatus.Expired))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Confirmed).SetProperty(x => x.PaidAt, (DateTimeOffset?)now), ct);
        }
        catch (Exception ex) when (errors.IsExclusionViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            await RefundLatePaymentAsync(reference, tenantId.Value, ct);
            return;
        }
        if (claimed == 0)
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (retried) return;
            db.ChangeTracker.Clear();
            await ConfirmAsync(reference, amountMinor, currency, retried: true, ct);
            return;
        }
        stay.Status = StayStatus.Confirmed;
        stay.PaidAt = now;
        var midnight = stay.CheckIn.ToDateTime(TimeOnly.MinValue);
        db.Earnings.Add(new Earning
        {
            Source = EarningSource.Stay, SourceId = stay.Id, Description = $"{listing.Title}, {stay.CheckIn:d MMM} to {stay.CheckOut:d MMM yyyy}",
            UserId = stay.GuestUserId, GrossMinor = stay.TotalMinor, FeePercent = PropertyRules.CommissionPercent, FeeMinor = stay.CommissionMinor,
            NetMinor = stay.TotalMinor - stay.CommissionMinor,
            AvailableAt = (new DateTimeOffset(midnight, InspectionService.Zone.GetUtcOffset(midnight)) + PropertyRules.HostPayoutDelay).ToUniversalTime(),
        });
        await NotifyConfirmedAsync(stay, listing, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (errors.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            logger.LogInformation("Stay {Reference} was already confirmed", reference); // webhook and return page raced
        }
    }

    /// <summary>Paid after the hold expired and someone else took the dates: give the money back.</summary>
    private async Task RefundLatePaymentAsync(string reference, Guid tenantId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        core.UseTenantOf(tenantId);
        var late = await db.Stays.FirstAsync(s => s.Reference == reference, ct);
        await gateway.RefundAsync(reference, late.TotalMinor, ct);
        late.Status = StayStatus.Cancelled;
        late.CancelledAt = core.Now;
        late.RefundMinor = late.TotalMinor;
        await EmailRefundAsync(late, late.TotalMinor, "Your payment arrived after the hold on these dates ran out, and someone else has booked them", ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task EmailRefundAsync(Stay stay, long amountMinor, string reason, CancellationToken ct)
    {
        var title = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == stay.ListingId).Select(l => l.Title).FirstOrDefaultAsync(ct) ?? "your stay";
        if (await db.Users.Where(u => u.Id == stay.GuestUserId).Select(u => u.Email).FirstOrDefaultAsync(ct) is { } guestEmail)
            notifications.Email(guestEmail, "stay.refunded", new()
            {
                ["listing"] = title, ["dates"] = $"{stay.CheckIn:d MMM} to {stay.CheckOut:d MMM yyyy}", ["amount"] = Money.Format(amountMinor, stay.Currency),
                ["reason"] = reason, ["link"] = $"{core.Web}/trips/{stay.Id}",
            }, stay.TenantId);
    }

    /// <summary>A payment that doesn't match the stay: the guest is told it didn't go through, and Finance is asked to sort it out.</summary>
    private async Task AlertMismatchAsync(Stay stay, long paidMinor, string paidCurrency, CancellationToken ct)
    {
        var title = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == stay.ListingId).Select(l => l.Title).FirstOrDefaultAsync(ct) ?? "a stay";
        var paid = Money.Format(paidMinor, paidCurrency.ToUpperInvariant());
        var expected = Money.Format(stay.TotalMinor, stay.Currency);
        if (await db.Users.Where(u => u.Id == stay.GuestUserId).Select(u => u.Email).FirstOrDefaultAsync(ct) is { } guestEmail)
            notifications.Email(guestEmail, "stay.payment_mismatch", new()
            {
                ["listing"] = title, ["paid"] = paid, ["expected"] = expected, ["reference"] = stay.Reference, ["link"] = $"{core.Web}/trips/{stay.Id}",
            }, stay.TenantId);
        var finance = await (from a in db.PlatformAdmins join u in db.Users on a.UserId equals u.Id
                             where a.RemovedAt == null && (a.Role == PlatformRole.Finance || a.Role == PlatformRole.SuperAdmin)
                             select u.Email).ToListAsync(ct);
        foreach (var email in finance.OfType<string>())
            notifications.Email(email, "payments.mismatch", new()
            {
                ["kind"] = "Short-let stay", ["listing"] = title, ["paid"] = paid, ["expected"] = expected, ["reference"] = stay.Reference,
            });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Full refund more than 48 hours before arrival; after that the host keeps one night. Not once the stay has started.</summary>
    public async Task<MyStayDto> CancelAsync(Guid stayId, CancellationToken ct)
    {
        var stay = await MyStayAsync(stayId, ct);
        core.UseTenantOf(stay.TenantId);
        stay = await db.Stays.FirstAsync(s => s.Id == stayId, ct);
        if (stay.Status == StayStatus.Pending)
        {
            // Conditional, so a payment confirming at the same moment can't be overwritten: if it got there first, cancel the
            // now-confirmed stay instead (which refunds by the usual rules).
            var claimed = await db.Stays.Where(s => s.Id == stayId && s.Status == StayStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Cancelled).SetProperty(x => x.CancelledAt, core.Now)
                    .SetProperty(x => x.CancelledBy, (StayCanceller?)StayCanceller.Guest), ct);
            if (claimed == 0)
            {
                db.ChangeTracker.Clear();
                return await CancelAsync(stayId, ct);
            }
            stay.Status = StayStatus.Cancelled;
            stay.CancelledAt = core.Now;
            stay.CancelledBy = StayCanceller.Guest;
        }
        else
        {
            if (stay.Status != StayStatus.Confirmed) throw new ConflictException("This stay can't be cancelled.");
            if (Today(core.Now) >= stay.CheckIn) throw new ConflictException("Your stay has started. Contact the host.");
            var refund = PropertyRules.GuestRefund(stay, core.Now, InspectionService.Zone);
            await RefundAsync(db, gateway, stay, refund, StayCanceller.Guest, core.Now, ct);
            await NotifyCancelledAsync(stay, toHost: true, ct);
        }
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([stay], ct))[0];
    }

    /// <summary>
    /// Refunds through the platform's Paystack and shrinks or reverses the host's earning to match. The stay is claimed first with a
    /// conditional update, so a double click (or guest and host at once) refunds once; the claim is undone if Paystack refuses.
    /// </summary>
    public static async Task RefundAsync(IAppDbContext db, IPaymentGateway gateway, Stay stay, long refund, StayCanceller by, DateTimeOffset now, CancellationToken ct)
    {
        var claimed = await db.Stays.Where(s => s.Id == stay.Id && s.Status == StayStatus.Confirmed)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Cancelled).SetProperty(x => x.CancelledAt, now)
                .SetProperty(x => x.CancelledBy, by).SetProperty(x => x.RefundMinor, refund), ct);
        if (claimed == 0) throw new ConflictException("This stay has already been cancelled.");
        try
        {
            if (refund > 0) await gateway.RefundAsync(stay.Reference, refund, ct);
        }
        catch
        {
            await db.Stays.Where(s => s.Id == stay.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StayStatus.Confirmed)
                .SetProperty(x => x.CancelledAt, (DateTimeOffset?)null).SetProperty(x => x.CancelledBy, (StayCanceller?)null).SetProperty(x => x.RefundMinor, 0L),
                CancellationToken.None);
            throw;
        }
        stay.Status = StayStatus.Cancelled;
        stay.CancelledAt = now;
        stay.CancelledBy = by;
        stay.RefundMinor = refund;
        var earning = await db.Earnings.FirstOrDefaultAsync(e => e.Source == EarningSource.Stay && e.SourceId == stay.Id, ct);
        if (earning is null) return;
        var kept = stay.TotalMinor - refund;
        if (kept <= 0) earning.Reversed = true;
        else
        {
            earning.GrossMinor = kept;
            earning.FeeMinor = PropertyRules.Commission(kept);
            earning.NetMinor = kept - earning.FeeMinor;
        }
    }

    public async Task<IReadOnlyList<MyStayDto>> MineAsync(CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        var stays = await db.Stays.IgnoreQueryFilters().AsNoTracking().Where(s => s.GuestUserId == me && s.Status != StayStatus.Expired)
            .OrderByDescending(s => s.CheckIn).Take(100).ToListAsync(ct);
        return await ToDtosAsync(stays, ct);
    }

    private async Task<Stay> MyStayAsync(Guid stayId, CancellationToken ct)
    {
        var me = user.UserId ?? throw new ForbiddenException();
        return await db.Stays.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stayId && s.GuestUserId == me, ct) ?? throw new NotFoundException("Stay");
    }

    private async Task<Listing> ShortLetAsync(Guid listingId, CancellationToken ct)
    {
        var listing = await db.Listings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId && l.Status == ListingStatus.Active && db.Tenants.Any(t => t.Id == l.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Listing");
        if (listing.Purpose != ListingPurpose.ShortLet) throw BadRequestException.Field("listingId", "Only short-lets can be booked by the night.");
        return listing;
    }

    private async Task CheckAsync(Listing listing, StayRequest r, CancellationToken ct)
    {
        var nights = r.CheckOut.DayNumber - r.CheckIn.DayNumber;
        if (nights < 1) throw BadRequestException.Field("checkOut", "Check-out must be after check-in.");
        if (r.CheckIn < Today(core.Now)) throw BadRequestException.Field("checkIn", "Check-in can't be in the past.");
        if (nights > MaxNights) throw BadRequestException.Field("checkOut", $"Stays can be up to {MaxNights} nights.");
        if (nights < listing.MinNights) throw BadRequestException.Field("checkOut", $"This place needs at least {listing.MinNights} nights.");
        if (r.Guests < 1 || r.Guests > listing.MaxGuests) throw BadRequestException.Field("guests", $"This place sleeps up to {listing.MaxGuests}.");
        if (await db.StayBlocks.IgnoreQueryFilters().AnyAsync(b => b.ListingId == listing.Id && b.Start < r.CheckOut && b.End > r.CheckIn, ct))
            throw new ConflictException("The host has closed some of those dates. Choose other dates.");
        var now = core.Now;
        // The caller's own unpaid hold never blocks them: booking again releases it (see BookAsync), and the quote ignores it.
        var me = user.UserId;
        if (await db.Stays.IgnoreQueryFilters().AnyAsync(s => s.ListingId == listing.Id && s.CheckIn < r.CheckOut && s.CheckOut > r.CheckIn
                && (s.Status == StayStatus.Confirmed || (s.Status == StayStatus.Pending && s.HoldExpiresAt > now && (me == null || s.GuestUserId != me))), ct))
            throw new ConflictException("Those dates were just booked. Choose other dates.");
    }

    private async Task NotifyConfirmedAsync(Stay stay, Listing listing, CancellationToken ct)
    {
        var dates = $"{stay.CheckIn:ddd d MMM} to {stay.CheckOut:ddd d MMM yyyy}";
        if (await db.Users.Where(u => u.Id == stay.GuestUserId).Select(u => u.Email).FirstOrDefaultAsync(ct) is { } guestEmail)
            notifications.Email(guestEmail, "stay.confirmed", new()
            {
                ["listing"] = listing.Title, ["dates"] = dates, ["total"] = Money.Format(stay.TotalMinor, stay.Currency), ["address"] = listing.Address,
                ["checkIn"] = listing.CheckInTime ?? "", ["link"] = $"{core.Web}/trips/{stay.Id}",
            }, stay.TenantId);
        var agentEmail = await (from a in db.AgentProfiles join u in db.Users on a.UserId equals u.Id where a.Id == listing.AgentProfileId select u.Email).FirstOrDefaultAsync(ct);
        if (agentEmail is not null)
            notifications.Email(agentEmail, "stay.new", new()
            {
                ["listing"] = listing.Title, ["dates"] = dates, ["guest"] = stay.GuestName, ["phone"] = stay.GuestPhone ?? "", ["guests"] = stay.Guests.ToString(),
                ["net"] = Money.Format(stay.TotalMinor - stay.CommissionMinor, stay.Currency), ["link"] = $"{core.Web}/studio/stays",
            }, stay.TenantId);
    }

    private async Task NotifyCancelledAsync(Stay stay, bool toHost, CancellationToken ct)
    {
        var listing = await db.Listings.IgnoreQueryFilters().FirstAsync(l => l.Id == stay.ListingId, ct);
        var email = toHost
            ? await (from a in db.AgentProfiles.IgnoreQueryFilters() join u in db.Users on a.UserId equals u.Id where a.Id == listing.AgentProfileId select u.Email).FirstOrDefaultAsync(ct)
            : await db.Users.Where(u => u.Id == stay.GuestUserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        if (email is not null)
            notifications.Email(email, "stay.cancelled", new()
            {
                ["listing"] = listing.Title, ["dates"] = $"{stay.CheckIn:ddd d MMM} to {stay.CheckOut:ddd d MMM yyyy}",
                ["by"] = stay.CancelledBy == StayCanceller.Host ? "the host" : "the guest", ["refund"] = Money.Format(stay.RefundMinor, stay.Currency),
                ["link"] = toHost ? $"{core.Web}/studio/stays" : $"{core.Web}/trips/{stay.Id}",
            }, stay.TenantId);
    }

    public Task NotifyGuestCancelledAsync(Stay stay, CancellationToken ct) => NotifyCancelledAsync(stay, toHost: false, ct);

    private async Task<List<MyStayDto>> ToDtosAsync(IReadOnlyList<Stay> stays, CancellationToken ct)
    {
        var ids = stays.Select(s => s.ListingId).Distinct().ToList();
        var listings = await (from l in db.Listings.IgnoreQueryFilters()
                              join a in db.AgencyProfiles.IgnoreQueryFilters() on l.TenantId equals a.TenantId
                              join g in db.AgentProfiles.IgnoreQueryFilters() on l.AgentProfileId equals g.Id
                              where ids.Contains(l.Id)
                              select new { L = l, AgencySlug = a.Slug, g.DisplayName, g.Phone }).ToDictionaryAsync(x => x.L.Id, ct);
        var covers = (await db.ListingMedia.IgnoreQueryFilters().Where(m => ids.Contains(m.ListingId) && m.Status == MediaStatus.Ready)
            .Select(m => new { m.ListingId, m.Position, m.CardUrl }).ToListAsync(ct)).GroupBy(m => m.ListingId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Position).First().CardUrl);
        var tree = await locations.GetAsync(ct);
        return stays.Select(s =>
        {
            var confirmed = s.Status is StayStatus.Confirmed or StayStatus.Completed;
            // Money reached us: confirmed, or refunded after it arrived (a payment that landed after the guest cancelled or the hold ran out).
            var paid = s.PaidAt is not null || s.RefundMinor > 0;
            if (!listings.TryGetValue(s.ListingId, out var x))
                return new MyStayDto(s.Id, s.ListingId, "Listing removed", "", null, "", s.CheckIn, s.CheckOut, s.Guests, s.Nights, s.TotalMinor,
                    s.Currency, s.Status, s.HoldExpiresAt, s.RefundMinor, "", null, null, null, null, null, paid, s.NightlyMinor, s.CleaningMinor);
            return new MyStayDto(s.Id, s.ListingId, x.L.Title, core.ListingUrl(x.AgencySlug, x.L.Slug), covers.GetValueOrDefault(s.ListingId),
                tree.Find(x.L.LocationId)?.Name ?? "", s.CheckIn, s.CheckOut, s.Guests, s.Nights, s.TotalMinor, s.Currency, s.Status, s.HoldExpiresAt,
                s.RefundMinor, x.DisplayName, confirmed ? x.Phone : null, x.L.CheckInTime, x.L.CheckOutTime, confirmed ? x.L.Address : null,
                x.L.HouseRules, paid, s.NightlyMinor, s.CleaningMinor);
        }).ToList();
    }
}

/// <summary>The host's side of short-lets: stays, closed dates, cancellations, earnings and payouts.</summary>
public sealed class HostStayService(IAppDbContext db, PropertyCore core, IPaymentGateway gateway, PayoutService payouts, StayService stays, ICurrentUser user)
{
    public async Task<IReadOnlyList<StudioStayDto>> ListAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var start = from ?? StayService.Today(core.Now).AddDays(-30);
        var end = to ?? start.AddDays(400);
        return await (from s in db.Stays.AsNoTracking()
                      join l in db.Listings on s.ListingId equals l.Id into found
                      from l in found.DefaultIfEmpty()
                      where s.CheckOut > start && s.CheckIn < end && s.Status != StayStatus.Expired
                      // The host's net is the earning row, which a refund shrinks or reverses. Before payment there is no row: a
                      // pending stay shows what the host would earn, and one cancelled unpaid earns nothing.
                      let earning = db.Earnings.Where(e => e.Source == EarningSource.Stay && e.SourceId == s.Id).Select(e => new { e.NetMinor, e.Reversed }).FirstOrDefault()
                      orderby s.CheckIn
                      select new StudioStayDto(s.Id, s.ListingId, l == null ? "Listing removed" : l.Title, s.GuestName, s.Status == StayStatus.Confirmed || s.Status == StayStatus.Completed ? s.GuestPhone : null,
                          s.CheckIn, s.CheckOut, s.Guests, s.Nights, s.TotalMinor, s.CommissionMinor, s.Status, s.RefundMinor, s.CancelledBy, s.CreatedAt,
                          earning != null ? (earning.Reversed ? 0 : earning.NetMinor) : s.Status == StayStatus.Pending ? s.TotalMinor - s.CommissionMinor : 0))
            .Take(1000).ToListAsync(ct);
    }

    /// <summary>A host cancelling a paid stay refunds the guest in full, and moderators can see how often it happens.</summary>
    public async Task CancelAsync(Guid stayId, CancellationToken ct)
    {
        var stay = await db.Stays.FirstOrDefaultAsync(s => s.Id == stayId, ct) ?? throw new NotFoundException("Stay");
        if (stay.Status != StayStatus.Confirmed) throw new ConflictException("Only confirmed stays can be cancelled.");
        if (StayService.Today(core.Now) >= stay.CheckIn) throw new ConflictException("This stay has started. Contact support to change it.");
        await StayService.RefundAsync(db, gateway, stay, PropertyRules.HostCancelRefund(stay), StayCanceller.Host, core.Now, ct);
        var agency = await core.AgencyAsync(ct);
        agency.HostCancellations++;
        await stays.NotifyGuestCancelledAsync(stay, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BlockDto>> BlocksAsync(Guid listingId, CancellationToken ct) =>
        await db.StayBlocks.AsNoTracking().Where(b => b.ListingId == listingId).OrderBy(b => b.Start)
            .Select(b => new BlockDto(b.Id, b.Start, b.End, b.Note)).ToListAsync(ct);

    public async Task<BlockDto> AddBlockAsync(Guid listingId, BlockRequest r, CancellationToken ct)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == listingId && l.Purpose == ListingPurpose.ShortLet, ct)) throw new NotFoundException("Short-let");
        if (r.End <= r.Start) throw BadRequestException.Field("end", "The last day must be after the first.");
        if (await db.Stays.AnyAsync(s => s.ListingId == listingId && s.CheckIn < r.End && s.CheckOut > r.Start
                && (s.Status == StayStatus.Confirmed || (s.Status == StayStatus.Pending && s.HoldExpiresAt > core.Now)), ct))
            throw new ConflictException("Guests have booked some of those dates. Cancel their stay first if you must.");
        var block = new StayBlock { ListingId = listingId, Start = r.Start, End = r.End, Note = r.Note?.Trim() };
        db.StayBlocks.Add(block);
        await db.SaveChangesAsync(ct);
        return new BlockDto(block.Id, block.Start, block.End, block.Note);
    }

    public async Task DeleteBlockAsync(Guid listingId, Guid blockId, CancellationToken ct)
    {
        if (await db.StayBlocks.Where(b => b.Id == blockId && b.ListingId == listingId).ExecuteDeleteAsync(ct) == 0) throw new NotFoundException("Closed dates");
    }

    public async Task<PropertyEarningsDto> EarningsAsync(CancellationToken ct)
    {
        var agency = await core.AgencyAsync(ct);
        var ledger = await payouts.LedgerAsync(ct);
        var rows = await db.Earnings.AsNoTracking().Where(e => e.Source == EarningSource.Stay).OrderByDescending(e => e.CreatedAt).Take(200).ToListAsync(ct);
        return new PropertyEarningsDto(agency.Currency, ledger.AvailableMinor, ledger.PendingMinor, ledger.PaidOutMinor, rows.Select(e =>
            new PropertyEarningDto(e.Id, e.SourceId, e.Description, e.GrossMinor, e.FeeMinor, e.NetMinor, e.AvailableAt, e.CreatedAt,
                ledger.States.GetValueOrDefault(e.Id, LedgerRules.EarningState.Pending).ToString())).ToList());
    }

    public async Task<PayoutDto> RequestPayoutAsync(PayoutRequest r, CancellationToken ct)
    {
        var agency = await core.AgencyAsync(ct);
        return await payouts.RequestAsync(r.AmountMinor, agency.PayoutAccount, agency.Currency, ct);
    }

    public async Task<PayoutAccountDto> SetPayoutAccountAsync(PayoutAccountDto r, CancellationToken ct)
    {
        var agency = await core.AgencyAsync(ct);
        agency.PayoutAccount = new PayoutAccount { BankName = r.BankName.Trim(), AccountNumber = r.AccountNumber.Trim(), AccountName = r.AccountName.Trim() };
        await db.SaveChangesAsync(ct);
        return r;
    }

    public async Task<PayoutAccountDto?> PayoutAccountAsync(CancellationToken ct) =>
        (await core.AgencyAsync(ct)).PayoutAccount is { } p
            ? new PayoutAccountDto(p.BankName, user.IsSupportView ? AccountMask.Last4(p.AccountNumber) : p.AccountNumber, p.AccountName)
            : null;
}
