import { Inter, Noto_Sans, Noto_Serif } from "next/font/google";

/*
 * Document fonts, used on the invoice sheet only. The PDF embeds the same three families, so each one
 * must contain the naira sign (U+20A6). Hanken Grotesk (the app's UI face) does not, so "modern" is
 * Inter. The ₦ glyph lives in Google's latin-ext subset, which is why every family loads it.
 */

const docModern = Inter({
  subsets: ["latin", "latin-ext"],
  variable: "--font-doc-modern",
  display: "swap",
});

const docSerif = Noto_Serif({
  subsets: ["latin", "latin-ext"],
  style: ["normal", "italic"],
  variable: "--font-doc-serif",
  display: "swap",
  preload: false,
});

const docSans = Noto_Sans({
  subsets: ["latin", "latin-ext"],
  variable: "--font-doc-sans",
  display: "swap",
  preload: false,
});

/** Class names that define the three document font variables; set on the sheet itself. */
export const docFontVariables = `${docModern.variable} ${docSerif.variable} ${docSans.variable}`;

export type FontStyle = "modern" | "serif" | "sans";

export const DOC_FONT_STACK: Record<FontStyle, string> = {
  modern: 'var(--font-doc-modern), ui-sans-serif, system-ui, "Segoe UI", Arial, sans-serif',
  serif: 'var(--font-doc-serif), Georgia, Cambria, "Times New Roman", serif',
  sans: 'var(--font-doc-sans), ui-sans-serif, system-ui, "Segoe UI", Arial, sans-serif',
};
