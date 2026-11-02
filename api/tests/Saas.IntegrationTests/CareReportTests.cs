using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 12 reports: takings by method and day, debtors with a WhatsApp reminder link, revenue by price category and deposits held
/// for current admissions, each with a CSV (formula-safe cells, a byte order mark, local phone numbers). Cashier and leadership only;
/// nothing clinical in any of them.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareReportTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<string> CsvAsync(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        Assert.Equal("text/csv", r.Content.Headers.ContentType?.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync(Ct);
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV must start with a UTF-8 byte order mark");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    private Task<Guid> OpenBillAsync(CareKit.Clinic clinic, Guid patientId) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct)).Id);

    private Task AddLineAsync(CareKit.Clinic clinic, Guid billId, Guid itemId, int qty = 1) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, itemId, qty, null, Ct)).Id);

    private static async Task<Guid> PayAsync(HttpClient client, Guid billId, long amountMinor, string method, string? reference = null) =>
        (await Json(await client.PostAsJsonAsync($"/api/care/bills/{billId}/payments", new { amountMinor, method, reference }, Ct))).GetProperty("id").GetGuid();

    /// <summary>Moves a payment to 10:00 Lagos time on <paramref name="day"/>, so the test never depends on the clock.</summary>
    private Task MoveAsync(Guid paymentId, DateOnly day) => api.WithDbAsync(db =>
        db.CarePayments.IgnoreQueryFilters().Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAt, new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), CareCalendar.LagosOffset).ToUniversalTime()), Ct));

    private static long Method(JsonElement byMethod, string method) =>
        byMethod.EnumerateArray().Where(m => m.GetProperty("method").GetString() == method).Select(m => m.GetProperty("amountMinor").GetInt64()).SingleOrDefault();

    [Fact]
    public async Task Takings_report_totals_by_method()
    {
        var clinic = await kit.ClinicAsync("Takings Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var item = await kit.PriceItemAsync(clinic, "Takings consultation", "Consultation", 1_000_000);
        var billId = await OpenBillAsync(clinic, patientId);
        await AddLineAsync(clinic, billId, item);

        var day = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-5);
        var cash = await PayAsync(cashier, billId, 500_000, "Cash");
        var pos = await PayAsync(cashier, billId, 200_000, "Pos", "POS-1");
        var transfer = await PayAsync(cashier, billId, 100_000, "Transfer", "TRF-1");
        var earlier = await PayAsync(cashier, billId, 50_000, "Cash");
        foreach (var id in new[] { cash, pos, transfer }) await MoveAsync(id, day);
        await MoveAsync(earlier, day.AddDays(-1));
        // A reversed payment and an online checkout that never succeeded are not takings.
        (await cashier.PostAsJsonAsync($"/api/care/payments/{transfer}/reverse", new { reason = "Bounced transfer" }, Ct)).EnsureSuccessStatusCode();
        await api.WithDbAsync(db =>
        {
            db.CarePayments.Add(new CarePayment
            {
                TenantId = clinic.TenantId, BillId = billId, PatientId = patientId, AmountMinor = 300_000, Method = PayMethod.Online,
                Status = CarePaymentStatus.Pending, Reference = $"care_{Guid.NewGuid():N}", PaidAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(11, 0)), CareCalendar.LagosOffset).ToUniversalTime(),
            });
            return db.SaveChangesAsync(Ct);
        });

        var report = await Json(await cashier.GetAsync($"/api/care/reports/takings?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(700_000, report.GetProperty("totalMinor").GetInt64());
        Assert.Equal(500_000, Method(report.GetProperty("byMethod"), "Cash"));
        Assert.Equal(200_000, Method(report.GetProperty("byMethod"), "Pos"));
        Assert.Equal(0, Method(report.GetProperty("byMethod"), "Transfer"));
        Assert.Equal(0, Method(report.GetProperty("byMethod"), "Online"));
        var only = Assert.Single(report.GetProperty("days").EnumerateArray());
        Assert.Equal(day.ToString("yyyy-MM-dd"), only.GetProperty("date").GetString());

        // Two days: totals per day, and the range total.
        var two = await Json(await cashier.GetAsync($"/api/care/reports/takings?from={day.AddDays(-1):yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(750_000, two.GetProperty("totalMinor").GetInt64());
        var days = two.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(2, days.Count);
        Assert.Equal(50_000, days[0].GetProperty("totalMinor").GetInt64());
        Assert.Equal(700_000, days[1].GetProperty("totalMinor").GetInt64());

        var csv = await CsvAsync(await cashier.GetAsync($"/api/care/reports/takings.csv?from={day.AddDays(-1):yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("date,method,payments,amount", lines[0]);
        Assert.Contains($"{day:yyyy-MM-dd},\"Cash\",1,5000.00", lines);
        Assert.Contains($"{day:yyyy-MM-dd},\"POS\",1,2000.00", lines);
        Assert.DoesNotContain(lines, l => l.Contains("Bank transfer", StringComparison.Ordinal));
        Assert.Contains($"{day.AddDays(-1):yyyy-MM-dd},\"Cash\",1,500.00", lines);
        Assert.Equal("total,,3,7500.00", lines[^1]);

        // A backwards range is a field error.
        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync($"/api/care/reports/takings?from={day:yyyy-MM-dd}&to={day.AddDays(-1):yyyy-MM-dd}", Ct)).StatusCode);
    }

    /// <summary>An instant at a Lagos wall-clock time on <paramref name="day"/>, as UTC (what the database stores).</summary>
    private static DateTimeOffset Lagos(DateOnly day, int hour, int minute) =>
        new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), CareCalendar.LagosOffset).ToUniversalTime();

    [Fact]
    public async Task Takings_and_revenue_use_Lagos_days_at_the_boundary()
    {
        var clinic = await kit.ClinicAsync("Boundary Report Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var early = await kit.PriceItemAsync(clinic, "Boundary early", "Procedure", 300_000);
        var late = await kit.PriceItemAsync(clinic, "Boundary late", "Test", 70_000);
        var billId = await OpenBillAsync(clinic, patientId);
        var earlyLine = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, early, 1, null, Ct)).Id);
        var lateLine = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, late, 1, null, Ct)).Id);

        var day = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-5);
        // 00:30 Lagos on `day` is 23:30 UTC the day before; 23:30 Lagos on `day - 1` is 22:30 UTC that same UTC day.
        var morning = Lagos(day, 0, 30);
        var night = Lagos(day.AddDays(-1), 23, 30);
        Assert.Equal(day.AddDays(-1), DateOnly.FromDateTime(morning.UtcDateTime));
        var earlyPay = await PayAsync(cashier, billId, 120_000, "Cash");
        var latePay = await PayAsync(cashier, billId, 45_000, "Pos", "POS-NIGHT");
        await api.WithDbAsync(async db =>
        {
            await db.CarePayments.IgnoreQueryFilters().Where(p => p.Id == earlyPay).ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAt, morning), Ct);
            await db.CarePayments.IgnoreQueryFilters().Where(p => p.Id == latePay).ExecuteUpdateAsync(s => s.SetProperty(p => p.PaidAt, night), Ct);
            await db.BillLines.IgnoreQueryFilters().Where(l => l.Id == earlyLine).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, morning), Ct);
            return await db.BillLines.IgnoreQueryFilters().Where(l => l.Id == lateLine).ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedAt, night), Ct);
        });

        // One Lagos day: the 00:30 payment and line are in it; the 23:30 ones the night before are not.
        var takings = await Json(await cashier.GetAsync($"/api/care/reports/takings?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(120_000, takings.GetProperty("totalMinor").GetInt64());
        Assert.Equal(0, Method(takings.GetProperty("byMethod"), "Pos"));
        var before = await Json(await cashier.GetAsync($"/api/care/reports/takings?from={day.AddDays(-1):yyyy-MM-dd}&to={day.AddDays(-1):yyyy-MM-dd}", Ct));
        Assert.Equal(45_000, before.GetProperty("totalMinor").GetInt64());
        // Two days: each lands on its own Lagos day.
        var both = await Json(await cashier.GetAsync($"/api/care/reports/takings?from={day.AddDays(-1):yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        var days = both.GetProperty("days").EnumerateArray().ToDictionary(d => d.GetProperty("date").GetString()!, d => d.GetProperty("totalMinor").GetInt64());
        Assert.Equal(new Dictionary<string, long> { [day.AddDays(-1).ToString("yyyy-MM-dd")] = 45_000, [day.ToString("yyyy-MM-dd")] = 120_000 }, days);

        long Category(JsonElement report, string name) => report.GetProperty("byCategory").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == name).GetProperty("amountMinor").GetInt64();
        var revenue = await Json(await cashier.GetAsync($"/api/care/reports/revenue?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}", Ct));
        Assert.Equal(300_000, Category(revenue, "Procedure"));
        Assert.Equal(0, Category(revenue, "Test"));
        Assert.Equal(300_000, revenue.GetProperty("chargedMinor").GetInt64());
        var revenueBefore = await Json(await cashier.GetAsync($"/api/care/reports/revenue?from={day.AddDays(-1):yyyy-MM-dd}&to={day.AddDays(-1):yyyy-MM-dd}", Ct));
        Assert.Equal(70_000, Category(revenueBefore, "Test"));
        Assert.Equal(0, Category(revenueBefore, "Procedure"));
    }

    [Fact]
    public async Task Debtors_csv_escapes_and_formats_phone()
    {
        var clinic = await kit.ClinicAsync("Debtors Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var item = await kit.PriceItemAsync(clinic, "Debt consultation", "Consultation", 750_000);
        var owing = (await kit.PatientAsync(clinic, "=Ngozi", "Eze, Jr", phone: "0803 123 4567")).GetProperty("id").GetGuid();
        var paid = (await kit.PatientAsync(clinic, "Paid", "Up", "1985-01-01")).GetProperty("id").GetGuid();
        var credit = (await kit.PatientAsync(clinic, "Credit", "Held", "1980-01-01")).GetProperty("id").GetGuid();
        var owingBill = await OpenBillAsync(clinic, owing);
        await AddLineAsync(clinic, owingBill, item);
        await PayAsync(cashier, owingBill, 250_000, "Cash");
        var paidBill = await OpenBillAsync(clinic, paid);
        await AddLineAsync(clinic, paidBill, item);
        await PayAsync(cashier, paidBill, 750_000, "Cash");
        var creditBill = await OpenBillAsync(clinic, credit);
        await AddLineAsync(clinic, creditBill, item);
        await PayAsync(cashier, creditBill, 900_000, "Cash"); // InCredit: balance 0, not a debtor

        var report = await Json(await cashier.GetAsync("/api/care/reports/debtors", Ct));
        var debtor = Assert.Single(report.GetProperty("debtors").EnumerateArray());
        Assert.Equal(owing, debtor.GetProperty("patientId").GetGuid());
        Assert.Equal(500_000, debtor.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(500_000, report.GetProperty("totalMinor").GetInt64());
        var url = debtor.GetProperty("whatsAppUrl").GetString()!;
        Assert.StartsWith("https://wa.me/2348031234567?text=", url);
        Assert.Equal("Hello =Ngozi Eze, Jr, you have a balance of ₦5,000 at Debtors Clinic.", Uri.UnescapeDataString(url[(url.IndexOf("?text=", StringComparison.Ordinal) + 6)..]));

        var csv = await CsvAsync(await cashier.GetAsync("/api/care/reports/debtors.csv", Ct));
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("patient_number,patient_name,phone,bills,balance,oldest_bill", lines[0]);
        Assert.Equal(2, lines.Length);
        // The formula-looking name opens as text; the comma is quoted; the phone is written as people write it.
        Assert.Contains("\"'=Ngozi Eze, Jr\"", lines[1]);
        Assert.Contains("\"0803 123 4567\"", lines[1]);
        Assert.Contains(",1,5000.00,", lines[1]);
    }

    [Fact]
    public async Task Debtors_link_is_left_out_for_a_deceased_patient()
    {
        var clinic = await kit.ClinicAsync("Deceased Debt Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var item = await kit.PriceItemAsync(clinic, "Debt item", "Procedure", 100_000);
        var patientId = (await kit.PatientAsync(clinic, "Late", "Patient", phone: "08031112222")).GetProperty("id").GetGuid();
        var bill = await OpenBillAsync(clinic, patientId);
        await AddLineAsync(clinic, bill, item);
        await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == patientId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PatientStatus.Deceased), Ct));

        var debtor = Assert.Single((await Json(await cashier.GetAsync("/api/care/reports/debtors", Ct))).GetProperty("debtors").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, debtor.GetProperty("whatsAppUrl").ValueKind);
        Assert.Equal(100_000, debtor.GetProperty("balanceMinor").GetInt64());
    }

    [Fact]
    public async Task Revenue_report_totals_by_price_category()
    {
        var clinic = await kit.ClinicAsync("Revenue Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var consult = await kit.PriceItemAsync(clinic, "Revenue consultation", "Consultation", 500_000);
        var drug = await kit.PriceItemAsync(clinic, "Revenuecillin 500mg", "Drug", 20_000);
        var test = await kit.PriceItemAsync(clinic, "Revenue smear", "Test", 150_000);
        var bill = await OpenBillAsync(clinic, patientId);
        await AddLineAsync(clinic, bill, consult);
        await AddLineAsync(clinic, bill, drug, 3);
        await AddLineAsync(clinic, bill, test);
        // A removed line is not revenue.
        var removed = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(bill, test, 1, null, Ct)).Id);
        (await clinic.Owner.PostAsJsonAsync($"/api/care/bills/{bill}/lines/{removed}/remove", new { reason = "Charged twice" }, Ct)).EnsureSuccessStatusCode();
        (await clinic.Owner.PostAsJsonAsync($"/api/care/bills/{bill}/adjustments", new { kind = "Discount", amountMinor = 10_000, reason = "Staff family" }, Ct))
            .EnsureSuccessStatusCode();

        var today = CareCalendar.Today(DateTimeOffset.UtcNow);
        var report = await Json(await cashier.GetAsync($"/api/care/reports/revenue?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}", Ct));
        long Category(string name) => report.GetProperty("byCategory").EnumerateArray()
            .Single(c => c.GetProperty("category").GetString() == name).GetProperty("amountMinor").GetInt64();
        Assert.Equal(500_000, Category("Consultation"));
        Assert.Equal(60_000, Category("Drug"));
        Assert.Equal(150_000, Category("Test"));
        Assert.Equal(0, Category("Bed"));
        Assert.Equal(710_000, report.GetProperty("chargedMinor").GetInt64());
        Assert.Equal(10_000, report.GetProperty("discountsMinor").GetInt64());
        Assert.Equal(700_000, report.GetProperty("netMinor").GetInt64());

        // Category totals only: no item names (no drug or test names) anywhere in the report or its CSV.
        var body = await (await cashier.GetAsync($"/api/care/reports/revenue?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Revenuecillin", body);
        Assert.DoesNotContain("Revenue smear", body);
        var csv = await CsvAsync(await cashier.GetAsync($"/api/care/reports/revenue.csv?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}", Ct));
        Assert.DoesNotContain("Revenuecillin", csv);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("category,lines,amount", lines[0]);
        Assert.Contains("\"Drug\",1,600.00", lines);
        Assert.Contains("\"net\",,7000.00", lines);
    }

    [Fact]
    public async Task Deposits_report_shows_current_admissions_with_credit()
    {
        var clinic = await kit.ClinicAsync("Deposit Report Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var ward = await kit.WardAsync(clinic, "Deposit ward", minDepositMinor: 2_000_000);
        var bed = await kit.BedAsync(clinic, ward, "D1");
        var bed2 = await kit.BedAsync(clinic, ward, "D2");
        var patientId = (await kit.PatientAsync(clinic, "Depo", "Sit")).GetProperty("id").GetGuid();
        var other = (await kit.PatientAsync(clinic, "Gone", "Home", "1970-05-05")).GetProperty("id").GetGuid();
        var admission = await kit.AdmitAsync(clinic.Owner, patientId, "Depositreason vomiting", "Depositdiagnosis gastro");
        (await kit.AssignBedAsync(clinic.Owner, admission, bed, 2_500_000)).EnsureSuccessStatusCode();
        var discharged = await kit.AdmitAsync(clinic.Owner, other, "Depositreason cough", "Depositdiagnosis chest");
        (await kit.AssignBedAsync(clinic.Owner, discharged, bed2, 2_000_000)).EnsureSuccessStatusCode();
        (await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{discharged}/discharge", new { type = "AgainstAdvice", amaReason = "Wanted to go home" }, Ct)).EnsureSuccessStatusCode();

        var report = await Json(await cashier.GetAsync("/api/care/reports/deposits", Ct));
        var row = Assert.Single(report.GetProperty("admissions").EnumerateArray());
        Assert.Equal(admission, row.GetProperty("admissionId").GetGuid());
        Assert.Equal(2_500_000, row.GetProperty("depositMinor").GetInt64());
        Assert.Equal(2_500_000, row.GetProperty("creditMinor").GetInt64());
        Assert.Equal("Deposit ward", row.GetProperty("wardName").GetString());
        Assert.Equal("D1", row.GetProperty("bedLabel").GetString());
        Assert.Equal(2_500_000, report.GetProperty("depositsMinor").GetInt64());
        var text = row.GetRawText();
        Assert.DoesNotContain("Depositreason", text);
        Assert.DoesNotContain("Depositdiagnosis", text);

        var csv = await CsvAsync(await cashier.GetAsync("/api/care/reports/deposits.csv", Ct));
        Assert.DoesNotContain("Depositdiagnosis", csv);
        Assert.Equal("patient_number,patient_name,ward,bed,admitted_on,deposit,charges,paid,balance,credit", csv.Split("\r\n")[0]);
    }

    [Fact]
    public async Task Reports_are_for_the_cashier_and_leadership_only()
    {
        var clinic = await kit.ClinicAsync("Report Access Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        foreach (var path in new[]
                 {
                     "/api/care/reports/takings", "/api/care/reports/takings.csv", "/api/care/reports/debtors", "/api/care/reports/debtors.csv",
                     "/api/care/reports/revenue", "/api/care/reports/revenue.csv", "/api/care/reports/deposits", "/api/care/reports/deposits.csv",
                 })
        {
            foreach (var client in new[] { clinic.Owner, cashier, admin })
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, Ct)).StatusCode);
            foreach (var client in new[] { desk, doctor, nurse })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path, Ct)).StatusCode);
        }
    }
}
