using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>Shared setup for school tests: schools, their staff, students, guardians and parent accounts. Later tasks add student logins.</summary>
public sealed class SchoolKit(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed record School(HttpClient Owner, Guid TenantId, string Slug);

    public async Task<School> SchoolAsync(string name)
    {
        var client = api.CreateClient();
        var email = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            fullName = $"{name} Proprietor", email, password = "password123", country = "NG", product = "school", businessName = name,
        }, Ct);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        var tenant = auth.GetProperty("tenant");
        return new School(client, tenant.GetProperty("id").GetGuid(), tenant.GetProperty("slug").GetString()!);
    }

    /// <summary>The user id of the school's Owner (the proprietor who signed up).</summary>
    public Task<Guid> OwnerIdAsync(School school) =>
        api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters()
            .Where(m => m.TenantId == school.TenantId && m.Role == MemberRole.Owner).Select(m => m.UserId).FirstAsync(Ct));

    /// <summary>Invites a staff member with the given role, accepts the invite and returns their signed-in client.</summary>
    public async Task<(HttpClient Client, Guid UserId)> StaffAsync(School school, MemberRole role, string jobTitle)
    {
        await api.MakeProAsync(school.TenantId, "school", "standard"); // Free has three seats
        var email = $"{jobTitle.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}@example.com";
        (await school.Owner.PostAsJsonAsync("/api/team/invites", new { email, role = role.ToString() }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
        var response = await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{link[(link.LastIndexOf('/') + 1)..]}/accept",
            new { fullName = $"{jobTitle} Staff", password = "password123" }, Ct);
        response.EnsureSuccessStatusCode();
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accepted.GetProperty("accessToken").GetString());
        var userId = await api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync(Ct));
        return (client, userId);
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>A class of the current session at JSS 1 (the level is created when missing).</summary>
    public async Task<Guid> ClassAsync(School school, string arm = "A")
    {
        var levels = (await OkJsonAsync(await school.Owner.GetAsync("/api/school/levels", Ct))).EnumerateArray().ToList();
        var level = levels.FirstOrDefault(l => l.GetProperty("name").GetString() == "JSS 1");
        var levelId = level.ValueKind != JsonValueKind.Undefined
            ? level.GetProperty("id").GetGuid()
            : (await OkJsonAsync(await school.Owner.PostAsJsonAsync("/api/school/levels", new { name = "JSS 1", stage = "Jss", order = 10, hasStudentLogins = true }, Ct))).GetProperty("id").GetGuid();
        var classes = (await OkJsonAsync(await school.Owner.GetAsync("/api/school/classes", Ct))).EnumerateArray().ToList();
        var existing = classes.FirstOrDefault(c => c.GetProperty("name").GetString() == $"JSS 1{arm}");
        if (existing.ValueKind != JsonValueKind.Undefined) return existing.GetProperty("id").GetGuid();
        return (await OkJsonAsync(await school.Owner.PostAsJsonAsync("/api/school/classes", new { levelId, arm }, Ct))).GetProperty("id").GetGuid();
    }

    /// <summary>Adds an active student to the class and returns their id.</summary>
    public async Task<Guid> StudentAsync(School school, Guid classId, string firstName, string lastName = "Obi") =>
        (await OkJsonAsync(await school.Owner.PostAsJsonAsync("/api/school/students",
            new { admissionNo = $"ADM-{Guid.NewGuid().ToString("N")[..8]}", firstName, lastName, gender = "Female", classId }, Ct))).GetProperty("id").GetGuid();

    /// <summary>Adds a guardian to the student (siblings share one when the phone or email matches) and returns the guardian id.</summary>
    public async Task<Guid> GuardianAsync(School school, Guid studentId, string fullName, string? phone, string? email)
    {
        var student = await OkJsonAsync(await school.Owner.PostAsJsonAsync($"/api/school/students/{studentId}/guardians",
            new { fullName, phone, email, relationship = "Mother" }, Ct));
        return student.GetProperty("guardians").EnumerateArray().Single(g => g.GetProperty("fullName").GetString() == fullName).GetProperty("guardianId").GetGuid();
    }

    /// <summary>The token at the end of an invite link.</summary>
    public static string TokenOf(string link) => link[(link.LastIndexOf('/') + 1)..];

    /// <summary>Signs up a parent (customer) account and returns its signed-in client.</summary>
    public async Task<(HttpClient Parent, Guid UserId, string Email)> ParentAccountAsync(string? email = null)
    {
        email ??= $"parent-{Guid.NewGuid():N}@example.com";
        var client = api.CreateClient();
        var auth = await OkJsonAsync(await client.PostAsJsonAsync("/api/auth/parent/signup", new { fullName = "Parent Account", email, password = "password123" }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth.GetProperty("user").GetProperty("id").GetGuid(), email);
    }

    public const string StudentPassword = "student-pass-1";

    /// <summary>Sets the student's password (the student's level must have logins), signs in on the school page and returns the student's signed-in client.</summary>
    public async Task<HttpClient> StudentLoginAsync(School school, Guid studentId)
    {
        var student = await OkJsonAsync(await school.Owner.GetAsync($"/api/school/students/{studentId}", Ct));
        await OkJsonAsync(await school.Owner.PostAsJsonAsync($"/api/school/students/{studentId}/login", new { password = StudentPassword }, Ct));
        var client = api.CreateClient();
        var auth = await OkJsonAsync(await client.PostAsJsonAsync($"/api/school/{school.Slug}/student-login",
            new { admissionNo = student.GetProperty("admissionNo").GetString(), password = StudentPassword }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>The school invites the guardian; the returned link's token.</summary>
    public async Task<string> InviteAsync(School school, Guid guardianId) =>
        TokenOf((await OkJsonAsync(await school.Owner.PostAsync($"/api/school/guardians/{guardianId}/invite", null, Ct))).GetProperty("link").GetString()!);

    /// <summary>The school's own Paystack secret key in tests; webhooks for the school are signed with it.</summary>
    public const string PaystackKey = "sk_test_integration_school_key";

    /// <summary>The Owner saves the school's Paystack key (paying online also needs a plan with online fees).</summary>
    public async Task SetPaystackKeyAsync(School school, string key = PaystackKey) =>
        await OkJsonAsync(await school.Owner.PutAsJsonAsync("/api/school/settings/paystack", new { secretKey = key }, Ct));

    /// <summary>Makes Paystack (the fake) report the charge as successful when the reference is verified.</summary>
    public void Charge(string reference, long amountMinor, string currency = "NGN", string? inAccountOfKey = null)
    {
        api.Merchant.Charges[reference] = new Saas.Application.Billing.VerifiedCharge(reference, amountMinor, currency, DateTimeOffset.UtcNow);
        if (inAccountOfKey is not null) api.Merchant.ChargeKeys[reference] = inAccountOfKey;
    }

    /// <summary>The body Paystack posts for a successful charge.</summary>
    public static string ChargeSuccessBody(string reference, long amountMinor, string currency = "NGN") =>
        $$$"""{"event":"charge.success","data":{"status":"success","reference":"{{{reference}}}","amount":{{{amountMinor}}},"currency":"{{{currency}}}","paid_at":"{{{DateTimeOffset.UtcNow:O}}}"}}""";

    public static string Sign(string body, string key = PaystackKey) =>
        Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA512.HashData(System.Text.Encoding.UTF8.GetBytes(key), System.Text.Encoding.UTF8.GetBytes(body)));

    /// <summary>Posts a charge.success webhook to the school's webhook URL, signed with the given key (null signature: unsigned).</summary>
    public async Task<HttpResponseMessage> WebhookAsync(Guid tenantId, string body, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/paystack/school/{tenantId}")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        if (signature is not null) request.Headers.Add("x-paystack-signature", signature);
        return await api.CreateClient().SendAsync(request, Ct);
    }

    /// <summary>Invites the guardian, signs up a new parent account and accepts the invite: the parent's signed-in client.</summary>
    public async Task<(HttpClient Parent, Guid UserId)> ParentAsync(School school, Guid guardianId)
    {
        var token = await InviteAsync(school, guardianId);
        var (parent, userId, _) = await ParentAccountAsync();
        await OkJsonAsync(await parent.PostAsync($"/api/school-invites/{token}/accept", null, Ct));
        return (parent, userId);
    }
}
