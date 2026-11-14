using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;

namespace Saas.IntegrationTests;

/// <summary>
/// The OPay fix round (final review C1, I1, I3, M4, M6, M7): confirmations of one plan payment that arrive together apply it once,
/// for OPay and for Paystack; the re-check leaves old payments alone; provider status calls have a per-payment cooldown.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class OpayFixRoundTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>
    /// An Invoices business (Business plan: 1,000,000 kobo a month) on a complimentary Pro plan, as in the coordinator's repro,
    /// signed in on <paramref name="host"/>.
    /// </summary>
    private async Task<(HttpClient Owner, Guid TenantId, Guid CompId)> BusinessOnProCompAsync(string name, WebApplicationFactory<Program>? host = null)
    {
        var (owner, auth) = await api.SignupInvoicesAsync(name.Replace(" ", ""));
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        var compId = await api.WithDbAsync(async db =>
        {
            var pro = await db.Plans.FirstAsync(p => p.Product == Products.Invoices && p.Code == "pro", Ct);
            var comp = new Subscription
            {
                TenantId = tenantId, PlanId = pro.Id, Status = SubscriptionStatus.Active, Provider = "comp",
                CurrentPeriodStart = DateTimeOffset.UtcNow, CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(60),
            };
            db.Subscriptions.Add(comp);
            await db.SaveChangesAsync(Ct);
            return comp.Id;
        });
        return (host is null ? owner : ApiFixture.On(host, owner), tenantId, compId);
    }

    private static async Task<string> CheckoutAsync(HttpClient owner, string plan = "business")
    {
        var checkout = await Json(await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = plan }, Ct));
        return checkout.GetProperty("authorizationUrl").GetString()!.Split('/').Last();
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient owner, string reference) =>
        owner.PostAsJsonAsync("/api/billing/verify", new { reference }, Ct);

    private Task<HttpResponseMessage> OpayCallbackAsync(string reference) =>
        api.CreateClient().PostAsJsonAsync("/api/webhooks/opay", new
        {
            payload = new { reference, status = "SUCCESS", amount = "1000000", currency = "NGN" }, sha512 = "forged", type = "transaction-status",
        }, Ct);

    private Task<HttpResponseMessage> PaystackWebhookAsync(string reference, long amount)
    {
        var body = $$$"""{"event":"charge.success","data":{"status":"success","reference":"{{{reference}}}","amount":{{{amount}}},"currency":"NGN"}}""";
        var signature = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(ApiFixture.PaystackSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paystack") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("x-paystack-signature", signature);
        return api.CreateClient().SendAsync(request, Ct);
    }

    private static async Task WaitForAsync(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Timed out waiting for {what}.");
            await Task.Delay(20, Ct);
        }
    }

    /// <summary>The business's subscriptions after the confirmations: exactly one paid Business period of one month, the comp untouched.</summary>
    private async Task AssertAppliedOnceAsync(Guid tenantId, Guid compId, string reference, DateTimeOffset startedAt)
    {
        var subs = await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().AsNoTracking().Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId).ToListAsync(Ct));
        var paid = Assert.Single(subs, s => s.Status == SubscriptionStatus.Active && s.Provider != "comp");
        Assert.Equal("business", paid.Plan!.Code);
        var expectedEnd = startedAt.AddMonths(1);
        Assert.InRange(paid.CurrentPeriodEnd!.Value, expectedEnd.AddMinutes(-1), DateTimeOffset.UtcNow.AddMonths(1).AddMinutes(1));
        var comp = Assert.Single(subs, s => s.Id == compId);
        Assert.Equal((SubscriptionStatus.Active, "comp"), (comp.Status, comp.Provider));
        Assert.Equal(PaymentStatus.Succeeded, await api.WithDbAsync(db =>
            db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference).Select(p => p.Status).SingleAsync(Ct)));
    }

    // ---------- C1: OPay ----------

    [Fact]
    public async Task Opay_verify_and_callback_released_together_apply_the_payment_once()
    {
        var (owner, tenantId, compId) = await BusinessOnProCompAsync("Race Opay Together", api.OpayHost);
        var reference = await CheckoutAsync(owner);
        api.Opay.Pay(reference);
        var startedAt = DateTimeOffset.UtcNow;
        using var gate = new SemaphoreSlim(0);
        api.Opay.StatusGate = gate;
        try
        {
            var verify = VerifyAsync(owner, reference);
            await WaitForAsync(() => api.Opay.StatusCalls.GetValueOrDefault(reference) == 1, "the return check to ask OPay");
            await api.ExpireStatusCooldownAsync(reference); // OPay is slow: the callback arrives after the cooldown
            var callback = OpayCallbackAsync(reference);
            await WaitForAsync(() => api.Opay.StatusCalls.GetValueOrDefault(reference) == 2, "the callback to ask OPay");

            gate.Release(2);
            var responses = await Task.WhenAll(verify, callback);

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            api.Opay.StatusGate = null;
        }
        await AssertAppliedOnceAsync(tenantId, compId, reference, startedAt);
    }

    [Fact]
    public async Task Opay_a_stale_second_confirmation_does_not_extend_the_plan_again()
    {
        var (owner, tenantId, compId) = await BusinessOnProCompAsync("Race Opay Stale", api.OpayHost);
        var reference = await CheckoutAsync(owner);
        api.Opay.Pay(reference);
        var startedAt = DateTimeOffset.UtcNow;
        using var gate = new SemaphoreSlim(0);
        api.Opay.StatusGate = gate;
        try
        {
            var verify = VerifyAsync(owner, reference);
            await WaitForAsync(() => api.Opay.StatusCalls.GetValueOrDefault(reference) == 1, "the return check to ask OPay");
            await api.ExpireStatusCooldownAsync(reference);
            var callback = OpayCallbackAsync(reference);
            await WaitForAsync(() => api.Opay.StatusCalls.GetValueOrDefault(reference) == 2, "the callback to ask OPay");

            // Both have read the payment as Pending. One finishes and commits; only then does the other go on.
            gate.Release(1);
            var first = await Task.WhenAny(verify, callback);
            Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
            gate.Release(1);
            Assert.Equal(HttpStatusCode.OK, (await (first == verify ? callback : verify)).StatusCode);
        }
        finally
        {
            api.Opay.StatusGate = null;
        }
        await AssertAppliedOnceAsync(tenantId, compId, reference, startedAt);
    }

    // ---------- C1: Paystack ----------

    [Fact]
    public async Task Paystack_two_return_checks_released_together_apply_the_payment_once()
    {
        var (owner, tenantId, compId) = await BusinessOnProCompAsync("Race Paystack Together");
        var reference = await CheckoutAsync(owner);
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 1_000_000, "NGN", DateTimeOffset.UtcNow);
        var startedAt = DateTimeOffset.UtcNow;
        using var gate = new SemaphoreSlim(0);
        api.Gateway.StatusGate = gate;
        try
        {
            var first = VerifyAsync(owner, reference); // e.g. a page reload, or two tabs
            await WaitForAsync(() => api.Gateway.StatusCalls.GetValueOrDefault(reference) == 1, "the first return check to ask Paystack");
            await api.ExpireStatusCooldownAsync(reference);
            var second = VerifyAsync(owner, reference);
            await WaitForAsync(() => api.Gateway.StatusCalls.GetValueOrDefault(reference) == 2, "the second return check to ask Paystack");

            gate.Release(2);
            var responses = await Task.WhenAll(first, second);

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            api.Gateway.StatusGate = null;
        }
        await AssertAppliedOnceAsync(tenantId, compId, reference, startedAt);
    }

    [Fact]
    public async Task Paystack_a_return_check_after_the_webhook_confirmed_does_not_extend_the_plan_again()
    {
        var (owner, tenantId, compId) = await BusinessOnProCompAsync("Race Paystack Stale");
        var reference = await CheckoutAsync(owner);
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 1_000_000, "NGN", DateTimeOffset.UtcNow);
        var startedAt = DateTimeOffset.UtcNow;
        using var gate = new SemaphoreSlim(0);
        api.Gateway.StatusGate = gate;
        try
        {
            var verify = VerifyAsync(owner, reference);
            await WaitForAsync(() => api.Gateway.StatusCalls.GetValueOrDefault(reference) == 1, "the return check to ask Paystack");

            // The signed webhook confirms the payment while the return check is still waiting on Paystack.
            Assert.Equal(HttpStatusCode.OK, (await PaystackWebhookAsync(reference, 1_000_000)).StatusCode);
            gate.Release(1);
            Assert.Equal(HttpStatusCode.OK, (await verify).StatusCode);
        }
        finally
        {
            api.Gateway.StatusGate = null;
        }
        await AssertAppliedOnceAsync(tenantId, compId, reference, startedAt);
    }

    // ---------- I1 ----------

    [Fact]
    public async Task Recheck_leaves_pending_payments_older_than_48_hours_alone()
    {
        var (_, auth) = await api.SignupAsync("OldPending");
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        var reference = $"sub_old{Guid.NewGuid():N}"[..20];
        await api.WithDbAsync(async db =>
        {
            var pro = await db.Plans.FirstAsync(p => p.Product == Products.WhatsAppOrders && p.Code == "pro", Ct);
            db.Payments.Add(new Payment
            {
                TenantId = tenantId, AmountMinor = pro.PriceMinor, Currency = "NGN", Provider = "paystack", ProviderRef = reference,
                Purpose = "subscription", Metadata = new() { ["planId"] = pro.Id.ToString() }, CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            return await db.SaveChangesAsync(Ct);
        });
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 300_000, "NGN", DateTimeOffset.UtcNow);

        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PlanPaymentJobs>().RunOnceAsync(DateTimeOffset.UtcNow, Ct);

        Assert.False(api.Gateway.StatusCalls.ContainsKey(reference));
        Assert.Equal(PaymentStatus.Pending, await api.WithDbAsync(db =>
            db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference).Select(p => p.Status).SingleAsync(Ct)));
        Assert.Equal(0, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct)));
    }

    // ---------- I3 ----------

    [Fact]
    public async Task A_callback_and_return_check_flood_asks_opay_once_per_cooldown()
    {
        var (owner, _, _) = await BusinessOnProCompAsync("Flood Opay", api.OpayHost);
        var reference = await CheckoutAsync(owner);

        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.OK, (await OpayCallbackAsync(reference)).StatusCode);
        for (var i = 0; i < 5; i++)
            Assert.Equal("Pending", (await Json(await VerifyAsync(owner, reference))).GetProperty("status").GetString());

        Assert.Equal(1, api.Opay.StatusCalls[reference]);
    }

    // ---------- M4 ----------

    [Fact]
    public async Task Opay_saying_try_again_keeps_the_payment_pending()
    {
        var (owner, _, _) = await BusinessOnProCompAsync("Opay Busy", api.OpayHost);
        var reference = await CheckoutAsync(owner);
        api.Opay.Pay(reference);
        api.Opay.UnavailableFor[reference] = true;

        var answer = await Json(await VerifyAsync(owner, reference));

        Assert.Equal("Pending", answer.GetProperty("status").GetString());
        Assert.Equal(PaymentStatus.Pending, await api.WithDbAsync(db =>
            db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference).Select(p => p.Status).SingleAsync(Ct)));
    }

    // ---------- M6 ----------

    [Fact]
    public void An_unknown_plan_provider_fails_at_startup()
    {
        var host = api.WithWebHostBuilder(b => b.UseSetting("Billing:PlanProvider", "opay2"));

        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("Billing:PlanProvider", error.ToString());
    }

    // ---------- M7 ----------

    [Fact]
    public async Task An_opay_callback_over_64_kb_is_refused_without_asking_opay()
    {
        var (owner, _, _) = await BusinessOnProCompAsync("Opay Big Body", api.OpayHost);
        var reference = await CheckoutAsync(owner);
        var body = $$"""{"payload":{"reference":"{{reference}}","status":"SUCCESS"},"padding":"{{new string('x', 70_000)}}"}""";

        var response = await api.CreateClient().PostAsync("/api/webhooks/opay", new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False(api.Opay.StatusCalls.ContainsKey(reference));
    }

    // ---------- Review gap: CareLink's return page ----------

    [Fact]
    public void CareLink_payers_return_to_the_settings_page_that_runs_the_return_check()
    {
        Assert.Equal("/app/settings", BillingService.BillingPath(Products.Care));
    }
}
