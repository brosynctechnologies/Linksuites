using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Bookings;
using Saas.Domain.Core;

namespace Saas.Application.Bookings;

/// <summary>What customers use without signing in: the booking page and their manage-booking link.</summary>
public sealed class PublicBookingService(
    IAppDbContext db,
    ITenantContext tenant,
    BookingCore core,
    PlanService plans,
    IMerchantPayments payments,
    ISecretProtector secrets,
    ILogger<PublicBookingService> logger)
{
    public async Task<PublicBookingPageDto> PageAsync(string slug, CancellationToken ct)
    {
        var b = await ResolveAsync(slug, ct);
        var deposits = await core.DepositsEnabledAsync(b, ct);
        var services = await db.BookableServices.AsNoTracking().Where(s => s.IsActive && s.StaffIds.Count > 0)
            .OrderBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct);
        var staff = await db.StaffMembers.AsNoTracking().Where(s => s.IsBookable).OrderBy(s => s.Position).ThenBy(s => s.DisplayName).ToListAsync(ct);
        var staffIds = staff.Select(s => s.Id).ToHashSet();
        var hours = (await db.BusinessHours.AsNoTracking().OrderBy(h => h.Weekday).ToListAsync(ct)).Select(BookingCore.ToDto).ToList();
        var ratings = await db.Bookings.AsNoTracking().Where(x => x.Rating != null).Select(x => x.Rating!.Value).ToListAsync(ct);

        return new PublicBookingPageDto(b.Name, b.Slug, b.Category, b.Description, b.LogoUrl, b.CoverUrl, b.Address, b.Phone,
            b.WhatsappNumber, b.Timezone, b.Currency, b.BrandColor, b.Policies, b.CancellationHours, b.MaxDaysAhead, b.BookingEnabled,
            !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct),
            ratings.Count == 0 ? null : new RatingDto(Math.Round(ratings.Average(), 1), ratings.Count),
            services.Select(s => new PublicServiceDto(s.Id, s.Name, s.Description, s.DurationMinutes, s.PriceMinor,
                deposits ? s.DepositMinor : 0, s.StaffIds.Where(staffIds.Contains).ToList()))
                .Where(s => s.StaffIds.Count > 0).ToList(),
            staff.Select(s => new PublicStaffDto(s.Id, s.DisplayName, s.PhotoUrl, s.Bio,
                services.Where(x => x.StaffIds.Contains(s.Id)).Select(x => x.Id).ToList())).ToList(),
            hours);
    }

    public async Task<AvailabilityDto> AvailabilityAsync(string slug, Guid serviceId, Guid? staffId, DateOnly date, CancellationToken ct)
    {
        var b = await ResolveAsync(slug, ct);
        var service = await ActiveServiceAsync(serviceId, ct);
        var slots = b.BookingEnabled ? await core.SlotsAsync(b, service, staffId, date, forPublic: true, ignoreHours: false, null, ct) : [];
        return new AvailabilityDto(date, b.Timezone, slots.Select(s => new SlotDto(s.StartsAt, s.EndsAt, s.StaffIds)).ToList());
    }

    public async Task<IReadOnlyList<DayAvailabilityDto>> DaysAsync(string slug, Guid serviceId, Guid? staffId, string month, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", out var first)) throw BadRequestException.Field("month", "Use YYYY-MM.");
        var b = await ResolveAsync(slug, ct);
        var service = await ActiveServiceAsync(serviceId, ct);
        var zone = AvailabilityEngine.Zone(b.Timezone);
        var today = AvailabilityEngine.LocalDate(core.Now, zone);
        var result = new List<DayAvailabilityDto>();
        for (var d = first; d.Month == first.Month; d = d.AddDays(1))
        {
            var open = b.BookingEnabled && d >= today && d <= today.AddDays(b.MaxDaysAhead)
                && (await core.SlotsAsync(b, service, staffId, d, forPublic: true, ignoreHours: false, null, ct)).Count > 0;
            result.Add(new DayAvailabilityDto(d, open));
        }
        return result;
    }

    public async Task<PublicBookResult> BookAsync(string slug, PublicBookRequest r, CancellationToken ct)
    {
        var b = await ResolveAsync(slug, ct);
        if (!b.BookingEnabled) throw new ConflictException($"{b.Name} isn't taking online bookings right now.", "booking_closed");
        await core.EnsureMonthlyLimitAsync(b, ct);
        var service = await ActiveServiceAsync(r.ServiceId, ct);
        var startsAt = r.StartsAt.ToUniversalTime();
        var staffId = await core.ResolveStaffAsync(b, service, r.StaffId, startsAt, forPublic: true, force: false, null, ct);
        var customer = await core.UpsertCustomerAsync(r.Customer, ct);
        var deposit = await core.DepositsEnabledAsync(b, ct) ? service.DepositMinor : 0;
        var booking = core.NewBooking(service, staffId, startsAt, customer, BookingSource.Online, r.Notes, deposit);
        db.Bookings.Add(booking);
        if (booking.Status == BookingStatus.Confirmed) await core.OnConfirmedAsync(booking, customer, ct);
        await core.SaveBookingAsync(booking, ct);

        string? url = null;
        if (booking.Status == BookingStatus.Pending)
        {
            var email = customer.Email ?? $"{customer.Phone}@customers.booklink.invalid";
            url = await StartPaymentAsync(b, booking, email, ct);
        }
        return new PublicBookResult(await ToPublicAsync(booking, b, ct), url, core.ManageUrl(booking.ManageToken));
    }

    public async Task<PublicBookingDto> GetAsync(string token, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        return await ToPublicAsync(booking, b, ct);
    }

    public async Task<PayResultDto> PayAsync(string token, BookingPayRequest r, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        await EnsureOpenAsync(booking, b, ct);
        if (booking.Status != BookingStatus.Pending || booking.HoldExpiresAt <= core.Now)
            throw new ConflictException("This booking isn't waiting for a deposit any more. Book a new time.", "hold_expired");
        if (BookingCore.DepositDueMinor(booking) <= 0)
            throw new ConflictException($"Your deposit is already covered. Contact {b.Name} to confirm your booking.", "deposit_paid");
        var email = string.IsNullOrWhiteSpace(r.Email) ? booking.Customer!.Email ?? $"{booking.Customer.Phone}@customers.booklink.invalid" : r.Email.Trim();
        return new PayResultDto(await StartPaymentAsync(b, booking, email, ct));
    }

    /// <summary>Called when the customer returns from Paystack; asks Paystack whether the deposit went through.</summary>
    public async Task<PublicBookingDto> VerifyAsync(string token, string reference, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        if (reference.StartsWith($"bk_{booking.Id:N}_", StringComparison.Ordinal) && b.PaystackSecretKeyProtected is { } key
            && booking.Payments.All(p => p.ProviderRef != reference))
        {
            VerifiedCharge? charge;
            try { charge = await payments.VerifyAsync(secrets.Unprotect(key), reference, ct); }
            catch (HttpRequestException) { charge = null; } // Paystack unavailable: not confirmed yet
            if (charge is not null) await RecordDepositAsync(booking, charge, ct);
        }
        return await ToPublicAsync(booking, b, ct);
    }

    public async Task<bool> HandleWebhookAsync(Guid tenantId, string rawBody, string? signature,
        Func<string, string, string?, bool> isValidSignature, Func<string, VerifiedCharge?> parse, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Product == Products.Bookings, ct)) return false;
        tenant.SetTenant(tenantId);
        var b = await db.BookingBusinesses.FirstOrDefaultAsync(ct);
        if (b?.PaystackSecretKeyProtected is not { } key || !isValidSignature(secrets.Unprotect(key), rawBody, signature)) return false;
        if (parse(rawBody) is not { } charge) return true;
        var parts = charge.Reference.Split('_');
        if (parts.Length != 3 || parts[0] != "bk" || !Guid.TryParseExact(parts[1], "N", out var bookingId)) return true;
        var booking = await core.LoadAsync(bookingId, true, ct);
        if (booking.Payments.All(p => p.ProviderRef != charge.Reference)) await RecordDepositAsync(booking, charge, ct);
        return true;
    }

    /// <summary>
    /// Records an online deposit. If the hold already ran out and someone else took the time, the money is still
    /// recorded and the booking stays expired with a note, so the business can refund or rebook.
    /// </summary>
    private async Task RecordDepositAsync(Booking booking, VerifiedCharge charge, CancellationToken ct)
    {
        core.AddPayment(booking, charge.AmountMinor, BookingPaymentType.Deposit, BookingPaymentMethod.Online, charge.Reference, charge.Reference);
        if (booking.Status is BookingStatus.Pending or BookingStatus.Expired)
        {
            var wasExpired = booking.Status == BookingStatus.Expired;
            booking.Status = BookingStatus.Pending; // so the change to Confirmed sends confirmations and reminders
            await core.ChangeStatusAsync(booking, BookingStatus.Confirmed, wasExpired ? "Deposit arrived after the hold ran out" : "Deposit paid", ct);
        }
        try
        {
            await core.SaveBookingAsync(booking, ct);
        }
        catch (ConflictException)
        {
            // The slot went to someone else while the hold was expired: keep the payment, flag the booking.
            db.ChangeTracker.Clear();
            var fresh = await core.LoadAsync(booking.Id, true, ct);
            core.AddPayment(fresh, charge.AmountMinor, BookingPaymentType.Deposit, BookingPaymentMethod.Online, charge.Reference, charge.Reference);
            fresh.Status = BookingStatus.Expired;
            fresh.History.Add(core.History(BookingStatus.Expired, "Deposit paid after the time was taken. Refund or rebook this customer."));
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Deposit {Reference} arrived after booking {Code}'s slot was taken", charge.Reference, fresh.Code);
        }
        catch (DbUpdateException ex)
        {
            logger.LogInformation(ex, "Deposit {Reference} was already recorded", charge.Reference);
        }
    }

    /// <summary>A suspended business's customers can still see their booking, but not change or pay for it online.</summary>
    private async Task EnsureOpenAsync(Booking booking, BookingBusiness b, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == booking.TenantId && t.Status == TenantStatus.Active, ct))
            throw new ConflictException($"{b.Name} is currently unavailable. Contact them directly.");
    }

    public async Task<PublicBookingDto> CancelAsync(string token, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        await EnsureOpenAsync(booking, b, ct);
        if (!CanChange(booking, b)) throw new ConflictException($"Online changes close {b.CancellationHours} hours before your appointment. Contact {b.Name}.");
        await core.ChangeStatusAsync(booking, BookingStatus.Cancelled, "Cancelled by the customer", ct);
        await core.SaveBookingAsync(booking, ct);
        return await ToPublicAsync(booking, b, ct);
    }

    public async Task<PublicBookingDto> RescheduleAsync(string token, PublicRescheduleRequest r, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        await EnsureOpenAsync(booking, b, ct);
        if (booking.Status != BookingStatus.Confirmed || !CanChange(booking, b))
            throw new ConflictException($"This booking can't be moved online. Contact {b.Name}.");
        var service = await ActiveServiceAsync(booking.ServiceId, ct);
        var startsAt = r.StartsAt.ToUniversalTime();
        var staffId = await core.ResolveStaffAsync(b, service, r.StaffId ?? booking.StaffId, startsAt, forPublic: true, force: false, booking.Id, ct);
        booking.StaffId = staffId;
        BookingCore.SetTimes(booking, service, startsAt);
        booking.History.Add(core.History(booking.Status, "Moved by the customer"));
        await core.ScheduleRemindersAsync(booking, booking.Customer!, ct);
        await core.SaveBookingAsync(booking, ct);
        return await ToPublicAsync(booking, b, ct);
    }

    public async Task<PublicBookingDto> ReviewAsync(string token, ReviewRequest r, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        if (booking.Status != BookingStatus.Completed) throw new ConflictException("You can review after your appointment.");
        booking.Rating = r.Rating;
        booking.ReviewComment = string.IsNullOrWhiteSpace(r.Comment) ? null : r.Comment.Trim();
        booking.ReviewedAt = core.Now;
        await db.SaveChangesAsync(ct);
        return await ToPublicAsync(booking, b, ct);
    }

    public async Task<string> IcsAsync(string token, CancellationToken ct)
    {
        var (booking, b) = await ResolveBookingAsync(token, ct);
        var staff = await db.StaffMembers.Where(s => s.Id == booking.StaffId).Select(s => s.DisplayName).FirstOrDefaultAsync(ct);
        return Ics.Calendar(b.Name, [Ics.Event(booking.Id, booking.StartsAt, booking.EndsAt, $"{booking.ServiceName} at {b.Name}",
            $"With {staff}. Manage: {core.ManageUrl(booking.ManageToken)}", b.Address, booking.Status == BookingStatus.Cancelled)]);
    }

    /// <summary>A staff member's private calendar feed: the last month and everything ahead.</summary>
    public async Task<string> StaffFeedAsync(string feedToken, CancellationToken ct)
    {
        var staff = await db.StaffMembers.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.FeedToken == feedToken && db.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Calendar");
        tenant.SetTenant(staff.TenantId);
        var b = await core.BusinessAsync(ct);
        var since = core.Now.AddDays(-31);
        var bookings = await db.Bookings.AsNoTracking().Include(x => x.Customer)
            .Where(x => x.StaffId == staff.Id && x.StartsAt >= since && Booking.Holding.Contains(x.Status)).OrderBy(x => x.StartsAt).ToListAsync(ct);
        return Ics.Calendar($"{staff.DisplayName} at {b.Name}", bookings.Select(x => Ics.Event(x.Id, x.StartsAt, x.EndsAt,
            $"{x.ServiceName}: {x.Customer!.Name}", $"Phone {x.Customer.Phone}{(x.Notes is null ? "" : $". {x.Notes}")}", b.Address, false)).ToList());
    }

    // Helpers

    private async Task<string> StartPaymentAsync(BookingBusiness b, Booking booking, string email, CancellationToken ct)
    {
        var reference = $"bk_{booking.Id:N}_{Codes.Random(8).ToLowerInvariant()}";
        return await payments.InitializeAsync(secrets.Unprotect(b.PaystackSecretKeyProtected!), new PaymentInit(
            email, BookingCore.DepositDueMinor(booking), b.Currency, reference, core.ManageUrl(booking.ManageToken),
            new() { ["booking"] = booking.Code, ["tenantId"] = booking.TenantId.ToString() }), ct);
    }

    private bool CanChange(Booking booking, BookingBusiness b) =>
        booking.Status is BookingStatus.Confirmed or BookingStatus.Pending && core.Now <= booking.StartsAt.AddHours(-b.CancellationHours);

    private async Task<BookingBusiness> ResolveAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var b = await db.BookingBusinesses.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normalized && db.Tenants.Any(t => t.Id == x.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Business");
        tenant.SetTenant(b.TenantId);
        return b;
    }

    private async Task<(Booking, BookingBusiness)> ResolveBookingAsync(string token, CancellationToken ct)
    {
        var id = await db.Bookings.IgnoreQueryFilters().Where(x => x.ManageToken == token)
            .Select(x => new { x.Id, x.TenantId }).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Booking");
        tenant.SetTenant(id.TenantId);
        var booking = await core.LoadAsync(id.Id, true, ct);
        if (booking.Status == BookingStatus.Pending && booking.HoldExpiresAt <= core.Now)
        {
            booking.Status = BookingStatus.Expired;
            booking.History.Add(core.History(BookingStatus.Expired, "The deposit wasn't paid in time"));
            await db.SaveChangesAsync(ct);
        }
        return (booking, await core.BusinessAsync(ct));
    }

    private async Task<BookableService> ActiveServiceAsync(Guid id, CancellationToken ct) =>
        await db.BookableServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.IsActive, ct)
        ?? throw BadRequestException.Field("serviceId", "That service isn't available.");

    private async Task<PublicBookingDto> ToPublicAsync(Booking booking, BookingBusiness b, CancellationToken ct)
    {
        var staff = await db.StaffMembers.AsNoTracking().Where(s => s.Id == booking.StaffId)
            .Select(s => new PublicStaffRef(s.Id, s.DisplayName, s.PhotoUrl)).FirstOrDefaultAsync(ct) ?? new PublicStaffRef(booking.StaffId, "", null);
        var customer = booking.Customer ?? await db.BookingCustomers.AsNoTracking().FirstAsync(c => c.Id == booking.CustomerId, ct);
        var deadline = booking.StartsAt.AddHours(-b.CancellationHours);
        var canChange = CanChange(booking, b);
        return new PublicBookingDto(booking.Code, booking.Status, booking.StartsAt, booking.EndsAt, b.Timezone,
            new PublicServiceRef(booking.ServiceId, booking.ServiceName, booking.DurationMinutes), staff, customer.Name, booking.PriceMinor,
            booking.DepositMinor, booking.PaidMinor, Math.Max(0, booking.PriceMinor - booking.PaidMinor), b.Currency,
            booking.Status == BookingStatus.Pending ? booking.HoldExpiresAt : null,
            canChange, canChange && booking.Status == BookingStatus.Confirmed, deadline,
            booking.Status == BookingStatus.Completed && booking.Rating is null,
            booking.Rating is { } rating ? new ReviewDto(rating, booking.ReviewComment, booking.ReviewedAt ?? core.Now) : null,
            new PublicBusinessRef(b.Name, b.Slug, b.Address, b.Phone, b.WhatsappNumber, b.LogoUrl, b.BrandColor, b.Policies),
            BookingCore.DepositDueMinor(booking));
    }
}

public sealed record PayResultDto(string AuthorizationUrl);

/// <summary>Minimal iCalendar (RFC 5545) writer for booking files and staff feeds.</summary>
public static class Ics
{
    public static string Calendar(string name, IReadOnlyList<string> events)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//BookLink//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n");
        sb.Append($"X-WR-CALNAME:{Escape(name)}\r\n");
        foreach (var e in events) sb.Append(e);
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    public static string Event(Guid id, DateTimeOffset start, DateTimeOffset end, string summary, string description, string? location, bool cancelled) =>
        "BEGIN:VEVENT\r\n" +
        $"UID:{id:N}@booklink\r\nDTSTAMP:{Utc(DateTimeOffset.UtcNow)}\r\nDTSTART:{Utc(start)}\r\nDTEND:{Utc(end)}\r\n" +
        $"SUMMARY:{Escape(summary)}\r\nDESCRIPTION:{Escape(description)}\r\n" +
        (location is null ? "" : $"LOCATION:{Escape(location)}\r\n") +
        $"STATUS:{(cancelled ? "CANCELLED" : "CONFIRMED")}\r\nEND:VEVENT\r\n";

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
}
