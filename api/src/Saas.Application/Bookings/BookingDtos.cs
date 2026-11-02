using FluentValidation;
using Saas.Application.Common;
using Saas.Domain.Bookings;

namespace Saas.Application.Bookings;

public sealed record BookingBusinessDto(
    string Name, string Slug, BusinessCategory Category, string? Description, string? LogoUrl, string? CoverUrl, string? Address,
    string? Phone, string? WhatsappNumber, string? Email, string Timezone, string Currency, string Country, string BrandColor,
    string? Policies, int CancellationHours, int SlotIntervalMinutes, int MinNoticeMinutes, int MaxDaysAhead, bool BookingEnabled,
    bool PaystackConnected, string BookingUrl, bool DepositsEnabled, bool DepositsInPlan);

public sealed record UpdateBookingBusinessRequest(
    string Name, string Slug, BusinessCategory Category, string? Description, string? Address, string? Phone, string? WhatsappNumber,
    string? Email, string Timezone, string BrandColor, string? Policies, int CancellationHours, int SlotIntervalMinutes,
    int MinNoticeMinutes, int MaxDaysAhead, bool BookingEnabled);

public sealed record HoursDto(int Weekday, string Open, string Close, bool Closed);
public sealed record IntervalDto(int Weekday, string Start, string End);

public sealed record SaveServiceRequest(
    string Name, string? Description, int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes, long PriceMinor,
    DepositType DepositType, long DepositValue, bool IsActive, int Position, List<Guid>? StaffIds);

public sealed record ServiceDto(
    Guid Id, string Name, string? Description, int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes, long PriceMinor,
    DepositType DepositType, long DepositValue, bool IsActive, int Position, IReadOnlyList<Guid> StaffIds, long DepositMinor);

public sealed record SaveStaffRequest(
    string DisplayName, string? Bio, string? Email, string? Color, bool IsBookable, int Position, List<Guid>? ServiceIds, Guid? UserId);

public sealed record StaffDto(
    Guid Id, string DisplayName, string? Bio, string? Email, string Color, bool IsBookable, int Position, IReadOnlyList<Guid> ServiceIds,
    Guid? UserId, string? PhotoUrl, IReadOnlyList<IntervalDto> Schedule, string CalendarFeedUrl);

public sealed record SaveTimeOffRequest(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);
public sealed record TimeOffDto(Guid Id, Guid StaffId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);

public sealed record SlotDto(DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> StaffIds);
public sealed record AvailabilityDto(DateOnly Date, string Timezone, IReadOnlyList<SlotDto> Slots);
public sealed record DayAvailabilityDto(DateOnly Date, bool Available);

public sealed record CustomerInput(string Name, string Phone, string? Email);

public sealed record CreateBookingRequest(
    Guid ServiceId, Guid? StaffId, DateTimeOffset StartsAt, CustomerInput Customer, BookingSource Source, string? Notes, bool Force);

public sealed record RescheduleRequest(DateTimeOffset? StartsAt, Guid? StaffId, string? Notes);
public sealed record ChangeStatusRequest(BookingStatus Status, string? Note);
public sealed record RecordBookingPaymentRequest(long AmountMinor, BookingPaymentType Type, BookingPaymentMethod Method, string? Reference);
public sealed record RefundRequest(long AmountMinor);

public sealed record BookingSummaryDto(
    Guid Id, string Code, DateTimeOffset StartsAt, DateTimeOffset EndsAt, BookingStatus Status, string CustomerName, string CustomerPhone,
    string ServiceName, Guid StaffId, string StaffName, string StaffColor, long PriceMinor, long DepositMinor, long PaidMinor,
    BookingSource Source);

public sealed record BookingCustomerRef(Guid Id, string Name, string Phone, string? Email, int NoShowCount);
public sealed record BookingPaymentDto(Guid Id, long AmountMinor, BookingPaymentType Type, BookingPaymentMethod Method, string? Reference, bool Online, DateTimeOffset CreatedAt);
public sealed record BookingHistoryDto(BookingStatus Status, string? Note, DateTimeOffset CreatedAt);
public sealed record ReminderDto(ReminderChannel Channel, DateTimeOffset SendAt, DateTimeOffset? SentAt, ReminderStatus Status);
public sealed record ReviewDto(int Rating, string? Comment, DateTimeOffset CreatedAt);

