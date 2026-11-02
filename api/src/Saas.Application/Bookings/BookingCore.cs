using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Bookings;

namespace Saas.Application.Bookings;

/// <summary>
/// The rules every booking path shares (business app, public page, reschedule, deposits, jobs):
/// availability, holding slots, the double-booking safety net, payments, status changes and reminders.
/// </summary>
public sealed class BookingCore(
    IAppDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    PlanService plans,
    NotificationQueue notifications,
    IDbErrors dbErrors,
    AppUrls urls,
    TimeProvider clock)
{
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(10);

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<BookingBusiness> BusinessAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        return await db.BookingBusinesses.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Booking profile");
    }

    public string ManageUrl(string token) => $"{urls.Web(Products.Bookings)}/b/{token}";
    public string BookingPageUrl(string slug) => $"{urls.Web(Products.Bookings)}/{slug}";
    public string FeedUrl(string token) => $"{urls.ApiBaseUrl.TrimEnd('/')}/api/public/staff-calendar/{token}.ics";

    public static string NewToken(int bytes = 18) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Deposits are only taken when the plan includes them and the business has connected Paystack.</summary>
    public async Task<bool> DepositsEnabledAsync(BookingBusiness business, CancellationToken ct) =>
        business.PaystackSecretKeyProtected is not null && await plans.HasFeatureAsync(LimitKeys.Deposits, ct);

    // Availability

    /// <summary>Slots for a service on a local date, for one staff member or everyone who does the service.</summary>
    public async Task<IReadOnlyList<SlotResult>> SlotsAsync(BookingBusiness business, BookableService service, Guid? staffId,
        DateOnly date, bool forPublic, bool ignoreHours, Guid? excludeBookingId, CancellationToken ct)
    {
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var staffIds = staffId is { } one ? [one] : service.StaffIds;
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id) && (!forPublic || s.IsBookable)).ToListAsync(ct);
        staff = staff.Where(s => service.StaffIds.Contains(s.Id) || !forPublic).ToList();
        if (staff.Count == 0) return [];

        var hours = await db.BusinessHours.AsNoTracking().FirstOrDefaultAsync(h => h.Weekday == (int)date.DayOfWeek, ct);
        var day = AvailabilityEngine.Day(date, zone);
        // Look a little either side of the day so buffers and overnight bookings are seen.
        var window = new Span(day.Start.AddHours(-12), day.End.AddHours(12));
        var busy = await BusySpansAsync(staff.Select(s => s.Id).ToList(), window, excludeBookingId, ct);

        var earliest = forPublic ? Now.AddMinutes(business.MinNoticeMinutes) : Now.AddMinutes(-60 * 24 * 365);
        if (forPublic && date > AvailabilityEngine.LocalDate(Now, zone).AddDays(business.MaxDaysAhead)) return [];

        return AvailabilityEngine.Compute(new AvailabilityInput(
            date, zone, hours is null || hours.Closed ? null : (hours.Open, hours.Close),
            service.DurationMinutes, service.BufferBeforeMinutes, service.BufferAfterMinutes, business.SlotIntervalMinutes, earliest,
            staff.Select(s => new StaffInput(s.Id, s.Schedule, busy.GetValueOrDefault(s.Id) ?? [])).ToList(),
            IgnoreHours: ignoreHours));
    }

    /// <summary>Bookings that hold time (including unexpired deposit holds) and time off, per staff member.</summary>
    private async Task<Dictionary<Guid, List<Span>>> BusySpansAsync(List<Guid> staffIds, Span window, Guid? excludeBookingId, CancellationToken ct)
    {
        var now = Now;
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => staffIds.Contains(b.StaffId) && Booking.Holding.Contains(b.Status) && b.Id != excludeBookingId
                && b.BlockedFrom < window.End && b.BlockedTo > window.Start
                && (b.Status != BookingStatus.Pending || b.HoldExpiresAt > now))
            .Select(b => new { b.StaffId, b.BlockedFrom, b.BlockedTo }).ToListAsync(ct);
        var timeOff = await db.TimeOffs.AsNoTracking()
            .Where(t => staffIds.Contains(t.StaffId) && t.StartsAt < window.End && t.EndsAt > window.Start)
            .Select(t => new { t.StaffId, t.StartsAt, t.EndsAt }).ToListAsync(ct);
        return bookings.Select(b => (b.StaffId, Span: new Span(b.BlockedFrom, b.BlockedTo)))
            .Concat(timeOff.Select(t => (t.StaffId, Span: new Span(t.StartsAt, t.EndsAt))))
            .GroupBy(x => x.StaffId).ToDictionary(g => g.Key, g => g.Select(x => x.Span).ToList());
    }

    /// <summary>
    /// Checks the requested start is an offered slot and picks the staff member: the one asked for, or, for
    /// "anyone", whoever has the fewest bookings that day.
    /// </summary>
    public async Task<Guid> ResolveStaffAsync(BookingBusiness business, BookableService service, Guid? staffId, DateTimeOffset startsAt,
        bool forPublic, bool force, Guid? excludeBookingId, CancellationToken ct)
    {
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var date = AvailabilityEngine.LocalDate(startsAt, zone);
        var slots = await SlotsAsync(business, service, staffId, date, forPublic, ignoreHours: force, excludeBookingId, ct);
        var slot = slots.FirstOrDefault(s => s.StartsAt == startsAt);
        if (slot is null && force && staffId is { } chosen) return chosen; // outside the grid; the database still guards overlaps
        if (slot is null)
            throw new ConflictException("That time is no longer available. Please choose another time.", "slot_unavailable");
        if (staffId is { } wanted) return wanted;

        var dayStart = AvailabilityEngine.Day(date, zone);
        var load = await db.Bookings.AsNoTracking()
            .Where(b => slot.StaffIds.Contains(b.StaffId) && Booking.Holding.Contains(b.Status)
                && b.StartsAt >= dayStart.Start && b.StartsAt < dayStart.End)
            .GroupBy(b => b.StaffId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return slot.StaffIds.OrderBy(id => load.GetValueOrDefault(id)).First();
    }

    // Creating and changing bookings

    public async Task<BookingCustomer> UpsertCustomerAsync(CustomerInput input, CancellationToken ct)
    {
        var country = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.Country).FirstAsync(ct);
        var phone = PhoneNumbers.Normalize(input.Phone, country) ?? throw BadRequestException.Field("customer.phone", "Enter a valid phone number.");
        var customer = await db.BookingCustomers.FirstOrDefaultAsync(c => c.Phone == phone, ct);
        if (customer is null)
        {
            customer = new BookingCustomer { Name = input.Name.Trim(), Phone = phone };
            db.BookingCustomers.Add(customer);
        }
        customer.Name = input.Name.Trim();
        if (!string.IsNullOrWhiteSpace(input.Email)) customer.Email = input.Email.Trim().ToLowerInvariant();
        return customer;
    }

    public async Task EnsureMonthlyLimitAsync(BookingBusiness business, CancellationToken ct)
    {
        var monthStart = MonthStart(business);
        var count = await db.Bookings.CountAsync(b => b.CreatedAt >= monthStart && b.Status != BookingStatus.Expired, ct);
        await plans.EnsureCanAddAsync(LimitKeys.BookingsPerMonth, count, "Bookings this month", ct);
    }

    public DateTimeOffset MonthStart(BookingBusiness business)
    {
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var today = AvailabilityEngine.LocalDate(Now, zone);
        return AvailabilityEngine.Day(new DateOnly(today.Year, today.Month, 1), zone).Start;
    }

    public Booking NewBooking(BookableService service, Guid staffId, DateTimeOffset startsAt, BookingCustomer customer,
        BookingSource source, string? notes, long depositMinor)
    {
        var booking = new Booking
        {
            Code = Codes.Random(6),
            ManageToken = NewToken(),
            CustomerId = customer.Id,
            StaffId = staffId,
            ServiceId = service.Id,
            ServiceName = service.Name,
            DurationMinutes = service.DurationMinutes,
            PriceMinor = service.PriceMinor,
            DepositMinor = depositMinor,
            Source = source,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = depositMinor > 0 ? BookingStatus.Pending : BookingStatus.Confirmed,
            HoldExpiresAt = depositMinor > 0 ? Now.Add(HoldFor) : null,
        };
        SetTimes(booking, service, startsAt);
        booking.History.Add(History(booking.Status, depositMinor > 0 ? "Waiting for deposit" : "Booked"));
        return booking;
    }

    public static void SetTimes(Booking booking, BookableService service, DateTimeOffset startsAt)
    {
        booking.StartsAt = startsAt;
        booking.EndsAt = startsAt.AddMinutes(booking.DurationMinutes);
        booking.BlockedFrom = startsAt.AddMinutes(-service.BufferBeforeMinutes);
        booking.BlockedTo = booking.EndsAt.AddMinutes(service.BufferAfterMinutes);
    }

    /// <summary>
    /// Saves, turning the database's overlap rejection into a friendly conflict. Stale deposit holds on the same
    /// staff member are expired first so they don't block the slot.
    /// </summary>
    public async Task SaveBookingAsync(Booking booking, CancellationToken ct)
    {
        var now = Now;
        await db.Bookings.Where(b => b.StaffId == booking.StaffId && b.Id != booking.Id && b.Status == BookingStatus.Pending && b.HoldExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (dbErrors.IsExclusionViolation(ex) || dbErrors.IsConcurrencyConflict(ex)) // a deadlock between two bookings is the same lost race
        {
            throw new ConflictException("Someone else just booked that time. Please choose another.", "slot_taken");
        }
    }

    public async Task ChangeStatusAsync(Booking booking, BookingStatus status, string? note, CancellationToken ct)
    {
        if (booking.Status == status) return;
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired && status != BookingStatus.Confirmed)
            throw new ConflictException("This booking was cancelled. Make a new booking instead.");
        var previous = booking.Status;
        booking.Status = status;
        booking.HoldExpiresAt = status == BookingStatus.Pending ? booking.HoldExpiresAt : null;
        booking.History.Add(History(status, note));

        var customer = booking.Customer ?? await db.BookingCustomers.FirstAsync(c => c.Id == booking.CustomerId, ct);
        if (status == BookingStatus.NoShow) customer.NoShowCount++;
        if (previous == BookingStatus.NoShow && customer.NoShowCount > 0) customer.NoShowCount--;

        if (status is BookingStatus.Cancelled or BookingStatus.NoShow or BookingStatus.Completed or BookingStatus.Expired)
            CancelReminders(booking);
        if (status == BookingStatus.Confirmed && previous is BookingStatus.Pending or BookingStatus.Cancelled or BookingStatus.Expired)
            await OnConfirmedAsync(booking, customer, ct);
    }

    /// <summary>Confirmation emails and reminders, sent when a booking becomes confirmed.</summary>
    public async Task OnConfirmedAsync(Booking booking, BookingCustomer customer, CancellationToken ct)
    {
        var business = await BusinessAsync(ct);
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var when = TimeZoneInfo.ConvertTime(booking.StartsAt, zone).ToString("ddd d MMM, HH:mm");
        var staffName = await db.StaffMembers.Where(s => s.Id == booking.StaffId).Select(s => s.DisplayName).FirstOrDefaultAsync(ct) ?? "";
        var payload = new Dictionary<string, string>
        {
            ["business"] = business.Name,
            ["customer"] = customer.Name,
            ["service"] = booking.ServiceName,
            ["staff"] = staffName,
            ["when"] = when,
            ["address"] = business.Address ?? "",
            ["link"] = ManageUrl(booking.ManageToken),
        };
        if (!string.IsNullOrWhiteSpace(customer.Email))
            notifications.Email(customer.Email, "booking.confirmed", payload, booking.TenantId);
        if (!string.IsNullOrWhiteSpace(business.Email))
            notifications.Email(business.Email, "booking.new", new(payload) { ["phone"] = customer.Phone }, booking.TenantId);
        await ScheduleRemindersAsync(booking, customer, ct);
    }

    public async Task ScheduleRemindersAsync(Booking booking, BookingCustomer customer, CancellationToken ct)
    {
        CancelReminders(booking);
        if (string.IsNullOrWhiteSpace(customer.Email) || !await plans.HasFeatureAsync(LimitKeys.BookingReminders, ct)) return;
        foreach (var before in new[] { TimeSpan.FromHours(24), TimeSpan.FromHours(2) })
        {
            var sendAt = booking.StartsAt - before;
            if (sendAt > Now) booking.Reminders.Add(new BookingReminder { BookingId = booking.Id, Channel = ReminderChannel.Email, SendAt = sendAt });
        }
    }

    public static void CancelReminders(Booking booking)
    {
        foreach (var r in booking.Reminders.Where(r => r.Status == ReminderStatus.Scheduled)) r.Status = ReminderStatus.Cancelled;
    }

    public void AddPayment(Booking booking, long amountMinor, BookingPaymentType type, BookingPaymentMethod method, string? reference, string? providerRef)
    {
        booking.Payments.Add(new BookingPayment
        {
            BookingId = booking.Id, AmountMinor = amountMinor, Type = type, Method = method,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), ProviderRef = providerRef,
        });
        booking.PaidMinor = booking.Payments.Sum(p => p.Type == BookingPaymentType.Refund ? -p.AmountMinor : p.AmountMinor);
    }

    public const string RefundOfPrefix = "Refund of ";

    /// <summary>
    /// Each online (Paystack) payment with what can still go back on it. A refund row names the charge it went back
    /// against in its reference ("Refund of {providerRef}"); refunds made before that was tracked per charge all went
    /// against the first online payment, so unmatched refunds are counted against the earliest charges first.
    /// </summary>
    public static IReadOnlyList<(BookingPayment Payment, long Refundable)> RefundableCharges(Booking booking)
    {
        var charges = booking.Payments.Where(p => p.ProviderRef is not null && p.Type != BookingPaymentType.Refund)
            .OrderBy(p => p.CreatedAt).ToList();
        var refunds = booking.Payments.Where(p => p.Type == BookingPaymentType.Refund).ToList();
        var left = charges.ToDictionary(p => p.ProviderRef!, p => p.AmountMinor);
        long unmatched = 0;
        foreach (var r in refunds)
        {
            var target = r.Reference is { } reference && reference.StartsWith(RefundOfPrefix, StringComparison.Ordinal)
                ? reference[RefundOfPrefix.Length..] : null;
            if (target is not null && left.ContainsKey(target)) left[target] -= r.AmountMinor;
            else unmatched += r.AmountMinor;
        }
        var result = new List<(BookingPayment, long)>();
        foreach (var p in charges)
        {
            var available = Math.Max(0, left[p.ProviderRef!]);
            var take = Math.Min(available, unmatched);
            unmatched -= take;
            result.Add((p, available - take));
        }
        return result;
    }

    /// <summary>What the business can still send back through Paystack.</summary>
    public static long RefundableMinor(Booking booking) => RefundableCharges(booking).Sum(x => x.Refundable);

    /// <summary>
    /// What the customer still owes towards the deposit of a booking waiting for one. The /pay link charges exactly
    /// this, and the manage page shows exactly this.
    /// </summary>
    public static long DepositDueMinor(Booking booking) =>
        booking.Status == BookingStatus.Pending ? Math.Max(0, booking.DepositMinor - booking.PaidMinor) : 0;

    public BookingStatusHistory History(BookingStatus status, string? note) =>
        new() { Status = status, Note = note, ChangedBy = user.UserId };

    public async Task<Booking> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = db.Bookings.Include(b => b.Customer).Include(b => b.Payments).Include(b => b.History).Include(b => b.Reminders);
        return await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Booking");
    }

    public async Task<BookableService> ServiceAsync(Guid id, CancellationToken ct) =>
        await db.BookableServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw BadRequestException.Field("serviceId", "That service does not exist.");

    // Mapping

    public async Task<IReadOnlyList<BookingSummaryDto>> SummariesAsync(IReadOnlyList<Booking> bookings, CancellationToken ct)
    {
        var staffIds = bookings.Select(b => b.StaffId).Distinct().ToList();
        var staff = await db.StaffMembers.AsNoTracking().Where(s => staffIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => (s.DisplayName, s.Color), ct);
        var customerIds = bookings.Where(b => b.Customer is null).Select(b => b.CustomerId).Distinct().ToList();
        var customers = await db.BookingCustomers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return bookings.Select(b =>
        {
            var c = b.Customer ?? customers[b.CustomerId];
            var (name, color) = staff.GetValueOrDefault(b.StaffId, ("(removed)", "#9A8FA6"));
            return new BookingSummaryDto(b.Id, b.Code, b.StartsAt, b.EndsAt, b.Status, c.Name, c.Phone, b.ServiceName, b.StaffId,
                name, color, b.PriceMinor, b.DepositMinor, b.PaidMinor, b.Source);
        }).ToList();
    }

    public async Task<BookingDto> ToDtoAsync(Booking b, CancellationToken ct)
    {
        var s = (await SummariesAsync([b], ct))[0];
        var c = b.Customer ?? await db.BookingCustomers.AsNoTracking().FirstAsync(x => x.Id == b.CustomerId, ct);
        var business = await BusinessAsync(ct);
        var when = TimeZoneInfo.ConvertTime(b.StartsAt, AvailabilityEngine.Zone(business.Timezone)).ToString("dddd d MMMM 'at' HH:mm");
        var reminder = $"Hi {c.Name}, this is a reminder of your {b.ServiceName} appointment at {business.Name} on {when}. " +
                       $"Manage your booking: {ManageUrl(b.ManageToken)}";
        return new BookingDto(s.Id, s.Code, s.StartsAt, s.EndsAt, s.Status, s.CustomerName, s.CustomerPhone, s.ServiceName, s.StaffId,
            s.StaffName, s.StaffColor, s.PriceMinor, s.DepositMinor, s.PaidMinor, s.Source,
            new BookingCustomerRef(c.Id, c.Name, c.Phone, c.Email, c.NoShowCount), b.ServiceId, b.Notes, b.HoldExpiresAt,
            ManageUrl(b.ManageToken), $"https://wa.me/{c.Phone}?text={Uri.EscapeDataString(reminder)}",
            b.Payments.OrderBy(p => p.CreatedAt).Select(p => new BookingPaymentDto(p.Id, p.AmountMinor, p.Type, p.Method, p.Reference,
                p.ProviderRef is not null, p.CreatedAt)).ToList(),
            b.History.OrderBy(h => h.CreatedAt).Select(h => new BookingHistoryDto(h.Status, h.Note, h.CreatedAt)).ToList(),
            b.Reminders.OrderBy(r => r.SendAt).Select(r => new ReminderDto(r.Channel, r.SendAt, r.SentAt, r.Status)).ToList(),
            b.Rating is { } rating ? new ReviewDto(rating, b.ReviewComment, b.ReviewedAt ?? b.UpdatedAt ?? b.CreatedAt) : null,
            RefundableMinor(b));
    }

    public static HoursDto ToDto(BusinessHours h) => new(h.Weekday, h.Open.ToString("HH:mm"), h.Close.ToString("HH:mm"), h.Closed);
}
