using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Domain.Core;
using Saas.Domain.Property;

namespace Saas.IntegrationTests;

/// <summary>
/// OPay beside Paystack for plan billing: the provider is chosen at checkout from <c>Billing:PlanProvider</c>, and every confirmation
/// (callback, return check, background re-check) asks the payment's own provider before a plan changes.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class OpayBillingTests(ApiFixture api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json, Ct);
    }

    /// <summary>A WhatsApp Orders business on the free plan (its Pro plan costs 300,000 kobo), signed in on <paramref name="host"/>.</summary>
    private async Task<(HttpClient Owner, Guid TenantId)> BusinessAsync(string name, WebApplicationFactory<Program>? host = null)
    {
        var (owner, auth) = await api.SignupAsync(name.Replace(" ", "")); // the name becomes part of the email
        var tenantId = auth.GetProperty("tenant").GetProperty("id").GetGuid();
        return (host is null ? owner : ApiFixture.On(host, owner), tenantId);
    }

    /// <summary>Starts a Pro checkout and returns the reference (the last segment of the checkout URL, for both providers).</summary>
    private static async Task<string> CheckoutAsync(HttpClient owner, string plan = "pro")
    {
        var checkout = await Json(await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = plan }, Ct));
        return checkout.GetProperty("authorizationUrl").GetString()!.Split('/').Last();
    }

    private static async Task<JsonElement> VerifyAsync(HttpClient owner, string reference) =>
        await Json(await owner.PostAsJsonAsync("/api/billing/verify", new { reference }, Ct));

    private static async Task<string> PlanAsync(HttpClient owner) =>
        (await Json(await owner.GetAsync("/api/billing", Ct))).GetProperty("currentPlan").GetProperty("code").GetString()!;

    private Task<Payment> PaymentAsync(string reference) =>
        api.WithDbAsync(db => db.Payments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.ProviderRef == reference, Ct));

    private Task<HttpResponseMessage> OpayCallbackAsync(string reference, string status = "SUCCESS", long amount = 300_000) =>
        api.CreateClient().PostAsJsonAsync("/api/webhooks/opay", new
        {
            payload = new { reference, status, amount = amount.ToString(), currency = "NGN", country = "NG", transactionId = "241100001" },
            sha512 = "forged-signature",
            type = "transaction-status",
        }, Ct);

    [Fact]
    public async Task Checkout_uses_opay_when_it_is_the_plan_provider_and_records_it()
    {
        var (owner, _) = await BusinessAsync("Opay Checkout", api.OpayHost);

        var checkout = await Json(await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct));
        var url = checkout.GetProperty("authorizationUrl").GetString()!;
        var reference = url.Split('/').Last();

        Assert.StartsWith($"{FakeOpay.BaseUrl}/cashier/sub_", url);
        var payment = await PaymentAsync(reference);
        Assert.Equal(("opay", PaymentStatus.Pending, 300_000L, "NGN"), (payment.Provider, payment.Status, payment.AmountMinor, payment.Currency));
        var order = api.Opay.Created[reference];
        Assert.Equal((300_000L, "NGN", "NG"), (order.Total, order.Currency, order.Country));
        Assert.EndsWith($"/dashboard/billing?ref={reference}", order.ReturnUrl);
        Assert.EndsWith("/api/webhooks/opay", order.CallbackUrl);
        Assert.EndsWith($"/dashboard/billing?ref={reference}&cancelled=1", order.CancelUrl);
        Assert.DoesNotContain(api.Gateway.Initialized, i => i.Reference == reference);
        Assert.Equal(0, api.Opay.Rejected);
    }

    [Fact]
    public async Task Paystack_stays_the_plan_provider_by_default()
    {
        var (owner, _) = await BusinessAsync("Paystack Default");

        var reference = await CheckoutAsync(owner);

        Assert.Equal("paystack", (await PaymentAsync(reference)).Provider);
        Assert.Contains(api.Gateway.Initialized, i => i.Reference == reference);
        Assert.False(api.Opay.Created.ContainsKey(reference));
    }

    [Fact]
    public async Task Opay_callback_confirms_only_after_opay_says_so_and_ignores_the_bodys_status()
    {
        var (owner, tenantId) = await BusinessAsync("Opay Callback", api.OpayHost);
        var reference = await CheckoutAsync(owner);

        // The callback claims SUCCESS, but OPay still says INITIAL: nothing changes.
        var forged = await OpayCallbackAsync(reference);
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        Assert.Equal(1, api.Opay.StatusCalls[reference]);
        Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(reference)).Status);
        Assert.Equal("free", await PlanAsync(owner));

        api.Opay.Pay(reference);
        await api.ExpireStatusCooldownAsync(reference); // OPay's retry comes after the per-payment cooldown
        var real = await OpayCallbackAsync(reference, status: "FAIL"); // the body's status is never read
        var retry = await OpayCallbackAsync(reference);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (real.StatusCode, retry.StatusCode));
        Assert.Equal(PaymentStatus.Succeeded, (await PaymentAsync(reference)).Status);
        Assert.Equal("pro", await PlanAsync(owner));
        Assert.Equal(1, await api.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, Ct)));
        Assert.Equal(0, api.Opay.Rejected);
    }

    [Fact]
    public async Task Opay_callback_answers_200_for_an_unknown_reference_or_an_unreadable_body()
    {
        var unknown = await OpayCallbackAsync($"sub_unknown{Guid.NewGuid():N}");
        var unreadable = await api.CreateClient().PostAsync("/api/webhooks/opay", new StringContent("not json", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (unknown.StatusCode, unreadable.StatusCode));
    }

    [Fact]
    public async Task Return_check_confirms_a_paystack_plan_payment()
    {
        var (owner, _) = await BusinessAsync("Paystack Return");
        var reference = await CheckoutAsync(owner);

        var pending = await VerifyAsync(owner, reference);
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 300_000, "NGN", DateTimeOffset.UtcNow);
        await api.ExpireStatusCooldownAsync(reference);
        var paid = await VerifyAsync(owner, reference);

        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal("pro", paid.GetProperty("plan").GetProperty("code").GetString());
        Assert.Equal("pro", await PlanAsync(owner));
    }

    [Fact]
    public async Task Return_check_confirms_an_opay_plan_payment_pending_then_paid()
    {
        var (owner, _) = await BusinessAsync("Opay Return", api.OpayHost);
        var reference = await CheckoutAsync(owner);

        var pending = await VerifyAsync(owner, reference);
        api.Opay.Pay(reference);
        await api.ExpireStatusCooldownAsync(reference);
        var paid = await VerifyAsync(owner, reference);
        var again = await VerifyAsync(owner, reference);

        Assert.Equal(("Pending", "Paid", "Paid"), (pending.GetProperty("status").GetString(), paid.GetProperty("status").GetString(), again.GetProperty("status").GetString()));
        Assert.Equal("pro", await PlanAsync(owner));
        Assert.Equal(2, api.Opay.StatusCalls[reference]); // a paid payment isn't asked about again
        Assert.Equal(0, api.Opay.Rejected);
    }

    [Fact]
    public async Task Return_check_is_refused_for_another_businesss_payment()
    {
        var (owner, _) = await BusinessAsync("Opay Owner", api.OpayHost);
        var (other, _) = await BusinessAsync("Opay Snoop", api.OpayHost);
        var reference = await CheckoutAsync(owner);
        api.Opay.Pay(reference);

        var response = await other.PostAsJsonAsync("/api/billing/verify", new { reference }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(api.Opay.StatusCalls.ContainsKey(reference));
        Assert.Equal(PaymentStatus.Pending, (await PaymentAsync(reference)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/api/billing/verify", new { reference }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_mismatched_amount_or_currency_marks_the_payment_failed()
    {
        var (owner, _) = await BusinessAsync("Opay Mismatch", api.OpayHost);
        var shortPaid = await CheckoutAsync(owner);
        var otherCurrency = await CheckoutAsync(owner);
        api.Opay.Report(shortPaid, "SUCCESS", total: 299_900);
        api.Opay.Report(otherCurrency, "SUCCESS", currency: "GHS");

        var first = await VerifyAsync(owner, shortPaid);
        var second = await VerifyAsync(owner, otherCurrency);

        Assert.Equal(("Failed", "Failed"), (first.GetProperty("status").GetString(), second.GetProperty("status").GetString()));
        Assert.Equal((PaymentStatus.Failed, PaymentStatus.Failed), ((await PaymentAsync(shortPaid)).Status, (await PaymentAsync(otherCurrency)).Status));
        Assert.Equal("free", await PlanAsync(owner));
    }

    [Fact]
    public async Task Opay_reporting_a_failed_payment_reads_as_failed()
    {
        var (owner, _) = await BusinessAsync("Opay Failed", api.OpayHost);
        var reference = await CheckoutAsync(owner);
        api.Opay.Report(reference, "FAIL");

        Assert.Equal("Failed", (await VerifyAsync(owner, reference)).GetProperty("status").GetString());
        Assert.Equal("free", await PlanAsync(owner));
    }

    [Fact]
    public async Task Recheck_confirms_a_pending_payment_then_abandons_one_after_24_hours()
    {
        var (paidOwner, _) = await BusinessAsync("Recheck Paid", api.OpayHost);
        var (strandedOwner, _) = await BusinessAsync("Recheck Stranded", api.OpayHost);
        var paid = await CheckoutAsync(paidOwner);
        var stranded = await CheckoutAsync(strandedOwner);
        api.Opay.Pay(paid);
        var now = DateTimeOffset.UtcNow;

        async Task RunAsync(DateTimeOffset at)
        {
            using var scope = api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PlanPaymentJobs>().RunOnceAsync(at, Ct);
        }

        await RunAsync(now.AddMinutes(10));
        Assert.Equal(PaymentStatus.Succeeded, (await PaymentAsync(paid)).Status);
        Assert.Equal("pro", await PlanAsync(paidOwner));
        Assert.Equal((PaymentStatus.Pending, 1), ((await PaymentAsync(stranded)).Status, api.Opay.StatusCalls[stranded]));

        await RunAsync(now.AddMinutes(12)); // throttled: asked at most every 10 minutes
        Assert.Equal(1, api.Opay.StatusCalls[stranded]);
        await RunAsync(now.AddMinutes(21));
        Assert.Equal(2, api.Opay.StatusCalls[stranded]);

        await RunAsync(now.AddHours(25));
        Assert.Equal(PaymentStatus.Abandoned, (await PaymentAsync(stranded)).Status);
        Assert.Equal("free", await PlanAsync(strandedOwner));

        // Abandoned is soft: a late payment OPay confirms is still applied.
        api.Opay.Pay(stranded);
        await api.ExpireStatusCooldownAsync(stranded); // the job's last ask was "25 hours from now"
        Assert.Equal("Paid", (await VerifyAsync(strandedOwner, stranded)).GetProperty("status").GetString());
        Assert.Equal("pro", await PlanAsync(strandedOwner));
    }

    [Fact]
    public async Task Switching_to_opay_keeps_verifying_older_paystack_payments()
    {
        var (paystackOwner, _) = await BusinessAsync("Switch Older");
        var reference = await CheckoutAsync(paystackOwner); // Paystack is the provider when the checkout starts
        api.Gateway.Charges[reference] = new VerifiedCharge(reference, 300_000, "NGN", DateTimeOffset.UtcNow);

        var onOpay = ApiFixture.On(api.OpayHost, paystackOwner); // then the owner switches to OPay
        var verified = await VerifyAsync(onOpay, reference);

        Assert.Equal("Paid", verified.GetProperty("status").GetString());
        Assert.Equal("pro", await PlanAsync(onOpay));
        Assert.False(api.Opay.StatusCalls.ContainsKey(reference));
    }

    [Fact]
    public async Task Courses_boosts_and_stays_keep_the_paystack_gateway_when_plans_use_opay()
    {
        using (var scope = api.OpayHost.Services.CreateScope())
        {
            Assert.Equal("paystack", scope.ServiceProvider.GetRequiredService<IPaymentGateway>().Name);
            Assert.Equal("opay", scope.ServiceProvider.GetRequiredService<PlanGateways>().Configured?.Name);
        }

        var kit = new PropertyKit(api);
        var agency = await kit.AgencyAsync("Opay Era Agency", verified: true);
        var listing = await kit.LiveListingAsync(agency.Client, await kit.ListingBodyAsync(title: "Opay era short-let", purpose: ListingPurpose.ShortLet, price: 5_000_000));
        var agencyOnOpay = ApiFixture.On(api.OpayHost, agency.Client);
        var boost = await Json(await agencyOnOpay.PostAsJsonAsync($"/api/property/listings/{listing}/boosts", new { code = "featured-7" }, Ct));

        var guest = ApiFixture.On(api.OpayHost, await kit.SeekerAsync("Opay Era Guest"));
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20);
        var stay = await Json(await guest.PostAsJsonAsync("/api/stays", new
        {
            listingId = listing, checkIn = checkIn.ToString("yyyy-MM-dd"), checkOut = checkIn.AddDays(2).ToString("yyyy-MM-dd"), guests = 2, phone = "+2348066666666",
        }, Ct));

        foreach (var url in new[] { boost.GetProperty("authorizationUrl").GetString()!, stay.GetProperty("authorizationUrl").GetString()! })
        {
            Assert.StartsWith("https://checkout.example/", url);
            var reference = url.Split('/').Last();
            Assert.Contains(api.Gateway.Initialized, i => i.Reference == reference);
            Assert.False(api.Opay.Created.ContainsKey(reference));
        }
    }

    [Fact]
    public async Task Admin_payments_list_shows_the_provider()
    {
        var (opayOwner, _) = await BusinessAsync("Admin Opay Row", api.OpayHost);
        var (paystackOwner, _) = await BusinessAsync("Admin Paystack Row");
        var opayRef = await CheckoutAsync(opayOwner);
        var paystackRef = await CheckoutAsync(paystackOwner);

        using var scope = api.Services.CreateScope();
        var rows = (await scope.ServiceProvider.GetRequiredService<Saas.Application.Platform.PlatformMoneyService>()
            .PaymentsAsync(Products.WhatsAppOrders, "Pending", DateTimeOffset.UtcNow.AddMinutes(-5), null, 1, 100, Ct)).Items;

        Assert.Equal("opay", rows.Single(r => r.Reference == opayRef).Provider);
        Assert.Equal("paystack", rows.Single(r => r.Reference == paystackRef).Provider);
    }

    [Fact]
    public async Task Checkout_says_so_when_the_plan_provider_is_not_configured()
    {
        var (owner, _) = await BusinessAsync("Opay Unset", api.UnconfiguredOpayHost);

        var billing = await Json(await owner.GetAsync("/api/billing", Ct));
        var checkout = await owner.PostAsJsonAsync("/api/billing/checkout", new { planCode = "pro" }, Ct);

        Assert.False(billing.GetProperty("paymentsEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, checkout.StatusCode);
        var detail = (await checkout.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("detail").GetString();
        Assert.Equal(BillingService.NotConfiguredMessage, detail);
        Assert.True((await Json(await ApiFixture.On(api.OpayHost, owner).GetAsync("/api/billing", Ct))).GetProperty("paymentsEnabled").GetBoolean());
    }
}
