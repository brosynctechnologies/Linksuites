/** Formatting: money in kobo ("₦3,500,000"), dates in Africa/Lagos ("13 Oct 2026, 14:32"). */

const TZ = "Africa/Lagos";
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const SYMBOLS: Record<string, string> = { NGN: "₦", GHS: "GH₵", KES: "KSh", USD: "$", GBP: "£", EUR: "€", ZAR: "R" };

const whole = new Intl.NumberFormat("en-NG", { maximumFractionDigits: 0 });
const withKobo = new Intl.NumberFormat("en-NG", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const count = new Intl.NumberFormat("en-NG");

/** Minor units → "₦3,500,000" (kobo shown only when not whole naira). */
export function money(minor: number | null | undefined, currency = "NGN"): string {
  if (minor === null || minor === undefined || Number.isNaN(minor)) return "—";
  const major = minor / 100;
  const symbol = SYMBOLS[currency?.toUpperCase()] ?? `${currency} `;
  const sign = major < 0 ? "−" : "";
  const abs = Math.abs(major);
  return `${sign}${symbol}${Number.isInteger(abs) ? whole.format(abs) : withKobo.format(abs)}`;
}

export function num(n: number | null | undefined): string {
  if (n === null || n === undefined) return "—";
  return count.format(n);
}

interface LagosParts {
  year: string;
  month: number;
  day: string;
  hour: string;
  minute: string;
}

const partsFmt = new Intl.DateTimeFormat("en-GB", {
  timeZone: TZ,
  year: "numeric",
  month: "numeric",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

function lagosParts(iso: string): LagosParts | null {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  const p = Object.fromEntries(partsFmt.formatToParts(d).map((x) => [x.type, x.value]));
  return { year: p.year, month: Number(p.month), day: String(Number(p.day)), hour: p.hour, minute: p.minute };
}

/** "13 Oct 2026, 14:32" in Lagos time. */
export function stamp(iso: string | null | undefined): string {
  if (!iso) return "—";
  const p = lagosParts(iso);
  if (!p) return "—";
  return `${p.day} ${MONTHS[p.month - 1]} ${p.year}, ${p.hour}:${p.minute}`;
}

/** "13 Oct 2026" in Lagos time. */
export function day(iso: string | null | undefined): string {
  if (!iso) return "—";
  const p = lagosParts(iso);
  if (!p) return "—";
  return `${p.day} ${MONTHS[p.month - 1]} ${p.year}`;
}

/** "14:32" in Lagos time. */
export function clock(iso: string | null | undefined): string {
  if (!iso) return "—";
  const p = lagosParts(iso);
  return p ? `${p.hour}:${p.minute}` : "—";
}

/** "4 min ago", "3 h ago", "2 days ago"; falls back to the stamp beyond a week. */
export function ago(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return "never";
  const t = new Date(iso).getTime();
  if (Number.isNaN(t)) return "—";
  const s = Math.round((now - t) / 1000);
  if (s < 60) return "just now";
  const m = Math.round(s / 60);
  if (m < 60) return `${m} min ago`;
  const h = Math.round(m / 60);
  if (h < 24) return `${h} h ago`;
  const d = Math.round(h / 24);
  if (d <= 7) return `${d} day${d === 1 ? "" : "s"} ago`;
  return stamp(iso);
}

/** "7 h 12 m" from milliseconds. */
export function timeLeft(ms: number): string {
  if (ms <= 0) return "0 m";
  const totalMin = Math.floor(ms / 60_000);
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return h > 0 ? `${h} h ${m} m` : `${m} m`;
}

/** Today's date in Lagos as YYYY-MM-DD (for date inputs). */
export function todayLagos(): string {
  const p = lagosParts(new Date().toISOString());
  if (!p) return "";
  return `${p.year}-${String(p.month).padStart(2, "0")}-${p.day.padStart(2, "0")}`;
}

/** A YYYY-MM-DD date at the start or end of the day in Lagos (UTC+1, no DST), as an ISO instant. */
export function lagosDayBound(date: string, edge: "start" | "end"): string {
  return edge === "start" ? `${date}T00:00:00+01:00` : `${date}T23:59:59+01:00`;
}

/** "a…@gmail.com" for the code screen. */
export function maskEmail(email: string): string {
  const [local, domain] = email.split("@");
  if (!domain) return email;
  return `${local.slice(0, 1)}…@${domain}`;
}

export function plural(n: number, one: string, many = `${one}s`): string {
  return `${num(n)} ${n === 1 ? one : many}`;
}

/** First 8 characters of a GUID, for compact mono display. */
export function shortId(id: string | null | undefined): string {
  return id ? id.slice(0, 8) : "—";
}

/** "Lekki Homes'" / "Ada's". */
export function possessive(name: string): string {
  return /s$/i.test(name.trim()) ? `${name}’` : `${name}’s`;
}
