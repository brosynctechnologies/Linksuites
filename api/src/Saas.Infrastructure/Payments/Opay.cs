using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Saas.Application.Billing;
using Saas.Application.Common;

namespace Saas.Infrastructure.Payments;

/// <summary>LinkSuite's own OPay merchant account, for plan payments (env OPAY_*). Keys are never logged or returned.</summary>
public sealed class OpayOptions
{
    public const string Section = "Opay";
    public const string SandboxBaseUrl = "https://sandboxapi.opaycheckout.com";

    public string? MerchantId { get; set; }
    public string? PublicKey { get; set; }
    public string? SecretKey { get; set; }
    /// <summary>Sandbox by default; live is <c>https://api.opaycheckout.com</c>.</summary>
    public string BaseUrl { get; set; } = SandboxBaseUrl;
    /// <summary>
    /// Development click-throughs without OPay: create returns the return URL and status reports SUCCESS, in-process. Honoured only
    /// when the host environment is Development (appsettings.Development.json turns it on); ignored anywhere else.
    /// </summary>
    public bool AllowLocalFixture { get; set; }

    /// <summary>
    /// In Production, OPay counts as not configured while <see cref="BaseUrl"/> is a sandbox host (test cards would buy real plans),
    /// unless this is true.
    /// </summary>
    public bool AllowSandboxInProduction { get; set; }
}

/// <summary>
/// OPay's international cashier API (doc.opaycheckout.com): create with <c>Bearer {PublicKey}</c>, status with
/// <c>Bearer {HMAC-SHA512(body, SecretKey) hex}</c>, both with the <c>MerchantId</c> header. Amounts are in kobo, country NG.
/// </summary>
public sealed class OpayClient
{
    public const string SuccessCode = "00000";
    /// <summary>"Service not available, please try again": not an answer, so the payment stays Pending and is asked again.</summary>
    public const string TryAgainCode = "50003";
    /// <summary>Authentication failed (02000), and the merchant codes 02002 and 02007: our configuration is wrong.</summary>
    public static readonly IReadOnlySet<string> MisconfiguredCodes = new HashSet<string>(StringComparer.Ordinal) { "02000", "02002", "02007" };
    /// <summary>The most local fixture orders kept in memory; the oldest are dropped beyond it.</summary>
    public const int MaxLocalOrders = 500;

    private static readonly ConcurrentDictionary<string, (PaymentInit Init, long Order)> LocalOrders = new(StringComparer.Ordinal);
    private static long localOrder;

    private readonly HttpClient http;
    private readonly OpayOptions options;
    private readonly ILogger<OpayClient> logger;

    public OpayClient(HttpClient http, IOptions<OpayOptions> options, IHostEnvironment environment, ILogger<OpayClient> logger)
    {
        this.http = http;
        this.options = options.Value;
        this.logger = logger;
        UsesLocalFixture = this.options.AllowLocalFixture && environment.IsDevelopment();
        SandboxRefused = environment.IsProduction() && !this.options.AllowSandboxInProduction && IsSandbox(this.options.BaseUrl);
    }

    /// <summary>True only in Development with <see cref="OpayOptions.AllowLocalFixture"/> on.</summary>
    public bool UsesLocalFixture { get; }

    /// <summary>
    /// Production pointed at OPay's sandbox (test cards would buy real plans) without <see cref="OpayOptions.AllowSandboxInProduction"/>:
    /// OPay then counts as not configured.
    /// </summary>
    public bool SandboxRefused { get; }

    /// <summary>An empty base URL means the sandbox default.</summary>
    public static bool IsSandbox(string? baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl)
        || (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : baseUrl).Contains("sandbox", StringComparison.OrdinalIgnoreCase);

    public bool HasKeys => !string.IsNullOrWhiteSpace(options.MerchantId) && !string.IsNullOrWhiteSpace(options.PublicKey)
        && !string.IsNullOrWhiteSpace(options.SecretKey);

    private string Url(string path) => $"{(string.IsNullOrWhiteSpace(options.BaseUrl) ? OpayOptions.SandboxBaseUrl : options.BaseUrl).TrimEnd('/')}{path}";

    /// <summary>A timeout (HttpClient.Timeout) is "OPay didn't answer", not the caller giving up.</summary>
    private static bool IsTimeout(Exception ex, CancellationToken ct) => ex is TaskCanceledException && !ct.IsCancellationRequested;

