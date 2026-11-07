import { Bricolage_Grotesque, Onest } from "next/font/google";

/**
 * The two LinkSuite faces (docs/design/linksuite.md). Only the public frame loads them: the (site) pages, and the
 * header and footer around the marketplace pages. The listings themselves and the studio keep Archivo and Instrument Sans.
 */
const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

/** Class names that define the suite font variables on an element. */
export const suiteFonts = `${display.variable} ${text.variable}`;
