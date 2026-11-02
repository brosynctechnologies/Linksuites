using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Saas.Application.Billing;
using Saas.Application.Common;

namespace Saas.Infrastructure.Payments;

public sealed class PaystackOptions
{
    public const string Section = "Paystack";
    /// <summary>Our own sk_test_... or sk_live_... key, for plan subscriptions. Empty disables plan upgrades.</summary>
    public string? SecretKey { get; set; }
    /// <summary>
    /// Lets keys starting with <see cref="MerchantKeyRules.LocalFixtureKeyPrefix"/> confirm payments in-process, without Paystack, for
    /// localhost click-throughs. Off by default; on only in appsettings.Development.json. When off, such a key is refused at save and,
    /// if one is already stored, is sent to Paystack like any other key.
    /// </summary>
    public bool AllowLocalFixtureKeys { get; set; }
}

/// <summary>Paystack's HTTP API, with the secret key passed per call so it works for us and for each business.</summary>
public sealed class PaystackClient : IMerchantPayments
{
    /// <summary>The most local fixture checkouts kept in memory; the oldest are dropped beyond it.</summary>
    public const int MaxLocalCharges = 500;

    /// <summary>Checkouts started with a local fixture key, until verified. Bounded by <see cref="MaxLocalCharges"/>.</summary>
    private static readonly ConcurrentDictionary<string, (PaymentInit Init, long Order)> LocalCharges = new(StringComparer.Ordinal);
    private static long localOrder;

    private readonly HttpClient http;
    private readonly bool allowLocalFixtureKeys;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public PaystackClient(HttpClient http, IOptions<PaystackOptions> options)
    {
        this.http = http;
        allowLocalFixtureKeys = options.Value.AllowLocalFixtureKeys;
    }

    /// <summary>A client with local fixture keys off, as in production.</summary>
    public PaystackClient(HttpClient http) : this(http, Options.Create(new PaystackOptions())) { }

    /// <summary>
    /// Keys used for localhost click-throughs (Development only). They never leave the process: initialize returns the callback URL, and
    /// verify confirms the same charge. With <see cref="PaystackOptions.AllowLocalFixtureKeys"/> off they call Paystack like any key.
    /// </summary>
    public const string LocalFixtureKeyPrefix = MerchantKeyRules.LocalFixtureKeyPrefix;

    public static bool IsLocalFixtureKey(string secretKey) => MerchantKeyRules.IsLocalFixtureKey(secretKey);

    private bool UsesLocalFixture(string secretKey) => allowLocalFixtureKeys && IsLocalFixtureKey(secretKey);

    private static void RememberLocalCharge(PaymentInit init)
    {
        LocalCharges[init.Reference] = (init, Interlocked.Increment(ref localOrder));
        var excess = LocalCharges.Count - MaxLocalCharges;
        if (excess <= 0) return;
        foreach (var old in LocalCharges.OrderBy(x => x.Value.Order).Take(excess).ToList())
            LocalCharges.TryRemove(old.Key, out _);
    }

    /// <summary>A timeout (HttpClient.Timeout) is "Paystack didn't answer", not the caller giving up.</summary>
    private static bool IsTimeout(Exception ex, CancellationToken ct) => ex is TaskCanceledException && !ct.IsCancellationRequested;

    public async Task<string> InitializeAsync(string secretKey, PaymentInit init, CancellationToken ct)
    {
        if (UsesLocalFixture(secretKey))
        {
            RememberLocalCharge(init);
            return init.CallbackUrl;
        }

        try
        {
            return await InitializeRemoteAsync(secretKey, init, ct);
        }
        catch (Exception ex) when (IsTimeout(ex, ct) || ex is HttpRequestException)
        {
            throw new ServiceUnavailableException("Could not start the payment. Please try again.");
        }
    }

