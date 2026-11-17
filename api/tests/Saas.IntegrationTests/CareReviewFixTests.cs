using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1, CareLink item 7 (final-review fixes): no stage rewind once the doctor has seen the patient; orders added after a visit is Done
/// send the bill-due notice; a late online payment counts on the day it is confirmed; a reversal is a negative row on its own day and leaves
/// the day the money came in unchanged.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareReviewFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string CareKey = "sk_test_integration_care_key";
    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? code = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        if (code is not null) Assert.Equal(code, JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json).GetProperty("code").GetString());
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    // ---------- (a) No stage rewind after the doctor ----------

    [Fact]
    public async Task A_visit_never_goes_back_to_triage_after_the_doctor()
    {
        var clinic = await kit.ClinicAsync("Rewind Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Rewind", "Patient"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        string Stage(JsonElement v) => v.GetProperty("stage").GetString()!;

        // Before the doctor, the desk can still move between the waiting stages.
        Assert.Equal("WaitingDoctor", Stage(await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingDoctor" }, Ct))));
        Assert.Equal("WaitingVitals", Stage(await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingVitals" }, Ct))));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new { vitals = new { pulse = 80 }, doctorUserId = doctorId }, Ct));
        await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct));
        Assert.Equal("WithDoctor", Stage(await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WithDoctor" }, Ct))));

        // From the doctor on, the desk and clinicians (the doctor too) can't send it back to the waiting stages, so triage can't be reopened.
        foreach (var client in new[] { desk, nurse, doctor })
            foreach (var stage in new[] { "WaitingVitals", "WaitingDoctor" })
                await AssertProblemAsync(await client.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage }, Ct), HttpStatusCode.Conflict, "visit_seen");
        Assert.Equal("WaitingPayment", Stage(await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingPayment" }, Ct))));
        await AssertProblemAsync(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingDoctor" }, Ct), HttpStatusCode.Conflict, "visit_seen");
        // Back to the doctor (a second look) is fine.
        Assert.Equal("WithDoctor", Stage(await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WithDoctor" }, Ct))));
        await AssertProblemAsync(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new { vitals = new { pulse = 80 }, doctorUserId = doctorId }, Ct),
            HttpStatusCode.Conflict);

        // Leadership may send it back (a hand-over to another doctor through re-triage), audited with ids only.
        Assert.Equal("WaitingDoctor", Stage(await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "WaitingDoctor" }, Ct))));
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.Where(a => a.Action == "care.visit_sent_back" && a.EntityId == visitId.ToString()).ToListAsync(Ct)));
        Assert.Contains("WithDoctor", audit.AfterJson);
    }

    // ---------- (b) Late orders on a Done visit ----------

    [Fact]
    public async Task An_order_after_the_visit_is_done_tells_the_family_the_bill_is_due()
    {
        var clinic = await kit.ClinicAsync("Late Order Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var drug = await kit.PriceItemAsync(clinic, "Late zinc", "Drug", 150_000);
        var lab = await kit.PriceItemAsync(clinic, "Late FBC", "Test", 250_000);
        var patientId = Id(await kit.PatientAsync(clinic, "Late", "Order"));
        var (_, portalUserId) = await kit.PortalAccountAsync(clinic, patientId);
        var visit = await kit.CheckInAsync(desk, patientId);
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{Id(visit)}/triage", new { vitals = new { pulse = 80 }, doctorUserId = doctorId }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{Id(visit)}/consultation", null, Ct)));
        await Json(await desk.PostAsJsonAsync($"/api/care/visits/{Id(visit)}/stage", new { stage = "Done" }, Ct)); // nothing owed yet
        Task<List<Saas.Domain.Core.UserNotification>> DueAsync() => api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .Where(n => n.UserId == portalUserId && n.Kind == CareNotices.BillDue).OrderBy(n => n.CreatedAt).ToListAsync(Ct));
        Assert.Empty(await DueAsync());

        // The doctor prescribes from the closed visit's consultation (within the follow-up window): the line lands and the family hears.
        await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId, allergyAcknowledged = false,
            items = new[] { new { drug = "Zinc", dose = "20 mg", frequency = "OD", durationDays = 10, quantity = 10, priceItemId = drug } },
        }, Ct));
        var first = Assert.Single(await DueAsync());
        Assert.Contains("₦15,000", first.Body);
        Assert.DoesNotContain("Zinc", first.Body);

        // A test does the same, with the new balance.
        await Json(await doctor.PostAsJsonAsync("/api/care/tests", new { patientId, consultationId, kind = "Lab", description = "FBC", priceItemId = lab }, Ct));
        var due = await DueAsync();
        Assert.Equal(2, due.Count);
        Assert.Contains("₦17,500", due[^1].Body);
        Assert.DoesNotContain("FBC", due[^1].Body);
    }

    [Fact]
    public async Task An_order_on_an_open_visit_sends_no_bill_due_notice()
    {
        var clinic = await kit.ClinicAsync("Open Order Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var drug = await kit.PriceItemAsync(clinic, "Open zinc", "Drug", 150_000);
        var patientId = Id(await kit.PatientAsync(clinic, "Open", "Order"));
        var (_, portalUserId) = await kit.PortalAccountAsync(clinic, patientId);
        var visit = await kit.CheckInAsync(desk, patientId);
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{Id(visit)}/triage", new { vitals = new { pulse = 80 }, doctorUserId = doctorId }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{Id(visit)}/consultation", null, Ct)));
        await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId, allergyAcknowledged = false,
            items = new[] { new { drug = "Zinc", dose = "20 mg", frequency = "OD", durationDays = 10, quantity = 10, priceItemId = drug } },
        }, Ct));
        Assert.Equal(0, await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == portalUserId && n.Kind == CareNotices.BillDue, Ct)));
    }

    // ---------- (c) and (d) Takings by the day the money moved ----------

    private static async Task<JsonElement> TakingsAsync(HttpClient client, DateOnly from, DateOnly to) =>
        await Json(await client.GetAsync($"/api/care/reports/takings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", Ct));

    [Fact]
    public async Task A_late_online_payment_counts_on_the_day_it_is_confirmed()
    {
        var clinic = await kit.ClinicAsync("Late Online Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        await Json(await clinic.Owner.PutAsJsonAsync("/api/care/settings/paystack", new { secretKey = CareKey }, Ct));
        var patientId = Id(await kit.PatientAsync(clinic, "Late", "Payer"));
        var item = await kit.PriceItemAsync(clinic, "Online fee", "Consultation", 400_000);
        var billId = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b =>
        {
            var bill = await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct);
            await b.AddLineAsync(bill.Id, item, 1, null, Ct);
            return bill.Id;
        });
        var payer = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == clinic.TenantId && m.Role == Saas.Domain.Core.MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        var reference = (await api.AsTenantAsync<OnlinePaymentService, CareCheckoutResult>(clinic.TenantId, s => s.StartCheckoutAsync(billId, payer, null, Ct))).Reference;

        // Paystack says the patient paid three days ago; the clinic only learns of it today.
        var providerTime = DateTimeOffset.UtcNow.AddDays(-3);
        api.Merchant.Charges[reference] = new Saas.Application.Billing.VerifiedCharge(reference, 400_000, "NGN", providerTime);
        Assert.Equal("Succeeded", (await Json(await cashier.PostAsync($"/api/care/payments/online/{reference}/verify", null, Ct))).GetProperty("status").GetString());

        var payment = await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().FirstAsync(p => p.ProviderRef == reference, Ct));
        Assert.Equal(Today, CareCalendar.Today(payment.PaidAt));
        Assert.Equal(0, (await TakingsAsync(cashier, CareCalendar.Today(providerTime), CareCalendar.Today(providerTime))).GetProperty("totalMinor").GetInt64());
        Assert.Equal(400_000, (await TakingsAsync(cashier, Today, Today)).GetProperty("totalMinor").GetInt64());
        // The provider's own time is kept for reference on the payment.
        var paid = (await Json(await cashier.GetAsync($"/api/care/bills/{billId}", Ct))).GetProperty("payments").EnumerateArray().Single(p => Id(p) == payment.Id);
        Assert.Equal(CareCalendar.Today(providerTime), CareCalendar.Today(paid.GetProperty("providerPaidAt").GetDateTimeOffset()));
    }

    [Fact]
    public async Task A_reversal_is_a_negative_row_on_its_own_day()
    {
        var clinic = await kit.ClinicAsync("Reversal Day Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = Id(await kit.PatientAsync(clinic, "Reversal", "Day"));
        var item = await kit.PriceItemAsync(clinic, "Reversal fee", "Consultation", 900_000);
        var billId = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b =>
        {
            var bill = await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct);
            await b.AddLineAsync(bill.Id, item, 1, null, Ct);
            return bill.Id;
        });
        var cash = Id(await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 300_000, method = "Cash" }, Ct)));
        var transfer = Id(await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor = 200_000, method = "Transfer", reference = "TRF-9" }, Ct)));
        var closed = Today.AddDays(-2);
        await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().Where(p => p.Id == cash || p.Id == transfer)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAt, new DateTimeOffset(closed.ToDateTime(new TimeOnly(10, 0)), CareCalendar.LagosOffset).ToUniversalTime()), Ct));
        var before = await TakingsAsync(cashier, closed, closed);
        Assert.Equal(500_000, before.GetProperty("totalMinor").GetInt64());

        // Today the transfer bounces and is reversed.
        await Json(await cashier.PostAsJsonAsync($"/api/care/payments/{transfer}/reverse", new { reason = "Bounced" }, Ct));

        // The closed day is unchanged.
        var after = await TakingsAsync(cashier, closed, closed);
        Assert.Equal(500_000, after.GetProperty("totalMinor").GetInt64());
        Assert.Empty(after.GetProperty("reversals").EnumerateArray());

        // Today shows the reversal as a negative row.
        var today = await TakingsAsync(cashier, Today, Today);
        Assert.Equal(-200_000, today.GetProperty("totalMinor").GetInt64());
        var reversal = Assert.Single(today.GetProperty("reversals").EnumerateArray());
        Assert.Equal("Transfer", reversal.GetProperty("method").GetString());
        Assert.Equal(-200_000, reversal.GetProperty("amountMinor").GetInt64());
        var day = Assert.Single(today.GetProperty("days").EnumerateArray());
        Assert.Equal(-200_000, day.GetProperty("totalMinor").GetInt64());
        Assert.Equal(-200_000, Assert.Single(day.GetProperty("reversals").EnumerateArray()).GetProperty("amountMinor").GetInt64());

        // Across both days: the money in less the money out.
        Assert.Equal(300_000, (await TakingsAsync(cashier, closed, Today)).GetProperty("totalMinor").GetInt64());
        var csvBytes = await (await cashier.GetAsync($"/api/care/reports/takings.csv?from={closed:yyyy-MM-dd}&to={Today:yyyy-MM-dd}", Ct)).Content.ReadAsByteArrayAsync(Ct);
        var lines = Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains($"{closed:yyyy-MM-dd},\"Bank transfer\",1,2000.00", lines);
        Assert.Contains($"{Today:yyyy-MM-dd},\"Reversal: Bank transfer\",1,-2000.00", lines);
        Assert.Equal("total,,2,3000.00", lines[^1]);
    }
}
