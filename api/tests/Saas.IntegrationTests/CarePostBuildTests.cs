using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;
using Saas.Infrastructure.Notifications;

namespace Saas.IntegrationTests;

/// <summary>
/// CareLink post-build round: staff invites name the clinic role (email and public preview), and every test result waits for a doctor's
/// (or the medical director's) review before the patient's portal shows it or the patient is told.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class CarePostBuildTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly CareKit kit = new(api);
    private const string ClinicalRefused = "Only doctors and nurses can see clinical notes.";
    private const string DoctorsReview = "Only doctors can review results.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode} {text}");
        if (detail is not null) Assert.Equal(detail, JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json).GetProperty("detail").GetString());
    }

    // ---------- Staff invites name the clinic role ----------

    private Task<Notification> InviteEmailAsync(string email) => api.WithDbAsync(db => db.Notifications
        .Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).OrderByDescending(n => n.CreatedAt).FirstAsync(Ct));

    private async Task<JsonElement> PreviewAsync(Notification invite)
    {
        var link = invite.Payload["link"];
        return await Json(await api.CreateClient().GetAsync($"/api/public/invites/{link[(link.LastIndexOf('/') + 1)..]}", Ct));
    }

    [Fact]
    public async Task Care_invites_name_the_clinic_role_in_the_email_and_the_preview()
    {
        var clinic = await kit.ClinicAsync("Invite Role Clinic");
        await api.MakeProAsync(clinic.TenantId, "care", "clinic");
        foreach (var (role, clinicalRole, label) in new (string, string?, string)[]
                 {
                     ("Staff", "Nurse", "Nurse"), ("Staff", "Doctor", "Doctor"), ("Staff", "FrontDesk", "Front desk"), ("Accountant", null, "Cashier"),
                     ("Admin", null, "Admin"), ("Admin", "Doctor", "Admin (doctor)"), ("Admin", "Nurse", "Admin (nurse)"),
                 })
        {
            var email = $"invite-role-{Guid.NewGuid():N}@example.com";
            await Json(await clinic.Owner.PostAsJsonAsync("/api/team/invites", new { email, role, clinicalRole }, Ct));
            var n = await InviteEmailAsync(email);
            Assert.Equal(label, n.Payload["role"]);
            var rendered = EmailTemplates.Render(n, "CareLink");
            Assert.Contains($"to join Invite Role Clinic as {label}.", rendered.Text);
            Assert.DoesNotContain("as Staff", rendered.Text);

            var preview = await PreviewAsync(n);
            Assert.Equal(label, preview.GetProperty("roleLabel").GetString());
            Assert.Equal(role, preview.GetProperty("role").GetString()); // the platform role is still there for other clients
        }
    }

    [Fact]
    public async Task Invoices_team_invite_is_unchanged()
    {
        var (owner, auth) = await api.SignupInvoicesAsync("InviteUnchanged");
        await api.MakeProAsync(auth.GetProperty("tenant").GetProperty("id").GetGuid(), "invoices", "business"); // Free and Pro have one seat
        var email = $"invoices-staff-{Guid.NewGuid():N}@example.com";
        await Json(await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Staff" }, Ct));
        var n = await InviteEmailAsync(email);
        Assert.Equal("Staff", n.Payload["role"]);
        Assert.Contains("InviteUnchanged Studio as Staff.", EmailTemplates.Render(n, "InvoiceLink").Text);
        var preview = await PreviewAsync(n);
        Assert.Equal("Staff", preview.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("roleLabel").ValueKind);
    }

    // ---------- Results wait for a doctor's review ----------

    private sealed record ReviewWorld(CareKit.Clinic Clinic, HttpClient Doctor, Guid DoctorId, HttpClient Nurse, HttpClient Desk, HttpClient Portal,
        Guid PortalUserId, Guid PatientId, Guid AdmissionId);

    private async Task<ReviewWorld> ReviewWorldAsync(string name)
    {
        var clinic = await kit.ClinicAsync(name);
        var (doctor, doctorId) = await kit.StaffAsync(clinic, "Doctor");
        var (nurse, _) = await kit.StaffAsync(clinic, "Nurse");
        var (desk, _) = await kit.StaffAsync(clinic, "FrontDesk");
        var patientId = (await kit.PatientAsync(clinic, "Reviewee", "Okoro")).GetProperty("id").GetGuid();
        var admissionId = await kit.AdmitAsync(doctor, patientId);

        // A family portal account linked to the patient (a link-only invite from the director, accepted).
        var link = (await Json(await clinic.Owner.PostAsJsonAsync($"/api/care/patients/{patientId}/invites", new { }, Ct))).GetProperty("link").GetString()!;
        var portal = api.CreateClient();
        var portalEmail = $"review-family-{Guid.NewGuid():N}@example.com";
        var auth = await Json(await api.SignUpAsync(portal, "/api/auth/patient/signup",
            new { fullName = "Review Family", email = portalEmail, password = "password123" }, Ct));
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        await Json(await portal.PostAsJsonAsync($"/api/care-portal/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept", new { relationship = "Parent" }, Ct));
        var portalUserId = await api.WithDbAsync(db => db.Users.Where(u => u.Email == portalEmail).Select(u => u.Id).FirstAsync(Ct));
        return new ReviewWorld(clinic, doctor, doctorId, nurse, desk, portal, portalUserId, patientId, admissionId);
    }

    private async Task<Guid> RequestTestAsync(ReviewWorld w, string description) =>
        (await Json(await w.Doctor.PostAsJsonAsync("/api/care/tests", new { patientId = w.PatientId, admissionId = w.AdmissionId, kind = "Lab", description }, Ct)))
        .GetProperty("id").GetGuid();

    private Task<int> ResultNoticesAsync(ReviewWorld w) => api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
        .CountAsync(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.result_ready", Ct));

    private async Task<List<JsonElement>> PortalResultsAsync(ReviewWorld w) =>
        (await Json(await w.Portal.GetAsync($"/api/care-portal/patients/{w.PatientId}/record", Ct))).GetProperty("results").EnumerateArray().ToList();

    private static Task<HttpResponseMessage> ReviewAsync(HttpClient client, Guid testId) => client.PostAsync($"/api/care/tests/{testId}/review", null, Ct);

    [Fact]
    public async Task A_result_shows_in_the_portal_and_tells_the_patient_only_after_a_doctor_reviews_it()
    {
        var w = await ReviewWorldAsync("Result Review Clinic");
        var testId = await RequestTestAsync(w, "Reviewtest smear");

        // Not resulted yet: nothing to review.
        await AssertProblemAsync(await ReviewAsync(w.Doctor, testId), HttpStatusCode.Conflict);

        // A nurse records it: hidden from the portal, nobody is told.
        await Json(await w.Nurse.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Reviewresult negative" }, Ct));
        Assert.Empty(await PortalResultsAsync(w));
        Assert.Equal(0, await ResultNoticesAsync(w));
        var portalRecord = await (await w.Portal.GetAsync($"/api/care-portal/patients/{w.PatientId}/record", Ct)).Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Reviewresult", portalRecord);
        Assert.DoesNotContain("Reviewtest", portalRecord);

        // Staff still see it, awaiting review; the doctors' list holds it (ids, the patient and when: no test text or result).
        var staffResult = Assert.Single((await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct))).GetProperty("results").EnumerateArray());
        Assert.Equal("Reviewresult negative", staffResult.GetProperty("resultText").GetString());
        Assert.Equal(JsonValueKind.Null, staffResult.GetProperty("reviewedAt").ValueKind);
        var awaitingBody = await (await w.Doctor.GetAsync("/api/care/tests/awaiting-review", Ct)).Content.ReadAsStringAsync(Ct);
        var awaiting = JsonSerializer.Deserialize<JsonElement>(awaitingBody, ApiFixture.Json);
        var row = Assert.Single(awaiting.EnumerateArray(), r => r.GetProperty("id").GetGuid() == testId);
        Assert.Equal(w.PatientId, row.GetProperty("patientId").GetGuid());
        Assert.DoesNotContain("Reviewtest", awaitingBody);
        Assert.DoesNotContain("Reviewresult", awaitingBody);

        // A nurse and the front desk can't review, or read the list.
        await AssertProblemAsync(await ReviewAsync(w.Nurse, testId), HttpStatusCode.Forbidden, DoctorsReview);
        await AssertProblemAsync(await ReviewAsync(w.Desk, testId), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await w.Nurse.GetAsync("/api/care/tests/awaiting-review", Ct), HttpStatusCode.Forbidden, DoctorsReview);
        await AssertProblemAsync(await w.Desk.GetAsync("/api/care/tests/awaiting-review", Ct), HttpStatusCode.Forbidden, ClinicalRefused);
        await AssertProblemAsync(await ReviewAsync(w.Doctor, Guid.NewGuid()), HttpStatusCode.NotFound);
        Assert.Empty(await PortalResultsAsync(w));
        Assert.Equal(0, await ResultNoticesAsync(w));

        // The doctor reviews: recorded with who and when, audited with ids only, and the patient's portal account is told once.
        var reviewed = await Json(await ReviewAsync(w.Doctor, testId));
        Assert.Equal(w.DoctorId, reviewed.GetProperty("reviewedBy").GetGuid());
        Assert.Equal("Doctor Staff", reviewed.GetProperty("reviewedByName").GetString());
        var reviewedAt = reviewed.GetProperty("reviewedAt").GetDateTimeOffset();
        // Idempotent: again (or by the director) changes nothing and tells nobody again.
        var again = await Json(await ReviewAsync(w.Clinic.Owner, testId));
        Assert.Equal(w.DoctorId, again.GetProperty("reviewedBy").GetGuid());
        Assert.Equal(reviewedAt, again.GetProperty("reviewedAt").GetDateTimeOffset());

        var notices = await api.WithDbAsync(db => db.UserNotifications.IgnoreQueryFilters()
            .Where(n => n.TenantId == w.Clinic.TenantId && n.Kind == "care.result_ready").ToListAsync(Ct));
        var notice = Assert.Single(notices);
        Assert.Equal(w.PortalUserId, notice.UserId);
        Assert.Equal("A new result for Reviewee is ready in your CareLink portal.", notice.Body);

        var audits = await api.WithDbAsync(db => db.AuditLogs.Where(a => a.Action == "care.result_reviewed" && a.EntityId == testId.ToString()).ToListAsync(Ct));
        var audit = Assert.Single(audits);
        Assert.Equal(w.DoctorId, audit.ActorId);
        Assert.Equal(w.Clinic.TenantId, audit.TenantId);
        Assert.DoesNotContain("Reviewtest", audit.AfterJson ?? "");
        Assert.DoesNotContain("Reviewresult", audit.AfterJson ?? "");

        var shown = Assert.Single(await PortalResultsAsync(w));
        Assert.Equal("Reviewresult negative", shown.GetProperty("resultText").GetString());
        var onRecord = Assert.Single((await Json(await w.Doctor.GetAsync($"/api/care/patients/{w.PatientId}/record", Ct))).GetProperty("results").EnumerateArray());
        Assert.Equal(w.DoctorId, onRecord.GetProperty("reviewedBy").GetGuid());
        Assert.Equal("Doctor Staff", onRecord.GetProperty("reviewedByName").GetString());
        var after = await Json(await w.Doctor.GetAsync("/api/care/tests/awaiting-review", Ct));
        Assert.DoesNotContain(after.EnumerateArray(), r => r.GetProperty("id").GetGuid() == testId);
    }

    [Fact]
    public async Task The_director_reviews_too()
    {
        var w = await ReviewWorldAsync("Director Review Clinic");
        var testId = await RequestTestAsync(w, "Directortest panel");
        await Json(await w.Doctor.PostAsJsonAsync($"/api/care/tests/{testId}/result", new { text = "Doctor-recorded result" }, Ct));
        // Recorded by a doctor is still not reviewed.
        Assert.Empty(await PortalResultsAsync(w));
        Assert.Equal(0, await ResultNoticesAsync(w));
        await Json(await ReviewAsync(w.Clinic.Owner, testId));
        Assert.Single(await PortalResultsAsync(w));
        Assert.Equal(1, await ResultNoticesAsync(w));
    }
}
