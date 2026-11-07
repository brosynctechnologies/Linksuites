import { Bricolage_Grotesque, Onest } from "next/font/google";

/**
 * The LinkSuite faces, loaded only by public pages: the (site) route group and the 404 page. Signed-in screens keep
 * SchoolLink's own Zilla Slab and Atkinson Hyperlegible Next.
 */
const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

/** Put on the public page's wrapper: the suite faces, Chalk ground and Ink text. */
export const suiteFrame = `${display.variable} ${text.variable} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`;