    private async Task<string> InitializeRemoteAsync(string secretKey, PaymentInit init, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/transaction/initialize")
        {
            Content = JsonContent.Create(new
            {
                email = init.Email,
                amount = init.AmountMinor,
                currency = init.Currency,
                reference = init.Reference,
                callback_url = init.CallbackUrl,
                metadata = init.Metadata,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!response.IsSuccessStatusCode || !body.GetProperty("status").GetBoolean())
            throw new ServiceUnavailableException("Could not start the payment. Please try again.");
        return body.GetProperty("data").GetProperty("authorization_url").GetString()!;
    }

    public async Task<VerifiedCharge?> VerifyAsync(string secretKey, string reference, CancellationToken ct) =>
        (await LookupAsync(secretKey, reference, ct)).Charge;

    public async Task<ChargeLookup> LookupAsync(string secretKey, string reference, CancellationToken ct)
    {
        if (UsesLocalFixture(secretKey))
        {
            // Forgotten once confirmed: a verified checkout is never asked about again.
            return LocalCharges.TryRemove(reference, out var local)
                ? new ChargeLookup(new VerifiedCharge(local.Init.Reference, local.Init.AmountMinor, local.Init.Currency, DateTimeOffset.UtcNow), null)
                : ChargeLookup.NotFound;
        }

        try
        {
            return await LookupRemoteAsync(secretKey, reference, ct);
        }
        catch (Exception ex) when (IsTimeout(ex, ct))
        {
            // HttpClient.Timeout elapsed: no answer, the same as a 5xx, so callers keep the payment as "unknown".
            throw new HttpRequestException("Paystack did not answer in time.", ex);
        }
    }

    private async Task<ChargeLookup> LookupRemoteAsync(string secretKey, string reference, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.paystack.co/transaction/verify/{Uri.EscapeDataString(reference)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        using var response = await http.SendAsync(request, ct);
        // Paystack not answering (5xx, 429) is not an answer: throw so callers can tell "unknown" from "not found" (404, 400, status false).
        if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new HttpRequestException($"Paystack could not verify the payment right now ({(int)response.StatusCode}).", null, response.StatusCode);
        if (!response.IsSuccessStatusCode) return ChargeLookup.NotFound;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (body.TryGetProperty("status", out var ok) && ok.ValueKind == JsonValueKind.False) return ChargeLookup.NotFound;
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return ChargeLookup.NotFound;
        if (ParseCharge(data) is { } charge) return new ChargeLookup(charge, null);
        // Paystack has the transaction, but it wasn't paid (abandoned, failed, reversed...).
        var status = data.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        return new ChargeLookup(null, string.IsNullOrWhiteSpace(status) ? "unknown" : status);
    }

    public async Task RefundAsync(string secretKey, string reference, long amountMinor, CancellationToken ct)
    {
        if (UsesLocalFixture(secretKey)) return;

        try
        {
            await RefundRemoteAsync(secretKey, reference, amountMinor, ct);
        }
        catch (Exception ex) when (IsTimeout(ex, ct))
        {
            throw new ServiceUnavailableException("Paystack didn't answer in time. Check the refund in your Paystack dashboard before trying again.");
        }
    }

    private async Task RefundRemoteAsync(string secretKey, string reference, long amountMinor, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.paystack.co/refund")
        {
            Content = JsonContent.Create(new { transaction = reference, amount = amountMinor }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!response.IsSuccessStatusCode || !body.TryGetProperty("status", out var ok) || !ok.GetBoolean())
        {
            var message = body.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new ServiceUnavailableException($"Paystack didn't accept the refund{(message is null ? "." : $": {message}")}");
        }
    }

    /// <summary>Paystack signs the raw body with HMAC-SHA512 using the secret key (x-paystack-signature header).</summary>
    public static bool IsValidSignature(string secretKey, string rawBody, string? signature)
    {
        if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(signature)) return false;
        var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(rawBody));
        byte[] given;
        try { given = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <summary>Returns the charge for a successful "charge.success" webhook event, otherwise null.</summary>
    public static VerifiedCharge? ParseChargeSuccess(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        if (!root.TryGetProperty("event", out var ev) || ev.GetString() != "charge.success") return null;
        return root.TryGetProperty("data", out var data) ? ParseCharge(data) : null;
    }

    private static VerifiedCharge? ParseCharge(JsonElement data)
    {
        if (!data.TryGetProperty("status", out var status) || status.GetString() != "success") return null;
        var paidAt = data.TryGetProperty("paid_at", out var p) && p.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(p.GetString(), out var parsed) ? parsed.ToUniversalTime() : DateTimeOffset.UtcNow;
        return new VerifiedCharge(data.GetProperty("reference").GetString()!, data.GetProperty("amount").GetInt64(),
            data.GetProperty("currency").GetString()!, paidAt);
    }
}

/// <summary>Plan subscriptions paid to us, with our own key from configuration.</summary>
public sealed class PaystackGateway(PaystackClient client, IOptions<PaystackOptions> options) : IPaymentGateway
{
    public string Name => "paystack";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.SecretKey);

    public Task<string> InitializeAsync(PaymentInit init, CancellationToken ct) => client.InitializeAsync(options.Value.SecretKey!, init, ct);

    /// <summary>Platform payments keep their behaviour: Paystack being unavailable reads as "not confirmed yet" (null).</summary>
    public async Task<VerifiedCharge?> VerifyAsync(string reference, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try { return await client.VerifyAsync(options.Value.SecretKey!, reference, ct); }
        catch (HttpRequestException) { return null; }
    }

    public Task RefundAsync(string reference, long amountMinor, CancellationToken ct) =>
        IsConfigured ? client.RefundAsync(options.Value.SecretKey!, reference, amountMinor, ct)
            : throw new ServiceUnavailableException("Online payments aren't set up, so refunds can't be sent.");

    public bool IsValidSignature(string rawBody, string? signature) =>
        IsConfigured && PaystackClient.IsValidSignature(options.Value.SecretKey!, rawBody, signature);
}
