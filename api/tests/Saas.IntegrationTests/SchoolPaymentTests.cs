using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Domain.School;

namespace Saas.IntegrationTests;

/// <summary>SchoolLink payments: online (the school's own Paystack key), manual, receipts and reversals.</summary>
[Collection(nameof(ApiCollection))]
public sealed partial class SchoolPaymentTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly SchoolKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(expected == r.StatusCode, $"expected {(int)expected}, got {(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private sealed record Family(SchoolKit.School School, Guid ClassId, Guid StudentId, Guid InvoiceId, HttpClient Parent, string GuardianEmail);

    /// <summary>A school with one student whose term invoice is the tuition, a guardian with an email and their linked parent account.</summary>
    private async Task<Family> FamilyAsync(string name, bool online = true, long tuition = 10_000_000)
    {
        var school = await kit.SchoolAsync(name);
        if (online)
        {
            await api.MakeProAsync(school.TenantId, "school", "standard");
            await kit.SetPaystackKeyAsync(school);
        }
        var classId = await kit.ClassAsync(school);
        var studentId = await kit.StudentAsync(school, classId, "Ada");
        var email = $"guardian-{Guid.NewGuid():N}@example.com";
        var guardianId = await kit.GuardianAsync(school, studentId, "Ngozi Obi", null, email);
        var (parent, _) = await kit.ParentAsync(school, guardianId);

        var levelId = await api.WithDbAsync(db => db.SchoolClasses.IgnoreQueryFilters().Where(c => c.Id == classId).Select(c => c.LevelId).FirstAsync(Ct));
        var termId = await api.WithDbAsync(db => db.Terms.IgnoreQueryFilters().Where(t => t.TenantId == school.TenantId && t.IsCurrent).Select(t => t.Id).FirstAsync(Ct));
        var item = (await Json(await school.Owner.PostAsJsonAsync("/api/school/fee-items", new { name = "Tuition", isOptional = false }, Ct))).GetProperty("id").GetGuid();
        await Json(await school.Owner.PutAsJsonAsync($"/api/school/fee-structure/{termId}/{levelId}",
            new { lines = new[] { new { feeItemId = item, amountMinor = tuition } } }, Ct));
        await Json(await school.Owner.PostAsJsonAsync("/api/school/invoices/generate", new { termId }, Ct));
        var invoiceId = (await Json(await school.Owner.GetAsync($"/api/school/invoices?termId={termId}", Ct))).EnumerateArray().Single().GetProperty("id").GetGuid();
        return new Family(school, classId, studentId, invoiceId, parent, email);
    }

    private static Task<HttpResponseMessage> PayAsync(Family f, long amountMinor) =>
        f.Parent.PostAsJsonAsync($"/api/parent/children/{f.StudentId}/invoices/{f.InvoiceId}/pay", new { amountMinor }, Ct);

    private static async Task<string> StartPaymentAsync(Family f, long amountMinor) =>
        (await Json(await PayAsync(f, amountMinor))).GetProperty("reference").GetString()!;

    private static async Task<JsonElement> StaffInvoiceAsync(Family f) => await Json(await f.School.Owner.GetAsync($"/api/school/invoices/{f.InvoiceId}", Ct));

    private static async Task<JsonElement> ParentInvoiceAsync(Family f) =>
        (await Json(await f.Parent.GetAsync($"/api/parent/children/{f.StudentId}/invoices", Ct))).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == f.InvoiceId);

    private static Task<HttpResponseMessage> ManualAsync(HttpClient staff, Guid invoiceId, long amountMinor, string method = "Transfer", string? reference = null, DateOnly? paidOn = null) =>
        staff.PostAsJsonAsync($"/api/school/invoices/{invoiceId}/payments",
            new { amountMinor, method, reference, paidOn = paidOn ?? DateOnly.FromDateTime(DateTime.UtcNow) }, Ct);

    private Task<List<FeePayment>> PaymentsAsync(Guid invoiceId) =>
        api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.InvoiceId == invoiceId).ToListAsync(Ct));

    [GeneratedRegex(@"^sfee_[0-9a-f]{32}$")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"^RCT-\d{4}-00001$")]
    private static partial Regex FirstReceipt();

    [Fact]
    public async Task Parent_pays_part_of_an_invoice_online_and_gets_a_receipt()
    {
        var f = await FamilyAsync("Online Pay Academy");
        var view = await ParentInvoiceAsync(f);
        Assert.True(view.GetProperty("canPayOnline").GetBoolean());
        Assert.Equal(10_000_000L, view.GetProperty("balance").GetInt64());

        var started = await Json(await PayAsync(f, 3_000_000));
        var reference = started.GetProperty("reference").GetString()!;
        Assert.Matches(ReferencePattern(), reference);
        Assert.EndsWith(reference, started.GetProperty("authorizationUrl").GetString());
        var init = api.Merchant.Initialized.Single(i => i.Reference == reference);
        Assert.Equal(3_000_000L, init.AmountMinor);
        Assert.Equal("NGN", init.Currency);
        Assert.Contains($"/parent/pay/return?reference={reference}", init.CallbackUrl);
        var pending = Assert.Single(await PaymentsAsync(f.InvoiceId));
        Assert.Equal(FeePaymentStatus.Pending, pending.Status);
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());

        kit.Charge(reference, 3_000_000);
        var verified = await Json(await f.Parent.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct));
        Assert.Equal("Succeeded", verified.GetProperty("status").GetString());
        Assert.Matches(FirstReceipt(), verified.GetProperty("receiptNumber").GetString());

        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal(3_000_000L, invoice.GetProperty("paid").GetInt64());
        Assert.Equal(7_000_000L, invoice.GetProperty("balance").GetInt64());
        Assert.Equal("PartlyPaid", invoice.GetProperty("status").GetString());
        var payment = Assert.Single(invoice.GetProperty("payments").EnumerateArray());
        Assert.Equal("Online", payment.GetProperty("method").GetString());
        var paymentId = payment.GetProperty("id").GetGuid();

        var emails = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.receipt" && n.Recipient == f.GuardianEmail).ToListAsync(Ct));
        var email = Assert.Single(emails);
        Assert.Contains("/parent", email.Payload["link"]);
        Assert.Equal(verified.GetProperty("receiptNumber").GetString(), email.Payload["receipt"]);

        var pdf = await f.Parent.GetAsync($"/api/parent/payments/{paymentId}/receipt.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", pdf.Headers.CacheControl?.ToString());
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(Ct), 0, 4));
        var staffPdf = await f.School.Owner.GetAsync($"/api/school/payments/{paymentId}/receipt.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, staffPdf.StatusCode);
        Assert.Contains("no-store", staffPdf.Headers.CacheControl?.ToString());

        // Another family's parent learns nothing about the payment.
        var (stranger, _, _) = await kit.ParentAccountAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/parent/payments/{paymentId}/receipt.pdf", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/parent/children/{f.StudentId}/invoices/{f.InvoiceId}/pay", new { amountMinor = 3_000_000L }, Ct)).StatusCode);

        // The FeePayment rows are audited.
        Assert.True(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.EntityType == nameof(FeePayment) && a.EntityId == paymentId.ToString(), Ct)));
    }

    [Fact]
    public async Task Webhook_and_verify_together_record_one_payment()
    {
        var f = await FamilyAsync("Race Pay Academy");
        var reference = await StartPaymentAsync(f, 2_500_000);
        kit.Charge(reference, 2_500_000);
        var body = SchoolKit.ChargeSuccessBody(reference, 2_500_000);
        var signature = SchoolKit.Sign(body);

        var calls = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 3; i++)
        {
            calls.Add(kit.WebhookAsync(f.School.TenantId, body, signature));
            calls.Add(f.Parent.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct));
        }
        var responses = await Task.WhenAll(calls);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // A late retry from Paystack changes nothing either.
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, body, signature)).StatusCode);

        var payment = Assert.Single(await PaymentsAsync(f.InvoiceId));
        Assert.Equal(FeePaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.ReceiptNumber);
        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal(2_500_000L, invoice.GetProperty("paid").GetInt64());
        Assert.Equal(7_500_000L, invoice.GetProperty("balance").GetInt64());
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.receipt" && n.Recipient == f.GuardianEmail, Ct)));
        Assert.Equal(1, await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == f.School.TenantId && p.ReceiptNumber != null, Ct)));
    }

    [Fact]
    public async Task Bad_signature_is_401_and_records_nothing()
    {
        var f = await FamilyAsync("Forged Pay Academy");
        var reference = await StartPaymentAsync(f, 2_000_000);
        var body = SchoolKit.ChargeSuccessBody(reference, 2_000_000);

        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, body, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, body, "00ff")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body, "sk_test_someone_elses_key_123"))).StatusCode);
        // The platform's own key doesn't sign a school's webhook either.
        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body, ApiFixture.PaystackSecret))).StatusCode);

        var payment = Assert.Single(await PaymentsAsync(f.InvoiceId));
        Assert.Equal(FeePaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ReceiptNumber);
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());

        // Another school, validly signed with its own key, can't confirm this school's reference.
        var other = await kit.SchoolAsync("Other Pay Academy");
        await api.MakeProAsync(other.TenantId, "school", "standard");
        await kit.SetPaystackKeyAsync(other, "sk_test_other_school_key_4567");
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(other.TenantId, body, SchoolKit.Sign(body, "sk_test_other_school_key_4567"))).StatusCode);
        Assert.Equal(FeePaymentStatus.Pending, Assert.Single(await PaymentsAsync(f.InvoiceId)).Status);

        // An unknown reference is acknowledged and ignored.
        var unknown = SchoolKit.ChargeSuccessBody($"sfee_{Guid.NewGuid():N}", 2_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, unknown, SchoolKit.Sign(unknown))).StatusCode);
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
    }

    [Fact]
    public async Task Wrong_amount_is_not_applied_and_alerts_the_bursar()
    {
        var f = await FamilyAsync("Mismatch Pay Academy");
        var (_, bursarId) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var bursarEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == bursarId).Select(u => u.Email).FirstAsync(Ct));
        var ownerEmail = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == f.School.TenantId && m.Role == MemberRole.Owner).Select(m => m.User!.Email).FirstAsync(Ct));

        var reference = await StartPaymentAsync(f, 3_000_000);
        var body = SchoolKit.ChargeSuccessBody(reference, 300_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).StatusCode);

        var payment = Assert.Single(await PaymentsAsync(f.InvoiceId));
        Assert.Equal(FeePaymentStatus.Mismatch, payment.Status);
        Assert.Null(payment.ReceiptNumber);
        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal(0L, invoice.GetProperty("paid").GetInt64());
        Assert.Equal("Unpaid", invoice.GetProperty("status").GetString());

        var alerts = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.payment_mismatch" && n.TenantId == f.School.TenantId).ToListAsync(Ct));
        Assert.Equal(new[] { bursarEmail, ownerEmail }.Order(), alerts.Select(a => a.Recipient).Order());
        Assert.Equal(0, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.receipt" && n.Recipient == f.GuardianEmail, Ct)));

        // A wrong currency is not applied either, and the return verify doesn't apply a mismatched charge later.
        var second = await StartPaymentAsync(f, 3_000_000);
        kit.Charge(second, 3_000_000, "USD");
        var verified = await Json(await f.Parent.PostAsync($"/api/parent/payments/{second}/verify", null, Ct));
        Assert.Equal("Mismatch", verified.GetProperty("status").GetString());
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
        var correct = SchoolKit.ChargeSuccessBody(second, 3_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, correct, SchoolKit.Sign(correct))).StatusCode);
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
        Assert.Equal(4, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.payment_mismatch" && n.TenantId == f.School.TenantId, Ct)));
    }

    [Fact]
    public async Task Minimum_part_payment_is_1000_naira_unless_the_balance_is_smaller()
    {
        var f = await FamilyAsync("Minimum Pay Academy");
        var tooSmall = await ProblemAsync(await PayAsync(f, 99_999), HttpStatusCode.BadRequest);
        Assert.True(tooSmall.GetProperty("errors").TryGetProperty("amountMinor", out _));
        await Json(await PayAsync(f, 100_000));

        // Instalments: the next instalment's outstanding amount may be paid even when it's below the minimum.
        var sessionEnd = await api.WithDbAsync(db => db.AcademicSessions.IgnoreQueryFilters().Where(s => s.TenantId == f.School.TenantId && s.IsCurrent).Select(s => s.EndDate).FirstAsync(Ct));
        await Json(await f.School.Owner.PutAsJsonAsync($"/api/school/invoices/{f.InvoiceId}/instalments", new[]
        {
            new { dueDate = sessionEnd.AddDays(-60), amountMinor = 9_960_000L },
            new { dueDate = sessionEnd.AddDays(-30), amountMinor = 40_000L },
        }, Ct));
        await Json(await ManualAsync(f.School.Owner, f.InvoiceId, 9_900_000));
        // Balance 100_000; the first instalment has 60_000 outstanding.
        await Json(await PayAsync(f, 60_000));
        Assert.True((await ProblemAsync(await PayAsync(f, 70_000), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("amountMinor", out _));

        // Balance below the minimum: the whole balance may be paid, and nothing less (except the next instalment).
        await Json(await ManualAsync(f.School.Owner, f.InvoiceId, 50_000));
        Assert.Equal(50_000L, (await ParentInvoiceAsync(f)).GetProperty("balance").GetInt64());
        await Json(await PayAsync(f, 50_000));
        await Json(await PayAsync(f, 10_000)); // the first instalment's last 10_000
        Assert.True((await ProblemAsync(await PayAsync(f, 20_000), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("amountMinor", out _));
        Assert.True((await ProblemAsync(await PayAsync(f, 0), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("amountMinor", out _));
    }

    [Fact]
    public async Task Overpayment_is_refused()
    {
        var f = await FamilyAsync("Overpay Academy", tuition: 1_000_000);
        var online = await ProblemAsync(await PayAsync(f, 1_000_001), HttpStatusCode.BadRequest);
        Assert.True(online.GetProperty("errors").TryGetProperty("amountMinor", out _));

        var manual = await ProblemAsync(await ManualAsync(f.School.Owner, f.InvoiceId, 1_000_001), HttpStatusCode.BadRequest);
        Assert.True(manual.GetProperty("errors").TryGetProperty("amountMinor", out _));
        await Json(await ManualAsync(f.School.Owner, f.InvoiceId, 600_000, "Cash"));
        Assert.True((await ProblemAsync(await ManualAsync(f.School.Owner, f.InvoiceId, 400_001, "Pos"), HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("amountMinor", out _));

        var future = await ProblemAsync(await ManualAsync(f.School.Owner, f.InvoiceId, 100_000, paidOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)), HttpStatusCode.BadRequest);
        Assert.True(future.GetProperty("errors").TryGetProperty("paidOn", out _));
        var badMethod = await ProblemAsync(await ManualAsync(f.School.Owner, f.InvoiceId, 100_000, "Online"), HttpStatusCode.BadRequest);
        Assert.True(badMethod.GetProperty("errors").TryGetProperty("method", out _));

        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await ManualAsync(teacher, f.InvoiceId, 100_000)).StatusCode);

        await api.WithDbAsync(async db =>
        {
            var invoice = await db.StudentInvoices.IgnoreQueryFilters().FirstAsync(i => i.Id == f.InvoiceId, Ct);
            invoice.Status = InvoiceStatus.Cancelled;
            return await db.SaveChangesAsync(Ct);
        });
        await ProblemAsync(await ManualAsync(f.School.Owner, f.InvoiceId, 100_000), HttpStatusCode.Conflict);

        var paid = await StaffInvoiceAsync(f);
        Assert.Equal(600_000L, paid.GetProperty("paid").GetInt64());
    }

    [Fact]
    public async Task Manual_payment_issues_a_numbered_receipt()
    {
        var f = await FamilyAsync("Manual Pay Academy", online: false);
        var (bursar, _) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(1));

        var first = await Json(await ManualAsync(bursar, f.InvoiceId, 2_000_000, "Transfer", "TRF-123", today));
        Assert.Matches(FirstReceipt(), first.GetProperty("receiptNumber").GetString());
        Assert.Equal("Transfer", first.GetProperty("method").GetString());
        Assert.Equal("TRF-123", first.GetProperty("reference").GetString());
        var second = await Json(await ManualAsync(bursar, f.InvoiceId, 3_000_000, "Cash", null, today.AddDays(-1)));
        Assert.EndsWith("-00002", second.GetProperty("receiptNumber").GetString());

        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal(5_000_000L, invoice.GetProperty("paid").GetInt64());
        Assert.Equal(5_000_000L, invoice.GetProperty("balance").GetInt64());
        Assert.Equal(2, invoice.GetProperty("payments").GetArrayLength());

        var pdf = await bursar.GetAsync($"/api/school/payments/{first.GetProperty("id").GetGuid()}/receipt.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Contains("no-store", pdf.Headers.CacheControl?.ToString());
        Assert.Equal(2, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.receipt" && n.Recipient == f.GuardianEmail, Ct)));

        // The parent sees the payments and can print the receipt; with no key the school has no Pay now.
        var view = await ParentInvoiceAsync(f);
        Assert.Equal(2, view.GetProperty("payments").GetArrayLength());
        Assert.False(view.GetProperty("canPayOnline").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await f.Parent.GetAsync($"/api/parent/payments/{first.GetProperty("id").GetGuid()}/receipt.pdf", Ct)).StatusCode);

        // A student with payments has history and can't be deleted.
        await ProblemAsync(await f.School.Owner.DeleteAsync($"/api/school/students/{f.StudentId}", Ct), HttpStatusCode.Conflict);

        // Paying the rest marks the invoice paid.
        await Json(await ManualAsync(bursar, f.InvoiceId, 5_000_000, "Pos"));
        Assert.Equal("Paid", (await StaffInvoiceAsync(f)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reversal_restores_the_balance_and_cannot_repeat()
    {
        var f = await FamilyAsync("Reversal Academy");
        var manual = await Json(await ManualAsync(f.School.Owner, f.InvoiceId, 2_000_000, "Transfer", "TRF-9"));
        var id = manual.GetProperty("id").GetGuid();

        var shortReason = await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{id}/reverse", new { reason = "oops" }, Ct), HttpStatusCode.BadRequest);
        Assert.True(shortReason.GetProperty("errors").TryGetProperty("reason", out _));
        var (teacher, _) = await kit.StaffAsync(f.School, MemberRole.Staff, "Teacher");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"/api/school/payments/{id}/reverse", new { reason = "Bank transfer bounced" }, Ct)).StatusCode);

        var reversed = await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{id}/reverse", new { reason = "Bank transfer bounced" }, Ct));
        Assert.NotNull(reversed.GetProperty("payment").GetProperty("reversedAt").GetString());
        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal(0L, invoice.GetProperty("paid").GetInt64());
        Assert.Equal(10_000_000L, invoice.GetProperty("balance").GetInt64());
        Assert.Equal("Unpaid", invoice.GetProperty("status").GetString());

        var again = await ProblemAsync(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{id}/reverse", new { reason = "Bank transfer bounced" }, Ct), HttpStatusCode.Conflict);
        Assert.Contains("already been reversed", again.GetProperty("detail").GetString());
        var row = Assert.Single(await PaymentsAsync(f.InvoiceId));
        Assert.Equal("Bank transfer bounced", row.ReversalReason);
        var audits = await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(FeePayment) && a.EntityId == id.ToString() && a.Action == "modified").ToListAsync(Ct));
        Assert.Contains(audits, a => a.AfterJson!.Contains("Bank transfer bounced"));
        // The receipt still prints, marked reversed.
        Assert.Equal(HttpStatusCode.OK, (await f.School.Owner.GetAsync($"/api/school/payments/{id}/receipt.pdf", Ct)).StatusCode);

        // An online payment is reversed in the books only; the response says no refund was sent.
        var reference = await StartPaymentAsync(f, 1_000_000);
        kit.Charge(reference, 1_000_000);
        await Json(await f.Parent.PostAsync($"/api/parent/payments/{reference}/verify", null, Ct));
        var onlineId = (await PaymentsAsync(f.InvoiceId)).Single(p => p.ProviderRef == reference).Id;
        var refunds = api.Merchant.Refunds.Count;
        var online = await Json(await f.School.Owner.PostAsJsonAsync($"/api/school/payments/{onlineId}/reverse", new { reason = "Paid twice by mistake" }, Ct));
        Assert.Contains("Paystack", online.GetProperty("message").GetString());
        Assert.Equal(refunds, api.Merchant.Refunds.Count);
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
    }

    private const string NewKey = "sk_test_integration_replacement_key";

    private Task<int> UnverifiedAlertsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "school.payment_unverified" && n.TenantId == tenantId, Ct));

    [Fact]
    public async Task Changing_the_key_while_parents_are_paying_needs_force()
    {
        var f = await FamilyAsync("Key Guard Academy");
        var reference = await StartPaymentAsync(f, 1_000_000);

        var put = await ProblemAsync(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = NewKey }, Ct), HttpStatusCode.Conflict);
        Assert.Equal("Some parents are in the middle of paying. Try again in a few minutes, or ask support.", put.GetProperty("detail").GetString());
        var delete = await ProblemAsync(await f.School.Owner.DeleteAsync("/api/school/settings/paystack", Ct), HttpStatusCode.Conflict);
        Assert.Equal("Some parents are in the middle of paying. Try again in a few minutes, or ask support.", delete.GetProperty("detail").GetString());
        Assert.True((await ParentInvoiceAsync(f)).GetProperty("canPayOnline").GetBoolean());

        // A checkout older than a day no longer blocks the change.
        await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow.AddHours(-25)), Ct));
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = NewKey }, Ct));

        // force overrides the guard.
        await StartPaymentAsync(f, 1_000_000);
        await ProblemAsync(await f.School.Owner.DeleteAsync("/api/school/settings/paystack", Ct), HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.NoContent, (await f.School.Owner.DeleteAsync("/api/school/settings/paystack?force=true", Ct)).StatusCode);
        Assert.False((await ParentInvoiceAsync(f)).GetProperty("canPayOnline").GetBoolean());
    }

    [Fact]
    public async Task A_checkout_started_before_a_key_change_still_confirms_for_7_days()
    {
        var f = await FamilyAsync("Key Change Academy");
        var byWebhook = await StartPaymentAsync(f, 1_000_000);
        var byVerify = await StartPaymentAsync(f, 2_000_000);
        var afterDelete = await StartPaymentAsync(f, 3_000_000);
        kit.Charge(byVerify, 2_000_000, inAccountOfKey: SchoolKit.PaystackKey);
        kit.Charge(afterDelete, 3_000_000, inAccountOfKey: SchoolKit.PaystackKey);
        kit.Charge(byWebhook, 1_000_000, inAccountOfKey: SchoolKit.PaystackKey); // a previous-key webhook is checked with Paystack

        // The school moves to another Paystack account.
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = NewKey, force = true }, Ct));

        var body = SchoolKit.ChargeSuccessBody(byWebhook, 1_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).StatusCode);
        Assert.Equal("Succeeded", (await Json(await f.Parent.PostAsync($"/api/parent/payments/{byVerify}/verify", null, Ct))).GetProperty("status").GetString());
        Assert.Equal(3_000_000L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());

        // A new checkout under the new key works too.
        var fresh = await StartPaymentAsync(f, 1_000_000);
        var freshBody = SchoolKit.ChargeSuccessBody(fresh, 1_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, freshBody, SchoolKit.Sign(freshBody, NewKey))).StatusCode);
        Assert.Equal(4_000_000L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());

        // Back to the first account, then the key is removed: the removed key is retained, so the checkout still confirms by verify.
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = SchoolKit.PaystackKey, force = true }, Ct));
        Assert.Equal(HttpStatusCode.NoContent, (await f.School.Owner.DeleteAsync("/api/school/settings/paystack?force=true", Ct)).StatusCode);
        Assert.Equal("Succeeded", (await Json(await f.Parent.PostAsync($"/api/parent/payments/{afterDelete}/verify", null, Ct))).GetProperty("status").GetString());
        Assert.Equal(7_000_000L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
        Assert.Equal(0, await UnverifiedAlertsAsync(f.School.TenantId));
    }

    [Fact]
    public async Task A_charge_no_key_can_confirm_alerts_the_school_once()
    {
        var f = await FamilyAsync("Unverified Academy");
        var (_, bursarId) = await kit.StaffAsync(f.School, MemberRole.Accountant, "Bursar");
        var bursarEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == bursarId).Select(u => u.Email).FirstAsync(Ct));
        var byWebhook = await StartPaymentAsync(f, 1_000_000);
        var byVerify = await StartPaymentAsync(f, 2_000_000);
        kit.Charge(byVerify, 2_000_000, inAccountOfKey: SchoolKit.PaystackKey);
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = NewKey, force = true }, Ct));
        // More than 7 days later the old key is gone.
        await api.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE school_profiles SET previous_key_retired_at = {DateTimeOffset.UtcNow.AddDays(-8)} WHERE tenant_id = {f.School.TenantId}", Ct));
        await api.WithDbAsync(db => db.FeePayments.IgnoreQueryFilters().Where(p => p.ProviderRef == byWebhook || p.ProviderRef == byVerify)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTimeOffset.UtcNow.AddDays(-9)), Ct)); // started before the change

        // A webhook no key can check (here signed with the expired key, then forged) is refused and alerts nobody, so it can't use up
        // the one alert for that reference.
        var body = SchoolKit.ChargeSuccessBody(byWebhook, 1_000_000);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, body, "00ff")).StatusCode);
        Assert.Equal(0, await UnverifiedAlertsAsync(f.School.TenantId));

        // The parents' return verifies are stranded (the charges sit in the old account): each reference alerts once.
        Assert.Equal("Pending", (await Json(await f.Parent.PostAsync($"/api/parent/payments/{byVerify}/verify", null, Ct))).GetProperty("status").GetString());
        await Json(await f.Parent.PostAsync($"/api/parent/payments/{byVerify}/verify", null, Ct));
        Assert.Equal("Pending", (await Json(await f.Parent.PostAsync($"/api/parent/payments/{byWebhook}/verify", null, Ct))).GetProperty("status").GetString());

        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
        var alerts = await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "school.payment_unverified" && n.TenantId == f.School.TenantId).ToListAsync(Ct));
        // One alert per reference, to the bursar and the Owner.
        Assert.Equal(4, alerts.Count);
        Assert.Equal(2, alerts.Count(a => a.Recipient == bursarEmail));
        Assert.Equal(new[] { byVerify, byWebhook }.Order(), alerts.Where(a => a.Recipient == bursarEmail).Select(a => a.Payload["reference"]).Order());

        // A forged webhook for an unknown reference alerts nobody.
        var unknown = SchoolKit.ChargeSuccessBody($"sfee_{Guid.NewGuid():N}", 1_000_000);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kit.WebhookAsync(f.School.TenantId, unknown, "00ff")).StatusCode);
        Assert.Equal(4, await UnverifiedAlertsAsync(f.School.TenantId));
    }

    [Fact]
    public async Task A_webhook_signed_only_with_the_previous_key_is_applied_only_when_Paystack_confirms()
    {
        var f = await FamilyAsync("Leaked Key Academy");
        var forged = await StartPaymentAsync(f, 1_000_000);
        var real = await StartPaymentAsync(f, 2_000_000);
        // The key leaked and was rotated; whoever holds the old key can still sign webhooks for 7 days.
        await Json(await f.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = NewKey, force = true }, Ct));

        // A signed claim of success that Paystack knows nothing about is acknowledged but not applied.
        var forgedBody = SchoolKit.ChargeSuccessBody(forged, 1_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, forgedBody, SchoolKit.Sign(forgedBody))).StatusCode);
        Assert.Equal(FeePaymentStatus.Pending, (await PaymentsAsync(f.InvoiceId)).Single(p => p.ProviderRef == forged).Status);
        Assert.Equal(0L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());

        // Paystack confirms the other one (in the old account): it is applied with Paystack's figures, not the body's.
        kit.Charge(real, 2_000_000, inAccountOfKey: SchoolKit.PaystackKey);
        var realBody = SchoolKit.ChargeSuccessBody(real, 999_999_999);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, realBody, SchoolKit.Sign(realBody))).StatusCode);
        var payment = (await PaymentsAsync(f.InvoiceId)).Single(p => p.ProviderRef == real);
        Assert.Equal(FeePaymentStatus.Succeeded, payment.Status);
        Assert.Equal(2_000_000L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());

        // A webhook signed with the current key keeps the existing behaviour (trusted as signed).
        var current = await StartPaymentAsync(f, 1_000_000);
        var currentBody = SchoolKit.ChargeSuccessBody(current, 1_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, currentBody, SchoolKit.Sign(currentBody, NewKey))).StatusCode);
        Assert.Equal(3_000_000L, (await StaffInvoiceAsync(f)).GetProperty("paid").GetInt64());
    }

    [Fact]
    public async Task School_webhooks_are_not_held_to_the_public_write_limit()
    {
        // Production limits: public writes at their default of 10 a minute, webhooks at their default of 600. Paystack
        // sends every school's webhooks from a few IPs, so 20 in a minute from one IP must all get through to the handler.
        await using var production = api.WithWebHostBuilder(b => b
            .UseSetting("RateLimits:PublicWritesPerMinute", "10")
            .UseSetting("RateLimits:WebhooksPerMinute", "600")); // the Program.cs default (a null override reads as "" and fails to bind)
        var client = production.CreateClient();
        var body = SchoolKit.ChargeSuccessBody($"sfee_{Guid.NewGuid():N}", 100_000);
        async Task<HttpStatusCode> SendAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/paystack/school/{Guid.NewGuid()}")
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-paystack-signature", "00ff");
            return (await client.SendAsync(request, Ct)).StatusCode;
        }
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync()); // reached the handler, not 429
        // The webhook policy, at 600 a minute, is the one applied: the 601st is refused.
        for (var i = 20; i < 600; i++) Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAsync());
    }

    [Fact]
    public async Task A_charge_above_the_current_balance_is_recorded_and_flagged()
    {
        var f = await FamilyAsync("Over Balance Academy");
        var reference = await StartPaymentAsync(f, 6_000_000);
        await Json(await ManualAsync(f.School.Owner, f.InvoiceId, 5_000_000, "Cash")); // balance now 5_000_000
        Assert.False((await StaffInvoiceAsync(f)).GetProperty("needsReview").GetBoolean());

        var body = SchoolKit.ChargeSuccessBody(reference, 6_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).StatusCode);

        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal(11_000_000L, invoice.GetProperty("paid").GetInt64());
        Assert.Equal(0L, invoice.GetProperty("balance").GetInt64());
        Assert.Equal("Paid", invoice.GetProperty("status").GetString());
        Assert.True(invoice.GetProperty("needsReview").GetBoolean());
        Assert.Equal(FeePaymentStatus.Succeeded, (await PaymentsAsync(f.InvoiceId)).Single(p => p.ProviderRef == reference).Status);
    }

    [Fact]
    public async Task A_charge_on_an_invoice_cancelled_after_checkout_is_recorded_and_flagged()
    {
        var f = await FamilyAsync("Cancelled Pay Academy");
        var reference = await StartPaymentAsync(f, 2_000_000);
        await api.WithDbAsync(async db =>
        {
            var invoice = await db.StudentInvoices.IgnoreQueryFilters().FirstAsync(i => i.Id == f.InvoiceId, Ct);
            invoice.Status = InvoiceStatus.Cancelled;
            return await db.SaveChangesAsync(Ct);
        });

        var body = SchoolKit.ChargeSuccessBody(reference, 2_000_000);
        Assert.Equal(HttpStatusCode.OK, (await kit.WebhookAsync(f.School.TenantId, body, SchoolKit.Sign(body))).StatusCode);

        var payment = (await PaymentsAsync(f.InvoiceId)).Single(p => p.ProviderRef == reference);
        Assert.Equal(FeePaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.ReceiptNumber);
        var invoice = await StaffInvoiceAsync(f);
        Assert.Equal("Cancelled", invoice.GetProperty("status").GetString());
        Assert.Equal(2_000_000L, invoice.GetProperty("paid").GetInt64());
        Assert.True(invoice.GetProperty("needsReview").GetBoolean());
    }

    [Fact]
    public async Task Free_plan_or_no_key_hides_pay_now()
    {
        // Free plan, key saved: no online fees.
        var free = await FamilyAsync("Free Pay Academy", online: false);
        await kit.SetPaystackKeyAsync(free.School);
        Assert.False((await ParentInvoiceAsync(free)).GetProperty("canPayOnline").GetBoolean());
        var refused = await ProblemAsync(await PayAsync(free, 1_000_000), HttpStatusCode.Conflict);
        Assert.Equal("Online payment isn't set up for this school.", refused.GetProperty("detail").GetString());

        // Paid plan, no key.
        var noKey = await FamilyAsync("No Key Academy", online: false);
        await api.MakeProAsync(noKey.School.TenantId, "school", "standard");
        Assert.False((await ParentInvoiceAsync(noKey)).GetProperty("canPayOnline").GetBoolean());
        Assert.Equal("Online payment isn't set up for this school.",
            (await ProblemAsync(await PayAsync(noKey, 1_000_000), HttpStatusCode.Conflict)).GetProperty("detail").GetString());
        Assert.DoesNotContain(api.Merchant.Initialized, i => i.Metadata.GetValueOrDefault("tenantId") == noKey.School.TenantId.ToString());

        // Saving the key turns it on; removing it turns it off. Only the Owner manages the key.
        var (bursar, _) = await kit.StaffAsync(noKey.School, MemberRole.Accountant, "Bursar");
        Assert.Equal(HttpStatusCode.Forbidden, (await bursar.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = SchoolKit.PaystackKey }, Ct)).StatusCode);
        var badKey = await ProblemAsync(await noKey.School.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = "pk_live_nope" }, Ct), HttpStatusCode.BadRequest);
        Assert.True(badKey.GetProperty("errors").TryGetProperty("secretKey", out _));
        await kit.SetPaystackKeyAsync(noKey.School);
        Assert.True((await ParentInvoiceAsync(noKey)).GetProperty("canPayOnline").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await noKey.School.Owner.DeleteAsync("/api/school/settings/paystack", Ct)).StatusCode);
        Assert.False((await ParentInvoiceAsync(noKey)).GetProperty("canPayOnline").GetBoolean());
        // The key is stored encrypted.
        var stored = await api.WithDbAsync(db => db.SchoolProfiles.IgnoreQueryFilters().Where(p => p.TenantId == free.School.TenantId).Select(p => p.PaystackSecretKeyProtected).FirstAsync(Ct));
        Assert.NotNull(stored);
        Assert.DoesNotContain(SchoolKit.PaystackKey, stored);
    }
}
