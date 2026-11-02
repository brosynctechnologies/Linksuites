using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Saas.Application.Common;
using Saas.Domain.Core;

namespace Saas.Application.Invoices;

public sealed record RateSnapshot(DateOnly AsOf, IReadOnlyDictionary<string, decimal> PerUsd);

/// <summary>A source of daily rates, all quoted as units per 1 USD.</summary>
public interface IExchangeRateProvider
{
    string Name { get; }
    Task<RateSnapshot> FetchLatestAsync(CancellationToken ct);
}

public sealed class FxUnavailableException(string message) : AppException(message, 503, "fx_unavailable");

public sealed record RateDto(string Currency, decimal ToBase);
public sealed record ExchangeRatesDto(string Base, DateOnly? AsOf, string Source, IReadOnlyList<RateDto> Rates);

/// <summary>
/// Converts between currencies with stored daily rates. The provider is called at most every few hours;
/// each fetch is kept, so a date in the past uses the rate that was current then.
/// </summary>
public sealed class ExchangeRateService(IAppDbContext db, IExchangeRateProvider provider, TimeProvider clock, ILogger<ExchangeRateService> logger)
{
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    /// <summary>Value of 1 unit of <paramref name="from"/> in <paramref name="to"/> on a date (today when null).</summary>
    public async Task<decimal> RateAsync(string from, string to, DateOnly? date, CancellationToken ct)
    {
        from = from.ToUpperInvariant();
        to = to.ToUpperInvariant();
        if (from == to) return 1;
        var snapshot = await SnapshotAsync(date, ct)
            ?? throw new FxUnavailableException("Exchange rates are unavailable right now. Enter the rate yourself.");
        if (!snapshot.PerUsd.TryGetValue(from, out var fromPerUsd) || !snapshot.PerUsd.TryGetValue(to, out var toPerUsd) || fromPerUsd == 0)
            throw new FxUnavailableException($"No exchange rate for {from} to {to}. Enter the rate yourself.");
        return Math.Round(toPerUsd / fromPerUsd, 8);
    }

    public async Task<ExchangeRatesDto> RatesAsync(string baseCurrency, CancellationToken ct)
    {
        var snapshot = await SnapshotAsync(null, ct);
        if (snapshot is null || !snapshot.PerUsd.TryGetValue(baseCurrency, out var basePerUsd))
            return new ExchangeRatesDto(baseCurrency, null, provider.Name, []);
        var rates = Money.SupportedCurrencies.Where(c => c != baseCurrency && snapshot.PerUsd.ContainsKey(c)).Order()
            .Select(c => new RateDto(c, Math.Round(basePerUsd / snapshot.PerUsd[c], 8))).ToList();
        return new ExchangeRatesDto(baseCurrency, snapshot.AsOf, provider.Name, rates);
    }

    /// <summary>Base-currency amount, rounded half away from zero.</summary>
    public static long ToBase(long amountMinor, decimal rate) => InvoiceMath.Round(amountMinor * rate);

    private async Task<RateSnapshot?> SnapshotAsync(DateOnly? date, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (date is null || date >= today) await RefreshIfStaleAsync(ct);

        var target = date ?? today;
        var asOf = await db.ExchangeRates.Where(r => r.AsOf <= target).MaxAsync(r => (DateOnly?)r.AsOf, ct)
            ?? await db.ExchangeRates.MinAsync(r => (DateOnly?)r.AsOf, ct); // before our history starts: oldest we have
        if (asOf is null) return null;
        var rows = await db.ExchangeRates.AsNoTracking().Where(r => r.AsOf == asOf).ToListAsync(ct);
        return new RateSnapshot(asOf.Value, rows.ToDictionary(r => r.Currency, r => r.PerUsd));
    }

    private async Task RefreshIfStaleAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var last = await db.ExchangeRates.MaxAsync(r => (DateTimeOffset?)r.FetchedAt, ct);
        if (last is not null && now - last < MaxAge) return;

        await RefreshGate.WaitAsync(ct);
        try
        {
            last = await db.ExchangeRates.MaxAsync(r => (DateTimeOffset?)r.FetchedAt, ct);
            if (last is not null && now - last < MaxAge) return;

            var fresh = await provider.FetchLatestAsync(ct);
            var existing = await db.ExchangeRates.Where(r => r.AsOf == fresh.AsOf).ToDictionaryAsync(r => r.Currency, ct);
            foreach (var (currency, perUsd) in fresh.PerUsd)
            {
                if (perUsd <= 0) continue;
                if (existing.TryGetValue(currency, out var row))
                {
                    row.PerUsd = perUsd;
                    row.FetchedAt = now;
                }
                else
                {
                    db.ExchangeRates.Add(new ExchangeRate { AsOf = fresh.AsOf, Currency = currency, PerUsd = perUsd, Source = provider.Name, FetchedAt = now });
                }
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DbUpdateException or System.Text.Json.JsonException)
        {
            // Keep working with the rates we already have; another instance may have stored the same day.
            logger.LogWarning(ex, "Could not refresh exchange rates from {Provider}", provider.Name);
        }
        finally
        {
            RefreshGate.Release();
        }
    }
}
