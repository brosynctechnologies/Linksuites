import type { ReactNode } from "react";
import { Bricolage_Grotesque, Onest } from "next/font/google";

const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

/** The LinkSuite frame for BookLink's public pages: suite faces, chalk ground, ink focus ring. */
export default function SiteLayout({ children }: { children: ReactNode }) {
  return (
    <div
      className={`${display.variable} ${text.variable} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink [--ls-focus:var(--color-ls-ink)]`}
    >
      {children}
    </div>
  );
}
