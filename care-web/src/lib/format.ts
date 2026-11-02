const stampFmt = new Intl.DateTimeFormat("en-GB", {
  day: "numeric",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false,
  timeZone: "Africa/Lagos",
});
const dayFmt = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });

/** "12 Oct 2026, 14:05" in Lagos time. */
export function formatStamp(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : stampFmt.format(d);
}

/** A clinic date ("YYYY-MM-DD") as "12 Oct 2026". */
export function formatDay(date: string | null | undefined): string {
  if (!date) return "—";
  const d = new Date(`${date}T00:00:00Z`);
  return Number.isNaN(d.getTime()) ? date : dayFmt.format(d);
}

/** Today in Lagos as "YYYY-MM-DD". */
export function lagosToday(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Lagos" }).format(new Date());
}

/** Age in whole years on Lagos today, from a "YYYY-MM-DD" date of birth: "34", or "8 months" under a year. */
export function age(dateOfBirth: string): string {
  const [y, m, d] = dateOfBirth.split("-").map(Number);
  const [ty, tm, td] = lagosToday().split("-").map(Number);
  if (!y || !m || !d) return "";
  let years = ty - y;
  if (tm < m || (tm === m && td < d)) years--;
  if (years >= 1) return `${years}`;
  let months = (ty - y) * 12 + (tm - m);
  if (td < d) months--;
  return months <= 0 ? "Under a month" : `${months} month${months === 1 ? "" : "s"}`;
}

/** ₦60,000 from kobo, rounded to whole naira. Only for plan prices, which are always whole. */
export function naira(minor: number): string {
  return `₦${(minor / 100).toLocaleString("en-NG", { maximumFractionDigits: 0 })}`;
}

/** Money from kobo, keeping the kobo when there are any: ₦1,500 or ₦1,500.50. */
export function money(minor: number): string {
  const whole = minor % 100 === 0;
  return `₦${(minor / 100).toLocaleString("en-NG", { minimumFractionDigits: whole ? 0 : 2, maximumFractionDigits: 2 })}`;
}

/** Naira typed by a person ("1,500", "1500.5") to kobo; null when it isn't a non-negative amount with at most two decimals. */
export function toKobo(input: string): number | null {
  const s = input.replace(/[₦,\s]/g, "");
  if (!/^\d+(\.\d{1,2})?$/.test(s)) return null;
  const [whole, frac = ""] = s.split(".");
  return Number(whole) * 100 + Number(frac.padEnd(2, "0"));
}

/** Kobo as the plain naira a person would type back: 150050 → "1500.50", 150000 → "1500". */
export function koboInput(minor: number): string {
  return minor % 100 === 0 ? String(minor / 100) : (minor / 100).toFixed(2);
}

export function plural(n: number, one: string, many = `${one}s`): string {
  return `${n.toLocaleString("en-NG")} ${n === 1 ? one : many}`;
}
