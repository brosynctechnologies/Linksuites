import { Bricolage_Grotesque, Onest } from "next/font/google";

/**
 * The LinkSuite faces for public pages only (the signed-in app keeps Red Hat). Loaded once here and shared by the (site) layout and the
 * 404 page. Bricolage Grotesque: headings and signboards, narrowed like a painted shop sign. Onest: everything else; latin-ext carries ₦.
 */
const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

/** The wrapper of every public page: the font variables, Chalk ground, Onest and Ink. */
export const siteFrame = `${display.variable} ${text.variable} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`;
