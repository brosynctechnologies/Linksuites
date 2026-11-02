// Mirrors the line maths in docs/INVOICES_API.md. The server is the source of truth;
// this powers live previews in the free generator and the editor.

/** Round half away from zero to a whole minor unit (tolerant of float noise like 0.1 * 3). */
export function roundMinor(x: number): number {
  if (!Number.isFinite(x)) return 0;
  const cleaned = Number(x.toFixed(6));
  return Math.sign(cleaned) * Math.round(Math.abs(cleaned));
}

export interface MathLineInput {
  quantity: number;
  unitPriceMinor: number;
  discountPercent: number;
  /** Tax rate in percent (0 when untaxed). */
  ratePercent: number;
  /** Label used to group taxes, e.g. "VAT". Defaults to "Tax". */
  taxName?: string | null;
}

export interface MathLineResult {
  gross: number;
  discount: number;
  lineTotal: number;
  lineTax: number;
}

export interface TaxBreakdown {
  name: string;
  ratePercent: number;
  amountMinor: number;
}

export interface Totals {
  lines: MathLineResult[];
  subtotalMinor: number;
  discountTotalMinor: number;
  taxTotalMinor: number;
  taxes: TaxBreakdown[];
  shippingMinor: number;
  totalMinor: number;
  amountPaidMinor: number;
  balanceMinor: number;
}

export function computeLine(l: MathLineInput): MathLineResult {
  const qty = Number.isFinite(l.quantity) ? l.quantity : 0;
  const price = Number.isFinite(l.unitPriceMinor) ? l.unitPriceMinor : 0;
  const disc = Math.min(100, Math.max(0, Number.isFinite(l.discountPercent) ? l.discountPercent : 0));
  const rate = Number.isFinite(l.ratePercent) ? l.ratePercent : 0;
  const gross = roundMinor(qty * price);
  const discount = roundMinor((gross * disc) / 100);
  const lineTotal = gross - discount;
  const lineTax = roundMinor((lineTotal * rate) / 100);
  return { gross, discount, lineTotal, lineTax };
}

export function computeTotals(lines: MathLineInput[], shippingMinor = 0, amountPaidMinor = 0): Totals {
  const results = lines.map(computeLine);
  const taxMap = new Map<string, TaxBreakdown>();
  lines.forEach((l, i) => {
    const rate = Number.isFinite(l.ratePercent) ? l.ratePercent : 0;
    if (!rate) return;
    const name = l.taxName || "Tax";
    const key = `${name}|${rate}`;
    const t = taxMap.get(key) ?? { name, ratePercent: rate, amountMinor: 0 };
    t.amountMinor += results[i].lineTax;
    taxMap.set(key, t);
  });
  const subtotalMinor = results.reduce((s, r) => s + r.gross, 0);
  const discountTotalMinor = results.reduce((s, r) => s + r.discount, 0);
  const taxTotalMinor = results.reduce((s, r) => s + r.lineTax, 0);
  const shipping = Number.isFinite(shippingMinor) ? shippingMinor : 0;
  const totalMinor = subtotalMinor - discountTotalMinor + taxTotalMinor + shipping;
  return {
    lines: results,
    subtotalMinor,
    discountTotalMinor,
    taxTotalMinor,
    taxes: [...taxMap.values()],
    shippingMinor: shipping,
    totalMinor,
    amountPaidMinor,
    balanceMinor: totalMinor - amountPaidMinor,
  };
}

/** Parse a user-typed number ("2.5", "1,000") — returns 0 when empty/invalid. */
export function parseNumber(s: string | number | null | undefined): number {
  if (typeof s === "number") return Number.isFinite(s) ? s : 0;
  if (!s) return 0;
  const n = Number(String(s).replace(/[,\s]/g, ""));
  return Number.isFinite(n) ? n : 0;
}
