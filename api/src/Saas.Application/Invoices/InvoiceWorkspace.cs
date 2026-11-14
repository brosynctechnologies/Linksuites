using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed record PdfParty(string Name, IReadOnlyList<string> Lines);
public sealed record PdfLine(string Description, string Quantity, string UnitPrice, string? Discount, string? Tax, string Amount);
public sealed record PdfRow(string Label, string Value, bool Strong = false);

/// <summary>The signature block: name and role always print; the images only while the plan includes brand images.</summary>
public sealed record PdfSignature(string? Name, string? Role, string? ImageUrl, string? StampImageUrl);

/// <summary>
/// Everything a PDF design needs, already formatted and with the show switches and plan rules applied. Designs only do layout.
/// <see cref="Meta"/> starts with the document number; <see cref="Totals"/> are the rows above the emphasised
/// <see cref="Grand"/> figure (balance due on invoices, total on quotes, amount paid on receipts); <see cref="AfterGrand"/> are
/// ordinary rows printed under it (a receipt's balance remaining).
/// </summary>
public sealed record DocumentPdfModel(
    string Kind, string Title, string Number, string Template, string BrandColor, string? SecondaryColor, string FontStyle,
    string? LogoUrl, string LogoSize, string BusinessName, string? HeaderLineText, string? RegistrationText,
    IReadOnlyList<string> Contact, PdfParty Client, string ClientLabel, IReadOnlyList<PdfRow> Meta, IReadOnlyList<PdfLine> Lines,
    IReadOnlyList<PdfRow> Totals, PdfRow Grand, string? BankDetails, string? Notes, string? Terms, PdfSignature? Signature,
    string? FooterText, string? Stamp, bool ShowBranding, string AppName, IReadOnlyList<PdfRow>? AfterGrand = null)
{
    public const string Invoice = "invoice";
    public const string Quote = "quote";
    public const string Receipt = "receipt";
}

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

    /// <summary>Whether the logo, signature and stamp print on documents: only while the plan includes them (they stay stored).</summary>
    public Task<bool> BrandImagesAsync(CancellationToken ct) => plans.HasFeatureAsync(LimitKeys.BrandImages, ct);

    // Mapping

    public static ClientSnapshotDto ToDto(Guid? clientId, ClientSnapshot c) =>
        new(clientId, c.Name, c.Company, c.Email, c.Phone, c.Address, c.TaxId);

    public static LineDto ToDto(DocumentLine l) => new(l.Id, l.ItemId, l.Description, l.Quantity, l.UnitPriceMinor,
        l.DiscountPercent, l.TaxRateId, l.TaxName, l.TaxRatePercent, l.LineTotalMinor);

    /// <summary>The business as clients see it: switched-off details and (without the plan) images are left out.</summary>
    public static PublicBusinessDto ToPublic(BusinessSettings b, bool brandImages) =>
        new(b.LegalName, brandImages ? b.LogoUrl : null, b.Address, b.Email, b.Phone, b.ShowTaxId ? b.TaxId : null,
            b.ShowBankDetails ? b.BankDetails : null, b.BrandColor, b.Tagline,
            b.ShowRegistrationNumber || b.HeaderLine == "registration" ? b.RegistrationNumber : null,
            b.HeaderLine, b.SecondaryColor, b.LogoSize, b.FontStyle, b.DocumentTitle, b.ShowTaxId,
            b.ShowRegistrationNumber, b.ShowBankDetails, b.ShowSignature,
            b.ShowSignature ? b.SignatureName : null, b.ShowSignature ? b.SignatureRole : null, // hidden means not sent
            brandImages ? b.SignatureImageUrl : null, brandImages ? b.StampImageUrl : null, b.FooterText);

    public static string WhatsAppShare(ClientSnapshot client, string text, string country)
    {
        var phone = PhoneNumbers.Normalize(client.Phone, country);
        var encoded = Uri.EscapeDataString(text);
        return phone is null ? $"https://wa.me/?text={encoded}" : $"https://wa.me/{phone}?text={encoded}";
    }

    // PDF

    /// <summary>
    /// The PDF model of an invoice or quote. <paramref name="dates"/> follow the number in the meta block
    /// (issue date, then due date or valid until).
    /// </summary>
    public static DocumentPdfModel PdfModel(
        string kind, Document doc, IEnumerable<DocumentLine> lines, BusinessSettings business, IReadOnlyList<PdfRow> dates,
        string? stamp, bool showBranding, bool brandImages)
    {
        var c = doc.Currency;
        var isInvoice = kind == DocumentPdfModel.Invoice;
        var lineList = lines.OrderBy(l => l.Position).ToList();
        var totals = new List<PdfRow> { new("Subtotal", Money.Format(doc.SubtotalMinor, c)) };
        if (doc.DiscountTotalMinor > 0) totals.Add(new("Discount", Minus + Money.Format(doc.DiscountTotalMinor, c)));
        totals.AddRange(InvoiceMath.Taxes(lineList).Select(t => new PdfRow($"{t.Name} ({Percent(t.RatePercent)})", Money.Format(t.AmountMinor, c))));
        if (doc.ShippingMinor > 0) totals.Add(new("Shipping", Money.Format(doc.ShippingMinor, c)));

        PdfRow grand;
        if (doc is Invoice invoice && isInvoice)
        {
            totals.Add(new("Total", Money.Format(doc.TotalMinor, c), Strong: true));
            if (invoice.AmountPaidMinor > 0) totals.Add(new("Amount paid", Minus + Money.Format(invoice.AmountPaidMinor, c)));
            grand = new("Balance due", Money.Format(invoice.BalanceMinor, c), Strong: true);
        }
        else grand = new("Total", Money.Format(doc.TotalMinor, c), Strong: true);

        var client = new PdfParty(doc.Client.Name, Present(doc.Client.Company, doc.Client.Address, doc.Client.Email, doc.Client.Phone,
            doc.Client.TaxId is { Length: > 0 } ctid ? $"Tax ID: {ctid}" : null));
        var pdfLines = lineList.Select(l => new PdfLine(l.Description, Quantity(l.Quantity), Money.Format(l.UnitPriceMinor, c),
            l.DiscountPercent > 0 ? Percent(l.DiscountPercent) : null,
            l.TaxRatePercent > 0 ? $"{l.TaxName ?? "Tax"} {Percent(l.TaxRatePercent)}" : null,
            Money.Format(l.LineTotalMinor, c))).ToList();
        var meta = new List<PdfRow> { new(isInvoice ? "Invoice no." : "Quote no.", doc.Number, Strong: true) };
        meta.AddRange(dates);

        return Compose(kind, doc.Number, doc.Template, business, brandImages, client, isInvoice ? "Bill to" : "Prepared for", meta,
            pdfLines, totals, grand, isInvoice ? business.BankDetails : null, doc.Notes, doc.Terms, stamp, showBranding);
    }

    /// <summary>
    /// The PDF model of a payment receipt: laid out like an invoice in the invoice's design, titled "Receipt". It headlines the
    /// payment it acknowledges ("Amount paid"), with the balance remaining as an ordinary row under it.
    /// </summary>
    public static DocumentPdfModel ReceiptPdfModel(
        Invoice invoice, InvoicePayment payment, long paidBeforeMinor, BusinessSettings business, bool showBranding, bool brandImages)
    {
        var c = invoice.Currency;
        var amount = Money.Format(payment.AmountMinor, c);
        var meta = new List<PdfRow>
        {
            new("Receipt no.", payment.ReceiptNumber, Strong: true),
            new("Date paid", Date(payment.PaidAt)),
            new("Invoice", invoice.Number),
            new("Method", payment.Method == PaymentMethod.BankTransfer ? "Bank transfer" : payment.Method.ToString()),
        };
        if (payment.Reference is { Length: > 0 } reference) meta.Add(new("Reference", reference));
        var totals = new List<PdfRow> { new("Invoice total", Money.Format(invoice.TotalMinor, c)) };
        if (paidBeforeMinor > 0) totals.Add(new("Paid before", Minus + Money.Format(paidBeforeMinor, c)));
        var remaining = Math.Max(0, invoice.TotalMinor - paidBeforeMinor - payment.AmountMinor);
        var client = new PdfParty(invoice.Client.Name, Present(invoice.Client.Company, invoice.Client.Email));

        return Compose(DocumentPdfModel.Receipt, payment.ReceiptNumber, invoice.Template, business, brandImages, client, "Received from",
            meta, [new($"Payment for invoice {invoice.Number}", "1", amount, null, null, amount)], totals,
            new("Amount paid", amount, Strong: true), null, payment.Note, null, "PAID", showBranding)
            with { AfterGrand = [new("Balance remaining", Money.Format(remaining, c))] };
    }

    /// <summary>The minus sign (U+2212) for amounts taken off, as on the web documents.</summary>
    public const string Minus = "−";

    /// <summary>
    /// The letterhead and the rendering rules shared by every document: title, the line under the name, the contact row,
    /// the show switches (the signature block only when switched on, in every design) and the images (only with
    /// <paramref name="brandImages"/>).
    /// </summary>
    private static DocumentPdfModel Compose(
        string kind, string number, string template, BusinessSettings b, bool brandImages, PdfParty client, string clientLabel,
        IReadOnlyList<PdfRow> meta, IReadOnlyList<PdfLine> lines, IReadOnlyList<PdfRow> totals, PdfRow grand, string? bankDetails,
        string? notes, string? terms, string? stamp, bool showBranding)
    {
        var design = InvoiceTemplates.Exists(template) ? template : "classic";
        var headerLine = Letterhead.HeaderLines.Contains(b.HeaderLine) ? b.HeaderLine : "address";
        var address = OneLine(b.Address);
        var reg = Clean(b.RegistrationNumber) is { } r ? $"Reg. no. {r}" : null;
        var line = headerLine switch
        {
            "tagline" => Clean(b.Tagline),
            "address" => address,
            "registration" => reg,
            _ => null,
        };
        var regInLetterhead = headerLine == "registration" && line is not null;
        var taxId = b.ShowTaxId && Clean(b.TaxId) is { } tid ? $"Tax ID: {tid}" : null;
        var contact = Present(b.Email, b.Phone, headerLine == "address" && line is not null ? null : address, taxId,
            b.ShowRegistrationNumber && !regInLetterhead ? reg : null);

        var title = kind switch
        {
            DocumentPdfModel.Quote => "Quote",
            DocumentPdfModel.Receipt => "Receipt",
            _ => Letterhead.DocumentTitles.Contains(b.DocumentTitle) ? b.DocumentTitle : "Invoice",
        };
        var signature = b.ShowSignature
            ? new PdfSignature(Clean(b.SignatureName), Clean(b.SignatureRole),
                brandImages ? Clean(b.SignatureImageUrl) : null, brandImages ? Clean(b.StampImageUrl) : null)
            : null;

        return new DocumentPdfModel(
            kind, title, number, design, b.BrandColor, Clean(b.SecondaryColor),
            Letterhead.FontStyles.Contains(b.FontStyle) ? b.FontStyle : "modern",
            brandImages ? Clean(b.LogoUrl) : null, Letterhead.LogoSizes.Contains(b.LogoSize) ? b.LogoSize : "medium",
            Clean(b.LegalName) ?? "Your business", line, b.ShowRegistrationNumber && !regInLetterhead ? reg : null, contact,
            client, clientLabel, meta, lines, totals, grand,
            b.ShowBankDetails ? Clean(bankDetails) : null, Clean(notes), Clean(terms), signature, Clean(b.FooterText),
            stamp, showBranding, AppName);
    }

    /// <summary>An address on one line: line breaks become ", ".</summary>
    private static string? OneLine(string? value) =>
        Clean(value) is { } v ? string.Join(", ", v.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Date(DateOnly d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    public static string Percent(decimal p) => p.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    public static string Quantity(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);

    private static List<string> Present(params string?[] values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
}
