using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Saas.Application.Common;
using Saas.Application.Payouts;

namespace Saas.Infrastructure.Payments;

/// <summary>
/// Paystack's transfer side, on the platform's own account (Paystack:SecretKey): banks, resolve account number, transfer recipients,
/// subaccounts, transfers and transfer verify.
/// </summary>
public sealed class PaystackTransfersClient(HttpClient http, IOptions<PaystackOptions> options) : IPayoutProvider
{
    private const string Base = "https://api.paystack.co";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.SecretKey);

    public async Task<IReadOnlyList<BankDto>> BanksAsync(string currency, CancellationToken ct)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, $"/bank?currency={Uri.EscapeDataString(currency)}&perPage=200", null, ct);
        if (status != HttpStatusCode.OK || !Ok(body) || body.GetProperty("data").ValueKind != JsonValueKind.Array)
            throw new ServiceUnavailableException("Couldn't load the list of banks. Please try again.");
        return body.GetProperty("data").EnumerateArray()
            .Where(b => !b.TryGetProperty("active", out var a) || a.ValueKind != JsonValueKind.False)
            .Where(b => !b.TryGetProperty("is_deleted", out var d) || d.ValueKind != JsonValueKind.True)
            .Select(b => new BankDto(Text(b, "name") ?? "", Text(b, "code") ?? ""))
            .Where(b => b.Name.Length > 0 && b.Code.Length > 0)
            .DistinctBy(b => b.Code).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string?> ResolveAccountAsync(string accountNumber, string bankCode, CancellationToken ct)
    {
        var (status, body) = await SendAsync(HttpMethod.Get,
            $"/bank/resolve?account_number={Uri.EscapeDataString(accountNumber)}&bank_code={Uri.EscapeDataString(bankCode)}", null, ct);
        if (Unanswered(status)) throw new ServiceUnavailableException("The bank didn't answer. Please try again in a minute.");
        if (status != HttpStatusCode.OK || !Ok(body) || !body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
        return Text(data, "account_name")?.Trim() is { Length: > 0 } name ? name : null;
    }

    public async Task<string> CreateRecipientAsync(string name, string accountNumber, string bankCode, string currency, CancellationToken ct)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/transferrecipient",
            new { type = "nuban", name, account_number = accountNumber, bank_code = bankCode, currency }, ct);
        return Success(status) && Ok(body) && body.TryGetProperty("data", out var data) && Text(data, "recipient_code") is { Length: > 0 } code
            ? code
            : throw new ServiceUnavailableException($"Paystack couldn't save the bank account for transfers{Because(body)}");
    }

    public async Task<string> CreateSubaccountAsync(string businessName, string bankCode, string accountNumber, decimal percentageCharge, string? contactEmail,
        CancellationToken ct)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/subaccount", new
        {
            business_name = businessName, settlement_bank = bankCode, account_number = accountNumber, percentage_charge = percentageCharge,
            primary_contact_email = contactEmail, description = "LinkSuite seller",
        }, ct);
        return Success(status) && Ok(body) && body.TryGetProperty("data", out var data) && Text(data, "subaccount_code") is { Length: > 0 } code
            ? code
            : throw new ServiceUnavailableException($"Paystack couldn't set up direct settlement{Because(body)}");
    }

    public async Task<TransferOutcome> TransferAsync(long amountMinor, string recipientCode, string reference, string reason, string currency, CancellationToken ct)
    {
        HttpStatusCode status;
        JsonElement body;
        try
        {
            (status, body) = await SendAsync(HttpMethod.Post, "/transfer",
                new { source = "balance", amount = amountMinor, recipient = recipientCode, reference, reason, currency }, ct);
        }
        catch (ServiceUnavailableException)
        {
            return new TransferOutcome(TransferState.Unknown, null, null, null);
        }
        if (Unanswered(status)) return new TransferOutcome(TransferState.Unknown, null, null, null);
        if (!Success(status) || !Ok(body))
            // Paystack answered and refused it (insufficient balance, invalid recipient...): no transfer was made.
            return new TransferOutcome(TransferState.Failed, "failed", null, Text(body, "message") ?? $"Paystack refused the transfer ({(int)status}).");
        return Outcome(body.GetProperty("data"));
    }

    public async Task<TransferOutcome> VerifyTransferAsync(string reference, CancellationToken ct)
    {
        HttpStatusCode status;
        JsonElement body;
        try
        {
            (status, body) = await SendAsync(HttpMethod.Get, $"/transfer/verify/{Uri.EscapeDataString(reference)}", null, ct);
        }
        catch (ServiceUnavailableException)
        {
            return new TransferOutcome(TransferState.Unknown, null, null, null);
        }
        if (Unanswered(status)) return new TransferOutcome(TransferState.Unknown, null, null, null);
        // Only a clear "no such transfer" may lead to a re-send: HTTP 404, or Paystack's own "Transfer not found" message. Any other
        // refusal (400, a 200 with status false, a bad key) says nothing about whether the transfer exists.
        if (status is HttpStatusCode.NotFound
            || (!Ok(body) && Text(body, "message") is { } message && message.Contains("transfer not found", StringComparison.OrdinalIgnoreCase)))
            return new TransferOutcome(TransferState.NotFound, null, null, Text(body, "message"));
        if (!Success(status) || !body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return new TransferOutcome(TransferState.Unknown, null, null, null);
        return Outcome(data);
    }

    private static TransferOutcome Outcome(JsonElement data)
    {
        var status = Text(data, "status");
        return new TransferOutcome(TransferStatuses.StateOf(status), status?.ToLowerInvariant(), Text(data, "transfer_code"),
            null); // data.reason is our own narration, not why it failed
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string path, object? content, CancellationToken ct)
    {
        if (!IsConfigured) throw new ServiceUnavailableException("Paystack isn't set up on the platform yet.");
        using var request = new HttpRequestMessage(method, Base + path);
        if (content is not null) request.Content = JsonContent.Create(content);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);
        try
        {
            using var response = await http.SendAsync(request, ct);
            JsonElement body;
            try { body = await response.Content.ReadFromJsonAsync<JsonElement>(ct); }
            catch (JsonException) { body = default; }
            return (response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new ServiceUnavailableException("Paystack didn't answer. Please try again in a minute.");
        }
    }

    private static bool Success(HttpStatusCode s) => (int)s is >= 200 and < 300;
    private static bool Unanswered(HttpStatusCode s) => (int)s >= 500 || s == HttpStatusCode.TooManyRequests;
    private static bool Ok(JsonElement body) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Because(JsonElement body) => Text(body, "message") is { Length: > 0 } m ? $": {m}" : ".";
}
