"use client";

import { Notice } from "@/components/ui";
import type { PlanPaymentReturn } from "@/lib/plan-payment";

/**
 * The return check's answer on a billing page: active, still confirming (with re-checks), didn't go through, cancelled, or can't be
 * checked. Each answer is a Notice, which is a live region (role="status", or "alert" for errors where the app has them).
 */
export function PlanPaymentNotice({ payment }: { payment: PlanPaymentReturn }) {
  switch (payment.status) {
    case null:
      return null;
    case "Paid":
      return <Notice tone="success">Payment received, your plan is active.</Notice>;
    case "Failed":
      return <Notice tone="warning">Payment didn&apos;t go through. Your plan hasn&apos;t changed, so you can try again.</Notice>;
    case "Cancelled":
      return <Notice tone="info">Payment cancelled. Your plan hasn&apos;t changed.</Notice>;
    case "Unavailable":
      return <Notice tone="warning">We couldn&apos;t check this payment. Sign in again and reopen this page, or contact us if your plan doesn&apos;t change.</Notice>;
    default:
      return (
        <Notice tone="info">
          Still confirming your payment.{" "}
          {payment.watching ? (
            "This page checks again every few seconds."
          ) : (
            <>
              It can take a few minutes.{" "}
              <button type="button" onClick={payment.checkAgain} className="font-semibold underline underline-offset-2">
                Check again
              </button>
            </>
          )}
        </Notice>
      );
  }
}