public sealed record BookingDto(
    Guid Id, string Code, DateTimeOffset StartsAt, DateTimeOffset EndsAt, BookingStatus Status, string CustomerName, string CustomerPhone,
    string ServiceName, Guid StaffId, string StaffName, string StaffColor, long PriceMinor, long DepositMinor, long PaidMinor,
    BookingSource Source, BookingCustomerRef Customer, Guid ServiceId, string? Notes, DateTimeOffset? HoldExpiresAt, string ManageUrl,
    string WhatsappReminderUrl, IReadOnlyList<BookingPaymentDto> Payments, IReadOnlyList<BookingHistoryDto> History,
    IReadOnlyList<ReminderDto> Reminders, ReviewDto? Review, long RefundableMinor);

public sealed record CalendarStaffDto(Guid Id, string Name, string Color);
public sealed record CalendarDto(string Timezone, IReadOnlyList<CalendarStaffDto> Staff, IReadOnlyList<BookingSummaryDto> Bookings,
    IReadOnlyList<TimeOffDto> TimeOff, IReadOnlyList<HoursDto> Hours);

public sealed record BookingCustomerDto(
    Guid Id, string Name, string Phone, string? Email, string? Notes, int BookingsCount, int CompletedCount, int NoShowCount,
    long TotalSpentMinor, DateTimeOffset? LastVisitAt);
public sealed record BookingCustomerDetailDto(
    Guid Id, string Name, string Phone, string? Email, string? Notes, int BookingsCount, int CompletedCount, int NoShowCount,
    long TotalSpentMinor, DateTimeOffset? LastVisitAt, IReadOnlyList<BookingSummaryDto> Bookings);
public sealed record UpdateCustomerRequest(string Name, string Phone, string? Email, string? Notes);

public sealed record BookingDashboardDto(
    string Timezone, string Currency, IReadOnlyList<BookingSummaryDto> Today, int UpcomingCount, int PendingCount,
    long RevenueThisMonthMinor, int BookingsThisMonth, long BookingLimit, double NoShowRate);

public sealed record NamedStat(string Name, int Bookings, long RevenueMinor);
public sealed record StaffStat(string Name, int Bookings, long RevenueMinor, int NoShows);
public sealed record DayStat(DateOnly Date, int Bookings, long RevenueMinor);
public sealed record BookingReportDto(
    string Currency, DateOnly From, DateOnly To, int Bookings, int Completed, int Cancelled, int NoShows, double NoShowRate,
    long RevenueMinor, long DepositsMinor, IReadOnlyList<NamedStat> ByService, IReadOnlyList<StaffStat> ByStaff, IReadOnlyList<DayStat> ByDay);

// Public

public sealed record PublicServiceDto(Guid Id, string Name, string? Description, int DurationMinutes, long PriceMinor, long DepositMinor, IReadOnlyList<Guid> StaffIds);
public sealed record PublicStaffDto(Guid Id, string DisplayName, string? PhotoUrl, string? Bio, IReadOnlyList<Guid> ServiceIds);
public sealed record RatingDto(double Average, int Count);

public sealed record PublicBookingPageDto(
    string Name, string Slug, BusinessCategory Category, string? Description, string? LogoUrl, string? CoverUrl, string? Address,
    string? Phone, string? WhatsappNumber, string Timezone, string Currency, string BrandColor, string? Policies, int CancellationHours,
    int MaxDaysAhead, bool BookingEnabled, bool ShowBranding, RatingDto? Rating, IReadOnlyList<PublicServiceDto> Services,
    IReadOnlyList<PublicStaffDto> Staff, IReadOnlyList<HoursDto> Hours);

public sealed record PublicBookRequest(Guid ServiceId, Guid? StaffId, DateTimeOffset StartsAt, CustomerInput Customer, string? Notes);

public sealed record PublicBusinessRef(string Name, string Slug, string? Address, string? Phone, string? WhatsappNumber, string? LogoUrl, string BrandColor, string? Policies);
public sealed record PublicServiceRef(Guid Id, string Name, int DurationMinutes);
public sealed record PublicStaffRef(Guid Id, string DisplayName, string? PhotoUrl);

