import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import { Bricolage_Grotesque, Onest } from "next/font/google";
import { SITE_DESCRIPTION, SITE_NAME, SITE_TITLE, SITE_URL, baseOpenGraph, baseTwitter } from "@/lib/site";
import "./globals.css";

/** Bricolage Grotesque: headings and signboards, narrowed on the width axis like a painted shop sign. */
const display = Bricolage_Grotesque({
  subsets: ["latin"],
  axes: ["opsz", "wdth"],
  variable: "--font-ls-display-face",
  display: "swap",
});

/** Onest: everything else. Plain, open shapes and clear figures at small sizes; latin-ext carries the naira sign. */
const text = Onest({
  subsets: ["latin", "latin-ext"],
  weight: ["400", "500", "600"],
  variable: "--font-ls-text-face",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: { default: SITE_TITLE, template: `%s | ${SITE_NAME}` },
  description: SITE_DESCRIPTION,
  applicationName: SITE_NAME,
  alternates: { canonical: "/" },
  robots: { index: true, follow: true },
  openGraph: { ...baseOpenGraph, url: "/", title: SITE_TITLE, description: SITE_DESCRIPTION },
  twitter: { ...baseTwitter, title: SITE_TITLE, description: SITE_DESCRIPTION },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#f1f2ee",
};

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="en-NG" className={`${display.variable} ${text.variable}`}>
      <body className="flex min-h-dvh flex-col">{children}</body>
    </html>
  );
}
