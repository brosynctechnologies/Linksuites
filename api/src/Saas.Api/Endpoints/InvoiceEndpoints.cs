using Saas.Api.Infrastructure;
using Saas.Application.Invoices;
using Saas.Domain.Core;
using Saas.Domain.Invoices;

namespace Saas.Api.Endpoints;

/// <summary>Invoice Generator: business settings, clients, invoices, quotes, recurring, and public client pages.</summary>
public static class InvoiceEndpoints
{
    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(Policies.InvoicesProduct);

        var business = api.MapGroup("/business").WithTags("Invoices: settings");
        business.MapGet("/", (InvoiceSetupService s, CancellationToken ct) => s.GetBusinessAsync(ct));
        business.MapPut("/", (UpdateBusinessRequest r, InvoiceSetupService s, CancellationToken ct) => s.UpdateBusinessAsync(r, ct))
            .Validate<UpdateBusinessRequest>().RequireAuthorization(Policies.Admin);
        business.MapPost("/logo", (IFormFile file, InvoiceSetupService s, CancellationToken ct) => s.UploadLogoAsync(file.ToUpload(), ct))
            .DisableAntiforgery().RequireAuthorization(Policies.Admin);
        business.MapPut("/paystack", (PaystackKeyRequest r, InvoiceSetupService s, CancellationToken ct) => s.SetPaystackKeyAsync(r, ct))
            .RequireAuthorization(Policies.Owner);
        business.MapDelete("/paystack", (InvoiceSetupService s, CancellationToken ct) => s.RemovePaystackKeyAsync(ct))
            .RequireAuthorization(Policies.Owner);

        var taxes = api.MapGroup("/tax-rates").WithTags("Invoices: settings").WritableBy(MemberRole.Owner, MemberRole.Admin);
        taxes.MapGet("/", (InvoiceSetupService s, CancellationToken ct) => s.ListTaxRatesAsync(ct));
        taxes.MapPost("/", (SaveTaxRateRequest r, InvoiceSetupService s, CancellationToken ct) => s.SaveTaxRateAsync(null, r, ct)).Validate<SaveTaxRateRequest>();
        taxes.MapPut("/{id:guid}", (Guid id, SaveTaxRateRequest r, InvoiceSetupService s, CancellationToken ct) => s.SaveTaxRateAsync(id, r, ct))
            .Validate<SaveTaxRateRequest>();
        taxes.MapDelete("/{id:guid}", async (Guid id, InvoiceSetupService s, CancellationToken ct) =>
        {
            await s.DeleteTaxRateAsync(id, ct);
            return Results.NoContent();
        });

        var clients = api.MapGroup("/clients").WithTags("Invoices: clients").WritableBy(MemberRole.Owner, MemberRole.Admin, MemberRole.Staff);
        clients.MapGet("/", (string? q, InvoiceSetupService s, CancellationToken ct) => s.ListClientsAsync(q, ct));
        clients.MapGet("/{id:guid}", (Guid id, InvoiceSetupService s, CancellationToken ct) => s.GetClientAsync(id, ct));
        clients.MapPost("/", (SaveClientRequest r, InvoiceSetupService s, CancellationToken ct) => s.SaveClientAsync(null, r, ct)).Validate<SaveClientRequest>();
        clients.MapPut("/{id:guid}", (Guid id, SaveClientRequest r, InvoiceSetupService s, CancellationToken ct) => s.SaveClientAsync(id, r, ct))
            .Validate<SaveClientRequest>();
        clients.MapDelete("/{id:guid}", async (Guid id, InvoiceSetupService s, CancellationToken ct) =>
        {
            await s.DeleteClientAsync(id, ct);
            return Results.NoContent();
        });

        var items = api.MapGroup("/items").WithTags("Invoices: items").WritableBy(MemberRole.Owner, MemberRole.Admin, MemberRole.Staff);
        items.MapGet("/", (string? q, InvoiceSetupService s, CancellationToken ct) => s.ListItemsAsync(q, ct));
        items.MapPost("/", (SaveItemRequest r, InvoiceSetupService s, CancellationToken ct) => s.SaveItemAsync(null, r, ct)).Validate<SaveItemRequest>();
        items.MapPut("/{id:guid}", (Guid id, SaveItemRequest r, InvoiceSetupService s, CancellationToken ct) => s.SaveItemAsync(id, r, ct))
            .Validate<SaveItemRequest>();
        items.MapDelete("/{id:guid}", async (Guid id, InvoiceSetupService s, CancellationToken ct) =>
        {
            await s.DeleteItemAsync(id, ct);
            return Results.NoContent();
        });

