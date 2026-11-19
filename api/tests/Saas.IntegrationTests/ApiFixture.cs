using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Saas.Application.Billing;
using Saas.Application.Common;
using Saas.Application.Invoices;
using Saas.Domain.Core;
using Saas.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Saas.IntegrationTests;

/// <summary>Runs the real API against a throwaway PostgreSQL container.</summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PaystackSecret = "sk_test_integration";
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgis/postgis:17-3.5").Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();
        _ = Server; // boots the host, which migrates and seeds
        // The migration starts with online payment off (bank transfer only); the suite runs with it on, and the tests for the off
        // state switch it off themselves and back on when they finish.
        await ResetPaymentSettingsAsync();
    }

    public const string InitialBankName = "OPay";
    public const string InitialAccountNumber = "9067382888";
    public const string InitialAccountName = "Brosync Technologies";

    /// <summary>
    /// Writes the platform payment settings row straight to the database (provider null = <c>Billing:PlanProvider</c> of each host) and
    /// drops the cached copy on this host and every host made from it, so the next request reads it.
    /// </summary>
    public async Task SetPaymentSettingsAsync(bool online, string? provider = null, string? bankName = InitialBankName,
        string? accountNumber = InitialAccountNumber, string? accountName = InitialAccountName)
    {
        await WithDbAsync(db => db.PlatformPaymentSettings.ExecuteUpdateAsync(s => s
            .SetProperty(x => x.OnlinePaymentsEnabled, online)
            .SetProperty(x => x.PlanProvider, provider)
            .SetProperty(x => x.BankName, bankName)
            .SetProperty(x => x.BankAccountNumber, accountNumber)
            .SetProperty(x => x.BankAccountName, accountName)));
        foreach (var host in Factories.Prepend(this))
        {
            try { host.Services.GetRequiredService<PaymentSettingsCache>().Invalidate(); }
            // A test's own host that it already disposed, or one that was made to fail at startup: it serves nothing to invalidate.
            catch (Exception) when (!ReferenceEquals(host, this)) { }
        }
    }

    /// <summary>Back to the suite's default: online payment on, provider from config, the initial bank details.</summary>
    public Task ResetPaymentSettingsAsync() => SetPaymentSettingsAsync(true);

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Paystack:SecretKey", PaystackSecret);
        builder.UseSetting("Opay:MerchantId", FakeOpay.MerchantId);
        builder.UseSetting("Opay:PublicKey", FakeOpay.PublicKey);
        builder.UseSetting("Opay:SecretKey", FakeOpay.SecretKey);
        builder.UseSetting("Opay:BaseUrl", FakeOpay.BaseUrl);
        builder.UseSetting("Jobs:BillingEnabled", "false"); // tests run PlanPaymentJobs by hand
        builder.UseSetting("Notifications:DispatcherEnabled", "false");
        builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "saas-it-uploads"));
        builder.UseSetting("Jobs:InvoicesEnabled", "false");
        builder.UseSetting("Jobs:BookingsEnabled", "false");
        builder.UseSetting("Jobs:PayoutsEnabled", "false"); // tests run the transfer re-check by hand
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:PublicWritesPerMinute", "100000");
        builder.UseSetting("RateLimits:WebhooksPerMinute", "10000");
        builder.UseSetting("RateLimits:OtpPer10Minutes", "100000");
        builder.UseSetting("RateLimits:PreviewPerMinute", "1000");
        builder.UseSetting("RateLimits:AdminPerMinute", "1000"); // NextReleaseAuthTests lowers it for its own factory
        builder.UseSetting("RateLimits:ClientErrorsPerMinute", "1000");
        builder.UseSetting("RateLimits:ImportPreviewsPerMinute", "1000");
        builder.UseSetting("Video:ProcessorEnabled", "false");
        builder.UseSetting("Jobs:PropertyEnabled", "false"); // tests run PropertyJobs by hand
        builder.UseSetting("Jobs:SchoolEnabled", "false"); // tests run SchoolJobs by hand
        builder.UseSetting("Jobs:CareEnabled", "false"); // tests run CareJobs by hand
        builder.UseSetting("Photos:ProcessorEnabled", "false"); // tests run the photo processor by hand
        builder.UseSetting("Video:FfmpegPath", "ffmpeg-not-installed-in-tests"); // uploads are published as MP4 in tests
        builder.UseSetting("CustomDomains:Target", CustomDomainsTarget);
        builder.UseSetting("CustomDomains:ApexIp", CustomDomainsIp);
        builder.UseSetting("CustomDomains:HostSecret", CustomDomainsHostSecret);
        builder.UseSetting("Jobs:CustomDomainsEnabled", "false"); // tests run CustomDomainJobs by hand
        builder.UseSetting("Platform:AdminEmails:0", AdminEmail);
        builder.UseSetting("Platform:AdminEmails:1", UnclaimedAdminEmail);
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IMerchantPayments>(_ => Merchant);
            services.AddSingleton<IExchangeRateProvider, FakeRates>();
            services.AddSingleton<Saas.Application.Orders.IDomainResolver>(Dns);
            // Platform payments with Paystack (plans, courses, stays, boosts) go to this fake; registered last, so it wins over the real
            // Paystack gateway.
            services.AddScoped<IPlanPaymentGateway>(_ => Gateway);
            // OPay is faked at the HTTP level, so the real client's signing, headers and parsing are exercised.
            services.AddHttpClient<Saas.Infrastructure.Payments.OpayClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Opay).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            // Paystack transfers, recipients and subaccounts are faked at the HTTP level too.
            services.AddHttpClient<Saas.Infrastructure.Payments.PaystackTransfersClient>()
                .ConfigurePrimaryHttpMessageHandler(() => PaystackTransfers).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        });
    }

    /// <summary>Stands in for Paystack's transfer, recipient and subaccount API, shared by every host of this fixture.</summary>
    public FakePaystackTransfers PaystackTransfers { get; } = new();

    private WebApplicationFactory<Program>? payoutsHost;

    /// <summary>The same API and database with automatic Paystack transfers and Paystack subaccounts switched on.</summary>
    public WebApplicationFactory<Program> PayoutsHost => payoutsHost ??= WithWebHostBuilder(b =>
    {
        b.UseSetting("Payouts:AutomaticTransfers", "true");
        b.UseSetting("Payouts:PaystackSubaccounts", "true");
    });

    public const string CustomDomainsTarget = "stores.linksuite.test";
    public const string CustomDomainsIp = "203.0.113.10";
    public const string CustomDomainsHostSecret = "host-secret-for-integration-tests";

    /// <summary>Stands in for DNS: custom domain checks resolve names here, shared by every host of this fixture.</summary>
    public FakeDomainResolver Dns { get; } = new();

    /// <summary>Stands in for OPay's cashier API (create and status), shared by every host of this fixture.</summary>
    public FakeOpay Opay { get; } = new();

    private WebApplicationFactory<Program>? opayHost;

    /// <summary>The same API and database, with <c>Billing:PlanProvider</c> set to opay.</summary>
    public WebApplicationFactory<Program> OpayHost => opayHost ??= WithWebHostBuilder(b => b.UseSetting("Billing:PlanProvider", "opay"));

    private WebApplicationFactory<Program>? unconfiguredHost;

    /// <summary>OPay chosen for plans, but its secret key missing.</summary>
    public WebApplicationFactory<Program> UnconfiguredOpayHost => unconfiguredHost ??= WithWebHostBuilder(b =>
    {
        b.UseSetting("Billing:PlanProvider", "opay");
        b.UseSetting("Opay:SecretKey", "");
    });

    /// <summary>
    /// Lets the next callback or return check ask the provider again, as if the per-payment status cooldown had passed
    /// (clears <c>RecheckedAt</c>).
    /// </summary>
    public Task ExpireStatusCooldownAsync(string reference) => WithDbAsync(async db =>
        await db.Payments.IgnoreQueryFilters().Where(p => p.ProviderRef == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecheckedAt, (DateTimeOffset?)null))
        + await db.CourseOrders.IgnoreQueryFilters().Where(o => o.Reference == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.RecheckedAt, (DateTimeOffset?)null))
        + await db.Stays.IgnoreQueryFilters().Where(o => o.Reference == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.RecheckedAt, (DateTimeOffset?)null))
        + await db.Boosts.IgnoreQueryFilters().Where(o => o.Reference == reference)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.RecheckedAt, (DateTimeOffset?)null)));

    /// <summary>A client on <paramref name="host"/> signed in as <paramref name="signedIn"/> (the same token works on every host).</summary>
    public static HttpClient On(WebApplicationFactory<Program> host, HttpClient signedIn)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        return client;
    }

    public const string AdminEmail = "admin@platform.test";
    /// <summary>In config but never signed up by any test except the squatting one.</summary>
    public const string UnclaimedAdminEmail = "unclaimed-admin@platform.test";

    /// <summary>The platform Paystack account (plan upgrades and course sales).</summary>
    public FakePlatformGateway Gateway { get; } = new();

    /// <summary>Stands in for Paystack: records what was initialised and answers verify calls from <see cref="Charges"/>.</summary>
    public FakeMerchantPayments Merchant { get; } = new();

    public async Task<(HttpClient Client, JsonElement Auth)> SignupInvoicesAsync(string name, string? email = null, string password = "password123")
    {
        var client = CreateClient();
        var response = await SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = name,
            email = email ?? $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            password,
            country = "NG",
            product = "invoices",
            businessName = $"{name} Studio",
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth);
    }

    /// <summary>Puts a tenant on the Pro plan of its product, as a paid subscription would.</summary>
    public Task MakeProAsync(Guid tenantId, string product, string planCode = "pro") => WithDbAsync(async db =>
    {
        var pro = await db.Plans.FirstAsync(p => p.Product == product && p.Code == planCode);
        db.Subscriptions.Add(new Subscription
        {
            TenantId = tenantId, PlanId = pro.Id, Status = SubscriptionStatus.Active,
            CurrentPeriodStart = DateTimeOffset.UtcNow, CurrentPeriodEnd = DateTimeOffset.UtcNow.AddMonths(1),
        });
        return await db.SaveChangesAsync();
    });

    /// <summary>Runs a scoped service as the given tenant, like the background jobs do.</summary>
    public async Task<T> AsTenantAsync<TService, T>(Guid tenantId, Func<TService, Task<T>> action) where TService : notnull
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId);
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>The 6-digit code most recently emailed to <paramref name="email"/> (sign-up confirmation or code sign-in).</summary>
    public async Task<string> LatestCodeAsync(string email) =>
        (await WithDbAsync(db => db.Notifications.Where(n => n.TemplateKey == "otp" && n.Recipient == email.Trim().ToLowerInvariant())
            .OrderByDescending(n => n.CreatedAt).FirstAsync())).Payload["code"];

    /// <summary>
    /// Final fix pass B: a password sign-up answers <c>{ confirmationRequired: true }</c> and emails a code; the session comes from
    /// <c>{path}/confirm</c> with that code and the same password. Posts the sign-up to <paramref name="path"/> and, when the API asks for the
    /// code, confirms it as the mailbox's owner would: the answer is the confirm response (a session). A refused sign-up, or one that
    /// already answers with a session (a confirmed account adding a product), is returned as it is.
    /// </summary>
    public async Task<HttpResponseMessage> SignUpAsync(HttpClient client, string path, object body, CancellationToken ct = default)
    {
        var response = await client.PostAsJsonAsync(path, body, ct);
        if (!response.IsSuccessStatusCode) return response;
        // The caller may read the answer again, so it gets a copy whose content hasn't been read (a TestServer stream reads once).
        var text = await response.Content.ReadAsStringAsync(ct);
        var answer = JsonSerializer.Deserialize<JsonElement>(text, Json);
        if (!answer.TryGetProperty("confirmationRequired", out var required) || required.ValueKind != JsonValueKind.True)
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json"), RequestMessage = response.RequestMessage,
            };
        var sent = JsonSerializer.SerializeToElement(body, Json);
        string? Field(string name) => sent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var email = Field("email")!;
        return await client.PostAsJsonAsync($"{path}/confirm",
            new { email, code = await LatestCodeAsync(email), password = Field("password"), product = Field("product") }, ct);
    }

    /// <summary>
    /// An account as sign-up made them before final fix pass B: a password and an email that was never confirmed (it can still sign in with
    /// the password, and its first code confirmation takes it over).
    /// </summary>
    public async Task<Guid> LegacyUnconfirmedAccountAsync(string email, string password = "password123", string name = "Legacy Account")
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var user = new AppUser { FullName = name, Email = email, UserName = email };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        return user.Id;
    }

    public async Task<(HttpClient Client, JsonElement Auth)> SignupAsync(string name)
    {
        var client = CreateClient();
        var id = Guid.NewGuid().ToString("N")[..8];
        var response = await SignUpAsync(client, "/api/auth/signup", new
        {
            fullName = name,
            email = $"{name.ToLowerInvariant()}-{id}@example.com",
            password = "password123",
            storeName = $"{name} Store {id}",
            whatsappNumber = "08031234567",
            country = "NG",
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return (client, auth);
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

public sealed class FakeMerchantPayments : IMerchantPayments
{
    public List<PaymentInit> Initialized { get; } = [];
    public Dictionary<string, VerifiedCharge> Charges { get; } = [];
    public int VerifyCalls { get; private set; }
    /// <summary>How many times each reference was looked up (with any key).</summary>
    public Dictionary<string, int> LookupsFor { get; } = [];
    /// <summary>Verifying these references throws, like a Paystack 5xx or 429 would.</summary>
    public HashSet<string> ThrowFor { get; } = [];
    /// <summary>Verifying these references times out (TaskCanceledException without the caller's token cancelled), like a hung Paystack call.</summary>
    public HashSet<string> TimeoutFor { get; } = [];
    /// <summary>References Paystack knows but that were not paid, with Paystack's status (abandoned, failed...).</summary>
    public Dictionary<string, string> Unpaid { get; } = [];

    public Task<string> InitializeAsync(string secretKey, PaymentInit init, CancellationToken ct)
    {
        Initialized.Add(init);
        return Task.FromResult($"https://checkout.example/{init.Reference}");
    }

    /// <summary>Charges listed here exist only in the Paystack account of that secret key: verify with any other key finds nothing.</summary>
    public Dictionary<string, string> ChargeKeys { get; } = [];

    public async Task<VerifiedCharge?> VerifyAsync(string secretKey, string reference, CancellationToken ct) =>
        (await LookupAsync(secretKey, reference, ct)).Charge;

    public Task<ChargeLookup> LookupAsync(string secretKey, string reference, CancellationToken ct)
    {
        VerifyCalls++;
        LookupsFor[reference] = LookupsFor.GetValueOrDefault(reference) + 1;
        if (ThrowFor.Contains(reference)) throw new HttpRequestException("Paystack is unavailable (503).");
        if (TimeoutFor.Contains(reference)) throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 20 seconds elapsing.");
        if (ChargeKeys.TryGetValue(reference, out var owner) && owner != secretKey) return Task.FromResult(ChargeLookup.NotFound);
        if (Charges.TryGetValue(reference, out var charge)) return Task.FromResult(new ChargeLookup(charge, null));
        if (Unpaid.TryGetValue(reference, out var status)) return Task.FromResult(new ChargeLookup(null, status));
        return Task.FromResult(ChargeLookup.NotFound);
    }

    public List<(string Reference, long AmountMinor)> Refunds { get; } = [];

    public Task RefundAsync(string secretKey, string reference, long amountMinor, CancellationToken ct)
    {
        Refunds.Add((reference, amountMinor));
        return Task.CompletedTask;
    }
}

/// <summary>DNS for custom domain checks: names point where a test says; the target host points at the server's IP.</summary>
public sealed class FakeDomainResolver : Saas.Application.Orders.IDomainResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Net.IPAddress[]> records = new(StringComparer.OrdinalIgnoreCase)
    {
        [ApiFixture.CustomDomainsTarget] = [System.Net.IPAddress.Parse(ApiFixture.CustomDomainsIp)],
    };

    /// <summary>Lookups of these names fail (a DNS server error, not "no such name").</summary>
    public System.Collections.Concurrent.ConcurrentBag<string> Broken { get; } = [];

    public void Point(string host, params string[] addresses) => records[host] = addresses.Select(System.Net.IPAddress.Parse).ToArray();

    public Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        if (Broken.Contains(host, StringComparer.OrdinalIgnoreCase)) throw new Saas.Application.Orders.DomainLookupException($"Lookup of {host} failed.");
        return Task.FromResult<IReadOnlyList<System.Net.IPAddress>>(records.TryGetValue(host, out var a) ? a : []);
    }
}

/// <summary>Fixed rates (units per USD) so tests never call the real provider.</summary>
public sealed class FakeRates : IExchangeRateProvider
{
    public string Name => "Test rates";

    public Task<RateSnapshot> FetchLatestAsync(CancellationToken ct) => Task.FromResult(new RateSnapshot(
        DateOnly.FromDateTime(DateTime.UtcNow),
        new Dictionary<string, decimal> { ["USD"] = 1m, ["NGN"] = 1600m, ["GBP"] = 0.8m, ["GHS"] = 16m }));
}

/// <summary>Stands in for the platform's own Paystack account (courses, boosts, stays, and plans when Paystack is the plan provider).</summary>
public sealed class FakePlatformGateway : IPlanPaymentGateway
{
    /// <summary>References Paystack knows but reports unpaid, with Paystack's status (abandoned, failed...).</summary>
    public Dictionary<string, string> Unpaid { get; } = [];

    /// <summary>How many plan status lookups each reference got.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, int> StatusCalls { get; } = new();

    /// <summary>When set, each status lookup is counted, then waits here before answering, so tests can hold two confirms together.</summary>
    public SemaphoreSlim? StatusGate { get; set; }

    public async Task<ProviderChargeStatus> GetStatusAsync(string reference, CancellationToken ct)
    {
        StatusCalls.AddOrUpdate(reference, 1, (_, n) => n + 1);
        if (StatusGate is { } gate) await gate.WaitAsync(ct);
        if (Charges.TryGetValue(reference, out var charge))
            return new ProviderChargeStatus(ProviderChargeState.Success, charge.AmountMinor, charge.Currency);
        if (Unpaid.TryGetValue(reference, out var status))
            return new ProviderChargeStatus(status == "failed" ? ProviderChargeState.Failed : ProviderChargeState.Pending, 0, null);
        return ProviderChargeStatus.NotFound;
    }

    public string Name => "paystack";
    public bool IsConfigured => true;
    public List<PaymentInit> Initialized { get; } = [];
    public Dictionary<string, VerifiedCharge> Charges { get; } = [];
    public List<(string Reference, long AmountMinor)> Refunds { get; } = [];

    public Task<string> InitializeAsync(PaymentInit init, CancellationToken ct)
    {
        Initialized.Add(init);
        return Task.FromResult($"https://checkout.example/{init.Reference}");
    }

    public Task<VerifiedCharge?> VerifyAsync(string reference, CancellationToken ct) => Task.FromResult(Charges.GetValueOrDefault(reference));

    /// <summary>Holds each refund call open this long, so tests can overlap two requests.</summary>
    public TimeSpan RefundDelay { get; set; }

    /// <summary>
    /// Refunds of these references are taken, but the answer is lost (a timeout after Paystack took it): the caller sees an
    /// exception that is not a refusal.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, bool> RefundTimesOut { get; } = new();
    /// <summary>Refunds of these references are refused by Paystack (400), with this message.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> RefundRefused { get; } = new();
    /// <summary>What a refund status lookup answers per reference; by default Refunded when a refund was taken, otherwise NotFound.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, RefundState> RefundStates { get; } = new();

    public async Task RefundAsync(string reference, long amountMinor, int attempt, CancellationToken ct)
    {
        if (RefundDelay > TimeSpan.Zero) await Task.Delay(RefundDelay, ct);
        if (RefundRefused.TryGetValue(reference, out var refusal)) throw new RefundRefusedException($"Paystack didn't accept the refund: {refusal}");
        lock (Refunds) Refunds.Add((reference, amountMinor));
        if (RefundTimesOut.ContainsKey(reference))
            throw new ServiceUnavailableException("Paystack didn't answer in time. Check the refund in your Paystack dashboard before trying again.");
    }

    public Task<RefundState> RefundStatusAsync(string reference, int attempt, CancellationToken ct)
    {
        if (RefundStates.TryGetValue(reference, out var state)) return Task.FromResult(state);
        lock (Refunds) return Task.FromResult(Refunds.Any(r => r.Reference == reference) ? RefundState.Refunded : RefundState.NotFound);
    }
}

/// <summary>
/// Stands in for OPay's cashier API at the HTTP level. Create requires <c>Bearer {PublicKey}</c> and the MerchantId header; status
/// requires <c>Bearer {HMAC-SHA512(exact body bytes, SecretKey) hex}</c>, like OPay. Orders start INITIAL until a test pays them.
/// </summary>
public sealed class FakeOpay : HttpMessageHandler
{
    public const string MerchantId = "256600000000001";
    public const string PublicKey = "OPAYPUB_integration_public";
    public const string SecretKey = "OPAYPRV_integration_secret";
    public const string BaseUrl = "https://sandboxapi.opay.test";

    public sealed record Order(string Reference, long Total, string Currency, string Country, string ReturnUrl, string CallbackUrl, string CancelUrl);

    public System.Collections.Concurrent.ConcurrentDictionary<string, Order> Created { get; } = new();
    /// <summary>What a status call answers per reference: OPay's status and the amount it reports.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, (string Status, long Total, string Currency)> Statuses { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, int> StatusCalls { get; } = new();
    /// <summary>Requests refused for a wrong key, merchant id or signature. Tests expect none.</summary>
    public int Rejected;
    public (string Body, string Authorization)? LastStatusRequest { get; private set; }

    /// <summary>When set, each status call is counted, then waits here before answering, so tests can hold two confirms together.</summary>
    public SemaphoreSlim? StatusGate { get; set; }

    /// <summary>Status calls for these references answer 50003 ("service not available, try again").</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, bool> UnavailableFor { get; } = new();

    /// <summary>OPay reports the order paid, for the amount and currency it was created with.</summary>
    public void Pay(string reference)
    {
        var order = Created[reference];
        Statuses[reference] = ("SUCCESS", order.Total, order.Currency);
    }

    public void Report(string reference, string status, long? total = null, string? currency = null)
    {
        var order = Created[reference];
        Statuses[reference] = (status, total ?? order.Total, currency ?? order.Currency);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var auth = request.Headers.Authorization;
        var merchant = request.Headers.TryGetValues("MerchantId", out var values) ? values.FirstOrDefault() : null;
        if (!request.RequestUri.ToString().StartsWith(BaseUrl, StringComparison.Ordinal) || merchant != MerchantId || auth?.Scheme != "Bearer")
            return Refuse();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (path == "/api/v1/international/cashier/create")
        {
            if (auth.Parameter != PublicKey) return Refuse();
            var reference = root.GetProperty("reference").GetString()!;
            var amount = root.GetProperty("amount");
            Created[reference] = new Order(reference, amount.GetProperty("total").GetInt64(), amount.GetProperty("currency").GetString()!,
                root.GetProperty("country").GetString()!, root.GetProperty("returnUrl").GetString()!, root.GetProperty("callbackUrl").GetString()!,
                root.GetProperty("cancelUrl").GetString()!);
            Statuses.TryAdd(reference, ("INITIAL", Created[reference].Total, Created[reference].Currency));
            return Answer(new
            {
                code = "00000", message = "SUCCESSFUL",
                data = new { reference, orderNo = $"2411{reference.Length}", cashierUrl = $"{BaseUrl}/cashier/{reference}", status = "INITIAL" },
            });
        }
        if (path == "/api/v1/international/cashier/status")
        {
            // Like the real OPay (checked against the sandbox, 14 Nov 2026): the signature is over the fields in alphabetical order,
            // so a body sent as {"reference":..,"country":..} signed as sent is refused with 02000.
            if (!Signed(root, auth.Parameter)) return Refuse();
            LastStatusRequest = (body, auth.Parameter!);
            var reference = root.GetProperty("reference").GetString()!;
            StatusCalls.AddOrUpdate(reference, 1, (_, n) => n + 1);
            if (StatusGate is { } gate) await gate.WaitAsync(ct);
            if (UnavailableFor.ContainsKey(reference))
                return Answer(new { code = "50003", message = "service not available, please try again" });
            // OPay's codes (query status doc): 02006 order not found, 02000 authentication failed.
            if (!Statuses.TryGetValue(reference, out var status))
                return Answer(new { code = "02006", message = "order not found" });
            return Answer(new
            {
                code = "00000", message = "SUCCESSFUL",
                data = new { reference, orderNo = "24110001", status = status.Status, amount = new { total = status.Total, currency = status.Currency } },
            });
        }
        if (path == "/api/v1/international/payment/refund/create")
        {
            // Signed like the status call: HMAC-SHA512 with the secret key over the body's fields in alphabetical order (nested too).
            if (!Signed(root, auth.Parameter)) return Refuse();
            var reference = root.GetProperty("reference").GetString()!;
            var original = root.GetProperty("originalReference").GetString()!;
            var amount = root.GetProperty("amount");
            var refund = new RefundRequest(reference, original, amount.GetProperty("total").GetInt64(), amount.GetProperty("currency").GetString()!,
                root.GetProperty("country").GetString()!);
            if (RefundUnavailable) return Answer(new { code = "50003", message = "service not available, please try again" });
            // OPay's refund doc: 02006 original payment not found, 20000 duplicate merchant order number.
            if (!Statuses.TryGetValue(original, out var paid) || paid.Status != "SUCCESS")
                return Answer(new { code = "02006", message = "original payment not found." });
            if (!Refunds.TryAdd(reference, refund)) return Answer(new { code = "20000", message = "Duplicate merchant order number" });
            RefundStatuses[reference] = RefundStatus;
            if (RefundAnswerLost) return new HttpResponseMessage(System.Net.HttpStatusCode.GatewayTimeout);
            return Answer(new
            {
                code = "00000", message = "SUCCESSFUL",
                data = new
                {
                    reference, originalReference = original, orderNo = "211003140885499643", originalOrderNo = "211003140885503763", country = refund.Country,
                    refundAmount = new { currency = refund.Currency, total = refund.Total }, orderStatus = RefundStatus,
                },
            });
        }
        if (path == "/api/v1/international/payment/refund/query")
        {
            // OPay's "Query Refund Status": the refund's own reference, signed like the status call.
            if (!Signed(root, auth.Parameter)) return Refuse();
            var reference = root.GetProperty("reference").GetString()!;
            RefundQueries.AddOrUpdate(reference, 1, (_, n) => n + 1);
            if (RefundQueryUnavailable) return Answer(new { code = "50003", message = "service not available, please try again" });
            if (!Refunds.TryGetValue(reference, out var refund)) return Answer(new { code = "02006", message = "not found" });
            return Answer(new
            {
                code = "00000", message = "SUCCESSFUL",
                data = new
                {
                    reference, originalReference = refund.OriginalReference, orderNo = "211003140885499643", originalOrderNo = "211003140885503763",
                    country = refund.Country, amount = new { total = refund.Total, currency = refund.Currency },
                    orderStatus = RefundStatuses.GetValueOrDefault(reference, "SUCCESS"),
                },
            });
        }
        return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }

    /// <summary>Each refund's <c>orderStatus</c> as the refund query reports it (set when the refund is created; tests may change it).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> RefundStatuses { get; } = new();
    /// <summary>Refund queries per refund reference.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, int> RefundQueries { get; } = new();
    /// <summary>Refund queries answer 50003.</summary>
    public bool RefundQueryUnavailable { get; set; }
    /// <summary>Refunds are taken, but the answer never arrives (504), like a timeout after OPay took the refund.</summary>
    public bool RefundAnswerLost { get; set; }

    public sealed record RefundRequest(string Reference, string OriginalReference, long Total, string Currency, string Country);

    /// <summary>Refunds OPay accepted, by our refund reference.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, RefundRequest> Refunds { get; } = new();
    /// <summary>The refund's <c>orderStatus</c> in OPay's answer (INITIAL, PENDING, SUCCESS or FAIL).</summary>
    public string RefundStatus { get; set; } = "SUCCESS";
    /// <summary>Refund calls answer 50003 ("service not available, try again").</summary>
    public bool RefundUnavailable { get; set; }

    /// <summary>True when <paramref name="bearer"/> is HMAC-SHA512 (secret key) of the body with its fields sorted alphabetically, at every level.</summary>
    private static bool Signed(JsonElement root, string? bearer)
    {
        var canonical = JsonSerializer.Serialize(Sorted(root));
        var expected = Convert.ToHexString(System.Security.Cryptography.HMACSHA512.HashData(
            System.Text.Encoding.UTF8.GetBytes(SecretKey), System.Text.Encoding.UTF8.GetBytes(canonical)));
        return string.Equals(bearer, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static object? Sorted(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => new SortedDictionary<string, object?>(e.EnumerateObject().ToDictionary(p => p.Name, p => Sorted(p.Value)), StringComparer.Ordinal),
        JsonValueKind.Array => e.EnumerateArray().Select(Sorted).ToList(),
        _ => e.Clone(),
    };

    private HttpResponseMessage Refuse()
    {
        Interlocked.Increment(ref Rejected);
        return Answer(new { code = "02000", message = "authentication failed" });
    }

    private static HttpResponseMessage Answer(object body) =>
        new(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
