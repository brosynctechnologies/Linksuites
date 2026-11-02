import type { Metadata } from "next";
import { Suspense } from "react";
import { SitePage } from "@/components/site-chrome";
import { PageLoader } from "@/components/ui";
import { VerifyPayment } from "./verify-payment";

export const metadata: Metadata = { title: "Confirming your payment", robots: { index: false } };

/** Paystack returns learners here with ?reference=…; the client confirms the payment with the API. */
export default function CheckoutReturnPage() {
  return (
    <SitePage hideSearch>
      <div className="mx-auto max-w-lg px-4 py-12 sm:py-16">
        <Suspense fallback={<PageLoader label="Confirming your payment…" />}>
          <VerifyPayment />
        </Suspense>
      </div>
    </SitePage>
  );
}
