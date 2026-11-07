import type { Metadata, Viewport } from "next";
import { Figtree, Literata } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
import { SITE_DESCRIPTION, SITE_TITLE, baseOpenGraph, baseTwitter } from "@/lib/site";
import "./globals.css";

/** Figtree: all UI, numbers and the studio. */
const figtree = Figtree({
  subsets: ["latin", "latin-ext"],
  variable: "--font-figtree",
  display: "swap",
});

/** Literata with optical sizes: course titles, lesson titles and the body of text lessons. */
const literata = Literata({
  subsets: ["latin", "latin-ext"],
  axes: ["opsz"],
  style: ["normal", "italic"],
  variable: "--font-literata",
  display: "swap",
});

/*
 * No canonical, Open Graph url or Twitter title here: every page below would inherit them, and course pages would then
 * point at the home page. Each public page sets its own canonical and Open Graph (see pageOpenGraph in lib/site.ts);
 * Twitter cards take their title and description from Open Graph.
 */
export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: { default: SITE_TITLE, template: `%s | ${APP_NAME}` },
  description: SITE_DESCRIPTION,
  applicationName: APP_NAME,
  robots: { index: true, follow: true },
  openGraph: { ...baseOpenGraph, title: SITE_TITLE, description: SITE_DESCRIPTION },
  twitter: { ...baseTwitter },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#10202B",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en-NG" className={`${figtree.variable} ${literata.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-mist font-sans text-ink">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
