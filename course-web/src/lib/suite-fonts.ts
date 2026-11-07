import { Bricolage_Grotesque, Onest } from "next/font/google";

/** Bricolage Grotesque: the LinkSuite frame's headings, narrowed on the width axis like a painted shop sign. */
const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });

/** Onest: the frame's text face. latin-ext carries the naira sign. */
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

/**
 * The two suite faces as CSS variables. Only public pages load them: the (site) layout, and SitePage for the signed-in
 * pages that share the public header and footer (My learning, checkout).
 */
export const suiteFontVars = `${display.variable} ${text.variable}`;
