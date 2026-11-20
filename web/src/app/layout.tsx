import type { Metadata, Viewport } from "next";
import { Suspense } from "react";
import { Bricolage_Grotesque, Onest } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_DESCRIPTION, SITE_TITLE, SITE_URL, baseOpenGraph, baseTwitter } from "@/lib/config";
import { PwaSetup } from "@/components/pwa";
import "./globals.css";

/*
 * The app and its public pages use the same two faces, so they load once here under the LinkSuite names. Bricolage carries
 * the wdth axis for the narrow suite headings; Onest's latin-ext subset carries the naira sign.
 */
const bricolage = Bricolage_Grotesque({
  subsets: ["latin"],
  axes: ["opsz", "wdth"],
  variable: "--font-ls-display-face",
  display: "swap",
});

const onest = Onest({
  subsets: ["latin", "latin-ext"],
  variable: "--font-ls-text-face",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: { default: SITE_TITLE, template: `%s | ${APP_NAME}` },
  description: SITE_DESCRIPTION,
  applicationName: APP_NAME,
  appleWebApp: { capable: true, title: APP_NAME, statusBarStyle: "default" },
  alternates: { canonical: "/" },
  robots: { index: true, follow: true },
  openGraph: { ...baseOpenGraph, url: "/", title: SITE_TITLE, description: SITE_DESCRIPTION },
  twitter: { ...baseTwitter, title: SITE_TITLE, description: SITE_DESCRIPTION },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#1b1f3b",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en-NG" className={`${bricolage.variable} ${onest.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <SupportBanner />
        {children}
        <Suspense fallback={null}>
          <PwaSetup />
        </Suspense>
      </body>
    </html>
  );
}
