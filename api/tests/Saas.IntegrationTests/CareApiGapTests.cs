using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Care;
using Saas.Domain.Care;

namespace Saas.IntegrationTests;

/// <summary>
/// Wave 1, CareLink items 5 and 6 (staff side): WhatsApp reminder links on appointments and the queue, recording an as-needed (PRN) dose,
/// bill search and paging, the pre-register duplicate check, and the bed day on bill lines.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareApiGapTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private static DateOnly Today => CareCalendar.Today(DateTimeOffset.UtcNow);

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

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    // ---------- Item 5: WhatsApp reminders ----------

    [Fact]
    public async Task Appointments_and_the_queue_offer_a_whatsapp_reminder()
    {
        var clinic = await kit.ClinicAsync("Reminder Link Clinic");
        var (_, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/doctors/{doctorId}/schedule", new
        {
            blocks = Enum.GetValues<DayOfWeek>().Select(d => new { weekday = d.ToString(), start = "07:00", end = "21:00", slotMinutes = 15 }).ToArray(),
        }, Ct));
        await Json(await clinic.Owner.PutAsJsonAsync("/api/care/settings/profile", new { name = "Reminder Link Clinic" }, Ct));
        var ada = Id(await kit.PatientAsync(clinic, "Ada", "Phone", phone: "08031112222"));
        var noPhone = Id(await kit.PatientAsync(clinic, "Bayo", "Nophone", "1985-05-05"));
        var day = Today.AddDays(2);
        var slots = (await Json(await desk.GetAsync($"/api/care/doctors/{doctorId}/slots?date={day:yyyy-MM-dd}", Ct)))
            .EnumerateArray().Select(s => s.GetProperty("start").GetDateTimeOffset()).ToList();
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = ada, doctorUserId = doctorId, start = slots[0], reason = "Painful knee" }, Ct));
        await Json(await desk.PostAsJsonAsync("/api/care/appointments", new { patientId = noPhone, doctorUserId = doctorId, start = slots[1] }, Ct));

        var list = (await Json(await desk.GetAsync($"/api/care/appointments?date={day:yyyy-MM-dd}", Ct))).EnumerateArray().ToList();
        var url = list.Single(a => a.GetProperty("patientId").GetGuid() == ada).GetProperty("whatsAppUrl").GetString()!;
        Assert.StartsWith("https://wa.me/2348031112222?text=", url);
        var text = Uri.UnescapeDataString(url[(url.IndexOf("text=", StringComparison.Ordinal) + 5)..]);
        Assert.Contains("Hello Ada", text);
        Assert.Contains("Reminder Link Clinic", text);
        Assert.Contains("Doctor Staff", text);
        Assert.DoesNotContain("knee", text); // never the booking reason
        Assert.Equal(JsonValueKind.Null, list.Single(a => a.GetProperty("patientId").GetGuid() == noPhone).GetProperty("whatsAppUrl").ValueKind);

        // A cancelled appointment gets none.
        await Json(await desk.PostAsync($"/api/care/appointments/{Id(list.Single(a => a.GetProperty("patientId").GetGuid() == ada))}/cancel", null, Ct));
        var after = (await Json(await desk.GetAsync($"/api/care/appointments?date={day:yyyy-MM-dd}", Ct))).EnumerateArray()
            .Single(a => a.GetProperty("patientId").GetGuid() == ada);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("whatsAppUrl").ValueKind);

        // The queue: a patient waiting gets a "come back, it's nearly your turn" link; one waiting to pay a "come to the cashier" link.
        var visit = await kit.CheckInAsync(desk, ada);
        var queue = await Json(await desk.GetAsync("/api/care/queue?mine=false", Ct));
        var row = queue.GetProperty("stages").EnumerateArray().SelectMany(s => s.GetProperty("visits").EnumerateArray()).Single(v => Id(v) == Id(visit));
        var waiting = Uri.UnescapeDataString(row.GetProperty("whatsAppUrl").GetString()!);
        Assert.Contains("Hello Ada", waiting);
        Assert.Contains("nearly your turn", waiting);
        await Json(await desk.PostAsJsonAsync($"/api/care/visits/{Id(visit)}/stage", new { stage = "WaitingPayment" }, Ct));
        var paying = Uri.UnescapeDataString((await Json(await desk.GetAsync($"/api/care/visits/{Id(visit)}", Ct))).GetProperty("whatsAppUrl").GetString()!);
        Assert.Contains("cashier", paying);
    }

    // ---------- Item 6: PRN doses ----------

    [Fact]
    public async Task Ward_staff_record_an_as_needed_dose()
    {
        var clinic = await kit.ClinicAsync("Prn Clinic");
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var (otherNurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var wardId = await kit.WardAsync(clinic, "Prn ward", nurseUserIds: [nurseId]);
        var bedId = await kit.BedAsync(clinic, wardId, "P1");
        var patientId = Id(await kit.PatientAsync(clinic, "Prn", "Patient"));
        await kit.AssignAsync(clinic, patientId, doctorId);
        var admissionId = await kit.AdmitAsync(doctor, patientId, emergencyReason: null);
        await Json(await kit.AssignBedAsync(desk, admissionId, bedId));
        var rx = await Json(await doctor.PostAsJsonAsync("/api/care/prescriptions", new
        {
            patientId, admissionId, allergyAcknowledged = false,
            items = new object[]
            {
                new { drug = "Paracetamol", dose = "1 g", frequency = "PRN", route = "Oral" },
                new { drug = "Amoxicillin", dose = "500 mg", frequency = "TDS", durationDays = 1 },
            },
        }, Ct));
        var prn = Id(rx.GetProperty("items")[0]);
        var scheduled = Id(rx.GetProperty("items")[1]);
        var path = $"/api/care/admissions/{admissionId}/prn-doses";

        var given = await Json(await nurse.PostAsJsonAsync(path, new { prescriptionItemId = prn, status = "Given", note = "For headache" }, Ct));
        Assert.Equal("Given", given.GetProperty("status").GetString());
        Assert.Equal(nurseId, given.GetProperty("recordedByUserId").GetGuid());
        Assert.Equal("For headache", given.GetProperty("note").GetString());
        // It shows on today's chart, recorded.
        var chart = await Json(await nurse.GetAsync($"/api/care/admissions/{admissionId}/medication-chart", Ct));
        var dose = chart.GetProperty("doses").EnumerateArray().Single(d => d.GetProperty("prescriptionItemId").GetGuid() == prn);
        Assert.Equal("Given", dose.GetProperty("status").GetString());
        Assert.Equal("For headache", dose.GetProperty("note").GetString());
        // A second dose later is its own row.
        await Json(await nurse.PostAsJsonAsync(path, new { prescriptionItemId = prn, status = "Refused" }, Ct));
        Assert.Equal(2, await api.WithDbAsync(db => db.MedicationDoses.IgnoreQueryFilters().CountAsync(d => d.PrescriptionItemId == prn, Ct)));

        // Only an as-needed drug of this admission; only given, held or refused; the ward rules as for any dose.
        await AssertProblemAsync(await nurse.PostAsJsonAsync(path, new { prescriptionItemId = scheduled, status = "Given" }, Ct), HttpStatusCode.BadRequest, field: "prescriptionItemId");
        await AssertProblemAsync(await nurse.PostAsJsonAsync(path, new { prescriptionItemId = Guid.NewGuid(), status = "Given" }, Ct), HttpStatusCode.BadRequest, field: "prescriptionItemId");
        await AssertProblemAsync(await nurse.PostAsJsonAsync(path, new { prescriptionItemId = prn, status = "Missed" }, Ct), HttpStatusCode.BadRequest, field: "status");
        await AssertProblemAsync(await otherNurse.PostAsJsonAsync(path, new { prescriptionItemId = prn, status = "Given" }, Ct), HttpStatusCode.Forbidden, WardCareService.NotOnWard);
        await AssertProblemAsync(await desk.PostAsJsonAsync(path, new { prescriptionItemId = prn, status = "Given" }, Ct), HttpStatusCode.Forbidden, CareAccess.ClinicalRefused);
        var other = await kit.ClinicAsync("Prn Other Clinic");
        await AssertProblemAsync(await other.Owner.PostAsJsonAsync(path, new { prescriptionItemId = prn, status = "Given" }, Ct), HttpStatusCode.NotFound);
    }

    // ---------- Item 6: bill search and paging, bed day ----------

    [Fact]
    public async Task Bills_search_and_page_and_lines_carry_their_bed_day()
    {
        var clinic = await kit.ClinicAsync("Bill Search Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var item = await kit.PriceItemAsync(clinic, "Search fee", "Other", 1_000);
        var chidi = Id(await kit.PatientAsync(clinic, "Chidi", "Searchable"));
        var funke = Id(await kit.PatientAsync(clinic, "Funke", "Other", "1970-03-03"));
        var bills = new List<Guid>();
        foreach (var patient in new[] { chidi, chidi, funke })
            bills.Add(await api.AsTenantAsync<BillingService, Guid>(clinic.TenantId, async b =>
            {
                var bill = await b.OpenBillForVisitAsync(patient, Guid.NewGuid(), Ct);
                await b.AddLineAsync(bill.Id, item, 1, null, Ct);
                return bill.Id;
            }));

        var byName = (await Json(await cashier.GetAsync("/api/care/bills?q=searchable", Ct))).EnumerateArray().ToList();
        Assert.Equal(2, byName.Count);
        Assert.All(byName, b => Assert.Equal(chidi, b.GetProperty("patientId").GetGuid()));
        var number = (await Json(await cashier.GetAsync($"/api/care/bills/{bills[2]}", Ct))).GetProperty("number").GetString()!;
        Assert.Equal(bills[2], Id(Assert.Single((await Json(await cashier.GetAsync($"/api/care/bills?q={number}", Ct))).EnumerateArray())));
        var patientNumber = (await Json(await cashier.GetAsync($"/api/care/patients/{funke}", Ct))).GetProperty("number").GetString()!;
        Assert.Equal(bills[2], Id(Assert.Single((await Json(await cashier.GetAsync($"/api/care/bills?q={patientNumber}", Ct))).EnumerateArray())));

        // Paging: newest first, offset and limit.
        var page1 = (await Json(await cashier.GetAsync("/api/care/bills?limit=2", Ct))).EnumerateArray().Select(Id).ToList();
        var page2 = (await Json(await cashier.GetAsync("/api/care/bills?limit=2&offset=2", Ct))).EnumerateArray().Select(Id).ToList();
        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
        Assert.Equal(bills.AsEnumerable().Reverse(), page1.Concat(page2));
        await AssertProblemAsync(await cashier.GetAsync("/api/care/bills?limit=0", Ct), HttpStatusCode.BadRequest, field: "limit");
        await AssertProblemAsync(await cashier.GetAsync("/api/care/bills?limit=201", Ct), HttpStatusCode.BadRequest, field: "limit");
        await AssertProblemAsync(await cashier.GetAsync("/api/care/bills?offset=-1", Ct), HttpStatusCode.BadRequest, field: "offset");

        // A bed line reads its bed day; other lines read null.
        var day = Today.AddDays(-1);
        await api.WithDbAsync(db => db.BillLines.IgnoreQueryFilters().Where(l => l.BillId == bills[0]).ExecuteUpdateAsync(s => s.SetProperty(l => l.ChargeDay, day), Ct));
        var line = Assert.Single((await Json(await cashier.GetAsync($"/api/care/bills/{bills[0]}", Ct))).GetProperty("lines").EnumerateArray());
        Assert.Equal(day, DateOnly.Parse(line.GetProperty("chargeDay").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        var plain = Assert.Single((await Json(await cashier.GetAsync($"/api/care/bills/{bills[1]}", Ct))).GetProperty("lines").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("chargeDay").ValueKind);
    }

    // ---------- Item 6: duplicate check before registering ----------

    [Fact]
    public async Task The_desk_checks_for_duplicates_before_registering()
    {
        var clinic = await kit.ClinicAsync("Duplicate Check Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var existing = Id(await kit.PatientAsync(clinic, "Ngozi", "Eze", "1992-07-01", phone: "08035556666"));
        var count = await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().CountAsync(p => p.TenantId == clinic.TenantId, Ct));

        var byName = (await Json(await desk.PostAsJsonAsync("/api/care/patients/duplicates",
            new { firstName = "ngozi", lastName = "EZE", dateOfBirth = "1992-07-01" }, Ct))).EnumerateArray().ToList();
        Assert.Equal(existing, Id(Assert.Single(byName)));
        var byPhone = (await Json(await desk.PostAsJsonAsync("/api/care/patients/duplicates", new { firstName = "Someone", lastName = "Else", phone = "+234 803 555 6666" }, Ct)))
            .EnumerateArray().ToList();
        Assert.Equal(existing, Id(Assert.Single(byPhone)));
        Assert.Empty((await Json(await desk.PostAsJsonAsync("/api/care/patients/duplicates", new { firstName = "Ngozi", lastName = "Eze", dateOfBirth = "1992-07-02" }, Ct))).EnumerateArray());
        // Nothing is registered; the cashier can't use it; another clinic sees none of ours.
        Assert.Equal(count, await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().CountAsync(p => p.TenantId == clinic.TenantId, Ct)));
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/care/patients/duplicates", new { firstName = "Ngozi", lastName = "Eze" }, Ct)).StatusCode);
        var other = await kit.ClinicAsync("Duplicate Other Clinic");
        Assert.Empty((await Json(await other.Owner.PostAsJsonAsync("/api/care/patients/duplicates", new { firstName = "Ngozi", lastName = "Eze", dateOfBirth = "1992-07-01" }, Ct))).EnumerateArray());
    }
}
