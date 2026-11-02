import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { Renew } from "./renew";

export const metadata: Metadata = { title: "Renew listing", robots: { index: false } };

export default function RenewPage() {
  return (
    <Suspense fallback={<PageLoader label="Loading your listing…" />}>
      <Renew />
    </Suspense>
  );
}
