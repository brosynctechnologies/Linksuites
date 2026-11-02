using Saas.Application.Property;
using Saas.Domain.Property;

namespace Saas.UnitTests;

public sealed class PropertyRulesTests
{
    private static readonly TimeZoneInfo Lagos = TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Quote_counts_nights()
    {
        var quote = PropertyRules.Quote(5_000_000, 1_000_000, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 13));
        Assert.Equal(new StayQuote(3, 15_000_000, 1_000_000, 16_000_000), quote);
    }

    [Fact]
    public void Commission_is_ten_percent() => Assert.Equal(1_600_000, PropertyRules.Commission(16_000_000));

    private static Stay StayFrom(DateOnly checkIn) => new()
    {
        Reference = "stay_test", CheckIn = checkIn, CheckOut = checkIn.AddDays(3), Nights = 3, NightlyMinor = 5_000_000,
        CleaningMinor = 1_000_000, TotalMinor = 16_000_000, Status = StayStatus.Confirmed,
    };

    [Fact]
    public void Guest_refund_is_full_more_than_48_hours_before() =>
        Assert.Equal(16_000_000, PropertyRules.GuestRefund(StayFrom(new DateOnly(2026, 10, 15)), Now, Lagos));

    [Fact]
    public void Guest_refund_keeps_one_night_inside_48_hours() =>
        Assert.Equal(11_000_000, PropertyRules.GuestRefund(StayFrom(new DateOnly(2026, 10, 13)), Now, Lagos));

    [Fact]
    public void Host_cancel_refunds_everything() => Assert.Equal(16_000_000, PropertyRules.HostCancelRefund(StayFrom(new DateOnly(2026, 10, 13))));

    [Fact]
    public void Expiry_and_warning_dates()
    {
        var expires = PropertyRules.ExpiresAt(Now, 30);
        Assert.Equal(Now.AddDays(30), expires);
        Assert.Equal(Now.AddDays(25), PropertyRules.WarnAt(expires));
    }

    [Fact]
    public void Rank_puts_top_then_featured_then_verified_then_newest()
    {
        var newestPlain = new ListingRankInput(null, null, false, Now);
        var verifiedOld = new ListingRankInput(null, null, true, Now.AddDays(-20));
        var featured = new ListingRankInput(null, Now.AddDays(3), false, Now.AddDays(-30));
        var top = new ListingRankInput(Now.AddDays(1), null, false, Now.AddDays(-40));
        var expiredTop = new ListingRankInput(Now.AddDays(-1), Now.AddDays(-1), false, Now.AddDays(-1));

        var ordered = new[] { expiredTop, newestPlain, featured, verifiedOld, top }.OrderBy(x => PropertyRules.RankKey(x, Now)).ToList();

        Assert.Equal([top, featured, verifiedOld, newestPlain, expiredTop], ordered);
    }

    [Fact]
    public void Matches_respects_subtree_price_beds_and_amenities()
    {
        var lekki = Guid.NewGuid();
        var lagosSubtree = new HashSet<Guid> { lekki, Guid.NewGuid() };
        var listing = new ListingMatchInput(ListingPurpose.Rent, lekki, "apartment", 3_500_000_00, 3, new HashSet<string> { "pool", "parking" }, true, 6.44, 3.47);
        var filter = new ListingFilter(ListingPurpose.Rent, "lagos", "apartment", MinPrice: 3_000_000_00, MaxPrice: 4_000_000_00, MinBeds: 2, Amenities: ["pool"]);

        Assert.True(PropertyRules.Matches(filter, listing, lagosSubtree));
        Assert.False(PropertyRules.Matches(filter with { Purpose = ListingPurpose.Sale }, listing, lagosSubtree));
        Assert.False(PropertyRules.Matches(filter, listing, new HashSet<Guid> { Guid.NewGuid() }));
        Assert.False(PropertyRules.Matches(filter with { MaxPrice = 3_000_000_00 }, listing, lagosSubtree));
        Assert.False(PropertyRules.Matches(filter with { MinBeds = 4 }, listing, lagosSubtree));
        Assert.False(PropertyRules.Matches(filter with { Amenities = ["gym"] }, listing, lagosSubtree));
        Assert.False(PropertyRules.Matches(filter with { VerifiedOnly = true }, listing with { Verified = false }, lagosSubtree));
        Assert.False(PropertyRules.Matches(filter with { Bbox = [3.0, 6.0, 3.4, 6.5] }, listing, lagosSubtree));
        Assert.True(PropertyRules.Matches(new ListingFilter(), listing, null));
    }

    [Theory]
    [InlineData("Inspection fee")]
    [InlineData("INSPECTION")]
    [InlineData("Pre-inspection charge")]
    public void Cost_label_mentioning_inspection_is_rejected(string label) => Assert.NotNull(PropertyRules.ValidateCostLabel(label));

    [Fact]
    public void Ordinary_cost_labels_are_allowed() => Assert.Null(PropertyRules.ValidateCostLabel("Agency fee"));

    [Fact]
    public void Move_in_total_adds_every_cost() => Assert.Equal(4_500_000_00, PropertyRules.MoveInTotal(3_500_000_00, [350_000_00, 350_000_00, 300_000_00]));
}

public sealed class ImageHashTests
{
    private static byte[] Gradient(bool descending)
    {
        var pixels = new byte[72];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 9; x++)
            pixels[y * 9 + x] = (byte)(descending ? 250 - x * 25 : 10 + x * 25 + y);
        return pixels;
    }

    [Fact]
    public void DHash_of_same_gradient_is_identical_and_inverted_is_far()
    {
        Assert.Equal(0, ImageHash.Distance(ImageHash.DHash(Gradient(false)), ImageHash.DHash(Gradient(false))));
        Assert.True(ImageHash.Distance(ImageHash.DHash(Gradient(false)), ImageHash.DHash(Gradient(true))) >= 32);
    }

    [Fact]
    public void Distance_counts_bits() => Assert.Equal(3, ImageHash.Distance(0b1011, 0));

    [Fact]
    public void DHash_needs_nine_by_eight_pixels() => Assert.Throws<ArgumentException>(() => ImageHash.DHash(new byte[10]));
}
