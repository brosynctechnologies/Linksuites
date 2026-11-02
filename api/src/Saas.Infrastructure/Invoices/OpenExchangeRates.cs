using System.Net.Http.Json;
using System.Text.Json;
using Saas.Application.Invoices;

namespace Saas.Infrastructure.Invoices;

/// <summary>
/// ExchangeRate-API's open endpoint (no key, updated daily, attribution required in the UI):
/// GET https://open.er-api.com/v6/latest/USD.
/// </summary>
public sealed class OpenErApiRates(HttpClient http) : IExchangeRateProvider
{
    public string Name => "ExchangeRate-API";

    public async Task<RateSnapshot> FetchLatestAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("https://open.er-api.com/v6/latest/USD", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (body.GetProperty("result").GetString() != "success")
            throw new HttpRequestException("Exchange rate provider returned an error.");

        var updated = DateTimeOffset.FromUnixTimeSeconds(body.GetProperty("time_last_update_unix").GetInt64());
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var rate in body.GetProperty("rates").EnumerateObject())
            if (rate.Value.TryGetDecimal(out var value)) rates[rate.Name.ToUpperInvariant()] = value;
        return new RateSnapshot(DateOnly.FromDateTime(updated.UtcDateTime), rates);
    }
}
