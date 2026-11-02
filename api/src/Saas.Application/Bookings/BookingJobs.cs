using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Bookings;

namespace Saas.Application.Bookings;

/// <summary>
/// Background work for one bookings tenant (the caller sets the tenant): release unpaid holds and send due
/// reminders. Both are idempotent, so overlapping runs are harmless.
/// </summary>
public sealed class BookingJobs(IAppDbContext db, BookingCore core, PlanService plans, NotificationQueue notifications)
{
    public async Task<(int Expired, int Reminded)> RunAsync(CancellationToken ct)
    {
        var now = core.Now;
        var expired = await db.Bookings.Where(b => b.Status == BookingStatus.Pending && b.HoldExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Expired), ct);

        var due = await db.BookingReminders.Where(r => r.Status == ReminderStatus.Scheduled && r.SendAt <= now).ToListAsync(ct);
        if (due.Count == 0) return (expired, 0);

        var business = await core.BusinessAsync(ct);
        var allowed = await plans.HasFeatureAsync(LimitKeys.BookingReminders, ct);
        var bookingIds = due.Select(r => r.BookingId).Distinct().ToList();
        var bookings = await db.Bookings.Include(b => b.Customer).Where(b => bookingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var staffNames = await db.StaffMembers.ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        var zone = AvailabilityEngine.Zone(business.Timezone);
        var sent = 0;

        foreach (var reminder in due)
        {
            var booking = bookings.GetValueOrDefault(reminder.BookingId);
            // Skip anything no longer worth reminding about, including reminders that are hours late (server was down).
            if (!allowed || booking is null || booking.Status != BookingStatus.Confirmed || booking.StartsAt <= now
                || string.IsNullOrWhiteSpace(booking.Customer?.Email) || now - reminder.SendAt > TimeSpan.FromHours(3))
            {
                reminder.Status = ReminderStatus.Skipped;
                continue;
            }
            notifications.Email(booking.Customer.Email, "booking.reminder", new()
            {
                ["business"] = business.Name,
                ["customer"] = booking.Customer.Name,
                ["service"] = booking.ServiceName,
                ["staff"] = staffNames.GetValueOrDefault(booking.StaffId, ""),
                ["when"] = TimeZoneInfo.ConvertTime(booking.StartsAt, zone).ToString("dddd d MMMM 'at' HH:mm"),
                ["address"] = business.Address ?? "",
                ["link"] = core.ManageUrl(booking.ManageToken),
            }, booking.TenantId);
            reminder.Status = ReminderStatus.Sent;
            reminder.SentAt = now;
            sent++;
        }
        await db.SaveChangesAsync(ct);
        return (expired, sent);
    }
}
