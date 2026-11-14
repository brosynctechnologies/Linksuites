"use client";

import { useEffect, useRef, useState } from "react";
import { toApiError, type ApiError } from "@/lib/api";
import { Button, ErrorBox, Notice, Panel as Box } from "@/components/ui";
import { cancelTransfer, claimTransfer, formatTransferAmount, type BankTransfer, type PendingTransfer } from "@/lib/plan-payment";

interface TransferPlan {
  code: string;
  name: string;
  priceMinor: number;
  currency: string;
}

/** Copies a value and says so for two seconds (announced to screen readers). */
function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      // Clipboard blocked: the value is on screen to copy by hand.
    }
  }
  return (
    <button type="button" onClick={copy} className="text-sm font-semibold underline underline-offset-2" aria-label={copied ? `${label} copied` : `Copy ${label}`}>
      <span aria-live="polite">{copied ? "Copied" : "Copy"}</span>
    </button>
  );
}

function Detail({ label, value, copy }: { label: string; value: string; copy?: boolean }) {
  return (
    <div>
      <dt className="text-muted">{label}</dt>
      <dd className="mt-0.5 flex flex-wrap items-baseline gap-x-3 font-semibold">
        <span className="break-all">{value}</span>
        {copy && <CopyButton value={value} label={label.toLowerCase()} />}
      </dd>
    </div>
  );
}

/**
 * Paying for a plan by bank transfer while online payment is off: the amount, the company account and the reference for the transfer
 * note, then "I've sent the transfer", which records the claim for LinkSuite to confirm.
 */
export function PlanTransferPanel({ plan, bank, onClaimed, onClose }: {
  plan: TransferPlan;
  bank: BankTransfer;
  onClaimed: (claim: PendingTransfer) => void;
  onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const panel = useRef<HTMLElement>(null);
  const amount = formatTransferAmount(plan.priceMinor, plan.currency);

  // On a phone the button is far below where the panel opens: bring the panel into view, then put focus on its heading.
  useEffect(() => {
    const reduce = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
    panel.current?.scrollIntoView({ behavior: reduce ? "auto" : "smooth", block: "start" });
    heading.current?.focus({ preventScroll: true });
  }, [plan.code]);

  async function claim() {
    setBusy(true);
    setError(null);
    try {
      onClaimed(await claimTransfer(plan.code, bank.reference));
    } catch (e) {
      setError(toApiError(e));
      setBusy(false);
    }
  }

  return (
    <Box>
      <section ref={panel} aria-labelledby="plan-transfer-title" className="scroll-mt-4 space-y-4">
        <div>
          <h2 id="plan-transfer-title" ref={heading} tabIndex={-1} className="text-lg font-semibold outline-none">
            Pay for {plan.name} by bank transfer
          </h2>
          <p className="mt-1 text-sm text-muted">
            Send exactly {amount} from your bank app to the account below, with the reference in the transfer note. Then tap
            &ldquo;I&apos;ve sent the transfer&rdquo;. Your plan starts once we&apos;ve confirmed the payment.
          </p>
        </div>
        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <Detail label="Amount" value={amount} />
          <Detail label="Bank" value={bank.bankName} />
          <Detail label="Account number" value={bank.accountNumber} copy />
          <Detail label="Account name" value={bank.accountName} />
          <Detail label="Reference for the transfer note" value={bank.reference} copy />
        </dl>
        {error && <ErrorBox error={error} />}
        <div className="flex flex-wrap gap-3">
          <Button loading={busy} onClick={claim}>
            I&apos;ve sent the transfer
          </Button>
          <Button variant="secondary" disabled={busy} onClick={onClose}>
            Close
          </Button>
        </div>
      </section>
    </Box>
  );
}

/** A transfer the business said it sent, waiting for LinkSuite to confirm it, with "Cancel" for those who may change the plan. */
export function PendingTransferNotice({ pending, canCancel, onCancelled }: {
  pending: PendingTransfer;
  canCancel: boolean;
  onCancelled: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [asking, setAsking] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function cancel() {
    setBusy(true);
    setError(null);
    try {
      await cancelTransfer();
      setAsking(false);
      onCancelled();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-2">
      <Notice tone="info">
        We&apos;re checking your transfer of {formatTransferAmount(pending.amountMinor, pending.currency)} (reference {pending.reference}) for the{" "}
        {pending.planName} plan. Your plan starts once it&apos;s confirmed.
        {canCancel && !asking && (
          <>
            {" "}
            <button type="button" onClick={() => setAsking(true)} className="font-semibold underline underline-offset-2">
              Cancel
            </button>
          </>
        )}
      </Notice>
      {canCancel && asking && (
        <div role="alertdialog" aria-labelledby="transfer-cancel-question" className="space-y-3">
          <Notice tone="warning">
            <span id="transfer-cancel-question">
              Only cancel if you haven&apos;t sent the money. If you already sent it, keep this so we can match it.
            </span>
          </Notice>
          <div className="flex flex-wrap gap-3">
            <Button variant="secondary" autoFocus disabled={busy} onClick={() => setAsking(false)}>
              Keep it
            </Button>
            <Button variant="danger" loading={busy} onClick={cancel}>
              Cancel the transfer claim
            </Button>
          </div>
        </div>
      )}
      {error && <ErrorBox error={error} />}
    </div>
  );
}
