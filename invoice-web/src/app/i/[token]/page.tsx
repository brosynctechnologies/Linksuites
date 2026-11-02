import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { PublicInvoice } from "./public-invoice";

export const metadata: Metadata = { title: "Invoice", robots: { index: false } };

export default function PublicInvoicePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PublicInvoice />
    </Suspense>
  );
}
