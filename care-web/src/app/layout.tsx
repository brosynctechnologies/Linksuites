import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import { Red_Hat_Display, Red_Hat_Text } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
import "./globals.css";

/** Red Hat Display: titles, the clinic name, the wordmark and big figures (hospital wayfinding signs). */
const display = Red_Hat_Display({
  subsets: ["latin", "latin-ext"],
  weight: ["600", "700"],
  variable: "--font-redhat-display",
  display: "swap",
});

/** Red Hat Text: all UI and body text; clear figures for patient numbers, phones and naira amounts. */
const text = Red_Hat_Text({
  subsets: ["latin", "latin-ext"],
  weight: "variable",
  variable: "--font-redhat-text",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: `${APP_NAME}: patients, wards and billing for your clinic`,
    template: `%s | ${APP_NAME}`,
  },
  description:
    "Clinic software for private clinics and small hospitals in Nigeria: register patients, run the front desk and the wards, bill and take payments.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#0A3A36",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en-NG" className={`${display.variable} ${text.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-gauze font-sans text-ink">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
