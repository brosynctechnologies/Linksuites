using FluentValidation;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed record PublicZoneDto(Guid Id, string Name, long FeeMinor, string? EtaText);
public sealed record PublicCategoryDto(Guid Id, string Name);

public sealed record PublicStoreDto(
    string Name, string Slug, string? Description, string? LogoUrl, string? CoverUrl, string Currency,
    string ThemeColor, bool IsOpen, IReadOnlyList<PublicZoneDto> DeliveryZones,
    IReadOnlyList<PublicCategoryDto> Categories, bool ShowBranding, bool HasDiscounts);

public sealed record ImageUrlDto(string Url);
public sealed record OptionDto(string Name, IReadOnlyList<string> Values);
public sealed record PublicVariantDto(Guid Id, IReadOnlyList<string> OptionValues, string Title, long PriceMinor, bool InStock);

public sealed record PublicProductDto(
    Guid Id, string Slug, string Name, string? Description, long PriceMinor, long? CompareAtPriceMinor,
    bool InStock, Guid? CategoryId, IReadOnlyList<ImageUrlDto> Images, IReadOnlyList<OptionDto> Options,
    IReadOnlyList<PublicVariantDto> Variants);

public sealed record CartItemRequest(Guid ProductId, Guid? VariantId, int Quantity);

public record QuoteRequest
{
    public IReadOnlyList<CartItemRequest> Items { get; init; } = [];
    public Fulfilment Fulfilment { get; init; } = Fulfilment.Delivery;
    public Guid? DeliveryZoneId { get; init; }
    public string? DiscountCode { get; init; }
}

public sealed record PlaceOrderRequest : QuoteRequest
{
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Address { get; init; }
    public string? Note { get; init; }
}

public sealed record QuoteLineDto(
    Guid ProductId, Guid? VariantId, string Name, string? Variant, long UnitPriceMinor, int Quantity, long LineTotalMinor,
    int? StockQty = null);

public sealed record QuoteDto(
    IReadOnlyList<QuoteLineDto> Lines, long SubtotalMinor, long DeliveryFeeMinor, long DiscountMinor,
    long TotalMinor, string Currency, string? DiscountMessage);

public sealed record PlaceOrderResult(string Code, long Number, long TotalMinor, string Currency, string WhatsappUrl, string OrderUrl);

public sealed record PublicOrderItemDto(string Name, string? Variant, int Quantity, long LineTotalMinor);

public sealed record PublicOrderDto(
    string Code, long Number, string StoreName, string StoreSlug, OrderStatus Status, OrderPaymentStatus PaymentStatus,
    Fulfilment Fulfilment, IReadOnlyList<PublicOrderItemDto> Items, long SubtotalMinor, long DeliveryFeeMinor,
    long DiscountMinor, long TotalMinor, string Currency, DateTimeOffset CreatedAt, string WhatsappUrl, bool ShowBranding);

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("Your cart is empty.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(50).WithMessage("Too many different items in one order.");
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).NotEmpty();
            i.RuleFor(x => x.Quantity).InclusiveBetween(1, 100);
        });
        RuleFor(x => x.Fulfilment).IsInEnum();
        RuleFor(x => x.DiscountCode).MaximumLength(40);
    }
}

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        Include(new QuoteRequestValidator());
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Address).NotEmpty().When(x => x.Fulfilment == Fulfilment.Delivery)
            .WithMessage("Enter a delivery address.");
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
