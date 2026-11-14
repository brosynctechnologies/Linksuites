using Microsoft.EntityFrameworkCore;
using Saas.Application.Auth;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed class InvoicesProvisioner(IAppDbContext db) : IProductProvisioner
{
    public string Product => Products.Invoices;

    public Task ProvisionAsync(Tenant tenant, SignupRequest request, Guid ownerUserId, CancellationToken ct)
    {
        db.BusinessSettings.Add(new BusinessSettings
        {
            LegalName = tenant.Name,
            Email = request.Email.Trim().ToLowerInvariant(),
            Currency = tenant.Currency,
            DefaultTerms = "Payment is due within 14 days. Thank you for your business.",
        });
        if (tenant.Country == "NG") db.TaxRates.Add(new TaxRate { Name = "VAT", RatePercent = 7.5m });
        return Task.CompletedTask;
    }
}

public sealed class InvoicesUsage(IAppDbContext db, InvoiceWorkspace workspace) : IUsageProvider
{
    public string Product => Products.Invoices;

    public async Task<IReadOnlyList<UsageItemDto>> GetAsync(Plan plan, CancellationToken ct)
    {
        var monthStart = await workspace.MonthStartUtcAsync(ct);
        var count = await db.Invoices.CountAsync(i => i.CreatedAt >= monthStart, ct);
        return [new(LimitKeys.InvoicesPerMonth, "Invoices this month", count, plan.Limit(LimitKeys.InvoicesPerMonth))];
    }
}

