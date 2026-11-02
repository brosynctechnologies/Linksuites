import type { Metadata, Viewport } from "next";
import { Archivo, Instrument_Sans } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
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
  title: {
    default: `${APP_NAME}: homes from checked agents, every cost shown`,
    template: `%s | ${APP_NAME}`,
  },
  description:
    "Rent, buy or book a short-let in Lagos from verified agents. Every listing shows the full move-in cost: rent, agency fee, legal fee and caution, added up.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#17201C",
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