    /// <summary>Starts a cashier order and returns the URL to send the payer to. <paramref name="callbackUrl"/> receives OPay's callback.</summary>
    public async Task<string> CreateAsync(PaymentInit init, string callbackUrl, CancellationToken ct)
    {
        if (UsesLocalFixture)
        {
            LocalOrders[init.Reference] = (init, Interlocked.Increment(ref localOrder));
            var excess = LocalOrders.Count - MaxLocalOrders;
            if (excess > 0)
                foreach (var old in LocalOrders.OrderBy(x => x.Value.Order).Take(excess).ToList())
                    LocalOrders.TryRemove(old.Key, out _);
            return init.CallbackUrl;
        }

        var planName = init.Metadata.GetValueOrDefault("planName") ?? "Plan";
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            country = "NG",
            reference = init.Reference,
            amount = new { total = init.AmountMinor, currency = init.Currency },
            returnUrl = init.CallbackUrl,
            callbackUrl,
            // A payer who cancels comes back to the billing page marked cancelled, which shows "Payment cancelled" without checking.
            cancelUrl = $"{init.CallbackUrl}{(init.CallbackUrl.Contains('?') ? '&' : '?')}cancelled=1",
            expireAt = 30,
            userInfo = new { userEmail = init.Email, userId = init.Metadata.GetValueOrDefault("tenantId") ?? init.Email, userName = init.Email },
            product = new { name = $"LinkSuite {planName}", description = $"LinkSuite {planName} plan" },
        });
        try
        {
            using var request = Request("/api/v1/international/cashier/create", body, options.PublicKey!);
            using var response = await http.SendAsync(request, ct);
            var (code, message, data) = await ReadAsync(response, ct);
            if (!response.IsSuccessStatusCode || code != SuccessCode || data is not { } d
                || !d.TryGetProperty("cashierUrl", out var url) || url.ValueKind != JsonValueKind.String)
            {
                logger.LogWarning("OPay refused to create order {Reference}: HTTP {Status}, code {Code}, {Message}", init.Reference, (int)response.StatusCode, code, message);
                throw new ServiceUnavailableException("Could not start the payment. Please try again.");
            }
            return url.GetString()!;
        }
        catch (Exception ex) when (IsTimeout(ex, ct) || ex is HttpRequestException || ex is JsonException)
        {
            logger.LogWarning("OPay create for {Reference} failed: {Error}", init.Reference, ex.GetType().Name);
            throw new ServiceUnavailableException("Could not start the payment. Please try again.");
        }
    }

    /// <summary>
    /// OPay's status for a reference. Throws <see cref="HttpRequestException"/> when OPay can't answer (5xx, 429, network, timeout).
    /// An answer with an error code (unknown order, for example) is <see cref="ProviderChargeState.NotFound"/>.
    /// </summary>
    public async Task<ProviderChargeStatus> StatusAsync(string reference, CancellationToken ct)
    {
        if (UsesLocalFixture)
            return LocalOrders.TryGetValue(reference, out var local)
                ? new ProviderChargeStatus(ProviderChargeState.Success, local.Init.AmountMinor, local.Init.Currency)
                : ProviderChargeStatus.NotFound;

        // The signature covers these exact bytes, which are also what is sent. OPay checks it against the fields in alphabetical
        // order (confirmed on the sandbox: reference-first is refused with 02000), so the fields are written in that order.
        var body = JsonSerializer.SerializeToUtf8Bytes(new { country = "NG", reference });
        var signature = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(options.SecretKey ?? ""), body));
        try
        {
            using var request = Request("/api/v1/international/cashier/status", body, signature);
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new HttpRequestException($"OPay could not check the payment right now ({(int)response.StatusCode}).", null, response.StatusCode);
            var (code, message, data) = await ReadAsync(response, ct);
            if (code == TryAgainCode)
                throw new HttpRequestException($"OPay asked to try again ({code}).");
            if (code is not null && MisconfiguredCodes.Contains(code))
            {
                // Our merchant id or keys are wrong: every payment would stay unconfirmed, so this is an error, not "unknown order".
                logger.LogError("OPay refused the status check for {Reference} with {Code} ({Message}): check Opay:MerchantId, Opay:SecretKey and Opay:BaseUrl",
                    reference, code, message);
                throw new HttpRequestException($"OPay refused the status check ({code}).");
            }
            if (!response.IsSuccessStatusCode || code != SuccessCode || data is not { } d)
            {
                logger.LogWarning("OPay status for {Reference}: HTTP {Status}, code {Code}, {Message}", reference, (int)response.StatusCode, code, message);
                return ProviderChargeStatus.NotFound;
            }
            var status = d.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var (total, currency) = Amount(d);
            return status?.ToUpperInvariant() switch
            {
                "SUCCESS" => new ProviderChargeStatus(ProviderChargeState.Success, total, currency),
                "FAIL" or "CLOSE" => new ProviderChargeStatus(ProviderChargeState.Failed, total, currency),
                _ => new ProviderChargeStatus(ProviderChargeState.Pending, total, currency), // INITIAL, PENDING
            };
        }
        catch (Exception ex) when (IsTimeout(ex, ct))
        {
            throw new HttpRequestException("OPay did not answer in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("OPay sent an unreadable answer.", ex);
        }
    }

    private HttpRequestMessage Request(string path, byte[] body, string bearer)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, Url(path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.TryAddWithoutValidation("MerchantId", options.MerchantId);
        return request;
    }

    private static async Task<(string? Code, string? Message, JsonElement? Data)> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return (null, null, null);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, null, null);
        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        JsonElement? data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d.Clone() : null;
        return (Str("code"), Str("message"), data);
    }

    /// <summary><c>amount.total</c> (kobo, a number or a string) and <c>amount.currency</c>.</summary>
    private static (long Total, string? Currency) Amount(JsonElement data)
    {
        if (!data.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.Object) return (0, null);
        long total = 0;
        if (amount.TryGetProperty("total", out var t))
        {
            if (t.ValueKind == JsonValueKind.Number) t.TryGetInt64(out total);
            else if (t.ValueKind == JsonValueKind.String) long.TryParse(t.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out total);
        }
        var currency = amount.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        return (total, currency);
    }
}