        var invoices = api.MapGroup("/invoices").WithTags("Invoices").WritableBy(MemberRole.Owner, MemberRole.Admin, MemberRole.Staff);
        invoices.MapGet("/", (InvoiceStatus? status, Guid? clientId, string? q, int? page, int? pageSize, InvoiceService s, CancellationToken ct) =>
            s.ListAsync(status, clientId, q, page ?? 1, pageSize ?? 20, ct));
        invoices.MapGet("/export.csv", async (InvoiceStatus? status, DateOnly? from, DateOnly? to, InvoiceService s, CancellationToken ct) =>
            Results.File(await s.ExportCsvAsync(status, from, to, ct), "text/csv", $"invoices-{DateTime.UtcNow:yyyy-MM-dd}.csv"))
            .RequireAuthorization(Policies.Reports);
        invoices.MapGet("/{id:guid}", (Guid id, InvoiceService s, CancellationToken ct) => s.GetAsync(id, ct));
        invoices.MapPost("/", (SaveInvoiceRequest r, InvoiceService s, CancellationToken ct) => s.CreateAsync(r, ct)).Validate<SaveInvoiceRequest>();
        invoices.MapPut("/{id:guid}", (Guid id, SaveInvoiceRequest r, InvoiceService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .Validate<SaveInvoiceRequest>();
        invoices.MapDelete("/{id:guid}", async (Guid id, InvoiceService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        invoices.MapPost("/{id:guid}/send", (Guid id, SendRequest r, InvoiceService s, CancellationToken ct) => s.SendAsync(id, r, ct))
            .Validate<SendRequest>();
        invoices.MapPost("/{id:guid}/mark-sent", (Guid id, InvoiceService s, CancellationToken ct) => s.MarkSentAsync(id, ct));
        invoices.MapPost("/{id:guid}/void", (Guid id, InvoiceService s, CancellationToken ct) => s.VoidAsync(id, ct));
        invoices.MapPost("/{id:guid}/duplicate", (Guid id, InvoiceService s, CancellationToken ct) => s.DuplicateAsync(id, ct));
        invoices.MapGet("/{id:guid}/pdf", async (Guid id, InvoiceService s, CancellationToken ct) => Pdf(await s.PdfAsync(id, ct)));
        invoices.MapPost("/{id:guid}/payments", (Guid id, RecordPaymentRequest r, InvoiceService s, CancellationToken ct) =>
            s.RecordPaymentAsync(id, r, ct)).Validate<RecordPaymentRequest>();
        invoices.MapDelete("/{id:guid}/payments/{paymentId:guid}", (Guid id, Guid paymentId, InvoiceService s, CancellationToken ct) =>
            s.DeletePaymentAsync(id, paymentId, ct));
        api.MapGet("/invoice-dashboard", (InvoiceService s, CancellationToken ct) => s.DashboardAsync(ct)).WithTags("Invoices");
        api.MapGet("/invoice-templates", (InvoiceService s, CancellationToken ct) => s.TemplatesAsync(ct)).WithTags("Invoices");

        var quotes = api.MapGroup("/quotes").WithTags("Quotes").WritableBy(MemberRole.Owner, MemberRole.Admin, MemberRole.Staff);
        quotes.MapGet("/", (QuoteStatus? status, string? q, int? page, int? pageSize, QuoteService s, CancellationToken ct) =>
            s.ListAsync(status, q, page ?? 1, pageSize ?? 20, ct));
        quotes.MapGet("/{id:guid}", (Guid id, QuoteService s, CancellationToken ct) => s.GetAsync(id, ct));
        quotes.MapPost("/", (SaveQuoteRequest r, QuoteService s, CancellationToken ct) => s.CreateAsync(r, ct)).Validate<SaveQuoteRequest>();
        quotes.MapPut("/{id:guid}", (Guid id, SaveQuoteRequest r, QuoteService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .Validate<SaveQuoteRequest>();
        quotes.MapDelete("/{id:guid}", async (Guid id, QuoteService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        quotes.MapPost("/{id:guid}/send", (Guid id, SendRequest r, QuoteService s, CancellationToken ct) => s.SendAsync(id, r, ct))
            .Validate<SendRequest>();
        quotes.MapPost("/{id:guid}/mark-sent", (Guid id, QuoteService s, CancellationToken ct) => s.MarkSentAsync(id, ct));
        quotes.MapPost("/{id:guid}/convert", (Guid id, QuoteService s, CancellationToken ct) => s.ConvertAsync(id, ct));
        quotes.MapGet("/{id:guid}/pdf", async (Guid id, QuoteService s, CancellationToken ct) => Pdf(await s.PdfAsync(id, ct)));

        var recurring = api.MapGroup("/recurring").WithTags("Recurring invoices").WritableBy(MemberRole.Owner, MemberRole.Admin, MemberRole.Staff);
        recurring.MapGet("/", (RecurringService s, CancellationToken ct) => s.ListAsync(ct));
        recurring.MapPost("/", (SaveRecurringRequest r, RecurringService s, CancellationToken ct) => s.SaveAsync(null, r, ct))
            .Validate<SaveRecurringRequest>();
        recurring.MapPut("/{id:guid}", (Guid id, SaveRecurringRequest r, RecurringService s, CancellationToken ct) => s.SaveAsync(id, r, ct))
            .Validate<SaveRecurringRequest>();
        recurring.MapDelete("/{id:guid}", async (Guid id, RecurringService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        api.MapGet("/exchange-rates", async (InvoiceWorkspace ws, ExchangeRateService fx, CancellationToken ct) =>
            await fx.RatesAsync((await ws.BusinessAsync(ct)).Currency, ct)).WithTags("Invoices: settings");

        var expenses = api.MapGroup("/expenses").WithTags("Expenses").WritableBy(MemberRole.Owner, MemberRole.Admin, MemberRole.Staff);
        expenses.MapGet("/", (DateOnly? from, DateOnly? to, string? category, string? q, int? page, int? pageSize, ExpenseService s,
            CancellationToken ct) => s.ListAsync(from, to, category, q, page ?? 1, pageSize ?? 20, ct));
        expenses.MapGet("/categories", (ExpenseService s, CancellationToken ct) => s.CategoriesAsync(ct));
        expenses.MapGet("/export.csv", async (DateOnly? from, DateOnly? to, ExpenseService s, CancellationToken ct) =>
            Results.File(await s.ExportCsvAsync(from, to, ct), "text/csv", $"expenses-{DateTime.UtcNow:yyyy-MM-dd}.csv"))
            .RequireAuthorization(Policies.Reports);
        expenses.MapGet("/{id:guid}", (Guid id, ExpenseService s, CancellationToken ct) => s.GetAsync(id, ct));
        expenses.MapPost("/", (SaveExpenseRequest r, ExpenseService s, CancellationToken ct) => s.SaveAsync(null, r, ct)).Validate<SaveExpenseRequest>();
        expenses.MapPut("/{id:guid}", (Guid id, SaveExpenseRequest r, ExpenseService s, CancellationToken ct) => s.SaveAsync(id, r, ct))
            .Validate<SaveExpenseRequest>();
        expenses.MapDelete("/{id:guid}", async (Guid id, ExpenseService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });
        expenses.MapPost("/{id:guid}/receipt", (Guid id, IFormFile file, ExpenseService s, CancellationToken ct) =>
            s.UploadReceiptAsync(id, file.ToUpload(), ct)).DisableAntiforgery();
        expenses.MapDelete("/{id:guid}/receipt", (Guid id, ExpenseService s, CancellationToken ct) => s.RemoveReceiptAsync(id, ct));

        var reports = api.MapGroup("/reports").WithTags("Reports").RequireAuthorization(Policies.Reports);
        reports.MapGet("/profit", (DateOnly? from, DateOnly? to, ReportService s, CancellationToken ct) => s.ProfitAsync(from, to, ct));
        reports.MapGet("/profit.csv", async (DateOnly? from, DateOnly? to, ReportService s, CancellationToken ct) =>
            Results.File(await s.ProfitCsvAsync(from, to, ct), "text/csv", "profit-and-loss.csv"));

        var pub = app.MapGroup("/api/public").WithTags("Invoices: public");
        pub.MapGet("/invoices/{token}", (string token, PublicDocumentsService s, CancellationToken ct) => s.GetInvoiceAsync(token, ct));
        pub.MapGet("/invoices/{token}/pdf", async (string token, PublicDocumentsService s, CancellationToken ct) =>
            Pdf(await s.InvoicePdfAsync(token, ct)));
        pub.MapPost("/invoices/{token}/pay", (string token, PayRequest r, PublicDocumentsService s, CancellationToken ct) =>
            s.PayAsync(token, r, ct)).RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapPost("/invoices/{token}/verify", (string token, VerifyRequest r, PublicDocumentsService s, CancellationToken ct) =>
            s.VerifyAsync(token, r, ct)).RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapGet("/invoices/{token}/receipts/{paymentId:guid}/pdf", async (string token, Guid paymentId, PublicDocumentsService s,
            CancellationToken ct) => Pdf(await s.ReceiptPdfAsync(token, paymentId, ct)));
        pub.MapGet("/quotes/{token}", (string token, PublicDocumentsService s, CancellationToken ct) => s.GetQuoteAsync(token, ct));
        pub.MapPost("/quotes/{token}/accept", (string token, PublicDocumentsService s, CancellationToken ct) => s.RespondAsync(token, true, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapPost("/quotes/{token}/decline", (string token, PublicDocumentsService s, CancellationToken ct) => s.RespondAsync(token, false, ct))
            .RequireRateLimiting(RateLimits.PublicWrite);
        pub.MapGet("/quotes/{token}/pdf", async (string token, PublicDocumentsService s, CancellationToken ct) => Pdf(await s.QuotePdfAsync(token, ct)));
        pub.MapGet("/invoice-templates", () =>
            InvoiceTemplates.All.Select(t => new { key = t.Key, name = t.Name, isPremium = t.Premium }));
        pub.MapPost("/invoice-preview/pdf", async (PreviewRequest r, PublicDocumentsService s, CancellationToken ct) =>
            Results.File(await s.PreviewAsync(r, ct), "application/pdf", $"{(string.IsNullOrWhiteSpace(r.Number) ? "invoice" : r.Number)}.pdf"))
            .Validate<PreviewRequest>().RequireRateLimiting(RateLimits.Preview);
    }

    private static IResult Pdf((byte[] Pdf, string FileName) file) => Results.File(file.Pdf, "application/pdf", file.FileName);
}
