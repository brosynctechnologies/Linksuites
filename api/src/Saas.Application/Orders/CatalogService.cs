using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed record CategoryDto(Guid Id, string Name, int Position, int ProductCount);
public sealed record SaveCategoryRequest(string Name, int Position);

public sealed record ImageDto(Guid Id, string Url, int Position);
public sealed record VariantDto(Guid Id, IReadOnlyList<string> OptionValues, string Title, long? PriceMinor, bool InStock, string? Sku);

public sealed record ProductDto(
    Guid Id, Guid? CategoryId, string? CategoryName, string Name, string Slug, string? Description, long PriceMinor,
    long? CompareAtPriceMinor, bool InStock, int? StockQty, bool IsActive, int Position, IReadOnlyList<ImageDto> Images,
    IReadOnlyList<OptionDto> Options, IReadOnlyList<VariantDto> Variants);

public sealed record SaveOptionRequest(string Name, List<string> Values);
public sealed record SaveVariantRequest(Guid? Id, List<string> OptionValues, long? PriceMinor, bool InStock, string? Sku);

public sealed record SaveProductRequest
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public Guid? CategoryId { get; init; }
    public long PriceMinor { get; init; }
    public long? CompareAtPriceMinor { get; init; }
    public bool InStock { get; init; } = true;
    public int? StockQty { get; init; }

    /// <summary>
    /// The stock count the edit form loaded (null when it loaded an untracked product). When sent, stock is a
    /// compare-and-set: unchanged stock is left alone, and a change is applied only if the stored count still matches,
    /// so sales made while the form was open are never written over.
    /// </summary>
    public int? ExpectedStockQty
    {
        get => _expectedStockQty;
        init
        {
            _expectedStockQty = value;
            HasExpectedStockQty = true; // only called when the JSON has the property, even when it is null
        }
    }
    private readonly int? _expectedStockQty;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasExpectedStockQty { get; private init; }

    public bool IsActive { get; init; } = true;
    public int Position { get; init; }
    public List<SaveOptionRequest> Options { get; init; } = [];
    public List<SaveVariantRequest> Variants { get; init; } = [];
}

public sealed class SaveCategoryRequestValidator : AbstractValidator<SaveCategoryRequest>
{
    public SaveCategoryRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
}

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.PriceMinor).InclusiveBetween(0, 100_000_000_000);
        RuleFor(x => x.CompareAtPriceMinor).GreaterThan(x => x.PriceMinor)
            .When(x => x.CompareAtPriceMinor is not null).WithMessage("Compare-at price should be higher than the price.");
        RuleFor(x => x.StockQty).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Options).Must(o => o.Count <= 3).WithMessage("Use at most 3 options (for example size, colour, material).");
        RuleForEach(x => x.Options).ChildRules(o =>
        {
            o.RuleFor(x => x.Name).NotEmpty().MaximumLength(30);
            o.RuleFor(x => x.Values).NotEmpty().Must(v => v.Count <= 30 && v.All(s => !string.IsNullOrWhiteSpace(s) && s.Length <= 40));
        });
        RuleFor(x => x.Variants).Must(v => v.Count <= 100).WithMessage("Use at most 100 variants.");
        RuleFor(x => x).Must(VariantsMatchOptions)
            .WithName("variants").WithMessage("Each variant must pick one value from every option.");
        RuleForEach(x => x.Variants).ChildRules(v => v.RuleFor(x => x.PriceMinor).GreaterThanOrEqualTo(0));
    }

    private static bool VariantsMatchOptions(SaveProductRequest r) =>
        r.Options.Count == 0
            ? r.Variants.Count == 0
            : r.Variants.All(v => v.OptionValues.Count == r.Options.Count
                && v.OptionValues.Select((value, i) => r.Options[i].Values.Contains(value)).All(ok => ok));
}

