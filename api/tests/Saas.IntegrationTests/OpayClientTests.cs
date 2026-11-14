using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Saas.Application.Billing;
using Saas.Infrastructure.Payments;

namespace Saas.IntegrationTests;

/// <summary>The OPay client on its own, against the fake cashier API: headers, signing, kobo amounts and the Development fixture mode.</summary>
public sealed class OpayClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Saas.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static OpayClient Client(FakeOpay opay, string environment = "Testing", bool allowLocalFixture = false, string baseUrl = FakeOpay.BaseUrl,
        bool allowSandboxInProduction = false, string secretKey = FakeOpay.SecretKey) =>
        new(new HttpClient(opay), Options.Create(new OpayOptions
        {
            MerchantId = FakeOpay.MerchantId, PublicKey = FakeOpay.PublicKey, SecretKey = secretKey, BaseUrl = baseUrl,
            AllowLocalFixture = allowLocalFixture, AllowSandboxInProduction = allowSandboxInProduction,
        }), new Env(environment), NullLogger<OpayClient>.Instance);

    private static readonly Saas.Application.Common.AppUrls Urls = new("https://app.example", "https://api.example");

    [Fact]
    public async Task Sandbox_base_url_is_not_configured_in_production()
    {
        var sandboxInProduction = new OpayGateway(Client(new FakeOpay(), "Production"), Urls);

        Assert.False(sandboxInProduction.IsConfigured);
        var refused = await Assert.ThrowsAsync<Saas.Application.Common.ServiceUnavailableException>(() =>
            sandboxInProduction.InitializeAsync(Init($"sub_{Guid.NewGuid():N}"), Ct));
        Assert.Equal(BillingService.NotConfiguredMessage, refused.Message);

        Assert.True(new OpayGateway(Client(new FakeOpay(), "Production", allowSandboxInProduction: true), Urls).IsConfigured);
        Assert.True(new OpayGateway(Client(new FakeOpay(), "Production", baseUrl: "https://api.opaycheckout.com"), Urls).IsConfigured);
        Assert.True(new OpayGateway(Client(new FakeOpay(), "Staging"), Urls).IsConfigured); // the sandbox is fine outside Production
    }

    [Fact]
    public async Task A_cancelled_checkout_returns_to_the_billing_page_marked_cancelled()
    {
        var opay = new FakeOpay();
        var reference = $"sub_{Guid.NewGuid():N}";

        await Client(opay).CreateAsync(Init(reference), "https://api.example/api/webhooks/opay", Ct);

        Assert.Equal($"https://app.example/billing?ref={reference}", opay.Created[reference].ReturnUrl);
        Assert.Equal($"https://app.example/billing?ref={reference}&cancelled=1", opay.Created[reference].CancelUrl);
    }

    [Fact]
    public async Task Opay_try_again_and_authentication_failures_are_errors_not_unknown_orders()
    {
        var opay = new FakeOpay();
        var reference = $"sub_{Guid.NewGuid():N}";
        await Client(opay).CreateAsync(Init(reference), "https://api.example/api/webhooks/opay", Ct);
        opay.UnavailableFor[reference] = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(opay).StatusAsync(reference, Ct)); // 50003
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(new FakeOpay(), secretKey: "wrong-secret").StatusAsync(reference, Ct)); // 02000
    }

    private static PaymentInit Init(string reference, long amount = 450_000) =>
        new("payer@example.com", amount, "NGN", reference, $"https://app.example/billing?ref={reference}", new() { ["planName"] = "Pro" });

    [Fact]
    public async Task Creates_with_the_public_key_and_checks_status_signed_with_hmac_sha512_of_the_exact_body()
    {
        var opay = new FakeOpay();
        var client = Client(opay);
        var reference = $"sub_{Guid.NewGuid():N}";

        var url = await client.CreateAsync(Init(reference), "https://api.example/api/webhooks/opay", Ct);
        var initial = await client.StatusAsync(reference, Ct);
        opay.Pay(reference);
        var paid = await client.StatusAsync(reference, Ct);

        Assert.Equal($"{FakeOpay.BaseUrl}/cashier/{reference}", url);
        Assert.Equal((450_000L, "NGN", "NG"), (opay.Created[reference].Total, opay.Created[reference].Currency, opay.Created[reference].Country));
        Assert.Equal(ProviderChargeState.Pending, initial.State);
        Assert.Equal(new ProviderChargeStatus(ProviderChargeState.Success, 450_000, "NGN"), paid);
        Assert.Equal(0, opay.Rejected);
        var (body, signature) = opay.LastStatusRequest!.Value;
        Assert.Equal(Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(FakeOpay.SecretKey), Encoding.UTF8.GetBytes(body))).ToLowerInvariant(), signature);
    }

    [Theory]
    [InlineData("INITIAL", ProviderChargeState.Pending)]
    [InlineData("PENDING", ProviderChargeState.Pending)]
    [InlineData("SUCCESS", ProviderChargeState.Success)]
    [InlineData("FAIL", ProviderChargeState.Failed)]
    [InlineData("CLOSE", ProviderChargeState.Failed)]
    public async Task Maps_opays_statuses(string status, ProviderChargeState expected)
    {
        var opay = new FakeOpay();
        var client = Client(opay);
        var reference = $"sub_{Guid.NewGuid():N}";
        await client.CreateAsync(Init(reference), "https://api.example/api/webhooks/opay", Ct);
        opay.Report(reference, status);

        Assert.Equal(expected, (await client.StatusAsync(reference, Ct)).State);
    }

    [Fact]
    public async Task An_unknown_reference_is_not_found()
    {
        Assert.Equal(ProviderChargeState.NotFound, (await Client(new FakeOpay()).StatusAsync("sub_nobody", Ct)).State);
    }

    [Fact]
    public async Task Local_fixture_answers_in_process_only_in_development_with_the_flag()
    {
        var opay = new FakeOpay();
        var reference = $"sub_{Guid.NewGuid():N}";
        var local = Client(opay, "Development", allowLocalFixture: true);

        var url = await local.CreateAsync(Init(reference), "https://api.example/api/webhooks/opay", Ct);
        var status = await local.StatusAsync(reference, Ct);

        Assert.Equal($"https://app.example/billing?ref={reference}", url);
        Assert.Equal(new ProviderChargeStatus(ProviderChargeState.Success, 450_000, "NGN"), status);
        Assert.Empty(opay.Created);
        Assert.True(local.UsesLocalFixture);

        // The flag outside Development, or Development without the flag: OPay is called like any other time.
        foreach (var client in new[] { Client(opay, "Production", allowLocalFixture: true), Client(opay, "Development") })
        {
            var other = $"sub_{Guid.NewGuid():N}";
            Assert.False(client.UsesLocalFixture);
            Assert.StartsWith(FakeOpay.BaseUrl, await client.CreateAsync(Init(other), "https://api.example/api/webhooks/opay", Ct));
            Assert.True(opay.Created.ContainsKey(other));
        }
    }
}
