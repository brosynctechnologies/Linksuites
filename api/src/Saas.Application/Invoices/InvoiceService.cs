using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Domain.Invoices;

namespace Saas.Application.Invoices;

public sealed class InvoiceService(
    IAppDbContext db,
    InvoiceWorkspace ws,
    PlanService plans,
    NotificationQueue notifications,
    IDocumentPdfRenderer pdf,
    ICurrentUser user,
    TimeProvider clock)
{
    private static readonly InvoiceStatus[] Open = [InvoiceStatus.Sent, InvoiceStatus.Viewed, InvoiceStatus.PartiallyPaid];

    public async Task<Paged<InvoiceSummaryDto>> ListAsync(
        InvoiceStatus? status, Guid? clientId, string? q, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var today = await ws.TodayAsync(ct);
        var query = Filter(db.Invoices.AsNoTracking(), status, today);
        if (clientId is { } cid) query = query.Where(i => i.ClientId == cid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(i => i.Number.ToLower().Contains(term) || i.Client.Name.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new Paged<InvoiceSummaryDto>(rows.Select(i => Summary(i, today)).ToList(), page, pageSize, total);
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await LoadAsync(id, track: false, ct), ct);

    public async Task<InvoiceDto> CreateAsync(SaveInvoiceRequest r, CancellationToken ct)
    {
        await EnsureMonthlyLimitAsync(ct);
        var business = await ws.BusinessAsync(ct);
        var invoice = new Invoice
        {
            Number = await ws.NextNumberAsync(DocumentCounter.Invoice, business.InvoicePrefix, ct),
            Currency = r.Currency.ToUpperInvariant(),
            PublicToken = InvoiceWorkspace.NewPublicToken(),
        };
        await FillAsync(invoice, r, business, ct);
        invoice.Events.Add(Event(InvoiceEventType.Created));
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return await GetAsync(invoice.Id, ct);
    }

    public async Task<InvoiceDto> UpdateAsync(Guid id, SaveInvoiceRequest r, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status == InvoiceStatus.Void) throw new ConflictException("A void invoice can't be edited. Duplicate it instead.");
        var business = await ws.BusinessAsync(ct);
        await FillAsync(invoice, r, business, ct);
        if (invoice.TotalMinor < invoice.AmountPaidMinor)
            throw BadRequestException.Field("items", "The new total is less than what has already been paid. Remove a payment first.");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status != InvoiceStatus.Draft)
            throw new ConflictException("Only drafts can be deleted. Void the invoice instead so your records stay complete.");
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
    }

    public async Task<InvoiceDto> SendAsync(Guid id, SendRequest r, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status == InvoiceStatus.Void) throw new ConflictException("A void invoice can't be sent.");
        var to = string.IsNullOrWhiteSpace(r.To) ? invoice.Client.Email : r.To.Trim();
        if (string.IsNullOrWhiteSpace(to)) throw BadRequestException.Field("to", "Add an email address for this client first.");
        var business = await ws.BusinessAsync(ct);

        notifications.Email(to, "invoice.sent", new()
        {
            ["business"] = business.LegalName,
            ["client"] = invoice.Client.Name,
            ["number"] = invoice.Number,
            ["total"] = Money.Format(invoice.BalanceMinor, invoice.Currency),
            ["due"] = InvoiceWorkspace.Date(invoice.DueDate),
            ["link"] = ws.InvoiceUrl(invoice.PublicToken),
            ["message"] = r.Message?.Trim() ?? "",
            ["replyTo"] = business.Email ?? "",
        }, invoice.TenantId, user.UserId);
        MarkSent(invoice, $"Emailed to {to}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> MarkSentAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status == InvoiceStatus.Void) throw new ConflictException("A void invoice can't be sent.");
        MarkSent(invoice, "Shared");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> VoidAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status != InvoiceStatus.Void)
        {
            invoice.Status = InvoiceStatus.Void;
            invoice.Events.Add(Event(InvoiceEventType.Voided));
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> DuplicateAsync(Guid id, CancellationToken ct)
    {
        var source = await LoadAsync(id, track: false, ct);
        var business = await ws.BusinessAsync(ct);
        var today = await ws.TodayAsync(ct);
        var request = new SaveInvoiceRequest
        {
            ClientId = source.ClientId ?? throw new ConflictException("The client for this invoice was deleted. Create a new invoice instead."),
            IssueDate = today,
            DueDate = today.AddDays(business.PaymentTermsDays),
            Currency = source.Currency,
            Template = source.Template,
            Items = source.Items.OrderBy(l => l.Position).Select(ToRequest).ToList(),
            ShippingMinor = source.ShippingMinor,
            Notes = source.Notes,
            Terms = source.Terms,
        };
        return await CreateAsync(request, ct);
    }

    public async Task<InvoiceDto> RecordPaymentAsync(Guid id, RecordPaymentRequest r, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        if (invoice.Status == InvoiceStatus.Void) throw new ConflictException("You can't record a payment on a void invoice.");
        if (r.AmountMinor > invoice.BalanceMinor)
            throw BadRequestException.Field("amountMinor", $"That's more than the balance of {Money.Format(invoice.BalanceMinor, invoice.Currency)}.");
        var payment = await AddPaymentAsync(invoice, r.AmountMinor, r.Method, r.Reference, r.PaidAt, r.Note, providerRef: null, ct);
        if (r.SendReceipt) QueueReceipt(invoice, payment, await ws.BusinessAsync(ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> DeletePaymentAsync(Guid id, Guid paymentId, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: true, ct);
        var payment = invoice.Payments.FirstOrDefault(p => p.Id == paymentId) ?? throw new NotFoundException("Payment");
        if (payment.ProviderRef is not null) throw new ConflictException("Online payments can't be removed here. Refund them in Paystack.");
        invoice.Payments.Remove(payment);
        invoice.Events.Add(Event(InvoiceEventType.PaymentRemoved, $"{Money.Format(payment.AmountMinor, invoice.Currency)} ({payment.ReceiptNumber})"));
        InvoiceMath.ApplyPayments(invoice, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Adds a payment, recalculates the balance and status, and logs events. Shared with online payments.</summary>
    internal async Task<InvoicePayment> AddPaymentAsync(Invoice invoice, long amountMinor, PaymentMethod method, string? reference,
        DateOnly paidAt, string? note, string? providerRef, CancellationToken ct)
    {
        if (invoice.Status == InvoiceStatus.Draft) MarkSent(invoice, "Marked sent when a payment was recorded");
        var rate = await ws.RateOrAsync(invoice.Currency, paidAt, await ws.BusinessAsync(ct), invoice.ExchangeRate, ct);
        var payment = new InvoicePayment
        {
            InvoiceId = invoice.Id,
            AmountMinor = amountMinor,
            ExchangeRate = rate,
            BaseAmountMinor = ExchangeRateService.ToBase(amountMinor, rate),
            Method = method,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            ProviderRef = providerRef,
            PaidAt = paidAt,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ReceiptNumber = await ws.NextNumberAsync(DocumentCounter.Receipt, "RCT-", ct),
        };
        invoice.Payments.Add(payment);
        invoice.Events.Add(Event(InvoiceEventType.PaymentRecorded, $"{Money.Format(amountMinor, invoice.Currency)} by {method}"));
        var wasPaid = invoice.Status == InvoiceStatus.Paid;
        InvoiceMath.ApplyPayments(invoice, clock.GetUtcNow());
        if (!wasPaid && invoice.Status == InvoiceStatus.Paid) invoice.Events.Add(Event(InvoiceEventType.Paid));
        return payment;
    }

    internal void QueueReceipt(Invoice invoice, InvoicePayment payment, BusinessSettings business)
    {
        if (string.IsNullOrWhiteSpace(invoice.Client.Email)) return;
        notifications.Email(invoice.Client.Email, "receipt", new()
        {
            ["business"] = business.LegalName,
            ["client"] = invoice.Client.Name,
            ["number"] = invoice.Number,
            ["receipt"] = payment.ReceiptNumber,
            ["amount"] = Money.Format(payment.AmountMinor, invoice.Currency),
            ["balance"] = Money.Format(invoice.BalanceMinor, invoice.Currency),
            ["link"] = ws.InvoiceUrl(invoice.PublicToken),
        }, invoice.TenantId);
    }

    public async Task<(byte[] Pdf, string FileName)> PdfAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadAsync(id, track: false, ct);
        return (await RenderAsync(invoice, await ws.BusinessAsync(ct), await ws.TodayAsync(ct), ct), $"{invoice.Number}.pdf");
    }

    internal async Task<byte[]> RenderAsync(Invoice invoice, BusinessSettings business, DateOnly today, CancellationToken ct)
    {
        var c = invoice.Currency;
        var meta = new List<PdfRow>
        {
            new("Invoice date", InvoiceWorkspace.Date(invoice.IssueDate)),
            new("Due date", InvoiceWorkspace.Date(invoice.DueDate)),
            new("Balance due", Money.Format(invoice.BalanceMinor, c), Strong: true),
        };
        var extra = invoice.AmountPaidMinor > 0
            ? new List<PdfRow> { new("Amount paid", "-" + Money.Format(invoice.AmountPaidMinor, c)), new("Balance due", Money.Format(invoice.BalanceMinor, c), true) }
            : [];
        var stamp = invoice.DisplayStatus(today) switch
        {
            InvoiceStatus.Paid => "PAID",
            InvoiceStatus.Void => "VOID",
            InvoiceStatus.Overdue => "OVERDUE",
            _ => null,
        };
        var model = InvoiceWorkspace.PdfModel("INVOICE", invoice, invoice.Items, business, meta, extra, stamp, await ws.ShowBrandingAsync(ct));
        return await pdf.RenderAsync(model, ct);
    }

    internal async Task<byte[]> RenderReceiptAsync(Invoice invoice, InvoicePayment payment, BusinessSettings business, CancellationToken ct)
    {
        var c = invoice.Currency;
        var paidBefore = invoice.Payments.Where(p => p.CreatedAt < payment.CreatedAt).Sum(p => p.AmountMinor);
        var model = new DocumentPdfModel(
            "RECEIPT", payment.ReceiptNumber, invoice.Template, business.BrandColor, business.LogoUrl,
            new PdfParty(business.LegalName, new[] { business.Address, business.Email, business.Phone }.OfType<string>().ToList()),
            new PdfParty(invoice.Client.Name, new[] { invoice.Client.Company, invoice.Client.Email }.OfType<string>().ToList()),
            [
                new("Date paid", InvoiceWorkspace.Date(payment.PaidAt)),
                new("Invoice", invoice.Number),
                new("Method", payment.Method.ToString()),
                .. payment.Reference is { } reference ? [new PdfRow("Reference", reference)] : Array.Empty<PdfRow>(),
            ],
            [new($"Payment for invoice {invoice.Number}", "1", Money.Format(payment.AmountMinor, c), null, null, Money.Format(payment.AmountMinor, c))],
            [
                new("Invoice total", Money.Format(invoice.TotalMinor, c)),
                new("Amount paid", Money.Format(payment.AmountMinor, c), true),
                new("Balance remaining", Money.Format(Math.Max(0, invoice.TotalMinor - paidBefore - payment.AmountMinor), c)),
            ],
            null, payment.Note, null, "PAID", await ws.ShowBrandingAsync(ct), InvoiceWorkspace.AppName);
        return await pdf.RenderAsync(model, ct);
    }

    public async Task<byte[]> ExportCsvAsync(InvoiceStatus? status, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = await ws.TodayAsync(ct);
        var query = Filter(db.Invoices.AsNoTracking(), status, today);
        if (from is { } f) query = query.Where(i => i.IssueDate >= f);
        if (to is { } t) query = query.Where(i => i.IssueDate <= t);
        var rows = await query.OrderBy(i => i.IssueDate).ThenBy(i => i.Number).ToListAsync(ct);

        var sb = new StringBuilder("Number,Client,Issue date,Due date,Status,Currency,Subtotal,Discount,Tax,Shipping,Total,Paid,Balance,Exchange rate,Total (base)\r\n");
        foreach (var i in rows)
        {
            sb.AppendJoin(',', Csv(i.Number), Csv(i.Client.Name), i.IssueDate.ToString("yyyy-MM-dd"), i.DueDate.ToString("yyyy-MM-dd"),
                i.DisplayStatus(today), i.Currency, Major(i.SubtotalMinor), Major(i.DiscountTotalMinor), Major(i.TaxTotalMinor),
                Major(i.ShippingMinor), Major(i.TotalMinor), Major(i.AmountPaidMinor), Major(i.BalanceMinor),
                i.ExchangeRate.ToString("0.########", CultureInfo.InvariantCulture), Major(ExchangeRateService.ToBase(i.TotalMinor, i.ExchangeRate)));
            sb.Append("\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<InvoiceDashboardDto> DashboardAsync(CancellationToken ct)
    {
        var business = await ws.BusinessAsync(ct);
        var today = await ws.TodayAsync(ct);
        var monthStart = await ws.MonthStartUtcAsync(ct);
        var monthStartDate = new DateOnly(today.Year, today.Month, 1);

        // Balances are in each invoice's own currency; convert with the rate saved on the invoice.
        var open = await db.Invoices.AsNoTracking().Where(i => Open.Contains(i.Status) && i.BalanceMinor > 0)
            .Select(i => new { i.BalanceMinor, i.ExchangeRate, i.DueDate }).ToListAsync(ct);
        var outstanding = open.Sum(i => ExchangeRateService.ToBase(i.BalanceMinor, i.ExchangeRate));
        var overdue = open.Where(i => i.DueDate < today).ToList();
        var overdueMinor = overdue.Sum(i => ExchangeRateService.ToBase(i.BalanceMinor, i.ExchangeRate));
        var overdueCount = overdue.Count;
        var paidThisMonth = await db.InvoicePayments.Where(p => p.PaidAt >= monthStartDate).SumAsync(p => (long?)p.BaseAmountMinor, ct) ?? 0;
        var drafts = await db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Draft, ct);
        var thisMonth = await db.Invoices.CountAsync(i => i.CreatedAt >= monthStart, ct);
        var plan = await plans.GetCurrentPlanAsync(ct);
        var recent = await db.Invoices.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(8).ToListAsync(ct);

        return new InvoiceDashboardDto(business.Currency, outstanding, overdueMinor, overdueCount, paidThisMonth, drafts, thisMonth,
            plan.Limit(LimitKeys.InvoicesPerMonth), recent.Select(i => Summary(i, today)).ToList());
    }

    public async Task<IReadOnlyList<TemplateDto>> TemplatesAsync(CancellationToken ct)
    {
        var premium = await plans.HasFeatureAsync(LimitKeys.PremiumTemplates, ct);
        return InvoiceTemplates.All.Select(t => new TemplateDto(t.Key, t.Name, t.Premium, !t.Premium || premium)).ToList();
    }

    // Helpers

    internal async Task EnsureMonthlyLimitAsync(CancellationToken ct)
    {
        var monthStart = await ws.MonthStartUtcAsync(ct);
        await plans.EnsureCanAddAsync(LimitKeys.InvoicesPerMonth, await db.Invoices.CountAsync(i => i.CreatedAt >= monthStart, ct),
            "Invoices this month", ct);
    }

    private async Task FillAsync(Invoice invoice, SaveInvoiceRequest r, BusinessSettings business, CancellationToken ct)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ClientId, ct)
            ?? throw BadRequestException.Field("clientId", "That client does not exist.");
        invoice.ClientId = client.Id;
        invoice.Client = ClientSnapshot.From(client);
        invoice.IssueDate = r.IssueDate;
        invoice.DueDate = r.DueDate;
        invoice.Currency = r.Currency.ToUpperInvariant();
        invoice.ExchangeRate = await ws.ResolveRateAsync(invoice.Currency, r.ExchangeRate, r.IssueDate, business, ct);
        invoice.Template = await ws.ResolveTemplateAsync(r.Template, business, ct);
        invoice.ShippingMinor = r.ShippingMinor;
        invoice.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        invoice.Terms = string.IsNullOrWhiteSpace(r.Terms) ? null : r.Terms.Trim();

        var lines = await ws.BuildLinesAsync<InvoiceItem>(r.Items, ct);
        invoice.Items.Clear();
        foreach (var line in lines)
        {
            line.InvoiceId = invoice.Id;
            invoice.Items.Add(line);
        }
        InvoiceMath.Apply(invoice, invoice.Items.Cast<DocumentLine>().ToList());
        InvoiceMath.ApplyPayments(invoice, clock.GetUtcNow());
    }

    internal static void MarkSent(Invoice invoice, string note)
    {
        invoice.SentAt ??= DateTimeOffset.UtcNow;
        if (invoice.Status == InvoiceStatus.Draft) invoice.Status = InvoiceStatus.Sent;
        invoice.Events.Add(Event(InvoiceEventType.Sent, note));
    }

    internal static InvoiceEvent Event(InvoiceEventType type, string? note = null) => new() { Type = type, Note = note };

    private static IQueryable<Invoice> Filter(IQueryable<Invoice> query, InvoiceStatus? status, DateOnly today) => status switch
    {
        null => query,
        InvoiceStatus.Overdue => query.Where(i => Open.Contains(i.Status) && i.DueDate < today && i.BalanceMinor > 0),
        InvoiceStatus.Sent => query.Where(i => (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Viewed) && !(i.DueDate < today && i.BalanceMinor > 0)),
        InvoiceStatus.Viewed or InvoiceStatus.PartiallyPaid => query.Where(i => i.Status == status && !(i.DueDate < today && i.BalanceMinor > 0)),
        _ => query.Where(i => i.Status == status),
    };

    private async Task<Invoice> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = db.Invoices.Include(i => i.Items).Include(i => i.Payments).Include(i => i.Events);
        return await (track ? query : query.AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Invoice");
    }

    internal static LineRequest ToRequest(DocumentLine l) =>
        new(l.ItemId, l.Description, l.Quantity, l.UnitPriceMinor, l.DiscountPercent, l.TaxRateId);

    internal static InvoiceSummaryDto Summary(Invoice i, DateOnly today) => new(
        i.Id, i.Number, i.Client.Name, i.IssueDate, i.DueDate, i.Currency, i.TotalMinor, i.BalanceMinor, i.DisplayStatus(today), i.CreatedAt,
        ExchangeRateService.ToBase(i.TotalMinor, i.ExchangeRate), ExchangeRateService.ToBase(i.BalanceMinor, i.ExchangeRate));

    private async Task<InvoiceDto> ToDtoAsync(Invoice i, CancellationToken ct)
    {
        var today = await ws.TodayAsync(ct);
        var business = await ws.BusinessAsync(ct);
        var url = ws.InvoiceUrl(i.PublicToken);
        var share = $"Hi {i.Client.Name}, here is invoice {i.Number} from {business.LegalName} for " +
                    $"{Money.Format(i.BalanceMinor, i.Currency)}, due {InvoiceWorkspace.Date(i.DueDate)}: {url}";
        var items = i.Items.OrderBy(l => l.Position).ToList();
        return new InvoiceDto(i.Id, i.Number, i.DisplayStatus(today), InvoiceWorkspace.ToDto(i.ClientId, i.Client), i.IssueDate, i.DueDate,
            i.Currency, i.Template, items.Select(InvoiceWorkspace.ToDto).ToList(), i.SubtotalMinor, i.DiscountTotalMinor, i.TaxTotalMinor,
            InvoiceMath.Taxes(items), i.ShippingMinor, i.TotalMinor, i.AmountPaidMinor, i.BalanceMinor, i.Notes, i.Terms, url,
            InvoiceWorkspace.WhatsAppShare(i.Client, share, await ws.CountryAsync(ct)), i.SentAt, i.ViewedAt, i.PaidAt, i.QuoteId,
            i.RecurringScheduleId,
            i.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.CreatedAt).Select(p => new PaymentDto(p.Id, p.AmountMinor, p.Method,
                p.Reference, p.PaidAt, p.ReceiptNumber, p.Note, p.ProviderRef is not null, p.ExchangeRate, p.BaseAmountMinor)).ToList(),
            i.Events.OrderBy(e => e.CreatedAt).Select(e => new EventDto(e.Type, e.Note, e.CreatedAt)).ToList(), i.CreatedAt,
            business.Currency, i.ExchangeRate, ExchangeRateService.ToBase(i.TotalMinor, i.ExchangeRate),
            ExchangeRateService.ToBase(i.BalanceMinor, i.ExchangeRate));
    }

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@')
            ? $"\"{(value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('-') || value.StartsWith('@') ? "'" : "")}{value.Replace("\"", "\"\"")}\""
            : value;

    private static string Major(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
