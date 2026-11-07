import type { Viewport } from "next";
import type { ReactNode } from "react";
import { Bricolage_Grotesque, Onest } from "next/font/google";

/** The LinkSuite faces load only on the public pages; the signed-in app keeps Fraunces and Hanken Grotesk. */
const display = Bricolage_Grotesque({ subsets: ["latin"], axes: ["opsz", "wdth"], variable: "--font-ls-display-face", display: "swap" });
const text = Onest({ subsets: ["latin", "latin-ext"], weight: ["400", "500", "600"], variable: "--font-ls-text-face", display: "swap" });

export const viewport: Viewport = { themeColor: "#f1f2ee" };

export default function SiteLayout({ children }: { children: ReactNode }) {
  return (
    <div className={`${display.variable} ${text.variable} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`}>{children}</div>
  );
}
