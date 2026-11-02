import { DEFAULT_BRAND } from "./config";

/** A valid "#RRGGBB" or the fallback. Accepts "#rgb" too. */
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

/** White or deep ink, whichever reads better on `bg`. */
export function readableOn(bg: string): string {
  return contrast(bg, "#FFFFFF") >= contrast(bg, "#10202B") ? "#FFFFFF" : "#10202B";
}

/** `hex` at an alpha, as rgba(). */
export function withAlpha(hex: string, alpha: number): string {
  const [r, g, b] = rgb(hex);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

/** Darken a colour towards deep ink (for hover states and text on white). */
function darken(hex: string, amount: number): string {
  const [r, g, b] = rgb(hex);
  const mix = (v: number, t: number) => Math.round(v + (t - v) * amount);
  const to = [16, 32, 43];
  return `#${[mix(r, to[0]), mix(g, to[1]), mix(b, to[2])].map((v) => v.toString(16).padStart(2, "0")).join("")}`;
}

/**
 * CSS custom properties that swap Lagoon for an academy's brand colour on its own pages.
 * Every `lagoon` utility reads these variables, so the whole subtree re-themes.
 */
export function brandVars(brand: string | null | undefined): Record<string, string> {
  const c = normalizeHex(brand);
  const text = contrast(c, "#FFFFFF") >= 4.5 ? c : darken(c, 0.45);
  return {
    "--lagoon": c,
    "--lagoon-dark": darken(c, 0.25),
    "--lagoon-text": text,
    "--on-lagoon": readableOn(c),
    "--lagoon-wash": withAlpha(c, 0.09),
    "--lagoon-line": withAlpha(c, 0.35),
    "--focus-ring": c,
  };
}

/** Initials for an avatar: "Tunde Bakare" → "TB". */
export function initials(name: string | null | undefined): string {
  const parts = (name ?? "").trim().split(/\s+/).filter(Boolean);
  if (!parts.length) return "?";
  return ((parts[0][0] ?? "") + (parts.length > 1 ? parts[parts.length - 1][0] : "")).toUpperCase();
}
