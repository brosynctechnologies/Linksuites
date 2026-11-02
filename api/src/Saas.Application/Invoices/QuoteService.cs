using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed class QuoteService(
    IAppDbContext db,
    InvoiceWorkspace ws,
    InvoiceService invoices,
    NotificationQueue notifications,
    IDocumentPdfRenderer pdf,
    ICurrentUser user)
{
    public async Task<Paged<QuoteSummaryDto>> ListAsync(QuoteStatus? status, string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = await ws.TodayAsync(ct);
        var query = db.Quotes.AsNoTracking();
        query = status switch
        {
            null => query,
            QuoteStatus.Expired => query.Where(x => (x.Status == QuoteStatus.Draft || x.Status == QuoteStatus.Sent) && x.ExpiryDate < today),
            QuoteStatus.Draft or QuoteStatus.Sent => query.Where(x => x.Status == status && x.ExpiryDate >= today),
            _ => query.Where(x => x.Status == status),
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(x => x.Number.ToLower().Contains(term) || x.Client.Name.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<QuoteSummaryDto>(rows.Select(x => new QuoteSummaryDto(x.Id, x.Number, x.Client.Name, x.IssueDate, x.ExpiryDate,
            x.Currency, x.TotalMinor, x.DisplayStatus(today), x.CreatedAt)).ToList(), page, pageSize, total);
    }

    public async Task<QuoteDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadAsync(id, track: false, ct), ct);

    public async Task<QuoteDto> CreateAsync(SaveQuoteRequest r, CancellationToken ct)
    {
        var business = await ws.BusinessAsync(ct);
        var quote = new Quote
        {
            Number = await ws.NextNumberAsync(DocumentCounter.Quote, business.QuotePrefix, ct),
            Currency = r.Currency.ToUpperInvariant(),
            PublicToken = InvoiceWorkspace.NewPublicToken(),
        };
        await FillAsync(quote, r, business, ct);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);
        return await GetAsync(quote.Id, ct);
    }

    public async Task<QuoteDto> UpdateAsync(Guid id, SaveQuoteRequest r, CancellationToken ct)
    {
        var quote = await LoadAsync(id, track: true, ct);
        if (quote.Status == QuoteStatus.Converted) throw new ConflictException("This quote is already an invoice. Edit the invoice instead.");
        await FillAsync(quote, r, await ws.BusinessAsync(ct), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var quote = await LoadAsync(id, track: true, ct);
        if (quote.Status == QuoteStatus.Converted) throw new ConflictException("This quote became an invoice and can't be deleted.");
        db.Quotes.Remove(quote);
        await db.SaveChangesAsync(ct);
    }

    public async Task<QuoteDto> SendAsync(Guid id, SendRequest r, CancellationToken ct)
    {
        var quote = await LoadAsync(id, track: true, ct);
        var to = string.IsNullOrWhiteSpace(r.To) ? quote.Client.Email : r.To.Trim();
        if (string.IsNullOrWhiteSpace(to)) throw BadRequestException.Field("to", "Add an email address for this client first.");
        var business = await ws.BusinessAsync(ct);
        notifications.Email(to, "quote.sent", new()
        {
            ["business"] = business.LegalName,
            ["client"] = quote.Client.Name,
            ["number"] = quote.Number,
            ["total"] = Money.Format(quote.TotalMinor, quote.Currency),
            ["expires"] = InvoiceWorkspace.Date(quote.ExpiryDate),
            ["link"] = ws.QuoteUrl(quote.PublicToken),
            ["message"] = r.Message?.Trim() ?? "",
        }, quote.TenantId, user.UserId);
        MarkSent(quote);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<QuoteDto> MarkSentAsync(Guid id, CancellationToken ct)
    {
        var quote = await LoadAsync(id, track: true, ct);
        MarkSent(quote);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Turns the quote into a draft invoice with the same lines, dated today.</summary>
    public async Task<InvoiceDto> ConvertAsync(Guid id, CancellationToken ct)
    {
        var quote = await LoadAsync(id, track: true, ct);
        if (quote.Status == QuoteStatus.Converted && quote.ConvertedInvoiceId is { } existing) return await invoices.GetAsync(existing, ct);
        if (quote.Status == QuoteStatus.Declined) throw new ConflictException("The client declined this quote.");
        await invoices.EnsureMonthlyLimitAsync(ct);

        var business = await ws.BusinessAsync(ct);
        var today = await ws.TodayAsync(ct);
        var invoice = new Invoice
        {
            Number = await ws.NextNumberAsync(DocumentCounter.Invoice, business.InvoicePrefix, ct),
            PublicToken = InvoiceWorkspace.NewPublicToken(),
            ClientId = quote.ClientId,
            Client = Copy(quote.Client),
            IssueDate = today,
            DueDate = today.AddDays(business.PaymentTermsDays),
            Currency = quote.Currency,
            ExchangeRate = await ws.RateOrAsync(quote.Currency, today, business, quote.ExchangeRate, ct),
            Template = quote.Template,
            ShippingMinor = quote.ShippingMinor,
            Notes = quote.Notes,
            Terms = quote.Terms,
            QuoteId = quote.Id,
        };
        // Copy lines as they are, so tax names and rates match what the client accepted.
        foreach (var l in quote.Items.OrderBy(l => l.Position))
        {
            invoice.Items.Add(new InvoiceItem
            {
                InvoiceId = invoice.Id, ItemId = l.ItemId, Description = l.Description, Quantity = l.Quantity,
                UnitPriceMinor = l.UnitPriceMinor, DiscountPercent = l.DiscountPercent, TaxRateId = l.TaxRateId,
                TaxName = l.TaxName, TaxRatePercent = l.TaxRatePercent, Position = l.Position,
            });
        }
        InvoiceMath.Apply(invoice, invoice.Items.Cast<DocumentLine>().ToList());
        InvoiceMath.ApplyPayments(invoice, DateTimeOffset.UtcNow);
        invoice.Events.Add(InvoiceService.Event(InvoiceEventType.Created, $"From quote {quote.Number}"));
        db.Invoices.Add(invoice);

        quote.Status = QuoteStatus.Converted;
        quote.ConvertedInvoiceId = invoice.Id;
        await db.SaveChangesAsync(ct);
        return await invoices.GetAsync(invoice.Id, ct);
    }

    public async Task<(byte[] Pdf, string FileName)> PdfAsync(Guid id, CancellationToken ct)
    {
        var quote = await LoadAsync(id, track: false, ct);
        return (await RenderAsync(quote, await ws.BusinessAsync(ct), await ws.TodayAsync(ct), ct), $"{quote.Number}.pdf");
    }

    internal async Task<byte[]> RenderAsync(Quote quote, BusinessSettings business, DateOnly today, CancellationToken ct)
    {
        var meta = new List<PdfRow>
        {
            new("Quote date", InvoiceWorkspace.Date(quote.IssueDate)),
            new("Valid until", InvoiceWorkspace.Date(quote.ExpiryDate)),
            new("Total", Money.Format(quote.TotalMinor, quote.Currency), Strong: true),
        };
        var stamp = quote.DisplayStatus(today) switch
        {
            QuoteStatus.Accepted or QuoteStatus.Converted => "ACCEPTED",
            QuoteStatus.Declined => "DECLINED",
            QuoteStatus.Expired => "EXPIRED",
            _ => null,
        };
        var model = InvoiceWorkspace.PdfModel("QUOTE", quote, quote.Items, business, meta, [], stamp, await ws.ShowBrandingAsync(ct));
        return await pdf.RenderAsync(model, ct);
    }

    private async Task FillAsync(Quote quote, SaveQuoteRequest r, BusinessSettings business, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClientId, ct)
            ?? throw BadRequestException.Field("clientId", "That client does not exist.");
        quote.ClientId = client.Id;
        quote.Client = ClientSnapshot.From(client);
        quote.IssueDate = r.IssueDate;
        quote.ExpiryDate = r.ExpiryDate;
        quote.Currency = r.Currency.ToUpperInvariant();
        quote.ExchangeRate = await ws.ResolveRateAsync(quote.Currency, r.ExchangeRate, r.IssueDate, business, ct);
        quote.Template = await ws.ResolveTemplateAsync(r.Template, business, ct);
        quote.ShippingMinor = r.ShippingMinor;
        quote.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        quote.Terms = string.IsNullOrWhiteSpace(r.Terms) ? null : r.Terms.Trim();
        var lines = await ws.BuildLinesAsync<QuoteItem>(r.Items, ct);
        quote.Items.Clear();
        foreach (var line in lines)
        {
            line.QuoteId = quote.Id;
            quote.Items.Add(line);
        }
        InvoiceMath.Apply(quote, quote.Items.Cast<DocumentLine>().ToList());
    }

    private static void MarkSent(Quote quote)
    {
        quote.SentAt ??= DateTimeOffset.UtcNow;
        if (quote.Status == QuoteStatus.Draft) quote.Status = QuoteStatus.Sent;
    }

    private static ClientSnapshot Copy(ClientSnapshot c) =>
        new() { Name = c.Name, Company = c.Company, Email = c.Email, Phone = c.Phone, Address = c.Address, TaxId = c.TaxId };

    private async Task<Quote> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = db.Quotes.Include(x => x.Items);
        return await (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Quote");
    }

    private async Task<QuoteDto> ToDtoAsync(Quote x, CancellationToken ct)
    {
        var today = await ws.TodayAsync(ct);
        var business = await ws.BusinessAsync(ct);
        var url = ws.QuoteUrl(x.PublicToken);
        var share = $"Hi {x.Client.Name}, here is quote {x.Number} from {business.LegalName} for " +
                    $"{Money.Format(x.TotalMinor, x.Currency)}, valid until {InvoiceWorkspace.Date(x.ExpiryDate)}: {url}";
        var items = x.Items.OrderBy(l => l.Position).ToList();
        return new QuoteDto(x.Id, x.Number, x.DisplayStatus(today), InvoiceWorkspace.ToDto(x.ClientId, x.Client), x.IssueDate, x.ExpiryDate,
            x.Currency, x.Template, items.Select(InvoiceWorkspace.ToDto).ToList(), x.SubtotalMinor, x.DiscountTotalMinor, x.TaxTotalMinor,
            InvoiceMath.Taxes(items), x.ShippingMinor, x.TotalMinor, x.Notes, x.Terms, url,
            InvoiceWorkspace.WhatsAppShare(x.Client, share, await ws.CountryAsync(ct)), x.SentAt, x.ViewedAt, x.ConvertedInvoiceId, x.CreatedAt,
            business.Currency, x.ExchangeRate, ExchangeRateService.ToBase(x.TotalMinor, x.ExchangeRate));
    }
}
