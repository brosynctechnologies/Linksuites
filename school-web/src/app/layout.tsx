import type { Metadata, Viewport } from "next";
import { Atkinson_Hyperlegible_Next, Zilla_Slab } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, DEFAULT_DESCRIPTION, DEFAULT_TITLE, SITE_URL, baseOpenGraph, baseTwitter } from "@/lib/site";
import "./globals.css";

/** Zilla Slab: titles, the school name, grades and figures (a textbook-cover slab). */
const zilla = Zilla_Slab({
  subsets: ["latin", "latin-ext"],
  weight: ["600", "700"],
  variable: "--font-zilla",
  display: "swap",
});

/** Atkinson Hyperlegible Next: all UI and body text; clear 1/l/I and 0/O for admission numbers and codes. */
const atkinson = Atkinson_Hyperlegible_Next({
  subsets: ["latin", "latin-ext"],
  weight: "variable",
  variable: "--font-atkinson",
  // Next has no metrics for this family yet, so it can't build an adjusted fallback; the CSS stack falls back to the system UI font.
  adjustFontFallback: false,
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
  themeColor: "#40111F",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en-NG" className={`${zilla.variable} ${atkinson.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-ruled font-sans text-ink">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
