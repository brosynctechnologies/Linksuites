using Saas.Domain.Common;

namespace Saas.Domain.Bookings;

/// <summary>The business's public booking profile and booking rules.</summary>
[Audited]
public class BookingBusiness : TenantEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public BusinessCategory Category { get; set; } = BusinessCategory.Salon;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    /// <summary>International digits for wa.me links.</summary>
    public string? WhatsappNumber { get; set; }
    public string? Email { get; set; }
    public string Timezone { get; set; } = "Africa/Lagos";
    public string Currency { get; set; } = "NGN";
    public string BrandColor { get; set; } = "#C2255C";
    public string? Policies { get; set; }
    public int CancellationHours { get; set; } = 24;
    public int SlotIntervalMinutes { get; set; } = 15;
    public int MinNoticeMinutes { get; set; } = 60;
    public int MaxDaysAhead { get; set; } = 60;
    public bool BookingEnabled { get; set; } = true;
    /// <summary>The business's own Paystack key, encrypted; deposits go straight to them.</summary>
    public string? PaystackSecretKeyProtected { get; set; }
}

public enum BusinessCategory { Salon, Barber, Spa, MakeUp, Photography, Clinic, Fitness, Other }

/// <summary>Opening hours for one weekday (0 = Sunday).</summary>
public class BusinessHours : TenantEntity
{
    public int Weekday { get; set; }
    public TimeOnly Open { get; set; } = new(9, 0);
    public TimeOnly Close { get; set; } = new(18, 0);
    public bool Closed { get; set; }
}

[Audited]
public class BookableService : TenantEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }
    public long PriceMinor { get; set; }
    public DepositType DepositType { get; set; }
    /// <summary>Fixed amount in minor units, or a percentage (1-100).</summary>
    public long DepositValue { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
    /// <summary>Staff who can do this service.</summary>
    public List<Guid> StaffIds { get; set; } = [];

    public long DepositMinor => DepositType switch
    {
        DepositType.Fixed => Math.Min(DepositValue, PriceMinor),
        DepositType.Percent => PriceMinor * Math.Clamp(DepositValue, 0, 100) / 100,
        _ => 0,
    };
}

public enum DepositType { None, Fixed, Percent }

/// <summary>One stretch of working time on a weekday. Several per day; the gaps are breaks.</summary>
public class ScheduleInterval
{
    public int Weekday { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

[Audited]
public class StaffMember : TenantEntity
{
    public required string DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? Email { get; set; }
    public string? PhotoUrl { get; set; }
    public string Color { get; set; } = "#7C5CBF";
    public bool IsBookable { get; set; } = true;
    public int Position { get; set; }
    /// <summary>Links a team member's login, so "My calendar" can show their bookings.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Secret for the private iCal feed.</summary>
    public required string FeedToken { get; set; }
    public List<ScheduleInterval> Schedule { get; set; } = [];
}

public class TimeOff : TenantEntity
{
    public Guid StaffId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Reason { get; set; }
}

public class BookingCustomer : TenantEntity
{
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public int NoShowCount { get; set; }
}

[Audited]
public class Booking : TenantEntity
{
    public required string Code { get; set; }
    public Guid CustomerId { get; set; }
    public BookingCustomer? Customer { get; set; }
    public Guid StaffId { get; set; }
    public Guid ServiceId { get; set; }
    public required string ServiceName { get; set; }
    public int DurationMinutes { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    /// <summary>Start minus the buffer before; with <see cref="BlockedTo"/> this is the range the database keeps exclusive per staff member.</summary>
    public DateTimeOffset BlockedFrom { get; set; }
    public DateTimeOffset BlockedTo { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public long PriceMinor { get; set; }
    public long DepositMinor { get; set; }
    /// <summary>Deposits and balances received, minus refunds.</summary>
    public long PaidMinor { get; set; }
    public BookingSource Source { get; set; }
    public string? Notes { get; set; }
    /// <summary>Secret for the customer's manage-booking link.</summary>
    public required string ManageToken { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }
    public int? Rating { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public List<BookingPayment> Payments { get; set; } = [];
    public List<BookingStatusHistory> History { get; set; } = [];
    public List<BookingReminder> Reminders { get; set; } = [];

    /// <summary>Statuses that hold the time slot (the database exclusion constraint uses the same list).</summary>
    public static readonly BookingStatus[] Holding = [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Completed, BookingStatus.NoShow];
}

public enum BookingStatus { Pending, Confirmed, Completed, Cancelled, NoShow, Expired }
public enum BookingSource { Online, WalkIn, Phone }

public class BookingPayment : TenantEntity
{
    public Guid BookingId { get; set; }
    /// <summary>Always positive; refunds have <see cref="BookingPaymentType.Refund"/>.</summary>
    public long AmountMinor { get; set; }
    public BookingPaymentType Type { get; set; }
    public BookingPaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    /// <summary>Paystack reference for online payments; unique so a charge is recorded once.</summary>
    public string? ProviderRef { get; set; }
}

public enum BookingPaymentType { Deposit, Balance, Refund }
public enum BookingPaymentMethod { Online, Cash, Transfer, Card }

public class BookingStatusHistory : TenantEntity
{
    public Guid BookingId { get; set; }
    public BookingStatus Status { get; set; }
    public string? Note { get; set; }
    public Guid? ChangedBy { get; set; }
}

public class BookingReminder : TenantEntity
{
    public Guid BookingId { get; set; }
    public ReminderChannel Channel { get; set; }
    public DateTimeOffset SendAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Scheduled;
}

public enum ReminderChannel { Email, WhatsApp, Sms }
public enum ReminderStatus { Scheduled, Sent, Cancelled, Skipped }
