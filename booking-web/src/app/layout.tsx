import type { Metadata, Viewport } from "next";
import { Suspense } from "react";
import { Archivo } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_DESCRIPTION, SITE_TITLE, SITE_URL, baseOpenGraph, baseTwitter } from "@/lib/site";
import { PwaSetup } from "@/components/pwa";
import "./globals.css";

/**
 * One family, two widths: Archivo variable with the `wdth` axis.
 * Normal width for UI; `font-expanded` (font-stretch 125%) for times, dates and business names.
 */
const archivo = Archivo({
  subsets: ["latin", "latin-ext"],
  axes: ["wdth"],
  variable: "--font-archivo",
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
  viewportFit: "cover",
  themeColor: "#2B1838",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en-NG" className={`${archivo.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-mist font-sans text-aubergine">
        <SupportBanner />
        {children}
        <Suspense fallback={null}>
          <PwaSetup />
        </Suspense>
      </body>
    </html>
  );
}
