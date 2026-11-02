"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { DownloadIcon } from "@/components/icons";
import { Button, Card, ErrorBox, Notice, PageHeader, PageLoader, buttonClass } from "@/components/ui";
import { api, downloadFile, toApiError, type ApiError } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import type { PaymentStatusDto } from "@/lib/types/portals";
import { ABANDONED_COPY, PortalBlocked, money, useChild } from "../../_shared";

export default function PayReturnPage() {
  const params = useSearchParams();
  const reference = (params.get("reference") ?? params.get("trxref") ?? "").trim();
  const { select } = useChild();
  const selectRef = useRef(select);
  const [status, setStatus] = useState<PaymentStatusDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [loading, setLoading] = useState(!!reference);
  const [attempt, setAttempt] = useState(0);
  const receipt = useAction();

  useEffect(() => {
    selectRef.current = select;
  }, [select]);

  useEffect(() => {
    if (!reference) return;
    let cancelled = false;
    api<PaymentStatusDto>(`/api/parent/payments/${encodeURIComponent(reference)}/verify`, { method: "POST" }).then(
      (data) => {
        if (cancelled) return;
        setStatus(data);
        selectRef.current(data.studentId);
        setLoading(false);
      },
      (e) => {
        if (cancelled) return;
        setError(toApiError(e));
        setLoading(false);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [reference, attempt]);

  function verify() {
    setError(null);
    setStatus(null);
    setLoading(true);
    setAttempt((n) => n + 1);
  }

  return (
    <div className="space-y-5">
      <PageHeader title="Payment" back="/parent/fees" />
      {!reference && (
        <Notice tone="warning">No payment reference came back. Open Fees and try Pay now again if you still need to pay.</Notice>
      )}
      {error && <PortalBlocked error={error} onRetry={verify} />}
      {loading && <PageLoader label="Checking your payment…" />}
      {status && !loading && (
        <Card>
          <StatusBody status={status} />
          {status.status === "Succeeded" && status.receiptNumber && (
            <div className="mt-4">
              <Button
                variant="secondary"
                loading={receipt.busy}
                onClick={() => receipt.run(() => downloadFile(`/api/parent/payments/${status.id}/receipt.pdf`, `${status.receiptNumber}.pdf`))}
              >
                <DownloadIcon className="h-4 w-4" /> Receipt {status.receiptNumber}
              </Button>
              {receipt.error && <ErrorBox error={receipt.error} />}
            </div>
          )}
          {status.status === "Pending" && (
            <Button className="mt-4" onClick={() => void verify()}>
              Check again
            </Button>
          )}
        </Card>
      )}
      <Link href="/parent/fees" className={buttonClass("secondary")}>
        Back to fees
      </Link>
    </div>
  );
}

function StatusBody({ status }: { status: PaymentStatusDto }) {
  if (status.status === "Abandoned") {
    return (
      <Notice tone="warning">
        {ABANDONED_COPY}
      </Notice>
    );
  }
  if (status.status === "Succeeded") {
    return (
      <>
        <p className="font-bold text-ink">Payment received</p>
        <p className="mt-1 text-sm text-slate-700">
          {money(status.amountMinor)} is on the invoice. Remaining balance {money(status.invoiceBalance)}.
        </p>
      </>
    );
  }
  if (status.status === "Mismatch") {
    return (
      <Notice tone="error">
        Paystack reported a different amount, so this payment was not applied. Ask the school office to check the reference{" "}
        <span className="tnum">{status.reference}</span>.
      </Notice>
    );
  }
  return (
    <>
      <p className="font-bold text-ink">Still waiting</p>
      <p className="mt-1 text-sm text-slate-700">
        Paystack hasn&apos;t confirmed this payment yet. If you were charged, it will update automatically.
      </p>
    </>
  );
}
