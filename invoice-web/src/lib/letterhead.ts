import { parseColor } from "./templates";
import type { DocumentTitle, FontStyle, HeaderLine, LogoSize } from "./types";

/** Choices for the letterhead settings, in the order the forms show them. Values match the API exactly. */
export const HEADER_LINE_OPTIONS: { value: HeaderLine; label: string }[] = [
  { value: "tagline", label: "Slogan or what you do" },
  { value: "address", label: "Address" },
  { value: "registration", label: "Registration number" },
  { value: "none", label: "Nothing" },
];

export const LOGO_SIZE_OPTIONS: { value: LogoSize; label: string }[] = [
  { value: "small", label: "Small" },
  { value: "medium", label: "Medium" },
  { value: "large", label: "Large" },
];

export const FONT_STYLE_OPTIONS: { value: FontStyle; label: string }[] = [
  { value: "modern", label: "Modern" },
  { value: "serif", label: "Classic serif" },
  { value: "sans", label: "Clean sans" },
];

export const DOCUMENT_TITLES: DocumentTitle[] = ["Invoice", "Tax invoice", "Bill", "Pro forma invoice", "Statement of account"];

/** Character limits, as the API enforces them. */
export const LETTERHEAD_LIMITS = {
  tagline: 100,
  registrationNumber: 40,
  signatureName: 80,
  signatureRole: 80,
  footerText: 160,
} as const;

const LIMIT_LABELS: Record<keyof typeof LETTERHEAD_LIMITS, string> = {
  tagline: "The slogan",
  registrationNumber: "The registration number",
  signatureName: "The signature name",
  signatureRole: "The signature role",
  footerText: "The footer line",
};

export interface LetterheadText {
  tagline: string;
  registrationNumber: string;
  secondaryColor: string;
  signatureName: string;
  signatureRole: string;
  footerText: string;
}

/** Field errors for the free-text letterhead fields, keyed by field name (empty when everything is valid). */
export function letterheadErrors(v: LetterheadText, prefix = ""): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  if (v.secondaryColor.trim() && !parseColor(v.secondaryColor)) {
    errors[`${prefix}secondaryColor`] = ["Enter the second accent as a colour code, for example #1f3a8a, or leave it empty."];
  }
  for (const key of Object.keys(LETTERHEAD_LIMITS) as (keyof typeof LETTERHEAD_LIMITS)[]) {
    const max = LETTERHEAD_LIMITS[key];
    if (v[key].trim().length > max) errors[`${prefix}${key}`] = [`${LIMIT_LABELS[key]} can be up to ${max} characters.`];
  }
  return errors;
}

/** The second accent as the API wants it: lower-case #rrggbb, or null when empty. */
export function secondaryColorValue(s: string): string | null {
  return s.trim() ? parseColor(s) : null;
}
