using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 4, CareLink module 4c item 2: pharmacy stock. Items linked to price-list drugs, batches with expiry, purchase receipts, dispensing a
/// prescription first-expiry-first-out (atomic under concurrency, never below zero), stock counts with an audited reason, low-stock and
/// near-expiry alerts, reports, the pharmacist role, the logged department read and tenant isolation.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CarePharmacyTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? code = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"expected {(int)status}, got {(int)r.StatusCode} {text}");
        var body = JsonDocument.Parse(text).RootElement;
        if (code is not null) Assert.Equal(code, body.GetProperty("code").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record World(
        CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Pharmacist, Guid PharmacistId, HttpClient Desk, Guid PatientId,
        Guid ConsultationId, Guid PriceItemId, Guid ItemId);

    /// <summary>A clinic with a doctor, a pharmacist and the desk; a patient in the doctor's consultation; Amoxicillin on the price list and in stock items (reorder at 10).</summary>
    private async Task<World> WorldAsync(string name)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (pharmacist, pharmacistId) = await kit.StaffAsync(clinic, "Pharmacist");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Bayo", "Ade"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 37.0, systolic = 118, diastolic = 76, pulse = 80, respRate = 16, spO2 = 98 }, urgency = "Routine", doctorUserId = doctorId,
        }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        var priceItemId = await kit.PriceItemAsync(clinic, "Amoxicillin 500 mg caps", "Drug", 5_000);
        var itemId = Id(await Json(await pharmacist.PostAsJsonAsync("/api/care/pharmacy/items",
            new { name = "Amoxicillin 500 mg", form = "Capsule", unit = "capsule", priceItemId, reorderLevel = 10 }, Ct)));
        return new World(clinic, doctor, doctorId, pharmacist, pharmacistId, desk, patientId, consultationId, priceItemId, itemId);
    }

    private async Task<JsonElement> ReceiveAsync(World w, params (string Batch, DateOnly Expires, int Quantity)[] lines) =>
        await Json(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/receipts", new
        {
            supplier = "Emzor depot", reference = "INV-1",
            lines = lines.Select(l => new { itemId = w.ItemId, batchNumber = l.Batch, expiresOn = l.Expires, quantity = l.Quantity, unitCostMinor = 2_000L }).ToArray(),
        }, Ct));

    private async Task<(Guid Prescription, Guid Item)> PrescribeAsync(World w, int quantity)
    {
        var rx = await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId = w.PatientId, consultationId = w.ConsultationId, allergyAcknowledged = false,
            items = new[] { new { drug = "Amoxicillin", strength = "500 mg", dose = "1 capsule", frequency = "TDS", durationDays = 5, quantity, priceItemId = w.PriceItemId } },
        }, Ct));
        return (Id(rx), Id(rx.GetProperty("items")[0]));
    }

    private Task<List<DrugBatch>> BatchesAsync(Guid itemId) =>
        api.WithDbAsync(db => db.DrugBatches.IgnoreQueryFilters().Where(b => b.DrugItemId == itemId).OrderBy(b => b.ExpiresOn).ToListAsync(Ct));

    [Fact]
    public async Task Receive_then_dispense_takes_first_expiry_first_and_records_batches()
    {
        var w = await WorldAsync("Pharmacy FEFO Clinic");
        // Received in the "wrong" order: the later expiry first.
        var receipt = await ReceiveAsync(w, ("LATE-2", Today.AddDays(400), 30), ("SOON-1", Today.AddDays(120), 8));
        Assert.Equal(76_000L, receipt.GetProperty("totalCostMinor").GetInt64());
        Assert.Equal(2, receipt.GetProperty("batches").GetArrayLength());
        var item = await Json(await w.Pharmacist.GetAsync($"/api/care/pharmacy/items/{w.ItemId}", Ct));
        Assert.Equal((38, false), (item.GetProperty("onHand").GetInt32(), item.GetProperty("low").GetBoolean()));

        var (rx, rxItem) = await PrescribeAsync(w, 15);
        var queue = await Json(await w.Pharmacist.GetAsync("/api/care/pharmacy/queue", Ct));
        var row = Assert.Single(queue.EnumerateArray());
        Assert.Equal((rx, 1, 0), (row.GetProperty("prescriptionId").GetGuid(), row.GetProperty("items").GetInt32(), row.GetProperty("itemsDone").GetInt32()));

        var open = await Json(await w.Pharmacist.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct));
        var line = open.GetProperty("items")[0];
        Assert.Equal(("Amoxicillin", 15, 0, w.ItemId, 38), (line.GetProperty("drug").GetString(), line.GetProperty("quantity").GetInt32(),
            line.GetProperty("dispensed").GetInt32(), line.GetProperty("stockItemId").GetGuid(), line.GetProperty("stockOnHand").GetInt32()));

        // 10 now: all 8 of the soon batch, then 2 of the late one.
        var first = await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem, quantity = 10 } } }, Ct));
        Assert.Equal([("SOON-1", 8), ("LATE-2", 2)], first.GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("batchNumber").GetString(), l.GetProperty("quantity").GetInt32())).OrderBy(x => x.Item1 == "LATE-2"));
        var batches = await BatchesAsync(w.ItemId);
        Assert.Equal([0, 28], batches.Select(b => b.QuantityOnHand));

        // More than is left of the prescription is refused; the rest (5) by default.
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem, quantity = 6 } } }, Ct), HttpStatusCode.Conflict, "over_dispense");
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = rxItem } } }, Ct));
        Assert.Equal(23, (await BatchesAsync(w.ItemId)).Sum(b => b.QuantityOnHand));
        Assert.Empty((await Json(await w.Pharmacist.GetAsync("/api/care/pharmacy/queue", Ct))).EnumerateArray());
        var reopened = await Json(await w.Pharmacist.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct));
        Assert.Equal(2, reopened.GetProperty("dispensings").GetArrayLength());
        Assert.Equal(0, reopened.GetProperty("items")[0].GetProperty("remaining").GetInt32());

        // The movements report shows the receipts and dispenses with their batches, never the patient.
        var movements = await w.Pharmacist.GetAsync("/api/care/pharmacy/movements", Ct);
        var text = await movements.Content.ReadAsStringAsync(Ct);
        var rows = JsonDocument.Parse(text).RootElement.EnumerateArray().ToList();
        Assert.Equal(5, rows.Count);
        Assert.Equal(3, rows.Count(r => r.GetProperty("kind").GetString() == "Dispense"));
        Assert.DoesNotContain(w.PatientId.ToString(), text);
        Assert.DoesNotContain("Bayo", text);
        // Dispensing is audited with ids only.
        Assert.Equal(2, await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.Action == "care.dispensed" && a.EntityId == rx.ToString(), Ct)));
    }

    [Fact]
    public async Task Expired_batches_are_never_dispensed_and_short_stock_takes_nothing()
    {
        var w = await WorldAsync("Pharmacy Expiry Clinic");
        await ReceiveAsync(w, ("GOOD", Today.AddDays(300), 5));
        // An expired batch (straight into the database: receipts refuse expired stock).
        await api.WithDbAsync(db =>
        {
            db.DrugBatches.Add(new DrugBatch { TenantId = w.Clinic.TenantId, DrugItemId = w.ItemId, BatchNumber = "OLD", ExpiresOn = Today.AddDays(-1), QuantityReceived = 50, QuantityOnHand = 50 });
            return db.SaveChangesAsync(Ct);
        });
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/receipts",
            new { lines = new[] { new { itemId = w.ItemId, batchNumber = "X", expiresOn = Today.AddDays(-2), quantity = 1 } } }, Ct), HttpStatusCode.BadRequest, field: "lines[0].expiresOn");
        var item = await Json(await w.Pharmacist.GetAsync($"/api/care/pharmacy/items/{w.ItemId}", Ct));
        Assert.Equal((5, 50, true), (item.GetProperty("onHand").GetInt32(), item.GetProperty("expiredOnHand").GetInt32(), item.GetProperty("low").GetBoolean()));

        var (rx, rxItem) = await PrescribeAsync(w, 6);
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem } } }, Ct), HttpStatusCode.Conflict, "insufficient_stock");
        Assert.Equal([50, 5], (await BatchesAsync(w.ItemId)).Select(b => b.QuantityOnHand)); // nothing taken
        Assert.Equal(0, await api.WithDbAsync(db => db.Dispensings.IgnoreQueryFilters().CountAsync(d => d.PrescriptionId == rx, Ct)));

        var alerts = await Json(await w.Pharmacist.GetAsync("/api/care/pharmacy/alerts", Ct));
        Assert.Single(alerts.GetProperty("lowStock").EnumerateArray());
        Assert.Equal("OLD", Assert.Single(alerts.GetProperty("expired").EnumerateArray()).GetProperty("batchNumber").GetString());
        Assert.Empty(alerts.GetProperty("nearExpiry").EnumerateArray()); // GOOD expires in 300 days
        var expiring = await Json(await w.Pharmacist.GetAsync("/api/care/pharmacy/expiring?days=365", Ct));
        Assert.Equal(["OLD", "GOOD"], expiring.EnumerateArray().Select(b => b.GetProperty("batchNumber").GetString()));
    }

    [Fact]
    public async Task Concurrent_dispensing_never_oversells_stock()
    {
        var w = await WorldAsync("Pharmacy Race Clinic");
        await ReceiveAsync(w, ("ONLY", Today.AddDays(200), 10));
        // Eight prescriptions of 3 each (24 wanted, 10 in stock), dispensed at once by the pharmacist and the director.
        var orders = new List<(Guid Rx, Guid Item)>();
        for (var i = 0; i < 8; i++) orders.Add(await PrescribeAsync(w, 3));
        var tasks = orders.Select((o, i) => (i % 2 == 0 ? w.Pharmacist : w.Clinic.Owner).PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{o.Rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = o.Item } } }, Ct)).ToList();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"{(int)r.StatusCode}"));
        Assert.Equal(3, results.Count(r => r.IsSuccessStatusCode)); // 3 × 3 = 9 of 10
        var batch = Assert.Single(await BatchesAsync(w.ItemId));
        Assert.Equal(1, batch.QuantityOnHand);
        var dispensed = await api.WithDbAsync(db => db.StockMovements.IgnoreQueryFilters().Where(m => m.DrugItemId == w.ItemId && m.Kind == StockMovementKind.Dispense).SumAsync(m => m.Quantity, Ct));
        Assert.Equal(-9, dispensed);

        // The same prescription dispensed twice at once is dispensed once.
        await ReceiveAsync(w, ("MORE", Today.AddDays(250), 20));
        var (rx, item) = await PrescribeAsync(w, 4);
        var twice = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = item } } }, Ct)));
        Assert.Equal(1, twice.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(-4, await api.WithDbAsync(db => db.StockMovements.IgnoreQueryFilters().Where(m => m.PrescriptionItemId == item).SumAsync(m => m.Quantity, Ct)));
    }

    [Fact]
    public async Task Stock_count_adjusts_with_reason_and_is_audited()
    {
        var w = await WorldAsync("Pharmacy Count Clinic");
        await ReceiveAsync(w, ("C-1", Today.AddDays(200), 20), ("C-2", Today.AddDays(220), 20));
        var batches = await BatchesAsync(w.ItemId);
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/counts",
            new { lines = new[] { new { batchId = batches[0].Id, counted = 18 } } }, Ct), HttpStatusCode.BadRequest, field: "reason");
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/counts",
            new { reason = "Count", lines = new[] { new { batchId = batches[0].Id, counted = -1 } } }, Ct), HttpStatusCode.BadRequest, field: "lines[0].counted");
        var result = await Json(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/counts", new
        {
            reason = "Monthly count: two capsules damaged",
            lines = new[] { new { batchId = batches[0].Id, counted = 18 }, new { batchId = batches[1].Id, counted = 20 } },
        }, Ct));
        Assert.Equal(1, result.GetProperty("adjusted").GetInt32());
        var movement = result.GetProperty("movements")[0];
        Assert.Equal(("Adjustment", -2, 18, "Monthly count: two capsules damaged"), (movement.GetProperty("kind").GetString(), movement.GetProperty("quantity").GetInt32(),
            movement.GetProperty("balanceAfter").GetInt32(), movement.GetProperty("reason").GetString()));
        Assert.Equal(1, await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.Action == "care.stock_counted" && a.TenantId == w.Clinic.TenantId, Ct)));
        Assert.Equal([18, 20], (await BatchesAsync(w.ItemId)).Select(b => b.QuantityOnHand));
    }

    [Fact]
    public async Task Low_stock_and_near_expiry_tell_pharmacists_and_the_director_once()
    {
        var w = await WorldAsync("Pharmacy Alert Clinic");
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == w.Clinic.TenantId && m.Role == Saas.Domain.Core.MemberRole.Owner)
            .Select(m => m.UserId).FirstAsync(Ct));
        Task<List<Saas.Domain.Core.UserNotification>> NoticesAsync(string kind) => api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == w.Clinic.TenantId && n.Kind == kind).ToListAsync(Ct));

        await ReceiveAsync(w, ("A-1", Today.AddDays(60), 14));
        var (rx, item) = await PrescribeAsync(w, 5);
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = item, quantity = 2 } } }, Ct));
        Assert.Empty(await NoticesAsync(CareNotices.LowStock)); // 12 left, reorder at 10
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = item, quantity = 2 } } }, Ct));
        var low = await NoticesAsync(CareNotices.LowStock);
        Assert.Equal(new[] { ownerId, w.PharmacistId }.Order(), low.Select(n => n.UserId).Order());
        Assert.Equal("Amoxicillin 500 mg is low: 10 capsule left (reorder at 10).", low[0].Body);
        Assert.DoesNotContain("Bayo", low[0].Body);
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = item, quantity = 1 } } }, Ct));
        Assert.Equal(2, (await NoticesAsync(CareNotices.LowStock)).Count); // told once per crossing
        // A receipt lifts it above the level, re-arming the warning.
        await ReceiveAsync(w, ("A-2", Today.AddDays(500), 50));
        Assert.Null(await api.WithDbAsync(db => db.DrugItems.IgnoreQueryFilters().Where(i => i.Id == w.ItemId).Select(i => i.LowStockNotifiedAt).FirstAsync(Ct)));

        // The job flags the batch expiring in 60 days once (not the one in 500 days).
        var steps = () => api.AsTenantAsync<PharmacyService, int>(w.Clinic.TenantId, s => s.CheckStockAsync(DateTimeOffset.UtcNow, Ct));
        Assert.Equal(1, await steps());
        Assert.Equal(0, await steps());
        var expiry = await NoticesAsync(CareNotices.NearExpiry);
        Assert.Equal(2, expiry.Count);
        Assert.Equal("1 batch expires within 90 days.", expiry[0].Body);
    }

    /// <summary>
    /// Wave 4 review: dispensing from a stock item that isn't the prescribed drug (another price item) needs a substitution reason, kept on
    /// the movement and audited; a drug prescribed without a quantity needs one at the counter and is dispensed once.
    /// </summary>
    [Fact]
    public async Task Substitutes_need_a_reason_and_an_open_ended_drug_is_dispensed_once()
    {
        var w = await WorldAsync("Pharmacy Substitute Clinic");
        await ReceiveAsync(w, ("AMX-1", Today.AddDays(200), 30));
        var otherPrice = await kit.PriceItemAsync(w.Clinic, "Ampiclox 500 mg caps", "Drug", 6_000);
        var otherItem = Id(await Json(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/items",
            new { name = "Ampiclox 500 mg", form = "Capsule", unit = "capsule", priceItemId = otherPrice, reorderLevel = 0 }, Ct)));
        await Json(await w.Pharmacist.PostAsJsonAsync("/api/care/pharmacy/receipts", new
        {
            lines = new[] { new { itemId = otherItem, batchNumber = "AMP-1", expiresOn = Today.AddDays(200), quantity = 30, unitCostMinor = 2_000L } },
        }, Ct));
        var (rx, rxItem) = await PrescribeAsync(w, 6);

        // Another drug without a reason: refused, nothing taken.
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem, itemId = otherItem, quantity = 6 } } }, Ct), HttpStatusCode.BadRequest, field: "lines[0].substitutionReason");
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem, itemId = otherItem, quantity = 6, substitutionReason = new string('x', 301) } } }, Ct),
            HttpStatusCode.BadRequest, field: "lines[0].substitutionReason");
        Assert.Equal(30, (await BatchesAsync(otherItem)).Sum(b => b.QuantityOnHand));
        // The prescribed drug's own stock item needs no reason (named or by default).
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem, itemId = w.ItemId, quantity = 2 } } }, Ct));
        // With a reason: dispensed, the reason on the movement, and audited (ids only).
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense",
            new { lines = new[] { new { prescriptionItemId = rxItem, itemId = otherItem, quantity = 4, substitutionReason = "Amoxicillin batch on hold" } } }, Ct));
        var movement = await api.WithDbAsync(db => db.StockMovements.IgnoreQueryFilters().SingleAsync(m => m.DrugItemId == otherItem && m.Kind == StockMovementKind.Dispense, Ct));
        Assert.Equal((-4, "Substituted for the prescribed drug: Amoxicillin batch on hold"), (movement.Quantity, movement.Reason));
        var audit = await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.dispense_substituted", Ct));
        Assert.Contains(otherItem.ToString(), audit.AfterJson);
        Assert.Contains(rxItem.ToString(), audit.AfterJson);
        Assert.DoesNotContain("on hold", audit.AfterJson);

        // A drug written without a quantity: the counter must say how many, and it is dispensed once.
        var open = await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId = w.PatientId, consultationId = w.ConsultationId, allergyAcknowledged = false,
            items = new[] { new { drug = "Amoxicillin", strength = "500 mg", dose = "1 capsule", frequency = "TDS", durationDays = 5, quantity = (int?)null, priceItemId = w.PriceItemId } },
        }, Ct));
        var (openRx, openItem) = (Id(open), Id(open.GetProperty("items")[0]));
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{openRx}/dispense",
            new { lines = new[] { new { prescriptionItemId = openItem } } }, Ct), HttpStatusCode.BadRequest, field: "lines[0].quantity");
        await Json(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{openRx}/dispense",
            new { lines = new[] { new { prescriptionItemId = openItem, quantity = 15 } } }, Ct));
        await AssertProblemAsync(await w.Pharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{openRx}/dispense",
            new { lines = new[] { new { prescriptionItemId = openItem, quantity = 15 } } }, Ct), HttpStatusCode.Conflict, "over_dispense");
        Assert.Equal(30 - 2 - 15, (await BatchesAsync(w.ItemId)).Sum(b => b.QuantityOnHand));
    }

    private static Task<HttpResponseMessage> GetWithReasonAsync(HttpClient client, string path, string? reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (reason is not null) request.Headers.Add(RecordGuard.ReasonHeader, Uri.EscapeDataString(reason));
        return client.SendAsync(request, Ct);
    }

    /// <summary>
    /// Wave 4 review: the department open is the pharmacist's (and the director's). A doctor or nurse without a care relationship opens a
    /// prescription to dispense only as an emergency: a reason, an emergency row and the director's notice.
    /// </summary>
    [Fact]
    public async Task A_doctor_or_nurse_without_a_care_relationship_needs_a_reason_to_open_a_prescription()
    {
        var w = await WorldAsync("Pharmacy Privacy Clinic");
        var (rx, _) = await PrescribeAsync(w, 3);
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == w.Clinic.TenantId && m.Role == Saas.Domain.Core.MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        Task<int> NoticesAsync() => api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .CountAsync(n => n.UserId == ownerId && n.TenantId == w.Clinic.TenantId && n.Kind == "care.emergency_access", Ct));
        Task<List<RecordAccess>> LogAsync() => api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters()
            .Where(a => a.PatientId == w.PatientId && a.Area == "dispensing").OrderBy(a => a.At).ToListAsync(Ct));
        var (nurse, nurseId) = await kit.StaffAsync(w.Clinic, "Nurse");
        var (otherDoctor, _) = await kit.StaffAsync(w.Clinic, "Doctor");

        foreach (var client in new[] { nurse, otherDoctor })
            await AssertProblemAsync(await client.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct), HttpStatusCode.Conflict, "emergency_reason_required");
        Assert.Empty(await LogAsync());
        Assert.Equal(0, await NoticesAsync());

        var opened = await Json(await GetWithReasonAsync(nurse, $"/api/care/pharmacy/prescriptions/{rx}", "Night cover, pharmacy closed"));
        Assert.Equal("Amoxicillin", opened.GetProperty("items")[0].GetProperty("drug").GetString());
        var row = Assert.Single(await LogAsync());
        Assert.Equal((nurseId, "Nurse", true), (row.StaffUserId, row.Role, row.Emergency));
        Assert.Equal("Night cover, pharmacy closed", api.Services.GetRequiredService<ClinicalText>().Read(row.Reason));
        Assert.Equal(1, await NoticesAsync());

        // The pharmacist (no relationship), the prescribing doctor and the director open it without a reason.
        await Json(await w.Pharmacist.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct));
        await Json(await w.Doctor.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct));
        await Json(await w.Clinic.Owner.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct));
        var rest = (await LogAsync()).Skip(1).ToList();
        Assert.Equal([(w.PharmacistId, "Pharmacist"), (w.DoctorId, "Doctor"), (ownerId, "Medical director")], rest.Select(l => (l.StaffUserId, l.Role)));
        Assert.All(rest, l => Assert.Equal((false, (string?)null), (l.Emergency, l.Reason)));
        Assert.Equal(1, await NoticesAsync());
    }

    [Fact]
    public async Task Roles_logging_and_isolation()
    {
        var w = await WorldAsync("Pharmacy Role Clinic");
        await ReceiveAsync(w, ("R-1", Today.AddDays(200), 20));
        var (rx, item) = await PrescribeAsync(w, 2);
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        var (admin, _) = await kit.StaffAsync(w.Clinic, "Admin");
        var (nurse, _) = await kit.StaffAsync(w.Clinic, "Nurse");

        // Stock: pharmacists and leadership. Dispensing: pharmacists and clinical staff.
        foreach (var client in new[] { w.Desk, cashier, w.Doctor, nurse })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/care/pharmacy/items", Ct)).StatusCode);
        await Json(await admin.GetAsync("/api/care/pharmacy/items", Ct));
        foreach (var client in new[] { w.Desk, cashier, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/care/pharmacy/queue", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct)).StatusCode);
        }
        // A pharmacist sees no record and writes no prescription.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Pharmacist.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Pharmacist.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct)).StatusCode);
        var me = await Json(await w.Pharmacist.GetAsync("/api/care/staff/me", Ct));
        Assert.Equal((false, true, true, false), (me.GetProperty("isClinical").GetBoolean(), me.GetProperty("canDispense").GetBoolean(),
            me.GetProperty("canManageStock").GetBoolean(), me.GetProperty("canWorkLab").GetBoolean()));

        // Opening the prescription is logged (area dispensing); a nurse may dispense too.
        await Json(await w.Pharmacist.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct));
        var log = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == w.PatientId && a.Area == "dispensing").ToListAsync(Ct));
        Assert.Equal((w.PharmacistId, "Pharmacist", false), (Assert.Single(log).StaffUserId, log[0].Role, log[0].Emergency));
        await Json(await nurse.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = item } } }, Ct));

        // Another clinic sees none of it.
        var other = await kit.ClinicAsync("Pharmacy Other Clinic");
        var (otherPharmacist, _) = await kit.StaffAsync(other, "Pharmacist");
        var batch = (await BatchesAsync(w.ItemId))[0];
        foreach (var r in new[]
                 {
                     await otherPharmacist.GetAsync($"/api/care/pharmacy/items/{w.ItemId}", Ct),
                     await otherPharmacist.GetAsync($"/api/care/pharmacy/items/{w.ItemId}/batches", Ct),
                     await otherPharmacist.PutAsJsonAsync($"/api/care/pharmacy/items/{w.ItemId}", new { name = "Stolen" }, Ct),
                     await otherPharmacist.GetAsync($"/api/care/pharmacy/prescriptions/{rx}", Ct),
                     await otherPharmacist.PostAsJsonAsync($"/api/care/pharmacy/prescriptions/{rx}/dispense", new { lines = new[] { new { prescriptionItemId = item } } }, Ct),
                 })
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        await AssertProblemAsync(await otherPharmacist.PostAsJsonAsync("/api/care/pharmacy/counts",
            new { reason = "x", lines = new[] { new { batchId = batch.Id, counted = 0 } } }, Ct), HttpStatusCode.BadRequest, field: "lines[0].batchId");
        await AssertProblemAsync(await otherPharmacist.PostAsJsonAsync("/api/care/pharmacy/receipts",
            new { lines = new[] { new { itemId = w.ItemId, batchNumber = "X", expiresOn = Today.AddDays(30), quantity = 1 } } }, Ct), HttpStatusCode.BadRequest, field: "lines[0].itemId");
        Assert.Empty((await Json(await otherPharmacist.GetAsync("/api/care/pharmacy/items", Ct))).EnumerateArray());
        Assert.Empty((await Json(await otherPharmacist.GetAsync("/api/care/pharmacy/movements", Ct))).EnumerateArray());
        Assert.Equal(20 - 2, (await BatchesAsync(w.ItemId)).Sum(b => b.QuantityOnHand));
    }
}
