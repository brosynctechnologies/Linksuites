using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Final fix pass B (user decision I3), platform-wide: every password sign-up is confirmed with a 6-digit emailed code before the account
/// can be used; a confirmed password is never removed; a reset confirms and ends earlier sessions; the first-confirmation takeover is left
/// only for accounts that were never confirmed before this change; a second sign-up on an unconfirmed address can't hijack it.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class AuthConfirmTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public const string ConfirmFirst = "Confirm your email first. Enter the 6-digit code we emailed you, or ask for a new one.";

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {r.RequestMessage?.Method} {r.RequestMessage?.RequestUri} {await r.Content.ReadAsStringAsync(Ct)}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage r, HttpStatusCode status, string? detail = null, string? code = null)
    {
        var text = await r.Content.ReadAsStringAsync(Ct);
        Assert.True(r.StatusCode == status, $"expected {(int)status}, got {(int)r.StatusCode} {r.RequestMessage?.RequestUri} {text}");
        var problem = JsonSerializer.Deserialize<JsonElement>(text, ApiFixture.Json);
        if (detail is not null) Assert.Equal(detail, problem.GetProperty("detail").GetString());
        if (code is not null) Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static string NewEmail(string what) => $"{what}-{Guid.NewGuid():N}@example.com";

    private static object Business(string email, string password, string product = "invoices") =>
        new { fullName = "Confirm Owner", email, password, country = "NG", product, businessName = $"Confirm Biz {Guid.NewGuid():N}"[..24] };

    private Task<int> CodesSentAsync(string email) => api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "otp" && n.Recipient == email, Ct));

    private Task<(bool Confirmed, DateTimeOffset? At)> StateAsync(string email) => api.WithDbAsync(db => db.Users
        .Where(u => u.NormalizedEmail == email.ToUpperInvariant())
        .Select(u => new ValueTuple<bool, DateTimeOffset?>(u.EmailConfirmed, u.EmailConfirmedAt)).FirstAsync(Ct));

    private HttpClient Bearer(JsonElement auth)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<string> RequestCodeAsync(string email)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await api.CreateClient().PostAsJsonAsync("/api/auth/otp/request", new { email }, Ct)).StatusCode);
        return await api.LatestCodeAsync(email);
    }

    private async Task ResetAsync(string email, string newPassword)
    {
        var anon = api.CreateClient();
        (await anon.PostAsJsonAsync("/api/auth/password/forgot", new { email }, Ct)).EnsureSuccessStatusCode();
        var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "password.reset" && n.Recipient == email)
            .OrderByDescending(n => n.CreatedAt).FirstAsync(Ct))).Payload["link"];
        var fragment = System.Web.HttpUtility.ParseQueryString(link[(link.IndexOf('#') + 1)..]);
        (await anon.PostAsJsonAsync("/api/auth/password/reset", new { email = fragment["email"], token = fragment["token"], newPassword }, Ct)).EnsureSuccessStatusCode();
    }

    // ---------- Codes at sign-up ----------

    /// <summary>A business sign-up answers "confirm your email" with no session, emails a code at once, and refuses password login until the code is entered.</summary>
    [Fact]
    public async Task Business_signup_gives_no_session_until_the_code_is_entered()
    {
        var email = NewEmail("biz-confirm");
        var anon = api.CreateClient();
        var pending = await Json(await anon.PostAsJsonAsync("/api/auth/signup", Business(email, "password123"), Ct));
        Assert.True(pending.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal(email, pending.GetProperty("email").GetString());
        Assert.False(pending.TryGetProperty("accessToken", out _));
        Assert.False(pending.TryGetProperty("refreshToken", out _));
        Assert.Equal(1, await CodesSentAsync(email));
        Assert.Equal(0, await api.WithDbAsync(db => db.RefreshTokens.CountAsync(t => db.Users.Any(u => u.Id == t.UserId && u.Email == email), Ct)));

        // Unusable until confirmed: password login is refused with the sentence, and so is a wrong code.
        await ProblemAsync(await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "password123", product = "invoices" }, Ct),
            HttpStatusCode.Forbidden, ConfirmFirst, "email_unconfirmed");
        var code = await api.LatestCodeAsync(email);
        var wrong = code == "000000" ? "111111" : "000000";
        await ProblemAsync(await anon.PostAsJsonAsync("/api/auth/signup/confirm", new { email, code = wrong, password = "password123", product = "invoices" }, Ct),
            HttpStatusCode.BadRequest, "That code is wrong or has expired. Request a new one.");
        Assert.Equal((false, (DateTimeOffset?)null), await StateAsync(email));

        var session = await Json(await anon.PostAsJsonAsync("/api/auth/signup/confirm", new { email, code, password = "password123", product = "invoices" }, Ct));
        Assert.Equal("invoices", session.GetProperty("tenant").GetProperty("product").GetString());
        await Json(await Bearer(session).GetAsync("/api/me", Ct));
        var (confirmed, at) = await StateAsync(email);
        Assert.True(confirmed);
        Assert.NotNull(at);
        // The code is used up, and the password chosen at sign-up now signs in.
        await ProblemAsync(await anon.PostAsJsonAsync("/api/auth/signup/confirm", new { email, code, password = "password123", product = "invoices" }, Ct),
            HttpStatusCode.BadRequest);
        await Json(await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "password123", product = "invoices" }, Ct));
    }

    /// <summary>The "your account is ready" welcome is sent once the email is confirmed, not at an unconfirmed sign-up.</summary>
    [Fact]
    public async Task Welcome_email_waits_for_the_code()
    {
        var email = NewEmail("biz-welcome");
        var anon = api.CreateClient();
        Task<int> WelcomesAsync() => api.WithDbAsync(db => db.Notifications.CountAsync(n => n.TemplateKey == "welcome" && n.Recipient == email, Ct));

        await Json(await anon.PostAsJsonAsync("/api/auth/signup", Business(email, "password123"), Ct));
        Assert.Equal(0, await WelcomesAsync());

        var code = await api.LatestCodeAsync(email);
        await Json(await anon.PostAsJsonAsync("/api/auth/signup/confirm", new { email, code, password = "password123", product = "invoices" }, Ct));
        Assert.Equal(1, await WelcomesAsync());

        // A later code sign-in of the confirmed account sends no second welcome.
        var again = await RequestCodeAsync(email);
        await Json(await anon.PostAsJsonAsync("/api/auth/signup/confirm", new { email, code = again, password = "password123", product = "invoices" }, Ct));
        Assert.Equal(1, await WelcomesAsync());
    }

    /// <summary>The same for every customer sign-up: learners, parents, patients and home-seekers.</summary>
    [Theory]
    [InlineData("learner")]
    [InlineData("parent")]
    [InlineData("patient")]
    [InlineData("seeker")]
    public async Task Customer_signup_gives_no_session_until_the_code_is_entered(string kind)
    {
        var email = NewEmail($"{kind}-confirm");
        var anon = api.CreateClient();
        var pending = await Json(await anon.PostAsJsonAsync($"/api/auth/{kind}/signup", new { fullName = "Confirm Customer", email, password = "password123" }, Ct));
        Assert.True(pending.GetProperty("confirmationRequired").GetBoolean());
        Assert.False(pending.TryGetProperty("accessToken", out _));
        Assert.Equal(1, await CodesSentAsync(email));
        await ProblemAsync(await anon.PostAsJsonAsync($"/api/auth/{kind}/login", new { email, password = "password123" }, Ct),
            HttpStatusCode.Forbidden, ConfirmFirst, "email_unconfirmed");
        // The business sign-in says the same (one account across products).
        await ProblemAsync(await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" }, Ct), HttpStatusCode.Forbidden, ConfirmFirst);

        // A short password is refused before the code is used, so the same code still confirms.
        var code = await api.LatestCodeAsync(email);
        await ProblemAsync(await anon.PostAsJsonAsync($"/api/auth/{kind}/signup/confirm", new { email, code, password = "short" }, Ct), HttpStatusCode.BadRequest);
        var session = await Json(await anon.PostAsJsonAsync($"/api/auth/{kind}/signup/confirm", new { email, code, password = "password123" }, Ct));
        Assert.Equal(JsonValueKind.Null, session.GetProperty("tenant").ValueKind);
        Assert.True((await StateAsync(email)).Confirmed);
        await Json(await anon.PostAsJsonAsync($"/api/auth/{kind}/login", new { email, password = "password123" }, Ct));
    }

    /// <summary>A customer who closed the page before entering the code comes back through the login page: the code plus the password they type.</summary>
    [Fact]
    public async Task Login_page_confirms_with_a_new_code_and_the_typed_password()
    {
        var email = NewEmail("late-confirm");
        var anon = api.CreateClient();
        await Json(await anon.PostAsJsonAsync("/api/auth/parent/signup", new { fullName = "Late Parent", email, password = "password123" }, Ct));
        await ProblemAsync(await anon.PostAsJsonAsync("/api/auth/parent/login", new { email, password = "password123" }, Ct), HttpStatusCode.Forbidden, code: "email_unconfirmed");
        var code = await RequestCodeAsync(email);
        await Json(await anon.PostAsJsonAsync("/api/auth/parent/signup/confirm", new { email, code, password = "password123" }, Ct));
        await Json(await anon.PostAsJsonAsync("/api/auth/parent/login", new { email, password = "password123" }, Ct));
    }

    // ---------- Confirmed passwords are kept ----------

    /// <summary>After confirming, a code sign-in (business or customer) never removes the password, and the earlier sessions keep working.</summary>
    [Fact]
    public async Task Confirmed_password_survives_a_later_code_sign_in()
    {
        var owner = NewEmail("kept-owner");
        var ownerSession = await Json(await api.SignUpAsync(api.CreateClient(), "/api/auth/signup", Business(owner, "password123"), Ct));
        var byCode = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/otp/verify", new { email = owner, code = await RequestCodeAsync(owner), product = "invoices" }, Ct));
        Assert.Equal(ownerSession.GetProperty("tenant").GetProperty("id").GetGuid(), byCode.GetProperty("tenant").GetProperty("id").GetGuid());
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = owner, password = "password123", product = "invoices" }, Ct));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = ownerSession.GetProperty("refreshToken").GetString() }, Ct));

        var patient = NewEmail("kept-patient");
        var patientSession = await Json(await api.SignUpAsync(api.CreateClient(), "/api/auth/patient/signup", new { fullName = "Kept Patient", email = patient, password = "password123" }, Ct));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/otp/verify", new { email = patient, code = await RequestCodeAsync(patient) }, Ct));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email = patient, password = "password123" }, Ct));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = patientSession.GetProperty("refreshToken").GetString() }, Ct));
    }

    // ---------- Resets confirm too ----------

    /// <summary>
    /// A reset proves the mailbox: it confirms an unconfirmed account and ends every earlier session, so a squatter's session (here an account
    /// from before this change, which could still sign in with its password) dies; the reset password then survives a later code sign-in.
    /// </summary>
    [Fact]
    public async Task Password_reset_confirms_the_account_and_revokes_earlier_sessions()
    {
        var email = NewEmail("reset-confirms");
        await api.LegacyUnconfirmedAccountAsync(email, "squatter-pass");
        var squatter = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email, password = "squatter-pass" }, Ct));
        await Task.Delay(TimeSpan.FromMilliseconds(1100), Ct); // the squatter's token falls in an earlier second than the confirmation (iat)

        await ResetAsync(email, "owner-pass-1");
        var (confirmed, at) = await StateAsync(email);
        Assert.True(confirmed);
        Assert.NotNull(at);
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = squatter.GetProperty("refreshToken").GetString() }, Ct)).IsSuccessStatusCode);
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email, password = "squatter-pass" }, Ct)).IsSuccessStatusCode);
        // The squatter's still-unexpired access token is older than the confirmation: the portal refuses it.
        Assert.Empty((await Json(await Bearer(squatter).GetAsync("/api/care-portal/patients", Ct))).EnumerateArray());

        // Already confirmed by the reset: a later code sign-in is no takeover.
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/otp/verify", new { email, code = await RequestCodeAsync(email) }, Ct));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email, password = "owner-pass-1" }, Ct));
    }

    /// <summary>A sign-up that was never confirmed can be finished with a reset instead of the code: the reset password signs in.</summary>
    [Fact]
    public async Task Password_reset_finishes_an_unconfirmed_signup()
    {
        var email = NewEmail("reset-pending");
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/seeker/signup", new { fullName = "Pending Seeker", email, password = "password123" }, Ct));
        await ResetAsync(email, "password456");
        Assert.True((await StateAsync(email)).Confirmed);
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/seeker/login", new { email, password = "password456" }, Ct));
    }

    // ---------- No silent hijack ----------

    /// <summary>
    /// A squatter signs up on someone's address. The owner's sign-up on the same address is answered the same way and a new code goes to the
    /// mailbox; nobody can use the account without it; whoever enters the code sets the password. A squatter who signs up again after the owner
    /// (to change the pending password) changes nothing either.
    /// </summary>
    [Fact]
    public async Task Second_signup_on_an_unconfirmed_address_cannot_hijack_it()
    {
        var email = NewEmail("hijack");
        var squatter = api.CreateClient();
        var first = await Json(await squatter.PostAsJsonAsync("/api/auth/signup", Business(email, "squatter-pass"), Ct));
        Assert.True(first.GetProperty("confirmationRequired").GetBoolean());

        var owner = api.CreateClient();
        var second = await Json(await owner.PostAsJsonAsync("/api/auth/signup", Business(email, "owner-pass-1"), Ct));
        Assert.True(second.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal(2, await CodesSentAsync(email));
        // The squatter tries again after the owner, and guesses at the code.
        await Json(await squatter.PostAsJsonAsync("/api/auth/signup", Business(email, "squatter-pass"), Ct));
        foreach (var guess in new[] { "123456", "654321" })
            if (guess != await api.LatestCodeAsync(email))
                await ProblemAsync(await squatter.PostAsJsonAsync("/api/auth/signup/confirm", new { email, code = guess, password = "squatter-pass", product = "invoices" }, Ct),
                    HttpStatusCode.BadRequest);
        foreach (var password in new[] { "squatter-pass", "owner-pass-1" })
            await ProblemAsync(await squatter.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct), HttpStatusCode.Forbidden, code: "email_unconfirmed");

        // The mailbox's owner enters the code: their password is the account's, the squatter's is not.
        var session = await Json(await owner.PostAsJsonAsync("/api/auth/signup/confirm",
            new { email, code = await api.LatestCodeAsync(email), password = "owner-pass-1", product = "invoices" }, Ct));
        Assert.Equal("invoices", session.GetProperty("tenant").GetProperty("product").GetString());
        await Json(await owner.PostAsJsonAsync("/api/auth/login", new { email, password = "owner-pass-1" }, Ct));
        await ProblemAsync(await squatter.PostAsJsonAsync("/api/auth/login", new { email, password = "squatter-pass" }, Ct),
            HttpStatusCode.BadRequest, "Email or password is incorrect.");
        // Confirmed now: a further sign-up needs the owner's password.
        await ProblemAsync(await squatter.PostAsJsonAsync("/api/auth/signup", Business(email, "squatter-pass", "booking"), Ct), HttpStatusCode.BadRequest);
        await ProblemAsync(await squatter.PostAsJsonAsync("/api/auth/parent/signup", new { fullName = "Squatter", email, password = "squatter-pass" }, Ct),
            HttpStatusCode.BadRequest);
    }

    /// <summary>The same for customers: the second sign-up re-sends the code, and the code's holder sets the password.</summary>
    [Fact]
    public async Task Second_customer_signup_cannot_hijack_either()
    {
        var email = NewEmail("hijack-customer");
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/signup", new { fullName = "Squatter", email, password = "squatter-pass" }, Ct));
        var again = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/signup", new { fullName = "Real Owner", email, password = "owner-pass-1" }, Ct));
        Assert.True(again.GetProperty("confirmationRequired").GetBoolean());
        Assert.Equal(2, await CodesSentAsync(email));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/signup/confirm",
            new { email, code = await api.LatestCodeAsync(email), password = "owner-pass-1" }, Ct));
        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email, password = "owner-pass-1" }, Ct));
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/patient/login", new { email, password = "squatter-pass" }, Ct)).IsSuccessStatusCode);
    }

    // ---------- The takeover stays only for old accounts ----------

    /// <summary>
    /// An account from before this change (password, never confirmed) still signs in with its password, and its first code confirmation still
    /// takes it over: the password is removed, the stamp rotated, the refresh tokens revoked.
    /// </summary>
    [Fact]
    public async Task Legacy_unconfirmed_account_is_still_taken_over_by_a_code_sign_in()
    {
        var email = NewEmail("legacy");
        var userId = await api.LegacyUnconfirmedAccountAsync(email);
        var old = await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/parent/login", new { email, password = "password123" }, Ct));
        var stamp = await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstAsync(Ct));

        await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/parent/otp/verify", new { email, code = await RequestCodeAsync(email) }, Ct));
        Assert.True((await StateAsync(email)).Confirmed);
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/parent/login", new { email, password = "password123" }, Ct)).IsSuccessStatusCode);
        Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = old.GetProperty("refreshToken").GetString() }, Ct)).IsSuccessStatusCode);
        Assert.NotEqual(stamp, await api.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).FirstAsync(Ct)));
    }

    // ---------- Invites still hold ----------

    /// <summary>
    /// A team invite proves the mailbox: a new account made from it signs in with its password at once; an address that only has an unconfirmed
    /// sign-up joins too, and the invitee's password becomes the account's (whoever proves the mailbox sets it).
    /// </summary>
    [Fact]
    public async Task Team_invites_still_confirm_and_keep_the_password()
    {
        var clinic = await new CareKit(api).ClinicAsync("Confirm Invite Clinic");
        await api.MakeProAsync(clinic.TenantId, "care", "clinic");
        var owner = clinic.Owner;
        foreach (var pendingFirst in new[] { false, true })
        {
            var email = NewEmail(pendingFirst ? "invite-pending" : "invite-new");
            if (pendingFirst)
                await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/seeker/signup", new { fullName = "Earlier Signup", email, password = "squatter-pass" }, Ct));
            (await owner.PostAsJsonAsync("/api/team/invites", new { email, role = "Admin" }, Ct)).EnsureSuccessStatusCode();
            var link = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "team.invite" && n.Recipient == email).FirstAsync(Ct))).Payload["link"];
            var token = link[(link.LastIndexOf('/') + 1)..];
            var preview = await Json(await api.CreateClient().GetAsync($"/api/public/invites/{token}", Ct));
            Assert.False(preview.GetProperty("hasAccount").GetBoolean()); // an unconfirmed sign-up is not an account to prove with a password
            var joined = await Json(await api.CreateClient().PostAsJsonAsync($"/api/auth/invites/{token}/accept", new { fullName = "Invited Staff", password = "invitee-pass" }, Ct));
            Assert.Equal("Admin", joined.GetProperty("tenant").GetProperty("role").GetString());
            Assert.True((await StateAsync(email)).Confirmed);
            await Json(await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "invitee-pass", product = "care" }, Ct));
            Assert.False((await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "squatter-pass" }, Ct)).IsSuccessStatusCode);
        }
    }
}
