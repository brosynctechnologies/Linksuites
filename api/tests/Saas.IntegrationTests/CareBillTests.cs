using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;

namespace Saas.IntegrationTests;

/// <summary>CareLink Task 2: the price list and the bills core (frozen prices, totals, manual lines and removals).</summary>
[Collection(nameof(ApiCollection))]
public sealed class CareBillTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertStatusAsync(HttpResponseMessage r, HttpStatusCode status)
    {
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
    }

    private Task<Guid> OpenVisitBillAsync(CareKit.Clinic clinic, Guid patientId, Guid visitId) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.OpenBillForVisitAsync(patientId, visitId, Ct)).Id);

    private Task<Guid> AddLineAsync(CareKit.Clinic clinic, Guid billId, Guid priceItemId, int qty, string? clinicalRef = null) =>
        api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.AddLineAsync(billId, priceItemId, qty, clinicalRef, Ct)).Id);

    [Fact]
    public async Task Price_list_is_written_by_leadership_or_cashier()
    {
        var clinic = await kit.ClinicAsync("Price List Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");

        var item = await Json(await cashier.PostAsJsonAsync("/api/care/price-list", new { name = "Malaria test", category = "Test", priceMinor = 300_000 }, Ct));
        Assert.Equal("Malaria test", item.GetProperty("name").GetString());
        Assert.Equal("Test", item.GetProperty("category").GetString());
        Assert.Equal(300_000, item.GetProperty("priceMinor").GetInt64());
        Assert.True(item.GetProperty("active").GetBoolean());
        var id = item.GetProperty("id").GetGuid();

        await AssertStatusAsync(await doctor.PostAsJsonAsync("/api/care/price-list", new { name = "X-ray", category = "Test", priceMinor = 1 }, Ct), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await frontDesk.PutAsJsonAsync($"/api/care/price-list/{id}",
            new { name = "Malaria test", category = "Test", priceMinor = 1, active = true }, Ct), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await cashier.PostAsJsonAsync("/api/care/price-list", new { name = "", category = "Test", priceMinor = 100 }, Ct), HttpStatusCode.BadRequest);
        await AssertStatusAsync(await cashier.PostAsJsonAsync("/api/care/price-list", new { name = "Free", category = "Test", priceMinor = -1 }, Ct), HttpStatusCode.BadRequest);
        await AssertStatusAsync(await cashier.PostAsJsonAsync("/api/care/price-list", new { name = "Malaria TEST", category = "Test", priceMinor = 100 }, Ct), HttpStatusCode.Conflict);

        // Deactivated items stay on the list for everyone (shown inactive) and drop out of the active-only view.
        var off = await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/price-list/{id}",
            new { name = "Malaria test (RDT)", category = "Test", priceMinor = 350_000, active = false }, Ct));
        Assert.False(off.GetProperty("active").GetBoolean());
        var all = (await Json(await frontDesk.GetAsync("/api/care/price-list", Ct))).EnumerateArray().ToList();
        Assert.Contains(all, i => i.GetProperty("id").GetGuid() == id && i.GetProperty("name").GetString() == "Malaria test (RDT)");
        Assert.Empty((await Json(await doctor.GetAsync("/api/care/price-list?active=true", Ct))).EnumerateArray());
    }

    /// <summary>
    /// Final review I2 (user decision): the cashier and front desk see bill lines by category and amount only ("Drug", "Test"), never the
    /// item name, and get no price-item id to look it up by; doctors, the director and an admin see the names.
    /// </summary>
    [Fact]
    public async Task Cashier_sees_bill_lines_by_category_only()
    {
        var clinic = await kit.ClinicAsync("Bill Category Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var drug = await kit.PriceItemAsync(clinic, "Tenofovirmarker tablets", "Drug", 250_000);
        var test = await kit.PriceItemAsync(clinic, "Hepatitismarker viral load", "Test", 900_000);
        var billId = await OpenVisitBillAsync(clinic, patientId, Guid.NewGuid());
        await AddLineAsync(clinic, billId, drug, 1);
        await AddLineAsync(clinic, billId, test, 1);

        foreach (var client in new[] { cashier, frontDesk })
        {
            var response = await client.GetAsync($"/api/care/bills/{billId}", Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            var bill = await Json(response);
            Assert.DoesNotContain("Tenofovirmarker", text);
            Assert.DoesNotContain("Hepatitismarker", text);
            Assert.DoesNotContain(drug.ToString(), text);
            Assert.DoesNotContain(test.ToString(), text);
            var lines = bill.GetProperty("lines").EnumerateArray().ToList();
            Assert.Equal(["Drug", "Test"], lines.Select(l => l.GetProperty("description").GetString()));
            Assert.Equal([250_000L, 900_000L], lines.Select(l => l.GetProperty("amountMinor").GetInt64()));
        }
        foreach (var client in new[] { doctor, clinic.Owner, admin })
        {
            var lines = (await Json(await client.GetAsync($"/api/care/bills/{billId}", Ct))).GetProperty("lines").EnumerateArray().ToList();
            Assert.Equal(["Tenofovirmarker tablets", "Hepatitismarker viral load"], lines.Select(l => l.GetProperty("description").GetString()));
        }
    }

    [Fact]
    public async Task Bill_keeps_the_price_charged()
    {
        var clinic = await kit.ClinicAsync("Frozen Price Clinic");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var itemId = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        var visitId = Guid.NewGuid();

        var billId = await OpenVisitBillAsync(clinic, patientId, visitId);
        Assert.Equal(billId, await OpenVisitBillAsync(clinic, patientId, visitId)); // one bill per visit
        await AddLineAsync(clinic, billId, itemId, 1);

        (await clinic.Owner.PutAsJsonAsync($"/api/care/price-list/{itemId}",
            new { name = "General consultation", category = "Consultation", priceMinor = 700_000, active = true }, Ct)).EnsureSuccessStatusCode();

        var bill = await Json(await clinic.Owner.GetAsync($"/api/care/bills/{billId}", Ct));
        var line = Assert.Single(bill.GetProperty("lines").EnumerateArray());
        Assert.Equal(500_000, line.GetProperty("unitPriceMinor").GetInt64());
        Assert.Equal(500_000, line.GetProperty("amountMinor").GetInt64());
        Assert.Equal(500_000, bill.GetProperty("totalMinor").GetInt64());
        Assert.Equal(500_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("Unpaid", bill.GetProperty("status").GetString());
        Assert.Matches(@"^BILL-\d{4}-00001$", bill.GetProperty("number").GetString());

        // A new charge uses the new price.
        await AddLineAsync(clinic, billId, itemId, 1);
        bill = await Json(await clinic.Owner.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Equal(1_200_000, bill.GetProperty("totalMinor").GetInt64());
    }

    [Fact]
    public async Task Recalculation_reads_the_paid_amount_fresh()
    {
        var clinic = await kit.ClinicAsync("Fresh Paid Clinic");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var itemId = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);

        // One scope (one DbContext) opens the bill, a payment lands from elsewhere, then the same scope adds a line.
        var billId = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b =>
        {
            var bill = await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct);
            await api.WithDbAsync(async db =>
            {
                var row = await db.PatientBills.IgnoreQueryFilters().FirstAsync(x => x.Id == bill.Id, Ct);
                row.PaidMinor = 100_000;
                return await db.SaveChangesAsync(Ct);
            });
            await b.AddLineAsync(bill.Id, itemId, 1, null, Ct);
            return bill.Id;
        });

        var read = await Json(await clinic.Owner.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Equal(100_000, read.GetProperty("paidMinor").GetInt64());
        Assert.Equal(400_000, read.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("PartPaid", read.GetProperty("status").GetString());
    }

    [Fact]
    public async Task New_charge_on_a_waived_bill_is_owed()
    {
        var clinic = await kit.ClinicAsync("Waived Bill Clinic");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var first = await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 200_000);
        var second = await kit.PriceItemAsync(clinic, "Malaria test", "Test", 300_000);
        var billId = await OpenVisitBillAsync(clinic, patientId, Guid.NewGuid());
        await AddLineAsync(clinic, billId, first, 1);
        await api.WithDbAsync(async db =>
        {
            var row = await db.PatientBills.IgnoreQueryFilters().FirstAsync(x => x.Id == billId, Ct);
            row.Status = Saas.Domain.Care.BillStatus.Waived;
            row.BalanceMinor = 0;
            return await db.SaveChangesAsync(Ct);
        });

        await AddLineAsync(clinic, billId, second, 1);

        var read = await Json(await clinic.Owner.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Equal("Unpaid", read.GetProperty("status").GetString());
        Assert.True(read.GetProperty("balanceMinor").GetInt64() >= 300_000, read.GetProperty("balanceMinor").ToString());
    }

    [Fact]
    public async Task Bill_totals_follow_lines_and_removals()
    {
        var clinic = await kit.ClinicAsync("Bill Totals Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = (await kit.PatientAsync(clinic)).GetProperty("id").GetGuid();
        var drug = await kit.PriceItemAsync(clinic, "Paracetamol 500mg (strip)", "Drug", 50_000);
        var test = await kit.PriceItemAsync(clinic, "Full blood count", "Test", 400_000);
        var dressing = await kit.PriceItemAsync(clinic, "Wound dressing", "Procedure", 250_000);

        var billId = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b => (await b.OpenBillForAdmissionAsync(patientId, Guid.NewGuid(), Ct)).Id);
        await AddLineAsync(clinic, billId, drug, 3, clinicalRef: Guid.NewGuid().ToString());
        await AddLineAsync(clinic, billId, test, 1);

        // A manual line by the cashier.
        var manual = await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines",
            new { priceItemId = dressing, quantity = 2, reason = "Dressing changed twice on the ward" }, Ct));
        Assert.Equal(150_000 + 400_000 + 500_000, manual.GetProperty("totalMinor").GetInt64());
        var manualLine = manual.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("source").GetString() == "Manual");
        Assert.Equal("Procedure", manualLine.GetProperty("description").GetString()); // final review I2: the cashier reads the category
        Assert.Equal(500_000, manualLine.GetProperty("amountMinor").GetInt64());

        // Front desk reads bills but can't change them.
        await AssertStatusAsync(await frontDesk.PostAsJsonAsync($"/api/care/bills/{billId}/lines",
            new { priceItemId = dressing, quantity = 1, reason = "x" }, Ct), HttpStatusCode.Forbidden);
        var asFrontDesk = await Json(await frontDesk.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.All(asFrontDesk.GetProperty("lines").EnumerateArray(), l => Assert.False(l.TryGetProperty("clinicalRef", out _)));
        // Clinical staff see the clinical reference.
        var asDirector = await Json(await clinic.Owner.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Contains(asDirector.GetProperty("lines").EnumerateArray(), l => l.TryGetProperty("clinicalRef", out var r) && r.ValueKind == JsonValueKind.String);

        // Removing a line needs a reason; the line stays, struck out, and leaves the total.
        var testLineId = asDirector.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("description").GetString() == "Full blood count").GetProperty("id").GetGuid();
        await AssertStatusAsync(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines/{testLineId}/remove", new { reason = " " }, Ct), HttpStatusCode.BadRequest);
        var removed = await Json(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines/{testLineId}/remove", new { reason = "Test not done" }, Ct));
        Assert.Equal(150_000 + 500_000, removed.GetProperty("totalMinor").GetInt64());
        Assert.Equal(150_000 + 500_000, removed.GetProperty("balanceMinor").GetInt64());
        var gone = removed.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == testLineId);
        Assert.Equal("Test not done", gone.GetProperty("removedReason").GetString());
        await AssertStatusAsync(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines/{testLineId}/remove", new { reason = "Again" }, Ct), HttpStatusCode.Conflict);

        // The patient's bills list carries the totals.
        var list = (await Json(await cashier.GetAsync($"/api/care/bills?patientId={patientId}", Ct))).EnumerateArray().ToList();
        var row = Assert.Single(list);
        Assert.Equal(650_000, row.GetProperty("totalMinor").GetInt64());

        // Inactive items can't be charged.
        (await clinic.Owner.PutAsJsonAsync($"/api/care/price-list/{test}",
            new { name = "Full blood count", category = "Test", priceMinor = 400_000, active = false }, Ct)).EnsureSuccessStatusCode();
        await AssertStatusAsync(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines",
            new { priceItemId = test, quantity = 1, reason = "Repeat" }, Ct), HttpStatusCode.BadRequest);
        await AssertStatusAsync(await cashier.PostAsJsonAsync($"/api/care/bills/{billId}/lines",
            new { priceItemId = dressing, quantity = 0, reason = "Zero" }, Ct), HttpStatusCode.BadRequest);
    }
}
