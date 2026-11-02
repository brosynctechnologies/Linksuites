using NetTopologySuite.Geometries;
using Saas.Domain.Common;
using Saas.Domain.Payouts;

namespace Saas.Domain.Property;

// Global reference data and seekers' own records (not tenant-owned)

public enum LocationKind { State, City, Area }

/// <summary>State, city or area. Areas are where listings live; searching a city includes all its areas.</summary>
public class Location : Entity
{
    public Guid? ParentId { get; set; }
    public LocationKind Kind { get; set; }
    public required string Name { get; set; }
    /// <summary>Globally unique, e.g. "lagos", "lekki-lagos".</summary>
    public required string Slug { get; set; }
    public Point? Centre { get; set; }
    public int Position { get; set; }
}

public class PropertyType : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public int Position { get; set; }
}

public class Amenity : Entity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public int Position { get; set; }
}

public class SavedListing : Entity
{
    public Guid UserId { get; set; }
    public Guid ListingId { get; set; }
}

public enum AlertFrequency { Off, Instant, Daily }

public class SavedSearch : Entity
{
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public required ListingFilter Filter { get; set; }
    public AlertFrequency Frequency { get; set; } = AlertFrequency.Daily;
    public DateTimeOffset? LastSentAt { get; set; }
}

/// <summary>Search filters, used by the search page and stored on saved searches.</summary>
public sealed record ListingFilter(
    ListingPurpose? Purpose = null, string? Location = null, string? Type = null, long? MinPrice = null, long? MaxPrice = null,
    int? MinBeds = null, string[]? Amenities = null, bool VerifiedOnly = false, double[]? Bbox = null, string? Sort = null);

// Agency side (tenant-owned)

public enum AgencyKind { Agency, Agent, Landlord }

/// <summary>The business behind the listings: an agency, a solo agent or a landlord. One per tenant.</summary>
[Audited]
public class AgencyProfile : TenantEntity
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public AgencyKind Kind { get; set; }
    public string? LogoUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Bio { get; set; }
    public string Currency { get; set; } = "NGN";
    public DateTimeOffset? VerifiedAt { get; set; }
    public PayoutAccount? PayoutAccount { get; set; }
    /// <summary>Confirmed short-let stays the host cancelled. Shown to moderators.</summary>
    public int HostCancellations { get; set; }
}

/// <summary>A team member who lists properties and takes enquiries.</summary>
[Audited]
public class AgentProfile : TenantEntity
{
    public Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public required string Slug { get; set; }
    public string? Bio { get; set; }
    public string? Phone { get; set; }
    public string? WhatsApp { get; set; }
    public string? LicenceNo { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }
}

public enum ListingPurpose { Rent, Sale, ShortLet }
public enum PricePeriod { Year, Month, Total, Night }
public enum ListingStatus { Draft, Pending, Active, Expired, Rented, Sold, Rejected, Hidden }