public sealed class CatalogService(IAppDbContext db, StoreSettingsService stores, PlanService plans, IFileStorage storage)
{
    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(c => c.Position).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Position, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<CategoryDto> SaveCategoryAsync(Guid? id, SaveCategoryRequest r, CancellationToken ct)
    {
        Category category;
        if (id is null)
        {
            var store = await stores.CurrentStoreAsync(ct);
            category = new Category { StoreId = store.Id, Name = r.Name.Trim() };
            db.Categories.Add(category);
        }
        else
        {
            category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category");
        }
        category.Name = r.Name.Trim();
        category.Position = r.Position;
        await db.SaveChangesAsync(ct);
        var count = await db.Products.CountAsync(p => p.CategoryId == category.Id, ct);
        return new CategoryDto(category.Id, category.Name, category.Position, count);
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category");
        await db.Products.Where(p => p.CategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, (Guid?)null), ct);
        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ProductDto>> ListProductsAsync(string? q, Guid? categoryId, CancellationToken ct)
    {
        var query = Products();
        if (categoryId is { } cid) query = query.Where(p => p.CategoryId == cid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }
        var list = await query.OrderBy(p => p.Position).ThenByDescending(p => p.CreatedAt).ToListAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<ProductDto> GetProductAsync(Guid id, CancellationToken ct) => ToDto(await LoadAsync(id, track: false, ct));

    public async Task<ProductDto> SaveProductAsync(Guid? id, SaveProductRequest r, CancellationToken ct)
    {
        var store = await stores.CurrentStoreAsync(ct);
        if (r.CategoryId is { } cid && !await db.Categories.AnyAsync(c => c.Id == cid, ct))
            throw BadRequestException.Field("categoryId", "That category does not exist.");

        Product product;
        if (id is null)
        {
            await plans.EnsureCanAddAsync(LimitKeys.Products, await db.Products.CountAsync(ct), "Products", ct);
            product = new Product { StoreId = store.Id, Name = r.Name.Trim(), Slug = await UniqueSlugAsync(r.Name, null, ct) };
            db.Products.Add(product);
        }
        else
        {
            product = await LoadAsync(id.Value, track: true, ct);
            if (!string.Equals(product.Name, r.Name.Trim(), StringComparison.Ordinal))
                product.Slug = await UniqueSlugAsync(r.Name, product.Id, ct);
        }

        product.Name = r.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        product.CategoryId = r.CategoryId;
        product.PriceMinor = r.PriceMinor;
        product.CompareAtPriceMinor = r.CompareAtPriceMinor;
        product.InStock = r.InStock;
        // Without an expected count (new products, older clients) the stock is written as sent. With one, see below.
        var compareAndSetStock = false;
        if (id is null || !r.HasExpectedStockQty) product.StockQty = r.StockQty;
        else compareAndSetStock = r.StockQty != r.ExpectedStockQty; // unchanged in the form: leave the live count alone
        product.IsActive = r.IsActive;
        product.Position = r.Position;

        product.Options.Clear();
        product.Options.AddRange(r.Options.Select((o, i) => new ProductOption
        {
            ProductId = product.Id,
            Name = o.Name.Trim(),
            Values = o.Values.Select(v => v.Trim()).Distinct().ToList(),
            Position = i,
        }));

        var keep = r.Variants.Where(v => v.Id is not null).Select(v => v.Id!.Value).ToHashSet();
        product.Variants.RemoveAll(v => !keep.Contains(v.Id));
        foreach (var v in r.Variants)
        {
            var variant = v.Id is { } vid ? product.Variants.FirstOrDefault(x => x.Id == vid) : null;
            if (variant is null)
            {
                variant = new ProductVariant { ProductId = product.Id };
                product.Variants.Add(variant);
            }
            variant.OptionValues = v.OptionValues.Select(x => x.Trim()).ToList();
            variant.PriceMinor = v.PriceMinor;
            variant.InStock = v.InStock;
            variant.Sku = string.IsNullOrWhiteSpace(v.Sku) ? null : v.Sku.Trim();
        }

        if (!compareAndSetStock)
        {
            await db.SaveChangesAsync(ct);
            return await GetProductAsync(product.Id, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (expected, next) = (r.ExpectedStockQty, r.StockQty);
        var applied = await db.Products.Where(p => p.Id == product.Id && p.StockQty == expected)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQty, next), ct);
        if (applied == 0)
        {
            var current = await db.Products.AsNoTracking().Where(p => p.Id == product.Id).Select(p => p.StockQty).FirstAsync(ct);
            throw new ConflictException(
                $"The stock changed to {(current is { } n ? n.ToString() : "not tracked")} while you were editing, because orders came in. " +
                "Reload the product to see the new count, then set the stock again.", "stock_changed");
        }
        // The tracked product's StockQty is left untouched, so SaveChanges doesn't write the count again.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetProductAsync(product.Id, ct);
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken ct)
    {
        var product = await LoadAsync(id, track: true, ct);
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        foreach (var image in product.Images.Where(i => i.StorageKey is not null))
            await storage.DeleteAsync(image.StorageKey!, ct);
    }

    public async Task<ProductDto> AddImageAsync(Guid productId, Upload upload, CancellationToken ct)
    {
        var product = await LoadAsync(productId, track: true, ct);
        if (product.Images.Count >= 8) throw BadRequestException.Field("file", "A product can have up to 8 images.");
        var (buffer, ext, mime) = await ImageUploads.ReadAsync(upload, ct);
        await using var _ = buffer;
        var key = $"{product.TenantId}/products/{product.Id:N}/{Guid.CreateVersion7():N}{ext}";
        var url = await storage.SaveAsync(key, buffer, mime, ct);
        product.Images.Add(new ProductImage
        {
            ProductId = product.Id,
            Url = url,
            StorageKey = key,
            Position = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.Position) + 1,
        });
        db.Files.Add(new StoredFile { Key = key, Url = url, MimeType = mime, SizeBytes = buffer.Length, Purpose = "product-image" });
        await db.SaveChangesAsync(ct);
        return await GetProductAsync(product.Id, ct);
    }

