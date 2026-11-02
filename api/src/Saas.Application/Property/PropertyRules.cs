using Saas.Domain.Property;

namespace Saas.Application.Property;

public sealed record StayQuote(int Nights, long SubtotalMinor, long CleaningMinor, long TotalMinor);

public sealed record ListingRankInput(DateTimeOffset? TopUntil, DateTimeOffset? FeaturedUntil, bool Verified, DateTimeOffset PublishedAt);

public sealed record ListingMatchInput(ListingPurpose Purpose, Guid LocationId, string TypeSlug, long PriceMinor, int Bedrooms,
    IReadOnlySet<string> AmenitySlugs, bool Verified, double Lat, double Lng);

/// <summary>Pure pricing, refund, expiry, ranking and matching rules for the property product.</summary>
public static class PropertyRules
{
    public const int CommissionPercent = 10;
    public static readonly TimeSpan StayHold = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FreeCancellation = TimeSpan.FromHours(48);
    public static readonly TimeSpan HostPayoutDelay = TimeSpan.FromHours(24);
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(5);
    /// <summary>When a guest is treated as arriving, for the free-cancellation cut-off.</summary>
    public static readonly TimeOnly StandardCheckIn = new(14, 0);

    public static long MoveInTotal(long priceMinor, IEnumerable<long> costsMinor) => priceMinor + costsMinor.Sum();

    public static StayQuote Quote(long nightlyMinor, long cleaningMinor, DateOnly checkIn, DateOnly checkOut)
    {
        var nights = checkOut.DayNumber - checkIn.DayNumber;
        var subtotal = nightlyMinor * nights;
        return new StayQuote(nights, subtotal, cleaningMinor, subtotal + cleaningMinor);
    }

    public static long Commission(long totalMinor) => (long)Math.Round(totalMinor * CommissionPercent / 100m, MidpointRounding.AwayFromZero);

    /// <summary>When the guest is due to arrive, as an instant.</summary>
    public static DateTimeOffset ArrivalAt(DateOnly checkIn, TimeZoneInfo zone)
    {
        var local = checkIn.ToDateTime(StandardCheckIn);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Full refund more than 48 hours before arrival; after that the host keeps one night.</summary>
    public static long GuestRefund(Stay stay, DateTimeOffset now, TimeZoneInfo zone) =>
        ArrivalAt(stay.CheckIn, zone) - now > FreeCancellation ? stay.TotalMinor : Math.Max(0, stay.TotalMinor - stay.NightlyMinor);

    public static long HostCancelRefund(Stay stay) => stay.TotalMinor;

    public static DateTimeOffset ExpiresAt(DateTimeOffset from, int days) => from.AddDays(days);

    public static DateTimeOffset WarnAt(DateTimeOffset expiresAt) => expiresAt - ExpiryWarning;

    /// <summary>Ascending sort key: active top boost, then active featured, then verified agencies, then newest.</summary>
    public static (int Top, int Featured, int Verified, long Fresh) RankKey(ListingRankInput l, DateTimeOffset now) => (
        l.TopUntil > now ? 0 : 1,
        l.FeaturedUntil > now ? 0 : 1,
        l.Verified ? 0 : 1,
        -l.PublishedAt.UtcTicks);

    /// <summary>
    /// Whether a listing satisfies a saved search. <paramref name="locationSubtree"/> holds the ids of the filter's location
    /// and everything under it; null means the filter has no location.
    /// </summary>
    public static bool Matches(ListingFilter f, ListingMatchInput l, IReadOnlySet<Guid>? locationSubtree)
    {
        if (f.Purpose is { } purpose && purpose != l.Purpose) return false;
        if (locationSubtree is not null && !locationSubtree.Contains(l.LocationId)) return false;
        if (!string.IsNullOrEmpty(f.Type) && !string.Equals(f.Type, l.TypeSlug, StringComparison.OrdinalIgnoreCase)) return false;
        if (f.MinPrice is { } min && l.PriceMinor < min) return false;
        if (f.MaxPrice is { } max && l.PriceMinor > max) return false;
        if (f.MinBeds is { } beds && l.Bedrooms < beds) return false;
        if (f.Amenities is { Length: > 0 } wanted && !wanted.All(l.AmenitySlugs.Contains)) return false;
        if (f.VerifiedOnly && !l.Verified) return false;
        if (f.Bbox is [var w, var s, var e, var n] && (l.Lng < w || l.Lng > e || l.Lat < s || l.Lat > n)) return false;
        return true;
    }

    /// <summary>Inspection fees are the classic scam, so a cost may never be labelled as one.</summary>
    public static string? ValidateCostLabel(string label) =>
        label.Contains("inspection", StringComparison.OrdinalIgnoreCase)
            ? "Inspection fees aren't allowed. Seekers should never pay to view a property."
            : null;
}
