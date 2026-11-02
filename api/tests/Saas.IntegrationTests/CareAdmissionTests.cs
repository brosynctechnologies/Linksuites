using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 8: wards and beds (plan limit), admissions, the conditional bed claim, deposits recorded as real payments with a receipt
/// number, the deposit minimum and the director's override, transfers, the admission care assignments, and the bed board's demographics.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareAdmissionTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string BedOccupied = "That bed is occupied.";
    private const string ReasonRequired = "Give a reason to open this patient's record.";

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

    private Task<Bed> BedRowAsync(Guid bedId) => api.WithDbAsync(db => db.Beds.IgnoreQueryFilters().FirstAsync(b => b.Id == bedId, Ct));

    private Task<List<CareAssignment>> AssignmentsAsync(Guid admissionId) =>
        api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.AdmissionId == admissionId).ToListAsync(Ct));

    // ---------- Beds and the plan ----------

    [Fact]
    public async Task Beds_above_plan_are_refused()
    {
        var clinic = await kit.ClinicAsync("Bed Plan Clinic"); // Free: beds 0
        var wardId = await kit.WardAsync(clinic, "Female ward");

        // Free has no beds: an active bed is 402, nothing is added.
        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync($"/api/care/wards/{wardId}/beds", new { label = "F1" }, Ct), HttpStatusCode.PaymentRequired);
        Assert.Equal(0, await api.WithDbAsync(db => db.Beds.IgnoreQueryFilters().CountAsync(b => b.TenantId == clinic.TenantId, Ct)));
        // An inactive bed doesn't count; making it active does.
        var inactive = Id(await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/wards/{wardId}/beds", new { label = "F1", active = false }, Ct)));
        await AssertProblemAsync(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}/beds/{inactive}", new { label = "F1", active = true }, Ct),
            HttpStatusCode.PaymentRequired);

        // On the Clinic plan (20 beds) the bed activates and counts in usage; the wards checklist item is done.
        await api.MakeProAsync(clinic.TenantId, "care", "clinic");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}/beds/{inactive}", new { label = "F1", active = true }, Ct));
        var billing = await Json(await clinic.Owner.GetAsync("/api/billing", Ct));
        var beds = billing.GetProperty("usage").GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == "beds");
        Assert.Equal(1, beds.GetProperty("used").GetInt64());
        Assert.Equal(20, beds.GetProperty("limit").GetInt64());

        // At the limit, the next active bed is 402.
        await api.WithDbAsync(db =>
        {
            for (var i = 2; i <= 20; i++) db.Beds.Add(new Bed { TenantId = clinic.TenantId, WardId = wardId, Label = $"F{i}" });
            return db.SaveChangesAsync(Ct);
        });
        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync($"/api/care/wards/{wardId}/beds", new { label = "F21" }, Ct), HttpStatusCode.PaymentRequired);

        // Only leadership sets up wards and beds.
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        await AssertProblemAsync(await desk.PostAsJsonAsync($"/api/care/wards/{wardId}/beds", new { label = "F22", active = false }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/wards", new { name = "Desk ward" }, Ct), HttpStatusCode.Forbidden);
        // A ward's daily charge must be a Bed price item.
        var drug = await kit.PriceItemAsync(clinic, "Not a bed", "Drug", 500);
        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync("/api/care/wards", new { name = "Odd ward", bedPriceItemId = drug }, Ct),
            HttpStatusCode.BadRequest, field: "bedPriceItemId");
    }

    // ---------- Bed claim ----------

    [Fact]
    public async Task Two_admissions_race_for_one_bed()
    {
        var clinic = await kit.ClinicAsync("Bed Race Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var wardId = await kit.WardAsync(clinic, "Male ward");
        var bedId = await kit.BedAsync(clinic, wardId, "M1");
        var first = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Race", "One")));
        var second = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Race", "Two")));

        var results = await Task.WhenAll(kit.AssignBedAsync(desk, first, bedId), kit.AssignBedAsync(clinic.Owner, second, bedId));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(results, r => r.StatusCode != HttpStatusCode.OK);
        await AssertProblemAsync(loser, HttpStatusCode.Conflict, BedOccupied);

        Assert.Equal(BedStatus.Occupied, (await BedRowAsync(bedId)).Status);
        var holders = await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.BedId == bedId).ToListAsync(Ct));
        Assert.Single(holders);
        Assert.Single(await api.WithDbAsync(db => db.BedMoves.IgnoreQueryFilters().Where(m => m.ToBedId == bedId).ToListAsync(Ct)));

        // The loser can still take another bed; an admission that has a bed must transfer instead.
        var other = await kit.BedAsync(clinic, wardId, "M2");
        var loserId = holders[0].Id == first ? second : first;
        await Json(await kit.AssignBedAsync(desk, loserId, other));
        await AssertProblemAsync(await kit.AssignBedAsync(desk, loserId, other), HttpStatusCode.Conflict, AdmissionService.HasBed);

        // A patient already admitted can't be admitted again.
        var patientId = holders[0].PatientId;
        await AssertProblemAsync(await clinic.Owner.PostAsJsonAsync("/api/care/admissions", new { patientId, reason = "Again", admittingDiagnosis = "Again" }, Ct),
            HttpStatusCode.Conflict, AdmissionService.AlreadyAdmitted);
        // Only doctors admit.
        await AssertProblemAsync(await desk.PostAsJsonAsync("/api/care/admissions",
            new { patientId = Id(await kit.PatientAsync(clinic, "Desk", "Admit")), reason = "x", admittingDiagnosis = "y" }, Ct), HttpStatusCode.Forbidden);
    }

    // ---------- Deposits ----------

    [Fact]
    public async Task Deposit_below_minimum_needs_director()
    {
        var clinic = await kit.ClinicAsync("Deposit Clinic");
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var wardId = await kit.WardAsync(clinic, "Private ward", minDepositMinor: 5_000_000);
        var bedId = await kit.BedAsync(clinic, wardId, "P1");
        var admissionId = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Deposit", "Low")));

        // Below the ward minimum: 409 with the amount, even when the desk or an admin asks for an override.
        const string tooLow = "A deposit of at least ₦50,000 is needed for this ward.";
        await AssertProblemAsync(await kit.AssignBedAsync(desk, admissionId, bedId, 2_000_000), HttpStatusCode.Conflict, tooLow);
        await AssertProblemAsync(await kit.AssignBedAsync(desk, admissionId, bedId, 2_000_000, overrideByOwner: true), HttpStatusCode.Conflict, tooLow);
        await AssertProblemAsync(await kit.AssignBedAsync(admin, admissionId, bedId, 2_000_000, overrideByOwner: true), HttpStatusCode.Conflict, tooLow);
        // The director without the override flag is held to the minimum too.
        await AssertProblemAsync(await kit.AssignBedAsync(clinic.Owner, admissionId, bedId, 0), HttpStatusCode.Conflict, tooLow);
        Assert.Equal(BedStatus.Free, (await BedRowAsync(bedId)).Status);
        Assert.Equal(0, await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == clinic.TenantId, Ct)));

        // The director overrides: the bed is taken, the deposit is a real payment on the admission bill, and the override is audited.
        var assigned = await Json(await kit.AssignBedAsync(clinic.Owner, admissionId, bedId, 2_000_000, overrideByOwner: true));
        var deposit = assigned.GetProperty("deposit");
        Assert.Equal(2_000_000, deposit.GetProperty("amountMinor").GetInt64());
        Assert.Matches(@"^RCP-\d{4}-00001$", deposit.GetProperty("receiptNumber").GetString());
        Assert.True(deposit.GetProperty("isDeposit").GetBoolean());
        var admission = await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == admissionId, Ct));
        Assert.Equal(ownerId, admission.DepositOverrideBy);
        Assert.Equal(bedId, admission.BedId);
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == clinic.TenantId && a.Action == "care.deposit_override").ToListAsync(Ct)));
        Assert.Equal(admissionId.ToString(), audit.EntityId);
        Assert.Equal(ownerId, audit.ActorId);
        Assert.Contains("5000000", audit.AfterJson);

        // The clinic default applies to a ward without its own minimum.
        (await clinic.Owner.PutAsJsonAsync("/api/care/settings/profile", new { name = "Deposit Clinic", minDepositMinor = 1_500_000 }, Ct)).EnsureSuccessStatusCode();
        var general = await kit.WardAsync(clinic, "General ward");
        var generalBed = await kit.BedAsync(clinic, general, "G1");
        var second = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Deposit", "Default")));
        await AssertProblemAsync(await kit.AssignBedAsync(desk, second, generalBed, 1_000_000), HttpStatusCode.Conflict,
            "A deposit of at least ₦15,000 is needed for this ward.");
        await Json(await kit.AssignBedAsync(desk, second, generalBed, 1_500_000));
        Assert.Null((await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == second, Ct))).DepositOverrideBy);
    }

    [Fact]
    public async Task Deposit_is_recorded_as_a_payment_with_a_receipt_number()
    {
        var clinic = await kit.ClinicAsync("Receipt Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (cashier, cashierId) = await kit.StaffAsync(clinic, "Cashier");
        var wardId = await kit.WardAsync(clinic, "Children's ward", minDepositMinor: 1_000_000);
        var bed1 = await kit.BedAsync(clinic, wardId, "C1");
        var bed2 = await kit.BedAsync(clinic, wardId, "C2");
        var patientId = Id(await kit.PatientAsync(clinic, "Receipt", "One"));
        var admissionId = await kit.AdmitAsync(doctor, patientId);

        // A deposit needs a desk method.
        await AssertProblemAsync(await cashier.PostAsJsonAsync($"/api/care/admissions/{admissionId}/bed",
            new { bedId = bed1, depositMinor = 2_000_000 }, Ct), HttpStatusCode.BadRequest, field: "method");
        await AssertProblemAsync(await cashier.PostAsJsonAsync($"/api/care/admissions/{admissionId}/bed",
            new { bedId = bed1, depositMinor = 2_000_000, method = "Online" }, Ct), HttpStatusCode.BadRequest, field: "method");

        // The cashier takes the deposit by POS with a reference.
        var assigned = await Json(await cashier.PostAsJsonAsync($"/api/care/admissions/{admissionId}/bed",
            new { bedId = bed1, depositMinor = 2_000_000, method = "Pos", reference = "POS-7781" }, Ct));
        var year = CareCalendar.Today(DateTimeOffset.UtcNow).Year;
        Assert.Equal($"RCP-{year}-00001", assigned.GetProperty("deposit").GetProperty("receiptNumber").GetString());
        var billId = assigned.GetProperty("admission").GetProperty("billId").GetGuid();

        var payment = Assert.Single(await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().Where(p => p.TenantId == clinic.TenantId).ToListAsync(Ct)));
        Assert.Equal(billId, payment.BillId);
        Assert.Equal(patientId, payment.PatientId);
        Assert.Equal(2_000_000, payment.AmountMinor);
        Assert.Equal(PayMethod.Pos, payment.Method);
        Assert.Equal("POS-7781", payment.Reference);
        Assert.True(payment.IsDeposit);
        Assert.Equal(cashierId, payment.RecordedByUserId);
        // A retry of the same bed and deposit (a double click, a lost response) records nothing more: exactly one payment.
        await AssertProblemAsync(await cashier.PostAsJsonAsync($"/api/care/admissions/{admissionId}/bed",
            new { bedId = bed1, depositMinor = 2_000_000, method = "Pos", reference = "POS-7781" }, Ct), HttpStatusCode.Conflict, AdmissionService.HasBed);
        Assert.Equal(1, await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().CountAsync(p => p.TenantId == clinic.TenantId, Ct)));
        // M4: an amount over the desk maximum says so.
        var tooBig = await cashier.PostAsJsonAsync($"/api/care/admissions/{admissionId}/bed",
            new { bedId = bed1, depositMinor = 10_000_000_001L, method = "Cash" }, Ct);
        Assert.Equal("Enter an amount up to ₦100,000,000.", await FieldErrorAsync(tooBig, "depositMinor"));
        var wardItem = (await Json(await clinic.Owner.GetAsync("/api/care/wards", Ct))).EnumerateArray().Single(w => Id(w) == wardId).GetProperty("bedPriceItemId").GetGuid();
        var tooHigh = await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}",
            new { name = "Children's ward", bedPriceItemId = wardItem, minDepositMinor = 10_000_000_001L }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooHigh.StatusCode);
        Assert.Equal("Enter a minimum deposit up to ₦100,000,000.", await FieldErrorAsync(tooHigh, "minDepositMinor"));
        var bill = await Json(await cashier.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Equal(admissionId, bill.GetProperty("admissionId").GetGuid());
        Assert.Equal(2_000_000, bill.GetProperty("paidMinor").GetInt64());

        // The next payment takes the next receipt number (the clinic's receipt counter), through PaymentService directly too.
        var second = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Receipt", "Two")));
        var next = await Json(await kit.AssignBedAsync(cashier, second, bed2, 1_000_000));
        Assert.Equal($"RCP-{year}-00002", next.GetProperty("deposit").GetProperty("receiptNumber").GetString());
        var desk = await api.AsTenantAsync<PaymentService, CarePaymentDto>(clinic.TenantId, s => s.RecordDeskAsync(billId, 500_000, PayMethod.Cash, null, Ct));
        Assert.Equal($"RCP-{year}-00003", desk.ReceiptNumber);
        Assert.False(desk.IsDeposit);
        var after = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().FirstAsync(b => b.Id == billId, Ct));
        Assert.Equal(2_500_000, after.PaidMinor);
    }

    // ---------- Transfer and assignments ----------

    [Fact]
    public async Task Transfer_moves_bed_and_frees_old()
    {
        var clinic = await kit.ClinicAsync("Transfer Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurseA, nurseAId) = await kit.StaffAsync(clinic, "Nurse");
        var (nurseB, nurseBId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var wardA = await kit.WardAsync(clinic, "Ward A", nurseUserIds: [nurseAId]);
        var wardB = await kit.WardAsync(clinic, "Ward B", nurseUserIds: [nurseBId]);
        var a1 = await kit.BedAsync(clinic, wardA, "A1");
        var b1 = await kit.BedAsync(clinic, wardB, "B1");
        var patientId = Id(await kit.PatientAsync(clinic, "Transfer", "Patient"));
        var admissionId = await kit.AdmitAsync(doctor, patientId);

        // Admitting gives the doctor an admitting-doctor assignment; the bed gives ward A's nurse a ward-nurse assignment.
        var doctorRow = Assert.Single(await AssignmentsAsync(admissionId));
        Assert.Equal(CareAssignmentKind.AdmittingDoctor, doctorRow.Kind);
        Assert.Equal(doctorId, doctorRow.StaffUserId);
        Assert.Null(doctorRow.EndedAt);
        await Json(await kit.AssignBedAsync(desk, admissionId, a1));
        var nurseRow = Assert.Single(await AssignmentsAsync(admissionId), a => a.Kind == CareAssignmentKind.WardNurse);
        Assert.Equal(nurseAId, nurseRow.StaffUserId);
        Assert.Equal(wardA, nurseRow.WardId);
        await Json(await nurseA.GetAsync($"/api/care/patients/{patientId}", Ct));
        await AssertProblemAsync(await nurseB.GetAsync($"/api/care/patients/{patientId}", Ct), HttpStatusCode.Conflict, ReasonRequired);

        // Transfer: the cashier can't; the desk moves the patient to ward B.
        await AssertProblemAsync(await cashier.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer", new { bedId = b1 }, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await desk.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer", new { bedId = a1 }, Ct), HttpStatusCode.BadRequest, field: "bedId");
        var moved = await Json(await desk.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer", new { bedId = b1 }, Ct));
        Assert.Equal(b1, moved.GetProperty("bedId").GetGuid());
        Assert.Equal("Ward B", moved.GetProperty("wardName").GetString());
        Assert.Equal(BedStatus.Cleaning, (await BedRowAsync(a1)).Status);
        Assert.Equal(BedStatus.Occupied, (await BedRowAsync(b1)).Status);
        var moves = await api.WithDbAsync(db => db.BedMoves.IgnoreQueryFilters().Where(m => m.AdmissionId == admissionId).OrderBy(m => m.At).ToListAsync(Ct));
        Assert.Equal(2, moves.Count);
        Assert.Null(moves[0].FromBedId);
        Assert.Equal(a1, moves[1].FromBedId);
        Assert.Equal(b1, moves[1].ToBedId);

        // The ward-nurse assignment moved: ward A's nurse's row ended, ward B's nurse has one on ward B; the doctor's is untouched.
        var rows = await AssignmentsAsync(admissionId);
        Assert.NotNull(Assert.Single(rows, r => r.StaffUserId == nurseAId).EndedAt);
        var bRow = Assert.Single(rows, r => r.StaffUserId == nurseBId);
        Assert.Null(bRow.EndedAt);
        Assert.Equal(wardB, bRow.WardId);
        Assert.Null(Assert.Single(rows, r => r.Kind == CareAssignmentKind.AdmittingDoctor).EndedAt);
        await Json(await nurseB.GetAsync($"/api/care/patients/{patientId}", Ct));
        await AssertProblemAsync(await nurseA.GetAsync($"/api/care/patients/{patientId}", Ct), HttpStatusCode.Conflict, ReasonRequired);

        // The old bed can't be claimed while it's being cleaned; marking it ready frees it.
        var other = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Next", "Patient")));
        await AssertProblemAsync(await kit.AssignBedAsync(desk, other, a1), HttpStatusCode.Conflict, AdmissionService.BedCleaning);
        await AssertProblemAsync(await cashier.PostAsync($"/api/care/beds/{a1}/ready", null, Ct), HttpStatusCode.Forbidden);
        await Json(await desk.PostAsync($"/api/care/beds/{a1}/ready", null, Ct));
        await Json(await kit.AssignBedAsync(desk, other, a1));
        // Transferring into an occupied bed is 409.
        await AssertProblemAsync(await desk.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer", new { bedId = a1 }, Ct), HttpStatusCode.Conflict, BedOccupied);
    }

    [Fact]
    public async Task Ward_without_a_nurse_list_assigns_every_nurse_and_list_changes_follow()
    {
        var clinic = await kit.ClinicAsync("All Nurses Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (_, nurse1) = await kit.StaffAsync(clinic, "Nurse");
        var (_, nurse2) = await kit.StaffAsync(clinic, "Nurse");
        var wardId = await kit.WardAsync(clinic, "Open ward");
        var bedId = await kit.BedAsync(clinic, wardId, "O1");
        var admissionId = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Open", "Ward")));
        await Json(await kit.AssignBedAsync(clinic.Owner, admissionId, bedId));

        var nurses = (await AssignmentsAsync(admissionId)).Where(a => a.Kind == CareAssignmentKind.WardNurse).ToList();
        Assert.Equal(new[] { nurse1, nurse2 }.Order(), nurses.Select(n => n.StaffUserId).Order());
        Assert.All(nurses, n => Assert.Equal(wardId, n.WardId));

        // Naming the ward's nurses ends the other nurse's assignment for the patients in it.
        var ward = await Json(await clinic.Owner.GetAsync("/api/care/wards", Ct));
        var item = ward.EnumerateArray().Single(w => Id(w) == wardId).GetProperty("bedPriceItemId").GetGuid();
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}", new { name = "Open ward", kind = "General", bedPriceItemId = item, nurseUserIds = new[] { nurse1 } }, Ct));
        var rows = await AssignmentsAsync(admissionId);
        Assert.Null(Assert.Single(rows, r => r.StaffUserId == nurse1).EndedAt);
        Assert.NotNull(Assert.Single(rows, r => r.StaffUserId == nurse2).EndedAt);
        // A ward's nurse list holds nurses only.
        await AssertProblemAsync(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}",
            new { name = "Open ward", bedPriceItemId = item, nurseUserIds = new[] { Guid.NewGuid() } }, Ct), HttpStatusCode.BadRequest, field: "nurseUserIds");
    }

    // ---------- Reads ----------

    [Fact]
    public async Task Bed_board_and_ward_lists_show_no_clinical_fields()
    {
        var clinic = await kit.ClinicAsync("Bed Board Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var wardId = await kit.WardAsync(clinic, "Board ward");
        var bedId = await kit.BedAsync(clinic, wardId, "W1");
        await kit.BedAsync(clinic, wardId, "W2");
        var placed = Id(await kit.PatientAsync(clinic, "Board", "Placed"));
        var waiting = Id(await kit.PatientAsync(clinic, "Board", "Waiting"));
        var placedAdmission = await kit.AdmitAsync(doctor, placed, "Boardreason vomiting blood", "Boarddiagnosis ulcer");
        var waitingAdmission = await kit.AdmitAsync(doctor, waiting, "Boardreason fever", "Boarddiagnosis typhoid");
        await Json(await kit.AssignBedAsync(desk, placedAdmission, bedId));

        foreach (var client in new[] { desk, cashier, doctor })
        {
            var board = await (await client.GetAsync("/api/care/bed-board", Ct)).Content.ReadAsStringAsync(Ct);
            var patients = await (await client.GetAsync($"/api/care/wards/{wardId}/patients", Ct)).Content.ReadAsStringAsync(Ct);
            foreach (var body in new[] { board, patients })
            {
                Assert.DoesNotContain("Boardreason", body);
                Assert.DoesNotContain("Boarddiagnosis", body);
                Assert.DoesNotContain("\"reason\"", body);
                Assert.DoesNotContain("iagnosis", body);
            }
            var parsed = JsonDocument.Parse(board).RootElement;
            var beds = parsed.GetProperty("wards").EnumerateArray().Single(w => Id(w) == wardId).GetProperty("beds").EnumerateArray().ToList();
            Assert.Equal(["W1", "W2"], beds.Select(b => b.GetProperty("label").GetString()));
            Assert.Equal("Occupied", beds[0].GetProperty("status").GetString());
            Assert.Equal("Board Placed", beds[0].GetProperty("patient").GetProperty("patientName").GetString());
            Assert.Equal(JsonValueKind.Null, beds[1].GetProperty("patient").ValueKind);
            Assert.Equal(waitingAdmission, Id(Assert.Single(parsed.GetProperty("awaitingBed").EnumerateArray())));
            var list = JsonDocument.Parse(patients).RootElement;
            Assert.Equal(placedAdmission, Id(Assert.Single(list.EnumerateArray())));
        }

        // The admission itself, with its reason and diagnosis, is a clinical read: 403 for non-clinical staff, logged for the doctor.
        foreach (var client in new[] { desk, cashier })
            await AssertProblemAsync(await client.GetAsync($"/api/care/admissions/{placedAdmission}", Ct), HttpStatusCode.Forbidden,
                "Only doctors and nurses can see clinical notes.");
        var before = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == placed, Ct));
        var read = await Json(await doctor.GetAsync($"/api/care/admissions/{placedAdmission}", Ct));
        Assert.Equal("Boardreason vomiting blood", read.GetProperty("reason").GetString());
        Assert.Equal("Boarddiagnosis ulcer", read.GetProperty("admittingDiagnosis").GetString());
        var log = await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == placed).OrderBy(a => a.At).ToListAsync(Ct));
        Assert.Equal(before + 1, log.Count);
        Assert.Equal("admission", log[^1].Area);
        Assert.Equal(doctorId, log[^1].StaffUserId);

        // Reason and diagnosis are encrypted at rest.
        var raw = await api.WithDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT reason || ' ' || admitting_diagnosis AS \"Value\" FROM admissions WHERE id = {placedAdmission}").FirstAsync(Ct));
        Assert.DoesNotContain("Boardreason", raw);
        Assert.DoesNotContain("Boarddiagnosis", raw);
        var text = api.Services.GetRequiredService<ClinicalText>();
        var stored = await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == placedAdmission, Ct));
        Assert.Equal("Boardreason vomiting blood", text.Read(stored.Reason));

        // The record timeline lists the admission with its clinical fields.
        var record = await Json(await doctor.GetAsync($"/api/care/patients/{placed}/record", Ct));
        var admission = Assert.Single(record.GetProperty("admissions").EnumerateArray());
        Assert.Equal("Boarddiagnosis ulcer", admission.GetProperty("admittingDiagnosis").GetString());
        Assert.Equal("W1", admission.GetProperty("bedLabel").GetString());
    }

    // ---------- Fix round 1 ----------

    private static async Task<string?> FieldErrorAsync(HttpResponseMessage r, string field)
    {
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var error = body.GetProperty("errors").GetProperty(field);
        return error.ValueKind == JsonValueKind.Array ? error[0].GetString() : error.GetString();
    }

    private const string BedHasPatient = "This bed has a patient in it.";

    /// <summary>
    /// Holds a bed claim's row lock (Free to Occupied, not yet committed) while <paramref name="request"/> runs, waits until that request
    /// is blocked on the row, then commits the claim and returns the request's answer.
    /// </summary>
    private async Task<HttpResponseMessage> WithHeldClaimAsync(Guid bedId, Func<Task<HttpResponseMessage>> request)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Saas.Infrastructure.Persistence.AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"UPDATE beds SET status = 'Occupied' WHERE id = {bedId} AND status = 'Free' AND active", Ct));
        var pending = request();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var blocked = false;
        while (!pending.IsCompleted && DateTime.UtcNow < deadline)
        {
            blocked = await api.WithDbAsync(d => d.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query ILIKE '%beds%'").FirstAsync(Ct)) > 0;
            if (blocked) break;
            await Task.Delay(50, Ct);
        }
        await tx.CommitAsync(Ct);
        var response = await pending;
        Assert.True(blocked, "The request didn't wait on the held row lock.");
        return response;
    }

    /// <summary>I1: an occupied bed is never made inactive or deleted, whether the claim landed before or while the change ran.</summary>
    [Fact]
    public async Task Occupied_bed_is_never_deactivated_or_deleted()
    {
        var clinic = await kit.ClinicAsync("Bed Lock Clinic");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var wardId = await kit.WardAsync(clinic, "Lock ward");

        // Back to back: the claimed bed refuses both.
        var placed = await kit.BedAsync(clinic, wardId, "L1");
        var admission = await kit.AdmitAsync(doctor, Id(await kit.PatientAsync(clinic, "Lock", "One")));
        await Json(await kit.AssignBedAsync(clinic.Owner, admission, placed));
        await AssertProblemAsync(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}/beds/{placed}", new { label = "L1", active = false }, Ct),
            HttpStatusCode.Conflict, BedHasPatient);
        await AssertProblemAsync(await clinic.Owner.DeleteAsync($"/api/care/wards/{wardId}/beds/{placed}", Ct), HttpStatusCode.Conflict, BedHasPatient);

        // A claim holding the row lock while the change runs: the change waits for it, sees the patient, and refuses.
        var held = await kit.BedAsync(clinic, wardId, "L2");
        await AssertProblemAsync(await WithHeldClaimAsync(held, () =>
            clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}/beds/{held}", new { label = "L2", active = false }, Ct)), HttpStatusCode.Conflict, BedHasPatient);
        var row = await BedRowAsync(held);
        Assert.True(row.Active);
        Assert.Equal(BedStatus.Occupied, row.Status);

        var heldForDelete = await kit.BedAsync(clinic, wardId, "L3");
        await AssertProblemAsync(await WithHeldClaimAsync(heldForDelete, () =>
            clinic.Owner.DeleteAsync($"/api/care/wards/{wardId}/beds/{heldForDelete}", Ct)), HttpStatusCode.Conflict, BedHasPatient);
        Assert.Equal(BedStatus.Occupied, (await BedRowAsync(heldForDelete)).Status);

        // A free bed still deactivates and deletes.
        var free = await kit.BedAsync(clinic, wardId, "L4");
        Assert.False((await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/wards/{wardId}/beds/{free}", new { label = "L4", active = false }, Ct)))
            .GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await clinic.Owner.DeleteAsync($"/api/care/wards/{wardId}/beds/{free}", Ct)).StatusCode);
    }

    /// <summary>
    /// Task 8 ruling (as carried in Task 9): a doctor with a live care relationship (here, from their own live consultation) admits without
    /// a reason; a doctor without one goes through emergency access (X-Care-Reason required, an emergency access-log row, the director
    /// told), even when quoting another doctor's consultation. The director is exempt.
    /// </summary>
    [Fact]
    public async Task Admitting_needs_a_live_relationship_or_emergency_reason()
    {
        var clinic = await kit.ClinicAsync("Admit Rule Clinic");
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (stranger, strangerId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");

        async Task<(Guid Patient, Guid Consultation)> SeenByDoctorAsync(string first)
        {
            var patientId = Id(await kit.PatientAsync(clinic, first, "Admit"));
            var visitId = Id(await kit.CheckInAsync(desk, patientId));
            await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
            {
                vitals = new { temperatureC = 38.0, pulse = 96 }, urgency = "Routine", doctorUserId = doctorId,
            }, Ct));
            return (patientId, Id(await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct))));
        }
        Task<int> EmergencyRowsAsync(Guid patientId) =>
            api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId && a.Emergency, Ct));
        Task<int> NoticesAsync() =>
            api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.UserId == ownerId && n.TenantId == clinic.TenantId && n.Kind == "care.emergency_access", Ct));

        // Own consultation: no reason needed, nothing emergency.
        var (own, ownConsultation) = await SeenByDoctorAsync("Own");
        await kit.AdmitAsync(doctor, own, consultationId: ownConsultation, emergencyReason: null);
        Assert.Equal(0, await EmergencyRowsAsync(own));

        // Another doctor's patient (even quoting that doctor's consultation), or no consultation: 409 without a reason, nothing written.
        var (theirs, theirConsultation) = await SeenByDoctorAsync("Theirs");
        foreach (var consultationId in new Guid?[] { theirConsultation, null })
        {
            var refused = await stranger.PostAsJsonAsync("/api/care/admissions",
                new { patientId = theirs, consultationId, reason = "Needs a bed", admittingDiagnosis = "Pneumonia" }, Ct);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("emergency_reason_required", (await refused.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("code").GetString());
        }
        Assert.Equal(0, await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().CountAsync(a => a.PatientId == theirs, Ct)));
        Assert.Equal(0, await NoticesAsync());

        // With a reason: admitted through emergency access, logged as an emergency with the reason, and the director is told.
        var admissionId = await kit.AdmitAsync(stranger, theirs, consultationId: theirConsultation, emergencyReason: "Covering the night shift");
        var row = Assert.Single(await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters()
            .Where(a => a.PatientId == theirs && a.StaffUserId == strangerId).ToListAsync(Ct)));
        Assert.True(row.Emergency);
        Assert.Equal("admission", row.Area);
        Assert.Equal("Covering the night shift", api.Services.GetRequiredService<ClinicalText>().Read(row.Reason));
        Assert.Equal(1, await NoticesAsync());
        Assert.Equal(strangerId, (await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == admissionId, Ct))).AdmittingDoctorUserId);

        // The director admits anyone without a reason or a notice.
        var anyone = Id(await kit.PatientAsync(clinic, "Director", "Admit"));
        await kit.AdmitAsync(clinic.Owner, anyone, emergencyReason: null);
        Assert.Equal(0, await EmergencyRowsAsync(anyone));
        Assert.Equal(1, await NoticesAsync());
    }

    /// <summary>
    /// Ruling (Task 8, carried to Task 9): admitting is free when the caller has a live care relationship with the patient, from any
    /// source; no consultation needs quoting, and nothing is logged as an emergency or noticed.
    /// </summary>
    [Fact]
    public async Task Admitting_is_free_with_a_live_care_relationship()
    {
        var clinic = await kit.ClinicAsync("Admit Live Clinic");
        var ownerId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = Id(await kit.PatientAsync(clinic, "Live", "Relationship"));
        await kit.AssignAsync(clinic, patientId, doctorId); // e.g. the doctor saw the patient in clinic last week

        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        Assert.Equal(doctorId, (await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().FirstAsync(a => a.Id == admissionId, Ct))).AdmittingDoctorUserId);
        Assert.Empty(await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).ToListAsync(Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.UserId == ownerId && n.TenantId == clinic.TenantId && n.Kind == "care.emergency_access", Ct)));
        Assert.Contains(await AssignmentsAsync(admissionId), a => a.Kind == CareAssignmentKind.AdmittingDoctor && a.StaffUserId == doctorId);
    }

    /// <summary>
    /// Ruling (Task 8, carried to Task 9): the doctor's own consultation no longer counts by itself. Once that visit's care relationship
    /// has ended (30 days after the visit), admitting from that old consultation needs a reason and goes through emergency access.
    /// </summary>
    [Fact]
    public async Task Admitting_from_a_stale_consultation_needs_a_reason()
    {
        var clinic = await kit.ClinicAsync("Admit Stale Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Stale", "Consultation"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await nurse.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 37.5, pulse = 80 }, urgency = "Routine", doctorUserId = doctorId,
        }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        // The visit was long ago: every assignment the doctor had for the patient has ended.
        await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.PatientId == patientId && a.StaffUserId == doctorId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndedAt, DateTimeOffset.UtcNow.AddDays(-1)), Ct));

        var refused = await doctor.PostAsJsonAsync("/api/care/admissions",
            new { patientId, consultationId, reason = "Readmission", admittingDiagnosis = "Pneumonia" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("emergency_reason_required", (await refused.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("code").GetString());
        Assert.Equal(0, await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId, Ct)));

        await kit.AdmitAsync(doctor, patientId, consultationId: consultationId, emergencyReason: "Came back through casualty");
        var row = Assert.Single(await api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters()
            .Where(a => a.PatientId == patientId && a.StaffUserId == doctorId && a.Emergency).ToListAsync(Ct)));
        Assert.Equal("admission", row.Area);
    }

    /// <summary>Ruling: admitting on a plan without beds is 402 with the upgrade sentence.</summary>
    [Fact]
    public async Task Admitting_needs_a_plan_with_beds()
    {
        var clinic = await kit.ClinicAsync("No Beds Clinic"); // Free: beds 0
        var patientId = Id(await kit.PatientAsync(clinic, "No", "Beds"));
        var refused = await clinic.Owner.PostAsJsonAsync("/api/care/admissions", new { patientId, reason = "Needs a bed", admittingDiagnosis = "Dehydration" }, Ct);
        await AssertProblemAsync(refused, HttpStatusCode.PaymentRequired, "Your plan doesn't include beds. Upgrade to admit patients.");
        Assert.Equal(0, await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().CountAsync(a => a.PatientId == patientId, Ct)));
        await api.MakeProAsync(clinic.TenantId, "care", "clinic");
        await kit.AdmitAsync(clinic.Owner, patientId);
    }

    /// <summary>
    /// Ruling: at discharge (Task 10 calls the helper), ward-nurse assignments end at the discharge time and the admitting doctor's ends
    /// 30 days later; rows that already ended keep their end.
    /// </summary>
    [Fact]
    public async Task Discharge_ends_ward_nurses_now_and_the_admitting_doctor_after_30_days()
    {
        var clinic = await kit.ClinicAsync("Discharge Helper Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (_, nurseAId) = await kit.StaffAsync(clinic, "Nurse");
        var (nurseB, nurseBId) = await kit.StaffAsync(clinic, "Nurse");
        var wardA = await kit.WardAsync(clinic, "Helper A", nurseUserIds: [nurseAId]);
        var wardB = await kit.WardAsync(clinic, "Helper B", nurseUserIds: [nurseBId]);
        var a1 = await kit.BedAsync(clinic, wardA, "HA1");
        var b1 = await kit.BedAsync(clinic, wardB, "HB1");
        var patientId = Id(await kit.PatientAsync(clinic, "Helper", "Patient"));
        var admissionId = await kit.AdmitAsync(doctor, patientId);
        await Json(await kit.AssignBedAsync(clinic.Owner, admissionId, a1));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{admissionId}/transfer", new { bedId = b1 }, Ct));
        var endedEarlier = (await AssignmentsAsync(admissionId)).Single(a => a.StaffUserId == nurseAId).EndedAt;
        Assert.NotNull(endedEarlier);
        await Json(await nurseB.GetAsync($"/api/care/patients/{patientId}", Ct));

        var dischargedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await api.AsTenantAsync<Saas.Application.Common.IAppDbContext, int>(clinic.TenantId, async db =>
        {
            await AdmissionService.EndAssignmentsAtDischargeAsync(db, admissionId, dischargedAt, Ct);
            return await db.SaveChangesAsync(Ct);
        });

        var rows = await AssignmentsAsync(admissionId);
        Assert.Equal(dischargedAt, rows.Single(a => a.StaffUserId == nurseBId).EndedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(dischargedAt.AddDays(30), rows.Single(a => a.Kind == CareAssignmentKind.AdmittingDoctor && a.StaffUserId == doctorId).EndedAt!.Value,
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(endedEarlier, rows.Single(a => a.StaffUserId == nurseAId).EndedAt);
        // The ward nurse needs a reason from now on; the doctor's follow-up window keeps the record open without one.
        await AssertProblemAsync(await nurseB.GetAsync($"/api/care/patients/{patientId}", Ct), HttpStatusCode.Conflict, ReasonRequired);
        await Json(await doctor.GetAsync($"/api/care/patients/{patientId}", Ct));
    }
}
