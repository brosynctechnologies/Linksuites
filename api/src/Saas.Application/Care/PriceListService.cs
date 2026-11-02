using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.Application.Care;

public sealed record PriceItemDto(Guid Id, string Name, PriceCategory Category, long PriceMinor, bool Active);
public sealed record SavePriceItemRequest(string? Name, PriceCategory? Category, long? PriceMinor, bool? Active = null);

/// <summary>The clinic's price list. Every staff member reads it; leadership (Owner, Admin) and the cashier write it.</summary>
public sealed class PriceListService(IAppDbContext db, CareAccess access, IDbErrors dbErrors)
{
    public const string NameTaken = "There is already a price item with that name.";

    public async Task<IReadOnlyList<PriceItemDto>> ListAsync(bool? active, PriceCategory? category, CancellationToken ct)
    {
        await access.CallerAsync(ct);
        return await db.PriceItems.AsNoTracking()
            .Where(i => (active == null || i.Active == active) && (category == null || i.Category == category))
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .Select(i => new PriceItemDto(i.Id, i.Name, i.Category, i.PriceMinor, i.Active)).ToListAsync(ct);
    }

    public async Task<PriceItemDto> CreateAsync(SavePriceItemRequest r, CancellationToken ct)
    {
        EnsureWriter(await access.CallerAsync(ct));
        var (name, category, price) = await ValidateAsync(r, null, ct);
        var item = new PriceItem { Name = name, Category = category, PriceMinor = price, Active = r.Active ?? true };
        db.PriceItems.Add(item);
        await SaveAsync(ct);
        return ToDto(item);
    }

    public async Task<PriceItemDto> UpdateAsync(Guid id, SavePriceItemRequest r, CancellationToken ct)
    {
        EnsureWriter(await access.CallerAsync(ct));
        var item = await db.PriceItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Price item");
        var (name, category, price) = await ValidateAsync(r, id, ct);
        item.Name = name;
        item.Category = category;
        // Bills keep the price they charged: bill lines copy the price, so changing it here never touches an existing bill.
        item.PriceMinor = price;
        if (r.Active is { } active) item.Active = active;
        await SaveAsync(ct);
        return ToDto(item);
    }

    /// <summary>Leadership or the cashier; 403 otherwise.</summary>
    public static void EnsureWriter(CareCaller c)
    {
        if (!c.IsLeadership && !c.IsCashier) throw new ForbiddenException();
    }

    private async Task<(string Name, PriceCategory Category, long Price)> ValidateAsync(SavePriceItemRequest r, Guid? id, CancellationToken ct)
    {
        var name = (r.Name ?? "").Trim();
        if (name.Length == 0) throw BadRequestException.Field("name", "Enter the item's name.");
        if (name.Length > 120) throw BadRequestException.Field("name", "The name can have at most 120 characters.");
        if (r.Category is not { } category || !Enum.IsDefined(category))
            throw BadRequestException.Field("category", "Choose Consultation, Procedure, Test, Drug, Bed, Registration or Other.");
        if (r.PriceMinor is not { } price) throw BadRequestException.Field("priceMinor", "Enter the price.");
        if (price < 0) throw BadRequestException.Field("priceMinor", "The price can't be negative.");
        if (price > 100_000_000_000) throw BadRequestException.Field("priceMinor", "The price is too large.");
        var lower = name.ToLower();
        if (await db.PriceItems.AnyAsync(i => i.Id != id && i.Name.ToLower() == lower, ct)) throw new ConflictException(NameTaken, "price_item_exists");
        return (name, category, price);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (dbErrors.IsUniqueViolation(ex)) { throw new ConflictException(NameTaken, "price_item_exists"); }
    }

    internal static PriceItemDto ToDto(PriceItem i) => new(i.Id, i.Name, i.Category, i.PriceMinor, i.Active);
}
