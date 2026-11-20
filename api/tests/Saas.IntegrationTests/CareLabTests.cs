using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 4, CareLink module 4c item 1: the lab. A catalogue per clinic (reference ranges by sex and age band, a turnaround target, a price
/// item); ordering from it; sample collection with a numbered label; structured results flagged against the patient's range (a critical
/// one tells the ordering doctor); amendments; the doctor's review still releasing the result to the portal; turnaround; the worklist; the
/// lab scientist role; department reads logged; tenant isolation.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareLabTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

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
        CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Lab, Guid LabId, HttpClient Desk, Guid PatientId, Guid ConsultationId,
        Guid LabTestId, Guid PriceItemId);

    /// <summary>A Full blood count on the catalogue: haemoglobin (ranges for women, men and children; critical limits) and a text analyte.</summary>
    private static object FullBloodCount(Guid priceItemId, string name = "Full blood count") => new
    {
        name, sampleType = "Blood (EDTA)", priceItemId, turnaroundHours = 4,
        analytes = new object[]
        {
            new
            {
                name = "Haemoglobin", unit = "g/dL",
                ranges = new object[]
                {
                    new { sex = "Female", minAgeYears = 18, low = 12m, high = 16m, criticalLow = 7m, criticalHigh = 20m },
                    new { sex = "Male", minAgeYears = 18, low = 13m, high = 17m, criticalLow = 7m, criticalHigh = 20m },
                    new { maxAgeYears = 18, low = 11m, high = 14m },
                },
            },
            new { name = "Malaria parasites", unit = (string?)null, ranges = Array.Empty<object>() },
        },
    };

    /// <summary>A clinic with a doctor, a lab scientist and the front desk; an adult woman triaged for the doctor, whose consultation is open; a catalogue test.</summary>
    private async Task<World> WorldAsync(string name)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (lab, labId) = await kit.StaffAsync(clinic, "LabScientist");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = Id(await kit.PatientAsync(clinic, "Amaka", "Eze", "1990-04-12"));
        var visitId = Id(await kit.CheckInAsync(desk, patientId));
        await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 37.0, systolic = 118, diastolic = 76, pulse = 80, respRate = 16, spO2 = 98 }, urgency = "Routine", doctorUserId = doctorId,
        }, Ct));
        var consultationId = Id(await Json(await doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        var priceItemId = await kit.PriceItemAsync(clinic, "FBC panel", "Test", 450_000);
        var labTestId = Id(await Json(await lab.PostAsJsonAsync("/api/care/lab/tests", FullBloodCount(priceItemId), Ct)));
        return new World(clinic, doctor, doctorId, lab, labId, desk, patientId, consultationId, labTestId, priceItemId);
    }

    private async Task<Guid> OrderAsync(World w) => Id(await Json(await w.Doctor.PostAsJsonAsync("/api/care/tests",
        new { patientId = w.PatientId, consultationId = w.ConsultationId, labTestId = w.LabTestId }, Ct)));

    private async Task<(Guid Haemoglobin, Guid Malaria)> AnalytesAsync(World w)
    {
        var tests = await Json(await w.Lab.GetAsync("/api/care/lab/tests", Ct));
        var analytes = tests.EnumerateArray().Single(t => Id(t) == w.LabTestId).GetProperty("analytes").EnumerateArray().ToList();
        return (Id(analytes[0]), Id(analytes[1]));
    }

    private Task<List<RecordAccess>> AccessLogAsync(Guid patientId) =>
        api.WithDbAsync(db => db.RecordAccesses.IgnoreQueryFilters().Where(a => a.PatientId == patientId).OrderBy(a => a.At).ToListAsync(Ct));

    // ---------- Catalogue ----------

    [Fact]
    public async Task Catalogue_is_validated_and_edited_by_lab_and_leadership()
    {
        var w = await WorldAsync("Lab Catalogue Clinic");
        var catalogue = await Json(await w.Desk.GetAsync("/api/care/lab/tests", Ct)); // every member reads it
        var test = Assert.Single(catalogue.EnumerateArray());
        Assert.Equal(("Full blood count", "Blood (EDTA)", 4, "FBC panel", 450_000L),
            (test.GetProperty("name").GetString(), test.GetProperty("sampleType").GetString(), test.GetProperty("turnaroundHours").GetInt32(),
             test.GetProperty("priceItemName").GetString(), test.GetProperty("priceMinor").GetInt64()));
        Assert.Equal(3, test.GetProperty("analytes")[0].GetProperty("ranges").GetArrayLength());

        // The front desk and a doctor can't change it; the director can. A duplicate name is a 409.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Desk.PostAsJsonAsync("/api/care/lab/tests", FullBloodCount(w.PriceItemId, "Desk test"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Doctor.PostAsJsonAsync("/api/care/lab/tests", FullBloodCount(w.PriceItemId, "Doctor test"), Ct)).StatusCode);
        await AssertProblemAsync(await w.Clinic.Owner.PostAsJsonAsync("/api/care/lab/tests", FullBloodCount(w.PriceItemId), Ct), HttpStatusCode.Conflict, "lab_test_exists");

        // Bad ranges are refused field by field.
        foreach (var (range, field) in new (object, string)[]
                 {
                     (new { low = 10m, high = 5m }, "analytes[0].ranges[0].high"),
                     (new { minAgeYears = 30, maxAgeYears = 20, low = 1m }, "analytes[0].ranges[0].maxAgeYears"),
                     (new { low = 10m, high = 20m, criticalLow = 12m }, "analytes[0].ranges[0].criticalLow"),
                     (new { low = 10m, high = 20m, criticalHigh = 15m }, "analytes[0].ranges[0].criticalHigh"),
                     (new { sex = (string?)null }, "analytes[0].ranges[0].low"),
                 })
            await AssertProblemAsync(await w.Lab.PostAsJsonAsync("/api/care/lab/tests",
                new { name = "Bad", sampleType = "Blood", analytes = new[] { new { name = "X", ranges = new[] { range } } } }, Ct), HttpStatusCode.BadRequest, field: field);
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync("/api/care/lab/tests", new { name = "Empty", sampleType = "Blood", analytes = Array.Empty<object>() }, Ct),
            HttpStatusCode.BadRequest, field: "analytes");

        // An edit keeps the analytes sent with their id, adds new ones and drops the rest.
        var hb = test.GetProperty("analytes")[0];
        var edited = await Json(await w.Clinic.Owner.PutAsJsonAsync($"/api/care/lab/tests/{w.LabTestId}", new
        {
            name = "Full blood count", sampleType = "Blood (EDTA)", priceItemId = w.PriceItemId, turnaroundHours = 6,
            analytes = new object[] { new { id = Id(hb), name = "Haemoglobin", unit = "g/dL", ranges = new[] { new { low = 12m, high = 16m } } }, new { name = "Platelets", unit = "x10^9/L" } },
        }, Ct));
        Assert.Equal(6, edited.GetProperty("turnaroundHours").GetInt32());
        Assert.Equal(["Haemoglobin", "Platelets"], edited.GetProperty("analytes").EnumerateArray().Select(a => a.GetProperty("name").GetString()));
        Assert.Equal(Id(hb), Id(edited.GetProperty("analytes")[0]));
    }

    // ---------- The order's life ----------

    [Fact]
    public async Task Lab_order_from_catalogue_to_review_with_flags_and_turnaround()
    {
        var w = await WorldAsync("Lab Flow Clinic");
        var testId = await OrderAsync(w);
        var (hb, mp) = await AnalytesAsync(w);

        // The order takes the catalogue's name and price: the FBC panel is on the visit's bill.
        var stored = await api.WithDbAsync(db => db.TestRequests.IgnoreQueryFilters().FirstAsync(t => t.Id == testId, Ct));
        Assert.Equal((w.LabTestId, TestKind.Lab, w.PriceItemId), (stored.LabTestId, stored.Kind, stored.PriceItemId));
        Assert.True(await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters().AnyAsync(l => l.PriceItemId == w.PriceItemId && l.ClinicalRef == testId.ToString(), Ct)));

        // The worklist shows it waiting for its sample, with the catalogue name and no description or result.
        var worklist = await Json(await w.Lab.GetAsync("/api/care/lab/worklist", Ct));
        var row = Assert.Single(worklist.EnumerateArray());
        Assert.Equal(("AwaitingSample", "Full blood count", "Blood (EDTA)", 36), (row.GetProperty("stage").GetString(), row.GetProperty("testName").GetString(),
            row.GetProperty("sampleType").GetString(), row.GetProperty("ageYears").GetInt32()));
        Assert.False(row.TryGetProperty("description", out _));

        // Results need the sample first; collection numbers it and the label prints.
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = hb, value = "13" }, new { analyteId = mp, value = "Not seen" } } }, Ct), HttpStatusCode.Conflict, "sample_not_collected");
        await AssertProblemAsync(await w.Lab.GetAsync($"/api/care/lab/orders/{testId}/label.pdf", Ct), HttpStatusCode.Conflict, "sample_not_collected");
        var sample = await Json(await w.Lab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct));
        Assert.Matches(@"^S\d{4}-00001$", sample.GetProperty("sampleNumber").GetString());
        await AssertProblemAsync(await w.Lab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct), HttpStatusCode.Conflict, "sample_collected");
        var label = await w.Lab.GetAsync($"/api/care/lab/orders/{testId}/label.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, label.StatusCode);
        Assert.Equal("application/pdf", label.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await label.Content.ReadAsByteArrayAsync(Ct))[..4]);

        // Opening the order shows the woman's range (not the man's or the child's).
        var order = await Json(await w.Lab.GetAsync($"/api/care/lab/orders/{testId}", Ct));
        var hbRange = order.GetProperty("analytes")[0];
        Assert.Equal((12m, 16m, 7m, "12-16"), (hbRange.GetProperty("low").GetDecimal(), hbRange.GetProperty("high").GetDecimal(),
            hbRange.GetProperty("criticalLow").GetDecimal(), hbRange.GetProperty("rangeText").GetString()));
        Assert.Equal("AwaitingResult", order.GetProperty("stage").GetString());

        // Every value is needed, numbers where the analyte has ranges, each once.
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results", new { values = new[] { new { analyteId = hb, value = "13" } } }, Ct),
            HttpStatusCode.BadRequest, field: "values");
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = hb, value = "low-ish" }, new { analyteId = mp, value = "Not seen" } } }, Ct), HttpStatusCode.BadRequest, field: "values[0].value");
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = Guid.NewGuid(), value = "1" } } }, Ct), HttpStatusCode.BadRequest, field: "values[0].analyteId");

        var entered = await Json(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = hb, value = "9.1" }, new { analyteId = mp, value = "Not seen" } }, comment = "Slight haemolysis" }, Ct));
        Assert.Equal(["Low", "None"], entered.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("flag").GetString()));
        Assert.False(entered.GetProperty("anyCritical").GetBoolean());
        Assert.Equal("Haemoglobin: 9.1 g/dL (low; ref 12-16)\nMalaria parasites: Not seen\nComment: Slight haemolysis", entered.GetProperty("resultText").GetString());
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = hb, value = "9.1" }, new { analyteId = mp, value = "Not seen" } } }, Ct), HttpStatusCode.Conflict, "test_resulted");

        // The summary is the test's result (encrypted at rest): the doctor reviews it as any result; text values are encrypted too.
        var raw = await api.WithDbAsync(db => db.TestRequests.IgnoreQueryFilters().Where(t => t.Id == testId).Select(t => t.ResultText).FirstAsync(Ct));
        Assert.DoesNotContain("Haemoglobin", raw);
        var rawText = await api.WithDbAsync(db => db.LabResultValues.IgnoreQueryFilters().Where(v => v.TestRequestId == testId && v.NumericValue == null).Select(v => v.TextValue).FirstAsync(Ct));
        Assert.DoesNotContain("Not seen", rawText);
        var awaiting = await Json(await w.Doctor.GetAsync("/api/care/tests/awaiting-review", Ct));
        Assert.Contains(awaiting.EnumerateArray(), a => Id(a) == testId);
        Assert.Equal("AwaitingReview", (await Json(await w.Lab.GetAsync("/api/care/lab/worklist", Ct)))[0].GetProperty("stage").GetString());
        await Json(await w.Doctor.PostAsync($"/api/care/tests/{testId}/review", null, Ct));
        Assert.Empty((await Json(await w.Lab.GetAsync("/api/care/lab/worklist", Ct))).EnumerateArray());
        Assert.Single((await Json(await w.Lab.GetAsync("/api/care/lab/worklist?stage=Done", Ct))).EnumerateArray());

        // The record shows the summary to the doctor.
        var record = await (await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("Haemoglobin: 9.1 g/dL (low; ref 12-16)", JsonDocument.Parse(record).RootElement.ToString());

        // Turnaround counts the order at every step, within the 4-hour target.
        var report = await Json(await w.Clinic.Owner.GetAsync("/api/care/lab/turnaround", Ct));
        var fbc = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal(("Full blood count", 4, 1, 1, 1, 1, 1, 0), (fbc.GetProperty("name").GetString(), fbc.GetProperty("targetHours").GetInt32(),
            fbc.GetProperty("ordered").GetInt32(), fbc.GetProperty("collected").GetInt32(), fbc.GetProperty("reported").GetInt32(),
            fbc.GetProperty("reviewed").GetInt32(), fbc.GetProperty("withinTarget").GetInt32(), fbc.GetProperty("late").GetInt32()));
        Assert.Equal(JsonValueKind.Number, fbc.GetProperty("avgOrderToReportMinutes").ValueKind);
        Assert.Equal(1, report.GetProperty("total").GetProperty("ordered").GetInt32());
        // The desk can't read it; the lab can.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Desk.GetAsync("/api/care/lab/turnaround", Ct)).StatusCode);
        await Json(await w.Lab.GetAsync("/api/care/lab/turnaround", Ct));
    }

    [Fact]
    public async Task Critical_value_tells_the_ordering_doctor_and_a_child_gets_the_childs_range()
    {
        var w = await WorldAsync("Lab Critical Clinic");
        var testId = await OrderAsync(w);
        var (hb, mp) = await AnalytesAsync(w);
        await Json(await w.Lab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct));
        var entered = await Json(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = hb, value = "6.2" }, new { analyteId = mp, value = "Seen ++" } } }, Ct));
        Assert.Equal("CriticalLow", entered.GetProperty("values")[0].GetProperty("flag").GetString());
        Assert.True(entered.GetProperty("anyCritical").GetBoolean());
        var notice = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().SingleAsync(n => n.UserId == w.DoctorId && n.Kind == CareNotices.CriticalResult, Ct));
        Assert.Equal("A lab test you ordered has a critical result.", notice.Body); // no test, no value
        Assert.DoesNotContain("6.2", notice.Title + notice.Body + notice.Link);

        // A 10-year-old boy is judged against the children's band (11-14), so 13.5 is normal and 10.5 low.
        var child = Id(await kit.PatientAsync(w.Clinic, "Tobi", "Eze", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10).ToString("yyyy-MM-dd"), sex: "Male"));
        var visitId = Id(await kit.CheckInAsync(w.Desk, child));
        await Json(await w.Clinic.Owner.PostAsJsonAsync($"/api/care/visits/{visitId}/triage", new
        {
            vitals = new { temperatureC = 37.0, systolic = 100, diastolic = 60, pulse = 90, respRate = 20, spO2 = 98 }, urgency = "Routine", doctorUserId = w.DoctorId,
        }, Ct));
        var consultation = Id(await Json(await w.Doctor.PostAsync($"/api/care/visits/{visitId}/consultation", null, Ct)));
        var childTest = Id(await Json(await w.Doctor.PostAsJsonAsync("/api/care/tests", new { patientId = child, consultationId = consultation, labTestId = w.LabTestId }, Ct)));
        await Json(await w.Lab.PostAsync($"/api/care/lab/orders/{childTest}/collect", null, Ct));
        var childResult = await Json(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{childTest}/results",
            new { values = new[] { new { analyteId = hb, value = "10.5" }, new { analyteId = mp, value = "Not seen" } } }, Ct));
        Assert.Equal(("Low", 11m, 14m), (childResult.GetProperty("values")[0].GetProperty("flag").GetString(),
            childResult.GetProperty("values")[0].GetProperty("low").GetDecimal(), childResult.GetProperty("values")[0].GetProperty("high").GetDecimal()));
        // The doctor entering a critical value themself isn't told.
        var before = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters().CountAsync(n => n.UserId == w.DoctorId && n.Kind == CareNotices.CriticalResult, Ct));
        Assert.Equal(1, before);
    }

    [Fact]
    public async Task Amending_keeps_the_old_values_and_waits_for_review_again()
    {
        var w = await WorldAsync("Lab Amend Clinic");
        var testId = await OrderAsync(w);
        var (hb, mp) = await AnalytesAsync(w);
        await Json(await w.Lab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct));
        await Json(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results",
            new { values = new[] { new { analyteId = hb, value = "19" }, new { analyteId = mp, value = "Not seen" } } }, Ct));
        await Json(await w.Doctor.PostAsync($"/api/care/tests/{testId}/review", null, Ct));

        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/amend",
            new { values = new[] { new { analyteId = hb, value = "13" }, new { analyteId = mp, value = "Not seen" } } }, Ct), HttpStatusCode.BadRequest, field: "reason");
        var amended = await Json(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/amend",
            new { values = new[] { new { analyteId = hb, value = "13" }, new { analyteId = mp, value = "Not seen" } }, reason = "Transcribed from the wrong sample" }, Ct));
        Assert.Equal("Normal", amended.GetProperty("values")[0].GetProperty("flag").GetString());

        var test = await api.WithDbAsync(db => db.TestRequests.IgnoreQueryFilters().FirstAsync(t => t.Id == testId, Ct));
        Assert.Null(test.ReviewedAt); // waits for review again
        Assert.NotNull(test.FirstReviewedAt); // the turnaround keeps the first review
        Assert.NotNull(test.AmendedAt);
        Assert.Single(await api.WithDbAsync(db => db.TestResultAmendments.IgnoreQueryFilters().Where(a => a.TestRequestId == testId).ToListAsync(Ct)));
        var order = await Json(await w.Lab.GetAsync($"/api/care/lab/orders/{testId}", Ct));
        var values = order.GetProperty("values").EnumerateArray().ToList();
        Assert.Equal(4, values.Count);
        Assert.Equal([false, false, true, true], values.Select(v => v.GetProperty("superseded").GetBoolean()));
        Assert.Equal("19", values[2].GetProperty("value").GetString());

        // A text correction through the tests route supersedes the structured values too.
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/tests/{testId}/amend", new { text = "Haemoglobin 13.2 g/dL on repeat", reason = "Repeat run" }, Ct));
        Assert.Equal(0, await api.WithDbAsync(db => db.LabResultValues.IgnoreQueryFilters().CountAsync(v => v.TestRequestId == testId && v.SupersededAt == null, Ct)));
    }

    // ---------- Roles, logging and isolation ----------

    [Fact]
    public async Task Lab_scientist_works_the_lab_and_nothing_else_clinical()
    {
        var w = await WorldAsync("Lab Role Clinic");
        var testId = await OrderAsync(w);
        var me = await Json(await w.Lab.GetAsync("/api/care/staff/me", Ct));
        Assert.Equal(("LabScientist", false, true, true, false), (me.GetProperty("clinicalRole").GetString(), me.GetProperty("isClinical").GetBoolean(),
            me.GetProperty("canWorkLab").GetBoolean(), me.GetProperty("canEditLabCatalogue").GetBoolean(), me.GetProperty("canDispense").GetBoolean()));

        // The record, vitals and ordering stay clinical; the front desk and the cashier get nothing from the lab.
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Lab.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Lab.PostAsJsonAsync("/api/care/tests",
            new { patientId = w.PatientId, consultationId = w.ConsultationId, labTestId = w.LabTestId }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Lab.PostAsJsonAsync($"/api/care/tests/{testId}/review", new { }, Ct)).StatusCode);
        var (cashier, _) = await kit.StaffAsync(w.Clinic, "Cashier");
        foreach (var client in new[] { w.Desk, cashier })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/care/lab/worklist", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/care/lab/orders/{testId}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct)).StatusCode);
        }

        // A lab scientist records a lab test's result as a file or text, but not an imaging test's.
        var imaging = Id(await Json(await w.Doctor.PostAsJsonAsync("/api/care/tests",
            new { patientId = w.PatientId, consultationId = w.ConsultationId, kind = "Imaging", description = "Chest X-ray" }, Ct)));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Lab.PostAsJsonAsync($"/api/care/tests/{imaging}/result", new { text = "Clear" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Lab.GetAsync($"/api/care/lab/orders/{imaging}", Ct)).StatusCode); // not a lab order
        var smear = Id(await Json(await w.Doctor.PostAsJsonAsync("/api/care/tests",
            new { patientId = w.PatientId, consultationId = w.ConsultationId, kind = "Lab", description = "Widal test" }, Ct)));
        await Json(await w.Lab.PostAsJsonAsync($"/api/care/tests/{smear}/result", new { text = "Not reactive" }, Ct));
        // A free-text lab order has no catalogue values to enter.
        await Json(await w.Lab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct));
        await AssertProblemAsync(await w.Lab.PostAsJsonAsync($"/api/care/lab/orders/{smear}/results",
            new { values = new[] { new { analyteId = Guid.NewGuid(), value = "1" } } }, Ct), HttpStatusCode.Conflict, "not_catalogue");
    }

    [Fact]
    public async Task Opening_an_order_and_its_label_is_logged_as_a_department_read()
    {
        var w = await WorldAsync("Lab Log Clinic");
        var testId = await OrderAsync(w);
        await Json(await w.Lab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct));
        var before = (await AccessLogAsync(w.PatientId)).Count;
        await Json(await w.Lab.GetAsync($"/api/care/lab/orders/{testId}", Ct));
        (await w.Lab.GetAsync($"/api/care/lab/orders/{testId}/label.pdf", Ct)).EnsureSuccessStatusCode();
        var log = (await AccessLogAsync(w.PatientId)).Skip(before).ToList();
        Assert.Equal(["lab", "pdf"], log.Select(l => l.Area));
        Assert.All(log, l => Assert.Equal((w.LabId, "Lab scientist", false), (l.StaffUserId, l.Role, l.Emergency)));
        // The worklist opens nothing.
        await Json(await w.Lab.GetAsync("/api/care/lab/worklist", Ct));
        Assert.Equal(before + 2, (await AccessLogAsync(w.PatientId)).Count);
        // The director's access log names the area.
        var directorLog = await Json(await w.Clinic.Owner.GetAsync($"/api/care/patients/{w.PatientId}/access-log", Ct));
        Assert.Contains(directorLog.EnumerateArray(), e => e.GetProperty("areaLabel").GetString() == "Lab order" && e.GetProperty("who").GetString()!.EndsWith(", Lab scientist"));
    }

    [Fact]
    public async Task Another_clinics_lab_ids_are_404()
    {
        var w = await WorldAsync("Lab Home Clinic");
        var testId = await OrderAsync(w);
        var (hb, _) = await AnalytesAsync(w);
        var other = await kit.ClinicAsync("Lab Other Clinic");
        var (otherLab, _) = await kit.StaffAsync(other, "LabScientist");
        foreach (var r in new[]
                 {
                     await otherLab.GetAsync($"/api/care/lab/orders/{testId}", Ct),
                     await otherLab.PostAsync($"/api/care/lab/orders/{testId}/collect", null, Ct),
                     await otherLab.GetAsync($"/api/care/lab/orders/{testId}/label.pdf", Ct),
                     await otherLab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/results", new { values = new[] { new { analyteId = hb, value = "1" } } }, Ct),
                     await otherLab.PostAsJsonAsync($"/api/care/lab/orders/{testId}/amend", new { values = new[] { new { analyteId = hb, value = "1" } }, reason = "x" }, Ct),
                     await otherLab.PutAsJsonAsync($"/api/care/lab/tests/{w.LabTestId}", FullBloodCount(w.PriceItemId), Ct),
                 })
            Assert.True(r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        Assert.Empty((await Json(await otherLab.GetAsync("/api/care/lab/worklist", Ct))).EnumerateArray());
        Assert.Empty((await Json(await otherLab.GetAsync("/api/care/lab/tests", Ct))).EnumerateArray());
        // Ordering another clinic's catalogue test is refused.
        await AssertProblemAsync(await w.Doctor.PostAsJsonAsync("/api/care/tests", new { patientId = w.PatientId, consultationId = w.ConsultationId,
            labTestId = Id(await Json(await otherLab.PostAsJsonAsync("/api/care/lab/tests", new { name = "Other FBC", sampleType = "Blood", analytes = new[] { new { name = "Hb" } } }, Ct))) }, Ct),
            HttpStatusCode.BadRequest, field: "labTestId");
    }
}
