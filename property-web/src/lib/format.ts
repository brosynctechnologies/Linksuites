// Formatting helpers shared by server and client components. Times are shown in the business time zone (Africa/Lagos).

const TZ = "Africa/Lagos";
const pad = (n: number) => String(n).padStart(2, "0");

const dayFmt = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: TZ });
const shortDayFmt = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone: TZ });
const stampFmt = new Intl.DateTimeFormat("en-GB", {
  day: "numeric",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
  timeZone: TZ,
});
const timeFmt = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: TZ });
const isoDayFmt = new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: TZ });

function toDate(iso: string): Date {
  if (/^\d{4}-\d{2}-\d{2}$/.test(iso)) {
    const [y, m, d] = iso.split("-").map(Number);
    return new Date(Date.UTC(y, m - 1, d, 12));
  }
  return new Date(iso);
}

/** "12 Oct 2026" for a date or an ISO timestamp. */
export function formatDay(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = toDate(iso);
  return Number.isNaN(d.getTime()) ? "—" : dayFmt.format(d);
}

/** "Mon, 12 Oct" for a date or an ISO timestamp. */
export function formatShortDay(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = toDate(iso);
  return Number.isNaN(d.getTime()) ? "—" : shortDayFmt.format(d);
}

/** "12 Oct 2026, 14:05" (Lagos time). */
export function formatStamp(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : stampFmt.format(d);
}

/** "14:05" (Lagos time). */
export function formatTime(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "" : timeFmt.format(d);
}

/** The Lagos calendar day of an instant, as "YYYY-MM-DD". */
export function lagosDay(iso: string | Date): string {
  const d = typeof iso === "string" ? new Date(iso) : iso;
  return isoDayFmt.format(d);
}

/** Today in Lagos, "YYYY-MM-DD". */
export function lagosToday(): string {
  return lagosDay(new Date());
}

/** Add days to a "YYYY-MM-DD" date. */
export function addDays(day: string, n: number): string {
  const [y, m, d] = day.split("-").map(Number);
  const t = new Date(Date.UTC(y, m - 1, d + n));
  return `${t.getUTCFullYear()}-${pad(t.getUTCMonth() + 1)}-${pad(t.getUTCDate())}`;
}

/** Whole days from `a` to `b` ("YYYY-MM-DD"). */
export function daysBetween(a: string, b: string): number {
  return Math.round((toDate(b).getTime() - toDate(a).getTime()) / 86_400_000);
}

/** "mm:ss" countdown for milliseconds remaining. */
export function formatCountdown(ms: number): string {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h ? `${h}:${pad(m)}:${pad(s % 60)}` : `${m}:${pad(s % 60)}`;
}

/** "2.4 MB" */
export function formatBytes(bytes: number | null | undefined): string {
  const b = bytes ?? 0;
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${Math.round(b / 1024)} KB`;
  return `${(b / 1024 / 1024).toFixed(1)} MB`;
}

/** "1,204" */
export function formatCount(n: number | null | undefined): string {
  return new Intl.NumberFormat("en").format(n ?? 0);
}

/** "1 bed" / "3 beds" */
export function plural(n: number, one: string, many = `${one}s`): string {
  return `${formatCount(n)} ${n === 1 ? one : many}`;
}

/** "3 days ago", "just now". */
export function timeAgo(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return "";
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return "";
  const s = Math.round((now - t) / 1000);
  if (s < 60) return "just now";
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h} h ago`;
  const d = Math.round(h / 24);
  if (d < 30) return `${d} day${d === 1 ? "" : "s"} ago`;
  return formatDay(iso);
}

/** URL-safe slug from free text. */
export function slugify(text: string): string {
  return text
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 60);
}

/** International digits for wa.me and tel: links. Nigerian local numbers (0803…) become 234803…. */
export function phoneDigits(phone: string | null | undefined): string {
  const d = (phone ?? "").replace(/\D/g, "");
  if (d.length === 11 && d.startsWith("0")) return `234${d.slice(1)}`;
  return d;
}

/** "Lekki, Lagos" style place line, skipping blanks. */
export function placeLine(...parts: (string | null | undefined)[]): string {
  return parts.filter((p) => p && p.trim()).join(", ");
}

/** "AB" initials for avatars. */
export function initials(name: string | null | undefined): string {
  const parts = (name ?? "").trim().split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toUpperCase() || "?";
}

/** The public listing path. */
export function listingPath(agencySlug: string, listingSlug: string): string {
  return `/listings/${encodeURIComponent(agencySlug)}/${encodeURIComponent(listingSlug)}`;
}

/** A same-site absolute URL from the API (listingUrl, publicUrl) becomes a local path. */
export function localPath(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url, "http://local.invalid");
    return u.pathname + u.search;
  } catch {
    return null;
  }
}
