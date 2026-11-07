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
const lagosDayFmt = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "Africa/Lagos" });

/** "12 Oct 2026, 14:05" in Lagos time. */
export function formatStamp(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : stampFmt.format(d);
}

/** A school date ("YYYY-MM-DD") as "12 Oct 2026". */
export function formatDay(date: string | null | undefined): string {
  if (!date) return "—";
  const d = new Date(`${date}T00:00:00Z`);
  return Number.isNaN(d.getTime()) ? date : dayFmt.format(d);
}

/** A timestamp as its day in Lagos, "12 Oct 2026". */
export function formatStampDay(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : lagosDayFmt.format(d);
}

/** Today in Lagos as "YYYY-MM-DD". */
export function lagosToday(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Lagos" }).format(new Date());
}

/**
 * ₦60,000 from kobo, rounded to whole naira. Only for plan prices and amounts already known to be whole: invoice money
 * (balances, totals, payments) goes through `money()` in parent/_shared or app/fees/fees-shared, which keeps the kobo.
 */
export function naira(minor: number): string {
  return `₦${(minor / 100).toLocaleString("en-NG", { maximumFractionDigits: 0 })}`;
}

export function plural(n: number, one: string, many = `${one}s`): string {
  return `${n.toLocaleString("en-NG")} ${n === 1 ? one : many}`;
}

/**
 * Present, late, absent and excused, counted against the days the class was open. Results published before late, absent
 * and excused were counted have null there, and print only present of open rather than false zeros.
 */
export function attendanceLine(days: {
  daysPresent: number;
  daysLate: number | null;
  daysAbsent: number | null;
  daysExcused: number | null;
  daysOpen: number;
}): string {
  if (days.daysLate === null || days.daysAbsent === null || days.daysExcused === null)
    return `Present ${days.daysPresent} of ${days.daysOpen} days`;
  return `Present ${days.daysPresent}, late ${days.daysLate}, absent ${days.daysAbsent}, excused ${days.daysExcused} of ${days.daysOpen} open days`;
}