public sealed record PublicBookingDto(
    string Code, BookingStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Timezone, PublicServiceRef Service,
    PublicStaffRef Staff, string CustomerName, long PriceMinor, long DepositMinor, long PaidMinor, long BalanceMinor, string Currency,
    DateTimeOffset? HoldExpiresAt, bool CanCancel, bool CanReschedule, DateTimeOffset CancelDeadline, bool CanReview, ReviewDto? Review,
    PublicBusinessRef Business, long DepositDueMinor);

public sealed record PublicBookResult(PublicBookingDto Booking, string? AuthorizationUrl, string ManageUrl);
public sealed record PublicRescheduleRequest(DateTimeOffset StartsAt, Guid? StaffId);
public sealed record ReviewRequest(int Rating, string? Comment);
public sealed record BookingPayRequest(string? Email);

// Validators

public sealed class UpdateBookingBusinessRequestValidator : AbstractValidator<UpdateBookingBusinessRequest>
{
    public UpdateBookingBusinessRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Slug).NotEmpty().Must(Slugs.IsWellFormed).WithMessage("Use 3-60 lowercase letters, numbers and dashes (some words are reserved).");
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.WhatsappNumber).MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Timezone).Must(z => TimeZoneInfo.TryFindSystemTimeZoneById(z, out _)).WithMessage("Unknown time zone.");
        RuleFor(x => x.BrandColor).Matches("^#[0-9a-fA-F]{6}$").WithMessage("Use a hex colour such as #C2255C.");
        RuleFor(x => x.Policies).MaximumLength(2000);
        RuleFor(x => x.CancellationHours).InclusiveBetween(0, 168);
        RuleFor(x => x.SlotIntervalMinutes).InclusiveBetween(5, 60);
        RuleFor(x => x.MinNoticeMinutes).InclusiveBetween(0, 10080);
        RuleFor(x => x.MaxDaysAhead).InclusiveBetween(1, 365);
    }
}

public sealed class SaveServiceRequestValidator : AbstractValidator<SaveServiceRequest>
{
    public SaveServiceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 12 * 60);
        RuleFor(x => x.BufferBeforeMinutes).InclusiveBetween(0, 240);
        RuleFor(x => x.BufferAfterMinutes).InclusiveBetween(0, 240);
        RuleFor(x => x.PriceMinor).InclusiveBetween(0, 100_000_000_000);
        RuleFor(x => x.DepositType).IsInEnum();
        RuleFor(x => x.DepositValue).InclusiveBetween(1, 100).When(x => x.DepositType == DepositType.Percent)
            .WithMessage("A percentage deposit must be between 1 and 100.");
        RuleFor(x => x.DepositValue).GreaterThan(0).When(x => x.DepositType == DepositType.Fixed);
    }
}

public sealed class SaveStaffRequestValidator : AbstractValidator<SaveStaffRequest>
{
    public SaveStaffRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Bio).MaximumLength(500);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Color).Matches("^#[0-9a-fA-F]{6}$").When(x => x.Color is not null);
    }
}

public sealed class SaveTimeOffRequestValidator : AbstractValidator<SaveTimeOffRequest>
{
    public SaveTimeOffRequestValidator()
    {
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt).WithMessage("Time off must end after it starts.");
        RuleFor(x => x.Reason).MaximumLength(200);
    }
}

public sealed class CustomerInputValidator : AbstractValidator<CustomerInput>
{
    public CustomerInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Customer).NotNull().SetValidator(new CustomerInputValidator());
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class PublicBookRequestValidator : AbstractValidator<PublicBookRequest>
{
    public PublicBookRequestValidator()
    {
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Customer).NotNull().SetValidator(new CustomerInputValidator());
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class RecordBookingPaymentRequestValidator : AbstractValidator<RecordBookingPaymentRequest>
{
    public RecordBookingPaymentRequestValidator()
    {
        RuleFor(x => x.AmountMinor).GreaterThan(0);
        RuleFor(x => x.Type).IsInEnum().NotEqual(BookingPaymentType.Refund).WithMessage("Use refund for money going back.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

public sealed class ReviewRequestValidator : AbstractValidator<ReviewRequest>
{
    public ReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
