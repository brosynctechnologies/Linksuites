import type { Metadata, Viewport } from "next";
import { Archivo } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
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
  title: {
    default: `${APP_NAME} — Online booking for salons, barbers and studios`,
    template: `%s · ${APP_NAME}`,
  },
  description:
    "Let customers book and pay a deposit from your Instagram bio. See who is in which chair today, and stop no-shows with deposits and reminders.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: "#2B1838",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${archivo.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-mist font-sans text-aubergine">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
