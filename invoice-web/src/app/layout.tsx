import type { Metadata, Viewport } from "next";
import { Fraunces, Hanken_Grotesk } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
import "./globals.css";

/** Display serif for titles, document headings and big amounts (optical size follows font size). */
const fraunces = Fraunces({
  subsets: ["latin", "latin-ext"],
  axes: ["opsz"],
  variable: "--font-fraunces",
  display: "swap",
});

/** UI and body text. */
const hanken = Hanken_Grotesk({
  subsets: ["latin", "latin-ext"],
  variable: "--font-hanken",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: `${APP_NAME} — Free invoice generator`,
    template: `%s · ${APP_NAME}`,
  },
  description:
    "Create a professional invoice in 60 seconds — no sign-up. Send invoices, get paid online, and track who owes you.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#1e2430",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${fraunces.variable} ${hanken.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-desk font-sans text-graphite">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
