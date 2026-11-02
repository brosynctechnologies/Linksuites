using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Billing;
using Saas.Application.Invoices;

namespace Saas.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class InvoicePhase2Tests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1)); // Lagos

    [Fact]
    public async Task Foreign_currency_invoices_keep_their_rate_and_dashboards_use_the_base_currency()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Fx");
        var client = await CreateClientAsync(owner, "US Client");

        var rates = await Json(await owner.GetAsync("/api/exchange-rates", Ct));
        Assert.Equal("NGN", rates.GetProperty("base").GetString());
        var usd = rates.GetProperty("rates").EnumerateArray().Single(r => r.GetProperty("currency").GetString() == "USD");
        Assert.Equal(1600m, usd.GetProperty("toBase").GetDecimal());

        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, "USD", 10_000), Ct)); // $100
        Assert.Equal(1600m, invoice.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(16_000_000, invoice.GetProperty("totalBaseMinor").GetInt64()); // ₦160,000
        Assert.Equal("NGN", invoice.GetProperty("baseCurrency").GetString());

        var manual = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, "USD", 10_000, rate: 1500m), Ct));
        Assert.Equal(15_000_000, manual.GetProperty("totalBaseMinor").GetInt64());

        var id = invoice.GetProperty("id").GetGuid();
        await owner.PostAsync($"/api/invoices/{id}/mark-sent", null, Ct);
        var paid = await Json(await owner.PostAsJsonAsync($"/api/invoices/{id}/payments",
            new { amountMinor = 4_000, method = "BankTransfer", paidAt = Today }, Ct));
        var payment = paid.GetProperty("payments")[0];
        Assert.Equal(6_400_000, payment.GetProperty("baseAmountMinor").GetInt64());

        var dashboard = await Json(await owner.GetAsync("/api/invoice-dashboard", Ct));
        Assert.Equal("NGN", dashboard.GetProperty("currency").GetString());
        Assert.Equal(9_600_000, dashboard.GetProperty("outstandingMinor").GetInt64()); // $60 left × 1600
        Assert.Equal(6_400_000, dashboard.GetProperty("paidThisMonthMinor").GetInt64());

        var business = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Json(await owner.GetAsync("/api/business", Ct))).GetRawText())!;
        business["currency"] = "USD";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/business", business, Ct)).StatusCode);
    }

    [Fact]
    public async Task Expenses_and_profit_report_need_the_business_plan_and_add_up_in_base_currency()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Profit");
        var expense = new
        {
            date = Today, category = "Software", vendor = "Figma", amountMinor = 2_000, currency = "USD", taxMinor = 0,
            paymentMethod = "Card",
        };
        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.PostAsJsonAsync("/api/expenses", expense, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.GetAsync("/api/reports/profit", Ct)).StatusCode);

        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices, "business");
        var usdExpense = await Json(await owner.PostAsJsonAsync("/api/expenses", expense, Ct));
        Assert.Equal(3_200_000, usdExpense.GetProperty("baseAmountMinor").GetInt64()); // $20 × 1600
        (await owner.PostAsJsonAsync("/api/expenses", new
        {
            date = Today, category = "Rent", amountMinor = 5_000_000, currency = "NGN", taxMinor = 348_837, paymentMethod = "BankTransfer",
        }, Ct)).EnsureSuccessStatusCode();

        using var receipt = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4 test"u8.ToArray()), "file", "receipt.pdf" } };
        var withReceipt = await Json(await owner.PostAsync($"/api/expenses/{usdExpense.GetProperty("id").GetGuid()}/receipt", receipt, Ct));
        Assert.EndsWith(".pdf", withReceipt.GetProperty("receiptUrl").GetString());

        var client = await CreateClientAsync(owner, "Payer");
        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, "NGN", 20_000_000), Ct));
        await owner.PostAsJsonAsync($"/api/invoices/{invoice.GetProperty("id").GetGuid()}/payments",
            new { amountMinor = 20_000_000, method = "Cash", paidAt = Today }, Ct);

        var report = await Json(await owner.GetAsync($"/api/reports/profit?from={Today.AddDays(-Today.Day + 1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct));
        Assert.Equal(20_000_000, report.GetProperty("incomeMinor").GetInt64());
        Assert.Equal(8_200_000, report.GetProperty("expensesMinor").GetInt64());
        Assert.Equal(11_800_000, report.GetProperty("profitMinor").GetInt64());
        Assert.Equal(348_837, report.GetProperty("taxOnExpensesMinor").GetInt64());
        Assert.Equal("Rent", report.GetProperty("expensesByCategory")[0].GetProperty("category").GetString());
        Assert.Single(report.GetProperty("months").EnumerateArray());

        var categories = await Json(await owner.GetAsync("/api/expenses/categories", Ct));
        Assert.Contains(categories.EnumerateArray(), c => c.GetString() == "Software");
        Assert.Contains("Figma", await owner.GetStringAsync("/api/expenses/export.csv", Ct));
        Assert.Contains("Total,200000.00,82000.00,118000.00", await owner.GetStringAsync("/api/reports/profit.csv", Ct));
    }

    [Fact]
    public async Task Accountant_joins_by_invite_and_can_read_and_report_but_not_change_anything()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Boss");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        var email = $"acct-{Guid.NewGuid():N}@example.com";

        var blocked = await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Accountant" }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode); // Free plan: 1 seat
        await api.MakeProAsync(tenantId, Products.Invoices, "business");

        (await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Accountant" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Staff" }, Ct)).StatusCode);
        var token = await InviteTokenAsync(email);

        var anon = api.CreateClient();
        var preview = await Json(await anon.GetAsync($"/api/public/invites/{token}", Ct));
        Assert.False(preview.GetProperty("hasAccount").GetBoolean());
        Assert.Equal("Boss Studio", preview.GetProperty("businessName").GetString());

        var joined = await Json(await anon.PostAsJsonAsync($"/api/auth/invites/{token}/accept", new { fullName = "Acct", password = "password123" }, Ct));
        Assert.Equal("Accountant", joined.GetProperty("tenant").GetProperty("role").GetString());
        Assert.Equal(tenantId, joined.GetProperty("tenant").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anon.PostAsJsonAsync($"/api/auth/invites/{token}/accept", new { password = "password123" }, Ct)).StatusCode); // used

        var accountant = api.CreateClient();
        accountant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joined.GetProperty("accessToken").GetString());
        var client = await CreateClientAsync(owner, "Shared client");
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/invoices", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/reports/profit", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/invoices/export.csv", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/invoices", Invoice(client, "NGN", 100), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync("/api/clients", new { name = "X" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/team", Ct)).StatusCode);

        // Owner makes them Staff: they can now write but lose reports (after their token refreshes).
        var team = await Json(await owner.GetAsync("/api/team", Ct));
        Assert.Equal(2, team.GetProperty("seats").GetProperty("used").GetInt32());
        var member = team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("email").GetString() == email);
        var memberId = member.GetProperty("id").GetGuid();
        (await owner.PatchAsJsonAsync($"/api/team/members/{memberId}", new { role = "Staff" }, Ct)).EnsureSuccessStatusCode();
        var refreshed = await Json(await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = joined.GetProperty("refreshToken").GetString() }, Ct));
        accountant.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync("/api/clients", new { name = "By staff" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/reports/profit", Ct)).StatusCode);

        var ownerMember = team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("role").GetString() == "Owner");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/team/members/{ownerMember.GetProperty("id").GetGuid()}", Ct)).StatusCode);

        // Removing them ends their session.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/team/members/{memberId}", Ct)).StatusCode);
        var afterRemoval = await anon.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refreshed.GetProperty("refreshToken").GetString() }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRemoval.StatusCode);
    }

    [Fact]
    public async Task Existing_user_joins_with_their_password_and_can_switch_between_businesses()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("Agency");
        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices, "business");
        var email = $"free-{Guid.NewGuid():N}@example.com";
        var (freelancer, own) = await api.SignupInvoicesAsync("Freelancer", email);

        (await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Admin" }, Ct)).EnsureSuccessStatusCode();
        var token = await InviteTokenAsync(email);
        var anon = api.CreateClient();
        Assert.True((await Json(await anon.GetAsync($"/api/public/invites/{token}", Ct))).GetProperty("hasAccount").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anon.PostAsJsonAsync($"/api/auth/invites/{token}/accept", new { password = "wrong-password" }, Ct)).StatusCode);
        (await anon.PostAsJsonAsync($"/api/auth/invites/{token}/accept", new { password = "password123" }, Ct)).EnsureSuccessStatusCode();

        var businesses = await Json(await freelancer.GetAsync("/api/me/businesses", Ct));
        Assert.Equal(2, businesses.GetArrayLength());
        var agency = businesses.EnumerateArray().Single(b => !b.GetProperty("current").GetBoolean());
        var switched = await Json(await freelancer.PostAsJsonAsync("/api/auth/switch", new { tenantId = agency.GetProperty("tenantId").GetGuid() }, Ct));
        Assert.Equal("Admin", switched.GetProperty("tenant").GetProperty("role").GetString());
        Assert.Equal("Agency Studio", switched.GetProperty("tenant").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await freelancer.PostAsJsonAsync("/api/auth/switch", new { tenantId = Guid.NewGuid() }, Ct)).StatusCode);
        _ = own;
    }

    // ---- Recheck fixes (orders-invoices.md) ----

    [Fact]
    public async Task Verify_UnknownReference_DoesNotConfirm()
    {
        var (owner, token, invoiceId) = await PayableInvoiceAsync("Verifier", 10_000);
        var anon = api.CreateClient();

        var bogus = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = "anything" }, Ct));
        Assert.False(Confirmed(bogus));

        var pay = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/pay", new { email = "payer@example.com" }, Ct));
        var declined = api.Merchant.Initialized.Single(i => pay.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Merchant.Unpaid[declined.Reference] = "failed";
        var failed = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = declined.Reference }, Ct));
        Assert.False(Confirmed(failed));
        Assert.Equal(10_000, failed.GetProperty("balanceMinor").GetInt64());

        var unreachable = $"inv_{invoiceId:N}_down0001";
        api.Merchant.ThrowFor.Add(unreachable);
        Assert.False(Confirmed(await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = unreachable }, Ct))));

        var pay2 = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/pay", new { email = "payer@example.com" }, Ct));
        var good = api.Merchant.Initialized.Single(i => pay2.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));
        api.Merchant.Charges[good.Reference] = new VerifiedCharge(good.Reference, good.AmountMinor, "NGN", DateTimeOffset.UtcNow);
        var paid = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = good.Reference }, Ct));
        Assert.True(Confirmed(paid));
        // Returning to the page again (already recorded) still confirms.
        Assert.True(Confirmed(await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = good.Reference }, Ct))));
        _ = owner;
    }

    [Fact]
    public async Task OnlinePayment_AfterManualFullPayment_FlagsOverpayment()
    {
        var (owner, token, invoiceId) = await PayableInvoiceAsync("Overpaid", 10_000);
        var anon = api.CreateClient();
        var pay = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/pay", new { email = "payer@example.com" }, Ct));
        var init = api.Merchant.Initialized.Single(i => pay.GetProperty("authorizationUrl").GetString()!.EndsWith(i.Reference));

        // The seller records a bank transfer for the full amount while the client is still on Paystack.
        (await owner.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new { amountMinor = 10_000, method = "BankTransfer", paidAt = Today }, Ct))
            .EnsureSuccessStatusCode();
        api.Merchant.Charges[init.Reference] = new VerifiedCharge(init.Reference, init.AmountMinor, "NGN", DateTimeOffset.UtcNow);
        var page = await Json(await anon.PostAsJsonAsync($"/api/public/invoices/{token}/verify", new { reference = init.Reference }, Ct));

        // The money arrived, so it is recorded, but the balance never goes below zero and the business sees a flag.
        Assert.Equal(20_000, page.GetProperty("amountPaidMinor").GetInt64());
        Assert.Equal(0, page.GetProperty("balanceMinor").GetInt64());
        var detail = await Json(await owner.GetAsync($"/api/invoices/{invoiceId}", Ct));
        Assert.Equal(2, detail.GetProperty("payments").GetArrayLength());
        Assert.Equal(0, detail.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("Paid", detail.GetProperty("status").GetString());
        var flag = Assert.Single(detail.GetProperty("events").EnumerateArray(), e => e.GetProperty("type").GetString() == "Overpaid");
        Assert.Contains("100", flag.GetProperty("note").GetString()); // "Overpaid by ₦100"
    }

    [Fact]
    public async Task DeleteTaxRate_InUse_Returns409()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("TaxDel");
        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices);
        var client = await CreateClientAsync(owner, "Taxed");
        var vat = await TaxRateAsync(owner, "VAT2", 7.5m);
        var recurringOnly = await TaxRateAsync(owner, "Levy", 2m);
        var unused = await TaxRateAsync(owner, "Unused", 1m);
        (await owner.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client, issueDate = Today, dueDate = Today.AddDays(14), currency = "NGN",
            items = new[] { new { description = "Work", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0, taxRateId = (Guid?)vat } },
        }, Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/recurring", Recurring(client, recurringOnly), Ct)).EnsureSuccessStatusCode();

        var inUse = await owner.DeleteAsync($"/api/tax-rates/{vat}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Contains("used on invoices", (await Json(inUse)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/tax-rates/{recurringOnly}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/tax-rates/{unused}", Ct)).StatusCode);
    }

    [Fact]
    public async Task DeleteTaxRate_RecurringJobStillRunsAndRemindersStillSend()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("TaxJob");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        await api.MakeProAsync(tenantId, Products.Invoices);
        var client = await CreateClientAsync(owner, "Retainer", "taxjob@example.com");
        var levy = await TaxRateAsync(owner, "Old levy", 2m);
        (await owner.PostAsJsonAsync("/api/recurring", Recurring(client, levy), Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/recurring", Recurring(client, null), Ct)).EnsureSuccessStatusCode();
        // A rate deleted before deletes were guarded leaves a schedule pointing at nothing.
        await api.WithDbAsync(db => db.TaxRates.IgnoreQueryFilters().Where(t => t.Id == levy).ExecuteDeleteAsync(Ct));

        var overdue = await Json(await owner.PostAsJsonAsync("/api/invoices", new
        {
            clientId = client, issueDate = Today.AddDays(-10), dueDate = Today.AddDays(-1), currency = "NGN",
            items = new[] { new { description = "Old work", quantity = 1, unitPriceMinor = 5_000, discountPercent = 0 } },
        }, Ct));
        await owner.PostAsync($"/api/invoices/{overdue.GetProperty("id").GetGuid()}/mark-sent", null, Ct);
        var business = JsonSerializer.Deserialize<Dictionary<string, object?>>((await Json(await owner.GetAsync("/api/business", Ct))).GetRawText())!;
        business["remindersEnabled"] = true;
        business["reminderDaysAfter"] = new[] { 1 };
        (await owner.PutAsJsonAsync("/api/business", business, Ct)).EnsureSuccessStatusCode();

        var result = await api.AsTenantAsync<InvoiceJobs, (int, int)>(tenantId, j => j.RunAsync(Ct));

        Assert.Equal((1, 1), result); // the healthy schedule ran and the reminder went out
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "invoice.reminder" && n.Recipient == "taxjob@example.com", Ct)));
    }

    [Fact]
    public async Task ClientOutstanding_ConvertsForeignBalancesToBase()
    {
        var (owner, _) = await api.SignupInvoicesAsync("Mixed");
        var client = await CreateClientAsync(owner, "Global client");
        var usd = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, "USD", 100_000), Ct)); // $1,000 at 1600
        var ngn = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, "NGN", 5_000_000), Ct)); // ₦50,000
        await owner.PostAsync($"/api/invoices/{usd.GetProperty("id").GetGuid()}/mark-sent", null, Ct);
        await owner.PostAsync($"/api/invoices/{ngn.GetProperty("id").GetGuid()}/mark-sent", null, Ct);

        var detail = await Json(await owner.GetAsync($"/api/clients/{client}", Ct));
        Assert.Equal(165_000_000, detail.GetProperty("outstandingMinor").GetInt64()); // ₦1,600,000 + ₦50,000
        var row = (await Json(await owner.GetAsync("/api/clients", Ct))).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == client);
        Assert.Equal(165_000_000, row.GetProperty("outstandingMinor").GetInt64());
        var dashboard = await Json(await owner.GetAsync("/api/invoice-dashboard", Ct));
        Assert.Equal(165_000_000, dashboard.GetProperty("outstandingMinor").GetInt64());
    }

    private static bool Confirmed(JsonElement page) =>
        page.TryGetProperty("paymentConfirmed", out var c) && c.ValueKind == JsonValueKind.True;

    private async Task<(HttpClient Owner, string Token, Guid InvoiceId)> PayableInvoiceAsync(string name, long amountMinor)
    {
        var (owner, auth) = await api.SignupInvoicesAsync(name);
        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), Products.Invoices);
        (await owner.PutAsJsonAsync("/api/business/paystack", new { secretKey = "sk_test_0123456789abcdef0123" }, Ct)).EnsureSuccessStatusCode();
        var client = await CreateClientAsync(owner, "Payer", "payer@example.com");
        var invoice = await Json(await owner.PostAsJsonAsync("/api/invoices", Invoice(client, "NGN", amountMinor), Ct));
        var id = invoice.GetProperty("id").GetGuid();
        (await owner.PostAsync($"/api/invoices/{id}/mark-sent", null, Ct)).EnsureSuccessStatusCode();
        return (owner, invoice.GetProperty("publicUrl").GetString()!.Split("/i/")[1], id);
    }

    private async Task<Guid> TaxRateAsync(HttpClient owner, string name, decimal ratePercent)
    {
        var response = await owner.PostAsJsonAsync("/api/tax-rates", new { name, ratePercent }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static object Recurring(Guid clientId, Guid? taxRateId) => new
    {
        clientId, frequency = "Monthly", startDate = Today, dueInDays = 7, autoSend = false, isActive = true, currency = "NGN",
        items = new[] { new { description = "Retainer", quantity = 1, unitPriceMinor = 1_000_000, discountPercent = 0, taxRateId } },
    };

    private async Task<string> InviteTokenAsync(string email)
    {
        var link = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).Select(n => n.Payload).FirstAsync(Ct));
        return link["link"].Split("/invite/")[1];
    }

    private static object Invoice(Guid clientId, string currency, long unitPriceMinor, decimal? rate = null) => new
    {
        clientId, issueDate = Today, dueDate = Today.AddDays(14), currency, exchangeRate = rate,
        items = new[] { new { description = "Work", quantity = 1, unitPriceMinor, discountPercent = 0 } },
    };

    private async Task<Guid> CreateClientAsync(HttpClient owner, string name, string? email = null)
    {
        var response = await owner.PostAsJsonAsync("/api/clients", new { name, email }, Ct);
        response.EnsureSuccessStatusCode();
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
}
