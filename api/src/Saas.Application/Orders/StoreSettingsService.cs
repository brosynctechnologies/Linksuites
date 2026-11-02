using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Orders;

namespace Saas.Application.Orders;

public sealed record StoreDto(
    Guid Id, string Name, string Slug, string? Description, string? LogoUrl, string? CoverUrl, string WhatsappNumber,
    string Currency, string ThemeColor, bool IsOpen, string? OpeningHours, string Country, string StoreUrl);

public sealed record UpdateStoreRequest(
    string Name, string Slug, string? Description, string WhatsappNumber, string Currency, string ThemeColor,
    bool IsOpen, string? OpeningHours);

public sealed class UpdateStoreRequestValidator : AbstractValidator<UpdateStoreRequest>
{
    public UpdateStoreRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Slug).NotEmpty().Must(Slugs.IsWellFormed)
            .WithMessage("Use 3-60 lowercase letters, numbers and dashes (some words are reserved).");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.WhatsappNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Currency).Must(c => Money.SupportedCurrencies.Contains(c)).WithMessage("Unsupported currency.");
        RuleFor(x => x.ThemeColor).Matches("^#[0-9a-fA-F]{6}$").WithMessage("Use a hex colour such as #16a34a.");
        RuleFor(x => x.OpeningHours).MaximumLength(200);
    }
}

public sealed class StoreSettingsService(IAppDbContext db, ITenantContext tenant, IFileStorage storage, AppUrls urls)
{
    public async Task<StoreDto> GetAsync(CancellationToken ct)
    {
        var store = await CurrentStoreAsync(ct);
        return await ToDtoAsync(store, ct);
    }

    public async Task<StoreDto> UpdateAsync(UpdateStoreRequest r, CancellationToken ct)
    {
        var store = await CurrentStoreAsync(ct);
        var t = await db.Tenants.FirstAsync(x => x.Id == store.TenantId, ct);
        var slug = r.Slug.Trim().ToLowerInvariant();
        Slugs.EnsureNotNewlyReserved(slug, store.Slug);
        if (slug != store.Slug && await db.Stores.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug, ct))
            throw BadRequestException.Field("slug", "That store link is already taken.");
        store.WhatsappNumber = PhoneNumbers.Normalize(r.WhatsappNumber, t.Country)
            ?? throw BadRequestException.Field("whatsappNumber", "Enter a valid WhatsApp number.");

        store.Name = r.Name.Trim();
        store.Slug = slug;
        store.Description = r.Description?.Trim();
        store.Currency = r.Currency.ToUpperInvariant();
        store.ThemeColor = r.ThemeColor;
        store.IsOpen = r.IsOpen;
        store.OpeningHours = r.OpeningHours?.Trim();
        t.Name = store.Name;
        t.Slug = slug;
        t.Currency = store.Currency;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(store, ct);
    }

    public async Task<StoreDto> UploadImageAsync(Upload upload, bool cover, CancellationToken ct)
    {
        var store = await CurrentStoreAsync(ct);
        var (buffer, ext, mime) = await ImageUploads.ReadAsync(upload, ct);
        await using var _ = buffer;
        var key = $"{store.TenantId}/store/{(cover ? "cover" : "logo")}-{Guid.CreateVersion7():N}{ext}";
        var url = await storage.SaveAsync(key, buffer, mime, ct);
        db.Files.Add(new StoredFile { Key = key, Url = url, MimeType = mime, SizeBytes = buffer.Length, Purpose = cover ? "store-cover" : "store-logo" });
        if (cover) store.CoverUrl = url; else store.LogoUrl = url;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(store, ct);
    }

    public async Task<Store> CurrentStoreAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        return await db.Stores.OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Store");
    }

    private async Task<StoreDto> ToDtoAsync(Store s, CancellationToken ct)
    {
        var country = await db.Tenants.Where(t => t.Id == s.TenantId).Select(t => t.Country).FirstAsync(ct);
        return new StoreDto(s.Id, s.Name, s.Slug, s.Description, s.LogoUrl, s.CoverUrl, s.WhatsappNumber, s.Currency,
            s.ThemeColor, s.IsOpen, s.OpeningHours, country, urls.StoreUrl(s.Slug));
    }
}
