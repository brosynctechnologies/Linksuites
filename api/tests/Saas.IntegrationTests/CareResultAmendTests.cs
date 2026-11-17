using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1, CareLink item 3: amending a test result. The original stays on file, marked entered in error with who, when and the required
/// reason; the corrected result replaces it and waits for a doctor's review again before the portal shows it (with a note that it was
/// corrected). Audited with ids only.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareResultAmendTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? field = null, string? code = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
        if (code is not null) Assert.Equal(code, body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_amended_result_keeps_the_original_and_shows_only_the_correction_in_the_portal()
    {
        var clinic = await kit.ClinicAsync("Amend Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = (await kit.PatientAsync(clinic, "Amendee", "Okafor")).GetProperty("id").GetGuid();
        var admissionId = await kit.AdmitAsync(doctor, patientId);
        var (portal, _) = await kit.PortalAccountAsync(clinic, patientId);
        var testId = (await Json(await doctor.PostAsJsonAsync("/api/care/tests", new { patientId, admissionId, kind = "Lab", description = "Haemoglobin" }, Ct)))
            .GetProperty("id").GetGuid();
        var amendPath = $"/api/care/tests/{testId}/amend";

        // Nothing to amend before a result.
        await AssertProblemAsync(await nurse.PostAsJsonAsync(amendPath, new { text = "Hb 10.0", reason = "Typo" }, Ct), HttpStatusCode.Conflict, code: "test_not_resulted");

        await Json(await nurse.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Hb 6.0 g/dL" }, Ct));
        await Json(await doctor.PostAsync($"/api/care/tests/{testId}/review", null, Ct));
        async Task<List<JsonElement>> PortalResultsAsync() =>
            (await Json(await portal.GetAsync($"/api/care-portal/patients/{patientId}/record", Ct))).GetProperty("results").EnumerateArray().ToList();
        Assert.Equal("Hb 6.0 g/dL", Assert.Single(await PortalResultsAsync()).GetProperty("resultText").GetString());

        // A reason is required; the desk can't amend; another clinic's test is 404.
        await AssertProblemAsync(await nurse.PostAsJsonAsync(amendPath, new { text = "Hb 10.0 g/dL" }, Ct), HttpStatusCode.BadRequest, field: "reason");
        await AssertProblemAsync(await nurse.PostAsJsonAsync(amendPath, new { reason = "Typo" }, Ct), HttpStatusCode.BadRequest, field: "text");
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.PostAsJsonAsync(amendPath, new { text = "Hb 10.0", reason = "Typo" }, Ct)).StatusCode);
        var other = await kit.ClinicAsync("Amend Other Clinic");
        Assert.Equal(HttpStatusCode.NotFound, (await other.Owner.PostAsJsonAsync(amendPath, new { text = "Hb 10.0", reason = "Typo" }, Ct)).StatusCode);

        // The nurse corrects it.
        var amended = await Json(await nurse.PostAsJsonAsync(amendPath, new { text = "Hb 10.0 g/dL", reason = "Sample from another patient" }, Ct));
        Assert.Equal("Hb 10.0 g/dL", amended.GetProperty("resultText").GetString());
        Assert.Equal(nurseId, amended.GetProperty("resultedBy").GetGuid());

        // Staff see the current result (waiting for review again) and the original, marked entered in error with who, when and why.
        var staff = Assert.Single((await Json(await doctor.GetAsync($"/api/care/patients/{patientId}/record", Ct))).GetProperty("results").EnumerateArray());
        Assert.Equal("Hb 10.0 g/dL", staff.GetProperty("resultText").GetString());
        Assert.Equal(JsonValueKind.Null, staff.GetProperty("reviewedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, staff.GetProperty("amendedAt").ValueKind);
        var original = Assert.Single(staff.GetProperty("superseded").EnumerateArray());
        Assert.Equal("Hb 6.0 g/dL", original.GetProperty("resultText").GetString());
        Assert.Equal("Sample from another patient", original.GetProperty("reason").GetString());
        Assert.Equal(nurseId, original.GetProperty("supersededBy").GetGuid());
        Assert.Equal(nurseId, original.GetProperty("resultedBy").GetGuid());
        Assert.Equal(doctorId, original.GetProperty("reviewedBy").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, original.GetProperty("supersededAt").ValueKind);
        Assert.Contains(Assert.IsType<JsonElement>((await Json(await doctor.GetAsync("/api/care/tests/awaiting-review", Ct)))).EnumerateArray(),
            r => r.GetProperty("id").GetGuid() == testId);

        // The portal shows neither until a doctor reviews the correction; then only the correction, marked as corrected.
        Assert.Empty(await PortalResultsAsync());
        await Json(await doctor.PostAsync($"/api/care/tests/{testId}/review", null, Ct));
        var shown = Assert.Single(await PortalResultsAsync());
        Assert.Equal("Hb 10.0 g/dL", shown.GetProperty("resultText").GetString());
        Assert.True(shown.GetProperty("corrected").GetBoolean());
        var portalText = await (await portal.GetAsync($"/api/care-portal/patients/{patientId}/record", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Hb 6.0", portalText);
        Assert.DoesNotContain("another patient", portalText);

        // Audited with ids only; the reason and the values stay out of the audit log.
        var audit = Assert.Single(await api.WithDbAsync(db => db.AuditLogs.Where(a => a.Action == "care.result_amended" && a.EntityId == testId.ToString()).ToListAsync(Ct)));
        Assert.Equal(nurseId, audit.ActorId);
        Assert.DoesNotContain("Hb", audit.AfterJson ?? "");
        Assert.DoesNotContain("another patient", audit.AfterJson ?? "");

        // A second correction keeps both earlier results, newest first.
        await Json(await doctor.PostAsJsonAsync(amendPath, new { text = "Hb 10.4 g/dL", reason = "Analyser recalibrated" }, Ct));
        var twice = Assert.Single((await Json(await doctor.GetAsync($"/api/care/patients/{patientId}/record", Ct))).GetProperty("results").EnumerateArray());
        var history = twice.GetProperty("superseded").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal("Hb 10.0 g/dL", history[0].GetProperty("resultText").GetString());
        Assert.Equal("Hb 6.0 g/dL", history[1].GetProperty("resultText").GetString());
    }
}
