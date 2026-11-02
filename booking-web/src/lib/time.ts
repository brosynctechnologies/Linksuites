// Time helpers. Every instant is shown in the BUSINESS time zone (an IANA id), never the viewer's.
// Plain dates ("YYYY-MM-DD") are calendar days with no zone: they are handled with UTC arithmetic
// so they never shift. Clock times are "HH:mm".

const pad = (n: number) => String(n).padStart(2, "0");

const cache = new Map<string, Intl.DateTimeFormat>();
function fmt(timeZone: string, opts: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = timeZone + JSON.stringify(opts);
  let f = cache.get(key);
  if (!f) {
    try {
      f = new Intl.DateTimeFormat("en-GB", { timeZone, ...opts });
    } catch {
      f = new Intl.DateTimeFormat("en-GB", { timeZone: "UTC", ...opts });
    }
    cache.set(key, f);
  }
  return f;
}

function toDate(v: string | Date): Date {
  return typeof v === "string" ? new Date(v) : v;
}

export interface ZonedParts {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  /** 0 = Sunday … 6 = Saturday */
  weekday: number;
}

const WEEKDAY_INDEX: Record<string, number> = { Sun: 0, Mon: 1, Tue: 2, Wed: 3, Thu: 4, Fri: 5, Sat: 6 };

/** Wall-clock parts of an instant in a time zone. */
export function zonedParts(instant: string | Date, tz: string): ZonedParts {
  const parts = fmt(tz, {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    weekday: "short",
    hourCycle: "h23",
  }).formatToParts(toDate(instant));
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? "";
  return {
    year: Number(get("year")),
    month: Number(get("month")),
    day: Number(get("day")),
    hour: Number(get("hour")) % 24,
    minute: Number(get("minute")),
    weekday: WEEKDAY_INDEX[get("weekday")] ?? 0,
  };
}

/** "10:30" in the business zone. */
export function formatTime(instant: string | Date, tz: string): string {
  const p = zonedParts(instant, tz);
  return `${pad(p.hour)}:${pad(p.minute)}`;
}

/** "10:30–11:15" */
export function formatTimeRange(start: string, end: string, tz: string): string {
  return `${formatTime(start, tz)}–${formatTime(end, tz)}`;
}

/** "Sat 14 Oct" for an instant, in the business zone. */
export function formatDate(instant: string | Date, tz: string): string {
  return formatPlainDate(dateKey(instant, tz));
}

/** "Saturday 14 October 2026" for an instant, in the business zone. */
export function formatDateLong(instant: string | Date, tz: string): string {
  return formatPlainDate(dateKey(instant, tz), "long");
}

/** "Sat 14 Oct, 10:30" */
export function formatDateTime(instant: string | Date, tz: string): string {
  return `${formatDate(instant, tz)}, ${formatTime(instant, tz)}`;
}

/** "Saturday" */
export function formatWeekday(instant: string | Date, tz: string): string {
  return WEEKDAYS[zonedParts(instant, tz).weekday];
}

/** The calendar date ("YYYY-MM-DD") an instant falls on in the business zone. */
export function dateKey(instant: string | Date, tz: string): string {
  const p = zonedParts(instant, tz);
  return `${p.year}-${pad(p.month)}-${pad(p.day)}`;
}

/** Today's date in the business zone. */
export function todayIn(tz: string): string {
  return dateKey(new Date(), tz);
}

/** Minutes since midnight in the business zone. */
export function minutesOfDay(instant: string | Date, tz: string): number {
  const p = zonedParts(instant, tz);
  return p.hour * 60 + p.minute;
}

/** "Lagos" from "Africa/Lagos"; "UTC" stays "UTC". */
export function cityOf(tz: string): string {
  const last = tz.split("/").pop() ?? tz;
  return last.replace(/_/g, " ");
}

/** Offset of `tz` from UTC at `instant`, in minutes (Lagos = +60). */
export function offsetMinutes(instant: Date, tz: string): number {
  const p = zonedParts(instant, tz);
  const asUtc = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute);
  const truncated = Math.floor(instant.getTime() / 60000) * 60000;
  return Math.round((asUtc - truncated) / 60000);
}

function offsetString(mins: number): string {
  const sign = mins >= 0 ? "+" : "-";
  const a = Math.abs(mins);
  return `${sign}${pad(Math.floor(a / 60))}:${pad(a % 60)}`;
}

/**
 * The instant for a wall-clock date and time in a zone, as ISO 8601 with that zone's offset,
 * e.g. ("2026-10-14", "10:30", "Africa/Lagos") → "2026-10-14T10:30:00+01:00".
 */
export function zonedInstant(date: string, time: string, tz: string): string {
  const [y, m, d] = date.split("-").map(Number);
  const [hh, mm] = time.split(":").map(Number);
  const wall = Date.UTC(y, m - 1, d, hh, mm);
  let off = offsetMinutes(new Date(wall), tz);
  // Second pass settles DST edges where the first guess crosses a transition.
  off = offsetMinutes(new Date(wall - off * 60000), tz);
  return `${date}T${pad(hh)}:${pad(mm)}:00${offsetString(off)}`;
}

/** Morning before 12:00, Afternoon before 17:00, Evening after. */
export type DayPart = "Morning" | "Afternoon" | "Evening";
export function dayPart(instant: string, tz: string): DayPart {
  const h = zonedParts(instant, tz).hour;
  return h < 12 ? "Morning" : h < 17 ? "Afternoon" : "Evening";
}

/** Group slots (anything with `startsAt`) into Morning / Afternoon / Evening, skipping empty groups. */
export function groupByDayPart<T extends { startsAt: string }>(items: T[], tz: string): { part: DayPart; items: T[] }[] {
  const groups: Record<DayPart, T[]> = { Morning: [], Afternoon: [], Evening: [] };
  for (const it of items) groups[dayPart(it.startsAt, tz)].push(it);
  return (["Morning", "Afternoon", "Evening"] as DayPart[]).filter((p) => groups[p].length).map((part) => ({ part, items: groups[part] }));
}

