using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 7: prescriptions with the allergy warning, test requests and results, and the prescription PDF. Doctors prescribe and
/// request from their own visit's consultation; drug text, test descriptions and results are encrypted at rest; drug lines reach the bill
/// only when the clinic dispenses; a result notice never carries clinical detail; the PDF and the result file are clinical reads.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareOrderTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";
    private const string AssignedToAnother = "This visit is assigned to another doctor.";
    private const string DoctorsPrescribe = "Only doctors can prescribe.";
    private const string DoctorsRequestTests = "Only doctors can request tests.";
    private const string ReasonRequired = "Give a reason to open this patient's record.";
    private const string ResultReady = "A new result for Ifeoma is ready in your CareLink portal."; // the first name only (Task 15 ruling)

    // A 1×1 PNG (the clinic logo on the letterhead).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] ResultPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% walk result\n1 0 obj << >> endobj\ntrailer << >>\n%%EOF\n");

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        if (detail is null && field is null) return;
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
    }

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    private static Task<HttpResponseMessage> GetWithReasonAsync(HttpClient client, string path, string? reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (reason is not null) request.Headers.Add(RecordGuard.ReasonHeader, Uri.EscapeDataString(reason));
        return client.SendAsync(request, Ct);
    }

    private sealed record World(
        CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Nurse, Guid NurseId, HttpClient Desk, Guid PatientId, Guid VisitId,
        Guid ConsultationId);

    /// <summary>A clinic with a doctor, a nurse and the front desk; a patient (allergic to <paramref name="allergies"/>) triaged for the doctor, who has opened the consultation.</summary>
    private async Task<World> WorldAsync(string name, params string[] allergies)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Ifeoma", "Okafor"));
        (await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies, conditions = Array.Empty<string>(), bloodGroup = "O+", genotype = "AA" }, Ct)).EnsureSuccessStatusCode();
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 38.9, systolic = 118, diastolic = 76, pulse = 104, respRate = 18, spO2 = 97 },
            urgency = "Routine", doctorUserId = doctorId,
        }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        return new World(clinic, doctor, doctorId, nurse, nurseId, desk, patientId, visitId, consultationId);
    }

    private static object Item(string drug, string frequency = "TDS", Guid? priceItemId = null, int? quantity = null) => new
    {
        drug, strength = "500 mg", dose = "1 capsule", frequency, durationDays = 7, quantity, route = "Oral", instructions = "Take after meals",
        priceItemId,
    };

    private static Task<HttpResponseMessage> PrescribeAsync(HttpClient client, World w, bool acknowledged, params object[] items) =>
        client.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, consultationId = w.ConsultationId, allergyAcknowledged = acknowledged, items }, Ct);

    private static Task<HttpResponseMessage> RequestTestAsync(HttpClient client, World w, string description, Guid? priceItemId = null, string kind = "Lab") =>
        client.PostAsJsonAsync("/api/care/tests", new { patientId = w.PatientId, consultationId = w.ConsultationId, kind, description, priceItemId }, Ct);

    private Task<string> RawRowsAsync(string table, string column, Guid id) => api.WithDbAsync(db => db.Database
        .SqlQueryRaw<string>($"SELECT coalesce(string_agg(row_to_json(t)::text, ' '), '') AS \"Value\" FROM {table} t WHERE {column} = {{0}}", id)
        .FirstAsync(Ct));

    // ---------- Frequencies ----------

    [Fact]
    public void Frequency_doses_per_day()
    {
        var expected = new Dictionary<DoseFrequency, int>
        {
            [DoseFrequency.OD] = 1, [DoseFrequency.BD] = 2, [DoseFrequency.TDS] = 3, [DoseFrequency.QDS] = 4, [DoseFrequency.Q4H] = 6,
            [DoseFrequency.Q6H] = 4, [DoseFrequency.Q8H] = 3, [DoseFrequency.Nocte] = 1, [DoseFrequency.Stat] = 1, [DoseFrequency.PRN] = 0,
        };
        Assert.Equal(Enum.GetValues<DoseFrequency>().Length, expected.Count);
        foreach (var (frequency, doses) in expected) Assert.Equal(doses, FrequencyDosesPerDay.For(frequency));
        Assert.True(FrequencyDosesPerDay.IsOnce(DoseFrequency.Stat));
        Assert.True(FrequencyDosesPerDay.IsAsNeeded(DoseFrequency.PRN));
        Assert.All(Enum.GetValues<DoseFrequency>().Where(f => f is not DoseFrequency.Stat), f => Assert.False(FrequencyDosesPerDay.IsOnce(f)));
        Assert.All(Enum.GetValues<DoseFrequency>().Where(f => f is not DoseFrequency.PRN), f => Assert.False(FrequencyDosesPerDay.IsAsNeeded(f)));
    }

    // ---------- Allergy ----------

    [Fact]
    public async Task Allergy_match_needs_acknowledgement()
    {
        var w = await WorldAsync("Allergy Clinic", "Penicillin", "Co-trimoxazole tablets");
        var logBefore = (await AccessLogAsync(w.PatientId)).Count;

        // The drug name contains the allergen (case-insensitively): 409 with the exact sentence, and nothing is saved.
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, Item("Amoxicillin/penicillin")),
            HttpStatusCode.Conflict, "Ifeoma Okafor is recorded as allergic to Penicillin.");
        // The allergen contains the drug name: a match too.
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, Item("Paracetamol"), Item("co-trimoxazole")),
            HttpStatusCode.Conflict, "Ifeoma Okafor is recorded as allergic to Co-trimoxazole tablets.");
        Assert.Equal(0, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));

        // No match: prescribed without acknowledgement.
        var plain = await Json(await PrescribeAsync(w.Doctor, w, false, Item("Paracetamol", "QDS")));
        Assert.False(plain.GetProperty("allergyAcknowledged").GetBoolean());
        Assert.Equal("Paracetamol", plain.GetProperty("items")[0].GetProperty("drug").GetString());

        // Acknowledged: saved, flagged, and the override is audited with ids only.
        var rx = await Json(await PrescribeAsync(w.Doctor, w, true, Item("Amoxicillin/penicillin")));
        Assert.True(rx.GetProperty("allergyAcknowledged").GetBoolean());
        Assert.Equal(w.ConsultationId, rx.GetProperty("consultationId").GetGuid());
        Assert.Equal(w.DoctorId, rx.GetProperty("doctorUserId").GetGuid());
        var item = rx.GetProperty("items")[0];
        Assert.Equal("Amoxicillin/penicillin", item.GetProperty("drug").GetString());
        Assert.Equal("500 mg", item.GetProperty("strength").GetString());
        Assert.Equal("1 capsule", item.GetProperty("dose").GetString());
        Assert.Equal("TDS", item.GetProperty("frequency").GetString());
        Assert.Equal("Oral", item.GetProperty("route").GetString());
        Assert.Equal("Take after meals", item.GetProperty("instructions").GetString());
        Assert.Equal(7, item.GetProperty("durationDays").GetInt32());
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.allergy_override").ToListAsync(Ct)));
        Assert.Equal(Id(rx).ToString(), audit.EntityId);
        foreach (var word in new[] { "Penicillin", "penicillin", "Amoxicillin" }) Assert.DoesNotContain(word, audit.AfterJson ?? "");

        // No raw column holds the drug, its strength, dose, route or instructions in plain text.
        var raw = await RawRowsAsync("prescription_items", "prescription_id", Id(rx));
        Assert.NotEmpty(raw);
        foreach (var word in new[] { "Amoxicillin", "penicillin", "500 mg", "1 capsule", "Oral", "after meals" }) Assert.DoesNotContain(word, raw);

        // Writes are not record opens.
        Assert.Equal(logBefore, (await AccessLogAsync(w.PatientId)).Count);

        // The timeline carries the prescriptions, decrypted.
        var record = await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct));
        var drugs = record.GetProperty("prescriptions").EnumerateArray().SelectMany(p => p.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("drug").GetString()).ToList();
        Assert.Equal(["Amoxicillin/penicillin", "Paracetamol"], drugs); // newest first

        // Who prescribes: doctors only, and only on their own visit.
        await AssertProblemAsync(await PrescribeAsync(w.Nurse, w, false, Item("Paracetamol")), HttpStatusCode.Forbidden, DoctorsPrescribe);
        await AssertProblemAsync(await PrescribeAsync(w.Desk, w, false, Item("Paracetamol")), HttpStatusCode.Forbidden, ClinicalRefused);
        var (otherDoctor, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await PrescribeAsync(otherDoctor, w, false, Item("Paracetamol")), HttpStatusCode.Forbidden, AssignedToAnother);
        await AssertProblemAsync(await PrescribeAsync(w.Clinic.Owner, w, false, Item("Paracetamol")), HttpStatusCode.Forbidden, AssignedToAnother);
        // An unrelated doctor never learns the allergy from the warning: the visit check comes first.
        await AssertProblemAsync(await PrescribeAsync(otherDoctor, w, false, Item("Penicillin V")), HttpStatusCode.Forbidden, AssignedToAnother);

        // Field errors.
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false), HttpStatusCode.BadRequest, field: "items");
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, new { drug = "", dose = "1", frequency = "OD" }), HttpStatusCode.BadRequest, field: "items[0].drug");
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, new { drug = "Zinc", dose = "", frequency = "OD" }), HttpStatusCode.BadRequest, field: "items[0].dose");
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, new { drug = "Zinc", dose = "1", frequency = (string?)null }), HttpStatusCode.BadRequest, field: "items[0].frequency");
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, allergyAcknowledged = false, items = new[] { Item("Zinc") } }, Ct), HttpStatusCode.BadRequest, field: "consultationId");
        var otherPatient = Id(await kit.PatientAsync(w.Clinic, "Other", "Person"));
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = otherPatient, consultationId = w.ConsultationId, allergyAcknowledged = false, items = new[] { Item("Zinc") } }, Ct),
            HttpStatusCode.BadRequest, field: "consultationId");

        // Another clinic's consultation is 404.
        var other = await WorldAsync("Allergy Other Clinic");
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, consultationId = other.ConsultationId, allergyAcknowledged = false, items = new[] { Item("Zinc") } }, Ct),
            HttpStatusCode.NotFound);
    }

    // ---------- Billing ----------

    private async Task<JsonElement> VisitBillAsync(HttpClient client, World w)
    {
        var billId = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(b => b.VisitId == w.VisitId).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(Ct));
        Assert.NotNull(billId);
        return await Json(await client.GetAsync($"/api/care/bills/{billId}", Ct));
    }

    [Fact]
    public async Task Drug_lines_billed_only_when_dispensing()
    {
        var w = await WorldAsync("Dispensing Clinic");
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        var amoxicillin = await kit.PriceItemAsync(w.Clinic, "Amoxicillin 500mg capsule", "Drug", 5_000);
        var smear = await kit.PriceItemAsync(w.Clinic, "Malaria smear", "Test", 150_000);

        // The clinic dispenses (the default): an item with a price item is charged, one without isn't.
        var rx = await Json(await PrescribeAsync(w.Doctor, w, false, Item("Amoxicillin", priceItemId: amoxicillin, quantity: 21), Item("Vitamin C")));
        var charged = rx.GetProperty("items")[0];
        var bill = await VisitBillAsync(w.Doctor, w);
        var line = Assert.Single(bill.GetProperty("lines").EnumerateArray());
        Assert.Equal("Amoxicillin 500mg capsule", line.GetProperty("description").GetString());
        Assert.Equal(21, line.GetProperty("quantity").GetInt32());
        Assert.Equal(105_000, line.GetProperty("amountMinor").GetInt64());
        Assert.Equal("Auto", line.GetProperty("source").GetString());
        Assert.Equal(Id(charged).ToString(), line.GetProperty("clinicalRef").GetString());
        Assert.Equal(105_000, bill.GetProperty("totalMinor").GetInt64());

        // The cashier sees the line by its category ("Drug") and the amount, never the drug name or the clinical reference (final review I2).
        var cashierBill = await VisitBillAsync(cashier, w);
        var cashierLine = Assert.Single(cashierBill.GetProperty("lines").EnumerateArray());
        Assert.Equal("Drug", cashierLine.GetProperty("description").GetString());
        Assert.DoesNotContain("Amoxicillin", cashierBill.GetRawText());
        Assert.False(cashierLine.TryGetProperty("clinicalRef", out _));

        // No quantity: one is charged.
        await Json(await PrescribeAsync(w.Doctor, w, false, Item("Amoxicillin", priceItemId: amoxicillin)));
        Assert.Equal(2, (await VisitBillAsync(w.Doctor, w)).GetProperty("lines").GetArrayLength());
        Assert.Equal(110_000, (await VisitBillAsync(w.Doctor, w)).GetProperty("totalMinor").GetInt64());

        // A clinic that doesn't dispense: the prescription is saved but no drug line is added.
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(c => c.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DispensesDrugs, false), Ct));
        await Json(await PrescribeAsync(w.Doctor, w, false, Item("Amoxicillin", priceItemId: amoxicillin, quantity: 14)));
        Assert.Equal(2, (await VisitBillAsync(w.Doctor, w)).GetProperty("lines").GetArrayLength());
        Assert.Equal(3, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));

        // Tests are charged whether or not the clinic dispenses drugs.
        var test = await Json(await RequestTestAsync(w.Doctor, w, "Malaria parasite smear", smear));
        var lines = (await VisitBillAsync(w.Doctor, w)).GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal("Malaria smear", lines[2].GetProperty("description").GetString());
        Assert.Equal(Id(test).ToString(), lines[2].GetProperty("clinicalRef").GetString());

        // An inactive price item is a field error, and nothing is saved.
        await api.WithDbAsync(db => db.PriceItems.IgnoreQueryFilters().Where(i => i.Id == amoxicillin).ExecuteUpdateAsync(s => s.SetProperty(i => i.Active, false), Ct));
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(c => c.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DispensesDrugs, true), Ct));
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, Item("Zinc"), Item("Amoxicillin", priceItemId: amoxicillin)),
            HttpStatusCode.BadRequest, field: "items[1].priceItemId");
        Assert.Equal(3, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));
        Assert.Equal(3, (await VisitBillAsync(w.Doctor, w)).GetProperty("lines").GetArrayLength());
    }

    // ---------- Tests and results ----------

    private sealed class StaffUser(Guid userId, MemberRole role) : ICurrentUser
    {
        public Guid? UserId => userId;
        public MemberRole? Role => role;
        public string? IpAddress => null;
    }

    private sealed class FixedRecipients(params Guid[] users) : ICarePortalRecipients
    {
        public Task<IReadOnlyList<Guid>> ForPatientAsync(Guid patientId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>(users);
    }

    /// <summary>A customer account with no business (a portal user, as Task 15 links them to patients).</summary>
    private Task<Guid> PortalUserAsync(string name) => api.WithDbAsync(async db =>
    {
        var email = $"portal-{Guid.NewGuid():N}@example.com";
        var account = new AppUser { FullName = name, Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
        db.Users.Add(account);
        await db.SaveChangesAsync(Ct);
        return account.Id;
    });

    [Fact]
    public async Task Result_notice_has_no_clinical_detail()
    {
        var w = await WorldAsync("Result Clinic");
        var logBefore = (await AccessLogAsync(w.PatientId)).Count;

        // Doctors request; a nurse can't, the front desk can't.
        await AssertProblemAsync(await RequestTestAsync(w.Nurse, w, "Full blood count"), HttpStatusCode.Forbidden, DoctorsRequestTests);
        await AssertProblemAsync(await RequestTestAsync(w.Desk, w, "Full blood count"), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await RequestTestAsync(w.Doctor, w, ""), HttpStatusCode.BadRequest, field: "description");
        var test = await Json(await RequestTestAsync(w.Doctor, w, "Malaria parasite smear"));
        Assert.Equal("Requested", test.GetProperty("status").GetString());
        Assert.Equal("Lab", test.GetProperty("kind").GetString());
        Assert.Equal("Malaria parasite smear", test.GetProperty("description").GetString());
        var testId = Id(test);

        // A nurse records the result (text). The front desk can't; a blank result is a field error.
        await AssertProblemAsync(await w.Desk.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Negative" }, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "  " }, Ct), HttpStatusCode.BadRequest, field: "text");
        var resulted = await Json(await w.Nurse.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Plasmodium falciparum seen ++" }, Ct));
        Assert.Equal("Resulted", resulted.GetProperty("status").GetString());
        Assert.Equal("Plasmodium falciparum seen ++", resulted.GetProperty("resultText").GetString());
        Assert.Equal(w.NurseId, resulted.GetProperty("resultedBy").GetGuid());
        Assert.False(resulted.GetProperty("hasFile").GetBoolean());
        // The answer echoes the nurse's own write: not the doctor's request text.
        Assert.False(resulted.TryGetProperty("description", out _));
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Again" }, Ct), HttpStatusCode.Conflict);

        // Encrypted at rest: neither the description nor the result is in a raw column.
        var raw = await RawRowsAsync("test_requests", "id", testId);
        Assert.NotEmpty(raw);
        foreach (var word in new[] { "Malaria", "smear", "Plasmodium", "falciparum" }) Assert.DoesNotContain(word, raw);

        // Writes are not record opens; the timeline shows the result to clinical staff.
        Assert.Equal(logBefore, (await AccessLogAsync(w.PatientId)).Count);
        var record = await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct));
        var onRecord = Assert.Single(record.GetProperty("results").EnumerateArray());
        Assert.Equal("Plasmodium falciparum seen ++", onRecord.GetProperty("resultText").GetString());
        Assert.Equal("Malaria parasite smear", onRecord.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, onRecord.GetProperty("reviewedAt").ValueKind); // post-build: awaiting a doctor's review

        // No portal account is linked to the patient yet (Task 15), so nobody is told, and staff never are.
        Assert.Equal(0, await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.result_ready", Ct)));

        // With linked portal users (the Task 15 hook), each gets the generic sentence: no test, result, drug or patient detail.
        var second = Id(await Json(await RequestTestAsync(w.Doctor, w, "Chest X-ray", kind: "Imaging")));
        var portalUser = await PortalUserAsync("Portal Parent");
        var otherPortalUser = await PortalUserAsync("Portal Guardian");
        await Json(await PrescribeAsync(w.Doctor, w, false, Item("Ciprofloxacin")));
        using (var scope = api.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(w.Clinic.TenantId);
            ICurrentUser user = new StaffUser(w.NurseId, MemberRole.Staff);
            var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
            ICarePortalRecipients recipients = new FixedRecipients(portalUser, otherPortalUser);
            var careNotices = ActivatorUtilities.CreateInstance<CareNotices>(sp, recipients);
            var tests = ActivatorUtilities.CreateInstance<TestService>(sp, access, careNotices);
            await tests.RecordResultAsync(second, "Right lower lobe consolidation", null, Ct);
        }
        // Post-build (user decision): recording tells nobody; the doctor's review does.
        Assert.Equal(0, await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .CountAsync(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.result_ready", Ct)));
        using (var scope = api.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(w.Clinic.TenantId);
            ICurrentUser user = new StaffUser(w.DoctorId, MemberRole.Staff);
            var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
            ICarePortalRecipients recipients = new FixedRecipients(portalUser, otherPortalUser);
            var careNotices = ActivatorUtilities.CreateInstance<CareNotices>(sp, recipients);
            var tests = ActivatorUtilities.CreateInstance<TestService>(sp, access, careNotices);
            await tests.ReviewAsync(second, Ct);
        }
        var notices = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.result_ready").ToListAsync(Ct));
        Assert.Equal(2, notices.Count);
        Assert.Equal(new[] { portalUser, otherPortalUser }.Order(), notices.Select(n => n.UserId).Order());
        foreach (var n in notices)
        {
            Assert.Equal(ResultReady, n.Body);
            Assert.Equal(Saas.Application.Billing.Products.Care, n.Product);
            foreach (var text in new[] { n.Title, n.Body, n.Link })
                foreach (var word in new[] { "X-ray", "Chest", "consolidation", "lobe", "Malaria", "Plasmodium", "Ciprofloxacin", "Okafor", testId.ToString(), second.ToString() })
                    Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }

        // A result file (multipart, with or without text): stored privately and served through a signed link that is a logged clinical read.
        var third = Id(await Json(await RequestTestAsync(w.Doctor, w, "Urinalysis")));
        using (var form = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(ResultPdf);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "urinalysis-positive.pdf");
            var withFile = await Json(await w.Nurse.PostAsync($"/api/care/tests/{third}/result", form, Ct));
            Assert.True(withFile.GetProperty("hasFile").GetBoolean());
            Assert.Equal(JsonValueKind.Null, withFile.GetProperty("resultText").ValueKind);
        }
        var key = await api.WithDbAsync(db => db.TestRequests.IgnoreQueryFilters().Where(t => t.Id == third).Select(t => t.ResultFileKey).FirstAsync(Ct));
        Assert.StartsWith($"private/care/{w.Clinic.TenantId}/", key);
        Assert.DoesNotContain("urinalysis", key, StringComparison.OrdinalIgnoreCase);

        // Not a PDF or an image: a field error.
        var fourth = Id(await Json(await RequestTestAsync(w.Doctor, w, "Lipid profile")));
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent("just text"u8.ToArray()), "file", "result.txt");
            await AssertProblemAsync(await w.Nurse.PostAsync($"/api/care/tests/{fourth}/result", form, Ct), HttpStatusCode.BadRequest, field: "file");
        }

        var logs = (await AccessLogAsync(w.PatientId)).Count;
        var link = await Json(await w.Doctor.GetAsync($"/api/care/tests/{third}/file", Ct));
        var opened = await AccessLogAsync(w.PatientId);
        Assert.Equal(logs + 1, opened.Count);
        Assert.Equal("file", opened[^1].Area);
        Assert.Equal(w.DoctorId, opened[^1].StaffUserId);
        var url = link.GetProperty("url").GetString()!;
        var anonymous = api.CreateClient();
        var download = await anonymous.GetAsync(new Uri(url).PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ResultPdf, await download.Content.ReadAsByteArrayAsync(Ct));
        // A tampered link is refused.
        var tampered = new Uri(url).PathAndQuery;
        tampered = tampered[..^4] + (tampered.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync(tampered, Ct)).StatusCode);

        // Non-clinical staff get 403 and nothing is logged; an unrelated doctor needs a reason; a test with no file is 404.
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        foreach (var client in new[] { w.Desk, cashier })
            await AssertProblemAsync(await client.GetAsync($"/api/care/tests/{third}/file", Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        Assert.Equal(logs + 1, (await AccessLogAsync(w.PatientId)).Count);
        var (stranger, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await stranger.GetAsync($"/api/care/tests/{third}/file", Ct), HttpStatusCode.Conflict, ReasonRequired);
        await Json(await GetWithReasonAsync(stranger, $"/api/care/tests/{third}/file", "Covering the ward"));
        Assert.True((await AccessLogAsync(w.PatientId))[^1].Emergency);
        await AssertProblemAsync(await w.Doctor.GetAsync($"/api/care/tests/{testId}/file", Ct), HttpStatusCode.NotFound);
    }

    // ---------- PDF ----------

    [Fact]
    public async Task Prescription_pdf_is_logged_and_clinical_only()
    {
        var w = await WorldAsync("Letterhead Clinic");
        var rx = Id(await Json(await PrescribeAsync(w.Doctor, w, false, Item("Amoxicillin", "BD"), Item("Paracetamol", "PRN"))));
        var logs = (await AccessLogAsync(w.PatientId)).Count;

        // The prescribing doctor downloads it: a PDF, logged as area pdf.
        var response = await w.Doctor.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var plain = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(plain, 0, 4));
        var log = await AccessLogAsync(w.PatientId);
        Assert.Equal(logs + 1, log.Count);
        Assert.Equal("pdf", log[^1].Area);
        Assert.Equal(w.DoctorId, log[^1].StaffUserId);
        Assert.False(log[^1].Emergency);

        // Non-clinical staff: 403 and no log row.
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        var (admin, _) = await kit.StaffAsync(w.Clinic, "Admin");
        foreach (var client in new[] { w.Desk, cashier, admin })
            await AssertProblemAsync(await client.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        Assert.Equal(logs + 1, (await AccessLogAsync(w.PatientId)).Count);

        // A nurse with a care relationship (she triaged the patient) reads it; an unrelated doctor needs a reason (an emergency open).
        Assert.Equal(HttpStatusCode.OK, (await w.Nurse.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct)).StatusCode);
        var (stranger, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await stranger.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct), HttpStatusCode.Conflict, ReasonRequired);
        Assert.Equal(HttpStatusCode.OK, (await GetWithReasonAsync(stranger, $"/api/care/prescriptions/{rx}/pdf", "Pharmacy query")).StatusCode);
        var last = (await AccessLogAsync(w.PatientId))[^1];
        Assert.True(last.Emergency);
        Assert.Equal("pdf", last.Area);

        // Unknown and another clinic's prescriptions are 404.
        await AssertProblemAsync(await w.Doctor.GetAsync($"/api/care/prescriptions/{Guid.NewGuid()}/pdf", Ct), HttpStatusCode.NotFound);
        var other = await WorldAsync("Letterhead Other Clinic");
        var foreign = Id(await Json(await PrescribeAsync(other.Doctor, other, false, Item("Zinc"))));
        await AssertProblemAsync(await w.Clinic.Owner.GetAsync($"/api/care/prescriptions/{foreign}/pdf", Ct), HttpStatusCode.NotFound);

        // With a logo, the letterhead carries it.
        Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(plain));
        var storage = api.Services.GetRequiredService<IFileStorage>();
        var logoKey = $"private/care/{w.Clinic.TenantId}/logo-{Guid.NewGuid():N}.png";
        await using (var stream = new MemoryStream(Png)) await storage.SaveAsync(logoKey, stream, "image/png", Ct);
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(c => c.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LogoKey, logoKey), Ct));
        var branded = await (await w.Doctor.GetAsync($"/api/care/prescriptions/{rx}/pdf", Ct)).Content.ReadAsByteArrayAsync(Ct);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(branded, 0, 4));
        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(branded));
    }

    // ---------- Fix round 1 ----------

    /// <summary>
    /// Task 8 (replaces I1's stub): an order from an admission needs an admission of this clinic, for this patient, not discharged, and a
    /// live care relationship (or the director); it is charged to the admission's bill.
    /// </summary>
    [Fact]
    public async Task Admission_orders_are_validated_and_billed_to_the_admission()
    {
        var w = await WorldAsync("Admission Orders Clinic");
        var drug = await kit.PriceItemAsync(w.Clinic, "Ward zinc", "Drug", 3_000);
        var lab = await kit.PriceItemAsync(w.Clinic, "Ward FBC", "Test", 400_000);
        object Rx(Guid admissionId, Guid? patientId = null, Guid? consultationId = null) =>
            new { patientId = patientId ?? w.PatientId, consultationId, admissionId, allergyAcknowledged = false, items = new[] { Item("Zinc", priceItemId: drug, quantity: 10) } };
        object Lab(Guid admissionId) => new { patientId = w.PatientId, admissionId, kind = "Lab", description = "Full blood count", priceItemId = lab };

        // An unknown admission, or another clinic's, is 404 on both routes.
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", Rx(Guid.NewGuid()), Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/tests", Lab(Guid.NewGuid()), Ct), HttpStatusCode.NotFound);
        var other = await kit.ClinicAsync("Admission Orders Other Clinic");
        await api.MakeProAsync(other.TenantId, "care", "clinic"); // admitting needs a plan with beds
        var foreign = await kit.AdmitAsync(other.Owner, Id(await kit.PatientAsync(other, "Other", "Clinic")));
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", Rx(foreign), Ct), HttpStatusCode.NotFound);

        var admissionId = await kit.AdmitAsync(w.Doctor, w.PatientId, consultationId: w.ConsultationId, emergencyReason: null);
        // Both a consultation and an admission: 400. Another patient's admission: 400.
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", Rx(admissionId, consultationId: w.ConsultationId), Ct),
            HttpStatusCode.BadRequest, field: "consultationId");
        var secondPatient = Id(await kit.PatientAsync(w.Clinic, "Second", "Patient"));
        var secondAdmission = await kit.AdmitAsync(w.Clinic.Owner, secondPatient);
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", Rx(secondAdmission), Ct), HttpStatusCode.BadRequest, field: "admissionId");
        // A doctor with no care relationship with the patient: 403. A nurse: 403 (doctors prescribe).
        var (stranger, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await stranger.PostAsJsonAsync("/api/care/prescriptions", Rx(admissionId), Ct), HttpStatusCode.Forbidden,
            "Your care of this patient has ended. Ask the medical director.");
        await AssertProblemAsync(await stranger.PostAsJsonAsync("/api/care/tests", Lab(admissionId), Ct), HttpStatusCode.Forbidden,
            "Your care of this patient has ended. Ask the medical director.");
        await AssertProblemAsync(await w.Nurse.PostAsJsonAsync("/api/care/prescriptions", Rx(admissionId), Ct), HttpStatusCode.Forbidden, DoctorsPrescribe);
        Assert.Equal(0, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));

        // The admitting doctor prescribes and requests; the director may too. Every charge goes on the admission bill, none on the visit's.
        var rx = await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", Rx(admissionId), Ct));
        Assert.Equal(admissionId, rx.GetProperty("admissionId").GetGuid());
        Assert.Equal(JsonValueKind.Null, rx.GetProperty("consultationId").ValueKind);
        var test = await Json(await w.Doctor.PostAsJsonAsync("/api/care/tests", Lab(admissionId), Ct));
        Assert.Equal(admissionId, test.GetProperty("admissionId").GetGuid());
        await Json(await w.Clinic.Owner.PostAsJsonAsync("/api/care/tests", Lab(admissionId), Ct));
        var bill = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().FirstAsync(b => b.AdmissionId == admissionId, Ct));
        var lines = await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters().Where(l => l.BillId == bill.Id).ToListAsync(Ct));
        Assert.Equal(3, lines.Count);
        Assert.Equal(30_000 + 400_000 + 400_000, bill.TotalMinor);
        var visitBill = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().FirstAsync(b => b.VisitId == w.VisitId, Ct));
        Assert.DoesNotContain(await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters().Where(l => l.BillId == visitBill.Id).ToListAsync(Ct)),
            l => l.PriceItemId == drug || l.PriceItemId == lab);

        // A discharged admission takes no new orders (Task 10 discharges; set directly here).
        await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.Id == admissionId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AdmissionStatus.Discharged).SetProperty(a => a.DischargedAt, DateTimeOffset.UtcNow), Ct));
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions", Rx(admissionId), Ct), HttpStatusCode.Conflict,
            "This patient has been discharged.");
        await AssertProblemAsync(await w.Clinic.Owner.PostAsJsonAsync("/api/care/tests", Lab(admissionId), Ct), HttpStatusCode.Conflict,
            "This patient has been discharged.");
        Assert.Equal(1, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));
    }

    /// <summary>I2 and M3: every matched allergen is named in the 409, and an acknowledged prescription stores what was overridden, encrypted.</summary>
    [Fact]
    public async Task Every_matched_allergen_is_named_and_stored()
    {
        var w = await WorldAsync("Two Allergy Clinic", "Penicillin", "Sulfa");
        await AssertProblemAsync(await PrescribeAsync(w.Doctor, w, false, Item("Amoxicillin/penicillin"), Item("Sulfamethoxazole")),
            HttpStatusCode.Conflict, "Ifeoma Okafor is recorded as allergic to Penicillin and Sulfa.");
        Assert.Equal(0, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));

        var rx = await Json(await PrescribeAsync(w.Doctor, w, true, Item("Amoxicillin/penicillin"), Item("Sulfamethoxazole")));
        Assert.True(rx.GetProperty("allergyAcknowledged").GetBoolean());
        Assert.Equal(["Penicillin", "Sulfa"], rx.GetProperty("overriddenAllergens").EnumerateArray().Select(a => a.GetString()).ToArray());
        var raw = await RawRowsAsync("prescriptions", "id", Id(rx));
        Assert.NotEmpty(raw);
        Assert.DoesNotContain("Penicillin", raw);
        Assert.DoesNotContain("Sulfa", raw);
        var record = await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct));
        Assert.Equal(["Penicillin", "Sulfa"], record.GetProperty("prescriptions")[0].GetProperty("overriddenAllergens").EnumerateArray().Select(a => a.GetString()).ToArray());

        // Ticking the box with no match stores no override.
        var none = await Json(await PrescribeAsync(w.Doctor, w, true, Item("Paracetamol")));
        Assert.False(none.GetProperty("allergyAcknowledged").GetBoolean());
        Assert.Empty(none.GetProperty("overriddenAllergens").EnumerateArray());

        // Three or more allergens are joined with commas and "and".
        Assert.Equal("Ada Obi is recorded as allergic to Penicillin, Sulfa and Aspirin.",
            PrescriptionService.AllergyWarning("Ada Obi", ["Penicillin", "Sulfa", "Aspirin"]));
        Assert.Equal("Ada Obi is recorded as allergic to Penicillin.", PrescriptionService.AllergyWarning("Ada Obi", ["Penicillin"]));
    }

    /// <summary>I3: the matching rules, as one pure function.</summary>
    [Theory]
    [InlineData("Augmentin", "Penicillin", "Penicillin")]
    [InlineData("Amoxicillin", "Penicillin", "Penicillin")]
    [InlineData("Ampiclox", "Penicillin", "Penicillin")]
    [InlineData("Phenoxymethylpenicillin", "Penicillin - rash", "Penicillin - rash")]
    [InlineData("Ampicillin", "Amoxicillin", "Amoxicillin")] // two members of one class
    [InlineData("Septrin", "Sulfa", "Sulfa")]
    [InlineData("Fansidar", "Sulfa drugs", "Sulfa drugs")]
    [InlineData("Sulfadoxine", "Sulpha", "Sulpha")]
    [InlineData("Sulphadoxine-pyrimethamine", "Sulfa", "Sulfa")]
    [InlineData("Sulfa tabs", "Sulpha", "Sulpha")]
    [InlineData("Co-trimoxazole", "Cotrimoxazole", "Cotrimoxazole")]
    [InlineData("Cotrimoxazole 960", "Co-trimoxazole", "Co-trimoxazole")]
    [InlineData("Ibuprofen", "NSAIDs", "NSAIDs")]
    [InlineData("Diclofenac sodium", "nsaid", "nsaid")]
    [InlineData("CEFTRIAXONE", "ceftriaxone", "ceftriaxone")] // case
    [InlineData("Metronidazole", "Metronidazole tablets", "Metronidazole tablets")] // allergen contains drug
    [InlineData("Ferrous sulfate", "Sulfa", null)] // a salt, not a sulfonamide
    [InlineData("Magnesium sulphate", "Sulpha", null)]
    [InlineData("Salbutamol sulfate", "Sulfonamides", null)]
    [InlineData("Nilstat", "Nil", null)] // "no allergies" entries are ignored
    [InlineData("Paracetamol", "None", null)]
    [InlineData("Paracetamol", "NKDA", null)]
    [InlineData("Amoxicillin", "No known drug allergies", null)]
    [InlineData("a", "Penicillin", null)] // a one-letter drug matches nothing
    [InlineData("ORS", "Horse serum", null)] // the reverse direction needs 3+ characters on the shorter side and real containment
    [InlineData("Paracetamol", "Penicillin", null)]
    [InlineData("Ibuprofen", "Penicillin", null)]
    // Fix round 2: plain containment for names of 5+ characters, mid-word and across hyphens (combination products).
    [InlineData("Hydroxychloroquine", "Chloroquine", "Chloroquine")]
    [InlineData("Chloroquine", "Hydroxychloroquine", "Hydroxychloroquine")]
    [InlineData("Esomeprazole", "Omeprazole", "Omeprazole")]
    [InlineData("Dihydrocodeine", "Codeine", "Codeine")]
    [InlineData("Levofloxacin", "Ofloxacin", "Ofloxacin")]
    [InlineData("Levocetirizine", "Cetirizine", "Cetirizine")]
    [InlineData("Artemether-lumefantrine", "Lumefantrine", "Lumefantrine")]
    [InlineData("Paracetamol-codeine", "Codeine", "Codeine")]
    [InlineData("Sulfadoxine-pyrimethamine", "Pyrimethamine", "Pyrimethamine")]
    [InlineData("Co-amoxiclav", "Penicillin", "Penicillin")]
    [InlineData("Amoxil", "Penicillin", "Penicillin")]
    // A no-allergy phrase is ignored only when it is the whole entry.
    [InlineData("Amoxicillin", "No known allergies except penicillin", "No known allergies except penicillin")]
    [InlineData("Amoxicillin", "No known allergies", null)]
    // Under 5 characters the shorter name must still start at a word; a hyphen starts a word.
    [InlineData("Codeine", "ORS-codeine mix", "ORS-codeine mix")]
    [InlineData("ORS", "Low-ORS", "Low-ORS")]
    public void Allergy_matching_rules(string drug, string allergy, string? expected)
    {
        var matched = PrescriptionService.AllergyMatches([drug], [allergy]);
        if (expected is null) Assert.Empty(matched);
        else Assert.Equal([expected], matched);
    }

    [Fact]
    public void Allergy_matches_are_distinct_and_in_recorded_order()
    {
        Assert.Equal(["Sulfa", "Penicillin"], PrescriptionService.AllergyMatches(
            ["Augmentin", "Septrin", "Amoxicillin"], ["Nil", "Sulfa", "Latex", "Penicillin"]));
        Assert.Empty(PrescriptionService.AllergyMatches(["Ferrous sulfate", "Paracetamol"], ["Sulfa", "  ", "Penicillin"]));
        // "Ss" or "a": nothing matches with fewer than 3 characters on the shorter side.
        Assert.Empty(PrescriptionService.AllergyMatches(["Zn"], ["Zinc"]));
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>I4 and M1: the signed link lasts 2 minutes, is bound to its test, and stops working the second after it expires.</summary>
    [Fact]
    public void Signed_link_expires_after_two_minutes()
    {
        var start = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
        var clock = new MovableClock(start);
        var signer = new CareFileSigner(new Saas.Application.Courses.MediaOptions { SigningKey = "unit-test-signing-key-0123456789abcdef" },
            new AppUrls("http://web.test", "http://api.test"), clock);
        Assert.Equal(TimeSpan.FromMinutes(2), CareFileSigner.Lifetime);
        var testId = Guid.NewGuid();
        var (url, expiresAt) = signer.ResultFileUrl(testId);
        Assert.Equal(start.AddMinutes(2), expiresAt);
        var parts = new Uri(url).AbsolutePath.Split('/');
        Assert.Equal(testId.ToString(), parts[^3]);
        var expires = long.Parse(parts[^2], System.Globalization.CultureInfo.InvariantCulture);
        var signature = parts[^1];
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), expires);

        Assert.True(signer.IsResultFileValid(testId, expires, signature));
        clock.Now = start.AddSeconds(119);
        Assert.True(signer.IsResultFileValid(testId, expires, signature));
        clock.Now = start.AddSeconds(120);
        Assert.True(signer.IsResultFileValid(testId, expires, signature)); // valid through its last second
        clock.Now = start.AddSeconds(121);
        Assert.False(signer.IsResultFileValid(testId, expires, signature));

        // Bound to the test and the expiry: another id or a stretched expiry fails.
        clock.Now = start;
        Assert.False(signer.IsResultFileValid(Guid.NewGuid(), expires, signature));
        Assert.False(signer.IsResultFileValid(testId, expires + 3600, signature));
        // A different key can't forge it.
        var other = new CareFileSigner(new Saas.Application.Courses.MediaOptions { SigningKey = "another-signing-key-0123456789abcdef" },
            new AppUrls("http://web.test", "http://api.test"), clock);
        Assert.False(other.IsResultFileValid(testId, expires, signature));
    }

    private sealed class CloudStorage : IFileStorage
    {
        public TimeSpan? Lifetime { get; private set; }
        public Task<string> SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.FromResult(key);
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public string? TemporaryUrl(string key, TimeSpan lifetime)
        {
            Lifetime = lifetime;
            return $"https://bucket.test/{key}";
        }
    }

    /// <summary>M1: with cloud storage the presigned redirect lives only for what is left of the signed link, never a fresh window.</summary>
    [Fact]
    public async Task Cloud_redirect_is_capped_by_the_link()
    {
        var clinic = await kit.ClinicAsync("Cloud Link Clinic");
        var patientId = Id(await kit.PatientAsync(clinic, "Cloud", "Link"));
        var testId = await api.WithDbAsync(async db =>
        {
            var test = new TestRequest
            {
                TenantId = clinic.TenantId, PatientId = patientId, RequestedBy = Guid.NewGuid(), Kind = TestKind.Lab, Description = "",
                Status = TestStatus.Resulted, ResultFileKey = $"private/care/{clinic.TenantId}/results/x.pdf", ResultFileType = "application/pdf",
            };
            db.TestRequests.Add(test);
            await db.SaveChangesAsync(Ct);
            return test.Id;
        });

        var start = DateTimeOffset.UtcNow;
        var clock = new MovableClock(start);
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var signer = ActivatorUtilities.CreateInstance<CareFileSigner>(sp, (TimeProvider)clock);
        var storage = new CloudStorage();
        var tests = ActivatorUtilities.CreateInstance<TestService>(sp, signer, (IFileStorage)storage, (TimeProvider)clock);
        var (url, _) = signer.ResultFileUrl(testId);
        var parts = new Uri(url).AbsolutePath.Split('/');
        var expires = long.Parse(parts[^2], System.Globalization.CultureInfo.InvariantCulture);

        // Fetched 90 seconds in: the redirect lives for the 30 seconds left, not another 2 minutes.
        clock.Now = DateTimeOffset.FromUnixTimeSeconds(expires).AddSeconds(-30);
        var file = await tests.FileByLinkAsync(testId, expires, parts[^1], Ct);
        Assert.NotNull(file?.RedirectUrl);
        Assert.NotNull(storage.Lifetime);
        Assert.True(storage.Lifetime <= TimeSpan.FromSeconds(30), $"{storage.Lifetime}");
        Assert.True(storage.Lifetime > TimeSpan.Zero);

        // Fetched straight away: never more than the 2-minute lifetime.
        clock.Now = start;
        await tests.FileByLinkAsync(testId, expires, parts[^1], Ct);
        Assert.True(storage.Lifetime <= TimeSpan.FromMinutes(2), $"{storage.Lifetime}");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>M2: ordering from a consultation needs a live care relationship, so the visit's doctor stops 30 days after the visit is Done.</summary>
    [Fact]
    public async Task Orders_stop_when_the_care_relationship_ends()
    {
        var w = await WorldAsync("Follow Up Orders Clinic");
        await Json(await w.Desk.PostAsJsonAsync($"/api/care/visits/{w.VisitId}/stage", new { stage = "Done" }, Ct));
        var doneAt = (await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().FirstAsync(v => v.Id == w.VisitId, Ct))).DoneAt!.Value;

        async Task<Exception?> OrderAtAsync(DateTimeOffset at, bool test)
        {
            using var scope = api.Services.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().SetTenant(w.Clinic.TenantId);
            ICurrentUser user = new StaffUser(w.DoctorId, MemberRole.Staff);
            var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
            TimeProvider clock = new FixedClock(at);
            try
            {
                if (test)
                    await ActivatorUtilities.CreateInstance<TestService>(sp, access, clock)
                        .RequestAsync(new CreateTestRequest(w.PatientId, w.ConsultationId, null, TestKind.Lab, "Repeat smear", null), Ct);
                else
                    await ActivatorUtilities.CreateInstance<PrescriptionService>(sp, access, user, clock)
                        .CreateAsync(new CreatePrescriptionRequest(w.PatientId, w.ConsultationId, null, false,
                            [new PrescriptionItemInput("Zinc", null, "1 tablet", DoseFrequency.OD, 10, null, null, null, null)]), Ct);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        // Day 29 after Done: the doctor's follow-up window is open.
        Assert.Null(await OrderAtAsync(doneAt.AddDays(29), test: false));
        Assert.Null(await OrderAtAsync(doneAt.AddDays(29), test: true));
        // Day 31: it has ended, so both orders are refused and nothing more is saved.
        foreach (var test in new[] { false, true })
        {
            var refused = await OrderAtAsync(doneAt.AddDays(31), test);
            var forbidden = Assert.IsType<ForbiddenException>(refused);
            Assert.Equal("Your care of this patient has ended. Ask the medical director.", forbidden.Message);
        }
        Assert.Equal(1, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.PatientId == w.PatientId, Ct)));
        Assert.Equal(1, await api.WithDbAsync(db => db.TestRequests.IgnoreQueryFilters().CountAsync(t => t.PatientId == w.PatientId, Ct)));
    }

    /// <summary>M2 exemption: the medical director, as the visit's doctor, still orders on day 31 (they oversee every patient).</summary>
    [Fact]
    public async Task Director_orders_after_the_follow_up_window()
    {
        var clinic = await kit.ClinicAsync("Director Orders Clinic");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        var patientId = Id(await kit.PatientAsync(clinic, "Director", "Patient"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 36.8, systolic = 118, diastolic = 76, pulse = 72, respRate = 14, spO2 = 98 },
            urgency = "Routine", doctorUserId = ownerId,
        }, Ct));
        var consultationId = Id(await Json(await clinic.Owner.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        await Json(await desk.PostAsJsonAsync($"/api/care/visits/{visitId}/stage", new { stage = "Done" }, Ct));
        var doneAt = (await api.WithDbAsync(db => db.Visits.IgnoreQueryFilters().FirstAsync(v => v.Id == visitId, Ct))).DoneAt!.Value;

        // The director's own visit assignment has ended by day 31, but the director needs none.
        Assert.False(await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters()
            .AnyAsync(a => a.PatientId == patientId && a.StaffUserId == ownerId && (a.EndedAt == null || a.EndedAt > doneAt.AddDays(31)), Ct)));

        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(clinic.TenantId);
        ICurrentUser user = new StaffUser(ownerId, MemberRole.Owner);
        var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
        TimeProvider clock = new FixedClock(doneAt.AddDays(31));
        var rx = await ActivatorUtilities.CreateInstance<PrescriptionService>(sp, access, user, clock)
            .CreateAsync(new CreatePrescriptionRequest(patientId, consultationId, null, false,
                [new PrescriptionItemInput("Zinc", null, "1 tablet", DoseFrequency.OD, 10, null, null, null, null)]), Ct);
        Assert.Equal(ownerId, rx.DoctorUserId);
        var test = await ActivatorUtilities.CreateInstance<TestService>(sp, access, clock)
            .RequestAsync(new CreateTestRequest(patientId, consultationId, null, TestKind.Lab, "Repeat smear", null), Ct);
        Assert.Equal(ownerId, test.RequestedBy);
    }
}
