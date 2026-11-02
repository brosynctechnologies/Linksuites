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
/// CareLink Task 10: discharge. The discharge summary (written and signed by the admission's doctor, encrypted, read through
/// RecordGuard), the discharge rules (home and transferred out need a signed summary, against advice a reason, died a time and cause and
/// the patient becomes Deceased), what discharge does (bed to Cleaning, bill final with the balance left open, assignments ended, future
/// doses stopped), the unsigned ward-round warning, the discharge PDF, the admissions list, the record timeline's ward care, and an
/// admission prescription racing a discharge.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareDischargeTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";
    private const string NotOnWard = "This patient isn't on your ward.";
    private const string Discharged = "This patient has been discharged.";
    private const string SignedNote = "This note is signed. Add an addendum instead.";
    private const string AuthorOnly = "Only the doctor who wrote this note can change it.";
    private const string SummaryUnsigned = "Sign the discharge summary before discharging the patient.";
    private const string UnsignedRounds = "Sign or discard the unsigned ward-round notes first.";
    private const string DoctorsOnly = "Only doctors can discharge patients.";
    private const string SummaryDoctorsOnly = "Only doctors can write discharge summaries.";
    private const string EmptySummary = "Write the diagnoses and the condition at discharge before signing.";
    private const string BillFinal = "This bill is final. No new charges can be added.";

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

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private ClinicalText Text => api.Services.GetRequiredService<ClinicalText>();

    private Task<string> RawRowsAsync(string table, string column, Guid id) => api.WithDbAsync(db => db.Database
        .SqlQueryRaw<string>($"SELECT coalesce(string_agg(row_to_json(t)::text, ' '), '') AS \"Value\" FROM {table} t WHERE {column} = {{0}}", id)
        .FirstAsync(Ct));

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    private Task<Admission> AdmissionAsync(Guid id) => api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == id, Ct));

    /// <summary>A clinic with a doctor who admitted the patient, the ward's nurse (bed assigned), the front desk and the cashier.</summary>
    private sealed record World(
        CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Nurse, Guid NurseId, HttpClient Desk, HttpClient Cashier,
        Guid PatientId, Guid AdmissionId, Guid WardId, Guid BedId);

    private async Task<World> WorldAsync(string name, string first = "Discharge", string last = "Patient", bool bed = true)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var wardId = await kit.WardAsync(clinic, $"{name} ward", nurseUserIds: [nurseId]);
        var bedId = await kit.BedAsync(clinic, wardId, "D1");
        var patientId = Id(await kit.PatientAsync(clinic, first, last));
        await kit.AssignAsync(clinic, patientId, doctorId); // a live relationship: admitting needs no reason
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        if (bed) await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        return new World(clinic, doctor, doctorId, nurse, nurseId, desk, cashier, patientId, admissionId, wardId, bedId);
    }

    /// <summary>Another patient admitted (and placed) in the same world by its doctor; returns the admission.</summary>
    private async Task<(Guid PatientId, Guid AdmissionId)> AdmitAnotherAsync(World w, string first, Guid? bedId = null)
    {
        var patientId = Id(await kit.PatientAsync(w.Clinic, first, "Other"));
        await kit.AssignAsync(w.Clinic, patientId, w.DoctorId);
        var admissionId = await kit.AdmitAsync(w.Doctor, patientId, emergencyReason: null);
        if (bedId is { } bed) await Json(await kit.AssignBedAsync(w.Desk, admissionId, bed));
        return (patientId, admissionId);
    }

    private static string SummaryPath(Guid admissionId) => $"/api/care/admissions/{admissionId}/discharge-summary";

    private static object FullSummary(string diagnoses = "Severe malaria, resolved", string condition = "Stable, afebrile") => new
    {
        diagnoses, treatment = "IV artesunate then oral ACT", condition, takeHome = "Complete the ACT course; return if fever recurs",
        followUp = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(7),
    };

    private async Task SignedSummaryAsync(HttpClient doctor, Guid admissionId)
    {
        await Json(await doctor.PutAsJsonAsync(SummaryPath(admissionId), FullSummary(), Ct));
        await Json(await doctor.PostAsync($"{SummaryPath(admissionId)}/sign", null, Ct));
    }

    private static Task<HttpResponseMessage> DischargeAsync(HttpClient client, Guid admissionId, object body) =>
        client.PostAsJsonAsync($"/api/care/admissions/{admissionId}/discharge", body, Ct);

    private static object Item(string drug, string frequency, int? durationDays = null) =>
        new { drug, dose = "1 tablet", frequency, durationDays, route = "Oral" };

    // ---------- The brief's tests ----------

    [Fact]
    public async Task Home_discharge_needs_signed_summary()
    {
        var w = await WorldAsync("Home Clinic");
        var (stranger, _) = await kit.StaffAsync(w.Clinic, "Doctor");
        var path = SummaryPath(w.AdmissionId);

        // No summary yet: a home or transferred-out discharge is 409 and nothing changes.
        foreach (var type in new[] { "Home", "TransferredOut" })
            await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type }), HttpStatusCode.Conflict, SummaryUnsigned, code: "summary_unsigned");
        Assert.Equal(AdmissionStatus.Admitted, (await AdmissionAsync(w.AdmissionId)).Status);

        // Who writes the summary: not the front desk (clinical), not a nurse (doctors only), not a doctor off the care team.
        await AssertProblemAsync(await w.Desk.PutAsJsonAsync(path, FullSummary(), Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await w.Nurse.PutAsJsonAsync(path, FullSummary(), Ct), HttpStatusCode.Forbidden, SummaryDoctorsOnly);
        await AssertProblemAsync(await stranger.PutAsJsonAsync(path, FullSummary(), Ct), HttpStatusCode.Forbidden, NotOnWard);
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync(path, new { diagnoses = new string('x', 2_001) }, Ct), HttpStatusCode.BadRequest, field: "diagnoses");
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync(path, new { diagnoses = "x", followUp = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1) }, Ct),
            HttpStatusCode.BadRequest, field: "followUp");
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync($"/api/care/admissions/{Guid.NewGuid()}/discharge-summary", FullSummary(), Ct), HttpStatusCode.NotFound);

        // The admitting doctor drafts it (a write: no access-log row); the text is encrypted at rest.
        var logBefore = (await AccessLogAsync(w.PatientId)).Count;
        var draft = await Json(await w.Doctor.PutAsJsonAsync(path, new { diagnoses = "Severe malaria, resolved", treatment = "IV artesunate then oral ACT" }, Ct));
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(w.DoctorId, draft.GetProperty("doctorUserId").GetGuid());
        Assert.Equal(logBefore, (await AccessLogAsync(w.PatientId)).Count);
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Conflict, SummaryUnsigned);

        // A blank condition can't be signed; another doctor can't touch the author's draft.
        await AssertProblemAsync(await w.Doctor.PostAsync($"{path}/sign", null, Ct), HttpStatusCode.Conflict, EmptySummary, code: "note_empty");
        await AssertProblemAsync(await w.Clinic.Owner.PutAsJsonAsync(path, FullSummary(), Ct), HttpStatusCode.Forbidden, AuthorOnly);
        await Json(await w.Doctor.PutAsJsonAsync(path, FullSummary(), Ct));
        var raw = await RawRowsAsync("discharge_summaries", "admission_id", w.AdmissionId);
        foreach (var plain in new[] { "malaria", "artesunate", "afebrile", "ACT course" }) Assert.DoesNotContain(plain, raw);
        var row = await api.WithDbAsync(db => db.DischargeSummaries.IgnoreQueryFilters().FirstAsync(s => s.AdmissionId == w.AdmissionId, Ct));
        Assert.Equal("Stable, afebrile", Text.Read(row.Condition));
        Assert.Equal("Complete the ACT course; return if fever recurs", Text.Read(row.TakeHome));

        var signed = await Json(await w.Doctor.PostAsync($"{path}/sign", null, Ct));
        Assert.Equal("Signed", signed.GetProperty("status").GetString());
        Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.note_signed" && a.EntityType == "DischargeSummary").ToListAsync(Ct)));
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync(path, FullSummary("Changed"), Ct), HttpStatusCode.Conflict, SignedNote);

        // Reading it is a clinical open (area discharge); the front desk is refused and nothing is logged for them.
        await AssertProblemAsync(await w.Desk.GetAsync(path, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        var read = await Json(await w.Nurse.GetAsync(path, Ct));
        Assert.Equal("Severe malaria, resolved", read.GetProperty("diagnoses").GetString());
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.Area == "discharge" && a.StaffUserId == w.NurseId);

        // Who discharges: doctors on the care team (and the director).
        await AssertProblemAsync(await DischargeAsync(w.Desk, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await DischargeAsync(w.Nurse, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Forbidden, DoctorsOnly);
        await AssertProblemAsync(await DischargeAsync(stranger, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Forbidden, NotOnWard);
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { }), HttpStatusCode.BadRequest, field: "type");

        var discharged = await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }));
        Assert.Equal("Home", discharged.GetProperty("type").GetString());
        Assert.Equal("Discharged", discharged.GetProperty("admission").GetProperty("status").GetString());
        var admission = await AdmissionAsync(w.AdmissionId);
        Assert.Equal(AdmissionStatus.Discharged, admission.Status);
        Assert.Equal(DischargeType.Home, admission.DischargeType);
        Assert.Equal(w.DoctorId, admission.DischargedByUserId);
        Assert.NotNull(admission.DischargedAt);
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Conflict, Discharged, code: "admission_discharged");

        // A signed summary takes addenda, after discharge too (area discharge); the summary read shows them.
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/notes/discharge/{Id(signed)}/addenda", new { text = "Clinic review booked" }, Ct));
        var withAddendum = await Json(await w.Doctor.GetAsync(path, Ct));
        Assert.Equal("Clinic review booked", withAddendum.GetProperty("addenda")[0].GetProperty("text").GetString());

        // Against advice needs the reason, stored encrypted; transferred out also needs the signed summary.
        var (_, amaAdmission) = await AdmitAnotherAsync(w, "Ama");
        await AssertProblemAsync(await DischargeAsync(w.Doctor, amaAdmission, new { type = "AgainstAdvice" }), HttpStatusCode.BadRequest, field: "amaReason");
        await Json(await DischargeAsync(w.Doctor, amaAdmission, new { type = "AgainstAdvice", amaReason = "Family wants traditional care" }));
        var ama = await AdmissionAsync(amaAdmission);
        Assert.Equal(DischargeType.AgainstAdvice, ama.DischargeType);
        Assert.DoesNotContain("traditional", ama.AmaReason);
        Assert.Equal("Family wants traditional care", Text.Read(ama.AmaReason));
        var amaRead = await Json(await w.Doctor.GetAsync($"/api/care/admissions/{amaAdmission}", Ct));
        Assert.Equal("Family wants traditional care", amaRead.GetProperty("amaReason").GetString());
    }

    [Fact]
    public async Task Died_sets_patient_deceased()
    {
        var w = await WorldAsync("Died Clinic", "Late", "Patient");
        var now = DateTimeOffset.UtcNow;

        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Died", deathCause = "Cerebral malaria" }), HttpStatusCode.BadRequest, field: "deathTime");
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Died", deathTime = now.AddMinutes(-10) }), HttpStatusCode.BadRequest, field: "deathCause");
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Died", deathTime = now.AddHours(2), deathCause = "Cerebral malaria" }),
            HttpStatusCode.BadRequest, field: "deathTime");
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Died", deathTime = now.AddDays(-3), deathCause = "Cerebral malaria" }),
            HttpStatusCode.BadRequest, field: "deathTime"); // before the admission
        Assert.Equal(AdmissionStatus.Admitted, (await AdmissionAsync(w.AdmissionId)).Status);

        // Died needs no discharge summary: the time and cause are recorded (the cause encrypted) and the patient becomes Deceased.
        var deathTime = (await AdmissionAsync(w.AdmissionId)).AdmittedAt.AddSeconds(1);
        await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Died", deathTime, deathCause = "Cerebral malaria" }));
        var admission = await AdmissionAsync(w.AdmissionId);
        Assert.Equal(DischargeType.Died, admission.DischargeType);
        Assert.Equal(AdmissionStatus.Discharged, admission.Status);
        Assert.True(Math.Abs((admission.DeathTime!.Value - deathTime).TotalMilliseconds) < 1);
        Assert.DoesNotContain("malaria", admission.DeathCause);
        Assert.Equal("Cerebral malaria", Text.Read(admission.DeathCause));
        Assert.DoesNotContain("malaria", await RawRowsAsync("admissions", "id", w.AdmissionId));

        var patient = await Json(await w.Desk.GetAsync($"/api/care/patients/{w.PatientId}", Ct));
        Assert.Equal("Deceased", patient.GetProperty("status").GetString());
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.patient_deceased").ToListAsync(Ct)));
        Assert.Equal(w.PatientId.ToString(), audit.EntityId);
        Assert.DoesNotContain("malaria", audit.AfterJson ?? "");

        // The clinical read shows the time and cause; a deceased patient can't be admitted or checked in again.
        var read = await Json(await w.Doctor.GetAsync($"/api/care/admissions/{w.AdmissionId}", Ct));
        Assert.Equal("Cerebral malaria", read.GetProperty("deathCause").GetString());
        Assert.Equal("Died", read.GetProperty("dischargeType").GetString());
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/admissions",
            new { patientId = w.PatientId, reason = "Again", admittingDiagnosis = "Again" }, Ct), HttpStatusCode.Conflict, code: "patient_deceased");
        Assert.Equal(BedStatus.Cleaning, await api.WithDbAsync(db => db.Beds.IgnoreQueryFilters().Where(b => b.Id == w.BedId).Select(b => b.Status).FirstAsync(Ct)));
    }

    [Fact]
    public async Task Discharge_frees_bed_and_keeps_balance_open()
    {
        var w = await WorldAsync("Balance Clinic", bed: false);
        // A deposit of ₦10,000, a ₦50,000 procedure and (Task 13) the one ₦10,000 bed day of a same-day stay: ₦50,000 is owed at discharge.
        await Json(await kit.AssignBedAsync(w.Desk, w.AdmissionId, w.BedId, 1_000_000));
        var billId = await api.WithDbAsync(db => db.PatientBills.IgnoreQueryFilters().Where(b => b.AdmissionId == w.AdmissionId).Select(b => b.Id).FirstAsync(Ct));
        var procedure = await kit.PriceItemAsync(w.Clinic, "Ward procedure", "Procedure", 5_000_000);
        await Json(await w.Clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/lines", new { priceItemId = procedure, quantity = 1, reason = "Wound care" }, Ct));
        // Doses: an admission prescription (QDS for 2 days) leaves future doses Due.
        await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false, items = new[] { Item("Balancillin", "QDS", 2) } }, Ct));
        await SignedSummaryAsync(w.Doctor, w.AdmissionId);

        var discharged = await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }));
        Assert.Equal(billId, discharged.GetProperty("billId").GetGuid());
        Assert.Equal(5_000_000, discharged.GetProperty("balanceMinor").GetInt64());
        Assert.True(discharged.GetProperty("stoppedDoses").GetInt32() > 0);
        var dischargedAt = (await AdmissionAsync(w.AdmissionId)).DischargedAt!.Value;

        // The bed goes to Cleaning (not Free) and, once ready, takes the next patient.
        Assert.Equal(BedStatus.Cleaning, await api.WithDbAsync(db => db.Beds.IgnoreQueryFilters().Where(b => b.Id == w.BedId).Select(b => b.Status).FirstAsync(Ct)));
        var (_, next) = await AdmitAnotherAsync(w, "Next");
        await AssertProblemAsync(await kit.AssignBedAsync(w.Desk, next, w.BedId), HttpStatusCode.Conflict, "That bed is being cleaned.");
        await Json(await w.Desk.PostAsync($"/api/care/beds/{w.BedId}/ready", null, Ct));
        await Json(await kit.AssignBedAsync(w.Desk, next, w.BedId));

        // The bill is final: the balance stays open (PartPaid, ₦50,000 owed), no new line can be added, but money is still taken.
        var bill = await Json(await w.Cashier.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Equal("PartPaid", bill.GetProperty("status").GetString());
        Assert.Equal(5_000_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal(JsonValueKind.String, bill.GetProperty("finalisedAt").ValueKind);
        await AssertProblemAsync(await w.Clinic.Owner.PostAsJsonAsync($"/api/care/bills/{billId}/lines", new { priceItemId = procedure, quantity = 1, reason = "Late" }, Ct),
            HttpStatusCode.Conflict, BillFinal, code: "bill_final");
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/tests",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, kind = "Lab", description = "Late film", priceItemId = procedure }, Ct), HttpStatusCode.Conflict, Discharged);
        var paid = await api.AsTenantAsync<PaymentService, CarePaymentDto>(w.Clinic.TenantId, s => s.RecordDeskAsync(billId, 1_500_000, PayMethod.Cash, null, Ct));
        Assert.NotNull(paid.ReceiptNumber);
        bill = await Json(await w.Cashier.GetAsync($"/api/care/bills/{billId}", Ct));
        Assert.Equal(3_500_000, bill.GetProperty("balanceMinor").GetInt64());
        Assert.Equal("PartPaid", bill.GetProperty("status").GetString());
        // The procedure and the bed day; the refused late line was never added.
        Assert.Equal(2, await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters().CountAsync(l => l.BillId == billId, Ct)));

        // Care assignments: the ward nurse's end at discharge, the admitting doctor's 30 days later.
        var rows = await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.AdmissionId == w.AdmissionId).ToListAsync(Ct));
        Assert.Equal(dischargedAt, Assert.Single(rows, a => a.Kind == CareAssignmentKind.WardNurse).EndedAt);
        Assert.Equal(dischargedAt.AddDays(30), Assert.Single(rows, a => a.Kind == CareAssignmentKind.AdmittingDoctor).EndedAt);

        // Doses due after discharge are Stopped; none is left Due after the discharge time.
        var doses = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == w.AdmissionId).ToListAsync(Ct));
        Assert.DoesNotContain(doses, d => d.Status == DoseStatus.Due && d.DueAt > dischargedAt);
        Assert.Equal(discharged.GetProperty("stoppedDoses").GetInt32(), doses.Count(d => d.Status == DoseStatus.Stopped));

        // Audit: the discharge, with ids and no clinical text.
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.discharged").ToListAsync(Ct)));
        Assert.Equal(w.AdmissionId.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task Discharge_pdf_logged()
    {
        var w = await WorldAsync("Pdf Clinic");
        var pdfPath = $"/api/care/admissions/{w.AdmissionId}/discharge.pdf";

        // Clinical only: the front desk and the cashier are refused before anything is logged.
        foreach (var client in new[] { w.Desk, w.Cashier })
            await AssertProblemAsync(await client.GetAsync(pdfPath, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        Assert.DoesNotContain(await AccessLogAsync(w.PatientId), a => a.Area == "pdf");
        await AssertProblemAsync(await w.Doctor.GetAsync($"/api/care/admissions/{Guid.NewGuid()}/discharge.pdf", Ct), HttpStatusCode.NotFound);

        // No signed summary yet: 409 (the open is logged first, as every clinical read is).
        await AssertProblemAsync(await w.Doctor.GetAsync(pdfPath, Ct), HttpStatusCode.Conflict, code: "summary_unsigned");

        await SignedSummaryAsync(w.Doctor, w.AdmissionId);
        await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }));
        var before = (await AccessLogAsync(w.PatientId)).Count(a => a.Area == "pdf");
        var response = await w.Doctor.GetAsync(pdfPath, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        Assert.StartsWith("discharge-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        var log = await AccessLogAsync(w.PatientId);
        Assert.Equal(before + 1, log.Count(a => a.Area == "pdf"));
        var row = log.Last(a => a.Area == "pdf");
        Assert.Equal(w.DoctorId, row.StaffUserId);
        Assert.False(row.Emergency);

        // A doctor with no care relationship needs a reason (emergency access), like any clinical read.
        var (stranger, strangerId) = await kit.StaffAsync(w.Clinic, "Doctor");
        await AssertProblemAsync(await stranger.GetAsync(pdfPath, Ct), HttpStatusCode.Conflict, code: "emergency_reason_required");
        var request = new HttpRequestMessage(HttpMethod.Get, pdfPath);
        request.Headers.Add(RecordGuard.ReasonHeader, Uri.EscapeDataString("Readmission review"));
        Assert.Equal(HttpStatusCode.OK, (await stranger.SendAsync(request, Ct)).StatusCode);
        Assert.Contains(await AccessLogAsync(w.PatientId), a => a.StaffUserId == strangerId && a.Area == "pdf" && a.Emergency);
    }

    // ---------- Fix round 1 ----------

    /// <summary>I1: the director takes over an unsigned summary draft (author moves, audited with ids only), signs it and discharges home.</summary>
    [Fact]
    public async Task Director_takes_over_a_summary_draft_and_discharges_home()
    {
        var w = await WorldAsync("Takeover Clinic");
        var path = SummaryPath(w.AdmissionId);
        var ownerId = await OwnerIdAsync(w.Clinic);
        await Json(await w.Doctor.PutAsJsonAsync(path, new { diagnoses = "Takeover diagnoses" }, Ct));

        // Only the director takes over, and only a draft that exists.
        await AssertProblemAsync(await w.Doctor.PostAsync($"{path}/take-over", null, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await w.Nurse.PostAsync($"{path}/take-over", null, Ct), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await w.Clinic.Owner.PostAsync($"/api/care/admissions/{Guid.NewGuid()}/discharge-summary/take-over", null, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await w.Clinic.Owner.PutAsJsonAsync(path, FullSummary(), Ct), HttpStatusCode.Forbidden, AuthorOnly);

        var taken = await Json(await w.Clinic.Owner.PostAsync($"{path}/take-over", null, Ct));
        Assert.Equal(ownerId, taken.GetProperty("doctorUserId").GetGuid());
        Assert.Equal("Takeover diagnoses", taken.GetProperty("diagnoses").GetString()); // the draft's text is kept
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.note_taken_over").ToListAsync(Ct)));
        Assert.Contains(w.DoctorId.ToString(), audit.AfterJson);
        Assert.DoesNotContain("Takeover", audit.AfterJson!);

        // The old author is now refused; the director completes, signs and discharges home.
        await AssertProblemAsync(await w.Doctor.PutAsJsonAsync(path, FullSummary(), Ct), HttpStatusCode.Forbidden, AuthorOnly);
        await Json(await w.Clinic.Owner.PutAsJsonAsync(path, FullSummary(), Ct));
        await Json(await w.Clinic.Owner.PostAsync($"{path}/sign", null, Ct));
        await AssertProblemAsync(await w.Clinic.Owner.PostAsync($"{path}/take-over", null, Ct), HttpStatusCode.Conflict, SignedNote);
        await Json(await DischargeAsync(w.Clinic.Owner, w.AdmissionId, new { type = "Home" }));
        Assert.Equal(AdmissionStatus.Discharged, (await AdmissionAsync(w.AdmissionId)).Status);
    }

    /// <summary>I1: the director (or the author) discards an unsigned summary draft; a signed summary is never discarded.</summary>
    [Fact]
    public async Task Director_discards_a_summary_draft()
    {
        var w = await WorldAsync("Summary Discard Clinic");
        var path = SummaryPath(w.AdmissionId);
        await Json(await w.Doctor.PutAsJsonAsync(path, new { diagnoses = "Discard me" }, Ct));
        var (otherDoctor, otherId) = await kit.StaffAsync(w.Clinic, "Doctor");
        await kit.AssignAsync(w.Clinic, w.PatientId, otherId, CareAssignmentKind.AdmittingDoctor);
        await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters().Where(a => a.StaffUserId == otherId && a.PatientId == w.PatientId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AdmissionId, (Guid?)w.AdmissionId), Ct));
        await AssertProblemAsync(await otherDoctor.PostAsync($"{path}/discard", null, Ct), HttpStatusCode.Forbidden, AuthorOnly);
        await AssertProblemAsync(await w.Nurse.PostAsync($"{path}/discard", null, Ct), HttpStatusCode.Forbidden);

        Assert.Equal(HttpStatusCode.NoContent, (await w.Clinic.Owner.PostAsync($"{path}/discard", null, Ct)).StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.DischargeSummaries.IgnoreQueryFilters().CountAsync(s => s.AdmissionId == w.AdmissionId, Ct)));
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.note_discarded" && a.EntityType == "DischargeSummary").ToListAsync(Ct)));
        Assert.DoesNotContain("Discard me", audit.AfterJson!);
        await AssertProblemAsync(await w.Clinic.Owner.PostAsync($"{path}/discard", null, Ct), HttpStatusCode.NotFound);

        // A fresh draft by another care-team doctor is theirs now; once signed it is never discarded.
        await Json(await otherDoctor.PutAsJsonAsync(path, FullSummary(), Ct));
        await Json(await otherDoctor.PostAsync($"{path}/sign", null, Ct));
        foreach (var client in new[] { otherDoctor, w.Clinic.Owner })
            await AssertProblemAsync(await client.PostAsync($"{path}/discard", null, Ct), HttpStatusCode.Conflict, SignedNote);
    }

    /// <summary>
    /// I4: after an against-advice discharge the admitting doctor writes and signs the summary within the 30-day window; after it (fixed
    /// clock) a non-director gets 403 "This patient isn't on your ward."; the director has no window; a nurse never writes it.
    /// </summary>
    [Fact]
    public async Task Summary_after_discharge_is_limited_to_the_follow_up_window()
    {
        var w = await WorldAsync("Window Clinic");
        var ownerId = await OwnerIdAsync(w.Clinic);
        await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "AgainstAdvice", amaReason = "Left early" }));
        var dischargedAt = (await AdmissionAsync(w.AdmissionId)).DischargedAt!.Value;
        var request = new SaveDischargeSummaryRequest("Window diagnoses", null, "Left before treatment ended", null, null);

        // Day 31: the admitting doctor's assignment has ended.
        var late = dischargedAt.AddDays(31);
        var refused = await Assert.ThrowsAsync<Saas.Application.Common.ForbiddenException>(() =>
            AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, MemberRole.Staff, late, s => s.SaveSummaryAsync(w.AdmissionId, request, Ct)));
        Assert.Equal(NotOnWard, refused.Message);
        Assert.Equal(0, await api.WithDbAsync(db => db.DischargeSummaries.IgnoreQueryFilters().CountAsync(s => s.AdmissionId == w.AdmissionId, Ct)));

        // Day 29, and now: within the window the doctor writes and signs after discharge; a nurse never can.
        await AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, MemberRole.Staff, dischargedAt.AddDays(29), s => s.SaveSummaryAsync(w.AdmissionId, request, Ct));
        await AssertProblemAsync(await w.Nurse.PutAsJsonAsync(SummaryPath(w.AdmissionId), FullSummary(), Ct), HttpStatusCode.Forbidden, SummaryDoctorsOnly);
        var signed = await Json(await w.Doctor.PostAsync($"{SummaryPath(w.AdmissionId)}/sign", null, Ct));
        Assert.Equal("Signed", signed.GetProperty("status").GetString());

        // The director has no window: on day 60 they write and sign another discharged patient's summary.
        var (_, other) = await AdmitAnotherAsync(w, "Windowless");
        await Json(await DischargeAsync(w.Doctor, other, new { type = "AgainstAdvice", amaReason = "Left early" }));
        var otherAt = (await AdmissionAsync(other)).DischargedAt!.Value.AddDays(60);
        await AsStaffAtAsync(w.Clinic.TenantId, ownerId, MemberRole.Owner, otherAt, s => s.SaveSummaryAsync(other, request, Ct));
        var directorSigned = await AsStaffAtAsync(w.Clinic.TenantId, ownerId, MemberRole.Owner, otherAt, s => s.SignSummaryAsync(other, Ct));
        Assert.Equal(NoteStatus.Signed, directorSigned.Status);
        await Assert.ThrowsAsync<Saas.Application.Common.ForbiddenException>(() =>
            AsStaffAtAsync(w.Clinic.TenantId, w.DoctorId, MemberRole.Staff, otherAt, s => s.SignSummaryAsync(other, Ct)));
    }

    /// <summary>M4: at discharge, unrecorded doses due at or before the discharge become Missed (no notice); later ones Stopped; recorded ones stay.</summary>
    [Fact]
    public async Task Discharge_marks_past_due_doses_missed()
    {
        var w = await WorldAsync("Missed Clinic");
        await SignedSummaryAsync(w.Doctor, w.AdmissionId);
        await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false, items = new[] { Item("Pastol", "QDS", 2) } }, Ct));
        var doses = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == w.AdmissionId).OrderBy(d => d.DueAt).ToListAsync(Ct));
        // Move the first three into the past; record the first as Given.
        await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.Id == doses[0].Id || d.Id == doses[1].Id || d.Id == doses[2].Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.DueAt, d => d.DueAt.AddDays(-2)), Ct));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{doses[0].Id}", new { status = "Given" }, Ct));
        var noticesBefore = await api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.TenantId == w.Clinic.TenantId, Ct));

        var discharged = await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }));
        Assert.Equal(2, discharged.GetProperty("missedDoses").GetInt32());
        var after = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == w.AdmissionId).ToDictionaryAsync(d => d.Id, Ct));
        Assert.Equal(DoseStatus.Given, after[doses[0].Id].Status);
        Assert.Equal(DoseStatus.Missed, after[doses[1].Id].Status);
        Assert.Equal(DoseStatus.Missed, after[doses[2].Id].Status);
        Assert.All(doses.Skip(3), d => Assert.Equal(DoseStatus.Stopped, after[d.Id].Status));
        Assert.DoesNotContain(after.Values, d => d.Status == DoseStatus.Due);
        Assert.Equal(noticesBefore, await api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.TenantId == w.Clinic.TenantId, Ct)));
    }

    private Task<Guid> OwnerIdAsync(CareKit.Clinic clinic) => api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
        .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaffUser(Guid userId, MemberRole role) : Saas.Application.Common.ICurrentUser
    {
        public Guid? UserId => userId;
        public MemberRole? Role => role;
        public string? IpAddress => null;
    }

    /// <summary>The discharge service as <paramref name="userId"/> with the clock fixed at <paramref name="at"/> (RecordGuard on the same clock).</summary>
    private async Task<T> AsStaffAtAsync<T>(Guid tenantId, Guid userId, MemberRole role, DateTimeOffset at, Func<DischargeService, Task<T>> action)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<Saas.Application.Common.ITenantContext>().SetTenant(tenantId);
        Saas.Application.Common.ICurrentUser user = new StaffUser(userId, role);
        var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
        TimeProvider clock = new FixedClock(at);
        var guard = ActivatorUtilities.CreateInstance<RecordGuard>(sp, access, user, clock);
        return await action(ActivatorUtilities.CreateInstance<DischargeService>(sp, access, guard, user, clock));
    }

    // ---------- Carries ----------

    /// <summary>Carry (Task 9): unsigned ward-round drafts give a 409 with their list unless the doctor acknowledges them (recorded).</summary>
    [Fact]
    public async Task Unsigned_ward_round_drafts_block_discharge_until_acknowledged()
    {
        var w = await WorldAsync("Drafts Clinic");
        await SignedSummaryAsync(w.Doctor, w.AdmissionId);
        var rounds = $"/api/care/admissions/{w.AdmissionId}/ward-rounds";
        var first = Id(await Json(await w.Doctor.PostAsJsonAsync(rounds, new { note = "Draft round, secretdraft" }, Ct)));

        var problem = await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Conflict, UnsignedRounds,
            code: "unsigned_ward_rounds");
        var listed = Assert.Single(problem.GetProperty("unsignedWardRounds").EnumerateArray().ToList());
        Assert.Equal(first, Id(listed));
        Assert.Equal(w.DoctorId, listed.GetProperty("doctorUserId").GetGuid());
        Assert.DoesNotContain("secretdraft", problem.GetRawText());
        Assert.Equal(AdmissionStatus.Admitted, (await AdmissionAsync(w.AdmissionId)).Status);

        // Discard: the author only (the director too is refused); then the discharge goes through without acknowledgement.
        await AssertProblemAsync(await w.Clinic.Owner.PostAsync($"/api/care/ward-rounds/{first}/discard", null, Ct), HttpStatusCode.Forbidden, AuthorOnly);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Doctor.PostAsync($"/api/care/ward-rounds/{first}/discard", null, Ct)).StatusCode);
        Assert.Equal(0, await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().CountAsync(r => r.Id == first, Ct)));
        var signedRound = Id(await Json(await w.Doctor.PostAsJsonAsync(rounds, new { note = "Signed round" }, Ct)));
        await Json(await w.Doctor.PostAsync($"/api/care/ward-rounds/{signedRound}/sign", null, Ct));
        await AssertProblemAsync(await w.Doctor.PostAsync($"/api/care/ward-rounds/{signedRound}/discard", null, Ct), HttpStatusCode.Conflict, SignedNote);

        // A new draft, acknowledged: discharged, the draft stays a draft, the acknowledgement is recorded and audited (ids only).
        var second = Id(await Json(await w.Doctor.PostAsJsonAsync(rounds, new { note = "Another draft" }, Ct)));
        await AssertProblemAsync(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }), HttpStatusCode.Conflict, UnsignedRounds);
        var discharged = await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home", acknowledgeUnsignedNotes = true }));
        Assert.Equal(1, discharged.GetProperty("unsignedRoundsAcknowledged").GetInt32());
        Assert.Equal(1, (await AdmissionAsync(w.AdmissionId)).UnsignedRoundsAtDischarge);
        Assert.Equal(NoteStatus.Draft, await api.WithDbAsync(db => db.WardRounds.IgnoreQueryFilters().Where(r => r.Id == second).Select(r => r.Status).FirstAsync(Ct)));
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.TenantId == w.Clinic.TenantId && a.Action == "care.unsigned_notes_acknowledged").ToListAsync(Ct)));
        Assert.Contains(second.ToString(), audit.AfterJson);
        Assert.DoesNotContain("draft", audit.AfterJson!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Carry (Task 9): an admission prescription takes the patient lock and re-checks "still admitted" inside its transaction. The test
    /// holds the patient's lock, lets the prescription reach it, discharges the admission under the lock, then releases it: 409, and nothing
    /// is written (no prescription, no doses, no bill line).
    /// </summary>
    [Fact]
    public async Task Admission_prescription_waits_for_the_patient_lock_and_rechecks_discharge()
    {
        var w = await WorldAsync("Lock Race Clinic");
        var drug = await kit.PriceItemAsync(w.Clinic, "Racing tablets", "Drug", 10_000);
        await api.WithDbAsync(db => db.ClinicProfiles.IgnoreQueryFilters().Where(c => c.TenantId == w.Clinic.TenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DispensesDrugs, true), Ct));
        var response = await UnderHeldDischargeAsync(w, "the prescription", () => w.Doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false,
            items = new[] { new { drug = "Racingcillin", dose = "1 tablet", frequency = "TDS", durationDays = 3, priceItemId = drug } },
        }, Ct));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, Discharged, code: "admission_discharged");
        Assert.Equal(0, await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().CountAsync(p => p.AdmissionId == w.AdmissionId, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().CountAsync(d => d.AdmissionId == w.AdmissionId, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters()
            .CountAsync(l => db.PatientBills.IgnoreQueryFilters().Any(b => b.Id == l.BillId && b.AdmissionId == w.AdmissionId), Ct)));
    }

    /// <summary>
    /// Holds the patient's admission lock in a raw transaction, starts <paramref name="request"/>, waits until a session is blocked on
    /// that advisory lock (filtered on the lock call's text, so another test's waiter can't count), discharges the admission under the
    /// lock (status, time and type, and the admission's open care assignments ended, as the real discharge does), then commits and returns
    /// the request's answer. Fails when the request never waited.
    /// </summary>
    private async Task<HttpResponseMessage> UnderHeldDischargeAsync(World w, string what, Func<Task<HttpResponseMessage>> request)
    {
        var key = AdmissionService.PatientLockKey(w.Clinic.TenantId, w.PatientId);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Saas.Infrastructure.Persistence.AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", Ct);
        var pending = request();
        var blocked = await WaitForAdvisoryWaiterAsync(pending);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE admissions SET status = 'Discharged', discharged_at = now(), discharge_type = 'AgainstAdvice' WHERE id = {w.AdmissionId}", Ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE care_assignments SET ended_at = now() WHERE admission_id = {w.AdmissionId} AND ended_at IS NULL AND kind = 'WardNurse'", Ct);
        await tx.CommitAsync(Ct);
        var response = await pending;
        Assert.True(blocked, $"{what} didn't wait on the patient lock: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return response;
    }

    /// <summary>True once some session waits on an advisory lock taken by <c>pg_advisory_xact_lock</c>; false if <paramref name="pending"/> finishes first.</summary>
    private async Task<bool> WaitForAdvisoryWaiterAsync(Task pending)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!pending.IsCompleted && DateTime.UtcNow < deadline)
        {
            if (await api.WithDbAsync(d => d.Database.SqlQuery<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND wait_event = 'advisory' AND query ILIKE '%pg_advisory_xact_lock%'").FirstAsync(Ct)) > 0)
                return true;
            await Task.Delay(50, Ct);
        }
        return false;
    }

    /// <summary>M2: an admission test request takes the patient lock and re-checks discharge, as prescriptions do.</summary>
    [Fact]
    public async Task Admission_test_request_waits_for_the_patient_lock_and_rechecks_discharge()
    {
        var w = await WorldAsync("Test Lock Clinic");
        var film = await kit.PriceItemAsync(w.Clinic, "Racing film", "Test", 20_000);
        var response = await UnderHeldDischargeAsync(w, "the test request", () => w.Doctor.PostAsJsonAsync("/api/care/tests",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, kind = "Imaging", description = "Racing chest film", priceItemId = film }, Ct));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, Discharged, code: "admission_discharged");
        Assert.Equal(0, await api.WithDbAsync(db => db.TestRequests.IgnoreQueryFilters().CountAsync(t => t.AdmissionId == w.AdmissionId, Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters()
            .CountAsync(l => db.PatientBills.IgnoreQueryFilters().Any(b => b.Id == l.BillId && b.AdmissionId == w.AdmissionId), Ct)));
    }

    /// <summary>
    /// I2: a ward nurse-list edit racing a discharge. The discharge lands under the patient lock while the edit (adding a nurse to the
    /// ward) runs; the edit must wait for the lock and see the patient discharged, so no open ward-nurse row is left on the admission.
    /// </summary>
    [Fact]
    public async Task Ward_nurse_edit_racing_a_discharge_leaves_no_open_nurse_row()
    {
        var w = await WorldAsync("Nurse Race Clinic");
        var (_, newNurseId) = await kit.StaffAsync(w.Clinic, "Nurse");
        var bedItem = await api.WithDbAsync(db => db.Wards.IgnoreQueryFilters().Where(x => x.Id == w.WardId).Select(x => x.BedPriceItemId).FirstAsync(Ct));
        var response = await UnderHeldDischargeAsync(w, "the ward edit", () => w.Clinic.Owner.PutAsJsonAsync($"/api/care/wards/{w.WardId}",
            new { name = "Nurse Race Clinic ward", kind = "General", bedPriceItemId = bedItem, nurseUserIds = new[] { w.NurseId, newNurseId } }, Ct));
        await Json(response);
        var open = await api.WithDbAsync(db => db.CareAssignments.IgnoreQueryFilters()
            .Where(a => a.AdmissionId == w.AdmissionId && a.Kind == CareAssignmentKind.WardNurse && a.EndedAt == null).ToListAsync(Ct));
        Assert.Empty(open);
        // The ward's list did change (the edit itself went through).
        Assert.Contains(newNurseId, await api.WithDbAsync(db => db.Wards.IgnoreQueryFilters().Where(x => x.Id == w.WardId).Select(x => x.NurseUserIds).FirstAsync(Ct)));
    }

    /// <summary>
    /// A prescription and a discharge sent together, several times: whichever wins, nothing is left Due after the discharge and no
    /// prescription is written after it (it either landed first, and its later doses were stopped, or it was refused).
    /// </summary>
    [Fact]
    public async Task Prescription_racing_a_discharge_never_outlives_it()
    {
        var w = await WorldAsync("Race Clinic");
        var admissions = new List<Guid> { w.AdmissionId };
        for (var i = 0; i < 3; i++) admissions.Add((await AdmitAnotherAsync(w, $"Racer{i}")).AdmissionId);

        foreach (var admissionId in admissions)
        {
            var patientId = (await AdmissionAsync(admissionId)).PatientId;
            var rx = w.Doctor.PostAsJsonAsync("/api/care/prescriptions", new
            {
                patientId, admissionId, allergyAcknowledged = false, items = new[] { Item("Racemol", "Q4H", 2) },
            }, Ct);
            var discharge = DischargeAsync(w.Doctor, admissionId, new { type = "AgainstAdvice", amaReason = "Racing home" });
            await Task.WhenAll(rx, discharge);
            await Json(discharge.Result);
            var rxResponse = rx.Result;
            var dischargedAt = (await AdmissionAsync(admissionId)).DischargedAt!.Value;
            var prescriptions = await api.WithDbAsync(db => db.Prescriptions.IgnoreQueryFilters().Where(p => p.AdmissionId == admissionId).ToListAsync(Ct));
            var doses = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == admissionId).ToListAsync(Ct));
            if (rxResponse.IsSuccessStatusCode)
            {
                var written = Assert.Single(prescriptions);
                Assert.True(written.CreatedAt <= dischargedAt, "a prescription was written after the discharge");
                Assert.NotEmpty(doses);
            }
            else
            {
                await AssertProblemAsync(rxResponse, HttpStatusCode.Conflict, Discharged);
                Assert.Empty(prescriptions);
                Assert.Empty(doses);
            }
            Assert.DoesNotContain(doses, d => d.Status == DoseStatus.Due && d.DueAt > dischargedAt);
        }
    }

    /// <summary>Carry: GET /admissions lists current and recent admissions for leadership and clinical staff; non-clinical callers get demographics, bed and status only.</summary>
    [Fact]
    public async Task Admissions_list_shows_current_and_recent()
    {
        var w = await WorldAsync("List Clinic", "Current", "Patient");
        var (admin, _) = await kit.StaffAsync(w.Clinic, "Admin");
        var (_, recent) = await AdmitAnotherAsync(w, "Recent");
        var (_, old) = await AdmitAnotherAsync(w, "Old");
        await Json(await DischargeAsync(w.Doctor, recent, new { type = "AgainstAdvice", amaReason = "Went home" }));
        await Json(await DischargeAsync(w.Doctor, old, new { type = "AgainstAdvice", amaReason = "Went home long ago" }));
        await api.WithDbAsync(db => db.Admissions.IgnoreQueryFilters().Where(a => a.Id == old)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DischargedAt, DateTimeOffset.UtcNow.AddDays(-31)), Ct));
        await Json(await w.Doctor.PutAsJsonAsync(SummaryPath(w.AdmissionId), new { diagnoses = "Draft only" }, Ct));

        // Ruling (I3): the cashier is refused; the front desk lists (the desk rule) with demographics, bed, status and dates only.
        await AssertProblemAsync(await w.Cashier.GetAsync("/api/care/admissions", Ct), HttpStatusCode.Forbidden);
        var asDesk = (await Json(await w.Desk.GetAsync("/api/care/admissions", Ct))).EnumerateArray().ToList();
        Assert.Equal(new[] { w.AdmissionId, recent }.OrderBy(x => x), asDesk.Select(Id).OrderBy(x => x));
        foreach (var row in asDesk)
        {
            Assert.False(row.TryGetProperty("dischargeType", out _));
            Assert.False(row.TryGetProperty("dischargeSummaryStatus", out _));
            Assert.DoesNotContain("Went home", row.GetRawText());
        }

        // Leadership without a clinical role: demographics, bed and status; no discharge type or summary status.
        var asAdmin = (await Json(await admin.GetAsync("/api/care/admissions", Ct))).EnumerateArray().ToList();
        Assert.Equal(new[] { w.AdmissionId, recent }.OrderBy(x => x), asAdmin.Select(Id).OrderBy(x => x));
        var current = asAdmin.Single(a => Id(a) == w.AdmissionId);
        Assert.Equal("Admitted", current.GetProperty("status").GetString());
        Assert.Equal("D1", current.GetProperty("bedLabel").GetString());
        Assert.Equal("Current Patient", current.GetProperty("patientName").GetString());
        var recentRow = asAdmin.Single(a => Id(a) == recent);
        Assert.Equal("Discharged", recentRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, recentRow.GetProperty("dischargedAt").ValueKind);
        foreach (var row in asAdmin)
        {
            Assert.False(row.TryGetProperty("dischargeType", out _));
            Assert.False(row.TryGetProperty("dischargeSummaryStatus", out _));
            Assert.DoesNotContain("Went home", row.GetRawText());
        }

        // Clinical staff also see how the patient left and the summary's status (never its text).
        var asNurse = (await Json(await w.Nurse.GetAsync("/api/care/admissions", Ct))).EnumerateArray().ToList();
        Assert.Equal("AgainstAdvice", asNurse.Single(a => Id(a) == recent).GetProperty("dischargeType").GetString());
        Assert.Equal("Draft", asNurse.Single(a => Id(a) == w.AdmissionId).GetProperty("dischargeSummaryStatus").GetString());
        Assert.DoesNotContain("Draft only", string.Join("", asNurse.Select(a => a.GetRawText())));

        // The status filter; listing is not a record open.
        var onlyDischarged = (await Json(await w.Clinic.Owner.GetAsync("/api/care/admissions?status=Discharged", Ct))).EnumerateArray().Select(Id).ToList();
        Assert.Equal([recent], onlyDischarged);
        Assert.DoesNotContain(await AccessLogAsync(w.PatientId), a => a.Area == "record");
    }

    /// <summary>Carry: the record timeline holds ward care (nursing notes, ward rounds, a medication chart summary) and the discharge summary.</summary>
    [Fact]
    public async Task Record_timeline_shows_ward_care_and_discharge_summary()
    {
        var w = await WorldAsync("Timeline Clinic");
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/nursing-notes", new { note = "Timeline nursing note" }, Ct));
        var round = Id(await Json(await w.Doctor.PostAsJsonAsync($"/api/care/admissions/{w.AdmissionId}/ward-rounds", new { note = "Timeline round" }, Ct)));
        await Json(await w.Doctor.PostAsync($"/api/care/ward-rounds/{round}/sign", null, Ct));
        var rx = await Json(await w.Doctor.PostAsJsonAsync("/api/care/prescriptions",
            new { patientId = w.PatientId, admissionId = w.AdmissionId, allergyAcknowledged = false, items = new[] { Item("Timelinol", "Stat"), Item("Laterol", "OD", 3) } }, Ct));
        var statDose = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters()
            .Where(d => d.PrescriptionItemId == rx.GetProperty("items")[0].GetProperty("id").GetGuid()).Select(d => d.Id).SingleAsync(Ct));
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/doses/{statDose}", new { status = "Given" }, Ct));
        await SignedSummaryAsync(w.Doctor, w.AdmissionId);
        await Json(await DischargeAsync(w.Doctor, w.AdmissionId, new { type = "Home" }));

        var record = await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct));
        Assert.Equal("Timeline nursing note", record.GetProperty("nursingNotes")[0].GetProperty("note").GetString());
        Assert.Equal("Timeline round", record.GetProperty("wardRounds")[0].GetProperty("note").GetString());
        var summary = record.GetProperty("medicationSummary").EnumerateArray().ToList();
        var stat = summary.Single(s => s.GetProperty("drug").GetString() == "Timelinol");
        Assert.Equal(1, stat.GetProperty("given").GetInt32());
        Assert.Equal(JsonValueKind.String, stat.GetProperty("lastGivenAt").ValueKind);
        var later = summary.Single(s => s.GetProperty("drug").GetString() == "Laterol");
        Assert.Equal(0, later.GetProperty("given").GetInt32());
        Assert.True(later.GetProperty("stopped").GetInt32() > 0);
        Assert.Equal(w.AdmissionId, later.GetProperty("admissionId").GetGuid());
        var discharge = record.GetProperty("dischargeSummaries")[0];
        Assert.Equal("Severe malaria, resolved", discharge.GetProperty("diagnoses").GetString());
        Assert.Equal("Signed", discharge.GetProperty("status").GetString());
        var admission = record.GetProperty("admissions")[0];
        Assert.Equal("Home", admission.GetProperty("dischargeType").GetString());
    }
}
