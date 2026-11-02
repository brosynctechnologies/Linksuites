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

/** Format an amount in minor units (kobo/cents). Whole amounts show no decimals, e.g. ₦1,500. */
export function formatMoney(minor: number | null | undefined, currency = "NGN"): string {
  const value = (minor ?? 0) / 100;
  const decimals = Number.isInteger(value) ? 0 : 2;
  return formatter(currency || "NGN", decimals).format(value);
}

/** Parse a major-unit string ("1500", "1,500.50") into minor units. Returns null when empty/invalid. */
export function toMinor(major: string): number | null {
  const cleaned = major.replace(/[,\s]/g, "");
  if (cleaned === "") return null;
  const n = Number(cleaned);
  if (!Number.isFinite(n)) return null;
  return Math.round(n * 100);
}

/** Minor units to a plain major-unit string for form inputs. */
export function toMajorInput(minor: number | null | undefined): string {
  if (minor === null || minor === undefined) return "";
  const v = minor / 100;
  return Number.isInteger(v) ? String(v) : v.toFixed(2);
}
