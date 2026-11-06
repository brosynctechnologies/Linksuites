"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { DownloadIcon } from "@/components/icons";
import { PortalError } from "@/components/portal-parts";
import { Button, Card, ErrorBox, Notice, PageHeader, PageLoader, buttonClass } from "@/components/ui";
import { api, downloadFile, toApiError, type ApiError } from "@/lib/api";
import { useHydrated } from "@/lib/auth";
import { money } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { checkoutPatient, portalHref } from "@/lib/portal";
import type { CareOnlineStatusDto } from "@/lib/types/portal";

/**
 * Paystack sends the payer back to `/portal/pay/return?reference=` (OnlinePaymentService). This page asks the API to verify the
 * reference (`POST /api/care-portal/payments/{reference}/verify`, which needs an active link to the payment's patient) and shows the result:
 * paid, still pending, a mismatch, or not completed. Verifying again never records a payment twice (Task 11).
 *
 * Post-build: one verify request per check, even when React runs the effect twice in development (the request is shared through a ref).
 * While Paystack hasn't confirmed, the page checks again on its own every 3 seconds for about 30 seconds, showing "Confirming your
 * payment"; only then does it show "Still waiting" with the "Check again" button.
 */
const RECHECK_MS = 3_000;
const RECHECK_FOR_MS = 30_000;

export default function PayReturnPage() {
  const params = useSearchParams();
  const reference = (params.get("reference") ?? params.get("trxref") ?? "").trim();
  const hydrated = useHydrated();
  const patientId = hydrated && reference ? checkoutPatient(reference) : null;
  const [status, setStatus] = useState<CareOnlineStatusDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [autoChecking, setAutoChecking] = useState(false);
  const loading = !!reference && !status && !error;
  const receipt = useAction();
  // The check in flight, keyed by reference and attempt: a second run of the same effect reuses it instead of asking again.
  const inflight = useRef<{ key: string; promise: Promise<CareOnlineStatusDto> } | null>(null);
  // When the automatic re-checks stop (set on the first check).
  const recheckUntil = useRef<number | null>(null);

  useEffect(() => {
    if (!reference) return;
    const key = `${reference}#${attempt}`;
    if (inflight.current?.key !== key) {
      inflight.current = { key, promise: api<CareOnlineStatusDto>(`/api/care-portal/payments/${encodeURIComponent(reference)}/verify`, { method: "POST" }) };
    }
    if (recheckUntil.current === null) recheckUntil.current = Date.now() + RECHECK_FOR_MS;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    inflight.current.promise.then(
      (data) => {
        if (cancelled) return;
        const again = data.status === "Pending" && recheckUntil.current !== null && Date.now() + RECHECK_MS <= recheckUntil.current;
        setAutoChecking(again);
        setStatus(data);
        if (again) timer = setTimeout(() => setAttempt((n) => n + 1), RECHECK_MS);
      },
      (e) => {
        if (cancelled) return;
        setAutoChecking(false);
        setError(toApiError(e));
      },
    );
    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
    };
  }, [reference, attempt]);

  function verifyAgain() {
    setError(null);
    setStatus(null);
    setAutoChecking(false);
    setAttempt((n) => n + 1);
  }

  const billsHref = portalHref("/portal/bills", patientId);
  return (
    <div className="space-y-5">
      <PageHeader title="Payment" back={billsHref} />
      {!reference && <Notice tone="warning">No payment reference came back. Open your bills and press Pay now again if you still need to pay.</Notice>}
      {error && <PortalError error={error} onRetry={verifyAgain} />}
      {loading && <PageLoader label="Checking your payment…" />}
      {status && autoChecking && <PageLoader label="Confirming your payment with Paystack…" />}
      {status && !autoChecking && (
        <Card>
          <Result status={status} />
          {status.status === "Succeeded" && status.receiptNumber && patientId && (
            <div className="mt-4">
              <Button
                variant="secondary"
                loading={receipt.busy}
                onClick={() =>
                  receipt.run(() => downloadFile(`/api/care-portal/patients/${patientId}/receipts/${status.id}.pdf`, `${status.receiptNumber}.pdf`))
                }
              >
                <DownloadIcon className="h-4 w-4" /> Receipt {status.receiptNumber}
              </Button>
              <ErrorBox error={receipt.error} />
            </div>
          )}
          {status.status === "Succeeded" && status.receiptNumber && !patientId && (
            <p className="mt-3 text-sm text-slate-700">Your receipt is on the bill under Bills and receipts.</p>
          )}
          {status.status === "Pending" && (
            <Button className="mt-4" onClick={verifyAgain}>
              Check again
            </Button>
          )}
        </Card>
      )}
      <Link href={billsHref} className={buttonClass("secondary")}>
        Back to bills
      </Link>
    </div>
  );
}

function Result({ status }: { status: CareOnlineStatusDto }) {
  switch (status.status) {
    case "Succeeded":
      return (
        <>
          <p className="display text-xl font-bold text-pass">Payment received</p>
          <p className="tnum mt-1 text-sm text-slate-700">
            {money(status.amountMinor)} is on the bill.{" "}
            {status.billBalanceMinor > 0 ? `${money(status.billBalanceMinor)} is still to pay.` : "Nothing is left to pay on it."}
          </p>
        </>
      );
    case "Pending":
      return (
        <>
          <p className="display text-xl font-bold text-ink">Still waiting</p>
          <p className="mt-1 text-sm text-slate-700">
            Paystack hasn&apos;t confirmed this payment yet. If you were charged, it will update on its own. You can check again in a minute.
          </p>
        </>
      );
    case "Mismatch":
      return (
        <Notice tone="error">
          Paystack reported a different amount, so this payment was not added to the bill. The clinic has been told. If you were charged, give the
          clinic this reference: <span className="tnum font-bold">{status.reference}</span>.
        </Notice>
      );
    default:
      return (
        <Notice tone="warning">
          This payment was not completed, so nothing was added to the bill. If you were charged, it will update on its own. Reference{" "}
          <span className="tnum font-bold">{status.reference}</span>.
        </Notice>
      );
  }
}
