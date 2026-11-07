import type { Metadata, Viewport } from "next";
import { Fraunces, Hanken_Grotesk } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
import { DEFAULT_DESCRIPTION, DEFAULT_TITLE, baseOpenGraph, baseTwitter } from "@/lib/site";
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
  title: { default: DEFAULT_TITLE, template: `%s | ${APP_NAME}` },
  description: DEFAULT_DESCRIPTION,
  applicationName: APP_NAME,
  alternates: { canonical: "/" },
  robots: { index: true, follow: true },
  openGraph: { ...baseOpenGraph, url: "/", title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
  twitter: { ...baseTwitter, title: DEFAULT_TITLE, description: DEFAULT_DESCRIPTION },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#1e2430",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en-NG" className={`${fraunces.variable} ${hanken.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-desk font-sans text-graphite">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
