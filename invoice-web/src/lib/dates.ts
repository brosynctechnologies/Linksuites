// Date helpers. Plain dates are "YYYY-MM-DD" strings and are always treated as LOCAL
// calendar dates (never parsed with `new Date("YYYY-MM-DD")`, which would be UTC).

const pad = (n: number) => String(n).padStart(2, "0");

/** Format a local Date as YYYY-MM-DD. */
export function toISODate(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Today's local date as YYYY-MM-DD. */
export function todayISO(): string {
  return toISODate(new Date());
}

/** Parse YYYY-MM-DD (or the date part of an ISO timestamp) as a local date. */
export function parseISODate(s: string | null | undefined): Date | null {
  if (!s) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(s);
  if (!m) return null;
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return Number.isNaN(d.getTime()) ? null : d;
}

export function addDays(iso: string, days: number): string {
  const d = parseISODate(iso) ?? new Date();
  d.setDate(d.getDate() + days);
  return toISODate(d);
}

/** "12 Oct 2026" for a YYYY-MM-DD date. */
export function formatDay(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = parseISODate(iso);
  if (!d) return iso;
  return d.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

/** "12 Oct 2026, 14:05" for a full ISO timestamp (shown in the viewer's timezone). */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString("en-GB", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

// ---- Period presets (expenses, reports) ----

export type PeriodPreset = "this-month" | "last-month" | "this-quarter" | "this-year" | "last-year" | "custom";

export const PERIOD_LABELS: Record<PeriodPreset, string> = {
  "this-month": "This month",
  "last-month": "Last month",
  "this-quarter": "This quarter",
  "this-year": "This year",
  "last-year": "Last year",
  custom: "Custom",
};

/** Inclusive YYYY-MM-DD range for a preset (local dates). `custom` returns this month as a starting point. */
export function presetRange(preset: PeriodPreset, now = new Date()): { from: string; to: string } {
  const y = now.getFullYear();
  const m = now.getMonth();
  const range = (a: Date, b: Date) => ({ from: toISODate(a), to: toISODate(b) });
  switch (preset) {
    case "last-month":
      return range(new Date(y, m - 1, 1), new Date(y, m, 0));
    case "this-quarter": {
      const q = Math.floor(m / 3) * 3;
      return range(new Date(y, q, 1), new Date(y, q + 3, 0));
    }
    case "this-year":
      return range(new Date(y, 0, 1), new Date(y, 11, 31));
    case "last-year":
      return range(new Date(y - 1, 0, 1), new Date(y - 1, 11, 31));
    default:
      return range(new Date(y, m, 1), new Date(y, m + 1, 0));
  }
}

/** "Oct 2026" for a "YYYY-MM" month key. */
export function formatMonth(key: string, style: "short" | "long" = "short"): string {
  const m = /^(\d{4})-(\d{2})/.exec(key);
  if (!m) return key;
  const d = new Date(Number(m[1]), Number(m[2]) - 1, 1);
  return d.toLocaleDateString("en-GB", { month: style, year: style === "long" ? "numeric" : "2-digit" });
}
