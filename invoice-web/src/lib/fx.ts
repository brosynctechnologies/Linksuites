// Exchange-rate helpers (docs/INVOICES_PHASE2_API.md). Rates are `toBase`: the value of 1 unit of a
// currency in the business's base currency, e.g. { currency: "USD", toBase: 1580.25 } for an NGN business.

import type { ExchangeRatesDto } from "./types";
import { useApi } from "./use-api";

export const RATES_ATTRIBUTION_URL = "https://www.exchangerate-api.com";

/** Load today's rates. Pass `false` to skip (e.g. while the currency is the base currency). */
export function useExchangeRates(enabled = true) {
  return useApi<ExchangeRatesDto>(enabled ? "/api/exchange-rates" : null);
}

/** Value of 1 `currency` in base, or null when unknown. 1 for the base currency itself. */
export function rateFor(rates: ExchangeRatesDto | null | undefined, currency: string): number | null {
  if (!rates) return null;
  if (currency === rates.base) return 1;
  const hit = rates.rates?.find((r) => r.currency === currency);
  return hit && hit.toBase > 0 ? hit.toBase : null;
}

/** Parse a rate typed by the user ("1,580.25"). Null when empty, invalid or not positive. */
export function parseRate(s: string): number | null {
  const cleaned = s.replace(/[,\s]/g, "");
  if (!cleaned) return null;
  const n = Number(cleaned);
  return Number.isFinite(n) && n > 0 ? n : null;
}

/** A rate as an input string (up to 6 decimals, no trailing zeros). */
export function rateInput(rate: number | null | undefined): string {
  if (!rate || !Number.isFinite(rate)) return "";
  return String(Number(rate.toFixed(6)));
}

/** Convert minor units of a foreign currency to base minor units. */
export function toBaseMinor(minor: number, rate: number): number {
  const v = minor * rate;
  return v < 0 ? -Math.round(-v) : Math.round(v);
}

const rateFormatters = new Map<string, Intl.NumberFormat>();

/** "₦1,580.25" — a rate shown in the base currency with up to 4 decimals. */
export function formatRateValue(rate: number, base: string): string {
  let f = rateFormatters.get(base);
  if (!f) {
    try {
      f = new Intl.NumberFormat("en", { style: "currency", currency: base, currencyDisplay: "narrowSymbol", minimumFractionDigits: 2, maximumFractionDigits: 4 });
    } catch {
      f = new Intl.NumberFormat("en", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
    }
    rateFormatters.set(base, f);
  }
  return f.format(rate);
}

/** "1 USD = ₦1,580.25" */
export function formatRate(currency: string, rate: number, base: string): string {
  return `1 ${currency} = ${formatRateValue(rate, base)}`;
}