[Audited]
public class Listing : TenantEntity
{
    public Guid AgentProfileId { get; set; }
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = "";
    public ListingPurpose Purpose { get; set; }
    public Guid PropertyTypeId { get; set; }
    /// <summary>The area (a <see cref="Location"/> of kind Area).</summary>
    public Guid LocationId { get; set; }
    public string Address { get; set; } = "";
    public required Point Point { get; set; }
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "NGN";
    public PricePeriod PricePeriod { get; set; }
    public int Bedrooms { get; set; }
    public int Bathrooms { get; set; }
    public int Toilets { get; set; }
    public int? SizeSqm { get; set; }
    public bool Furnished { get; set; }
    public string? TourUrl { get; set; }
    public string? VideoUrl { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public string? ModerationNote { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>When the listing first went live; alerts fire once, at this moment.</summary>
    public DateTimeOffset? FirstPublishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastConfirmedAt { get; set; }
    public DateTimeOffset? ExpiryWarnedAt { get; set; }
    public DateTimeOffset? FeaturedUntil { get; set; }
    public DateTimeOffset? TopUntil { get; set; }
    public int ViewsCount { get; set; }

    // Short-let only
    public long CleaningFeeMinor { get; set; }
    public int MinNights { get; set; } = 1;
    public int MaxGuests { get; set; } = 2;
    public string? CheckInTime { get; set; }
    public string? CheckOutTime { get; set; }
    public string? HouseRules { get; set; }

    public List<ListingCost> Costs { get; set; } = [];
    public List<ListingAmenity> Amenities { get; set; } = [];
}

public enum CostKind { AgencyFee, LegalFee, Caution, ServiceCharge, Other }

public class ListingCost : TenantEntity
{
    public Guid ListingId { get; set; }
    public CostKind Kind { get; set; }
    public required string Label { get; set; }
    public long AmountMinor { get; set; }
    public int Position { get; set; }
}

public class ListingAmenity : TenantEntity
{
    public Guid ListingId { get; set; }
    public Guid AmenityId { get; set; }
}

public enum MediaStatus { Processing, Ready, Failed }

public class ListingMedia : TenantEntity
{
    public Guid ListingId { get; set; }
    public required string OriginalKey { get; set; }
    public string FileName { get; set; } = "";
    public string? ThumbUrl { get; set; }
    public string? CardUrl { get; set; }
    public string? FullUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Position { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Processing;
    public string? Error { get; set; }
    /// <summary>64-bit difference hash, stored signed for Postgres bigint.</summary>
    public long? Phash { get; set; }
    public Guid? DuplicateOfMediaId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
}

public enum EnquiryStatus { New, Replied, Closed }

public class Enquiry : TenantEntity
{
    public Guid ListingId { get; set; }
    public Guid AgentProfileId { get; set; }
    public Guid? UserId { get; set; }
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string Message { get; set; } = "";
    public EnquiryStatus Status { get; set; } = EnquiryStatus.New;
}

/// <summary>A weekly window when an agent shows properties, cut into slots.</summary>
public class InspectionSlot : TenantEntity
{
    public Guid AgentProfileId { get; set; }
    public DayOfWeek Weekday { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int SlotMinutes { get; set; } = 30;
}

public enum InspectionStatus { Requested, Confirmed, Done, Cancelled, NoShow }

public class Inspection : TenantEntity
{
    public Guid ListingId { get; set; }
    public Guid AgentProfileId { get; set; }
    public Guid SeekerUserId { get; set; }
    public string SeekerName { get; set; } = "";
    public string? SeekerPhone { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public InspectionStatus Status { get; set; } = InspectionStatus.Requested;
    public string? Notes { get; set; }
    public bool ReminderSent { get; set; }
}

/// <summary>Daily counters per listing, so stats never need a row per view.</summary>
public class ListingStatDay : TenantEntity
{
    public Guid ListingId { get; set; }
    public DateOnly Day { get; set; }
    public int Views { get; set; }
    public int WhatsAppClicks { get; set; }
    public int CallClicks { get; set; }
    public int Enquiries { get; set; }
}

public enum VerificationStatus { Pending, Approved, Rejected }

public class VerificationDocument
{
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileName { get; set; } = "";
}

[Audited]
public class VerificationRequest : TenantEntity
{
    public AgencyKind Kind { get; set; }
    public Guid SubmittedBy { get; set; }
    public List<VerificationDocument> Documents { get; set; } = [];
    public string? LicenceNo { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;
    public string? Note { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

public enum ReportReason { Fake, AlreadyTaken, WrongPrice, Duplicate, Scam, Other }
public enum ReportStatus { Open, Upheld, Dismissed }

public class ListingReport : TenantEntity
{
    public Guid ListingId { get; set; }
    public Guid? ReporterUserId { get; set; }
    /// <summary>The user id, or a hash of the IP for anonymous reports, so one person counts once.</summary>
    public required string ReporterKey { get; set; }
    public ReportReason Reason { get; set; }
    public string? Details { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum ReviewStatus { Published, Hidden }
public enum ReviewBasis { Enquiry, Inspection, Stay }

public class AgentReview : TenantEntity
{
    public Guid AgentProfileId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Published;
    public ReviewBasis Basis { get; set; }
    public int ReportCount { get; set; }
}

public enum BoostKind { Featured, Top }
public enum BoostStatus { Pending, Active, Expired }

public class Boost : TenantEntity
{
    public Guid ListingId { get; set; }
    public required string Code { get; set; }
    public BoostKind Kind { get; set; }
    public int Days { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "NGN";
    public required string Reference { get; set; }
    public BoostStatus Status { get; set; } = BoostStatus.Pending;
    public Guid? BuyerUserId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}

/// <summary>Dates a host closes for a short-let. <see cref="End"/> is exclusive.</summary>
public class StayBlock : TenantEntity
{
    public Guid ListingId { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public string? Note { get; set; }
}

public enum StayStatus { Pending, Confirmed, Completed, Cancelled, Expired }
public enum StayCanceller { Guest, Host }

/// <summary>A short-let booking, paid to the platform. Check-out is exclusive (the guest leaves that morning).</summary>
[Audited]
public class Stay : TenantEntity
{
    public Guid ListingId { get; set; }
    public Guid GuestUserId { get; set; }
    public string GuestName { get; set; } = "";
    public string? GuestPhone { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Guests { get; set; }
    public int Nights { get; set; }
    public long NightlyMinor { get; set; }
    public long CleaningMinor { get; set; }
    public long TotalMinor { get; set; }
    public long CommissionMinor { get; set; }
    public string Currency { get; set; } = "NGN";
    public StayStatus Status { get; set; } = StayStatus.Pending;
    public DateTimeOffset HoldExpiresAt { get; set; }
    public required string Reference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public StayCanceller? CancelledBy { get; set; }
    public long RefundMinor { get; set; }
}
