using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Core;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

/// <summary>Pages a business's clients open from a link: view, download, pay, accept a quote. No sign-in.</summary>
public sealed class PublicDocumentsService(
    IAppDbContext db,
    ITenantContext tenant,
    InvoiceWorkspace ws,
    InvoiceService invoices,
    QuoteService quotes,
    PlanService plans,
    IMerchantPayments payments,
    ISecretProtector secrets,
    NotificationQueue notifications,
    IDocumentPdfRenderer pdf,
    TimeProvider clock,
    ILogger<PublicDocumentsService> logger)
{
    public async Task<PublicInvoiceDto> GetInvoiceAsync(string token, CancellationToken ct)
    {
        var invoice = await ResolveInvoiceAsync(token, track: true, ct);
        if (invoice.Status != InvoiceStatus.Draft && invoice.ViewedAt is null)
        {
            invoice.ViewedAt = clock.GetUtcNow();
            if (invoice.Status == InvoiceStatus.Sent) invoice.Status = InvoiceStatus.Viewed;
            invoice.Events.Add(InvoiceService.Event(InvoiceEventType.Viewed));
            await db.SaveChangesAsync(ct);
        }
        return await ToPublicAsync(invoice, ct);
    }

    public async Task<(byte[] Pdf, string FileName)> InvoicePdfAsync(string token, CancellationToken ct)
    {
        var invoice = await ResolveInvoiceAsync(token, track: false, ct);
        return (await invoices.RenderAsync(invoice, await ws.BusinessAsync(ct), await ws.TodayAsync(ct), ct), $"{invoice.Number}.pdf");
    }

    public async Task<(byte[] Pdf, string FileName)> ReceiptPdfAsync(string token, Guid paymentId, CancellationToken ct)
    {
        var invoice = await ResolveInvoiceAsync(token, track: false, ct);
        var payment = invoice.Payments.FirstOrDefault(p => p.Id == paymentId) ?? throw new NotFoundException("Receipt");
        return (await invoices.RenderReceiptAsync(invoice, payment, await ws.BusinessAsync(ct), ct), $"{payment.ReceiptNumber}.pdf");
    }

    public async Task<PayResult> PayAsync(string token, PayRequest r, CancellationToken ct)
    {
        var invoice = await ResolveInvoiceAsync(token, track: false, ct);
        var business = await ws.BusinessAsync(ct);
        if (!await CanPayOnlineAsync(invoice, business, ct)) throw new ConflictException("This invoice can't be paid online.");
        var email = string.IsNullOrWhiteSpace(r.Email) ? invoice.Client.Email : r.Email.Trim();
        if (string.IsNullOrWhiteSpace(email)) throw BadRequestException.Field("email", "Enter your email address to pay.");

        // The reference names the invoice, so a payment for one invoice can never be applied to another.
        var reference = $"inv_{invoice.Id:N}_{Codes.Random(8).ToLowerInvariant()}";
        var url = await payments.InitializeAsync(secrets.Unprotect(business.PaystackSecretKeyProtected!), new PaymentInit(
            email, invoice.BalanceMinor, invoice.Currency, reference, ws.InvoiceUrl(invoice.PublicToken),
            new() { ["invoice"] = invoice.Number, ["tenantId"] = invoice.TenantId.ToString() }), ct);
        return new PayResult(url);
    }

    /// <summary>
    /// Called when the payer returns from Paystack. Asks Paystack directly whether the charge succeeded.
    /// <c>PaymentConfirmed</c> is true only when this charge is recorded on the invoice (now or by an earlier
    /// webhook) or the invoice is paid; a failed, abandoned, unknown or unverifiable charge gives false.
    /// </summary>
    public async Task<PublicInvoiceDto> VerifyAsync(string token, VerifyRequest r, CancellationToken ct)
    {
        var invoice = await ResolveInvoiceAsync(token, track: true, ct);
        var business = await ws.BusinessAsync(ct);
        var ours = r.Reference.StartsWith($"inv_{invoice.Id:N}_", StringComparison.Ordinal);
        // The webhook may have recorded this charge before the payer got back to the page.
        var confirmed = ours && invoice.Payments.Any(p => p.ProviderRef == r.Reference);
        if (ours && !confirmed && business.PaystackSecretKeyProtected is { } key)
        {
            VerifiedCharge? charge;
            try { charge = await payments.VerifyAsync(secrets.Unprotect(key), r.Reference, ct); }
            catch (HttpRequestException) { charge = null; } // Paystack unavailable: not confirmed yet
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { charge = null; } // Paystack timed out
            if (charge is not null) confirmed = await RecordOnlineAsync(invoice, business, charge, ct);
        }
        var page = await ToPublicAsync(invoice, ct);
        return page with { PaymentConfirmed = confirmed || invoice.Status == InvoiceStatus.Paid };
    }

    /// <summary>For a business's own Paystack webhook. Returns false if the signature is wrong.</summary>
    public async Task<bool> HandleWebhookAsync(Guid tenantId, string rawBody, string? signature,
        Func<string, string, string?, bool> isValidSignature, Func<string, VerifiedCharge?> parse, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Product == Products.Invoices, ct)) return false;
        tenant.SetTenant(tenantId);
        var business = await db.BusinessSettings.FirstOrDefaultAsync(ct);
        if (business?.PaystackSecretKeyProtected is not { } key) return false;
        if (!isValidSignature(secrets.Unprotect(key), rawBody, signature)) return false;

        if (parse(rawBody) is not { } charge) return true;
        var parts = charge.Reference.Split('_');
        if (parts.Length != 3 || parts[0] != "inv" || !Guid.TryParseExact(parts[1], "N", out var invoiceId)) return true;
        var invoice = await db.Invoices.Include(i => i.Items).Include(i => i.Payments).Include(i => i.Events)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice is not null && invoice.Payments.All(p => p.ProviderRef != charge.Reference))
            await RecordOnlineAsync(invoice, business, charge, ct);
        return true;
    }

    /// <summary>Records a verified charge. Returns false only when it was ignored (wrong currency).</summary>
    private async Task<bool> RecordOnlineAsync(Invoice invoice, BusinessSettings business, VerifiedCharge charge, CancellationToken ct)
    {
        if (!string.Equals(charge.Currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Ignored payment {Reference}: currency {Currency} does not match invoice {Number}", charge.Reference, charge.Currency, invoice.Number);
            return false;
        }
        // The amount was fixed when the payer pressed Pay, so the invoice may have been paid another way since.
        // The money has been taken, so it is still recorded, but the overpayment is flagged for the business.
        var balanceBefore = Math.Max(0, invoice.TotalMinor - invoice.AmountPaidMinor);
        var overpaidMinor = charge.AmountMinor - balanceBefore;
        var payment = await invoices.AddPaymentAsync(invoice, charge.AmountMinor, PaymentMethod.Online, charge.Reference,
            DateOnly.FromDateTime(charge.PaidAt.UtcDateTime), null, charge.Reference, ct);
        if (overpaidMinor > 0)
        {
            var overpaid = Money.Format(overpaidMinor, invoice.Currency);
            invoice.Events.Add(InvoiceService.Event(InvoiceEventType.Overpaid,
                $"Overpaid by {overpaid}: online payment {charge.Reference} arrived after the balance was {Money.Format(balanceBefore, invoice.Currency)}"));
            logger.LogWarning("Invoice {Number} overpaid by {Amount} with {Reference}", invoice.Number, overpaid, charge.Reference);
        }
        invoices.QueueReceipt(invoice, payment, business);
        if (!string.IsNullOrWhiteSpace(business.Email))
            notifications.Email(business.Email, "invoice.paid", new()
            {
                ["number"] = invoice.Number,
                ["client"] = invoice.Client.Name,
                ["amount"] = Money.Format(charge.AmountMinor, invoice.Currency),
                ["balance"] = Money.Format(invoice.BalanceMinor, invoice.Currency),
                ["overpaid"] = overpaidMinor > 0 ? Money.Format(overpaidMinor, invoice.Currency) : "",
            }, invoice.TenantId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The webhook and the return page can race; the unique provider reference makes the second one fail harmlessly.
            logger.LogInformation(ex, "Payment {Reference} was already recorded", charge.Reference);
        }
        return true;
    }

    // Quotes

    public async Task<PublicQuoteDto> GetQuoteAsync(string token, CancellationToken ct)
    {
        var quote = await ResolveQuoteAsync(token, track: true, ct);
        if (quote.Status != QuoteStatus.Draft && quote.ViewedAt is null)
        {
            quote.ViewedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return await ToPublicAsync(quote, ct);
    }

    public async Task<PublicQuoteDto> RespondAsync(string token, bool accept, CancellationToken ct)
    {
        var quote = await ResolveQuoteAsync(token, track: true, ct);
        if (quote.DisplayStatus(await ws.TodayAsync(ct)) != QuoteStatus.Sent)
            throw new ConflictException("This quote can no longer be accepted or declined.");
        quote.Status = accept ? QuoteStatus.Accepted : QuoteStatus.Declined;
        quote.RespondedAt = clock.GetUtcNow();
        var business = await ws.BusinessAsync(ct);
        if (!string.IsNullOrWhiteSpace(business.Email))
            notifications.Email(business.Email, "quote.response", new()
            {
                ["number"] = quote.Number,
                ["client"] = quote.Client.Name,
                ["response"] = accept ? "accepted" : "declined",
                ["total"] = Money.Format(quote.TotalMinor, quote.Currency),
            }, quote.TenantId);
        await db.SaveChangesAsync(ct);
        return await ToPublicAsync(quote, ct);
    }

    public async Task<(byte[] Pdf, string FileName)> QuotePdfAsync(string token, CancellationToken ct)
    {
        var quote = await ResolveQuoteAsync(token, track: false, ct);
        return (await quotes.RenderAsync(quote, await ws.BusinessAsync(ct), await ws.TodayAsync(ct), ct), $"{quote.Number}.pdf");
    }

    // Free generator

    /// <summary>Renders an invoice that is never saved, for the no-signup generator. Free templates only, always branded.</summary>
    public async Task<byte[]> PreviewAsync(PreviewRequest r, CancellationToken ct)
    {
        var currency = r.Currency is { } cur && Money.SupportedCurrencies.Contains(cur) ? cur.ToUpperInvariant() : "NGN";
        var template = r.Template is { } t && InvoiceTemplates.Exists(t) && !InvoiceTemplates.IsPremium(t) ? t : "classic";
        var color = r.Business?.BrandColor is { Length: 7 } bc && bc.StartsWith('#') ? bc : "#2563eb";
        var issue = r.IssueDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var invoice = new Invoice
        {
            Number = string.IsNullOrWhiteSpace(r.Number) ? "INV-0001" : r.Number.Trim()[..Math.Min(r.Number.Trim().Length, 30)],
            Currency = currency,
            PublicToken = "",
            IssueDate = issue,
            DueDate = r.DueDate ?? issue.AddDays(14),
            Template = template,
            ShippingMinor = r.ShippingMinor,
            Notes = r.Notes,
            Terms = r.Terms,
            Client = new ClientSnapshot
            {
                Name = string.IsNullOrWhiteSpace(r.Client?.Name) ? "Client" : r.Client.Name.Trim(),
                Company = r.Client?.Company, Email = r.Client?.Email, Phone = r.Client?.Phone, Address = r.Client?.Address,
            },
        };
        var lines = (r.Items ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Description) || l.UnitPriceMinor > 0).Select((l, i) =>
            (DocumentLine)new InvoiceItem
            {
                Description = string.IsNullOrWhiteSpace(l.Description) ? "Item" : l.Description.Trim(),
                Quantity = l.Quantity, UnitPriceMinor = l.UnitPriceMinor, DiscountPercent = l.DiscountPercent,
                TaxRatePercent = l.TaxRatePercent, TaxName = l.TaxRatePercent > 0 ? "Tax" : null, Position = i,
            }).ToList();
        InvoiceMath.Apply(invoice, lines);
        var pb = r.Business;
        var business = new BusinessSettings
        {
            LegalName = string.IsNullOrWhiteSpace(pb?.Name) ? "Your business" : pb.Name.Trim(),
            Address = pb?.Address, Email = pb?.Email, Phone = pb?.Phone, TaxId = pb?.TaxId,
            BankDetails = pb?.BankDetails, BrandColor = color,
            // The free letterhead fields only; the generator never prints images.
            Tagline = Text(pb?.Tagline), RegistrationNumber = Text(pb?.RegistrationNumber),
            HeaderLine = pb?.HeaderLine ?? (string.IsNullOrWhiteSpace(pb?.Address) ? "none" : "address"),
            SecondaryColor = Text(pb?.SecondaryColor)?.ToLowerInvariant(), FontStyle = pb?.FontStyle ?? "modern",
            DocumentTitle = pb?.DocumentTitle ?? "Invoice", ShowSignature = pb?.ShowSignature ?? false,
            SignatureName = Text(pb?.SignatureName), SignatureRole = Text(pb?.SignatureRole), FooterText = Text(pb?.FooterText),
        };
        var dates = new List<PdfRow>
        {
            new("Issue date", InvoiceWorkspace.Date(invoice.IssueDate)),
            new("Due date", InvoiceWorkspace.Date(invoice.DueDate)),
        };
        return await pdf.RenderAsync(InvoiceWorkspace.PdfModel(DocumentPdfModel.Invoice, invoice, lines, business, dates, null,
            showBranding: true, brandImages: false), ct);
    }

    // Helpers

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<bool> CanPayOnlineAsync(Invoice invoice, BusinessSettings business, CancellationToken ct) =>
        business.PaystackSecretKeyProtected is not null
        && invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Void or InvoiceStatus.Paid)
        && invoice.BalanceMinor > 0
        && await plans.HasFeatureAsync(LimitKeys.PaymentLinks, ct);

    private async Task<Invoice> ResolveInvoiceAsync(string token, bool track, CancellationToken ct)
    {
        var query = db.Invoices.IgnoreQueryFilters().Include(i => i.Items).Include(i => i.Payments).Include(i => i.Events);
        var invoice = await (track ? query : query.AsNoTracking())
            .FirstOrDefaultAsync(i => i.PublicToken == token && db.Tenants.Any(t => t.Id == i.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Invoice");
        tenant.SetTenant(invoice.TenantId);
        return invoice;
    }

    private async Task<Quote> ResolveQuoteAsync(string token, bool track, CancellationToken ct)
    {
        var query = db.Quotes.IgnoreQueryFilters().Include(q => q.Items);
        var quote = await (track ? query : query.AsNoTracking())
            .FirstOrDefaultAsync(q => q.PublicToken == token && db.Tenants.Any(t => t.Id == q.TenantId && t.Status == TenantStatus.Active), ct)
            ?? throw new NotFoundException("Quote");
        tenant.SetTenant(quote.TenantId);
        return quote;
    }

    private async Task<PublicInvoiceDto> ToPublicAsync(Invoice i, CancellationToken ct)
    {
        var business = await ws.BusinessAsync(ct);
        var today = await ws.TodayAsync(ct);
        var items = i.Items.OrderBy(l => l.Position).ToList();
        return new PublicInvoiceDto(i.Number, i.DisplayStatus(today), InvoiceWorkspace.ToPublic(business, await ws.BrandImagesAsync(ct)),
            InvoiceWorkspace.ToDto(null, i.Client), i.IssueDate, i.DueDate, i.Currency, i.Template,
            items.Select(InvoiceWorkspace.ToDto).ToList(), i.SubtotalMinor, i.DiscountTotalMinor, i.TaxTotalMinor,
            InvoiceMath.Taxes(items), i.ShippingMinor, i.TotalMinor, i.AmountPaidMinor, i.BalanceMinor, i.Notes, i.Terms,
            await CanPayOnlineAsync(i, business, ct),
            i.Payments.OrderBy(p => p.PaidAt).Select(p => new PublicPaymentDto(p.Id, p.AmountMinor, p.Method, p.PaidAt, p.ReceiptNumber)).ToList(),
            await ws.ShowBrandingAsync(ct));
    }

    private async Task<PublicQuoteDto> ToPublicAsync(Quote q, CancellationToken ct)
    {
        var business = await ws.BusinessAsync(ct);
        var status = q.DisplayStatus(await ws.TodayAsync(ct));
        var items = q.Items.OrderBy(l => l.Position).ToList();
        return new PublicQuoteDto(q.Number, status, InvoiceWorkspace.ToPublic(business, await ws.BrandImagesAsync(ct)), InvoiceWorkspace.ToDto(null, q.Client),
            q.IssueDate, q.ExpiryDate, q.Currency, q.Template, items.Select(InvoiceWorkspace.ToDto).ToList(), q.SubtotalMinor,
            q.DiscountTotalMinor, q.TaxTotalMinor, InvoiceMath.Taxes(items), q.ShippingMinor, q.TotalMinor, q.Notes, q.Terms,
            status == QuoteStatus.Sent, await ws.ShowBrandingAsync(ct));
    }
}
