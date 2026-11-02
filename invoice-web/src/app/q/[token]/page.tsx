import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { PublicQuote } from "./public-quote";

export const metadata: Metadata = { title: "Quote", robots: { index: false } };

export default function PublicQuotePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PublicQuote />
    </Suspense>
  );
}
