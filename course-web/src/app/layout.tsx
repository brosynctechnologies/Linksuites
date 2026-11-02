import type { Metadata, Viewport } from "next";
import { Figtree, Literata } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
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

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: `${APP_NAME}: courses from tutors you trust`,
    template: `%s | ${APP_NAME}`,
  },
  description:
    "Exam prep, professional certificates and digital skills from African tutors. Pay in naira, learn on your phone, and finish with a certificate.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#10202B",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${figtree.variable} ${literata.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-mist font-sans text-ink">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
