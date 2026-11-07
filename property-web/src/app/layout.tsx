import type { Metadata, Viewport } from "next";
import { Archivo, Instrument_Sans } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
import { DEFAULT_DESCRIPTION, DEFAULT_TITLE, baseOpenGraph, baseTwitter } from "@/lib/site";
import "./globals.css";

/** Archivo with the width axis: headlines at wdth 112 (estate signboards), prices at wdth 100. */
const archivo = Archivo({
  subsets: ["latin", "latin-ext"],
  axes: ["wdth"],
  variable: "--font-archivo",
  display: "swap",
});

/** Instrument Sans: all UI and body text. */
const instrument = Instrument_Sans({
  subsets: ["latin", "latin-ext"],
  variable: "--font-instrument",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: { default: DEFAULT_TITLE, template: `%s | ${APP_NAME}` },
  description: DEFAULT_DESCRIPTION,
  applicationName: APP_NAME,
  robots: { index: true, follow: true },
  // No root canonical: it would be inherited by every listing and search page and point them all at the home page.
  // Each indexable page sets its own `alternates.canonical`.
  openGraph: { ...baseOpenGraph, url: "/", title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
  twitter: { ...baseTwitter, title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#f1f2ee",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en-NG" className={`${archivo.variable} ${instrument.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-plaster font-sans text-asphalt">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
