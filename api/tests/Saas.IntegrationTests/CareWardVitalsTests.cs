using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1, CareLink item 2: vital signs on the ward chart. Nursing staff with a care relationship to the admission (and the director) record
/// them with the same checks and flags as triage; reading them is a logged clinical open; a critical reading tells the admitting doctor.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareWardVitalsTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        var body = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, body.GetProperty("detail").GetString());
        if (field is not null) Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"no error on {field}: {text}");
    }

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    [Fact]
    public async Task A_long_stay_chart_shows_the_newest_500_readings()
    {
        var clinic = await kit.ClinicAsync("Long Stay Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var patientId = (await kit.PatientAsync(clinic, "Long", "Stay")).GetProperty("id").GetGuid();
        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        var start = DateTimeOffset.UtcNow.AddHours(-600);
        await api.WithDbAsync(async db =>
        {
            for (var i = 0; i < 520; i++)
                db.VitalSigns.Add(new VitalSigns
                {
                    TenantId = clinic.TenantId, PatientId = patientId, AdmissionId = admissionId, TakenBy = doctorId,
                    TakenAt = start.AddHours(i), Pulse = 60 + i % 40,
                });
            return await db.SaveChangesAsync(Ct);
        });

        var chart = await Json(await doctor.GetAsync($"/api/care/admissions/{admissionId}/vitals", Ct));
        var times = chart.EnumerateArray().Select(v => v.GetProperty("takenAt").GetDateTimeOffset()).ToList();
        Assert.Equal(500, times.Count);
        Assert.True(times.SequenceEqual(times.Order()), "oldest first");
        Assert.True(times[^1] >= start.AddHours(519).AddSeconds(-1), "the newest reading is on the chart");
    }

    [Fact]
    public async Task Ward_staff_chart_vitals_on_an_admission()
    {
        var clinic = await kit.ClinicAsync("Ward Vitals Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (otherNurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var wardId = await kit.WardAsync(clinic, "Vitals ward", nurseUserIds: [nurseId]);
        var bedId = await kit.BedAsync(clinic, wardId, "V1");
        var patientId = (await kit.PatientAsync(clinic, "Chart", "Patient")).GetProperty("id").GetGuid();
        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        var path = $"/api/care/admissions/{admissionId}/vitals";

        // The ward's nurse records a set: the same validation and flags as triage; the answer echoes it.
        await AssertProblemAsync(await nurse.PostAsJsonAsync(path, new { vitals = new { } }, Ct), HttpStatusCode.BadRequest, field: "vitals");
        await AssertProblemAsync(await nurse.PostAsJsonAsync(path, new { vitals = new { spO2 = 120 } }, Ct), HttpStatusCode.BadRequest, field: "vitals.spO2");
        var noticesBefore = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == doctorId && n.Kind == VisitService.CriticalNoticeKind, Ct));
        var set = await Json(await nurse.PostAsJsonAsync(path, new { vitals = new { temperatureC = 39.6, pulse = 128, spO2 = 85, weightKg = 70, heightCm = 175 } }, Ct));
        Assert.Equal(admissionId, set.GetProperty("admissionId").GetGuid());
        Assert.Equal(nurseId, set.GetProperty("takenBy").GetGuid());
        Assert.True(set.GetProperty("critical").GetBoolean());
        Assert.NotEmpty(set.GetProperty("flags").EnumerateArray());
        Assert.Equal(22.9m, set.GetProperty("bmi").GetDecimal());
        // A critical reading tells the admitting doctor in-app, with no value.
        var notices = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().Where(n => n.UserId == doctorId && n.Kind == VisitService.CriticalNoticeKind).ToListAsync(Ct));
        Assert.Equal(noticesBefore + 1, notices.Count);
        Assert.DoesNotContain("39", notices[^1].Body);

        // Writing is not an open: no access-log row for the nurse (she has a care relationship).
        var logs = (await AccessLogAsync(patientId)).Count;
        await Json(await doctor.PostAsJsonAsync(path, new { vitals = new { systolic = 120, diastolic = 80 } }, Ct));
        Assert.Equal(logs, (await AccessLogAsync(patientId)).Count);

        // A nurse not on the ward, the desk and the cashier can't write.
        await AssertProblemAsync(await otherNurse.PostAsJsonAsync(path, new { vitals = new { pulse = 80 } }, Ct), HttpStatusCode.Forbidden, WardCareService.NotOnWard);
        await AssertProblemAsync(await desk.PostAsJsonAsync(path, new { vitals = new { pulse = 80 } }, Ct), HttpStatusCode.Forbidden, CareAccess.ClinicalRefused);

        // Reading the chart is a logged clinical open, oldest first.
        var chart = await Json(await nurse.GetAsync(path, Ct));
        Assert.Equal(2, chart.GetArrayLength());
        Assert.Equal(nurseId, chart[0].GetProperty("takenBy").GetGuid());
        Assert.Equal(120, chart[1].GetProperty("systolic").GetInt32());
        var last = (await AccessLogAsync(patientId))[^1];
        Assert.Equal("vitals", last.Area);
        Assert.False(last.Emergency);
        await AssertProblemAsync(await desk.GetAsync(path, Ct), HttpStatusCode.Forbidden, CareAccess.ClinicalRefused);
        // An unrelated nurse needs a reason (emergency access).
        Assert.Equal(HttpStatusCode.Conflict, (await otherNurse.GetAsync(path, Ct)).StatusCode);

        // The visit's vitals stay separate: an admission set has no visit.
        Assert.All(chart.EnumerateArray(), v => Assert.Equal(JsonValueKind.Null, v.GetProperty("visitId").ValueKind));

        // Another clinic's admission is 404; a discharged admission takes no more.
        var other = await kit.ClinicAsync("Ward Vitals Other");
        await AssertProblemAsync(await other.Owner.PostAsJsonAsync(path, new { vitals = new { pulse = 80 } }, Ct), HttpStatusCode.NotFound);
        await AssertProblemAsync(await other.Owner.GetAsync(path, Ct), HttpStatusCode.NotFound);
        await Json(await doctor.PostAsJsonAsync($"/api/care/admissions/{admissionId}/discharge", new { type = "AgainstAdvice", amaReason = "Left" }, Ct));
        await AssertProblemAsync(await nurse.PostAsJsonAsync(path, new { vitals = new { pulse = 80 } }, Ct), HttpStatusCode.Conflict, AdmissionService.Discharged);
    }
}