/// <summary>Plan payments to LinkSuite's OPay account. Only plan billing uses it; courses, boosts and stays stay on Paystack.</summary>
public sealed class OpayGateway(OpayClient client, AppUrls urls) : IPlanPaymentGateway
{
    public string Name => BillingOptions.Opay;
    public bool IsConfigured => client.UsesLocalFixture || (client.HasKeys && !client.SandboxRefused);

    public Task<string> InitializeAsync(PaymentInit init, CancellationToken ct) =>
        IsConfigured ? client.CreateAsync(init, $"{urls.ApiBaseUrl.TrimEnd('/')}/api/webhooks/opay", ct)
            : throw new ServiceUnavailableException(BillingService.NotConfiguredMessage);

    public async Task<VerifiedCharge?> VerifyAsync(string reference, CancellationToken ct)
    {
        try
        {
            var status = await GetStatusAsync(reference, ct);
            return status.State == ProviderChargeState.Success && status.Currency is not null
                ? new VerifiedCharge(reference, status.AmountMinor, status.Currency, DateTimeOffset.UtcNow) : null;
        }
        catch (HttpRequestException) { return null; }
    }

    public Task RefundAsync(string reference, long amountMinor, CancellationToken ct) =>
        throw new ServiceUnavailableException("OPay refunds aren't available here yet. Refund the payment from the OPay dashboard.");

    public Task<ProviderChargeStatus> GetStatusAsync(string reference, CancellationToken ct) =>
        IsConfigured ? client.StatusAsync(reference, ct) : throw new HttpRequestException("OPay is not configured.");
}

/// <summary>
/// Startup warning (fix round I2): Production pointed at OPay's sandbox is refused, so plan checkouts with OPay answer 503 until
/// <c>Opay:BaseUrl</c> is the live host (<c>https://api.opaycheckout.com</c>) or <c>Opay:AllowSandboxInProduction</c> is set.
/// </summary>
public sealed class OpayStartupCheck(OpayClient client, BillingOptions billing, ILogger<OpayStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (client.SandboxRefused && (client.HasKeys || string.Equals(billing.PlanProvider, BillingOptions.Opay, StringComparison.OrdinalIgnoreCase)))
            logger.LogWarning("OPay is not configured: Opay:BaseUrl is OPay's sandbox in Production. Set Opay:BaseUrl (OPAY_BASE_URL) to "
                + "https://api.opaycheckout.com, or Opay:AllowSandboxInProduction=true to accept test payments on purpose");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
