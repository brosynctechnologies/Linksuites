using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed record PdfParty(string Name, IReadOnlyList<string> Lines);
public sealed record PdfLine(string Description, string Quantity, string UnitPrice, string? Discount, string? Tax, string Amount);
public sealed record PdfRow(string Label, string Value, bool Strong = false);

/// <summary>Everything a PDF template needs, already formatted. Templates only do layout.</summary>
public sealed record DocumentPdfModel(
    string Title, string Number, string Template, string BrandColor, string? LogoUrl, PdfParty Business, PdfParty Client,
    IReadOnlyList<PdfRow> Meta, IReadOnlyList<PdfLine> Lines, IReadOnlyList<PdfRow> Totals, string? BankDetails,
    string? Notes, string? Terms, string? Stamp, bool ShowBranding, string AppName);

public interface IDocumentPdfRenderer
{
    Task<byte[]> RenderAsync(DocumentPdfModel model, CancellationToken ct);
}

/// <summary>Helpers shared by the invoice, quote, recurring and public services.</summary>
public sealed class InvoiceWorkspace(
    IAppDbContext db, ITenantContext tenant, PlanService plans, AppUrls urls, ExchangeRateService fx, TimeProvider clock)
{
    public const string AppName = "InvoiceLink";

    public async Task<BusinessSettings> BusinessAsync(CancellationToken ct)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        return await db.BusinessSettings.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Business profile");
    }

    /// <summary>Today's date in the business's time zone, so "overdue" flips at their midnight.</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var timezone = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Timezone).FirstAsync(ct);
        return Today(clock.GetUtcNow(), timezone);
    }

    /// <summary>The first instant of this month in the business's time zone (for monthly plan limits).</summary>
    public async Task<DateTimeOffset> MonthStartUtcAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        var timezone = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Timezone).FirstAsync(ct);
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var found) ? found : TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz);
        var start = new DateTime(local.Year, local.Month, 1);
        return new DateTimeOffset(start, tz.GetUtcOffset(start)).ToUniversalTime();
    }

    public async Task<string> CountryAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        return await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Country).FirstAsync(ct);
    }

    public static DateOnly Today(DateTimeOffset nowUtc, string timezone)
    {
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime);
    }

    public async Task<string> NextNumberAsync(DocumentCounter counter, string prefix, CancellationToken ct) =>
        $"{prefix}{(await db.NextDocumentNumberAsync(counter, ct)).ToString("D4", CultureInfo.InvariantCulture)}";

    public string InvoiceUrl(string token) => $"{urls.Web(Products.Invoices)}/i/{token}";
    public string QuoteUrl(string token) => $"{urls.Web(Products.Invoices)}/q/{token}";

    public static string NewPublicToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Rate from a document currency to the business base currency: 1 for the same currency, the user's own rate
    /// when given, otherwise the stored rate for that date.
    /// </summary>
    public async Task<decimal> ResolveRateAsync(string currency, decimal? manual, DateOnly date, BusinessSettings business, CancellationToken ct)
    {
        if (string.Equals(currency, business.Currency, StringComparison.OrdinalIgnoreCase)) return 1;
        if (manual is > 0) return Math.Round(manual.Value, 8);
        return await fx.RateAsync(currency, business.Currency, date, ct);
    }

    /// <summary>Like <see cref="ResolveRateAsync"/>, but falls back to <paramref name="fallback"/> when no rate is available.</summary>
    public async Task<decimal> RateOrAsync(string currency, DateOnly date, BusinessSettings business, decimal fallback, CancellationToken ct)
    {
        try { return await ResolveRateAsync(currency, null, date, business, ct); }
        catch (FxUnavailableException) { return fallback; }
    }

    /// <summary>Premium templates need a plan that includes them.</summary>
    public async Task<string> ResolveTemplateAsync(string? requested, BusinessSettings business, CancellationToken ct)
    {
        var template = string.IsNullOrWhiteSpace(requested) ? business.DefaultTemplate : requested;
        if (InvoiceTemplates.IsPremium(template) && !await plans.HasFeatureAsync(LimitKeys.PremiumTemplates, ct))
            throw new PlanLimitException("Premium templates are part of the Pro plan. Upgrade to use them.");
        return template;
    }

    /// <summary>Builds document lines from a request, looking tax rates up so names and rates are copied onto the line.</summary>
    public async Task<List<TLine>> BuildLinesAsync<TLine>(IReadOnlyList<LineRequest> requests, CancellationToken ct)
        where TLine : DocumentLine, new()
    {
        var taxIds = requests.Where(r => r.TaxRateId is not null).Select(r => r.TaxRateId!.Value).Distinct().ToList();
        var taxes = await db.TaxRates.AsNoTracking().Where(t => taxIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        if (taxes.Count != taxIds.Count) throw BadRequestException.Field("items", "A tax rate on one of the lines no longer exists.");

        return requests.Select((r, i) =>
        {
            var tax = r.TaxRateId is { } id ? taxes[id] : null;
            return new TLine
            {
                ItemId = r.ItemId,
                Description = r.Description.Trim(),
                Quantity = r.Quantity,
                UnitPriceMinor = r.UnitPriceMinor,
                DiscountPercent = r.DiscountPercent,
                TaxRateId = tax?.Id,
                TaxName = tax?.Name,
                TaxRatePercent = tax?.RatePercent ?? 0,
                Position = i,
            };
        }).ToList();
    }

    public async Task<bool> ShowBrandingAsync(CancellationToken ct) => !await plans.HasFeatureAsync(LimitKeys.RemoveBranding, ct);

    // Mapping

    public static ClientSnapshotDto ToDto(Guid? clientId, ClientSnapshot c) =>
        new(clientId, c.Name, c.Company, c.Email, c.Phone, c.Address, c.TaxId);

    public static LineDto ToDto(DocumentLine l) => new(l.Id, l.ItemId, l.Description, l.Quantity, l.UnitPriceMinor,
        l.DiscountPercent, l.TaxRateId, l.TaxName, l.TaxRatePercent, l.LineTotalMinor);

    public static PublicBusinessDto ToPublic(BusinessSettings b) =>
        new(b.LegalName, b.LogoUrl, b.Address, b.Email, b.Phone, b.TaxId, b.BankDetails, b.BrandColor);

    public static string WhatsAppShare(ClientSnapshot client, string text, string country)
    {
        var phone = PhoneNumbers.Normalize(client.Phone, country);
        var encoded = Uri.EscapeDataString(text);
        return phone is null ? $"https://wa.me/?text={encoded}" : $"https://wa.me/{phone}?text={encoded}";
    }

    // PDF

    public static DocumentPdfModel PdfModel(
        string title, Document doc, IEnumerable<DocumentLine> lines, BusinessSettings business, IReadOnlyList<PdfRow> meta,
        IReadOnlyList<PdfRow> extraTotals, string? stamp, bool showBranding)
    {
        var c = doc.Currency;
        var lineList = lines.OrderBy(l => l.Position).ToList();
        var totals = new List<PdfRow> { new("Subtotal", Money.Format(doc.SubtotalMinor, c)) };
        if (doc.DiscountTotalMinor > 0) totals.Add(new("Discount", "-" + Money.Format(doc.DiscountTotalMinor, c)));
        totals.AddRange(InvoiceMath.Taxes(lineList).Select(t => new PdfRow($"{t.Name} ({Percent(t.RatePercent)})", Money.Format(t.AmountMinor, c))));
        if (doc.ShippingMinor > 0) totals.Add(new("Shipping", Money.Format(doc.ShippingMinor, c)));
        totals.Add(new("Total", Money.Format(doc.TotalMinor, c), Strong: true));
        totals.AddRange(extraTotals);

        return new DocumentPdfModel(
            title, doc.Number, doc.Template, business.BrandColor, business.LogoUrl,
            new PdfParty(business.LegalName, Present(business.Address, business.Email, business.Phone,
                business.TaxId is { Length: > 0 } tid ? $"Tax ID: {tid}" : null)),
            new PdfParty(doc.Client.Name, Present(doc.Client.Company, doc.Client.Address, doc.Client.Email, doc.Client.Phone,
                doc.Client.TaxId is { Length: > 0 } ctid ? $"Tax ID: {ctid}" : null)),
            meta,
            lineList.Select(l => new PdfLine(l.Description, Quantity(l.Quantity), Money.Format(l.UnitPriceMinor, c),
                l.DiscountPercent > 0 ? Percent(l.DiscountPercent) : null,
                l.TaxRatePercent > 0 ? Percent(l.TaxRatePercent) : null,
                Money.Format(l.LineTotalMinor, c))).ToList(),
            totals, business.BankDetails, doc.Notes, doc.Terms, stamp, showBranding, AppName);
    }

    public static string Date(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    public static string Percent(decimal p) => p.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    public static string Quantity(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);

    private static List<string> Present(params string?[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
}
