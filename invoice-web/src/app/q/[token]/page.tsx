import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { PublicQuote } from "./public-quote";

const DESCRIPTION = "View, download, accept or decline this quote online.";

/** A client's private link: never indexed, no canonical, and a share preview that describes the quote, not the product. */
export const metadata: Metadata = {
  title: "Quote",
  description: DESCRIPTION,
  alternates: {},
  robots: { index: false, follow: false },
  openGraph: { ...baseOpenGraph, title: "Quote", description: DESCRIPTION },
  twitter: { ...baseTwitter, title: "Quote", description: DESCRIPTION },
};

export default function PublicQuotePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PublicQuote />
    </Suspense>
  );
}