    public async Task<ProductDto> DeleteImageAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        var product = await LoadAsync(productId, track: true, ct);
        var image = product.Images.FirstOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("Image");
        product.Images.Remove(image);
        await db.SaveChangesAsync(ct);
        if (image.StorageKey is not null) await storage.DeleteAsync(image.StorageKey, ct);
        return await GetProductAsync(product.Id, ct);
    }

    private IQueryable<Product> Products() => db.Products.AsNoTracking()
        .Include(p => p.Category).Include(p => p.Images).Include(p => p.Options).Include(p => p.Variants);

    private async Task<Product> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = db.Products.Include(p => p.Category).Include(p => p.Images)
            .Include(p => p.Options).Include(p => p.Variants);
        var q = track ? query : query.AsNoTracking();
        return await q.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product");
    }

    private async Task<string> UniqueSlugAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var baseSlug = Slugs.From(name);
        var slug = baseSlug;
        for (var n = 2; await db.Products.AnyAsync(p => p.Slug == slug && p.Id != excludeId, ct); n++)
            slug = $"{baseSlug}-{n}";
        return slug;
    }

    private static ProductDto ToDto(Product p) => new(
        p.Id, p.CategoryId, p.Category?.Name, p.Name, p.Slug, p.Description, p.PriceMinor, p.CompareAtPriceMinor,
        p.InStock, p.StockQty, p.IsActive, p.Position,
        p.Images.OrderBy(i => i.Position).Select(i => new ImageDto(i.Id, i.Url, i.Position)).ToList(),
        p.Options.OrderBy(o => o.Position).Select(o => new OptionDto(o.Name, o.Values)).ToList(),
        p.Variants.Select(v => new VariantDto(v.Id, v.OptionValues, v.Title, v.PriceMinor, v.InStock, v.Sku)).ToList());
}
