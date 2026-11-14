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
    }

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
        builder.UseSetting("Notifications:DispatcherEnabled", "false");
        builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "saas-it-uploads"));
        builder.UseSetting("Jobs:InvoicesEnabled", "false");
        builder.UseSetting("Jobs:BookingsEnabled", "false");
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:PublicWritesPerMinute", "100000");
        builder.UseSetting("RateLimits:WebhooksPerMinute", "10000");
        builder.UseSetting("RateLimits:OtpPer10Minutes", "100000");
        builder.UseSetting("RateLimits:PreviewPerMinute", "1000");
        builder.UseSetting("RateLimits:AdminPerMinute", "1000"); // NextReleaseAuthTests lowers it for its own factory
        builder.UseSetting("Video:ProcessorEnabled", "false");
        builder.UseSetting("Jobs:PropertyEnabled", "false"); // tests run PropertyJobs by hand
        builder.UseSetting("Jobs:SchoolEnabled", "false"); // tests run SchoolJobs by hand
        builder.UseSetting("Jobs:CareEnabled", "false"); // tests run CareJobs by hand
        builder.UseSetting("Photos:ProcessorEnabled", "false"); // tests run the photo processor by hand
        builder.UseSetting("Video:FfmpegPath", "ffmpeg-not-installed-in-tests"); // uploads are published as MP4 in tests
        builder.UseSetting("Platform:AdminEmails:0", AdminEmail);
        builder.UseSetting("Platform:AdminEmails:1", UnclaimedAdminEmail);
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IMerchantPayments>(_ => Merchant);
            services.AddSingleton<IExchangeRateProvider, FakeRates>();
            services.AddScoped<IPaymentGateway>(_ => Gateway);
        });
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

/// <summary>Fixed rates (units per USD) so tests never call the real provider.</summary>
public sealed class FakeRates : IExchangeRateProvider
{
    public string Name => "Test rates";

    public Task<RateSnapshot> FetchLatestAsync(CancellationToken ct) => Task.FromResult(new RateSnapshot(
        DateOnly.FromDateTime(DateTime.UtcNow),
        new Dictionary<string, decimal> { ["USD"] = 1m, ["NGN"] = 1600m, ["GBP"] = 0.8m, ["GHS"] = 16m }));
}

/// <summary>Stands in for the platform's own Paystack account.</summary>
public sealed class FakePlatformGateway : IPaymentGateway
{
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

    public async Task RefundAsync(string reference, long amountMinor, CancellationToken ct)
    {
        if (RefundDelay > TimeSpan.Zero) await Task.Delay(RefundDelay, ct);
        lock (Refunds) Refunds.Add((reference, amountMinor));
    }
}
