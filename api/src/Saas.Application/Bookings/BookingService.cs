using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Bookings;

namespace Saas.Application.Bookings;

/// <summary>The business side: calendar, bookings, payments, customers, dashboard and reports.</summary>
public sealed class BookingService(
    IAppDbContext db, BookingCore core, PlanService plans, IMerchantPayments payments, ISecretProtector secrets)
{
    public async Task<AvailabilityDto> AvailabilityAsync(Guid serviceId, Guid? staffId, DateOnly date, CancellationToken ct)
    {
        var business = await core.BusinessAsync(ct);
        var service = await core.ServiceAsync(serviceId, ct);
        var slots = await core.SlotsAsync(business, service, staffId, date, forPublic: false, ignoreHours: false, null, ct);
        return new AvailabilityDto(date, business.Timezone, slots.Select(s => new SlotDto(s.StartsAt, s.EndsAt, s.StaffIds)).ToList());
    }

    public async Task<Paged<BookingSummaryDto>> ListAsync(DateOnly? from, DateOnly? to, Guid? staffId, BookingStatus? status, string? q,
        int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var business = await core.BusinessAsync(ct);
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var query = db.Bookings.AsNoTracking().Include(b => b.Customer).AsQueryable();
        if (from is { } f) { var start = AvailabilityEngine.Day(f, zone).Start; query = query.Where(b => b.StartsAt >= start); }
        if (to is { } t) { var end = AvailabilityEngine.Day(t, zone).End; query = query.Where(b => b.StartsAt < end); }
        if (staffId is { } sid) query = query.Where(b => b.StaffId == sid);
        if (status is { } st) query = query.Where(b => b.Status == st);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(b => b.Customer!.Name.ToLower().Contains(term) || b.Customer.Phone.Contains(term)
                || b.Code.ToLower() == term || b.ServiceName.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(b => b.StartsAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<BookingSummaryDto>(await core.SummariesAsync(rows, ct), page, pageSize, total);
    }

    public async Task<BookingDto> GetAsync(Guid id, CancellationToken ct) => await core.ToDtoAsync(await core.LoadAsync(id, false, ct), ct);

    /// <summary>Walk-ins and phone bookings. No deposit hold: the business confirms them directly.</summary>
    public async Task<BookingDto> CreateAsync(CreateBookingRequest r, CancellationToken ct)
    {
        var business = await core.BusinessAsync(ct);
        await core.EnsureMonthlyLimitAsync(business, ct);
        var service = await core.ServiceAsync(r.ServiceId, ct);
        if (r.Force && r.StaffId is null) throw BadRequestException.Field("staffId", "Choose who the booking is with.");
        var staffId = await core.ResolveStaffAsync(business, service, r.StaffId, r.StartsAt, forPublic: false, r.Force, null, ct);
        var customer = await core.UpsertCustomerAsync(r.Customer, ct);
        var booking = core.NewBooking(service, staffId, r.StartsAt.ToUniversalTime(), customer, r.Source, r.Notes, depositMinor: 0);
        db.Bookings.Add(booking);
        await core.OnConfirmedAsync(booking, customer, ct);
        await core.SaveBookingAsync(booking, ct);
        return await GetAsync(booking.Id, ct);
    }

    public async Task<BookingDto> RescheduleAsync(Guid id, RescheduleRequest r, CancellationToken ct)
    {
        var booking = await core.LoadAsync(id, true, ct);
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            throw new ConflictException("Only upcoming bookings can be moved.");
        if (r.Notes is not null) booking.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        if (r.StartsAt is { } startsAt && (startsAt != booking.StartsAt || (r.StaffId is { } s && s != booking.StaffId)))
        {
            var business = await core.BusinessAsync(ct);
            var service = await db.BookableServices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == booking.ServiceId, ct)
                ?? new BookableService { Name = booking.ServiceName, DurationMinutes = booking.DurationMinutes };
            var staffId = r.StaffId ?? booking.StaffId;
            // The business may move a booking anywhere free, even outside the usual hours.
            await core.ResolveStaffAsync(business, service, staffId, startsAt, forPublic: false, force: true, booking.Id, ct);
            booking.StaffId = staffId;
            BookingCore.SetTimes(booking, service, startsAt.ToUniversalTime());
            booking.History.Add(core.History(booking.Status, "Moved"));
            if (booking.Status == BookingStatus.Confirmed) await core.ScheduleRemindersAsync(booking, booking.Customer!, ct);
        }
        await core.SaveBookingAsync(booking, ct);
        return await GetAsync(id, ct);
    }

    public async Task<BookingDto> ChangeStatusAsync(Guid id, ChangeStatusRequest r, CancellationToken ct)
    {
        if (r.Status is BookingStatus.Pending or BookingStatus.Expired)
            throw BadRequestException.Field("status", "Choose Confirmed, Completed, Cancelled or No-show.");
        var booking = await core.LoadAsync(id, true, ct);
        await core.ChangeStatusAsync(booking, r.Status, string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(), ct);
        await core.SaveBookingAsync(booking, ct);
        return await GetAsync(id, ct);
    }

    public async Task<BookingDto> RecordPaymentAsync(Guid id, RecordBookingPaymentRequest r, CancellationToken ct)
    {
        var booking = await core.LoadAsync(id, true, ct);
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
            throw new ConflictException("This booking was cancelled.");
        core.AddPayment(booking, r.AmountMinor, r.Type, r.Method, r.Reference, providerRef: null);
        if (booking.Status == BookingStatus.Pending && r.Type == BookingPaymentType.Deposit)
            await core.ChangeStatusAsync(booking, BookingStatus.Confirmed, "Deposit recorded", ct);
        await core.SaveBookingAsync(booking, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Sends money back through Paystack, across every online payment on the booking (a customer can pay twice from
    /// two checkout links). Each charge is refunded against its own Paystack reference, earliest first.
    /// </summary>
    public async Task<BookingDto> RefundAsync(Guid id, RefundRequest r, CancellationToken ct)
    {
        var booking = await core.LoadAsync(id, true, ct);
        var charges = BookingCore.RefundableCharges(booking);
        if (charges.Count == 0)
            throw new ConflictException("There is no online payment to refund. Record a cash or transfer refund outside BookLink.");
        if (r.AmountMinor <= 0 || r.AmountMinor > charges.Sum(c => c.Refundable))
            throw BadRequestException.Field("amountMinor", "Refund up to the amount paid online that hasn't been refunded yet.");
        var business = await core.BusinessAsync(ct);
        var key = secrets.Unprotect(business.PaystackSecretKeyProtected ?? throw new ConflictException("Connect Paystack again to send refunds."));

        var remaining = r.AmountMinor;
        var sent = false;
        try
        {
            foreach (var (payment, refundable) in charges)
            {
                if (remaining == 0) break;
                var amount = Math.Min(refundable, remaining);
                if (amount == 0) continue;
                await payments.RefundAsync(key, payment.ProviderRef!, amount, ct);
                core.AddPayment(booking, amount, BookingPaymentType.Refund, BookingPaymentMethod.Online, $"{BookingCore.RefundOfPrefix}{payment.ProviderRef}", null);
                remaining -= amount;
                sent = true;
            }
        }
        catch when (sent)
        {
            // Paystack refused a later charge: keep the record of what already went back, then report the failure.
            booking.History.Add(core.History(booking.Status, "Refund partly sent"));
            await core.SaveBookingAsync(booking, CancellationToken.None);
            throw;
        }
        booking.History.Add(core.History(booking.Status, "Refund sent"));
        await core.SaveBookingAsync(booking, ct);
        return await GetAsync(id, ct);
    }

    public async Task<CalendarDto> CalendarAsync(DateOnly from, DateOnly to, Guid? staffId, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 42) throw BadRequestException.Field("to", "Choose up to six weeks.");
        var business = await core.BusinessAsync(ct);
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var (start, end) = (AvailabilityEngine.Day(from, zone).Start, AvailabilityEngine.Day(to, zone).End);
        var staff = await db.StaffMembers.AsNoTracking().Where(s => staffId == null || s.Id == staffId)
            .OrderBy(s => s.Position).ThenBy(s => s.DisplayName).ToListAsync(ct);
        var ids = staff.Select(s => s.Id).ToList();
        var bookings = await db.Bookings.AsNoTracking().Include(b => b.Customer)
            .Where(b => ids.Contains(b.StaffId) && b.StartsAt < end && b.EndsAt > start && b.Status != BookingStatus.Expired)
            .OrderBy(b => b.StartsAt).ToListAsync(ct);
        var timeOff = await db.TimeOffs.AsNoTracking().Where(t => ids.Contains(t.StaffId) && t.StartsAt < end && t.EndsAt > start)
            .Select(t => new TimeOffDto(t.Id, t.StaffId, t.StartsAt, t.EndsAt, t.Reason)).ToListAsync(ct);
        var hours = (await db.BusinessHours.AsNoTracking().OrderBy(h => h.Weekday).ToListAsync(ct)).Select(BookingCore.ToDto).ToList();
        return new CalendarDto(business.Timezone, staff.Select(s => new CalendarStaffDto(s.Id, s.DisplayName, s.Color)).ToList(),
            await core.SummariesAsync(bookings, ct), timeOff, hours);
    }

    // Customers

    public async Task<Paged<BookingCustomerDto>> CustomersAsync(string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.BookingCustomers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.Phone.Contains(term) || (c.Email != null && c.Email.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await CustomerDtos(query.OrderBy(c => c.Name).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return new Paged<BookingCustomerDto>(rows, page, pageSize, total);
    }

    public async Task<BookingCustomerDetailDto> CustomerAsync(Guid id, CancellationToken ct)
    {
        var c = await CustomerDtos(db.BookingCustomers.AsNoTracking().Where(x => x.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Customer");
        var bookings = await db.Bookings.AsNoTracking().Include(b => b.Customer).Where(b => b.CustomerId == id)
            .OrderByDescending(b => b.StartsAt).Take(100).ToListAsync(ct);
        return new BookingCustomerDetailDto(c.Id, c.Name, c.Phone, c.Email, c.Notes, c.BookingsCount, c.CompletedCount, c.NoShowCount,
            c.TotalSpentMinor, c.LastVisitAt, await core.SummariesAsync(bookings, ct));
    }

    public async Task<BookingCustomerDto> UpdateCustomerAsync(Guid id, UpdateCustomerRequest r, CancellationToken ct)
    {
        var customer = await db.BookingCustomers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer");
        var country = await db.Tenants.Where(t => t.Id == customer.TenantId).Select(t => t.Country).FirstAsync(ct);
        var phone = PhoneNumbers.Normalize(r.Phone, country) ?? throw BadRequestException.Field("phone", "Enter a valid phone number.");
        if (phone != customer.Phone && await db.BookingCustomers.AnyAsync(c => c.Phone == phone && c.Id != id, ct))
            throw BadRequestException.Field("phone", "Another customer already has this number.");
        customer.Name = r.Name.Trim();
        customer.Phone = phone;
        customer.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim().ToLowerInvariant();
        customer.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return await CustomerDtos(db.BookingCustomers.AsNoTracking().Where(x => x.Id == id)).FirstAsync(ct);
    }

    private IQueryable<BookingCustomerDto> CustomerDtos(IQueryable<BookingCustomer> customers) =>
        customers.Select(c => new BookingCustomerDto(c.Id, c.Name, c.Phone, c.Email, c.Notes,
            db.Bookings.Count(b => b.CustomerId == c.Id && b.Status != BookingStatus.Expired),
            db.Bookings.Count(b => b.CustomerId == c.Id && b.Status == BookingStatus.Completed),
            c.NoShowCount,
            db.Bookings.Where(b => b.CustomerId == c.Id).Sum(b => (long?)b.PaidMinor) ?? 0,
            db.Bookings.Where(b => b.CustomerId == c.Id && b.Status == BookingStatus.Completed).Max(b => (DateTimeOffset?)b.StartsAt)));

    // Dashboard and reports

    public async Task<BookingDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var business = await core.BusinessAsync(ct);
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var today = AvailabilityEngine.Day(AvailabilityEngine.LocalDate(core.Now, zone), zone);
        var monthStart = core.MonthStart(business);
        var now = core.Now;

        var todays = await db.Bookings.AsNoTracking().Include(b => b.Customer)
            .Where(b => b.StartsAt >= today.Start && b.StartsAt < today.End && b.Status != BookingStatus.Expired)
            .OrderBy(b => b.StartsAt).ToListAsync(ct);
        var upcoming = await db.Bookings.CountAsync(b => b.StartsAt >= now && b.Status == BookingStatus.Confirmed, ct);
        var pending = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending && b.HoldExpiresAt > now, ct);
        var revenue = await RevenueAsync(monthStart, now.AddYears(1), ct);
        var thisMonth = await db.Bookings.CountAsync(b => b.CreatedAt >= monthStart && b.Status != BookingStatus.Expired, ct);
        var completed = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Completed && b.StartsAt >= monthStart, ct);
        var noShows = await db.Bookings.CountAsync(b => b.Status == BookingStatus.NoShow && b.StartsAt >= monthStart, ct);
        var plan = await plans.GetCurrentPlanAsync(ct);
        return new BookingDashboardDto(business.Timezone, business.Currency, await core.SummariesAsync(todays, ct), upcoming, pending,
            revenue, thisMonth, plan.Limit(LimitKeys.BookingsPerMonth), Rate(noShows, completed));
    }

    public async Task<BookingReportDto> ReportAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var business = await core.BusinessAsync(ct);
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var today = AvailabilityEngine.LocalDate(core.Now, zone);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        if (end < start || end.DayNumber - start.DayNumber > 3660) throw BadRequestException.Field("to", "Choose a valid period of 10 years or less.");
        var (s, e) = (AvailabilityEngine.Day(start, zone).Start, AvailabilityEngine.Day(end, zone).End);

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => b.StartsAt >= s && b.StartsAt < e && b.Status != BookingStatus.Expired)
            .Select(b => new { b.StartsAt, b.Status, b.ServiceName, b.StaffId, b.PaidMinor }).ToListAsync(ct);
        // Money is reported by when it moved, and the breakdowns use the same rows, so they add up to the headline.
        var paymentRows = await (from p in db.BookingPayments.AsNoTracking()
                                 join b in db.Bookings.AsNoTracking() on p.BookingId equals b.Id
                                 where p.CreatedAt >= s && p.CreatedAt < e
                                 select new { p.AmountMinor, p.Type, p.CreatedAt, b.ServiceName, b.StaffId }).ToListAsync(ct);
        var staffNames = await db.StaffMembers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        long Signed(BookingPaymentType type, long amount) => type == BookingPaymentType.Refund ? -amount : amount;
        var completed = bookings.Count(b => b.Status == BookingStatus.Completed);
        var noShows = bookings.Count(b => b.Status == BookingStatus.NoShow);
        var days = new List<DayStat>();
        for (var d = start; d <= end && days.Count < 400; d = d.AddDays(1))
        {
            var day = AvailabilityEngine.Day(d, zone);
            days.Add(new DayStat(d, bookings.Count(b => b.StartsAt >= day.Start && b.StartsAt < day.End),
                paymentRows.Where(p => p.CreatedAt >= day.Start && p.CreatedAt < day.End).Sum(p => Signed(p.Type, p.AmountMinor))));
        }

        // Bookings are counted by appointment date; revenue is the money received in the period for those services/staff.
        var byService = bookings.Select(b => b.ServiceName).Concat(paymentRows.Select(p => p.ServiceName)).Distinct()
            .Select(name => new NamedStat(name, bookings.Count(b => b.ServiceName == name),
                paymentRows.Where(p => p.ServiceName == name).Sum(p => Signed(p.Type, p.AmountMinor))))
            .OrderByDescending(x => x.Bookings).ThenByDescending(x => x.RevenueMinor).ToList();
        var byStaff = bookings.Select(b => b.StaffId).Concat(paymentRows.Select(p => p.StaffId)).Distinct()
            .Select(staffId => new StaffStat(staffNames.GetValueOrDefault(staffId, "(removed)"), bookings.Count(b => b.StaffId == staffId),
                paymentRows.Where(p => p.StaffId == staffId).Sum(p => Signed(p.Type, p.AmountMinor)),
                bookings.Count(b => b.StaffId == staffId && b.Status == BookingStatus.NoShow)))
            .OrderByDescending(x => x.Bookings).ThenByDescending(x => x.RevenueMinor).ToList();
        // Only online deposits can be refunded in BookLink, so refunds are netted off the deposits figure.
        var netDeposits = paymentRows.Where(p => p.Type is BookingPaymentType.Deposit or BookingPaymentType.Refund)
            .Sum(p => Signed(p.Type, p.AmountMinor));

        return new BookingReportDto(business.Currency, start, end, bookings.Count, completed,
            bookings.Count(b => b.Status == BookingStatus.Cancelled), noShows, Rate(noShows, completed),
            paymentRows.Sum(p => Signed(p.Type, p.AmountMinor)), netDeposits, byService, byStaff, days);
    }

    private async Task<long> RevenueAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.BookingPayments.Where(p => p.CreatedAt >= from && p.CreatedAt < to)
            .Select(p => new { p.AmountMinor, p.Type }).ToListAsync(ct);
        return rows.Sum(p => p.Type == BookingPaymentType.Refund ? -p.AmountMinor : p.AmountMinor);
    }

    private static double Rate(int noShows, int completed) =>
        noShows + completed == 0 ? 0 : Math.Round(noShows / (double)(noShows + completed), 4);
}
