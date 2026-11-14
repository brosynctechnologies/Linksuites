import { DEFAULT_ACCENT } from "./config";
import type { TemplateDto } from "./types";

/** Built-in template catalogue (the server's /invoice-templates endpoints are authoritative). */
export const TEMPLATES: TemplateDto[] = [
  { key: "classic", name: "Classic", isPremium: false },
  { key: "modern", name: "Modern", isPremium: false },
  { key: "minimal", name: "Minimal", isPremium: false },
  { key: "bold", name: "Bold", isPremium: false },
  { key: "elegant", name: "Elegant", isPremium: false },
  { key: "executive", name: "Executive", isPremium: true },
  { key: "corporate", name: "Corporate", isPremium: true },
  { key: "ledger", name: "Ledger", isPremium: true },
  { key: "studio", name: "Studio", isPremium: true },
  { key: "prestige", name: "Prestige", isPremium: true },
];

export const FREE_TEMPLATES = TEMPLATES.filter((t) => !t.isPremium);

export function templateName(key: string) {
  return TEMPLATES.find((t) => t.key === key)?.name ?? key;
}

/** Parse #rgb or #rrggbb to lower-case #rrggbb, or null when it isn't a colour. */
export function parseColor(c: string | null | undefined): string | null {
  if (!c) return null;
  const s = c.trim();
  if (/^#[0-9a-f]{6}$/i.test(s)) return s.toLowerCase();
  const m = /^#([0-9a-f])([0-9a-f])([0-9a-f])$/i.exec(s);
  if (m) return `#${m[1]}${m[1]}${m[2]}${m[2]}${m[3]}${m[3]}`.toLowerCase();
  return null;
}

/** Normalise a brand colour to #rrggbb (falls back to the default accent). */
export function normalizeColor(c: string | null | undefined): string {
  return parseColor(c) ?? DEFAULT_ACCENT;
}

/** The colour with an alpha channel, e.g. tint("#2563eb", 0.1). */
export function tint(color: string, alpha: number) {
  const a = Math.round(Math.min(1, Math.max(0, alpha)) * 255)
    .toString(16)
    .padStart(2, "0");
  return `${normalizeColor(color)}${a}`;
}

/** Ink used for text on light accent fills (the PDF uses the same value). */
export const DOC_INK = "#111827";

function channels(color: string): [number, number, number] {
  const c = normalizeColor(color);
  return [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)];
}

function toHex(rgb: number[]) {
  return `#${rgb.map((v) => Math.round(Math.min(255, Math.max(0, v))).toString(16).padStart(2, "0")).join("")}`;
}

/** Each RGB channel multiplied by (1 - amount), rounded: darken("#2b47c9", 0.35). */
export function darken(color: string, amount: number) {
  return toHex(channels(color).map((v) => v * (1 - amount)));
}

/** WCAG relative luminance. */
export function luminance(color: string) {
  const [r, g, b] = channels(color).map((v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG contrast ratio between two colours (1 to 21). */
export function contrastRatio(a: string, b: string) {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** Second accent: the business's secondary colour, or the accent 35% darker when none is set. */
export function secondAccent(accent: string, secondary: string | null | undefined) {
  return parseColor(secondary) ?? darken(accent, 0.35);
}

/** Text colour for a fill: white or ink, whichever contrasts more. */
export function onColor(fill: string) {
  return contrastRatio(fill, "#ffffff") >= contrastRatio(fill, DOC_INK) ? "#ffffff" : DOC_INK;
}

/** The colour as text on white: darkened in 5% steps until it reaches 4.5:1 (ink if it never does). */
export function textOnWhite(color: string) {
  for (let i = 0; i <= 19; i++) {
    const c = darken(color, i * 0.05);
    if (contrastRatio(c, "#ffffff") >= 4.5) return c;
  }
  return DOC_INK;
}
