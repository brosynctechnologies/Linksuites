using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Invoices;
using Saas.Domain.Invoices;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class InvoiceFlowTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    [Fact]
    public async Task Invoice_goes_from_draft_to_paid_with_receipts_and_pdfs()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Ada");
        var vat = (await Json(await owner.GetAsync("/api/tax-rates", Ct))).EnumerateArray().Single();
        Assert.Equal(7.5m, vat.GetProperty("ratePercent").GetDecimal());
        var client = await CreateClientAsync(owner, "Bola Ltd", "bola@example.com", "0803 000 0000");

        var created = await owner.PostAsJsonAsync("/api/invoices", Invoice(client, vat.GetProperty("id").GetGuid()), Ct);
        var invoice = await Json(created);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal("INV-0001", invoice.GetProperty("number").GetString());
        Assert.Equal("Draft", invoice.GetProperty("status").GetString());
        Assert.Equal(18_137_625, invoice.GetProperty("totalMinor").GetInt64());
        Assert.Equal(1_012_500, invoice.GetProperty("taxes")[0].GetProperty("amountMinor").GetInt64());
        Assert.StartsWith("https://wa.me/2348030000000?text=", invoice.GetProperty("whatsappShareUrl").GetString());
        var id = invoice.GetProperty("id").GetGuid();
        var token = invoice.GetProperty("publicUrl").GetString()!.Split("/i/")[1];

        // Drafts are not "viewed" when the owner previews the public link.
        Assert.Equal("Draft", (await Json(await api.CreateClient().GetAsync($"/api/public/invoices/{token}", Ct))).GetProperty("status").GetString());

        var sent = await Json(await owner.PostAsJsonAsync($"/api/invoices/{id}/send", new { message = "Thanks!" }, Ct));
        Assert.Equal("Sent", sent.GetProperty("status").GetString());
        Assert.True(await api.WithDbAsync(db => db.Notifications.AnyAsync(n => n.TemplateKey == "invoice.sent" && n.Recipient == "bola@example.com", Ct)));

        var viewed = await Json(await api.CreateClient().GetAsync($"/api/public/invoices/{token}", Ct));
        Assert.Equal("Viewed", viewed.GetProperty("status").GetString());
        Assert.False(viewed.GetProperty("canPayOnline").GetBoolean());

        var tooMuch = await owner.PostAsJsonAsync($"/api/invoices/{id}/payments", Payment(99_999_999), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);

        var partial = await Json(await owner.PostAsJsonAsync($"/api/invoices/{id}/payments", Payment(8_137_625, sendReceipt: true), Ct));
        Assert.Equal("PartiallyPaid", partial.GetProperty("status").GetString());
        Assert.Equal(10_000_000, partial.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("RCT-0001", partial.GetProperty("payments")[0].GetProperty("receiptNumber").GetString());

        var paid = await Json(await owner.PostAsJsonAsync($"/api/invoices/{id}/payments", Payment(10_000_000), Ct));
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Contains(paid.GetProperty("events").EnumerateArray(), e => e.GetProperty("type").GetString() == "Paid");

        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/invoices/{id}", Ct)).StatusCode);
        await AssertPdf(await owner.GetAsync($"/api/invoices/{id}/pdf", Ct));
        await AssertPdf(await api.CreateClient().GetAsync($"/api/public/invoices/{token}/pdf", Ct));
        var paymentId = paid.GetProperty("payments")[0].GetProperty("id").GetGuid();
        await AssertPdf(await api.CreateClient().GetAsync($"/api/public/invoices/{token}/receipts/{paymentId}/pdf", Ct));

        var csv = await owner.GetStringAsync("/api/invoices/export.csv", Ct);
        Assert.Contains("INV-0001,Bola Ltd", csv);
        var dashboard = await Json(await owner.GetAsync("/api/invoice-dashboard", Ct));
        Assert.Equal(18_137_625, dashboard.GetProperty("paidThisMonthMinor").GetInt64());
        Assert.Equal(0, dashboard.GetProperty("outstandingMinor").GetInt64());
    }

    [Fact]
    public async Task One_login_works_across_products_but_tokens_are_product_scoped()
    {
        var email = $"multi-{Guid.NewGuid():N}@example.com";
        var orders = api.CreateClient();
        (await api.SignUpAsync(orders, "/api/auth/signup", new
        {
            fullName = "Multi", email, password = "password123", storeName = "Multi Store", whatsappNumber = "08031234567", country = "NG",
        }, Ct)).EnsureSuccessStatusCode();

        var wrongPassword = await api.SignUpAsync(api.CreateClient(), "/api/auth/signup",
            new { fullName = "Multi", email, password = "not-the-password", country = "NG", product = "invoices", businessName = "Multi Inv" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

        var (invoicesClient, auth) = await api.SignupInvoicesAsync("Multi", email);
        Assert.Equal("invoices", auth.GetProperty("tenant").GetProperty("product").GetString());

        var ordersLogin = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "password123", product = "whatsapp-orders" }, Ct));
        var invoicesLogin = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "password123", product = "invoices" }, Ct));
        Assert.NotEqual(ordersLogin.GetProperty("tenant").GetProperty("id").GetGuid(), invoicesLogin.GetProperty("tenant").GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await invoicesClient.GetAsync("/api/products", Ct)).StatusCode);
        orders.DefaultRequestHeaders.Authorization = new("Bearer", ordersLogin.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await orders.GetAsync("/api/invoices", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await orders.GetAsync("/api/products", Ct)).StatusCode);
    }

    [Fact]
    public async Task Free_plan_limits_invoices_per_month_and_premium_templates()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Free");
        var client = await CreateClientAsync(owner, "C", null, null);

        var premium = await owner.PostAsJsonAsync("/api/invoices", Invoice(client, null, template: "executive"), Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, premium.StatusCode);

        for (var i = 0; i < 10; i++) (await owner.PostAsJsonAsync("/api/invoices", Invoice(client, null), Ct)).EnsureSuccessStatusCode();
        var eleventh = await owner.PostAsJsonAsync("/api/invoices", Invoice(client, null), Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, eleventh.StatusCode);

        var billing = await Json(await owner.GetAsync("/api/billing", Ct));
        var usage = billing.GetProperty("usage").GetProperty("items")[0];
        Assert.Equal(10, usage.GetProperty("used").GetInt64());
        Assert.Equal(10, usage.GetProperty("limit").GetInt64());
    }

    [Fact]
    public async Task Accepted_quote_converts_to_an_invoice()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Quoter");
        var client = await CreateClientAsync(owner, "Client Q", "q@example.com", null);
        var quote = await Json(await owner.PostAsJsonAsync("/api/quotes", new
        {
            clientId = client, issueDate = Today, expiryDate = Today.AddDays(30), currency = "NGN",
            items = new[] { new { description = "Website", quantity = 1, unitPriceMinor = 50_000_000, discountPercent = 0 } },
        }, Ct));
        Assert.Equal("QUO-0001", quote.GetProperty("number").GetString());
        var id = quote.GetProperty("id").GetGuid();
        var token = quote.GetProperty("publicUrl").GetString()!.Split("/q/")[1];
        var anon = api.CreateClient();

        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsync($"/api/public/quotes/{token}/accept", null, Ct)).StatusCode); // still a draft
        await owner.PostAsync($"/api/quotes/{id}/mark-sent", null, Ct);
        var accepted = await Json(await anon.PostAsync($"/api/public/quotes/{token}/accept", null, Ct));
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.False(accepted.GetProperty("canRespond").GetBoolean());

        var invoice = await Json(await owner.PostAsync($"/api/quotes/{id}/convert", null, Ct));
        Assert.Equal(50_000_000, invoice.GetProperty("totalMinor").GetInt64());
        Assert.Equal(id, invoice.GetProperty("quoteId").GetGuid());
        var again = await Json(await owner.PostAsync($"/api/quotes/{id}/convert", null, Ct));
        Assert.Equal(invoice.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal("Converted", (await Json(await owner.GetAsync($"/api/quotes/{id}", Ct))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Online_payment_is_verified_with_the_business_key_and_recorded_once()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Payee");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.Invoices);
        (await owner.PutAsJsonAsync("/api/business/paystack", new { secretKey = "sk_test_0123456789abcdef0123" }, Ct)).EnsureSuccessStatusCode();
        var client = await CreateClientAsync(owner, "Payer", "payer@example.com", null);
        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, null), Ct));
        var id = invoice.GetProperty("id").GetGuid();
        await owner.PostAsync($"/api/invoices/{id}/mark-sent", null, Ct);
        var token = invoice.GetProperty("publicUrl").GetString()!.Split("/i/")[1];
        var anon = api.CreateClient();

        var page = await Json(await anon.GetAsync($"/api/public/invoices/{token}", Ct));
        Assert.True(page.GetProperty("canPayOnline").GetBoolean());
        var pay = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/pay", new { }, Ct));
        var init = api.Merchant.Initialized.Single(i => pay.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        Assert.Equal(invoice.GetProperty("totalMinor").GetInt64(), init.AmountMinor);
        Assert.StartsWith($"inv_{id:N}_", init.Reference);

        // A reference for a different invoice is ignored without asking Paystack.
        var before = api.Merchant.VerifyCalls;
        await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = $"inv_{Guid.NewGuid():N}_abc" }, Ct);
        Assert.Equal(before, api.Merchant.VerifyCalls);

        api.Merchant.Charges[init.Reference] = new VerifiedCharge(init.Reference, init.AmountMinor, "NGN", DateTimeOffset.UtcNow);
        var verified = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = init.Reference }, Ct));
        await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = init.Reference }, Ct);

        Assert.Equal("Paid", verified.GetProperty("status").GetString());
        var detail = await Json(await owner.GetAsync($"/api/invoices/{id}", Ct));
        var payment = Assert.Single(detail.GetProperty("payments").EnumerateArray());
        Assert.True(payment.GetProperty("online").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/invoices/{id}/payments/{payment.GetProperty("id").GetGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Jobs_create_recurring_invoices_and_send_reminders_once_a_day()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Jobs");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        var client = await CreateClientAsync(owner, "Retainer", "retainer@example.com", null);

        var recurringRequest = new
        {
            clientId = client, frequency = "Monthly", startDate = Today, dueInDays = 7, autoSend = true, isActive = true, currency = "NGN",
            items = new[] { new { description = "Monthly retainer", quantity = 1, unitPriceMinor = 20_000_000, discountPercent = 0 } },
        };
        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.PostAsJsonAsync("/api/recurring", recurringRequest, Ct)).StatusCode);
        await api.MakeProAsync(tenantId, Products.Invoices);
        (await owner.PostAsJsonAsync("/api/recurring", recurringRequest, Ct)).EnsureSuccessStatusCode();

        var overdue = await Json(await owner.PostAsJsonAsync("/api/invoices",
            Invoice(client, null) with { IssueDate = Today.AddDays(-10), DueDate = Today.AddDays(-1) }, Ct));
        await owner.PostAsync($"/api/invoices/{overdue.GetProperty("id").GetGuid()}/mark-sent", null, Ct);
        var business = await Json(await owner.GetAsync("/api/business", Ct));
        var update = JsonSerializer.Deserialize<Dictionary<string, object?>>(business.GetRawText())!;
        update["remindersEnabled"] = true;
        update["reminderDaysAfter"] = new[] { 1 };
        (await owner.PutAsJsonAsync("/api/business", update, Ct)).EnsureSuccessStatusCode();

        var first = await api.AsTenantAsync<InvoiceJobs, (int, int)>(tenantId, j => j.RunAsync(Ct));
        var second = await api.AsTenantAsync<InvoiceJobs, (int, int)>(tenantId, j => j.RunAsync(Ct));

        Assert.Equal((1, 1), first);
        Assert.Equal((0, 0), second);
        var list = await Json(await owner.GetAsync("/api/invoices?status=Sent", Ct));
        var generated = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(20_000_000, generated.GetProperty("totalMinor").GetInt64());
        var schedule = (await Json(await owner.GetAsync("/api/recurring", Ct))).EnumerateArray().Single();
        Assert.Equal(Today.AddMonths(1).ToString("yyyy-MM-dd"), schedule.GetProperty("nextRunDate").GetString());
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "invoice.reminder" && n.Recipient == "retainer@example.com", Ct)));
        Assert.Equal("Overdue", (await Json(await owner.GetAsync($"/api/invoices/{overdue.GetProperty("id").GetGuid()}", Ct))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task No_signup_generator_returns_a_pdf()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/public/invoice-preview/pdf", new
        {
            business = new { name = "Freelancer" },
            client = new { name = "Someone" },
            currency = "NGN",
            template = "prestige", // premium: falls back to a free template
            items = new[] { new { description = "Work", quantity = 3, unitPriceMinor = 1_000_000, discountPercent = 0, taxRatePercent = 7.5 } },
        }, Ct);
        await AssertPdf(response);
    }

    private static SaveInvoiceRequest Invoice(Guid clientId, Guid? taxRateId, string? template = null) => new()
    {
        ClientId = clientId,
        IssueDate = Today,
        DueDate = Today.AddDays(14),
        Currency = "NGN",
        Template = template,
        Items =
        [
            new LineRequest(null, "Logo design", 1, 15_000_000, 10, taxRateId),
            new LineRequest(null, "Business cards (box)", 2.5m, 1_250_050, 0, null),
        ],
        ShippingMinor = 500_000,
    };

    private static object Payment(long amount, bool sendReceipt = false) =>
        new { amountMinor = amount, method = "BankTransfer", paidAt = Today, reference = "TRF-1", sendReceipt };

    private async Task<Guid> CreateClientAsync(HttpClient owner, string name, string? email, string? phone)
    {
        var response = await owner.PostAsJsonAsync("/api/clients", new { name, email, phone }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task AssertPdf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
