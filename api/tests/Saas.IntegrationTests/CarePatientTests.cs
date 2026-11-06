using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Saas.IntegrationTests;

/// <summary>CareLink Task 2: patient registration, numbering, duplicates, search, clinical facts and going inactive.</summary>
[Collection(nameof(ApiCollection))]
public sealed class CarePatientTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null)
    {
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        if (detail is null) return;
        var body = await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        Assert.Equal(detail, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Patient_numbers_count_up_per_clinic()
    {
        var first = await kit.ClinicAsync("Numbers Clinic");
        var second = await kit.ClinicAsync("Other Numbers Clinic");

        Assert.Equal("CL-000001", (await kit.PatientAsync(first, "Ada", "Obi")).GetProperty("number").GetString());
        Assert.Equal("CL-000002", (await kit.PatientAsync(first, "Bayo", "Ade")).GetProperty("number").GetString());
        Assert.Equal("CL-000001", (await kit.PatientAsync(second, "Chi", "Eze")).GetProperty("number").GetString());

        // The prefix comes from the clinic profile.
        (await first.Owner.PutAsJsonAsync("/api/care/settings/profile", new { name = "Numbers Clinic", patientPrefix = "nc" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal("NC-000003", (await kit.PatientAsync(first, "Dayo", "Ola")).GetProperty("number").GetString());
    }

    [Fact]
    public async Task Duplicate_patient_is_warned_not_blocked()
    {
        var clinic = await kit.ClinicAsync("Duplicates Clinic");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var original = await kit.PatientAsync(clinic, "Ngozi", "Okafor", "1985-02-03", "08031234567");

        // Same name and date of birth (case and spacing aside): created, with a warning naming the original.
        var sameName = await Json(await frontDesk.PostAsJsonAsync("/api/care/patients",
            new { firstName = " ngozi ", lastName = "OKAFOR", sex = "Female", dateOfBirth = "1985-02-03" }, Ct));
        Assert.Equal("CL-000002", sameName.GetProperty("patient").GetProperty("number").GetString());
        var warned = sameName.GetProperty("possibleDuplicates").EnumerateArray().ToList();
        Assert.Single(warned);
        Assert.Equal(original.GetProperty("id").GetGuid(), warned[0].GetProperty("id").GetGuid());
        Assert.Equal("CL-000001", warned[0].GetProperty("number").GetString());

        // Same phone in another format: created, with a warning.
        var samePhone = await Json(await frontDesk.PostAsJsonAsync("/api/care/patients",
            new { firstName = "Emeka", lastName = "Okafor", sex = "Male", dateOfBirth = "2015-06-01", phone = "+234 803 123 4567" }, Ct));
        Assert.Contains(samePhone.GetProperty("possibleDuplicates").EnumerateArray(), d => d.GetProperty("id").GetGuid() == original.GetProperty("id").GetGuid());
        Assert.Equal("+2348031234567", samePhone.GetProperty("patient").GetProperty("phone").GetString());

        // Someone else: no warning.
        var other = await Json(await frontDesk.PostAsJsonAsync("/api/care/patients",
            new { firstName = "Tunde", lastName = "Bello", sex = "Male", dateOfBirth = "1970-01-01", phone = "08099990000" }, Ct));
        Assert.Empty(other.GetProperty("possibleDuplicates").EnumerateArray());

        Assert.Equal(4, await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().CountAsync(p => p.TenantId == clinic.TenantId, Ct)));
    }

    [Fact]
    public async Task Patients_are_found_by_name_number_or_phone()
    {
        var clinic = await kit.ClinicAsync("Search Clinic");
        var other = await kit.ClinicAsync("Search Other Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var ada = await kit.PatientAsync(clinic, "Adaeze", "Nwosu", phone: "08035550001");
        await kit.PatientAsync(clinic, "Bola", "Tinubu", phone: "08035550002");
        var outsider = await kit.PatientAsync(other, "Adaeze", "Nwosu");

        async Task<List<Guid>> Search(string q) => (await Json(await cashier.GetAsync($"/api/care/patients?q={Uri.EscapeDataString(q)}", Ct)))
            .EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([ada.GetProperty("id").GetGuid()], await Search("adaeze"));
        Assert.Equal([ada.GetProperty("id").GetGuid()], await Search("Adaeze Nwosu"));
        Assert.Equal([ada.GetProperty("id").GetGuid()], await Search("cl-000001"));
        Assert.Equal([ada.GetProperty("id").GetGuid()], await Search("+234 803 555 0001"));
        Assert.Equal(2, (await Search("")).Count);

        // Another clinic's patient is 404.
        await AssertProblemAsync(await clinic.Owner.GetAsync($"/api/care/patients/{outsider.GetProperty("id").GetGuid()}", Ct), HttpStatusCode.NotFound);
        // A cashier reads demographics but doesn't register patients.
        await AssertProblemAsync(await cashier.PostAsJsonAsync("/api/care/patients",
            new { firstName = "Cash", lastName = "Only", sex = "Male", dateOfBirth = "1990-01-01" }, Ct), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Front_desk_edits_demographics()
    {
        var clinic = await kit.ClinicAsync("Demographics Clinic");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patient = await kit.PatientAsync(clinic, "Kemi", "Adebayo");
        var id = patient.GetProperty("id").GetGuid();

        var saved = await Json(await frontDesk.PutAsJsonAsync($"/api/care/patients/{id}", new
        {
            firstName = "Kemi", middleName = "Folake", lastName = "Adebayo", sex = "Female", dateOfBirth = "1991-03-04", phone = "0802 000 1111",
            email = "kemi@example.com", address = "2 Allen Avenue, Ikeja", nextOfKinName = "Tayo Adebayo", nextOfKinPhone = "08020002222",
            nextOfKinRelationship = "Husband",
        }, Ct));
        Assert.Equal("Folake", saved.GetProperty("middleName").GetString());
        Assert.Equal("1991-03-04", saved.GetProperty("dateOfBirth").GetString());
        Assert.Equal("+2348020001111", saved.GetProperty("phone").GetString());
        Assert.Equal("Husband", saved.GetProperty("nextOfKinRelationship").GetString());
        Assert.Equal("CL-000001", saved.GetProperty("number").GetString());

        // Field errors are 400.
        await AssertProblemAsync(await frontDesk.PutAsJsonAsync($"/api/care/patients/{id}",
            new { firstName = "", lastName = "Adebayo", sex = "Female", dateOfBirth = "1991-03-04" }, Ct), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await frontDesk.PutAsJsonAsync($"/api/care/patients/{id}",
            new { firstName = "Kemi", lastName = "Adebayo", sex = "Female", dateOfBirth = "2999-01-01" }, Ct), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Front_desk_cannot_set_allergies()
    {
        var clinic = await kit.ClinicAsync("Allergy Clinic");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var id = (await kit.PatientAsync(clinic, "Ife", "Ola")).GetProperty("id").GetGuid();
        var facts = new { allergies = new[] { "Penicillin", " penicillin ", "Peanuts" }, conditions = new[] { "Asthma" }, bloodGroup = "O+", genotype = "AS" };

        await AssertProblemAsync(await frontDesk.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts", facts, Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await cashier.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts", facts, Ct), HttpStatusCode.Forbidden, ClinicalRefused);

        var saved = await Json(await nurse.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts", facts, Ct));
        Assert.Equal(["Penicillin", "Peanuts"], saved.GetProperty("allergies").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(["Asthma"], saved.GetProperty("conditions").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("O+", saved.GetProperty("bloodGroup").GetString());
        Assert.Equal("AS", saved.GetProperty("genotype").GetString());

        // Clinical staff read them back; front desk sees demographics only.
        var asDoctor = await Json(await clinic.Owner.GetAsync($"/api/care/patients/{id}", Ct));
        Assert.Equal(2, asDoctor.GetProperty("allergies").GetArrayLength());
        var asFrontDesk = await Json(await frontDesk.GetAsync($"/api/care/patients/{id}", Ct));
        Assert.False(asFrontDesk.TryGetProperty("allergies", out _));
        Assert.False(asFrontDesk.TryGetProperty("conditions", out _));
        Assert.Equal("Ife", asFrontDesk.GetProperty("firstName").GetString());

        // Allergies are clinical free text: stored encrypted.
        var stored = await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().FirstAsync(p => p.Id == id, Ct));
        Assert.DoesNotContain(stored.Allergies, a => a.Contains("Penicillin"));
        Assert.DoesNotContain(stored.Conditions, c => c.Contains("Asthma"));

        await AssertProblemAsync(await nurse.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts",
            new { allergies = Array.Empty<string>(), conditions = Array.Empty<string>(), bloodGroup = "Z+", genotype = (string?)null }, Ct), HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Final review I1 (ruling): the clinical-facts save carries the version it loaded. Two clinicians save from the same version: the first
    /// wins, the second gets 409 and the first one's allergy survives. A save without a version counts as version 0 (nothing saved yet).
    /// </summary>
    [Fact]
    public async Task Concurrent_allergy_edits_never_drop_an_entry()
    {
        const string Stale = "These clinical details were changed by someone else. Reload and try again.";
        var clinic = await kit.ClinicAsync("Stale Facts Clinic");
        var (nurse, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var id = (await kit.PatientAsync(clinic, "Stale", "Facts")).GetProperty("id").GetGuid();
        await kit.AssignAsync(clinic, id, nurseId, Saas.Domain.Care.CareAssignmentKind.TriageNurse);
        var first = await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts", new { allergies = new[] { "Penicillin" } }, Ct));
        var version = first.GetProperty("factsVersion").GetInt32();

        // Both read the same version.
        Assert.Equal(version, (await Json(await clinic.Owner.GetAsync($"/api/care/patients/{id}", Ct))).GetProperty("factsVersion").GetInt32());
        Assert.Equal(version, (await Json(await nurse.GetAsync($"/api/care/patients/{id}", Ct))).GetProperty("factsVersion").GetInt32());

        var doctorSave = await Json(await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts",
            new { allergies = new[] { "Penicillin", "Sulfa" }, version }, Ct));
        Assert.True(doctorSave.GetProperty("factsVersion").GetInt32() > version);
        await AssertProblemAsync(await nurse.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts",
            new { allergies = new[] { "Penicillin", "Latex" }, version }, Ct), HttpStatusCode.Conflict, Stale);
        // A save that names no version is never a blind replace either.
        await AssertProblemAsync(await nurse.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts",
            new { allergies = new[] { "Penicillin", "Latex" } }, Ct), HttpStatusCode.Conflict, Stale);

        var now = await Json(await clinic.Owner.GetAsync($"/api/care/patients/{id}", Ct));
        Assert.Equal(["Penicillin", "Sulfa"], now.GetProperty("allergies").EnumerateArray().Select(a => a.GetString()));
        // Reloaded, the nurse adds hers on top.
        var merged = await Json(await nurse.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts",
            new { allergies = new[] { "Penicillin", "Sulfa", "Latex" }, version = now.GetProperty("factsVersion").GetInt32() }, Ct));
        Assert.Equal(["Penicillin", "Sulfa", "Latex"], merged.GetProperty("allergies").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public async Task Audit_log_holds_no_clinical_values()
    {
        var clinic = await kit.ClinicAsync("Audit Clinic");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var id = (await kit.PatientAsync(clinic, "Uche", "Nnamdi")).GetProperty("id").GetGuid();
        (await clinic.Owner.PutAsJsonAsync($"/api/care/patients/{id}/clinical-facts",
            new { allergies = new[] { "Penicillin" }, conditions = new[] { "Asthma" }, bloodGroup = "O+", genotype = "AS" }, Ct)).EnsureSuccessStatusCode();
        (await frontDesk.PutAsJsonAsync($"/api/care/patients/{id}",
            new { firstName = "Uche", lastName = "Nnamdi", sex = "Male", dateOfBirth = "1990-04-12", phone = "08030001111" }, Ct)).EnsureSuccessStatusCode();
        (await frontDesk.PostAsync($"/api/care/patients/{id}/inactive", null, Ct)).EnsureSuccessStatusCode();
        (await frontDesk.PostAsync($"/api/care/patients/{id}/active", null, Ct)).EnsureSuccessStatusCode();

        // A bill line's clinical reference stays out of the audit log too.
        var drug = await kit.PriceItemAsync(clinic, "Amoxicillin 500mg", "Drug", 10_000);
        const string clinicalRef = "rx-item-7f3c";
        await api.AsTenantAsync<Saas.Application.Care.BillingService, Guid>(clinic.TenantId, async b =>
        {
            var bill = await b.OpenBillForVisitAsync(id, Guid.NewGuid(), Ct);
            return (await b.AddLineAsync(bill.Id, drug, 1, clinicalRef, Ct)).Id;
        });

        var rows = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == clinic.TenantId).ToListAsync(Ct));
        foreach (var json in rows.SelectMany(a => new[] { a.BeforeJson, a.AfterJson }).OfType<string>())
        {
            Assert.DoesNotContain("\"O+\"", json);
            Assert.DoesNotContain("\"AS\"", json);
            Assert.DoesNotContain("Penicillin", json);
            Assert.DoesNotContain("Asthma", json);
            Assert.DoesNotContain(clinicalRef, json);
        }

        // Explicit rows: what happened and which demographic fields changed, never clinical values.
        var mine = rows.Where(a => a.EntityId == id.ToString()).ToList();
        Assert.Contains(mine, a => a.Action == "patient.registered");
        Assert.Contains(mine, a => a.Action == "patient.clinical_facts" && a.AfterJson!.Contains("clinical facts updated"));
        var edit = Assert.Single(mine, a => a.Action == "patient.updated");
        Assert.Contains("phone", edit.AfterJson);
        Assert.DoesNotContain("08030001111", edit.AfterJson);
        Assert.DoesNotContain("2348030001111", edit.AfterJson);
        Assert.Contains(mine, a => a.Action == "patient.inactive");
        Assert.Contains(mine, a => a.Action == "patient.active");
    }

    [Fact]
    public async Task Patient_with_history_cannot_be_deleted()
    {
        var clinic = await kit.ClinicAsync("Inactive Clinic");
        var (frontDesk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var id = (await kit.PatientAsync(clinic, "Sola", "Ojo")).GetProperty("id").GetGuid();

        // There is no delete: the record stays.
        var deleted = await clinic.Owner.DeleteAsync($"/api/care/patients/{id}", Ct);
        Assert.False(deleted.IsSuccessStatusCode, $"{(int)deleted.StatusCode}");
        Assert.True(await api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().AnyAsync(p => p.Id == id, Ct)));

        // Going inactive keeps the record, its number and its demographics.
        var inactive = await Json(await frontDesk.PostAsync($"/api/care/patients/{id}/inactive", null, Ct));
        Assert.Equal("Inactive", inactive.GetProperty("status").GetString());
        var read = await Json(await clinic.Owner.GetAsync($"/api/care/patients/{id}", Ct));
        Assert.Equal("Inactive", read.GetProperty("status").GetString());
        Assert.Equal("CL-000001", read.GetProperty("number").GetString());
        Assert.Contains((await Json(await clinic.Owner.GetAsync("/api/care/patients?q=Sola", Ct))).EnumerateArray(),
            p => p.GetProperty("id").GetGuid() == id && p.GetProperty("status").GetString() == "Inactive");
    }
}
