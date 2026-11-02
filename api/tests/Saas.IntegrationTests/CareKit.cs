using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Saas.IntegrationTests;

/// <summary>Shared setup for CareLink tests: clinics and their staff. Later tasks add patients, portal accounts and admissions.</summary>
public sealed class CareKit(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record Clinic(HttpClient Owner, Guid TenantId, string Slug);

    /// <summary>Signs up a new clinic (product "care"); the returned client is the medical director (Owner).</summary>
    public async Task<Clinic> ClinicAsync(string name)
    {
        var client = api.CreateClient();
        var email = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Director", email, password = "password123", country = "NG", product = "care", businessName = name,
        }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var tenant = auth.GetProperty("tenant");
        return new Clinic(client, tenant.GetProperty("id").GetGuid(), tenant.GetProperty("slug").GetString()!);
    }

    /// <summary>
    /// Invites a staff member in a clinic role, accepts the invite and returns their signed-in client.
    /// <paramref name="clinicRole"/> is one of "Doctor", "Nurse", "FrontDesk" (Staff with that clinical role), "Cashier" (Accountant) or "Admin".
    /// </summary>
    public async Task<(HttpClient Client, Guid UserId)> StaffAsync(Clinic clinic, string clinicRole)
    {
        var (role, clinicalRole) = clinicRole switch
        {
            "Doctor" => ("Staff", "Doctor"),
            "Nurse" => ("Staff", "Nurse"),
            "FrontDesk" => ("Staff", "FrontDesk"),
            "Cashier" => ("Accountant", (string?)null),
            "Admin" => ("Admin", (string?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(clinicRole), clinicRole, "Use Doctor, Nurse, FrontDesk, Cashier or Admin."),
        };
        await api.MakeProAsync(clinic.TenantId, "care", "clinic"); // Free has three seats
        var email = $"{clinicRole.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com";
        var invite = await clinic.Owner.PostAsJsonAsync("/api/team/invites", new { email, role, clinicalRole }, Ct);
        if (!invite.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)invite.StatusCode} {await invite.Content.ReadAsStringAsync(Ct)}");
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
        var response = await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = $"{clinicRole} Staff", password = "password123" }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accepted.GetProperty("accessToken").GetString());
        var userId = await api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync(Ct));
        return (client, userId);
    }

    /// <summary>Adds a price-list item as the medical director and returns its id. <paramref name="category"/> is a <c>PriceCategory</c> name.</summary>
    public async Task<Guid> PriceItemAsync(Clinic clinic, string name, string category, long priceMinor)
    {
        var response = await clinic.Owner.PostAsJsonAsync("/api/care/price-list", new { name, category, priceMinor }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Seeds a care relationship directly (Task 5 and Task 8 write these from triage and admission). <paramref name="kind"/> is a
    /// <c>CareAssignmentKind</c>; <paramref name="ended"/> writes an ended assignment, which no longer opens the record.
    /// </summary>
    public Task AssignAsync(Clinic clinic, Guid patientId, Guid staffUserId,
        Saas.Domain.Care.CareAssignmentKind kind = Saas.Domain.Care.CareAssignmentKind.VisitDoctor, bool ended = false) => api.WithDbAsync(db =>
    {
        db.CareAssignments.Add(new Saas.Domain.Care.CareAssignment
        {
            TenantId = clinic.TenantId, PatientId = patientId, StaffUserId = staffUserId, Kind = kind,
            EndedAt = ended ? DateTimeOffset.UtcNow.AddMinutes(-1) : null,
        });
        return db.SaveChangesAsync(Ct);
    });

    /// <summary>Checks a patient in (a new visit) through <paramref name="client"/> and returns the visit JSON.</summary>
    public async Task<JsonElement> CheckInAsync(HttpClient client, Guid patientId, Guid? appointmentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/care/visits", new { patientId, appointmentId }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>
    /// Adds a ward as the medical director (a daily Bed price item is created for it) and returns its id. The clinic must be on a plan
    /// with beds for <see cref="BedAsync"/> (StaffAsync moves it to the Clinic plan).
    /// </summary>
    public async Task<Guid> WardAsync(Clinic clinic, string name, long? minDepositMinor = null, IEnumerable<Guid>? nurseUserIds = null, long bedPriceMinor = 1_000_000)
    {
        var itemId = await PriceItemAsync(clinic, $"{name} bed day", "Bed", bedPriceMinor);
        var response = await clinic.Owner.PostAsJsonAsync("/api/care/wards",
            new { name, kind = "General", bedPriceItemId = itemId, minDepositMinor, nurseUserIds = nurseUserIds?.ToArray() ?? [] }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Adds an active bed to a ward as the medical director and returns its id.</summary>
    public async Task<Guid> BedAsync(Clinic clinic, Guid wardId, string label)
    {
        var response = await clinic.Owner.PostAsJsonAsync($"/api/care/wards/{wardId}/beds", new { label }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Admits a patient through <paramref name="doctor"/> (a doctor or the director) and returns the admission id. Without the caller's own
    /// <paramref name="consultationId"/> a doctor admits through emergency access, so <paramref name="emergencyReason"/> goes in the
    /// <c>X-Care-Reason</c> header (ignored for the director and for the caller's own consultation).
    /// </summary>
    public async Task<Guid> AdmitAsync(HttpClient doctor, Guid patientId, string reason = "Severe malaria with vomiting", string admittingDiagnosis = "Severe malaria",
        Guid? consultationId = null, string? emergencyReason = "Admitted through casualty")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/care/admissions")
        {
            Content = JsonContent.Create(new { patientId, consultationId, reason, admittingDiagnosis }),
        };
        if (emergencyReason is not null) request.Headers.Add("X-Care-Reason", Uri.EscapeDataString(emergencyReason));
        var response = await doctor.SendAsync(request, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("id").GetGuid();
    }

    /// <summary>Assigns a bed with an optional cash deposit through <paramref name="client"/>; returns the response (not checked).</summary>
    public Task<HttpResponseMessage> AssignBedAsync(HttpClient client, Guid admissionId, Guid bedId, long depositMinor = 0, bool overrideByOwner = false) =>
        client.PostAsJsonAsync($"/api/care/admissions/{admissionId}/bed",
            new { bedId, depositMinor, method = depositMinor > 0 ? "Cash" : null, reference = (string?)null, overrideByOwner }, Ct);

    /// <summary>Registers a patient as the medical director and returns the patient JSON (from <c>{ patient, possibleDuplicates }</c>).</summary>
    public async Task<JsonElement> PatientAsync(Clinic clinic, string firstName = "Ada", string lastName = "Obi", string dateOfBirth = "1990-04-12",
        string? phone = null, string sex = "Female")
    {
        var response = await clinic.Owner.PostAsJsonAsync("/api/care/patients", new { firstName, lastName, sex, dateOfBirth, phone }, Ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct)).GetProperty("patient");
    }
}
