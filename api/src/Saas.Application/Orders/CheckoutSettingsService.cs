using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed record DeliveryZoneDto(Guid Id, string Name, long FeeMinor, string? EtaText, bool IsActive);
public sealed record SaveDeliveryZoneRequest(string Name, long FeeMinor, string? EtaText, bool IsActive);

public sealed record DiscountCodeDto(
    Guid Id, string Code, DiscountType Type, long Value, long MinOrderMinor, DateTimeOffset? ExpiresAt, int? MaxUses,
    int Uses, bool IsActive);

/// <summary><c>MinOrderMinor</c> is optional: null (an empty "Minimum order" field) means no minimum.</summary>
public sealed record SaveDiscountCodeRequest(
    string Code, DiscountType Type, long Value, long? MinOrderMinor, DateTimeOffset? ExpiresAt, int? MaxUses, bool IsActive);

public sealed class SaveDeliveryZoneRequestValidator : AbstractValidator<SaveDeliveryZoneRequest>
{
    public SaveDeliveryZoneRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.FeeMinor).InclusiveBetween(0, 1_000_000_000);
        RuleFor(x => x.EtaText).MaximumLength(60);
    }
}

public sealed class SaveDiscountCodeRequestValidator : AbstractValidator<SaveDiscountCodeRequest>
{
    public SaveDiscountCodeRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Use letters, numbers, dashes or underscores.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Value).InclusiveBetween(1, 100).When(x => x.Type == DiscountType.Percent)
            .WithMessage("A percentage must be between 1 and 100.");
        RuleFor(x => x.Value).GreaterThan(0).When(x => x.Type == DiscountType.Fixed);
        RuleFor(x => x.MinOrderMinor).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxUses).GreaterThan(0);
    }
}

/// <summary>Delivery zones and discount codes: the seller's checkout settings.</summary>
public sealed class CheckoutSettingsService(IAppDbContext db, StoreSettingsService stores, PlanService plans)
{
    public async Task<IReadOnlyList<DeliveryZoneDto>> ListZonesAsync(CancellationToken ct) =>
        await db.DeliveryZones.AsNoTracking().OrderBy(z => z.FeeMinor).ThenBy(z => z.Name)
            .Select(z => new DeliveryZoneDto(z.Id, z.Name, z.FeeMinor, z.EtaText, z.IsActive)).ToListAsync(ct);

    public async Task<DeliveryZoneDto> SaveZoneAsync(Guid? id, SaveDeliveryZoneRequest r, CancellationToken ct)
    {
        DeliveryZone zone;
        if (id is null)
        {
            await plans.EnsureCanAddAsync(LimitKeys.DeliveryZones, await db.DeliveryZones.CountAsync(ct), "Delivery zones", ct);
            var store = await stores.CurrentStoreAsync(ct);
            zone = new DeliveryZone { StoreId = store.Id, Name = r.Name.Trim() };
            db.DeliveryZones.Add(zone);
        }
        else
        {
            zone = await db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id, ct) ?? throw new NotFoundException("Delivery zone");
        }
        zone.Name = r.Name.Trim();
        zone.FeeMinor = r.FeeMinor;
        zone.EtaText = string.IsNullOrWhiteSpace(r.EtaText) ? null : r.EtaText.Trim();
        zone.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return new DeliveryZoneDto(zone.Id, zone.Name, zone.FeeMinor, zone.EtaText, zone.IsActive);
    }

    public async Task DeleteZoneAsync(Guid id, CancellationToken ct)
    {
        var zone = await db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id, ct) ?? throw new NotFoundException("Delivery zone");
        db.DeliveryZones.Remove(zone);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DiscountCodeDto>> ListDiscountsAsync(CancellationToken ct) =>
        await db.DiscountCodes.AsNoTracking().OrderByDescending(d => d.CreatedAt)
            .Select(d => new DiscountCodeDto(d.Id, d.Code, d.Type, d.Value, d.MinOrderMinor, d.ExpiresAt, d.MaxUses, d.Uses, d.IsActive))
            .ToListAsync(ct);

    public async Task<DiscountCodeDto> SaveDiscountAsync(Guid? id, SaveDiscountCodeRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.DiscountCodes.AnyAsync(d => d.Code == code && d.Id != id, ct))
            throw BadRequestException.Field("code", "You already have a discount with this code.");

        DiscountCode discount;
        if (id is null)
        {
            await plans.EnsureCanAddAsync(LimitKeys.DiscountCodes, await db.DiscountCodes.CountAsync(ct), "Discount codes", ct);
            var store = await stores.CurrentStoreAsync(ct);
            discount = new DiscountCode { StoreId = store.Id, Code = code };
            db.DiscountCodes.Add(discount);
        }
        else
        {
            discount = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Discount code");
        }
        discount.Code = code;
        discount.Type = r.Type;
        discount.Value = r.Value;
        discount.MinOrderMinor = r.MinOrderMinor ?? 0;
        discount.ExpiresAt = r.ExpiresAt?.ToUniversalTime(); // timestamptz only accepts UTC offsets
        discount.MaxUses = r.MaxUses;
        discount.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return new DiscountCodeDto(discount.Id, discount.Code, discount.Type, discount.Value, discount.MinOrderMinor,
            discount.ExpiresAt, discount.MaxUses, discount.Uses, discount.IsActive);
    }

    public async Task DeleteDiscountAsync(Guid id, CancellationToken ct)
    {
        var discount = await db.DiscountCodes.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Discount code");
        db.DiscountCodes.Remove(discount);
        await db.SaveChangesAsync(ct);
    }
}
