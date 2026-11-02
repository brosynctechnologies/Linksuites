import { DEFAULT_ACCENT } from "./config";
import type { TemplateDto } from "./types";

/** Built-in template catalogue (the server's /invoice-templates endpoints are authoritative). */
export const TEMPLATES: TemplateDto[] = [
  { key: "classic", name: "Classic", isPremium: false },
  { key: "modern", name: "Modern", isPremium: false },
  { key: "minimal", name: "Minimal", isPremium: false },
  { key: "bold", name: "Bold", isPremium: true },
  { key: "elegant", name: "Elegant", isPremium: true },
];

export const FREE_TEMPLATES = TEMPLATES.filter((t) => !t.isPremium);

export function templateName(key: string) {
  return TEMPLATES.find((t) => t.key === key)?.name ?? key;
}

/** Normalise a brand colour to #rrggbb (falls back to the default accent). */
export function normalizeColor(c: string | null | undefined): string {
  if (!c) return DEFAULT_ACCENT;
  const s = c.trim();
  if (/^#[0-9a-f]{6}$/i.test(s)) return s.toLowerCase();
  const m = /^#([0-9a-f])([0-9a-f])([0-9a-f])$/i.exec(s);
  if (m) return `#${m[1]}${m[1]}${m[2]}${m[2]}${m[3]}${m[3]}`.toLowerCase();
  return DEFAULT_ACCENT;
}

/** The colour with an alpha channel, e.g. tint("#2563eb", 0.1). */
export function tint(color: string, alpha: number) {
  const a = Math.round(Math.min(1, Math.max(0, alpha)) * 255)
    .toString(16)
    .padStart(2, "0");
  return `${normalizeColor(color)}${a}`;
}
