using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// Review fix round for plan bank transfers: money for a claim that is no longer Pending can still be booked ("confirm anyway"),
/// claims are rate limited and email the admins at most hourly, the claim must use the reference the page showed, and the emails go to
/// the right people with the right words.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class PlanBankTransferFixTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Note = "Found the transfer in the OPay app after the claim was withdrawn.";

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    private async Task<(HttpClient Owner, Guid TenantId)> BusinessAsync(string name)
    {
        var (owner, auth) = await api.SignupAsync(name.Replace(" ", ""));
        return (owner, auth.GetProperty("tenant").GetProperty("id").GetGuid());
    }

    private static async Task<string> ShownReferenceAsync(HttpClient owner) =>
        (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("bankTransfer").GetProperty("reference").GetString()!;

    /// <summary>Claims Pro with the reference the page shows (as the billing pages do).</summary>
    private static async Task<string> ClaimAsync(HttpClient owner) =>
        (await Json(await owner.PostAsJsonAsync("/api/billing/transfer", new { planCode = "pro", reference = await ShownReferenceAsync(owner) }, Ct)))
        .GetProperty("reference").GetString()!;

    private Task<Payment> PaymentAsync(string reference) =>
        api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.ProviderRef == reference, Ct));

    private async Task OfflineAsync(Func<Task> body)
    {
        await api.SetPaymentSettingsAsync(false);
        try { await body(); }
        finally { await api.ResetPaymentSettingsAsync(); }
    }

    private Task<HttpResponseMessage> ConfirmAsync(HttpClient admin, Guid id, object? body = null) =>
        body is null ? admin.PostAsync($"/api/platform/transfers/{id}/confirm", null, Ct) : admin.PostAsJsonAsync($"/api/platform/transfers/{id}/confirm", body, Ct);

    // I1: money for a withdrawn or rejected claim

    [Fact]
    public async Task A_withdrawn_or_rejected_claim_can_be_confirmed_anyway_with_a_note()
    {
        var (withdrawnOwner, withdrawnTenant) = await BusinessAsync("Anyway Withdrawn");
        var (rejectedOwner, rejectedTenant) = await BusinessAsync("Anyway Rejected");
        var (finance, financeId, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        await OfflineAsync(async () =>
        {
            var withdrawn = await ClaimAsync(withdrawnOwner);
            (await withdrawnOwner.DeleteAsync("/api/billing/transfer", Ct)).EnsureSuccessStatusCode();
            var rejected = await ClaimAsync(rejectedOwner);
            var rejectedId = (await PaymentAsync(rejected)).Id;
            (await finance.PostAsJsonAsync($"/api/platform/transfers/{rejectedId}/reject", new { reason = "No transfer with this reference yet." }, Ct)).EnsureSuccessStatusCode();
            var withdrawnId = (await PaymentAsync(withdrawn)).Id;

            // A plain confirm leaves them as they are; "confirm anyway" needs a real note.
            Assert.Equal("Abandoned", (await Json(await ConfirmAsync(finance, withdrawnId))).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(finance, withdrawnId, new { anyway = true, note = "ok" })).StatusCode);
            Assert.Equal(PaymentStatus.Abandoned, (await PaymentAsync(withdrawn)).Status);

            foreach (var (id, reference, owner, tenantId) in new[] { (withdrawnId, withdrawn, withdrawnOwner, withdrawnTenant), (rejectedId, rejected, rejectedOwner, rejectedTenant) })
            {
                var confirmed = await Json(await ConfirmAsync(finance, id, new { anyway = true, note = Note }));
                Assert.Equal("Succeeded", confirmed.GetProperty("status").GetString());
                Assert.Equal(PaymentStatus.Succeeded, (await PaymentAsync(reference)).Status);
                Assert.Equal("pro", (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("currentPlan").GetProperty("code").GetString());
                Assert.Equal(1, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct)));
                var audit = await api.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.Action == "transfer.confirm_anyway" && a.EntityId == id.ToString(), Ct));
                Assert.Equal(financeId, audit.ActorId);
                Assert.Contains(Note, audit.AfterJson);
            }

            // Already confirmed: confirming anyway again changes nothing.
            Assert.Equal("Succeeded", (await Json(await ConfirmAsync(finance, withdrawnId, new { anyway = true, note = Note }))).GetProperty("status").GetString());
            Assert.Equal(1, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == withdrawnTenant, Ct)));
        });
    }

    [Fact]
    public async Task Two_confirm_anyway_requests_at_once_give_one_subscription()
    {
        var (owner, tenantId) = await BusinessAsync("Anyway Double");
        var (_, _, token) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        await OfflineAsync(async () =>
        {
            var reference = await ClaimAsync(owner);
            (await owner.DeleteAsync("/api/billing/transfer", Ct)).EnsureSuccessStatusCode();
            var id = (await PaymentAsync(reference)).Id;
            var clients = Enumerable.Range(0, 4).Select(_ =>
            {
                var c = api.CreateClient();
                c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                return c;
            }).ToList();

            var answers = await Task.WhenAll(clients.Select(c => ConfirmAsync(c, id, new { anyway = true, note = Note })));

            Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
            Assert.Equal(1, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct)));
            Assert.Equal(1, await api.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.Action == "transfer.confirm_anyway" && a.EntityId == id.ToString(), Ct)));
        });
    }

    // I2: email flood

    [Fact]
    public async Task Claims_are_rate_limited_per_business_and_email_the_admins_at_most_hourly()
    {
        var (owner, tenantId) = await BusinessAsync("Claim Flood");
        await OfflineAsync(async () =>
        {
            var references = new List<string>();
            for (var i = 0; i < 5; i++) references.Add(await ClaimAsync(owner));
            var sixth = await owner.PostAsJsonAsync("/api/billing/transfer", new { planCode = "pro", reference = await ShownReferenceAsync(owner) }, Ct);

            Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
            Assert.Equal(5, await api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId && p.Provider == "transfer", Ct)));
            var emailed = (await api.WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "plan.transfer_claimed").ToListAsync(Ct)))
                .Where(n => references.Contains(n.Payload.GetValueOrDefault("reference") ?? "")).Select(n => n.Payload["reference"]).Distinct().ToList();
            Assert.Equal([references[0]], emailed); // later claims within the hour are listed for admins but not emailed
        });
    }

    // M1: the claim uses the reference the page showed

    [Fact]
    public async Task A_claim_with_a_reference_the_page_did_not_show_is_refused()
    {
        var (owner, _) = await BusinessAsync("Stale Reference");
        await OfflineAsync(async () =>
        {
            var shown = await ShownReferenceAsync(owner);
            var wrong = await owner.PostAsJsonAsync("/api/billing/transfer", new { planCode = "pro", reference = "LS-AAAAAA" }, Ct);
            Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
            Assert.Contains("Refresh the page", (await wrong.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString());

            var claimed = await Json(await owner.PostAsJsonAsync("/api/billing/transfer", new { planCode = "pro", reference = shown }, Ct));
            Assert.Equal(shown, claimed.GetProperty("reference").GetString());
            // The same page used again: that reference belongs to the first claim now.
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/billing/transfer", new { planCode = "pro", reference = shown }, Ct)).StatusCode);
        });
    }

    // M3: the reference can't be worked out without the server's key

    [Fact]
    public async Task The_shown_reference_depends_on_a_server_secret_and_stays_the_same_across_reloads()
    {
        var (owner, tenantId) = await BusinessAsync("Keyed Reference");
        WebApplicationFactory<Program> otherKey = api.WithWebHostBuilder(b => b.UseSetting("Billing:TransferReferenceKey", "a-different-server-secret-0123456789"));
        await OfflineAsync(async () =>
        {
            var shown = await ShownReferenceAsync(owner);
            Assert.Equal(shown, await ShownReferenceAsync(owner));
            var unkeyed = $"LS-{Codes.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes($"linksuite-transfer:{tenantId:N}:0")), 6)}";
            Assert.NotEqual(unkeyed, shown);
            Assert.NotEqual(shown, await ShownReferenceAsync(ApiFixture.On(otherKey, owner)));
        });
    }

    // M5, M6: the business emails

    [Fact]
    public async Task Confirm_and_reject_emails_go_to_an_active_claimant_else_the_owner_and_never_ask_for_a_reply()
    {
        var (owner, tenantId) = await BusinessAsync("Email Routing");
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        var (_, otherAuth) = await api.SignupAsync("EmailRoutingCoOwner");
        var coOwnerId = otherAuth.GetProperty("user").GetProperty("id").GetGuid();
        var coOwnerEmail = await api.WithDbAsync(db => db.Users.Where(u => u.Id == coOwnerId).Select(u => u.Email!).SingleAsync(Ct));
        var claimantId = await api.WithDbAsync(db => db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == tenantId).Select(m => m.UserId).SingleAsync(Ct));
        await OfflineAsync(async () =>
        {
            // The claimant leaves (disabled) and someone else owns the business.
            var first = await ClaimAsync(owner);
            await api.WithDbAsync(async db =>
            {
                await db.Memberships.IgnoreQueryFilters().Where(m => m.TenantId == tenantId && m.UserId == claimantId)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MembershipStatus.Disabled), Ct);
                db.Memberships.Add(new Membership { TenantId = tenantId, UserId = coOwnerId, Role = MemberRole.Owner });
                return await db.SaveChangesAsync(Ct);
            });
            var id = (await PaymentAsync(first)).Id;
            (await finance.PostAsJsonAsync($"/api/platform/transfers/{id}/reject", new { reason = "No transfer with this reference reached us." }, Ct)).EnsureSuccessStatusCode();

            var rejected = await api.WithDbAsync(db => db.Notifications.SingleAsync(n => n.TemplateKey == "plan.transfer_rejected" && n.TenantId == tenantId, Ct));
            Assert.Equal(coOwnerEmail, rejected.Recipient);
            var text = Saas.Infrastructure.Notifications.EmailTemplates.Render(rejected, "OrderLink").Text;
            Assert.DoesNotContain("reply", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ApiFixture.AdminEmail, text);

            await finance.PostAsJsonAsync($"/api/platform/transfers/{id}/confirm", new { anyway = true, note = Note }, Ct);
            var confirmed = await api.WithDbAsync(db => db.Notifications.SingleAsync(n => n.TemplateKey == "plan.transfer_confirmed" && n.TenantId == tenantId, Ct));
            Assert.Equal(coOwnerEmail, confirmed.Recipient);
        });
    }

    // M8: providers can't confirm a transfer

    [Fact]
    public async Task An_opay_callback_or_paystack_webhook_naming_a_transfer_reference_leaves_it_pending()
    {
        var (owner, _) = await BusinessAsync("Provider Ignores Transfer");
        await OfflineAsync(async () =>
        {
            var reference = await ClaimAsync(owner);
            var body = $$$"""{"event":"charge.success","data":{"status":"success","reference":"{{{reference}}}","amount":300000,"currency":"NGN"}}""";
            var signature = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(ApiFixture.PaystackSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            var paystack = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paystack") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            paystack.Headers.Add("x-paystack-signature", signature);

            var paystackAnswer = await api.CreateClient().SendAsync(paystack, Ct);
            var opayAnswer = await api.CreateClient().PostAsJsonAsync("/api/webhooks/opay", new
            {
                payload = new { reference, status = "SUCCESS", amount = "300000", currency = "NGN" }, sha512 = "forged", type = "transaction-status",
            }, Ct);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (paystackAnswer.StatusCode, opayAnswer.StatusCode));
            Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(reference)).Status);
            Assert.False(api.Opay.StatusCalls.ContainsKey(reference));
        });
    }

    // M9: extending a paid plan by transfer

    [Fact]
    public async Task A_transfer_that_extends_a_paid_plan_marks_the_subscription_as_paid_by_transfer()
    {
        var (owner, tenantId) = await BusinessAsync("Extend By Transfer");
        await api.MakeProAsync(tenantId, "whatsapp-orders");
        await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Provider, "paystack"), Ct));
        var (finance, _, _) = await PlatformAuthTests.PanelAdminAsync(api, PlatformRole.Finance);
        await OfflineAsync(async () =>
        {
            var reference = await ClaimAsync(owner);
            (await finance.PostAsync($"/api/platform/transfers/{(await PaymentAsync(reference)).Id}/confirm", null, Ct)).EnsureSuccessStatusCode();

            var sub = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId, Ct));
            Assert.Equal(("transfer", reference), (sub.Provider, sub.ProviderRef));
            Assert.True(sub.CurrentPeriodEnd > DateTimeOffset.UtcNow.AddDays(50)); // a month added to the month already paid
        });
    }
}
