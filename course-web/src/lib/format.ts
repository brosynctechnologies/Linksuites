// Formatting helpers shared by server and client components.

const pad = (n: number) => String(n).padStart(2, "0");

const dayFmt = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "Africa/Lagos" });
const stampFmt = new Intl.DateTimeFormat("en-GB", {
  day: "numeric",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
  timeZone: "Africa/Lagos",
});

/** "12 Oct 2026" for a date or an ISO timestamp. */
export function formatDay(iso: string | null | undefined): string {
  if (!iso) return "—";
  if (/^\d{4}-\d{2}-\d{2}$/.test(iso)) {
    const [y, m, d] = iso.split("-").map(Number);
    return dayFmt.format(new Date(Date.UTC(y, m - 1, d, 12)));
  }
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : dayFmt.format(d);
}

/** "12 Oct 2026, 14:05" (Lagos time). */
export function formatStamp(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : stampFmt.format(d);
}

/** "4 min" / "1 h 20 min" for a length in seconds. */
export function formatLength(sec: number | null | undefined): string {
  if (!sec || sec <= 0) return "";
  const mins = Math.max(1, Math.round(sec / 60));
  if (mins < 60) return `${mins} min`;
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return m ? `${h} h ${m} min` : `${h} h`;
}

/** "3:07" or "1:02:45" for a media position. */
export function formatClock(sec: number): string {
  const s = Math.max(0, Math.floor(sec));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h ? `${h}:${pad(m)}:${pad(s % 60)}` : `${m}:${pad(s % 60)}`;
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
  if (b < 1024 ** 3) return `${(b / 1024 / 1024).toFixed(1)} MB`;
  return `${(b / 1024 ** 3).toFixed(2)} GB`;
}

/** "1,204" */
export function formatCount(n: number | null | undefined): string {
  return new Intl.NumberFormat("en").format(n ?? 0);
}

/** "4.7" or "" when there are no ratings. */
export function formatRating(avg: number | null | undefined, count?: number): string {
  if (!avg || (count !== undefined && count <= 0)) return "";
  return avg.toFixed(1);
}

/** "1 lesson" / "12 lessons" */
export function plural(n: number, one: string, many = `${one}s`): string {
  return `${formatCount(n)} ${n === 1 ? one : many}`;
}

/** "3 days ago", "just now" (for Q&A and reviews). */
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

const LEVEL_LABELS: Record<string, string> = {
  Beginner: "Beginner",
  Intermediate: "Intermediate",
  Advanced: "Advanced",
  AllLevels: "All levels",
};
export function levelLabel(level: string | null | undefined): string {
  return level ? (LEVEL_LABELS[level] ?? level) : "";
}

/** The public course page path. */
export function coursePath(academySlug: string, courseSlug: string, viaAcademy = false): string {
  return `/courses/${encodeURIComponent(academySlug)}/${encodeURIComponent(courseSlug)}${viaAcademy ? "?via=academy" : ""}`;
}
