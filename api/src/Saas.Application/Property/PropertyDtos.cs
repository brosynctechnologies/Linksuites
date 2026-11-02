using FluentValidation;
using Saas.Application.Common;
using Saas.Domain.Property;

namespace Saas.Application.Property;

// Studio: listings

public sealed record CostInput(CostKind Kind, string Label, long AmountMinor);
public sealed record ShortLetInput(long CleaningFeeMinor, int MinNights, int MaxGuests, string? CheckInTime, string? CheckOutTime, string? HouseRules);

public sealed record SaveListingRequest(
    string Title, string? Description, ListingPurpose Purpose, Guid PropertyTypeId, Guid LocationId, string? Address, double Lat, double Lng,
    long PriceMinor, PricePeriod PricePeriod, int Bedrooms, int Bathrooms, int Toilets, int? SizeSqm, bool Furnished, string? TourUrl, string? VideoUrl,
    IReadOnlyList<CostInput>? Costs, IReadOnlyList<Guid>? AmenityIds, ShortLetInput? ShortLet, Guid? AgentProfileId = null);

public sealed record SetListingStatusRequest(ListingStatus Status);

public sealed record CostDto(CostKind Kind, string Label, long AmountMinor);
public sealed record ShortLetDto(long CleaningFeeMinor, int MinNights, int MaxGuests, string? CheckInTime, string? CheckOutTime, string? HouseRules);
public sealed record ListingMediaDto(Guid Id, string FileName, string? ThumbUrl, string? CardUrl, string? FullUrl, MediaStatus Status, int Position,
    string? Error, Guid? DuplicateOfListingId, string? DuplicateOfAgency);

