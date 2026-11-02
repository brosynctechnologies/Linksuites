import type { Metadata, Viewport } from "next";
import { Bricolage_Grotesque, Onest } from "next/font/google";
import { SupportBanner } from "@/components/support-banner";
import { APP_NAME, SITE_URL } from "@/lib/config";
import "./globals.css";

const bricolage = Bricolage_Grotesque({
  subsets: ["latin"],
  variable: "--font-bricolage",
  display: "swap",
});

const onest = Onest({
  subsets: ["latin"],
  variable: "--font-onest",
  display: "swap",
});

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: `${APP_NAME} — Take orders on WhatsApp`,
    template: `%s · ${APP_NAME}`,
  },
  description: "Create a mobile store in minutes. Customers order from your link and the order lands in your WhatsApp.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  themeColor: "#1b1f3b",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${bricolage.variable} ${onest.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col font-sans">
        <SupportBanner />
        {children}
      </body>
    </html>
  );
}