/** "45 min", "1 h", "1 h 30 min" */
export function formatDuration(mins: number): string {
  if (mins < 60) return `${mins} min`;
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return m ? `${h} h ${m} min` : `${h} h`;
}

/** "HH:mm" → minutes since midnight. */
export function clockToMinutes(t: string): number {
  const [h, m] = t.split(":").map(Number);
  return (h || 0) * 60 + (m || 0);
}

/** minutes since midnight → "HH:mm" */
export function minutesToClock(mins: number): string {
  const m = Math.max(0, Math.min(24 * 60, Math.round(mins)));
  return `${pad(Math.floor(m / 60))}:${pad(m % 60)}`;
}

/** "mm:ss" countdown text for milliseconds remaining. */
export function formatCountdown(ms: number): string {
  const s = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(s / 60)}:${pad(s % 60)}`;
}

// ---------- Plain dates (no zone) ----------

export const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"] as const;
export const WEEKDAYS_SHORT = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"] as const;
/** Monday-first order for editors and calendars. */
export const WEEK_ORDER = [1, 2, 3, 4, 5, 6, 0] as const;

function parsePlain(date: string): Date {
  const [y, m, d] = date.split("-").map(Number);
  return new Date(Date.UTC(y, (m || 1) - 1, d || 1));
}
function plainKey(d: Date): string {
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`;
}

/** "Sat 14 Oct" (short) or "Saturday 14 October 2026" (long) for a YYYY-MM-DD date. */
export function formatPlainDate(date: string, style: "short" | "long" | "medium" = "short"): string {
  if (!/^\d{4}-\d{2}-\d{2}/.test(date)) return date;
  const d = parsePlain(date);
  const opts: Intl.DateTimeFormatOptions =
    style === "long"
      ? { weekday: "long", day: "numeric", month: "long", year: "numeric" }
      : style === "medium"
        ? { day: "numeric", month: "short", year: "numeric" }
        : { weekday: "short", day: "numeric", month: "short" };
  return fmt("UTC", opts).format(d).replace(/,/g, "");
}

export function addDays(date: string, days: number): string {
  const d = parsePlain(date);
  d.setUTCDate(d.getUTCDate() + days);
  return plainKey(d);
}

/** 0 = Sunday … 6 = Saturday for a plain date. */
export function weekdayOf(date: string): number {
  return parsePlain(date).getUTCDay();
}

/** The Monday on or before `date`. */
export function startOfWeek(date: string): string {
  const wd = weekdayOf(date);
  return addDays(date, wd === 0 ? -6 : 1 - wd);
}

/** "YYYY-MM" for a plain date. */
export function monthOf(date: string): string {
  return date.slice(0, 7);
}

export function addMonths(month: string, n: number): string {
  const [y, m] = month.split("-").map(Number);
  const d = new Date(Date.UTC(y, m - 1 + n, 1));
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}`;
}

/** "October 2026" for "2026-10". */
export function formatMonth(month: string): string {
  return fmt("UTC", { month: "long", year: "numeric" }).format(parsePlain(`${month}-01`));
}

/** Number of days in a "YYYY-MM" month. */
export function daysInMonth(month: string): number {
  const [y, m] = month.split("-").map(Number);
  return new Date(Date.UTC(y, m, 0)).getUTCDate();
}

/** Inclusive day difference b − a. */
export function daysBetween(a: string, b: string): number {
  return Math.round((parsePlain(b).getTime() - parsePlain(a).getTime()) / 86400000);
}

// ---------- Period presets (bookings list, reports) ----------

export type PeriodPreset = "upcoming" | "today" | "this-week" | "this-month" | "last-month" | "last-30" | "this-year" | "custom";

export const PERIOD_LABELS: Record<PeriodPreset, string> = {
  upcoming: "Upcoming",
  today: "Today",
  "this-week": "This week",
  "this-month": "This month",
  "last-month": "Last month",
  "last-30": "Last 30 days",
  "this-year": "This year",
  custom: "Custom",
};

/** Inclusive YYYY-MM-DD range for a preset, relative to `today` (a date in the business zone). */
export function presetRange(preset: PeriodPreset, today: string): { from: string; to: string } {
  const month = monthOf(today);
  switch (preset) {
    case "upcoming":
      return { from: today, to: addDays(today, 365) };
    case "today":
      return { from: today, to: today };
    case "this-week": {
      const from = startOfWeek(today);
      return { from, to: addDays(from, 6) };
    }
    case "last-month": {
      const lm = addMonths(month, -1);
      return { from: `${lm}-01`, to: `${lm}-${pad(daysInMonth(lm))}` };
    }
    case "last-30":
      return { from: addDays(today, -29), to: today };
    case "this-year":
      return { from: `${today.slice(0, 4)}-01-01`, to: `${today.slice(0, 4)}-12-31` };
    default:
      return { from: `${month}-01`, to: `${month}-${pad(daysInMonth(month))}` };
  }
}

/** "12 Oct 2026, 14:05" for an audit timestamp, shown in the business zone. */
export function formatStamp(instant: string | null | undefined, tz: string): string {
  if (!instant) return "—";
  return `${formatPlainDate(dateKey(instant, tz), "medium")}, ${formatTime(instant, tz)}`;
}

/** "12 Oct 2026" for a date or the date part of an ISO timestamp (team and billing lists). */
export function formatDay(iso: string | null | undefined): string {
  if (!iso) return "—";
  return formatPlainDate(iso.slice(0, 10), "medium");
}
