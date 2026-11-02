import { DEFAULT_BRAND, STAFF_COLORS } from "./config";

/** A valid "#rrggbb" or the fallback. Accepts "#rgb" too. */
export function normalizeHex(color: string | null | undefined, fallback = DEFAULT_BRAND): string {
  if (!color) return fallback;
  const c = color.trim();
  if (/^#[0-9a-f]{6}$/i.test(c)) return c.toUpperCase();
  if (/^#[0-9a-f]{3}$/i.test(c)) return `#${c[1]}${c[1]}${c[2]}${c[2]}${c[3]}${c[3]}`.toUpperCase();
  return fallback;
}

function rgb(hex: string): [number, number, number] {
  const h = normalizeHex(hex).slice(1);
  return [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)];
}

function luminance(hex: string): number {
  const [r, g, b] = rgb(hex).map((v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: string, b: string): number {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** White or aubergine, whichever reads better on `bg`. */
export function readableOn(bg: string): string {
  return contrast(bg, "#FFFFFF") >= contrast(bg, "#2B1838") ? "#FFFFFF" : "#2B1838";
}

/** `hex` at an alpha, as rgba(). */
export function withAlpha(hex: string, alpha: number): string {
  const [r, g, b] = rgb(hex);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/** The colour a staff member's bookings use; falls back to a stable pick from the staff palette. */
export function staffColor(color: string | null | undefined, id?: string): string {
  if (color && /^#[0-9a-f]{3,6}$/i.test(color.trim())) return normalizeHex(color);
  let n = 0;
  for (const ch of id ?? "") n = (n * 31 + ch.charCodeAt(0)) >>> 0;
  return STAFF_COLORS[n % STAFF_COLORS.length];
}

/** CSS custom properties for a business brand colour (used on the public pages). */
export function brandVars(brand: string | null | undefined): Record<string, string> {
  const c = normalizeHex(brand);
  return {
    "--brand": c,
    "--on-brand": readableOn(c),
    "--brand-wash": withAlpha(c, 0.1),
    "--brand-line": withAlpha(c, 0.35),
  };
}

/** Initials for an avatar: "Tunde Bakare" → "TB". */
export function initials(name: string | null | undefined): string {
  const parts = (name ?? "").trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return "?";
  return ((parts[0][0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toUpperCase();
}
