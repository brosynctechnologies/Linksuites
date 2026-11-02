using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 11: desk payments (receipt numbers, the bill lock, idempotency keys), reversals, discounts and waivers, the deposit shown
/// as credit, the receipt PDF, and online payment through the clinic's own Paystack key (plan gate, webhook and return verify recorded once,
/// Mismatch, overpayment flagged, Abandoned as a soft state). Cashier and front-desk views of bills and payments carry nothing clinical.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class CarePaymentTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string CareKey = "sk_test_integration_care_key";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null, string? code = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        var body = string.IsNullOrEmpty(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
        if (code is not null) Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    [GeneratedRegex(@"^RCP-\d{4}-\d{5}$")]
    private static partial Regex ReceiptPattern();

    [GeneratedRegex(@"^care_[0-9a-f]{32}_[0-9a-f]{16}$")]
    private static partial Regex ReferencePattern();

    /// <summary>A clinic on the Clinic plan with a cashier, a front-desk clerk, an admin, a doctor and one patient whose visit bill holds ₦5,000.</summary>
    private sealed record World(CareKit.Clinic Clinic, HttpClient Cashier, HttpClient Desk, HttpClient Admin, HttpClient Doctor, Guid PatientId, Guid BillId, Guid ItemId);

    private async Task<World> WorldAsync(string name, long amountMinor = 500_000)
    {
        var clinic = await kit.ClinicAsync(name);
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier"); // moves the clinic to the Clinic plan
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var itemId = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", amountMinor);
        var billId = await OpenBillAsync(clinic, patientId);
        await AddLineAsync(clinic, billId, itemId);
        return new World(clinic, cashier, desk, admin, doctor, patientId, billId, itemId);
    }

    private Task<Guid> OpenBillAsync(CareKit.Clinic clinic, Guid patientId) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct)).Id);

    private Task<Guid> AddLineAsync(CareKit.Clinic clinic, Guid billId, Guid itemId, string? clinicalRef = null) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, itemId, 1, clinicalRef, Ct)).Id);

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid billId, long amountMinor, string method = "Cash", string? reference = null, string? idempotencyKey = null) =>
        client.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor, method, reference, idempotencyKey }, Ct);

    private static async Task<JsonElement> BillAsync(HttpClient client, Guid billId) => await Json(await client.GetAsync($"/api/care/bills/{billId}", Ct));

    private Task<List<CarePayment>> PaymentsAsync(Guid billId) =>
        api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().Where(p => p.BillId == billId).ToListAsync(Ct));

    // ---------- Online helpers ----------

    private static Task<HttpResponseMessage> SetKeyAsync(HttpClient client, string key = CareKey) =>
        client.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = key }, Ct);

    private Task<Guid> OwnerIdAsync(CareKit.Clinic clinic) => api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
        .Where(m => m.TenantId == clinic.TenantId && m.Role == Saas.Domain.Core.MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));

    /// <summary>Starts a portal checkout (Task 15 maps the portal route onto this service call) and returns the Paystack reference.</summary>
    private async Task<string> CheckoutAsync(World w, long? amountMinor = null)
    {
        var payer = await OwnerIdAsync(w.Clinic);
        var result = await api.AsTenantAsync<OnlinePaymentService, CareCheckoutResult>(w.Clinic.TenantId, s => s.StartCheckoutAsync(w.BillId, payer, amountMinor, Ct));
        return result.Reference;
    }

    private Task<HttpResponseMessage> WebhookAsync(Guid tenantId, string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/paystack/care/{tenantId}")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        if (signature is not null) request.Headers.Add("x-paystack-signature", signature);
        return api.CreateClient().SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string reference) =>
        client.PostAsync($"/api/care/payments/online/{reference}/verify", null, Ct);

    private void Charge(string reference, long amountMinor, string currency = "NGN", string? inAccountOfKey = null)
    {
        api.Merchant.Charges[reference] = new Saas.Application.Billing.VerifiedCharge(reference, amountMinor, currency, DateTimeOffset.UtcNow);
        if (inAccountOfKey is not null) api.Merchant.ChargeKeys[reference] = inAccountOfKey;
    }

    // ---------- Desk payments ----------

    [Fact]
    public async Task Desk_payment_issues_receipt_and_updates_balance()
    {
        var w = await WorldAsync("Desk Pay Clinic");

        // Front desk and doctors don't take bill payments; amount and method are checked.
        await AssertProblemAsync(await PayAsync(w.Desk, w.BillId, 200_000), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await PayAsync(w.Doctor, w.BillId, 200_000), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await PayAsync(w.Cashier, w.BillId, 0), HttpStatusCode.BadRequest, field: "amountMinor");
        await AssertProblemAsync(await PayAsync(w.Cashier, w.BillId, 200_000, "Online"), HttpStatusCode.BadRequest, field: "method");
        await AssertProblemAsync(await PayAsync(w.Cashier, Guid.NewGuid(), 200_000), HttpStatusCode.NotFound);

        var first = await Json(await PayAsync(w.Cashier, w.BillId, 200_000, "Pos", "POS-1182"));
        Assert.Matches(ReceiptPattern(), first.GetProperty("receiptNumber").GetString());
        Assert.EndsWith("-00001", first.GetProperty("receiptNumber").GetString());
        Assert.Equal("Succeeded", first.GetProperty("status").GetString());
        Assert.Equal("POS-1182", first.GetProperty("reference").GetString());

        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(200_000, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal(300_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("PartPaid", bill.GetProperty("status").GetString());
        var listed = Assert.Single(bill.GetProperty("payments").EnumerateArray());
        Assert.Equal(first.GetProperty("id").GetGuid(), listed.GetProperty("id").GetGuid());

        var second = await Json(await PayAsync(w.Admin, w.BillId, 300_000, "Transfer", "TRF-77"));
        Assert.EndsWith("-00002", second.GetProperty("receiptNumber").GetString());
        bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(0, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(0, bill.GetProperty("creditMinor").GetInt64());
        Assert.Equal("Paid", bill.GetProperty("status").GetString());

        // The receipt PDF: any clinic member, private and uncached; another clinic gets 404.
        var paymentId = first.GetProperty("id").GetGuid();
        foreach (var client in new[] { w.Cashier, w.Desk, w.Clinic.Owner })
        {
            var pdf = await client.GetAsync($"/api/care/payments/{paymentId}/receipt.pdf", Ct);
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", pdf.Headers.CacheControl?.ToString());
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(Ct), 0, 4));
        }
        var other = await kit.ClinicAsync("Other Desk Pay Clinic");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.GetAsync($"/api/care/payments/{paymentId}/receipt.pdf", Ct)).StatusCode);
        await AssertProblemAsync(await PayAsync(other.Owner, w.BillId, 100), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Desk_payment_idempotency_key_records_once()
    {
        var w = await WorldAsync("Idempotent Pay Clinic");
        var key = $"desk-{Guid.NewGuid():N}";

        // The same key sent five times at once (a double-click, a retry on a slow line) records one payment and returns it each time.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PayAsync(w.Cashier, w.BillId, 150_000, idempotencyKey: key)));
        var ids = new List<Guid>();
        foreach (var r in responses) ids.Add((await Json(r)).GetProperty("id").GetGuid());
        Assert.Single(ids.Distinct());
        var payment = Assert.Single(await PaymentsAsync(w.BillId));
        Assert.Equal(key, payment.IdempotencyKey);
        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(150_000, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal(350_000, bill.GetProperty("balanceMinor").GetInt64());

        // A later retry returns the same receipt; the same key for a different payment is refused; the receipt counter was used once.
        var again = await Json(await PayAsync(w.Cashier, w.BillId, 150_000, idempotencyKey: key));
        Assert.Equal(payment.ReceiptNumber, again.GetProperty("receiptNumber").GetString());
        await AssertProblemAsync(await PayAsync(w.Cashier, w.BillId, 90_000, idempotencyKey: key), HttpStatusCode.Conflict, code: "idempotency_key_reused");
        var next = await Json(await PayAsync(w.Cashier, w.BillId, 10_000));
        Assert.EndsWith("-00002", next.GetProperty("receiptNumber").GetString());
        Assert.Equal(160_000, (await BillAsync(w.Cashier, w.BillId)).GetProperty("paidMinor").GetInt64());
    }

    [Fact]
    public async Task Racing_desk_payments_all_count_under_the_bill_lock()
    {
        var w = await WorldAsync("Racing Pay Clinic");
        // One scope loads the bill first (a tracked, soon stale row); four payments race in from other requests; the scope's own payment
        // must re-read the paid amount once it holds the lock.
        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Saas.Application.Common.ITenantContext>().SetTenant(w.Clinic.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<Saas.Application.Common.IAppDbContext>();
        Assert.Equal(0, (await db.PatientBills.FirstAsync(b => b.Id == w.BillId, Ct)).PaidMinor);
        foreach (var r in await Task.WhenAll(Enumerable.Range(0, 4).Select(i => PayAsync(w.Cashier, w.BillId, 50_000, reference: $"R{i}")))) await Json(r);
        var stale = await scope.ServiceProvider.GetRequiredService<PaymentService>().RecordDeskAsync(w.BillId, 50_000, PayMethod.Cash, null, Ct);
        Assert.NotNull(stale.ReceiptNumber);

        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(250_000, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal(250_000, bill.GetProperty("balanceMinor").GetInt64());
        var numbers = (await PaymentsAsync(w.BillId)).Select(p => p.ReceiptNumber).ToList();
        Assert.Equal(5, numbers.Distinct().Count());
    }

    // ---------- Reversals ----------

    [Fact]
    public async Task Reversal_restores_balance_and_is_audited()
    {
        var w = await WorldAsync("Reversal Clinic");
        var paid = await Json(await PayAsync(w.Cashier, w.BillId, 500_000, "Transfer", "TRF-9"));
        var paymentId = paid.GetProperty("id").GetGuid();
        Assert.Equal("Paid", (await BillAsync(w.Cashier, w.BillId)).GetProperty("status").GetString());

        // Who and why: the front desk and doctors can't; a reason of 5 to 300 characters is needed.
        await AssertProblemAsync(await w.Desk.PostAsJsonAsync($"/api/care/payments/{paymentId}/reverse", new { reason = "Bounced transfer" }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync($"/api/care/payments/{paymentId}/reverse", new { reason = "Bounced transfer" }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await w.Cashier.PostAsJsonAsync($"/api/care/payments/{paymentId}/reverse", new { reason = "no" }, Ct), HttpStatusCode.BadRequest, field: "reason");

        var reversed = await Json(await w.Cashier.PostAsJsonAsync($"/api/care/payments/{paymentId}/reverse", new { reason = "Bounced transfer TRF-9" }, Ct));
        Assert.Equal("Reversed", reversed.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal("Bounced transfer TRF-9", reversed.GetProperty("payment").GetProperty("reversalReason").GetString());
        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(0, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal(500_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("Unpaid", bill.GetProperty("status").GetString());

        // Twice is refused; the reversed receipt still prints.
        await AssertProblemAsync(await w.Cashier.PostAsJsonAsync($"/api/care/payments/{paymentId}/reverse", new { reason = "Bounced transfer TRF-9" }, Ct),
            HttpStatusCode.Conflict, code: "payment_not_received");
        Assert.Equal(HttpStatusCode.OK, (await w.Cashier.GetAsync($"/api/care/payments/{paymentId}/receipt.pdf", Ct)).StatusCode);

        // Audited with ids and amounts only: the typed reason never reaches the audit log.
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId).ToListAsync(Ct));
        var audit = Assert.Single(audits, a => a.Action == "care.payment_reversed");
        Assert.Equal(paymentId.ToString(), audit.EntityId);
        Assert.Contains("500000", audit.AfterJson);
        Assert.Contains(w.BillId.ToString(), audit.AfterJson);
        Assert.DoesNotContain(audits, a => (a.AfterJson ?? "").Contains("Bounced", StringComparison.Ordinal) || (a.BeforeJson ?? "").Contains("Bounced", StringComparison.Ordinal));
    }

    // ---------- Adjustments ----------

    [Fact]
    public async Task Discount_and_waiver_adjust_the_balance_and_a_waiver_covers_only_what_was_billed()
    {
        var w = await WorldAsync("Adjustment Clinic");
        var path = $"/api/care/bills/{w.BillId}/adjustments";

        // Leadership only (cashier, front desk, doctor: 403); kind, amount and reason are checked.
        foreach (var client in new[] { w.Cashier, w.Desk, w.Doctor })
            await AssertProblemAsync(await client.PostAsJsonAsync(path, new { kind = "Discount", amountMinor = 100_000, reason = "Staff family" }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await w.Admin.PostAsJsonAsync(path, new { kind = "Discount", amountMinor = 100_000, reason = "" }, Ct), HttpStatusCode.BadRequest, field: "reason");
        await AssertProblemAsync(await w.Admin.PostAsJsonAsync(path, new { kind = "Discount", amountMinor = 0, reason = "Staff family" }, Ct), HttpStatusCode.BadRequest, field: "amountMinor");
        await AssertProblemAsync(await w.Admin.PostAsJsonAsync(path, new { kind = "Discount", amountMinor = 500_001, reason = "Staff family" }, Ct), HttpStatusCode.BadRequest, field: "amountMinor");

        // A ₦1,000 discount: the net amount drops, nothing is paid yet.
        var bill = await Json(await w.Admin.PostAsJsonAsync(path, new { kind = "Discount", amountMinor = 100_000, reason = "Staff family" }, Ct));
        Assert.Equal(100_000, bill.GetProperty("adjustedMinor").GetInt64());
        Assert.Equal(400_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("Unpaid", bill.GetProperty("status").GetString());
        await Json(await PayAsync(w.Cashier, w.BillId, 100_000));
        Assert.Equal("PartPaid", (await BillAsync(w.Cashier, w.BillId)).GetProperty("status").GetString());

        // The director waives the rest: Waived, balance 0.
        bill = await Json(await w.Clinic.Owner.PostAsJsonAsync(path, new { kind = "Waiver", amountMinor = 300_000, reason = "Hardship" }, Ct));
        Assert.Equal("Waived", bill.GetProperty("status").GetString());
        Assert.Equal(0, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(400_000, bill.GetProperty("adjustedMinor").GetInt64());
        Assert.Equal(2, bill.GetProperty("adjustments").GetArrayLength());
        // Nothing left to adjust.
        await AssertProblemAsync(await w.Admin.PostAsJsonAsync(path, new { kind = "Waiver", amountMinor = 1, reason = "Hardship" }, Ct), HttpStatusCode.Conflict);

        // A later charge is owed: the waiver covered only what was on the bill when it was granted.
        await AddLineAsync(w.Clinic, w.BillId, await kit.PriceItemAsync(w.Clinic, "Malaria test", "Test", 200_000));
        bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(200_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("PartPaid", bill.GetProperty("status").GetString());
        Assert.Equal(700_000, bill.GetProperty("totalMinor").GetInt64());

        // A discount that clears the balance with nothing paid also reads as waived.
        var second = await OpenBillAsync(w.Clinic, w.PatientId);
        await AddLineAsync(w.Clinic, second, w.ItemId);
        bill = await Json(await w.Admin.PostAsJsonAsync($"/api/care/bills/{second}/adjustments", new { kind = "Discount", amountMinor = 500_000, reason = "Free clinic day" }, Ct));
        Assert.Equal("Waived", bill.GetProperty("status").GetString());

        // Audited with ids and amounts only.
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.bill_adjusted").ToListAsync(Ct));
        Assert.Equal(3, audits.Count);
        Assert.All(audits, a => Assert.DoesNotContain("Hardship", a.AfterJson ?? ""));
        Assert.Contains(audits, a => (a.AfterJson ?? "").Contains("300000") && (a.AfterJson ?? "").Contains("Waiver"));
    }

    // ---------- Deposits as credit ----------

    [Fact]
    public async Task Deposit_on_a_zero_total_admission_bill_shows_as_credit()
    {
        var clinic = await kit.ClinicAsync("Credit Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var wardId = await kit.WardAsync(clinic, "Credit ward", bedPriceMinor: 1_000_000);
        var bedId = await kit.BedAsync(clinic, wardId, "C1");
        var admissionId = await kit.AdmitAsync(clinic.Owner, patientId);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId, 3_000_000));

        var billId = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(b => b.AdmissionId == admissionId).Select(b => b.Id).SingleAsync(Ct));
        var bill = await BillAsync(cashier, billId);
        Assert.Equal(0, bill.GetProperty("totalMinor").GetInt64());
        Assert.Equal(0, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(3_000_000, bill.GetProperty("creditMinor").GetInt64());
        Assert.Equal("InCredit", bill.GetProperty("status").GetString());
        var listed = Assert.Single((await Json(await cashier.GetAsync($"/api/care/bills?patientId={patientId}", Ct))).EnumerateArray());
        Assert.Equal(3_000_000, listed.GetProperty("creditMinor").GetInt64());
        Assert.Equal("InCredit", listed.GetProperty("status").GetString());

        // Bed days use up the credit; once the charges pass the deposit the bill owes the difference.
        var bedItem = await api.WithDbAsync(db => db.Wards.IgnoreQueryFilters().Where(x => x.Id == wardId).Select(x => x.BedPriceItemId).FirstAsync(Ct));
        await AddLineAsync(clinic, billId, bedItem);
        bill = await BillAsync(cashier, billId);
        Assert.Equal(2_000_000, bill.GetProperty("creditMinor").GetInt64());
        Assert.Equal("InCredit", bill.GetProperty("status").GetString());
        await AddLineAsync(clinic, billId, bedItem);
        await AddLineAsync(clinic, billId, bedItem);
        bill = await BillAsync(cashier, billId);
        Assert.Equal(0, bill.GetProperty("creditMinor").GetInt64());
        Assert.Equal(0, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("Paid", bill.GetProperty("status").GetString());
        await AddLineAsync(clinic, billId, bedItem);
        bill = await BillAsync(cashier, billId);
        Assert.Equal(1_000_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("PartPaid", bill.GetProperty("status").GetString());
    }

    // ---------- Online payment ----------

    [Fact]
    public async Task Online_needs_clinic_plan()
    {
        var clinic = await kit.ClinicAsync("Free Online Clinic"); // Free: online_payments 0
        await AssertProblemAsync(await SetKeyAsync(clinic.Owner), HttpStatusCode.PaymentRequired);
        var settings = await Json(await clinic.Owner.GetAsync("/api/care/settings/paystack", Ct));
        Assert.False(settings.GetProperty("connected").GetBoolean());
        Assert.False(settings.GetProperty("available").GetBoolean());

        // With no key, the portal can't start a checkout.
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var itemId = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        var billId = await OpenBillAsync(clinic, patientId);
        await AddLineAsync(clinic, billId, itemId);
        var owner = await OwnerIdAsync(clinic);
        var refused = await Assert.ThrowsAsync<Saas.Application.Common.ConflictException>(() =>
            api.AsTenantAsync<OnlinePaymentService, CareCheckoutResult>(clinic.TenantId, s => s.StartCheckoutAsync(billId, owner, null, Ct)));
        Assert.Equal(OnlinePaymentService.NotSetUp, refused.Message);

        // On the Clinic plan: the director saves the key (admins and cashiers can't); it's never returned.
        var (admin, _) = await kit.StaffAsync(clinic, "Admin"); // moves the clinic to the Clinic plan
        await AssertProblemAsync(await SetKeyAsync(admin), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await SetKeyAsync(clinic.Owner, "not-a-key"), HttpStatusCode.BadRequest, field: "secretKey");
        settings = await Json(await SetKeyAsync(clinic.Owner));
        Assert.True(settings.GetProperty("connected").GetBoolean());
        Assert.EndsWith($"/api/webhooks/paystack/care/{clinic.TenantId}", settings.GetProperty("webhookUrl").GetString());
        Assert.DoesNotContain(CareKey, settings.ToString());
        var stored = await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(p => p.TenantId == clinic.TenantId).Select(p => p.PaystackSecretKeyProtected).FirstAsync(Ct));
        Assert.NotNull(stored);
        Assert.DoesNotContain(CareKey, stored);

        var started = await api.AsTenantAsync<OnlinePaymentService, CareCheckoutResult>(clinic.TenantId, s => s.StartCheckoutAsync(billId, owner, null, Ct));
        Assert.Matches(ReferencePattern(), started.Reference);
        Assert.StartsWith($"care_{billId:N}_", started.Reference);
        var init = api.Merchant.Initialized.Single(i => i.Reference == started.Reference);
        Assert.Equal(500_000, init.AmountMinor);
        Assert.Equal("NGN", init.Currency);
        var pending = Assert.Single(await PaymentsAsync(billId));
        Assert.Equal(CarePaymentStatus.Pending, pending.Status);
        Assert.Null(pending.ReceiptNumber);
        Assert.Equal(0, (await BillAsync(clinic.Owner, billId)).GetProperty("paidMinor").GetInt64());
    }

    [Fact]
    public async Task Webhook_and_verify_together_record_once()
    {
        var w = await WorldAsync("Race Online Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w, 200_000);
        Charge(reference, 200_000);
        var body = SchoolKit.ChargeSuccessBody(reference, 200_000);
        var signature = SchoolKit.Sign(body, CareKey);

        var calls = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 3; i++)
        {
            calls.Add(WebhookAsync(w.Clinic.TenantId, body, signature));
            calls.Add(VerifyAsync(w.Cashier, reference));
        }
        var responses = await Task.WhenAll(calls);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, body, signature)).StatusCode);

        var payment = Assert.Single(await PaymentsAsync(w.BillId));
        Assert.Equal(CarePaymentStatus.Succeeded, payment.Status);
        Assert.Equal(PayMethod.Online, payment.Method);
        Assert.Matches(ReceiptPattern(), payment.ReceiptNumber);
        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(200_000, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal(300_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.False(bill.GetProperty("needsReview").GetBoolean());
        var status = await Json(await VerifyAsync(w.Cashier, reference));
        Assert.Equal("Succeeded", status.GetProperty("status").GetString());
        Assert.Equal(payment.ReceiptNumber, status.GetProperty("receiptNumber").GetString());

        // A bad signature, or another clinic's key, is 401 and records nothing; another clinic's webhook can't confirm this reference.
        var second = await CheckoutAsync(w, 100_000);
        var secondBody = SchoolKit.ChargeSuccessBody(second, 100_000);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(w.Clinic.TenantId, secondBody, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(w.Clinic.TenantId, secondBody, SchoolKit.Sign(secondBody, "sk_test_someone_elses_key_123"))).StatusCode);
        var other = await kit.ClinicAsync("Other Online Clinic");
        await api.MakeProAsync(other.TenantId, "care", "clinic");
        await Json(await SetKeyAsync(other.Owner, "sk_test_other_clinic_key_4567"));
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(other.TenantId, secondBody, SchoolKit.Sign(secondBody, "sk_test_other_clinic_key_4567"))).StatusCode);
        Assert.Equal(CarePaymentStatus.Pending, (await PaymentsAsync(w.BillId)).Single(p => p.ProviderRef == second).Status);
        // An unknown clinic is 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(Guid.NewGuid(), secondBody, SchoolKit.Sign(secondBody, CareKey))).StatusCode);
    }

    [Fact]
    public async Task Wrong_amount_is_mismatch()
    {
        var w = await WorldAsync("Mismatch Online Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w);
        var body = SchoolKit.ChargeSuccessBody(reference, 50_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, body, SchoolKit.Sign(body, CareKey))).StatusCode);

        var payment = Assert.Single(await PaymentsAsync(w.BillId));
        Assert.Equal(CarePaymentStatus.Mismatch, payment.Status);
        Assert.Equal(50_000, payment.ChargedAmountMinor);
        Assert.Null(payment.ReceiptNumber);
        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(0, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal("Unpaid", bill.GetProperty("status").GetString());
        Assert.Equal("Mismatch", Assert.Single(bill.GetProperty("payments").EnumerateArray()).GetProperty("status").GetString());

        // The director and the cashier are told in the app (bill number and amounts, nothing clinical); a correct retry doesn't apply it.
        var notices = await api.WithDbAsync(db => db.UserNotifications.Where(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.payment_mismatch").ToListAsync(Ct));
        Assert.Equal(2, notices.Count);
        var correct = SchoolKit.ChargeSuccessBody(reference, 500_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, correct, SchoolKit.Sign(correct, CareKey))).StatusCode);
        Assert.Equal(0, (await BillAsync(w.Cashier, w.BillId)).GetProperty("paidMinor").GetInt64());

        // A wrong currency through the return verify is a mismatch too.
        var second = await CheckoutAsync(w);
        Charge(second, 500_000, "USD");
        Assert.Equal("Mismatch", (await Json(await VerifyAsync(w.Cashier, second))).GetProperty("status").GetString());
        Assert.Equal(0, (await BillAsync(w.Cashier, w.BillId)).GetProperty("paidMinor").GetInt64());
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.payment_mismatch").ToListAsync(Ct));
        Assert.Equal(2, audits.Count);
    }

    [Fact]
    public async Task Overpayment_is_recorded_and_flagged()
    {
        var w = await WorldAsync("Overpay Online Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w); // the whole ₦5,000
        // The patient pays ₦3,000 at the desk while the checkout is open; Paystack then confirms the full ₦5,000.
        await Json(await PayAsync(w.Cashier, w.BillId, 300_000));
        Charge(reference, 500_000);
        Assert.Equal("Succeeded", (await Json(await VerifyAsync(w.Cashier, reference))).GetProperty("status").GetString());

        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.Equal(800_000, bill.GetProperty("paidMinor").GetInt64());
        Assert.Equal(0, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(300_000, bill.GetProperty("creditMinor").GetInt64());
        Assert.Equal("InCredit", bill.GetProperty("status").GetString());
        Assert.True(bill.GetProperty("needsReview").GetBoolean());
        Assert.Single(await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.payment_overpaid").ToListAsync(Ct)));

        // The cashier deals with it (refund or keep as credit) and clears the flag; the front desk can't.
        await AssertProblemAsync(await w.Desk.PostAsync($"/api/care/bills/{w.BillId}/reviewed", null, Ct), HttpStatusCode.Forbidden);
        Assert.False((await Json(await w.Cashier.PostAsync($"/api/care/bills/{w.BillId}/reviewed", null, Ct))).GetProperty("needsReview").GetBoolean());
    }

    [Fact]
    public async Task Abandoned_checkout_still_takes_a_late_confirmation()
    {
        var w = await WorldAsync("Abandoned Online Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w);
        api.Merchant.Unpaid[reference] = "abandoned";
        // Two days later the stranded-payment check (Task 13's job) finds Paystack reports it unpaid: Abandoned, quietly.
        await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Ct));
        var changed = await api.AsTenantAsync<OnlinePaymentService, int>(w.Clinic.TenantId, s => s.RecheckPendingAsync(DateTimeOffset.UtcNow, Ct));
        Assert.Equal(1, changed);
        Assert.Equal(CarePaymentStatus.Abandoned, Assert.Single(await PaymentsAsync(w.BillId)).Status);
        Assert.Empty(await api.WithDbAsync(db => db.UserNotifications.Where(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.payment_unverified").ToListAsync(Ct)));

        // The patient's bank completes it after all: the webhook is still applied.
        api.Merchant.Unpaid.Remove(reference);
        var body = SchoolKit.ChargeSuccessBody(reference, 500_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, body, SchoolKit.Sign(body, CareKey))).StatusCode);
        var payment = Assert.Single(await PaymentsAsync(w.BillId));
        Assert.Equal(CarePaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.ReceiptNumber);
        Assert.Equal("Paid", (await BillAsync(w.Cashier, w.BillId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Previous_key_webhook_is_applied_only_when_Paystack_confirms()
    {
        var w = await WorldAsync("Rotated Key Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var forged = await CheckoutAsync(w, 100_000);
        var real = await CheckoutAsync(w, 200_000);
        // The key is rotated (forced: checkouts are open). The old key still signs webhooks for 7 days, but only Paystack's answer counts.
        await AssertProblemAsync(await SetKeyAsync(w.Clinic.Owner, "sk_test_integration_care_key_two"), HttpStatusCode.Conflict, code: "checkouts_open");
        await Json(await w.Clinic.Owner.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = "sk_test_integration_care_key_two", force = true }, Ct));

        var forgedBody = SchoolKit.ChargeSuccessBody(forged, 100_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, forgedBody, SchoolKit.Sign(forgedBody, CareKey))).StatusCode);
        Assert.Equal(CarePaymentStatus.Pending, (await PaymentsAsync(w.BillId)).Single(p => p.ProviderRef == forged).Status);

        Charge(real, 200_000, inAccountOfKey: CareKey);
        var realBody = SchoolKit.ChargeSuccessBody(real, 200_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, realBody, SchoolKit.Sign(realBody, CareKey))).StatusCode);
        Assert.Equal(CarePaymentStatus.Succeeded, (await PaymentsAsync(w.BillId)).Single(p => p.ProviderRef == real).Status);
        Assert.Equal(200_000, (await BillAsync(w.Cashier, w.BillId)).GetProperty("paidMinor").GetInt64());

        // Past the 7 days the old key signs nothing.
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(p => p.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PreviousKeyUntil, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(w.Clinic.TenantId, forgedBody, SchoolKit.Sign(forgedBody, CareKey))).StatusCode);
    }

    // ---------- No clinical references ----------

    [Fact]
    public async Task Cashier_sees_no_clinical_refs_on_bills()
    {
        var w = await WorldAsync("Clean Bill Clinic");
        var drug = await kit.PriceItemAsync(w.Clinic, "Coartem tablets", "Drug", 150_000);
        await AddLineAsync(w.Clinic, w.BillId, drug, "rx-item-secret-ref");
        var paid = await Json(await PayAsync(w.Cashier, w.BillId, 100_000, "Pos", "POS-55"));
        await Json(await w.Admin.PostAsJsonAsync($"/api/care/bills/{w.BillId}/adjustments", new { kind = "Discount", amountMinor = 50_000, reason = "Loyalty" }, Ct));

        // Clinical staff see the line's clinical reference; the cashier and front desk see the price-item name only (existing ruling).
        Assert.Contains("rx-item-secret-ref", await (await w.Clinic.Owner.GetAsync($"/api/care/bills/{w.BillId}", Ct)).Content.ReadAsStringAsync(Ct));
        foreach (var client in new[] { w.Cashier, w.Desk })
        {
            foreach (var path in new[] { $"/api/care/bills/{w.BillId}", $"/api/care/bills?patientId={w.PatientId}" })
            {
                var text = await (await client.GetAsync(path, Ct)).Content.ReadAsStringAsync(Ct);
                Assert.DoesNotContain("rx-item-secret-ref", text);
                Assert.DoesNotContain("clinicalRef", text);
                var names = Names(JsonDocument.Parse(text).RootElement).Distinct().ToList();
                Assert.All(names, n => Assert.False(CareAccessTests.IsClinicalName(n), $"{path}: {n}"));
            }
            var bill = await BillAsync(client, w.BillId);
            Assert.Contains(bill.GetProperty("lines").EnumerateArray(), l => l.GetProperty("description").GetString() == "Coartem tablets");
        }
        var reversal = await Json(await w.Cashier.PostAsJsonAsync($"/api/care/payments/{paid.GetProperty("id").GetGuid()}/reverse", new { reason = "Card declined later" }, Ct));
        Assert.All(Names(reversal).Distinct(), n => Assert.False(CareAccessTests.IsClinicalName(n), n));

        // The receipt is built from fields with no clinical name (it prints no bill lines).
        Assert.All(typeof(CareReceiptData).GetProperties().Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]),
            n => Assert.False(CareAccessTests.IsClinicalName(n), n));
        Assert.DoesNotContain(typeof(CareReceiptData).GetProperties(), p => p.Name.Contains("Line", StringComparison.Ordinal));
    }

    // ---------- Fix round 1 ----------

    private Task<PatientBill> BillRowAsync(Guid billId) => api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().AsNoTracking().FirstAsync(b => b.Id == billId, Ct));

    [Fact]
    public async Task Recalculation_writes_every_column_even_from_a_stale_tracked_bill()
    {
        // I1: a context tracks a zero-total bill (credit 0, Unpaid); another request takes a ₦30,000 deposit (credit 30,000, InCredit);
        // the first context then adds a ₦30,000 line. The recomputed credit (0) equals the stale snapshot, but must still be written.
        var w = await WorldAsync("Stale Bill Clinic");
        var billId = await OpenBillAsync(w.Clinic, w.PatientId);
        var bedDay = await kit.PriceItemAsync(w.Clinic, "Ward bed day", "Bed", 3_000_000);
        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Saas.Application.Common.ITenantContext>().SetTenant(w.Clinic.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<Saas.Application.Common.IAppDbContext>();
        var tracked = await db.PatientBills.FirstAsync(b => b.Id == billId, Ct);
        Assert.Equal(0, tracked.CreditMinor);

        await Json(await PayAsync(w.Cashier, billId, 3_000_000));
        var mid = await BillRowAsync(billId);
        Assert.Equal(BillStatus.InCredit, mid.Status);
        Assert.Equal(3_000_000, mid.CreditMinor);

        await scope.ServiceProvider.GetRequiredService<BillingService>().AddLineAsync(billId, bedDay, 1, null, Ct);
        var row = await BillRowAsync(billId);
        Assert.Equal(BillStatus.Paid, row.Status);
        Assert.Equal(0, row.CreditMinor);
        Assert.Equal(0, row.BalanceMinor);
        Assert.Equal(3_000_000, row.PaidMinor);
        Assert.Equal(3_000_000, row.TotalMinor);

        // The balance variant: the context tracks a bill owing ₦5,000 (Unpaid); ₦2,500 is paid elsewhere (database: ₦2,500 owed, PartPaid);
        // the context adds a ₦2,500 line. The right balance, ₦5,000, equals the stale snapshot and must still be written.
        var second = await OpenBillAsync(w.Clinic, w.PatientId);
        await AddLineAsync(w.Clinic, second, w.ItemId);
        var half = await kit.PriceItemAsync(w.Clinic, "Dressing", "Procedure", 250_000);
        var stale = await db.PatientBills.FirstAsync(b => b.Id == second, Ct);
        Assert.Equal(500_000, stale.BalanceMinor);
        await Json(await PayAsync(w.Cashier, second, 250_000));
        await scope.ServiceProvider.GetRequiredService<BillingService>().AddLineAsync(second, half, 1, null, Ct);
        row = await BillRowAsync(second);
        Assert.Equal((750_000L, 250_000L, 500_000L, BillStatus.PartPaid), (row.TotalMinor, row.PaidMinor, row.BalanceMinor, row.Status));

        // A desk payment and a reversal from the same (stale again) context keep every column in step with the database.
        await Json(await PayAsync(w.Cashier, second, 250_000)); // database: ₦2,500 owed
        var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
        var mine = await payments.RecordDeskAsync(second, 250_000, PayMethod.Cash, null, Ct);
        row = await BillRowAsync(second);
        Assert.Equal((BillStatus.Paid, 0L, 750_000L), (row.Status, row.BalanceMinor, row.PaidMinor));
        await Json(await PayAsync(w.Cashier, second, 100_000)); // database: ₦1,000 credit, InCredit
        await payments.ReverseAsync(mine.Id, "Counted twice by mistake", Ct);
        row = await BillRowAsync(second);
        Assert.Equal((BillStatus.PartPaid, 150_000L, 600_000L, 0L), (row.Status, row.BalanceMinor, row.PaidMinor, row.CreditMinor));
    }

    [Fact]
    public async Task Mismatch_flags_the_bill_for_review()
    {
        var w = await WorldAsync("Mismatch Review Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w);
        var body = SchoolKit.ChargeSuccessBody(reference, 50_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(w.Clinic.TenantId, body, SchoolKit.Sign(body, CareKey))).StatusCode);
        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.True(bill.GetProperty("needsReview").GetBoolean());
        Assert.Equal("Unpaid", bill.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Staff_verify_is_refused_in_support_view()
    {
        var w = await WorldAsync("Support Verify Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w);
        Charge(reference, 500_000);

        // Over HTTP the read-only token is refused before the route.
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, Saas.Domain.Core.PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{w.Clinic.TenantId}/view-as", new { reason = "Checking a reported payment problem." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());
        await AssertProblemAsync(await VerifyAsync(viewer, reference), HttpStatusCode.Forbidden);

        // The service refuses a support view itself (the owner's identity with the view-as claim), so nothing is confirmed.
        var ownerId = await OwnerIdAsync(w.Clinic);
        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Saas.Application.Common.ITenantContext>().SetTenant(w.Clinic.TenantId);
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", ownerId.ToString()), new System.Security.Claims.Claim("role", "Owner"),
                new System.Security.Claims.Claim("vas", Guid.NewGuid().ToString()),
            ], "test")),
        };
        await Assert.ThrowsAsync<Saas.Application.Common.ForbiddenException>(() =>
            scope.ServiceProvider.GetRequiredService<OnlinePaymentService>().VerifyForStaffAsync(reference, Ct));
        Assert.Equal(CarePaymentStatus.Pending, Assert.Single(await PaymentsAsync(w.BillId)).Status);
    }

    [Fact]
    public async Task Reversing_the_overpayment_clears_the_review_flag()
    {
        var w = await WorldAsync("Overpay Reverse Clinic");
        await Json(await SetKeyAsync(w.Clinic.Owner));
        var reference = await CheckoutAsync(w);
        await Json(await PayAsync(w.Cashier, w.BillId, 300_000));
        Charge(reference, 500_000);
        await Json(await VerifyAsync(w.Cashier, reference));
        Assert.True((await BillAsync(w.Cashier, w.BillId)).GetProperty("needsReview").GetBoolean());

        // The online payment is refunded and reversed: no surplus is left, so the flag clears.
        var online = (await PaymentsAsync(w.BillId)).Single(p => p.ProviderRef == reference);
        await Json(await w.Cashier.PostAsJsonAsync($"/api/care/payments/{online.Id}/reverse", new { reason = "Refunded through Paystack" }, Ct));
        var bill = await BillAsync(w.Cashier, w.BillId);
        Assert.False(bill.GetProperty("needsReview").GetBoolean());
        Assert.Equal(0, bill.GetProperty("creditMinor").GetInt64());
        Assert.Equal(200_000, bill.GetProperty("balanceMinor").GetInt64());

        // A pending Mismatch keeps the flag even when a reversal leaves no credit.
        var other = await WorldAsync("Mismatch Reverse Clinic");
        await Json(await SetKeyAsync(other.Clinic.Owner));
        var wrong = await CheckoutAsync(other);
        var body = SchoolKit.ChargeSuccessBody(wrong, 10_000);
        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(other.Clinic.TenantId, body, SchoolKit.Sign(body, CareKey))).StatusCode);
        var desk = await Json(await PayAsync(other.Cashier, other.BillId, 100_000));
        await Json(await other.Cashier.PostAsJsonAsync($"/api/care/payments/{desk.GetProperty("id").GetGuid()}/reverse", new { reason = "Wrong patient" }, Ct));
        Assert.True((await BillAsync(other.Cashier, other.BillId)).GetProperty("needsReview").GetBoolean());
    }

    [Fact]
    public async Task Receipt_shows_reversed_and_the_date_not_the_reason()
    {
        var w = await WorldAsync("Receipt Reason Clinic");
        var paid = await Json(await PayAsync(w.Cashier, w.BillId, 200_000));
        var paymentId = paid.GetProperty("id").GetGuid();
        await Json(await w.Cashier.PostAsJsonAsync($"/api/care/payments/{paymentId}/reverse", new { reason = "Patient said malaria drugs were not given" }, Ct));

        var data = await api.AsTenantAsync<PaymentService, CareReceiptData>(w.Clinic.TenantId, s => s.ReceiptDataAsync(paymentId, Ct));
        var today = CareCalendar.Today(DateTimeOffset.UtcNow).ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"Reversed on {today}", data.ReversedText);
        Assert.DoesNotContain("malaria", System.Text.Json.JsonSerializer.Serialize(data with { Logo = null }), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await w.Cashier.GetAsync($"/api/care/payments/{paymentId}/receipt.pdf", Ct)).StatusCode);
    }

    private static IEnumerable<string> Names(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    yield return p.Name;
                    foreach (var n in Names(p.Value)) yield return n;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var n in Names(item)) yield return n;
                break;
        }
    }
}
