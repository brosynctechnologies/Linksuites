import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { PublicInvoice } from "./public-invoice";

const DESCRIPTION = "View, download or pay this invoice online.";

/** A client's private link: never indexed, no canonical, and a share preview that describes the invoice, not the product. */
export const metadata: Metadata = {
  title: "Invoice",
  description: DESCRIPTION,
  alternates: {},
  robots: { index: false, follow: false },
  openGraph: { ...baseOpenGraph, title: "Invoice", description: DESCRIPTION },
  twitter: { ...baseTwitter, title: "Invoice", description: DESCRIPTION },
};

export default function PublicInvoicePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PublicInvoice />
    </Suspense>
  );
}
