import type { PricePeriod } from "./types";

const formatters = new Map<string, Intl.NumberFormat>();

function formatter(currency: string, decimals: number) {
  const key = `${currency}:${decimals}`;
  let f = formatters.get(key);
  if (!f) {
    try {
      f = new Intl.NumberFormat("en", {
        style: "currency",
        currency,
        currencyDisplay: "narrowSymbol",
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals,
      });
    } catch {
      f = new Intl.NumberFormat("en", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
    }
    formatters.set(key, f);
  }
  return f;
}

/** Format an amount in kobo. Whole amounts show no decimals: ₦3,500,000. */
export function formatMoney(minor: number | null | undefined, currency = "NGN"): string {
  const value = (minor ?? 0) / 100;
  const decimals = Number.isInteger(value) ? 0 : 2;
  return formatter(currency || "NGN", decimals).format(value);
}

const PERIOD_SUFFIX: Record<PricePeriod, string> = { Year: "/year", Month: "/month", Night: "/night", Total: "" };

/** "/year", "/night" or "" (sale price). */
export function periodSuffix(period: PricePeriod | string | null | undefined): string {
  return (period && PERIOD_SUFFIX[period as PricePeriod]) ?? "";
}

/** "₦3,500,000/year" */
export function formatPrice(minor: number, period: PricePeriod | string | null | undefined, currency = "NGN"): string {
  return `${formatMoney(minor, currency)}${periodSuffix(period)}`;
}

function trim(n: number): string {
  const s = n >= 100 ? Math.round(n).toString() : n.toFixed(1);
  return s.replace(/\.0$/, "");
}

/** Short form for map pins and tight spaces: ₦3.5m, ₦850k, ₦1.2bn. */
export function shortMoney(minor: number): string {
  const v = Math.abs(minor) / 100;
  if (v >= 1e9) return `₦${trim(v / 1e9)}bn`;
  if (v >= 1e6) return `₦${trim(v / 1e6)}m`;
  if (v >= 1e3) return `₦${trim(v / 1e3)}k`;
  return `₦${Math.round(v)}`;
}

/** Parse a naira string ("1500", "1,500.50") into kobo. Returns null when empty/invalid. */
export function toMinor(major: string): number | null {
  const cleaned = major.replace(/[,\s₦]/g, "");
  if (cleaned === "") return null;
  const n = Number(cleaned);
  if (!Number.isFinite(n)) return null;
  return Math.round(Number((n * 100).toFixed(6)));
}

/** Kobo to a plain naira string for form inputs. */
export function toMajorInput(minor: number | null | undefined): string {
  if (minor === null || minor === undefined) return "";
  const v = minor / 100;
  return Number.isInteger(v) ? String(v) : v.toFixed(2);
}

/** "1,500,000" while typing into a naira field (keeps what the user typed when it isn't a number). */
export function groupDigits(input: string): string {
  const cleaned = input.replace(/[,\s]/g, "");
  if (!/^\d+$/.test(cleaned)) return input;
  return Number(cleaned).toLocaleString("en");
}
