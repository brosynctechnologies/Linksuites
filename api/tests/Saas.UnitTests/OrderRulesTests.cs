using Saas.Application.Common;
using Saas.Application.Orders;
using Saas.Domain.Orders;

namespace Saas.UnitTests;

public class OrderPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static DiscountCode Code(DiscountType type, long value, long minOrder = 0, int? maxUses = null, int uses = 0, DateTimeOffset? expires = null) =>
        new() { Code = "SAVE", Type = type, Value = value, MinOrderMinor = minOrder, MaxUses = maxUses, Uses = uses, ExpiresAt = expires };

    [Fact]
    public void Percent_discount_rounds_down() =>
        Assert.Equal(333, OrderPricing.ApplyDiscount(Code(DiscountType.Percent, 10), "SAVE", 3_333, "NGN", Now).AmountMinor);

    [Fact]
    public void Fixed_discount_never_exceeds_subtotal() =>
        Assert.Equal(500, OrderPricing.ApplyDiscount(Code(DiscountType.Fixed, 10_000), "SAVE", 500, "NGN", Now).AmountMinor);

    [Theory]
    [InlineData("expired")]
    [InlineData("used-up")]
    [InlineData("min-order")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    public void Invalid_codes_give_no_discount_and_a_message(string reason)
    {
        var code = reason switch
        {
            "expired" => Code(DiscountType.Percent, 10, expires: Now.AddMinutes(-1)),
            "used-up" => Code(DiscountType.Percent, 10, maxUses: 3, uses: 3),
            "min-order" => Code(DiscountType.Percent, 10, minOrder: 1_000_000),
            "inactive" => Code(DiscountType.Percent, 10),
            _ => null,
        };
        if (reason == "inactive") code!.IsActive = false;

        var result = OrderPricing.ApplyDiscount(code, "SAVE", 50_000, "NGN", Now);

        Assert.Equal(0, result.AmountMinor);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void No_code_entered_is_not_an_error() =>
        Assert.Equal(DiscountResult.None, OrderPricing.ApplyDiscount(null, "  ", 50_000, "NGN", Now));

    [Fact]
    public void Total_applies_discount_to_items_only() =>
        Assert.Equal(150_000, OrderPricing.Total(subtotalMinor: 100_000, deliveryFeeMinor: 150_000, discountMinor: 200_000));
}

public class WhatsAppMessageTests
{
    [Fact]
    public void Builds_the_message_from_the_plan_example()
    {
        var order = new Order
        {
            Number = 1042, Code = "AB12CD", CustomerName = "Ada", Phone = "2348030000000", Currency = "NGN",
            Fulfilment = Fulfilment.Delivery, DeliveryZoneName = "Lekki", DeliveryFeeMinor = 150_000,
            Address = "12 Admiralty Way", TotalMinor = 900_000,
            Items =
            [
                new OrderItem { NameSnapshot = "Jollof rice", VariantSnapshot = "large", Quantity = 2, LineTotalMinor = 600_000 },
                new OrderItem { NameSnapshot = "Chapman", Quantity = 1, LineTotalMinor = 150_000 },
            ],
        };

        var message = WhatsAppMessage.Build(order, "https://yourapp.com/o/AB12CD");

        Assert.Equal(
            """
            New order #1042
            2 x Jollof rice (large) - ₦6,000
            1 x Chapman - ₦1,500
            Delivery (Lekki) - ₦1,500
            Total: ₦9,000
            Name: Ada, 2348030000000
            Address: 12 Admiralty Way
            Order link: https://yourapp.com/o/AB12CD
            """.ReplaceLineEndings("\n"),
            message.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Link_url_encodes_the_text() =>
        Assert.Equal("https://wa.me/2348031234567?text=Hi%20%26%20%231", WhatsAppMessage.Link("2348031234567", "Hi & #1"));
}

public class TextHelperTests
{
    [Theory]
    [InlineData("0803 123 4567", "NG", "2348031234567")]
    [InlineData("+234 803-123-4567", "NG", "2348031234567")]
    [InlineData("2348031234567", "NG", "2348031234567")]
    [InlineData("8031234567", "NG", "2348031234567")]
    [InlineData("024 123 4567", "GH", "233241234567")]
    [InlineData("(212) 555-1234", "US", "12125551234")]
    [InlineData("0044 7700 900123", "NG", "447700900123")]
    public void Normalizes_phone_numbers_for_wa_me(string input, string country, string expected) =>
        Assert.Equal(expected, PhoneNumbers.Normalize(input, country));

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("not a phone")]
    public void Rejects_bad_phone_numbers(string input) => Assert.Null(PhoneNumbers.Normalize(input, "NG"));

    [Theory]
    [InlineData(600_000, "NGN", "₦6,000")]
    [InlineData(150_050, "NGN", "₦1,500.50")]
    [InlineData(-2_500, "USD", "-$25")]
    [InlineData(100, "XYZ", "XYZ 1")]
    public void Formats_money(long minor, string currency, string expected) => Assert.Equal(expected, Money.Format(minor, currency));

    [Theory]
    [InlineData("Mama Put's Kitchen!", "mama-put-s-kitchen")]
    [InlineData("  Café Délice  ", "cafe-delice")]
    [InlineData("!!!", "item")]
    public void Makes_slugs(string input, string expected) => Assert.Equal(expected, Slugs.From(input));

    [Theory]
    [InlineData("mama-put", true)]
    [InlineData("dashboard", false)]
    [InlineData("ab", false)]
    [InlineData("Bad Slug", false)]
    [InlineData("-lead", false)]
    public void Validates_store_slugs(string slug, bool valid) => Assert.Equal(valid, Slugs.IsValid(slug));

    [Fact]
    public void Order_codes_avoid_ambiguous_characters()
    {
        for (var i = 0; i < 500; i++)
            Assert.Matches("^[A-HJKMNP-Z2-9]{6}$", Codes.Random());
    }

    [Fact]
    public void Dashboard_periods_use_store_time_zone()
    {
        // 23:30 UTC on 31 Oct is already 1 Nov in Lagos (UTC+1).
        var (today, month) = OrderAdminService.PeriodStarts(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), "Africa/Lagos");
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero), today);
        Assert.Equal(today, month);
    }
}