public sealed record StudioListingDto(
    Guid Id, string Title, string Slug, string Description, ListingPurpose Purpose, Guid PropertyTypeId, Guid LocationId, string Address,
    double Lat, double Lng, long PriceMinor, string Currency, PricePeriod PricePeriod, int Bedrooms, int Bathrooms, int Toilets, int? SizeSqm,
    bool Furnished, string? TourUrl, string? VideoUrl, ListingStatus Status, string? ModerationNote, DateTimeOffset? PublishedAt,
    DateTimeOffset? ExpiresAt, DateTimeOffset? FeaturedUntil, DateTimeOffset? TopUntil, int ViewsCount, IReadOnlyList<CostDto> Costs,
    IReadOnlyList<Guid> AmenityIds, ShortLetDto? ShortLet, long MoveInTotalMinor, Guid AgentProfileId, string PublicUrl,
    IReadOnlyList<ListingMediaDto> Media, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record StudioListingSummaryDto(Guid Id, string Title, string Slug, ListingPurpose Purpose, ListingStatus Status, long PriceMinor,
    string Currency, PricePeriod PricePeriod, string AreaName, string? CoverUrl, int ViewsCount, DateTimeOffset? ExpiresAt,
    DateTimeOffset? FeaturedUntil, DateTimeOffset? TopUntil, DateTimeOffset UpdatedAt);

// Studio: profiles

public sealed record AgencyProfileDto(Guid Id, string Name, string Slug, AgencyKind Kind, string? LogoUrl, string? Phone, string? Email,
    string? Address, string? Bio, bool Verified, DateTimeOffset? VerifiedAt, string PublicUrl);
public sealed record UpdateAgencyRequest(string Name, string Slug, AgencyKind Kind, string? Phone, string? Email, string? Address, string? Bio);

public sealed record AgentProfileDto(Guid Id, Guid UserId, string DisplayName, string Slug, string? Bio, string? Phone, string? WhatsApp,
    string? LicenceNo, string? PhotoUrl, bool Verified, double RatingAverage, int RatingCount);
public sealed record UpdateAgentRequest(string DisplayName, string? Bio, string? Phone, string? WhatsApp, string? LicenceNo);

// Validators

public sealed class SaveListingRequestValidator : AbstractValidator<SaveListingRequest>
{
    public SaveListingRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.Lat).InclusiveBetween(4, 14).WithMessage("Drop the pin on the property's location in Nigeria.");
        RuleFor(x => x.Lng).InclusiveBetween(2, 15).WithMessage("Drop the pin on the property's location in Nigeria.");
        RuleFor(x => x.PriceMinor).GreaterThan(0).WithMessage("Enter the price.");
        RuleFor(x => x.Bedrooms).InclusiveBetween(0, 50);
        RuleFor(x => x.Bathrooms).InclusiveBetween(0, 50);
        RuleFor(x => x.Toilets).InclusiveBetween(0, 50);
        RuleFor(x => x.SizeSqm).InclusiveBetween(1, 1_000_000).When(x => x.SizeSqm is not null);
        RuleFor(x => x.TourUrl).Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.TourUrl)).WithMessage("Use a full https:// link.");
        RuleFor(x => x.VideoUrl).Must(BeHttpUrl).When(x => !string.IsNullOrWhiteSpace(x.VideoUrl)).WithMessage("Use a full https:// link.");
        RuleFor(x => x.Costs).Must(c => c is null || c.Count <= 10).WithMessage("Add up to 10 costs.");
        RuleForEach(x => x.Costs).ChildRules(c =>
        {
            c.RuleFor(x => x.Label).NotEmpty().MaximumLength(60);
            c.RuleFor(x => x.Label).Must(l => PropertyRules.ValidateCostLabel(l ?? "") is null)
                .WithMessage(x => PropertyRules.ValidateCostLabel(x.Label ?? "")!);
            c.RuleFor(x => x.AmountMinor).GreaterThanOrEqualTo(0);
        });
        RuleFor(x => x.AmenityIds).Must(a => a is null || a.Count <= 30);
        RuleFor(x => x.PricePeriod).Must((r, p) => r.Purpose switch
        {
            ListingPurpose.Rent => p is PricePeriod.Year or PricePeriod.Month,
            ListingPurpose.Sale => p == PricePeriod.Total,
            _ => p == PricePeriod.Night,
        }).WithMessage("Rent is per year or month, a sale is a total price and a short-let is per night.");
        RuleFor(x => x.ShortLet).NotNull().When(x => x.Purpose == ListingPurpose.ShortLet).WithMessage("Add the short-let details.");
        RuleFor(x => x.ShortLet!).ChildRules(s =>
        {
            s.RuleFor(x => x.CleaningFeeMinor).GreaterThanOrEqualTo(0);
            s.RuleFor(x => x.MinNights).InclusiveBetween(1, 90);
            s.RuleFor(x => x.MaxGuests).InclusiveBetween(1, 50);
            s.RuleFor(x => x.CheckInTime).Must(BeTime).When(x => x.CheckInTime is not null).WithMessage("Use a time like 14:00.");
            s.RuleFor(x => x.CheckOutTime).Must(BeTime).When(x => x.CheckOutTime is not null).WithMessage("Use a time like 12:00.");
            s.RuleFor(x => x.HouseRules).MaximumLength(2000);
        }).When(x => x.ShortLet is not null);
    }

    private static bool BeHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    private static bool BeTime(string? t) => TimeOnly.TryParseExact(t, "HH:mm", out _);
}

public sealed class UpdateAgencyRequestValidator : AbstractValidator<UpdateAgencyRequest>
{
    public UpdateAgencyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Slug).NotEmpty().Must(Slugs.IsWellFormed).WithMessage("Use 3-60 lowercase letters, numbers and dashes (some words are reserved).");
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.Bio).MaximumLength(2000);
    }
}

public sealed class UpdateAgentRequestValidator : AbstractValidator<UpdateAgentRequest>
{
    public UpdateAgentRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Bio).MaximumLength(2000);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.WhatsApp).MaximumLength(30);
        RuleFor(x => x.LicenceNo).MaximumLength(40);
    }
}
