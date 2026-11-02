using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Application.Common;
using Saas.Domain.Care;
using Saas.Domain.Core;
using Products = Saas.Application.Billing.Products;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 3: the clinical record guard. Every clinical read is logged, emergency access needs a reason and alerts the medical
/// director (once per staff member, patient and Lagos day), clinical text is encrypted at rest, and non-clinical staff never see a
/// clinical field on any <c>/api/care</c> GET route.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed partial class CareAccessTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string ReasonRequired = "Give a reason to open this patient's record.";
    private const string EmergencyNotice = "Emergency access was used on a patient record.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? code = null)
    {
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (code is not null) Assert.Equal(code, body.GetProperty("code").GetString());
    }

    /// <summary>A GET carrying the emergency reason in the <c>X-Care-Reason</c> header (UTF-8, percent-encoded, as care-web sends it).</summary>
    private static Task<HttpResponseMessage> GetWithReasonAsync(HttpClient client, string path, string? reason)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (reason is not null) request.Headers.Add(RecordGuard.ReasonHeader, Uri.EscapeDataString(reason));
        return client.SendAsync(request, Ct);
    }

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    private Task<int> NoticeCountAsync(Guid ownerId, Guid tenantId) =>
        api.WithDbAsync(db => db.UserNotifications.CountAsync(n => n.UserId == ownerId && n.TenantId == tenantId && n.Kind == "care.emergency_access", Ct));

    private Task<Guid> OwnerIdAsync(CareKit.Clinic clinic) =>
        api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == clinic.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));

    private string? ReadReason(RecordAccess row) => api.Services.GetRequiredService<ClinicalText>().Read(row.Reason);

    // ---------- Access log ----------

    /// <summary>
    /// Each clinical read route a doctor with a care relationship can open, and the area it logs. Later tasks add their routes here
    /// (consultation, admission, prescription, result, discharge, pdf, file) with the ids they seed.
    /// </summary>
    /// <summary>
    /// The route patterns <see cref="ClinicalReads"/> exercises ("GET /api/care/…/{id:guid}…"), so Task 16's role table can check that every
    /// clinical GET is proved to write the access log (Review Focus 5).
    /// </summary>
    internal static IReadOnlySet<string> ClinicalReadRoutes =>
        ClinicalReads(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
            .Select(r => "GET " + Regex.Replace(r.Path, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "{id:guid}"))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string Area, string Path)> ClinicalReads(Guid patientId, Guid visitId, Guid prescriptionId, Guid testId, Guid admissionId) =>
    [
        ("record", $"/api/care/patients/{patientId}"),
        ("record", $"/api/care/visits/{visitId}/vitals"),
        // Task 6: the patient record timeline (the consultation open is a POST, checked in CareConsultationTests).
        ("record", $"/api/care/patients/{patientId}/record"),
        // Task 8: one admission with its reason and admitting diagnosis.
        ("admission", $"/api/care/admissions/{admissionId}"),
        // Task 9: ward care reads on that admission.
        ("nursing", $"/api/care/admissions/{admissionId}/nursing-notes"),
        ("fluids", $"/api/care/admissions/{admissionId}/fluids"),
        ("ward-round", $"/api/care/admissions/{admissionId}/ward-rounds"),
        ("medication", $"/api/care/admissions/{admissionId}/medication-chart"),
        // Task 10: the discharge summary and its PDF.
        ("discharge", $"/api/care/admissions/{admissionId}/discharge-summary"),
        ("pdf", $"/api/care/admissions/{admissionId}/discharge.pdf"),
        // Task 7: the prescription PDF and the result file's signed link.
        ("pdf", $"/api/care/prescriptions/{prescriptionId}/pdf"),
        ("file", $"/api/care/tests/{testId}/file"),
    ];

    /// <summary>
    /// Seeds a prescription and a resulted test with a file for the patient straight into the database (no visit, consultation or triage,
    /// so the access log holds only the reads under test). Returns their ids.
    /// </summary>
    private async Task<(Guid Prescription, Guid Test, Guid Admission)> SeedOrdersAsync(CareKit.Clinic clinic, Guid patientId, Guid doctorId)
    {
        var text = api.Services.GetRequiredService<ClinicalText>();
        var storage = api.Services.GetRequiredService<IFileStorage>();
        var key = $"private/care/{clinic.TenantId}/results/{Guid.NewGuid():N}.pdf";
        await using (var stream = new MemoryStream("%PDF-1.4 seeded result"u8.ToArray())) await storage.SaveAsync(key, stream, "application/pdf", Ct);
        var prescription = new Prescription
        {
            TenantId = clinic.TenantId, PatientId = patientId, DoctorUserId = doctorId,
            Items = [new PrescriptionItem { TenantId = clinic.TenantId, Drug = text.Protect("Seeded drug"), Dose = text.Protect("1 tablet"), Frequency = DoseFrequency.OD }],
        };
        var test = new TestRequest
        {
            TenantId = clinic.TenantId, PatientId = patientId, RequestedBy = doctorId, Kind = TestKind.Lab, Description = text.Protect("Seeded test"),
            Status = TestStatus.Resulted, ResultFileKey = key, ResultFileType = "application/pdf", ResultedBy = doctorId, ResultedAt = DateTimeOffset.UtcNow,
        };
        // Task 8: an admission awaiting a bed (no care assignments are written, so only the reads under test log).
        var admission = new Admission
        {
            TenantId = clinic.TenantId, PatientId = patientId, AdmittingDoctorUserId = doctorId, Reason = text.Protect("Seeded admission reason"),
            AdmittingDiagnosis = text.Protect("Seeded admitting diagnosis"), AdmittedAt = DateTimeOffset.UtcNow,
        };
        // Task 10: a signed discharge summary on that admission (the summary read and the discharge PDF).
        var summary = new DischargeSummary
        {
            TenantId = clinic.TenantId, AdmissionId = admission.Id, PatientId = patientId, DoctorUserId = doctorId,
            Diagnoses = text.Protect("Seeded discharge diagnoses"), Condition = text.Protect("Seeded condition"), Status = NoteStatus.Signed,
            SignedAt = DateTimeOffset.UtcNow,
        };
        await api.WithDbAsync(db =>
        {
            db.Prescriptions.Add(prescription);
            db.TestRequests.Add(test);
            db.Admissions.Add(admission);
            db.DischargeSummaries.Add(summary);
            return db.SaveChangesAsync(Ct);
        });
        return (prescription.Id, test.Id, admission.Id);
    }

    [Fact]
    public async Task Every_clinical_read_writes_access_log()
    {
        var clinic = await kit.ClinicAsync("Access Log Clinic");
        var ownerId = await OwnerIdAsync(clinic);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");
        var patientId = (await kit.PatientAsync(clinic, "Bisi", "Ade")).GetProperty("id").GetGuid();
        await kit.AssignAsync(clinic, patientId, doctorId);
        var visitId = (await kit.CheckInAsync(frontDesk, patientId)).GetProperty("id").GetGuid();
        var (prescriptionId, testId, admissionId) = await SeedOrdersAsync(clinic, patientId, doctorId);

        // Demographics-only reads by non-clinical staff (front desk, an admin without a clinical role) are not clinical reads: no row.
        await Json(await frontDesk.GetAsync($"/api/care/patients/{patientId}", Ct));
        var asAdmin = await Json(await admin.GetAsync($"/api/care/patients/{patientId}", Ct));
        Assert.False(asAdmin.TryGetProperty("allergies", out _));
        // The PDF, the file link and the admission are clinical only: 403, and nothing is logged.
        foreach (var path in new[]
                 {
                     $"/api/care/prescriptions/{prescriptionId}/pdf", $"/api/care/tests/{testId}/file", $"/api/care/admissions/{admissionId}",
                     $"/api/care/admissions/{admissionId}/nursing-notes", $"/api/care/admissions/{admissionId}/fluids",
                     $"/api/care/admissions/{admissionId}/ward-rounds", $"/api/care/admissions/{admissionId}/medication-chart",
                     $"/api/care/admissions/{admissionId}/discharge-summary", $"/api/care/admissions/{admissionId}/discharge.pdf",
                 })
            foreach (var client in new[] { frontDesk, admin })
                await AssertProblemAsync(await client.GetAsync(path, Ct), HttpStatusCode.Forbidden, "Only doctors and nurses can see clinical notes.");
        Assert.Empty(await AccessLogAsync(patientId));

        var expected = 0;
        foreach (var (area, path) in ClinicalReads(patientId, visitId, prescriptionId, testId, admissionId))
        {
            var read = await doctor.GetAsync(path, Ct);
            Assert.True(read.IsSuccessStatusCode, $"{path}: {(int)read.StatusCode} {await read.Content.ReadAsStringAsync(Ct)}");
            expected++;
            var log = await AccessLogAsync(patientId);
            Assert.Equal(expected, log.Count);
            var row = log[^1];
            Assert.Equal(area, row.Area);
            Assert.Equal(doctorId, row.StaffUserId);
            Assert.Equal("Doctor", row.Role);
            Assert.False(row.Emergency);
            Assert.Null(row.Reason);
            Assert.Equal(clinic.TenantId, row.TenantId);
        }

        // An ended assignment no longer counts as a care relationship.
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        await kit.AssignAsync(clinic, patientId, nurseId, CareAssignmentKind.TriageNurse, ended: true);
        await AssertProblemAsync(await nurse.GetAsync($"/api/care/patients/{patientId}", Ct), HttpStatusCode.Conflict, ReasonRequired, "emergency_reason_required");

        // An assignment to one patient doesn't open another.
        var otherPatientId = (await kit.PatientAsync(clinic, "Kunle", "Ade")).GetProperty("id").GetGuid();
        await AssertProblemAsync(await doctor.GetAsync($"/api/care/patients/{otherPatientId}", Ct), HttpStatusCode.Conflict, ReasonRequired, "emergency_reason_required");
        Assert.Empty(await AccessLogAsync(otherPatientId));
        Assert.Equal(expected, (await AccessLogAsync(patientId)).Count);

        // The medical director opens any record: logged as Medical director, never an emergency, no reason, no notice.
        await Json(await clinic.Owner.GetAsync($"/api/care/patients/{otherPatientId}", Ct));
        var director = Assert.Single(await AccessLogAsync(otherPatientId));
        Assert.Equal(ownerId, director.StaffUserId);
        Assert.Equal("Medical director", director.Role);
        Assert.False(director.Emergency);
        Assert.Null(director.Reason);
        Assert.Equal(0, await NoticeCountAsync(ownerId, clinic.TenantId));

        // Only the medical director reads the log: not a doctor, the front desk, or an admin with or without a clinical role.
        var entries = (await Json(await clinic.Owner.GetAsync($"/api/care/patients/{patientId}/access-log", Ct))).EnumerateArray().ToList();
        Assert.Equal(expected, entries.Count);
        Assert.Equal("file", entries[0].GetProperty("area").GetString()); // newest first: the last read in ClinicalReads
        Assert.Equal("record", entries[^1].GetProperty("area").GetString());
        Assert.Equal("Doctor", entries[0].GetProperty("role").GetString());
        Assert.Equal("Doctor Staff", entries[0].GetProperty("staffName").GetString());
        Assert.False(entries[0].GetProperty("emergency").GetBoolean());
        var (clinicalAdmin, clinicalAdminId) = await kit.StaffAsync(clinic, "Admin");
        (await clinic.Owner.PutAsJsonAsync($"/api/care/staff/{clinicalAdminId}", new { clinicalRole = "Doctor" }, Ct)).EnsureSuccessStatusCode();
        foreach (var client in new[] { doctor, frontDesk, admin, clinicalAdmin })
            await AssertProblemAsync(await client.GetAsync($"/api/care/patients/{patientId}/access-log", Ct), HttpStatusCode.Forbidden);

        // Support's view-as session acts as the owner but never reads the log (reasons are written under pressure and can be clinical).
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{clinic.TenantId}/view-as", new { reason = "Checking a reported access log problem." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());
        await AssertProblemAsync(await viewer.GetAsync($"/api/care/patients/{patientId}/access-log", Ct), HttpStatusCode.Forbidden);

        // Another clinic's patient is 404, and nothing is logged against it.
        var other = await kit.ClinicAsync("Access Log Other Clinic");
        var foreign = (await kit.PatientAsync(other, "Femi", "Ola")).GetProperty("id").GetGuid();
        await AssertProblemAsync(await GetWithReasonAsync(doctor, $"/api/care/patients/{foreign}", "Checking"), HttpStatusCode.NotFound);
        Assert.Empty(await AccessLogAsync(foreign));
    }

    [Fact]
    public async Task Emergency_access_needs_a_reason_and_alerts_the_director()
    {
        var clinic = await kit.ClinicAsync("Emergency Clinic");
        var ownerId = await OwnerIdAsync(clinic);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var patient = await kit.PatientAsync(clinic, "Chidi", "Nwosu");
        var patientId = patient.GetProperty("id").GetGuid();
        (await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { "Sulfa" }, conditions = Array.Empty<string>(), bloodGroup = "A+", genotype = "AA" }, Ct)).EnsureSuccessStatusCode();
        var directorReads = (await AccessLogAsync(patientId)).Count; // none: the facts write isn't an open
        Assert.Equal(0, directorReads);

        // No care relationship and no reason (or a blank one): refused, nothing logged, nobody notified.
        await AssertProblemAsync(await doctor.GetAsync($"/api/care/patients/{patientId}", Ct), HttpStatusCode.Conflict, ReasonRequired, "emergency_reason_required");
        await AssertProblemAsync(await GetWithReasonAsync(doctor, $"/api/care/patients/{patientId}", "   "), HttpStatusCode.Conflict, ReasonRequired, "emergency_reason_required");

        // The reason never travels in the URL: a ?reason= query is refused outright, even with the header.
        await AssertProblemAsync(await doctor.GetAsync($"/api/care/patients/{patientId}?reason=Brought%20in", Ct), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await GetWithReasonAsync(doctor, $"/api/care/patients/{patientId}?reason=x", "Brought in"), HttpStatusCode.BadRequest);
        Assert.Empty(await AccessLogAsync(patientId));
        Assert.Equal(0, await NoticeCountAsync(ownerId, clinic.TenantId));

        // With a reason in the header (UTF-8, percent-encoded): opened, logged as emergency with the reason, and the director is told.
        const string reason = "Brought in unconscious — Ọmọ́ Àdìó";
        var opened = await Json(await GetWithReasonAsync(doctor, $"/api/care/patients/{patientId}", reason));
        Assert.Equal("Sulfa", opened.GetProperty("allergies")[0].GetString());
        var row = Assert.Single(await AccessLogAsync(patientId));
        Assert.True(row.Emergency);
        Assert.Equal(reason, ReadReason(row));
        Assert.DoesNotContain("unconscious", row.Reason); // stored through ClinicalText
        Assert.Equal(doctorId, row.StaffUserId);
        Assert.Equal("record", row.Area);

        var notice = Assert.Single(await api.WithDbAsync(db => db.UserNotifications.Where(n => n.UserId == ownerId && n.Kind == "care.emergency_access").ToListAsync(Ct)));
        Assert.Equal(EmergencyNotice, notice.Body);
        Assert.Equal(Products.Care, notice.Product);
        Assert.Equal(clinic.TenantId, notice.TenantId);
        foreach (var text in new[] { notice.Title, notice.Body, notice.Link })
        {
            Assert.DoesNotContain("Chidi", text);
            Assert.DoesNotContain("Sulfa", text);
            Assert.DoesNotContain("unconscious", text);
        }

        // A second emergency open the same day is logged (and audited) but doesn't notify again.
        await Json(await GetWithReasonAsync(doctor, $"/api/care/patients/{patientId}", "Still unconscious"));
        Assert.Equal(2, (await AccessLogAsync(patientId)).Count(a => a.Emergency));
        Assert.Equal(1, await NoticeCountAsync(ownerId, clinic.TenantId));

        // Emergency access goes to the audit log on every open, without the reason or anything clinical.
        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == clinic.TenantId && a.Action == "care.emergency_access").ToListAsync(Ct));
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a =>
        {
            Assert.Equal(patientId.ToString(), a.EntityId);
            Assert.DoesNotContain("unconscious", a.AfterJson ?? "");
            Assert.DoesNotContain("Sulfa", a.AfterJson ?? "");
        });

        // The director's access log shows the emergency and its reason, decrypted.
        var entry = (await Json(await clinic.Owner.GetAsync($"/api/care/patients/{patientId}/access-log", Ct))).EnumerateArray().Last();
        Assert.True(entry.GetProperty("emergency").GetBoolean());
        Assert.Equal(reason, entry.GetProperty("reason").GetString());

        // A nurse with a care relationship opens it freely: logged, not an emergency, no new notice. A reason she sends is ignored,
        // so its length isn't checked either.
        await kit.AssignAsync(clinic, patientId, nurseId, CareAssignmentKind.WardNurse);
        await Json(await GetWithReasonAsync(nurse, $"/api/care/patients/{patientId}", new string('x', 301)));
        var log = await AccessLogAsync(patientId);
        Assert.Equal(3, log.Count);
        Assert.False(log[2].Emergency);
        Assert.Null(log[2].Reason);
        Assert.Equal("Nurse", log[2].Role);
        Assert.Equal(1, await NoticeCountAsync(ownerId, clinic.TenantId));

        // An emergency reason longer than the log holds is a field error.
        var second = (await kit.PatientAsync(clinic, "Uzo", "Nwosu")).GetProperty("id").GetGuid();
        await AssertProblemAsync(await GetWithReasonAsync(doctor, $"/api/care/patients/{second}", new string('x', 301)), HttpStatusCode.BadRequest);
        Assert.Empty(await AccessLogAsync(second));

        // Two concurrent first emergency opens of another patient: both logged, one notice.
        await Task.WhenAll(
            GetWithReasonAsync(doctor, $"/api/care/patients/{second}", "Collapsed at the desk"),
            GetWithReasonAsync(doctor, $"/api/care/patients/{second}", "Collapsed at the desk"));
        Assert.Equal(2, (await AccessLogAsync(second)).Count);
        Assert.Equal(2, await NoticeCountAsync(ownerId, clinic.TenantId));

        // Non-clinical staff can't break the glass: a reason still gets them demographics only, and no log row.
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var desk = await Json(await GetWithReasonAsync(frontDesk, $"/api/care/patients/{patientId}", "Curious"));
        Assert.False(desk.TryGetProperty("allergies", out _));
        Assert.Equal(3, (await AccessLogAsync(patientId)).Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaffUser(Guid userId, MemberRole role) : ICurrentUser
    {
        public Guid? UserId => userId;
        public MemberRole? Role => role;
        public string? IpAddress => null;
    }

    /// <summary>Opens a record as <paramref name="userId"/> through <c>RecordGuard</c> with the clock at <paramref name="at"/>.</summary>
    private async Task OpenAtAsync(CareKit.Clinic clinic, Guid userId, Guid patientId, DateTimeOffset at)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ITenantContext>().SetTenant(clinic.TenantId);
        ICurrentUser user = new StaffUser(userId, MemberRole.Staff);
        var access = ActivatorUtilities.CreateInstance<CareAccess>(sp, user);
        TimeProvider clock = new FixedClock(at);
        var guard = ActivatorUtilities.CreateInstance<RecordGuard>(sp, access, user, clock);
        await guard.OpenAsync(patientId, RecordGuard.Areas.Record, "Called to the ward", Ct);
    }

    [Fact]
    public async Task Emergency_notice_is_once_per_staff_patient_and_Lagos_day()
    {
        var clinic = await kit.ClinicAsync("Lagos Day Clinic");
        var ownerId = await OwnerIdAsync(clinic);
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (_, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var patientId = (await kit.PatientAsync(clinic, "Day", "Patient")).GetProperty("id").GetGuid();
        var otherPatientId = (await kit.PatientAsync(clinic, "Other", "Patient")).GetProperty("id").GetGuid();

        // 22:30 and 22:50 UTC on 26 Oct are 23:30 and 23:50 in Lagos: the same Lagos day, one notice.
        await OpenAtAsync(clinic, doctorId, patientId, new DateTimeOffset(2026, 10, 26, 22, 30, 0, TimeSpan.Zero));
        await OpenAtAsync(clinic, doctorId, patientId, new DateTimeOffset(2026, 10, 26, 22, 50, 0, TimeSpan.Zero));
        Assert.Equal(2, (await AccessLogAsync(patientId)).Count);
        Assert.Equal(1, await NoticeCountAsync(ownerId, clinic.TenantId));

        // 23:10 UTC is still 26 Oct in UTC but 00:10 on 27 Oct in Lagos: a new Lagos day, a new notice.
        await OpenAtAsync(clinic, doctorId, patientId, new DateTimeOffset(2026, 10, 26, 23, 10, 0, TimeSpan.Zero));
        Assert.Equal(2, await NoticeCountAsync(ownerId, clinic.TenantId));
        await OpenAtAsync(clinic, doctorId, patientId, new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(2, await NoticeCountAsync(ownerId, clinic.TenantId));

        // Another patient, or another staff member, on the same day: a notice each.
        await OpenAtAsync(clinic, doctorId, otherPatientId, new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(3, await NoticeCountAsync(ownerId, clinic.TenantId));
        await OpenAtAsync(clinic, nurseId, patientId, new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(4, await NoticeCountAsync(ownerId, clinic.TenantId));
        Assert.All(await AccessLogAsync(patientId), a => Assert.True(a.Emergency));
    }

    [Fact]
    public async Task Patient_writes_answer_with_demographics_only()
    {
        var clinic = await kit.ClinicAsync("Write Echo Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var created = await Json(await doctor.PostAsJsonAsync("/api/care/patients",
            new { firstName = "Echo", lastName = "Patient", sex = "Male", dateOfBirth = "1980-01-01" }, Ct));
        var patientId = created.GetProperty("patient").GetProperty("id").GetGuid();
        await kit.AssignAsync(clinic, patientId, doctorId);
        (await doctor.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { "Iodine" }, conditions = new[] { "Diabetes" }, bloodGroup = "O+", genotype = "AA" }, Ct)).EnsureSuccessStatusCode();
        Assert.Empty(await AccessLogAsync(patientId));

        var responses = new List<JsonElement>
        {
            created.GetProperty("patient"),
            await Json(await doctor.PutAsJsonAsync($"/api/care/patients/{patientId}",
                new { firstName = "Echo", lastName = "Patient", sex = "Male", dateOfBirth = "1980-01-01", phone = "08030002222" }, Ct)),
            await Json(await doctor.PostAsync($"/api/care/patients/{patientId}/inactive", null, Ct)),
            await Json(await doctor.PostAsync($"/api/care/patients/{patientId}/active", null, Ct)),
        };
        foreach (var r in responses)
            foreach (var field in new[] { "allergies", "conditions", "bloodGroup", "genotype" })
                Assert.False(r.TryGetProperty(field, out _), $"{field} in a write response: {r}");
        Assert.Empty(await AccessLogAsync(patientId)); // the writes opened nothing
    }

    [Fact]
    public async Task Unreadable_clinical_text_fails_without_leaking()
    {
        var clinic = await kit.ClinicAsync("Broken Key Clinic");
        var patientId = (await kit.PatientAsync(clinic, "Kemi", "Bello")).GetProperty("id").GetGuid();
        await api.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE patients SET allergies = '[\"CfDJ8-not-a-real-ciphertext-Penicillin\"]'::jsonb WHERE id = {patientId}", Ct));

        var response = await clinic.Owner.GetAsync($"/api/care/patients/{patientId}", Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Penicillin", body);
        Assert.DoesNotContain("CfDJ8", body);
        Assert.DoesNotContain("Cryptographic", body);
        Assert.DoesNotContain("Exception", body);
        Assert.Contains("Please try again.", body);
        Assert.Single(await AccessLogAsync(patientId)); // the open was logged before the read failed
    }

    // ---------- Encryption ----------

    [Fact]
    public async Task Clinical_text_is_encrypted_at_rest()
    {
        var text = api.Services.GetRequiredService<ClinicalText>();
        const string note = "Chest pain radiating to the left arm since morning";

        var stored = text.Protect(note);
        Assert.DoesNotContain("Chest pain", stored);
        Assert.Equal(note, text.Read(stored));
        Assert.Null(text.Read(null));
        Assert.Null(text.Read(text.Protect(null)));
        Assert.Null(text.Read(text.Protect("   ")));

        // Allergies and conditions go through ClinicalText: the raw column holds no plain text, and the API reads them back.
        var clinic = await kit.ClinicAsync("Encryption Clinic");
        var patientId = (await kit.PatientAsync(clinic, "Tola", "Ajayi")).GetProperty("id").GetGuid();
        (await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { "Latex gloves" }, conditions = new[] { "Sickle cell disease" }, bloodGroup = "O-", genotype = "SS" }, Ct)).EnsureSuccessStatusCode();

        var raw = await api.WithDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT allergies::text || ' ' || conditions::text AS \"Value\" FROM patients WHERE id = {patientId}").FirstAsync(Ct));
        Assert.DoesNotContain("Latex", raw);
        Assert.DoesNotContain("Sickle", raw);

        var facts = await api.AsTenantAsync<PatientService, PatientClinicalFacts>(clinic.TenantId, s => s.ClinicalFactsAsync(patientId, Ct));
        Assert.Equal(["Latex gloves"], facts.Allergies);
        Assert.Equal(["Sickle cell disease"], facts.Conditions);
        var stored2 = await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.Id == patientId).Select(p => p.Allergies).FirstAsync(Ct));
        Assert.Equal("Latex gloves", text.Read(stored2[0]));
    }

    // ---------- Route walk ----------

    /// <summary>The seeded clinic the route walk calls into: real ids, with clinical data that a leak would show.</summary>
    public sealed record WalkWorld(CareKit.Clinic Clinic, IReadOnlyDictionary<string, Guid> Ids);

    /// <summary>Clinical free-text values seeded into the walk's clinic. A non-clinical body must not contain any of them anywhere.</summary>
    private static readonly string[] ClinicalMarkers =
    [
        "Penicillin", "Hypertension", "rx-item-walk", "Critical SpO2", "High temperature",
        // Task 6: consultation text, a diagnosis and an addendum.
        "Productive cough", "Wheeze walk", "Bronchodilator walk", "Falciparum walk", "Walk addendum",
        // Task 7: a prescription's drug, dose and instructions; a test's description and result.
        "Walkacillin", "Walk dose", "Walk instructions", "Walk smear", "Walk parasites",
        // Task 8: an admission's reason and admitting diagnosis.
        "Walk admit reason", "Walk admitting diagnosis",
        // Task 9: a nursing note, a ward round, an admission drug and a dose note.
        "Walk nursing note", "Walk round note", "Walkdolol", "Walk dose note",
        // Task 10: the discharge summary's sections and an against-advice reason.
        "Walk discharge diagnoses", "Walk treatment course", "Walk condition stable", "Walk take home", "Walk ama reason",
    ];

    /// <summary>Coded clinical values seeded into the walk's clinic (blood group, genotype): no JSON string value may equal one.</summary>
    private static readonly string[] ClinicalCodes = ["AB-", "SC", "B50.9"]; // Task 6: the seeded diagnosis code

    /// <summary>
    /// Clinical tokens. A JSON property name is split into camelCase, snake_case or kebab-case tokens, each token is folded to its
    /// singular (allergies → allergy, diagnoses → diagnosis, vitals → vital), and the name is clinical when any token is in this set.
    /// So <c>chiefComplaint</c>, <c>nursingNotes</c>, <c>dischargeSummary</c>, <c>bloodGroup</c> and <c>resultText</c> all count.
    /// </summary>
    private static readonly HashSet<string> ClinicalTokens = new(StringComparer.Ordinal)
    {
        "note", "result", "diagnosis", "complaint", "history", "examination", "plan", "allergy", "condition", "prescription",
        "medication", "summary", "blood", "genotype", "vital", "dose", "fluid",
        // Task 5 (carry from Task 3): prescription, note and finding words, and the flat vital-sign names, since vitals arrive here.
        "drug", "dosage", "symptom", "finding", "addendum", "assessment", "treatment",
        "temperature", "pulse", "systolic", "diastolic", "spo2", "respiratory", "resp", "bmi",
        // Task 5 fix round 1: the rest of a vital-signs row (weight, height) and its computed flags.
        "weight", "height", "flag", "critical",
        // Task 6: consultations and their addenda (addenda doesn't fold to addendum).
        "consultation", "addenda",
        // Task 7: the rest of a prescription item (dosing) and the order words.
        "strength", "frequency", "route", "instruction", "resulted",
        // Task 7 fix round 1: the allergens a prescription overrode.
        "allergen",
        // Task 9: ward care (nursing notes, ward rounds, fluid millilitres and totals, dose due times).
        "nursing", "round", "ml", "due",
        // Task 10: how a patient died and why they left against advice.
        "death", "ama",
    };

    /// <summary>
    /// Whole property names that are clinical although no token is: a bill line's clinical reference, and SpO2, whose camelCase
    /// <c>spO2</c> splits into "sp" and "o2".
    /// </summary>
    private static readonly HashSet<string> ClinicalNames = new(StringComparer.OrdinalIgnoreCase) { "clinicalRef", "spO2", "asNeeded", "takeHome" }; // Task 9: the chart's PRN drugs; Task 10: take-home instructions

    /// <summary>
    /// Property names that contain a clinical token but carry no clinical data, each with its reason. Keep this list short: every entry
    /// is a hole in the net. A later task adds an entry only for a name that is provably not clinical, for example
    /// <c>["noteKind"] = "the kind of note (consultation, ward round), never its text"</c>.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dispensesDrugs"] = "a clinic setting (prescribed drugs with a price item go on the bill): one yes/no about the clinic, never a patient's drug",
        ["consultationPriceItemId"] = "a clinic setting on GET /setup: the price-list item charged at check-in for a consultation, the same for every patient; never a note",
    };

    /// <summary>
    /// Every GET route under <c>/api/care</c>, keyed "GET {route pattern}", with the real path to call. A route missing here fails the walk:
    /// later tasks add a row for each GET route they map (and seed the ids it needs in <see cref="SeedWalkAsync"/>).
    /// </summary>
    internal static readonly Dictionary<string, Func<WalkWorld, string>> Routes = new()
    {
        ["GET /api/care/setup"] = _ => "/api/care/setup",
        ["GET /api/care/staff"] = _ => "/api/care/staff",
        ["GET /api/care/staff/me"] = _ => "/api/care/staff/me",
        ["GET /api/care/price-list"] = _ => "/api/care/price-list",
        ["GET /api/care/patients"] = _ => "/api/care/patients",
        ["GET /api/care/patients/{id:guid}"] = w => $"/api/care/patients/{w.Ids["patient"]}",
        ["GET /api/care/patients/{id:guid}/access-log"] = w => $"/api/care/patients/{w.Ids["patient"]}/access-log",
        ["GET /api/care/patients/{id:guid}/portal-links"] = w => $"/api/care/patients/{w.Ids["patient"]}/portal-links",
        ["GET /api/care/patients/{id:guid}/invites"] = w => $"/api/care/patients/{w.Ids["patient"]}/invites",
        ["GET /api/care/doctors"] = _ => "/api/care/doctors",
        ["GET /api/care/doctors/{userId:guid}/schedule"] = w => $"/api/care/doctors/{w.Ids["doctor"]}/schedule",
        ["GET /api/care/doctors/{userId:guid}/slots"] = w => $"/api/care/doctors/{w.Ids["doctor"]}/slots?date={WalkDate:yyyy-MM-dd}",
        ["GET /api/care/appointments"] = _ => "/api/care/appointments?date=" + WalkDate.ToString("yyyy-MM-dd"),
        ["GET /api/care/bills"] = _ => "/api/care/bills",
        ["GET /api/care/bills/{id:guid}"] = w => $"/api/care/bills/{w.Ids["bill"]}",
        ["GET /api/care/queue"] = _ => "/api/care/queue?mine=false",
        ["GET /api/care/visits/{id:guid}"] = w => $"/api/care/visits/{w.Ids["visit"]}",
        ["GET /api/care/visits/{id:guid}/vitals"] = w => $"/api/care/visits/{w.Ids["visit"]}/vitals",
        ["GET /api/care/patients/{id:guid}/record"] = w => $"/api/care/patients/{w.Ids["patient"]}/record",
        ["GET /api/care/diagnosis-codes"] = _ => "/api/care/diagnosis-codes?q=malaria",
        // Task 7: the prescription PDF and the result file's link are clinical reads (403 for non-clinical staff). The signed file route
        // carries its own proof and no sign-in; called without a valid signature it must refuse (403).
        ["GET /api/care/prescriptions/{id:guid}/pdf"] = w => $"/api/care/prescriptions/{w.Ids["prescription"]}/pdf",
        ["GET /api/care/tests/{id:guid}/file"] = w => $"/api/care/tests/{w.Ids["test"]}/file",
        ["GET /api/care/result-files/{id:guid}/{expires:long}/{signature}"] = w =>
            $"/api/care/result-files/{w.Ids["test"]}/{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}/forged-signature-forged-signature",
        // Task 8: wards, beds, the bed board and a ward's patients are demographics and bed status for everyone; one admission (reason,
        // admitting diagnosis) is a clinical read.
        ["GET /api/care/wards"] = _ => "/api/care/wards",
        ["GET /api/care/wards/{id:guid}/beds"] = w => $"/api/care/wards/{w.Ids["ward"]}/beds",
        ["GET /api/care/wards/{id:guid}/patients"] = w => $"/api/care/wards/{w.Ids["ward"]}/patients",
        ["GET /api/care/bed-board"] = _ => "/api/care/bed-board",
        ["GET /api/care/admissions/{id:guid}"] = w => $"/api/care/admissions/{w.Ids["admission"]}",
        // Task 9: ward care reads are clinical (403 for non-clinical staff).
        ["GET /api/care/admissions/{id:guid}/nursing-notes"] = w => $"/api/care/admissions/{w.Ids["admission"]}/nursing-notes",
        ["GET /api/care/admissions/{id:guid}/fluids"] = w => $"/api/care/admissions/{w.Ids["admission"]}/fluids",
        ["GET /api/care/admissions/{id:guid}/ward-rounds"] = w => $"/api/care/admissions/{w.Ids["admission"]}/ward-rounds",
        ["GET /api/care/admissions/{id:guid}/medication-chart"] = w => $"/api/care/admissions/{w.Ids["admission"]}/medication-chart",
        // Task 10: the admissions list (leadership and clinical staff; demographics, bed and status for non-clinical callers); the
        // discharge summary and its PDF are clinical reads.
        ["GET /api/care/admissions"] = _ => "/api/care/admissions",
        ["GET /api/care/admissions/{id:guid}/discharge-summary"] = w => $"/api/care/admissions/{w.Ids["admission"]}/discharge-summary",
        ["GET /api/care/admissions/{id:guid}/discharge.pdf"] = w => $"/api/care/admissions/{w.Ids["admission"]}/discharge.pdf",
        // Task 11: the Paystack settings (the director only); a payment's receipt PDF (every member, see NonClinicalFiles).
        ["GET /api/care/settings/paystack"] = _ => "/api/care/settings/paystack",
        ["GET /api/care/payments/{id:guid}/receipt.pdf"] = w => $"/api/care/payments/{w.Ids["payment"]}/receipt.pdf",
        // Task 12: reports (the cashier and leadership; front desk gets 403) and their CSVs; import templates (each kind's screen access).
        ["GET /api/care/reports/takings"] = _ => $"/api/care/reports/takings?from={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1):yyyy-MM-dd}&to={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(1):yyyy-MM-dd}",
        ["GET /api/care/reports/takings.csv"] = _ => $"/api/care/reports/takings.csv?from={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1):yyyy-MM-dd}&to={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(1):yyyy-MM-dd}",
        ["GET /api/care/reports/debtors"] = _ => "/api/care/reports/debtors",
        ["GET /api/care/reports/debtors.csv"] = _ => "/api/care/reports/debtors.csv",
        ["GET /api/care/reports/revenue"] = _ => $"/api/care/reports/revenue?from={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1):yyyy-MM-dd}&to={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(1):yyyy-MM-dd}",
        ["GET /api/care/reports/revenue.csv"] = _ => $"/api/care/reports/revenue.csv?from={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(-1):yyyy-MM-dd}&to={CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(1):yyyy-MM-dd}",
        ["GET /api/care/reports/deposits"] = _ => "/api/care/reports/deposits",
        ["GET /api/care/reports/deposits.csv"] = _ => "/api/care/reports/deposits.csv",
        ["GET /api/care/imports/{kind}/template.xlsx"] = _ => "/api/care/imports/care-patients/template.xlsx",
        ["GET /api/care/imports/{kind}/template.csv"] = _ => "/api/care/imports/care-patients/template.csv",
        // Task 16: the director's list of unsigned drafts (patient number, doctor, dates and age; never note text).
        ["GET /api/care/reports/unsigned-drafts"] = _ => "/api/care/reports/unsigned-drafts",
    };

    /// <summary>
    /// GET routes that may return a non-JSON body to non-clinical staff, each with its reason. Like <see cref="AllowedNames"/>, every entry
    /// is a hole in the net, so only a file that is provably not clinical belongs here.
    /// </summary>
    private static readonly Dictionary<string, string> NonClinicalFiles = new()
    {
        ["GET /api/care/payments/{id:guid}/receipt.pdf"] =
            "a payment receipt: clinic, patient name and number, bill number, amount, method, reference and receipt number, built from " +
            "CareReceiptData (no bill lines, no clinical field; CarePaymentTests.Cashier_sees_no_clinical_refs_on_bills checks its names)",
        // Task 12: report CSVs are the JSON reports' rows (money, methods, price categories, demographics); the walk scans their heading
        // names with the clinical-name scan and their whole text for the clinical markers.
        ["GET /api/care/reports/takings.csv"] = "takings by day and payment method: dates, methods, counts and amounts",
        ["GET /api/care/reports/debtors.csv"] = "debtors: patient number, name, phone, bill count, balance and oldest bill date",
        ["GET /api/care/reports/revenue.csv"] = "revenue by price category (category names only, never item names)",
        ["GET /api/care/reports/deposits.csv"] = "deposits held: patient, ward, bed, admission date and money columns",
        // Import templates hold headings and fill-in notes only (list kinds: no rows). The CSV one is scanned like the reports.
        ["GET /api/care/imports/{kind}/template.xlsx"] = "an empty import template: headings and fill-in notes, no patient rows",
        ["GET /api/care/imports/{kind}/template.csv"] = "an empty import template: the heading line only",
    };

    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>A Lagos date two weeks out, the day the walk's doctor works and has one appointment.</summary>
    private static DateOnly WalkDate => CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(14);

    /// <summary>Seeds the walk's clinic. Later tasks add their entities here (visits, consultations, admissions…) and put the ids in Ids.</summary>
    internal async Task<WalkWorld> SeedWalkAsync()
    {
        var clinic = await kit.ClinicAsync("Route Walk Clinic");
        var ids = new Dictionary<string, Guid>();
        var patientId = (await kit.PatientAsync(clinic, "Walk", "Patient")).GetProperty("id").GetGuid();
        ids["patient"] = patientId;
        (await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{patientId}/clinical-facts",
            new { allergies = new[] { "Penicillin" }, conditions = new[] { "Hypertension" }, bloodGroup = "AB-", genotype = "SC" }, Ct)).EnsureSuccessStatusCode();
        var itemId = await kit.PriceItemAsync(clinic, "Walk consultation", "Consultation", 500_000);
        ids["bill"] = await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b =>
        {
            var bill = await b.OpenBillForVisitAsync(patientId, Guid.NewGuid(), Ct);
            await b.AddLineAsync(bill.Id, itemId, 1, "rx-item-walk", Ct);
            return bill.Id;
        });

        // A doctor who works on WalkDate, with one appointment (a short, non-clinical booking reason).
        var (walkDoctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        ids["doctor"] = doctorId;
        var day = WalkDate;
        (await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule",
            new { blocks = new[] { new { weekday = day.DayOfWeek.ToString(), start = "09:00", end = "10:00", slotMinutes = 15 } } }, Ct)).EnsureSuccessStatusCode();
        var slot = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), CareCalendar.LagosOffset);
        (await clinic.Owner.PostAsJsonAsync("/api/care/appointments",
            new { patientId, doctorUserId = doctorId, start = slot, reason = "Booking walk" }, Ct)).EnsureSuccessStatusCode();

        // A visit in today's queue, triaged with a critical and a high reading (the flags are clinical markers).
        var visitId = (await kit.CheckInAsync(clinic.Owner, patientId)).GetProperty("id").GetGuid();
        ids["visit"] = visitId;
        (await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 38.4, systolic = 128, diastolic = 84, pulse = 90, respRate = 18, spO2 = 85, weightKg = 64, heightCm = 160 },
            urgency = "Urgent", doctorUserId = doctorId,
        }, Ct)).EnsureSuccessStatusCode();

        // Task 6: the visit's doctor sees the patient (a consultation with a coded diagnosis) and signs it; the director adds an addendum.
        var consultationId = (await (await walkDoctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)).Content
            .ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
        ids["consultation"] = consultationId;
        (await walkDoctor.PutAsJsonAsync($"/api/care/consultations/{consultationId}", new
        {
            complaint = "Productive cough for a week", history = "Wheeze walk at night", examination = "Crackles at the right base",
            plan = "Bronchodilator walk and review", diagnoses = new[] { new { text = "Falciparum walk", code = "B50.9", primary = true } },
        }, Ct)).EnsureSuccessStatusCode();
        (await walkDoctor.PostAsync($"/api/care/consultations/{consultationId}/sign", null, Ct)).EnsureSuccessStatusCode();
        (await clinic.Owner.PostAsJsonAsync($"/api/care/notes/consultation/{consultationId}/addenda", new { text = "Walk addendum after review" }, Ct))
            .EnsureSuccessStatusCode();

        // Task 7: the visit's doctor prescribes (a dispensed drug goes on the visit bill under the price item's name) and requests a
        // test; the director records its result with a file.
        var drugItem = await kit.PriceItemAsync(clinic, "Walk tablets", "Drug", 2_000);
        var rx = await walkDoctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, consultationId, allergyAcknowledged = false,
            items = new[] { new { drug = "Walkacillin", strength = "250 mg", dose = "Walk dose", frequency = "BD", durationDays = 5, quantity = 10, route = "Oral", instructions = "Walk instructions", priceItemId = drugItem } },
        }, Ct);
        rx.EnsureSuccessStatusCode();
        ids["prescription"] = (await rx.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
        var test = await walkDoctor.PostAsJsonAsync("/api/care/tests", new { patientId, consultationId, kind = "Lab", description = "Walk smear" }, Ct);
        test.EnsureSuccessStatusCode();
        ids["test"] = (await test.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent("Walk parasites seen"), "text");
            var file = new ByteArrayContent("%PDF-1.4 walk result"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "walk.pdf");
            (await clinic.Owner.PostAsync($"/api/care/tests/{ids["test"]}/result", form, Ct)).EnsureSuccessStatusCode();
        }

        // Task 8: the visit's doctor admits the patient (clinical reason and diagnosis); the director assigns a bed with a deposit; a
        // second patient waits for a bed (the bed board's waiting list).
        ids["ward"] = await kit.WardAsync(clinic, "Walk ward", minDepositMinor: 1_000_000);
        var bedId = await kit.BedAsync(clinic, ids["ward"], "Walk bed 1");
        ids["admission"] = await kit.AdmitAsync(walkDoctor, patientId, "Walk admit reason, vomiting", "Walk admitting diagnosis, malaria", consultationId: consultationId, emergencyReason: null);
        (await kit.AssignBedAsync(clinic.Owner, ids["admission"], bedId, 1_000_000)).EnsureSuccessStatusCode();
        // Task 11: the deposit is the walk's payment (its receipt PDF).
        ids["payment"] = await api.WithDbAsync(db => db.CarePayments.IgnoreQueryFilters().Where(p => p.TenantId == clinic.TenantId && p.IsDeposit).Select(p => p.Id).SingleAsync(Ct));
        var waiting = (await kit.PatientAsync(clinic, "Walk", "Waiting")).GetProperty("id").GetGuid();
        await kit.AdmitAsync(walkDoctor, waiting, "Walk admit reason, fever", "Walk admitting diagnosis, typhoid");

        // Task 9: ward care on the placed admission. The admitting doctor signs a ward round and prescribes from the admission (a Stat
        // dose due now and a PRN drug); the director writes a nursing note and a fluid entry and records the Stat dose with a note.
        var round = await walkDoctor.PostAsJsonAsync($"/api/care/admissions/{ids["admission"]}/ward-rounds", new { note = "Walk round note, settling" }, Ct);
        round.EnsureSuccessStatusCode();
        var roundId = (await round.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
        (await walkDoctor.PostAsync($"/api/care/ward-rounds/{roundId}/sign", null, Ct)).EnsureSuccessStatusCode();
        (await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{ids["admission"]}/nursing-notes", new { note = "Walk nursing note, comfortable" }, Ct))
            .EnsureSuccessStatusCode();
        (await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{ids["admission"]}/fluids", new { direction = "In", kind = "IV", ml = 750 }, Ct))
            .EnsureSuccessStatusCode();
        (await walkDoctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, admissionId = ids["admission"], allergyAcknowledged = false,
            items = new[]
            {
                new { drug = "Walkdolol", dose = "Walk ward dose", frequency = "Stat" },
                new { drug = "Walkdolol syrup", dose = "Walk ward dose", frequency = "PRN" },
            },
        }, Ct)).EnsureSuccessStatusCode();
        var doseId = await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().Where(d => d.AdmissionId == ids["admission"]).Select(d => d.Id).SingleAsync(Ct));
        (await clinic.Owner.PostAsJsonAsync($"/api/care/doses/{doseId}", new { status = "Given", note = "Walk dose note, tolerated" }, Ct)).EnsureSuccessStatusCode();

        // Task 10: the admitting doctor writes and signs the discharge summary and discharges the placed patient home; a third patient
        // leaves against advice (an encrypted reason). The waiting patient stays admitted, so the admissions list holds both kinds.
        var summaryPath = $"/api/care/admissions/{ids["admission"]}/discharge-summary";
        (await walkDoctor.PutAsJsonAsync(summaryPath, new
        {
            diagnoses = "Walk discharge diagnoses", treatment = "Walk treatment course", condition = "Walk condition stable", takeHome = "Walk take home",
            followUp = CareCalendar.Today(DateTimeOffset.UtcNow).AddDays(7),
        }, Ct)).EnsureSuccessStatusCode();
        (await walkDoctor.PostAsync($"{summaryPath}/sign", null, Ct)).EnsureSuccessStatusCode();
        (await walkDoctor.PostAsJsonAsync($"/api/care/admissions/{ids["admission"]}/discharge", new { type = "Home" }, Ct)).EnsureSuccessStatusCode();
        var leaving = (await kit.PatientAsync(clinic, "Walk", "Leaving")).GetProperty("id").GetGuid();
        var leavingAdmission = await kit.AdmitAsync(clinic.Owner, leaving, "Walk admit reason, cough", "Walk admitting diagnosis, pneumonia");
        (await clinic.Owner.PostAsJsonAsync($"/api/care/admissions/{leavingAdmission}/discharge", new { type = "AgainstAdvice", amaReason = "Walk ama reason" }, Ct))
            .EnsureSuccessStatusCode();
        return new WalkWorld(clinic, ids);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    [GeneratedRegex(@"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")]
    private static partial Regex Token();

    /// <summary>The token and its possible singulars (notes → note, doses → dose, allergies → allergy, diagnoses → diagnosis).</summary>
    private static IEnumerable<string> Singulars(string token)
    {
        yield return token;
        if (token.EndsWith('s')) yield return token[..^1];
        if (token.EndsWith("es", StringComparison.Ordinal)) yield return token[..^2];
        if (token.EndsWith("ies", StringComparison.Ordinal)) yield return token[..^3] + "y";
        if (token.EndsWith("ses", StringComparison.Ordinal)) yield return token[..^3] + "sis";
    }

    /// <summary>True when a JSON property name is clinical (by token, or by whole name) and not on the allow-list.</summary>
    internal static bool IsClinicalName(string name) =>
        !AllowedNames.ContainsKey(name)
        && (ClinicalNames.Contains(name)
            || Token().Matches(name).Any(m => Singulars(m.Value.ToLowerInvariant()).Any(ClinicalTokens.Contains)));

    [Fact]
    public void Clinical_name_scan_catches_compounds_and_plurals()
    {
        foreach (var name in new[]
        {
            "complaint", "chiefComplaint", "presenting_complaint", "history", "historyOfPresentIllness", "examinationFindings", "plan", "planText",
            "diagnoses", "diagnosis", "admittingDiagnosis", "allergies", "allergy", "conditions", "prescriptions", "prescription", "medications",
            "result", "results", "resultText", "note", "notes", "nursingNotes", "summary", "dischargeSummary", "bloodGroup", "genotype",
            "vitals", "dose", "doses", "fluidBalance", "clinicalRef",
            "drug", "drugName", "dosage", "symptoms", "presentingSymptom", "findings", "addendum", "addendum_text", "assessment", "treatment",
            "treatmentPlan", "temperatureC", "temperature", "pulse", "systolic", "diastolic", "spO2", "SpO2", "spo2", "respRate",
            "respiratoryRate", "bmi", "weightKg", "heightCm", "flags", "vitalFlags", "critical", "isCritical",
            "consultation", "consultations", "consultationId", "addenda", "noteKind", "dischargeSummaries",
            "strength", "frequency", "route", "instructions", "allergyAcknowledged", "resultedAt", "resultedBy", "resultFileKey", "overriddenAllergens",
            "nursingNote", "wardRounds", "wardRound", "ml", "inMl", "outMl", "balanceMl", "dueAt", "asNeeded", "doses", "medicationChart",
            "takeHome", "deathCause", "deathTime", "amaReason", "unsignedWardRounds", "dischargeSummaryStatus", "medicationSummary", "treatment",
        })
            Assert.True(IsClinicalName(name), name);
        foreach (var name in new[]
        {
            "name", "number", "isClinical", "clinicalRole", "patientPrefix", "status", "totalMinor", "description", "title",
            "stage", "urgency", "minutesWaited", "arrivedAt", "ageYears", "sex", "doctorName", "response", "appointmentId", "dispensesDrugs",
            "consultationPriceItemId", "registrationPriceItemId", "signedAt", "authorName", "authorUserId", "code", "primary",
            "direction", "kind", "recordedAt", "recordedByName", "entries", "date",
            "dischargedAt", "dischargeType", "finalisedAt", "followUp", "admittedAt", "bedLabel",
        })
            Assert.False(IsClinicalName(name), name);
    }

    internal List<(string Route, bool HasMethods)> CareRoutes()
    {
        _ = api.CreateClient();
        return api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            // /api/care itself and below; not /api/care-portal (Task 15's portal has its own walk in CarePortalTests).
            .Where(e => e.RoutePattern.RawText is { } p && (p == "/api/care" || p.StartsWith("/api/care/", StringComparison.Ordinal)))
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>() is { } m
                ? m.HttpMethods.Where(x => x == "GET").Select(_ => ($"GET {e.RoutePattern.RawText}", true))
                : [(e.RoutePattern.RawText!, false)])
            .Distinct().OrderBy(r => r.Item1).ToList();
    }

    [Fact]
    public async Task Non_clinical_roles_see_no_clinical_fields_anywhere()
    {
        var all = CareRoutes();
        var methodless = all.Where(r => !r.HasMethods).Select(r => r.Route).ToList();
        Assert.True(methodless.Count == 0, "These /api/care endpoints accept any method; map them with MapGet/MapPost…: " + string.Join(", ", methodless));
        var routes = all.Select(r => r.Route).ToList();
        var missing = routes.Where(r => !Routes.ContainsKey(r)).ToList();
        Assert.True(missing.Count == 0, "Add these /api/care GET routes to the route-walk table: " + string.Join(", ", missing));
        var stale = Routes.Keys.Where(k => !routes.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "These table rows match no route: " + string.Join(", ", stale));

        var world = await SeedWalkAsync();
        var (frontDesk, _) = await kit.StaffAsync(world.Clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(world.Clinic, "Cashier");
        var (admin, _) = await kit.StaffAsync(world.Clinic, "Admin"); // an admin without a clinical role is non-clinical

        // The seed is visible to clinical staff, so the walk would notice a leak.
        var clinicalView = await (await world.Clinic.Owner.GetAsync($"/api/care/patients/{world.Ids["patient"]}", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Penicillin", clinicalView);
        Assert.Contains("\"AB-\"", clinicalView);
        Assert.Contains(PropertyNames(JsonDocument.Parse(clinicalView).RootElement), IsClinicalName);
        var vitalsView = await (await world.Clinic.Owner.GetAsync($"/api/care/visits/{world.Ids["visit"]}/vitals", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Critical SpO2", vitalsView);
        var vitalNames = PropertyNames(JsonDocument.Parse(vitalsView).RootElement).ToList();
        foreach (var name in new[] { "temperatureC", "systolic", "diastolic", "pulse", "respRate", "spO2", "bmi", "weightKg", "heightCm", "flags", "critical" })
            Assert.True(vitalNames.Contains(name) && IsClinicalName(name), $"{name} missing from the vitals view or not caught by the scan");
        // Task 6: the director's record timeline holds the seeded note, diagnosis and addendum under clinically named fields.
        var recordView = await (await world.Clinic.Owner.GetAsync($"/api/care/patients/{world.Ids["patient"]}/record", Ct)).Content.ReadAsStringAsync(Ct);
        foreach (var marker in new[] { "Productive cough", "Wheeze walk", "Bronchodilator walk", "Falciparum walk", "Walk addendum" })
            Assert.Contains(marker, recordView);
        var recordNames = PropertyNames(JsonDocument.Parse(recordView).RootElement).ToList();
        foreach (var name in new[] { "consultations", "complaint", "history", "examination", "plan", "diagnoses", "addenda", "vitals", "dischargeSummaries" })
            Assert.True(recordNames.Contains(name) && IsClinicalName(name), $"{name} missing from the record view or not caught by the scan");
        // Task 7: the record holds the seeded prescription and test result under clinically named fields; the PDF and file are clinical reads.
        foreach (var marker in new[] { "Walkacillin", "Walk dose", "Walk instructions", "Walk smear", "Walk parasites" })
            Assert.Contains(marker, recordView);
        foreach (var name in new[] { "prescriptions", "drug", "strength", "dose", "frequency", "route", "instructions", "allergyAcknowledged", "overriddenAllergens", "results", "resultText", "resultedAt" })
            Assert.True(recordNames.Contains(name) && IsClinicalName(name), $"{name} missing from the record view or not caught by the scan");
        // Task 8: the record and the admission read hold the reason and admitting diagnosis.
        foreach (var marker in new[] { "Walk admit reason", "Walk admitting diagnosis" }) Assert.Contains(marker, recordView);
        foreach (var name in new[] { "admissions", "admittingDiagnosis" })
            Assert.True(recordNames.Contains(name), $"{name} missing from the record view");
        Assert.True(IsClinicalName("admittingDiagnosis"));
        var admissionView = await (await world.Clinic.Owner.GetAsync($"/api/care/admissions/{world.Ids["admission"]}", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Walk admit reason", admissionView);
        // Task 9: the ward care reads hold the seeded notes, fluid entry and doses under clinically named fields.
        foreach (var (route, markers, names) in new (string, string[], string[])[]
                 {
                     ("nursing-notes", ["Walk nursing note"], ["note"]),
                     ("ward-rounds", ["Walk round note"], ["note", "addenda"]),
                     ("fluids", [], ["ml", "inMl", "outMl", "balanceMl"]),
                     ("medication-chart", ["Walkdolol", "Walk dose note"], ["doses", "drug", "dose", "dueAt", "note", "asNeeded"]),
                 })
        {
            var view = await (await world.Clinic.Owner.GetAsync($"/api/care/admissions/{world.Ids["admission"]}/{route}", Ct)).Content.ReadAsStringAsync(Ct);
            foreach (var marker in markers) Assert.Contains(marker, view);
            var viewNames = PropertyNames(JsonDocument.Parse(view).RootElement).ToList();
            foreach (var name in names)
                Assert.True(viewNames.Contains(name) && IsClinicalName(name), $"{name} missing from the {route} view or not caught by the scan: {view}");
        }
        // Task 10: the record and the summary read hold the discharge summary, the record the ward care, under clinically named fields;
        // the clinical admissions list names how each patient left; the discharge PDF is a clinical read.
        foreach (var marker in new[] { "Walk discharge diagnoses", "Walk treatment course", "Walk condition stable", "Walk take home", "Walk nursing note", "Walk round note" })
            Assert.Contains(marker, recordView);
        foreach (var name in new[] { "dischargeSummaries", "diagnoses", "treatment", "condition", "takeHome", "nursingNotes", "wardRounds", "medicationSummary" })
            Assert.True(recordNames.Contains(name) && IsClinicalName(name), $"{name} missing from the record view or not caught by the scan");
        var summaryView = await (await world.Clinic.Owner.GetAsync($"/api/care/admissions/{world.Ids["admission"]}/discharge-summary", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Walk take home", summaryView);
        var listView = await (await world.Clinic.Owner.GetAsync("/api/care/admissions", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"dischargeSummaryStatus\"", listView);
        Assert.True(IsClinicalName("dischargeSummaryStatus") && IsClinicalName("amaReason") && IsClinicalName("deathCause"));
        Assert.Equal(HttpStatusCode.OK, (await world.Clinic.Owner.GetAsync($"/api/care/admissions/{world.Ids["admission"]}/discharge.pdf", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.Clinic.Owner.GetAsync($"/api/care/prescriptions/{world.Ids["prescription"]}/pdf", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await world.Clinic.Owner.GetAsync($"/api/care/tests/{world.Ids["test"]}/file", Ct)).StatusCode);

        var failures = new List<string>();
        foreach (var route in routes)
        {
            var path = Routes[route](world);
            Assert.DoesNotMatch(Parameter(), path);
            foreach (var (role, client) in new[] { ("FrontDesk", frontDesk), ("Cashier", cashier), ("Admin", admin) })
                await ScanAsync(role, client, route, path, failures);
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// One non-clinical caller on one GET route: a 403, or a body with no clinical field, value or code (a non-JSON body only on an
    /// allow-listed non-clinical file). Task 16's support-view walk uses it too.
    /// </summary>
    internal static async Task ScanAsync(string role, HttpClient client, string route, string path, List<string> failures)
    {
        var response = await client.GetAsync(path, Ct);
        if (response.StatusCode == HttpStatusCode.Forbidden) return;
        var body = await response.Content.ReadAsStringAsync(Ct);
        if (!response.IsSuccessStatusCode)
        {
            failures.Add($"{role} {route} → {(int)response.StatusCode} (expected 403 or a body): {body}");
            return;
        }
        if (NonClinicalFiles.ContainsKey(route) && response.Content.Headers.ContentType?.MediaType is "application/pdf" or XlsxType) return;
        if (NonClinicalFiles.ContainsKey(route) && response.Content.Headers.ContentType?.MediaType == "text/csv")
        {
            var headings = body.TrimStart('﻿').Split("\r\n")[0].Split(',').Select(h => h.Trim('"'));
            var clinicalHeadings = headings.Where(IsClinicalName).ToList();
            if (clinicalHeadings.Count > 0) failures.Add($"{role} {route} → clinical CSV columns {string.Join(", ", clinicalHeadings)}");
            foreach (var marker in ClinicalMarkers.Where(m => body.Contains(m, StringComparison.OrdinalIgnoreCase)))
                failures.Add($"{role} {route} → clinical value {marker}");
            foreach (var code in ClinicalCodes.Where(c => body.Contains($"\"{c}\"", StringComparison.Ordinal)))
                failures.Add($"{role} {route} → clinical code {code}");
            return;
        }
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
        {
            failures.Add($"{role} {route} → {response.Content.Headers.ContentType?.MediaType} (a non-JSON body must be 403 for non-clinical staff)");
            return;
        }
        var root = JsonDocument.Parse(body).RootElement;
        var leaked = PropertyNames(root).Where(IsClinicalName).Distinct().ToList();
        if (leaked.Count > 0) failures.Add($"{role} {route} → clinical fields {string.Join(", ", leaked)}");
        foreach (var marker in ClinicalMarkers.Where(m => body.Contains(m, StringComparison.OrdinalIgnoreCase)))
            failures.Add($"{role} {route} → clinical value {marker}");
        foreach (var code in StringValues(root).Where(ClinicalCodes.Contains).Distinct())
            failures.Add($"{role} {route} → clinical code {code}");
    }

    private static IEnumerable<string> PropertyNames(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    yield return p.Name;
                    foreach (var n in PropertyNames(p.Value)) yield return n;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var n in PropertyNames(item)) yield return n;
                break;
        }
    }

    private static IEnumerable<string> StringValues(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                yield return e.GetString()!;
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    foreach (var s in StringValues(p.Value)) yield return s;
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    foreach (var s in StringValues(item)) yield return s;
                break;
        }
    }
}
