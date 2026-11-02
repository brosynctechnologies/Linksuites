using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Care;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink Task 12 imports through the shared engine under <c>/api/care/imports</c>: patients (front desk and above, through the patient
/// screen's rules), the price list (leadership or the cashier) and staff (leadership; seats and the medical-director-only clinical role for
/// admins). Preview, then an idempotent confirm; a blank cell keeps what is saved and an equal value is Unchanged. Clinical columns are never
/// imported or held.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CareImportTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);

    private const string PatientHeadings =
        "patient_number,first_name,middle_name,last_name,sex,date_of_birth,phone,email,address,next_of_kin_name,next_of_kin_phone,next_of_kin_relationship";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static MultipartFormDataContent Form(string csv, string fileName)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string Csv(string headings, params string[] lines) => string.Join("\r\n", new[] { headings }.Concat(lines)) + "\r\n";

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string kind, string csv) =>
        client.PostAsync($"/api/care/imports/{kind}/preview", Form(csv, kind + ".csv"), Ct);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token) =>
        client.PostAsync($"/api/care/imports/{token}/confirm", null, Ct);

    private static List<JsonElement> Rows(JsonElement preview) => preview.GetProperty("rows").EnumerateArray().ToList();
    private static List<string> Warnings(JsonElement preview) => preview.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();

    private Task<List<Patient>> PatientsAsync(Guid tenantId) =>
        api.WithDbAsync(db => db.Patients.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).OrderBy(p => p.Number).ToListAsync(Ct));

    // ---------- Patients ----------

    [Fact]
    public async Task Patient_import_preview_and_confirm()
    {
        var clinic = await kit.ClinicAsync("Patient Import Clinic");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var existing = (await kit.PatientAsync(clinic, "Bola", "Ade", "1975-02-03", phone: "08030000001")).GetProperty("number").GetString()!;

        var csv = Csv(PatientHeadings,
            ",Chidi,Ifeanyi,Okafor,M,1990-04-12,0803 123 4567,chidi@example.com,\"12 Allen Avenue, Ikeja\",Ada Okafor,08035556666,Wife",
            ",Amaka,,Nwosu,Female,04/03/2012,,,,,,",
            $"{existing},,,,,,,bola@example.com,,,,",
            ",Bad,,Date,F,2099-01-01,,,,,,");
        var preview = await Json(await PreviewAsync(desk, "care-patients", csv));
        var rows = Rows(preview);
        Assert.Equal(["New", "New", "Update", "Error"], rows.Select(r => r.GetProperty("status").GetString()));
        Assert.Equal("The date of birth can't be in the future.", rows[3].GetProperty("message").GetString());
        // Nothing is saved by a preview.
        Assert.Single(await PatientsAsync(clinic.TenantId));

        // An error row blocks the confirm; fix the file and upload again.
        Assert.Equal(HttpStatusCode.Conflict, (await ConfirmAsync(desk, preview.GetProperty("token").GetString()!)).StatusCode);
        var fixedCsv = Csv(PatientHeadings,
            ",Chidi,Ifeanyi,Okafor,M,1990-04-12,0803 123 4567,chidi@example.com,\"12 Allen Avenue, Ikeja\",Ada Okafor,08035556666,Wife",
            ",Amaka,,Nwosu,Female,04/03/2012,,,,,,",
            $"{existing},,,,,,,bola@example.com,,,,");
        var token = (await Json(await PreviewAsync(desk, "care-patients", fixedCsv))).GetProperty("token").GetString()!;
        var applied = await Json(await ConfirmAsync(desk, token));
        Assert.Equal(2, applied.GetProperty("created").GetInt32());
        Assert.Equal(1, applied.GetProperty("updated").GetInt32());

        var patients = await PatientsAsync(clinic.TenantId);
        Assert.Equal(3, patients.Count);
        // Numbers come from the clinic's counter, in file order after the one already registered.
        Assert.Equal(["CL-000001", "CL-000002", "CL-000003"], patients.Select(p => p.Number));
        var chidi = patients.Single(p => p.FirstName == "Chidi");
        Assert.Equal("+2348031234567", chidi.Phone);
        Assert.Equal(PatientSex.Male, chidi.Sex);
        Assert.Equal(new DateOnly(1990, 4, 12), chidi.DateOfBirth);
        Assert.Equal("12 Allen Avenue, Ikeja", chidi.Address);
        Assert.Equal("+2348035556666", chidi.NextOfKinPhone);
        Assert.Equal(new DateOnly(2012, 3, 4), patients.Single(p => p.FirstName == "Amaka").DateOfBirth);
        // A blank cell keeps what is saved: only the email changed.
        var bola = patients.Single(p => p.Number == existing);
        Assert.Equal("bola@example.com", bola.Email);
        Assert.Equal("+2348030000001", bola.Phone);
        Assert.Equal("Bola", bola.FirstName);

        // The confirm is idempotent: the same token again returns the first result and saves nothing.
        var again = await Json(await ConfirmAsync(desk, token));
        Assert.True(again.GetProperty("alreadyImported").GetBoolean());
        Assert.Equal(3, (await PatientsAsync(clinic.TenantId)).Count);

        // The same file uploaded again changes nothing: every row is Unchanged (no duplicate registrations).
        var repeat = await Json(await PreviewAsync(desk, "care-patients", fixedCsv));
        Assert.All(Rows(repeat), r => Assert.Equal("Unchanged", r.GetProperty("status").GetString()));

        // The import is audited by counts and file name; registrations by field names. No values in the import's audit row.
        var audit = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == clinic.TenantId && a.Action == "imports.care-patients").SingleAsync(Ct));
        Assert.DoesNotContain("Chidi", audit.AfterJson);
    }

    [Fact]
    public async Task Patient_import_refuses_duplicates_and_unknown_numbers()
    {
        var clinic = await kit.ClinicAsync("Patient Duplicate Clinic");
        await kit.PatientAsync(clinic, "Tunde", "Bakare", "1980-06-01", phone: "08037778888");
        var preview = await Json(await PreviewAsync(clinic.Owner, "care-patients", Csv(PatientHeadings,
            ",Tunde,,Bakare,M,1980-06-01,08030001111,,,,,",   // same name and date of birth as a registered patient, different phone
            ",Kemi,,Bakare,F,2010-01-01,08037778888,,,,,",    // shares a phone with a registered patient: new, with a warning
            ",Sade,,Lawal,F,1999-09-09,,,,,,",
            ",sade,,LAWAL,F,1999-09-09,,,,,,",                 // the same person twice in the file
            "CL-999999,Ghost,,Number,F,1999-09-09,,,,,,")));
        var rows = Rows(preview);
        Assert.Equal(["Error", "New", "Error", "Error", "Error"], rows.Select(r => r.GetProperty("status").GetString()));
        Assert.Contains("CL-000001", rows[0].GetProperty("message").GetString());
        Assert.Contains("patient_number", rows[0].GetProperty("message").GetString());
        Assert.Equal("This patient appears more than once in the file.", rows[2].GetProperty("message").GetString());
        Assert.Equal("No patient has this number. Leave patient_number blank to register a new patient.", rows[4].GetProperty("message").GetString());
        Assert.Contains(Warnings(preview), w => w.Contains("a phone number with a registered patient", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_refuses_clinical_columns()
    {
        var clinic = await kit.ClinicAsync("Clinical Column Clinic");
        var csv = Csv(PatientHeadings + ",allergies,Diagnosis,notes",
            ",Ife,,Ola,F,1995-05-05,,,,,,,Penicillinmarker,Malariamarker,Asthmamarker");
        var preview = await Json(await PreviewAsync(clinic.Owner, "care-patients", csv));
        var warnings = Warnings(preview);
        Assert.Contains("These columns were ignored: allergies, diagnosis and notes.", warnings);
        Assert.Contains(warnings, w => w.StartsWith("Clinical details are never imported", StringComparison.Ordinal));
        Assert.Equal("New", Assert.Single(Rows(preview)).GetProperty("status").GetString());

        // Not even the held preview keeps the clinical values.
        var token = preview.GetProperty("token").GetString()!;
        var payload = await api.WithDbAsync(db => db.ImportBatches.IgnoreQueryFilters().Where(b => b.Token == token).Select(b => b.Payload).SingleAsync(Ct));
        foreach (var marker in new[] { "Penicillinmarker", "Malariamarker", "Asthmamarker" }) Assert.DoesNotContain(marker, payload);

        await Json(await ConfirmAsync(clinic.Owner, token));
        var patient = Assert.Single(await PatientsAsync(clinic.TenantId));
        Assert.Empty(patient.Allergies);
        Assert.Empty(patient.Conditions);
        var rawAudits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.TenantId == clinic.TenantId).Select(a => new { a.AfterJson, a.BeforeJson }).ToListAsync(Ct));
        var audits = rawAudits.Select(a => (a.AfterJson ?? "") + (a.BeforeJson ?? "")).ToList();
        foreach (var marker in new[] { "Penicillinmarker", "Malariamarker", "Asthmamarker" }) Assert.DoesNotContain(audits, a => a.Contains(marker, StringComparison.Ordinal));

        // The other kinds ignore clinical columns too.
        var price = await Json(await PreviewAsync(clinic.Owner, "care-price-list", Csv("name,category,price,medication", "Paracetamol 500mg,Drug,50,Painmarker")));
        Assert.Contains(Warnings(price), w => w.StartsWith("Clinical details are never imported", StringComparison.Ordinal));
        var held = price.GetProperty("token").GetString()!;
        Assert.DoesNotContain("Painmarker", await api.WithDbAsync(db => db.ImportBatches.IgnoreQueryFilters().Where(b => b.Token == held).Select(b => b.Payload).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Patient_template_round_trips_unchanged()
    {
        var clinic = await kit.ClinicAsync("Template Clinic");
        foreach (var kind in new[] { "care-patients", "care-price-list", "care-staff" })
        {
            var response = await clinic.Owner.GetAsync($"/api/care/imports/{kind}/template.csv", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.StartsWith("carelink-", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            var xlsx = await clinic.Owner.GetAsync($"/api/care/imports/{kind}/template.xlsx", Ct);
            Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        }
        var template = Encoding.UTF8.GetString(await clinic.Owner.GetByteArrayAsync("/api/care/imports/care-patients/template.csv", Ct)).TrimStart('﻿');
        var headings = template.Split("\r\n")[0];
        Assert.Equal(PatientHeadings, headings);

        // The template filled in: no column is ignored; confirmed, then the same file again is all Unchanged.
        var csv = Csv(headings, ",Yemi,,Alade,M,1988-08-08,08039990000,,,,,");
        var first = await Json(await PreviewAsync(clinic.Owner, "care-patients", csv));
        Assert.Empty(Warnings(first));
        await Json(await ConfirmAsync(clinic.Owner, first.GetProperty("token").GetString()!));
        var second = await Json(await PreviewAsync(clinic.Owner, "care-patients", csv));
        Assert.Equal("Unchanged", Assert.Single(Rows(second)).GetProperty("status").GetString());
        var number = Assert.Single(await PatientsAsync(clinic.TenantId)).Number;
        var byNumber = await Json(await PreviewAsync(clinic.Owner, "care-patients", Csv(headings, $"{number},Yemi,,Alade,M,1988-08-08,08039990000,,,,,")));
        Assert.Equal("Unchanged", Assert.Single(Rows(byNumber)).GetProperty("status").GetString());
    }

    // ---------- Access ----------

    [Fact]
    public async Task Import_access_follows_each_screen()
    {
        var clinic = await kit.ClinicAsync("Import Access Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var (doctor, _) = await kit.StaffAsync(clinic, "Doctor");
        var (admin, _) = await kit.StaffAsync(clinic, "Admin");

        async Task Expect(HttpClient client, string kind, HttpStatusCode status, string csv)
        {
            Assert.Equal(status, (await client.GetAsync($"/api/care/imports/{kind}/template.csv", Ct)).StatusCode);
            var preview = await PreviewAsync(client, kind, csv);
            Assert.True(status == preview.StatusCode, $"{kind}: {(int)preview.StatusCode} {await preview.Content.ReadAsStringAsync(Ct)}");
        }

        var patients = Csv(PatientHeadings, ",Access,,Check,F,1990-01-01,,,,,,");
        var prices = Csv("name,category,price", "Access item,Other,100");
        var staff = Csv("email,role,title", $"access-{Guid.NewGuid():N}@example.com,Nurse,");
        // Patients: everyone but the cashier (the registration screen).
        await Expect(cashier, "care-patients", HttpStatusCode.Forbidden, patients);
        foreach (var c in new[] { desk, doctor, admin }) await Expect(c, "care-patients", HttpStatusCode.OK, patients);
        // Price list: leadership and the cashier.
        foreach (var c in new[] { desk, doctor }) await Expect(c, "care-price-list", HttpStatusCode.Forbidden, prices);
        foreach (var c in new[] { cashier, admin }) await Expect(c, "care-price-list", HttpStatusCode.OK, prices);
        // Staff: leadership only.
        foreach (var c in new[] { desk, doctor, cashier }) await Expect(c, "care-staff", HttpStatusCode.Forbidden, staff);
        await Expect(admin, "care-staff", HttpStatusCode.OK, staff);

        // A token is confirmed only by the person who uploaded it.
        var token = (await Json(await PreviewAsync(desk, "care-patients", patients))).GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.NotFound, (await ConfirmAsync(admin, token)).StatusCode);

        // Kinds are per product: a clinic can't reach a school import, and a school can't reach a clinic's.
        Assert.Equal(HttpStatusCode.NotFound, (await clinic.Owner.GetAsync("/api/care/imports/students/template.csv", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clinic.Owner.GetAsync("/api/care/imports/fee-items/template.csv", Ct)).StatusCode);
        var school = await new SchoolKit(api).SchoolAsync("Import Access School");
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.GetAsync("/api/school/imports/care-patients/template.csv", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await school.Owner.PostAsync("/api/school/imports/care-price-list/preview", Form(prices, "p.csv"), Ct)).StatusCode);
    }

    // ---------- Price list ----------

    [Fact]
    public async Task Price_list_import_adds_updates_and_keeps_blanks()
    {
        var clinic = await kit.ClinicAsync("Price Import Clinic");
        var (cashier, _) = await kit.StaffAsync(clinic, "Cashier");
        await kit.PriceItemAsync(clinic, "General consultation", "Consultation", 500_000);
        await kit.PriceItemAsync(clinic, "Malaria smear", "Test", 150_000);

        var csv = Csv("name,category,price",
            "general CONSULTATION,,6000",       // update the price; blank category keeps Consultation
            "Malaria smear,Test,1500",          // equal: Unchanged
            "Amoxicillin 500mg,Drug,\"1,250.50\"",
            "Dressing,Procedure,",              // new needs a price
            "Scan,Imaging,5000");               // unknown category
        var preview = await Json(await PreviewAsync(cashier, "care-price-list", csv));
        var rows = Rows(preview);
        Assert.Equal(["Update", "Unchanged", "New", "Error", "Error"], rows.Select(r => r.GetProperty("status").GetString()));
        Assert.Equal("Enter the price.", rows[3].GetProperty("message").GetString());
        Assert.Equal("Choose Consultation, Procedure, Test, Drug, Bed, Registration or Other.", rows[4].GetProperty("message").GetString());

        var good = Csv("name,category,price", "general CONSULTATION,,6000", "Malaria smear,Test,1500", "Amoxicillin 500mg,Drug,\"1,250.50\"");
        var token = (await Json(await PreviewAsync(cashier, "care-price-list", good))).GetProperty("token").GetString()!;
        var applied = await Json(await ConfirmAsync(cashier, token));
        Assert.Equal(1, applied.GetProperty("created").GetInt32());
        Assert.Equal(1, applied.GetProperty("updated").GetInt32());
        var items = await api.WithDbAsync(db => db.PriceItems.IgnoreQueryFilters().Where(i => i.TenantId == clinic.TenantId).ToListAsync(Ct));
        var consult = items.Single(i => i.Category == PriceCategory.Consultation);
        Assert.Equal("General consultation", consult.Name); // never renamed
        Assert.Equal(600_000, consult.PriceMinor);
        Assert.Equal(125_050, items.Single(i => i.Name == "Amoxicillin 500mg").PriceMinor);

        var twice = await Json(await PreviewAsync(cashier, "care-price-list", Csv("name,category,price", "Gauze,Other,10", "gauze,Other,20")));
        Assert.All(Rows(twice), r => Assert.Equal("This price item appears more than once in the file.", r.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Price_list_category_must_be_one_defined_name()
    {
        var clinic = await kit.ClinicAsync("Category Parse Clinic");
        await kit.PriceItemAsync(clinic, "Existing dressing", "Procedure", 100_000);
        // "Procedure, Test" would parse as a flags combination (1 | 2 = Drug) and "3" as a number; both, and an unknown name, are errors.
        var preview = await Json(await PreviewAsync(clinic.Owner, "care-price-list", Csv("name,category,price",
            "Combo item,\"Procedure, Test\",100", "Numbered item,3,100", "Signed item,+3,100", "Bogus item,Bogus,100",
            "Existing dressing,\"Test, Bed\",", "Lower item,drug,100", "Spaced item, Other ,100")));
        var rows = Rows(preview);
        Assert.Equal(["Error", "Error", "Error", "Error", "Error", "New", "New"], rows.Select(r => r.GetProperty("status").GetString()));
        foreach (var row in rows.Take(5))
            Assert.Equal("Choose Consultation, Procedure, Test, Drug, Bed, Registration or Other.", row.GetProperty("message").GetString());
    }

    // ---------- Support view ----------

    [Fact]
    public async Task Import_confirm_is_refused_in_support_view()
    {
        var clinic = await kit.ClinicAsync("Support Import Clinic");
        var csv = Csv(PatientHeadings, ",Support,,Viewed,F,1990-01-01,,,,,,");
        var token = (await Json(await PreviewAsync(clinic.Owner, "care-patients", csv))).GetProperty("token").GetString()!;

        // Support's view-as session carries the owner's identity, so the token would be "theirs"; it is read-only and is refused.
        var (support, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Support);
        var viewAs = await Json(await support.PostAsJsonAsync($"/api/platform/businesses/{clinic.TenantId}/view-as", new { reason = "Checking a reported import problem." }, Ct));
        var viewer = api.CreateClient();
        viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewAs.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(viewer, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(viewer, "care-patients", csv)).StatusCode);
        Assert.Empty(await PatientsAsync(clinic.TenantId));

        // The owner's own confirm still works: the refused attempt didn't use up the token.
        Assert.Equal(1, (await Json(await ConfirmAsync(clinic.Owner, token))).GetProperty("created").GetInt32());
    }

    // ---------- Staff ----------

    [Fact]
    public async Task Staff_import_invites_and_keeps_roles()
    {
        var clinic = await kit.ClinicAsync("Staff Import Clinic");
        var (admin, adminId) = await kit.StaffAsync(clinic, "Admin");
        var (_, nurseId) = await kit.StaffAsync(clinic, "Nurse");
        var adminEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == adminId).Select(u => u.Email!).SingleAsync(Ct));
        var nurseEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == nurseId).Select(u => u.Email!).SingleAsync(Ct));
        var doctorEmail = $"import-doctor-{Guid.NewGuid():N}@example.com";
        var adminDoctorEmail = $"import-admin-doctor-{Guid.NewGuid():N}@example.com";

        var csv = Csv("email,role,title",
            $"{doctorEmail},Doctor,Consultant",           // new: invited as a doctor; title ignored with a warning
            $"{adminDoctorEmail},Admin (doctor),",        // only the medical director gives an admin a clinical role
            $"{nurseEmail},Doctor,Senior nurse",          // on the team: title updated, role not changed
            $"{adminEmail},,Head of admin",               // an admin can't edit their own profile
            "someone@example.com,Medical director,",
            "not-an-email,Nurse,");
        var preview = await Json(await PreviewAsync(admin, "care-staff", csv));
        var rows = Rows(preview);
        Assert.Equal(["New", "Error", "Update", "Error", "Error", "Error"], rows.Select(r => r.GetProperty("status").GetString()));
        Assert.Equal("Only the medical director can change an admin's clinical role.", rows[1].GetProperty("message").GetString());
        Assert.Equal("Ask the medical director to change your role.", rows[3].GetProperty("message").GetString());
        Assert.Contains("medical director", rows[4].GetProperty("message").GetString());
        var warnings = Warnings(preview);
        Assert.Contains(warnings, w => w.Contains("roles of people already on the team are not changed", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("Titles are saved only for people already on the team", StringComparison.Ordinal));

        // The medical director may invite an admin with a clinical role.
        var owner = await Json(await PreviewAsync(clinic.Owner, "care-staff", Csv("email,role,title", $"{adminDoctorEmail},Admin (doctor),")));
        Assert.Equal("New", Assert.Single(Rows(owner)).GetProperty("status").GetString());

        var good = Csv("email,role,title", $"{doctorEmail},Doctor,Consultant", $"{nurseEmail},Doctor,Senior nurse");
        var token = (await Json(await PreviewAsync(admin, "care-staff", good))).GetProperty("token").GetString()!;
        var applied = await Json(await ConfirmAsync(admin, token));
        Assert.Equal(1, applied.GetProperty("created").GetInt32());
        Assert.Equal(1, applied.GetProperty("updated").GetInt32());

        var invite = await api.WithDbAsync(db => db.Invitations.IgnoreQueryFilters().SingleAsync(i => i.Email == doctorEmail, Ct));
        Assert.Equal(MemberRole.Staff, invite.Role);
        Assert.Equal(ClinicalRole.Doctor, invite.ClinicalRole);
        var nurse = await api.WithDbAsync(db => db.CareStaffProfiles.IgnoreQueryFilters().SingleAsync(p => p.UserId == nurseId && p.TenantId == clinic.TenantId, Ct));
        Assert.Equal("Senior nurse", nurse.Title);
        Assert.Equal(ClinicalRole.Nurse, nurse.ClinicalRole); // the role column never changes someone on the team
        // The invitation email waits in the outbox for the commit; exactly one, even after a repeat confirm.
        Assert.True((await Json(await ConfirmAsync(admin, token))).GetProperty("alreadyImported").GetBoolean());
        Assert.Equal(1, await api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "team.invite" && n.Recipient == doctorEmail, Ct)));
    }

    [Fact]
    public async Task Staff_import_respects_team_seats()
    {
        // A Free clinic has three seats; the medical director holds one.
        var clinic = await kit.ClinicAsync("Seat Import Clinic");
        var emails = Enumerable.Range(1, 3).Select(i => $"seat-{i}-{Guid.NewGuid():N}@example.com").ToList();
        var preview = await Json(await PreviewAsync(clinic.Owner, "care-staff", Csv("email,role,title", emails.Select(e => $"{e},Nurse,").ToArray())));
        var rows = Rows(preview);
        Assert.Equal(["New", "New", "Error"], rows.Select(r => r.GetProperty("status").GetString()));

        // The row says what the team screen says when the plan is full.
        foreach (var e in emails.Take(2))
            (await clinic.Owner.PostAsJsonAsync("/api/team/invites", new { email = e, role = "Staff", clinicalRole = "Nurse" }, Ct)).EnsureSuccessStatusCode();
        var full = await clinic.Owner.PostAsJsonAsync("/api/team/invites", new { email = emails[2], role = "Staff", clinicalRole = "Nurse" }, Ct);
        Assert.Equal(HttpStatusCode.PaymentRequired, full.StatusCode);
        var detail = (await full.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("detail").GetString();
        Assert.Equal(detail, rows[2].GetProperty("message").GetString());
    }
}
