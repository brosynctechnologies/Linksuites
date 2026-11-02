import type { Metadata, Viewport } from "next";
import { Bricolage_Grotesque, Onest } from "next/font/google";
import "./globals.css";

/** Bricolage Grotesque: the wordmark, headline and product names. */
const bricolage = Bricolage_Grotesque({
  subsets: ["latin"],
  weight: ["500", "700", "800"],
  variable: "--font-bricolage",
  display: "swap",
});

/** Onest: everything else. */
const onest = Onest({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-onest",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(process.env.NEXT_PUBLIC_SITE_URL || "http://localhost:3600"),
  title: "LinkSuite: tools for running a business in Nigeria",
  description:
    "OrderLink, InvoiceLink, BookLink, LearnLink, PropertyLink, SchoolLink and CareLink. Pick the tool for your business and start free.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#f3f4f6",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en-NG" className={`${bricolage.variable} ${onest.variable}`}>
      <body className="min-h-dvh">{children}</body>
    </html>
  );
}