/// <summary>Business profile, tax rates, clients and saved items.</summary>
public sealed class InvoiceSetupService(
    IAppDbContext db, InvoiceWorkspace workspace, IFileStorage storage, ISecretProtector secrets, PlanService plans, MerchantKeyRules keyRules)
{
    public const string BrandImagesMessage = "Your logo, signature and stamp are part of the Pro plan. Upgrade to add them.";

    public async Task<BusinessDto> GetBusinessAsync(CancellationToken ct) => await DtoAsync(await workspace.BusinessAsync(ct), ct);

    public async Task<BusinessDto> UpdateBusinessAsync(UpdateBusinessRequest r, CancellationToken ct)
    {
        var b = await workspace.BusinessAsync(ct);
        if (r.RemindersEnabled && !b.RemindersEnabled && !await plans.HasFeatureAsync(LimitKeys.Reminders, ct))
            throw new PlanLimitException("Automatic reminders are part of the Pro plan. Upgrade to turn them on.");
        // Only a change of default design is checked, so a premium default kept after a downgrade doesn't block other settings.
        if (r.DefaultTemplate != b.DefaultTemplate && InvoiceTemplates.IsPremium(r.DefaultTemplate)
            && !await plans.HasFeatureAsync(LimitKeys.PremiumTemplates, ct))
            throw new PlanLimitException("Premium templates are part of the Pro plan. Upgrade to use them.");

        // The business currency is the base for reports; saved base amounts would be wrong after a change.
        if (!string.Equals(r.Currency, b.Currency, StringComparison.OrdinalIgnoreCase)
            && (await db.Invoices.AnyAsync(ct) || await db.Quotes.AnyAsync(ct) || await db.Expenses.AnyAsync(ct)))
            throw BadRequestException.Field("currency",
                "Your reporting currency can't change once you have invoices, quotes or expenses. You can still bill in other currencies.");

        // Numbers only move forward, so an invoice number can never be issued twice.
        if (r.NextInvoiceNumber < b.NextInvoiceNumber)
            throw BadRequestException.Field("nextInvoiceNumber", $"The next invoice number can't go below {b.NextInvoiceNumber}.");
        if (r.NextQuoteNumber < b.NextQuoteNumber)
            throw BadRequestException.Field("nextQuoteNumber", $"The next quote number can't go below {b.NextQuoteNumber}.");

        b.LegalName = r.LegalName.Trim();
        b.Address = Clean(r.Address);
        b.Email = Clean(r.Email)?.ToLowerInvariant();
        b.Phone = Clean(r.Phone);
        b.TaxId = Clean(r.TaxId);
        b.BankDetails = Clean(r.BankDetails);
        b.Currency = r.Currency.ToUpperInvariant();
        b.PaymentTermsDays = r.PaymentTermsDays;
        b.InvoicePrefix = r.InvoicePrefix.Trim();
        b.NextInvoiceNumber = r.NextInvoiceNumber;
        b.QuotePrefix = r.QuotePrefix.Trim();
        b.NextQuoteNumber = r.NextQuoteNumber;
        b.DefaultTemplate = r.DefaultTemplate;
        b.BrandColor = r.BrandColor;
        b.DefaultNotes = Clean(r.DefaultNotes);
        b.DefaultTerms = Clean(r.DefaultTerms);
        b.RemindersEnabled = r.RemindersEnabled;
        b.ReminderDaysBefore = r.ReminderDaysBefore;
        b.ReminderDaysAfter = (r.ReminderDaysAfter ?? []).Distinct().Order().ToList();

        // Letterhead text: null or absent keeps the stored value (a client that predates these fields can't wipe them); "" clears.
        b.Tagline = KeepOrClean(r.Tagline, b.Tagline);
        b.RegistrationNumber = KeepOrClean(r.RegistrationNumber, b.RegistrationNumber);
        b.SecondaryColor = KeepOrClean(r.SecondaryColor, b.SecondaryColor)?.ToLowerInvariant();
        b.SignatureName = KeepOrClean(r.SignatureName, b.SignatureName);
        b.SignatureRole = KeepOrClean(r.SignatureRole, b.SignatureRole);
        b.FooterText = KeepOrClean(r.FooterText, b.FooterText);
        b.HeaderLine = r.HeaderLine ?? b.HeaderLine;
        b.LogoSize = r.LogoSize ?? b.LogoSize;
        b.FontStyle = r.FontStyle ?? b.FontStyle;
        b.DocumentTitle = r.DocumentTitle ?? b.DocumentTitle;
        b.ShowTaxId = r.ShowTaxId ?? b.ShowTaxId;
        b.ShowRegistrationNumber = r.ShowRegistrationNumber ?? b.ShowRegistrationNumber;
        b.ShowBankDetails = r.ShowBankDetails ?? b.ShowBankDetails;
        b.ShowSignature = r.ShowSignature ?? b.ShowSignature;
        await db.SaveChangesAsync(ct);
        return await DtoAsync(b, ct);
    }

    /// <summary>Logo, signature or stamp. Uploading needs a plan with brand images; removing works on every plan.</summary>
    public async Task<BusinessDto> UploadImageAsync(BrandImage kind, Upload upload, CancellationToken ct)
    {
        if (!await plans.HasFeatureAsync(LimitKeys.BrandImages, ct)) throw new PlanLimitException(BrandImagesMessage);
        var b = await workspace.BusinessAsync(ct);
        var (buffer, ext, mime) = await ImageUploads.ReadAsync(upload, ct);
        await using var _ = buffer;
        ImageUploads.EnsureDimensions(buffer); // every PDF decodes these, so their pixel size is bounded
        var name = Name(kind);
        var key = $"{b.TenantId}/business/{name}-{Guid.CreateVersion7():N}{ext}";
        var url = await storage.SaveAsync(key, buffer, mime, ct);
        var previous = await StoredImageAsync(kind, Url(b, kind), ct);
        SetUrl(b, kind, url);
        db.Files.Add(new StoredFile { Key = key, Url = url, MimeType = mime, SizeBytes = buffer.Length, Purpose = $"business-{name}" });
        if (previous is not null) db.Files.Remove(previous);
        await db.SaveChangesAsync(ct);
        if (previous is not null) await storage.DeleteAsync(previous.Key, ct);
        return await DtoAsync(b, ct);
    }

    public async Task RemoveImageAsync(BrandImage kind, CancellationToken ct)
    {
        var b = await workspace.BusinessAsync(ct);
        var previous = await StoredImageAsync(kind, Url(b, kind), ct);
        SetUrl(b, kind, null);
        if (previous is not null) db.Files.Remove(previous);
        await db.SaveChangesAsync(ct);
        if (previous is not null) await storage.DeleteAsync(previous.Key, ct);
    }

    public async Task<BusinessDto> SetPaystackKeyAsync(PaystackKeyRequest r, CancellationToken ct)
    {
        var key = keyRules.Validate(r.SecretKey);
        var b = await workspace.BusinessAsync(ct);
        b.PaystackSecretKeyProtected = secrets.Protect(key);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(b, ct);
    }

    public async Task<BusinessDto> RemovePaystackKeyAsync(CancellationToken ct)
    {
        var b = await workspace.BusinessAsync(ct);
        b.PaystackSecretKeyProtected = null;
        await db.SaveChangesAsync(ct);
        return await DtoAsync(b, ct);
    }

    private static string Name(BrandImage kind) => kind switch
    {
        BrandImage.Logo => "logo",
        BrandImage.Signature => "signature",
        _ => "stamp",
    };

    private static string? Url(BusinessSettings b, BrandImage kind) => kind switch
    {
        BrandImage.Logo => b.LogoUrl,
        BrandImage.Signature => b.SignatureImageUrl,
        _ => b.StampImageUrl,
    };

    private static void SetUrl(BusinessSettings b, BrandImage kind, string? url)
    {
        switch (kind)
        {
            case BrandImage.Logo: b.LogoUrl = url; break;
            case BrandImage.Signature: b.SignatureImageUrl = url; break;
            default: b.StampImageUrl = url; break;
        }
    }

    private async Task<StoredFile?> StoredImageAsync(BrandImage kind, string? url, CancellationToken ct) =>
        url is null ? null : await db.Files.FirstOrDefaultAsync(f => f.Url == url && f.Purpose == $"business-{Name(kind)}", ct);

    // Tax rates

    public async Task<IReadOnlyList<TaxRateDto>> ListTaxRatesAsync(CancellationToken ct) =>
        await db.TaxRates.AsNoTracking().OrderBy(t => t.Name).Select(t => new TaxRateDto(t.Id, t.Name, t.RatePercent)).ToListAsync(ct);

    public async Task<TaxRateDto> SaveTaxRateAsync(Guid? id, SaveTaxRateRequest r, CancellationToken ct)
    {
        TaxRate rate;
        if (id is null)
        {
            rate = new TaxRate { Name = r.Name.Trim() };
            db.TaxRates.Add(rate);
        }
        else
        {
            rate = await db.TaxRates.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Tax rate");
        }
        rate.Name = r.Name.Trim();
        rate.RatePercent = r.RatePercent;
        await db.SaveChangesAsync(ct);
        return new TaxRateDto(rate.Id, rate.Name, rate.RatePercent);
    }

    /// <summary>
    /// Only an unused rate can be deleted. Invoice, quote and recurring lines keep pointing at their rate, and editing
    /// them or running a schedule looks it up again, so deleting a rate in use is refused.
    /// </summary>
    public async Task DeleteTaxRateAsync(Guid id, CancellationToken ct)
    {
        var rate = await db.TaxRates.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Tax rate");
        var inUse = await db.InvoiceItems.AnyAsync(l => l.TaxRateId == id, ct)
            || await db.Quotes.AnyAsync(q => q.Items.Any(l => l.TaxRateId == id), ct)
            // Recurring lines are stored as JSON, so check them in memory.
            || (await db.RecurringSchedules.AsNoTracking().Select(s => s.Lines).ToListAsync(ct)).Any(lines => lines.Any(l => l.TaxRateId == id));
        if (inUse)
            throw new ConflictException("This tax rate is used on invoices, quotes or recurring schedules, so it can't be deleted.", "tax_rate_in_use");
        await db.CatalogItems.Where(i => i.TaxRateId == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.TaxRateId, (Guid?)null), ct);
        db.TaxRates.Remove(rate);
        await db.SaveChangesAsync(ct);
    }

    // Clients

    public async Task<IReadOnlyList<ClientDto>> ListClientsAsync(string? q, CancellationToken ct)
    {
        var query = db.Clients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || (c.Company != null && c.Company.ToLower().Contains(term))
                || (c.Email != null && c.Email.ToLower().Contains(term)));
        }
        return await ClientDtosAsync(query.OrderBy(c => c.Name), ct);
    }

    public async Task<ClientDto> GetClientAsync(Guid id, CancellationToken ct) =>
        (await ClientDtosAsync(db.Clients.AsNoTracking().Where(c => c.Id == id), ct)).FirstOrDefault() ?? throw new NotFoundException("Client");

    public async Task<ClientDto> SaveClientAsync(Guid? id, SaveClientRequest r, CancellationToken ct)
    {
        Client client;
        if (id is null)
        {
            client = new Client { Name = r.Name.Trim() };
            db.Clients.Add(client);
        }
        else
        {
            client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Client");
        }
        client.Name = r.Name.Trim();
        client.Company = Clean(r.Company);
        client.Email = Clean(r.Email)?.ToLowerInvariant();
        client.Phone = Clean(r.Phone);
        client.Address = Clean(r.Address);
        client.TaxId = Clean(r.TaxId);
        client.Notes = Clean(r.Notes);
        await db.SaveChangesAsync(ct);
        return await GetClientAsync(client.Id, ct);
    }

    public async Task DeleteClientAsync(Guid id, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Client");
        if (await db.RecurringSchedules.AnyAsync(s => s.ClientId == id && s.IsActive, ct))
            throw new ConflictException("This client has an active recurring invoice. Stop it first.");
        await db.Invoices.Where(i => i.ClientId == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.ClientId, (Guid?)null), ct);
        await db.Quotes.Where(q => q.ClientId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.ClientId, (Guid?)null), ct);
        db.Clients.Remove(client);
        await db.SaveChangesAsync(ct);
    }

    // Saved items

    public async Task<IReadOnlyList<ItemDto>> ListItemsAsync(string? q, CancellationToken ct)
    {
        var query = db.CatalogItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(term));
        }
        return await query.OrderBy(i => i.Name)
            .Select(i => new ItemDto(i.Id, i.Name, i.Description, i.UnitPriceMinor, i.TaxRateId, i.Unit)).ToListAsync(ct);
    }

    public async Task<ItemDto> SaveItemAsync(Guid? id, SaveItemRequest r, CancellationToken ct)
    {
        if (r.TaxRateId is { } taxId && !await db.TaxRates.AnyAsync(t => t.Id == taxId, ct))
            throw BadRequestException.Field("taxRateId", "That tax rate does not exist.");
        CatalogItem item;
        if (id is null)
        {
            item = new CatalogItem { Name = r.Name.Trim() };
            db.CatalogItems.Add(item);
        }
        else
        {
            item = await db.CatalogItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Item");
        }
        item.Name = r.Name.Trim();
        item.Description = Clean(r.Description);
        item.UnitPriceMinor = r.UnitPriceMinor;
        item.TaxRateId = r.TaxRateId;
        item.Unit = Clean(r.Unit);
        await db.SaveChangesAsync(ct);
        return new ItemDto(item.Id, item.Name, item.Description, item.UnitPriceMinor, item.TaxRateId, item.Unit);
    }

    public async Task DeleteItemAsync(Guid id, CancellationToken ct)
    {
        var item = await db.CatalogItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Item");
        db.CatalogItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Outstanding is in the business's base currency: each balance is converted with the rate saved on its invoice,
    /// as the dashboard does, so invoices in different currencies can be added up.
    /// </summary>
    private async Task<List<ClientDto>> ClientDtosAsync(IQueryable<Client> clients, CancellationToken ct)
    {
        var rows = await clients.Select(c => new
        {
            Client = c,
            InvoiceCount = db.Invoices.Count(i => i.ClientId == c.Id && i.Status != InvoiceStatus.Void),
        }).ToListAsync(ct);
        var ids = rows.Select(r => r.Client.Id).ToList();
        var open = await db.Invoices.AsNoTracking()
            .Where(i => i.ClientId != null && ids.Contains(i.ClientId.Value) && i.Status != InvoiceStatus.Void
                && i.Status != InvoiceStatus.Draft && i.BalanceMinor > 0)
            .Select(i => new { ClientId = i.ClientId!.Value, i.BalanceMinor, i.ExchangeRate }).ToListAsync(ct);
        var outstanding = open.GroupBy(i => i.ClientId)
            .ToDictionary(g => g.Key, g => g.Sum(i => ExchangeRateService.ToBase(i.BalanceMinor, i.ExchangeRate)));
        return rows.Select(r => new ClientDto(r.Client.Id, r.Client.Name, r.Client.Company, r.Client.Email, r.Client.Phone,
            r.Client.Address, r.Client.TaxId, r.Client.Notes, r.InvoiceCount, outstanding.GetValueOrDefault(r.Client.Id))).ToList();
    }

    /// <summary>The owner always sees the stored image URLs, with whether the plan prints them.</summary>
    private async Task<BusinessDto> DtoAsync(BusinessSettings b, CancellationToken ct) => new(
        b.LegalName, b.LogoUrl, b.Address, b.Email, b.Phone, b.TaxId, b.BankDetails, b.Currency, await workspace.CountryAsync(ct),
        b.PaymentTermsDays, b.InvoicePrefix, b.NextInvoiceNumber, b.QuotePrefix, b.NextQuoteNumber, b.DefaultTemplate, b.BrandColor,
        b.DefaultNotes, b.DefaultTerms, b.RemindersEnabled, b.ReminderDaysBefore, b.ReminderDaysAfter,
        b.PaystackSecretKeyProtected is not null, b.Tagline, b.RegistrationNumber, b.HeaderLine, b.SecondaryColor, b.LogoSize,
        b.FontStyle, b.DocumentTitle, b.ShowTaxId, b.ShowRegistrationNumber, b.ShowBankDetails, b.ShowSignature, b.SignatureName,
        b.SignatureRole, b.SignatureImageUrl, b.StampImageUrl, b.FooterText, await plans.HasFeatureAsync(LimitKeys.BrandImages, ct));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A letterhead text field on save: null or absent keeps the stored value; an empty string clears it.</summary>
    private static string? KeepOrClean(string? value, string? stored) => value is null ? stored : Clean(value);
}
