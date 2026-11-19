using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Saas.IntegrationTests;

/// <summary>
/// Stands in for Paystack's transfer and subaccount API at the HTTP level (banks, resolve, transfer recipients, subaccounts, transfers and
/// transfer verify), so the real client's requests and parsing are exercised. Every call must carry <c>Bearer {ApiFixture.PaystackSecret}</c>.
/// A transfer reference Paystack already has answers with the existing transfer, as Paystack's idempotent references do.
/// </summary>
public sealed class FakePaystackTransfers : HttpMessageHandler
{
    public sealed record Transfer(string Reference, long Amount, string Recipient, string Reason, string TransferCode)
    {
        public string Status { get; set; } = "pending";
    }

    /// <summary>Account numbers the fake bank "058" knows, with the name on the account.</summary>
    public ConcurrentDictionary<string, string> Accounts { get; } = new() { ["0123456789"] = "ADA LOVELACE", ["0987654321"] = "GRACE HOPPER" };

    public ConcurrentQueue<JsonElement> Recipients { get; } = new();
    public ConcurrentQueue<JsonElement> Subaccounts { get; } = new();
    public ConcurrentDictionary<string, Transfer> Transfers { get; } = new(StringComparer.Ordinal);

    /// <summary>Every POST /transfer, including repeats of a reference Paystack already has.</summary>
    public ConcurrentQueue<string> TransferCalls { get; } = new();

    /// <summary>The status a new transfer starts in (Paystack answers "pending" when transfer OTP is off, "otp" when it is on).</summary>
    public string NewTransferStatus { get; set; } = "pending";

    /// <summary>The next POST /transfer answers 503 without recording the transfer (Paystack didn't get it).</summary>
    public bool UnavailableNextTransfer { get; set; }

    /// <summary>The next POST /transfer is refused with this message (400), like "Your balance is not enough to fulfil this request".</summary>
    public string? RefuseNextTransfer { get; set; }

    private int counter;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization?.ToString() != $"Bearer {ApiFixture.PaystackSecret}")
            return Answer(HttpStatusCode.Unauthorized, new { status = false, message = "Invalid key" });
        var path = request.RequestUri!.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? default : JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsStringAsync(ct));

        if (request.Method == HttpMethod.Get && path == "/bank")
            return Answer(HttpStatusCode.OK, new
            {
                status = true, message = "Banks retrieved",
                data = new object[]
                {
                    new { name = "Guaranty Trust Bank", code = "058", active = true, supports_transfer = true, currency = "NGN" },
                    new { name = "Access Bank", code = "044", active = true, supports_transfer = true, currency = "NGN" },
                    new { name = "Closed Bank", code = "999", active = false, supports_transfer = true, currency = "NGN" },
                },
            });

        if (request.Method == HttpMethod.Get && path == "/bank/resolve")
            return query["bank_code"] == "058" && Accounts.TryGetValue(query["account_number"] ?? "", out var name)
                ? Answer(HttpStatusCode.OK, new { status = true, message = "Account number resolved", data = new { account_number = query["account_number"], account_name = name, bank_id = 9 } })
                : Answer(HttpStatusCode.UnprocessableEntity, new { status = false, message = "Could not resolve account name. Check parameters or try again." });

        if (request.Method == HttpMethod.Post && path == "/transferrecipient")
        {
            Recipients.Enqueue(body);
            return Answer(HttpStatusCode.Created, new { status = true, message = "Transfer recipient created successfully", data = new { recipient_code = $"RCP_fake{Interlocked.Increment(ref counter)}" } });
        }

        if (request.Method == HttpMethod.Post && path == "/subaccount")
        {
            Subaccounts.Enqueue(body);
            return Answer(HttpStatusCode.Created, new { status = true, message = "Subaccount created", data = new { subaccount_code = $"ACCT_fake{Interlocked.Increment(ref counter)}" } });
        }

        if (request.Method == HttpMethod.Post && path == "/transfer")
        {
            var reference = body.GetProperty("reference").GetString()!;
            TransferCalls.Enqueue(reference);
            if (UnavailableNextTransfer)
            {
                UnavailableNextTransfer = false;
                return Answer(HttpStatusCode.ServiceUnavailable, new { status = false, message = "Service unavailable" });
            }
            if (RefuseNextTransfer is { } refusal)
            {
                RefuseNextTransfer = null;
                return Answer(HttpStatusCode.BadRequest, new { status = false, message = refusal });
            }
            var transfer = Transfers.GetOrAdd(reference, r => new Transfer(r, body.GetProperty("amount").GetInt64(), body.GetProperty("recipient").GetString()!,
                body.TryGetProperty("reason", out var why) ? why.GetString() ?? "" : "", $"TRF_fake{Interlocked.Increment(ref counter)}") { Status = NewTransferStatus });
            return Answer(HttpStatusCode.OK, new
            {
                status = true, message = "Transfer has been queued",
                data = new { reference, amount = transfer.Amount, currency = "NGN", status = transfer.Status, transfer_code = transfer.TransferCode },
            });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/transfer/verify/", StringComparison.Ordinal))
        {
            var reference = Uri.UnescapeDataString(path["/transfer/verify/".Length..]);
            return Transfers.TryGetValue(reference, out var t)
                ? Answer(HttpStatusCode.OK, new { status = true, message = "Transfer retrieved", data = new { reference, amount = t.Amount, status = t.Status, transfer_code = t.TransferCode } })
                : Answer(HttpStatusCode.NotFound, new { status = false, message = "Transfer not found" });
        }

        return Answer(HttpStatusCode.NotFound, new { status = false, message = $"No fake for {request.Method} {path}" });
    }

    private static HttpResponseMessage Answer(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
