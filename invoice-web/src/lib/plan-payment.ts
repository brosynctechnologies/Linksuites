"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api, ApiError } from "@/lib/api";

/**
 * What the return check found: Paid (the plan is active), Pending (still confirming), Failed (didn't go through),
 * Cancelled (the payer cancelled at the checkout) or Unavailable (this session can't check it: signed out or no access).
 */
export type PlanPaymentStatus = "Paid" | "Pending" | "Failed" | "Cancelled" | "Unavailable";

export interface PlanPaymentReturn {
  /** Null until the first answer, and when the page wasn't opened from a checkout. */
  status: PlanPaymentStatus | null;
  /** True while a payment that is still confirming is being checked again on its own. */
  watching: boolean;
  /** Starts the automatic re-checks again (the "Check again" button). */
  checkAgain: () => void;
}

/** A payment still confirming is checked again every few seconds, for about 30 seconds. */
const RECHECK_EVERY_MS = 3_000;
const RECHECK_FOR_MS = 30_000;

type Answer = PlanPaymentStatus | null;

/**
 * Checks in flight, per reference. A second mount (React StrictMode in development, a quick re-render) shares the first
 * request instead of sending another, so the server sees one return check at a time from this page.
 */
const inFlight = new Map<string, Promise<Answer>>();

function verify(reference: string): Promise<Answer> {
  const running = inFlight.get(reference);
  if (running) return running;
  const request = api<{ status: PlanPaymentStatus }>("/api/billing/verify", { method: "POST", body: { reference } })
    .then((res): Answer => res.status)
    .catch((e: unknown): Answer => {
      if (e instanceof ApiError) {
        // Not this business's payment: say nothing. Signed out or not allowed: say it can't be checked, and stop.
        if (e.status === 404 || e.status === 400) return null;
        if (e.status === 401 || e.status === 403) return "Unavailable";
      }
      return "Pending"; // offline, or the provider was slow: still confirming
    })
    .finally(() => inFlight.delete(reference));
  inFlight.set(reference, request);
  return request;
}

/**
 * Back from the secure checkout with ?ref=: asks the API to confirm the plan payment with the provider that took it
 * (POST /api/billing/verify), and keeps asking every few seconds for about 30 seconds while it is still confirming.
 * A checkout the payer cancelled (?cancelled=1) isn't checked at all. Calls onPaid when the plan becomes active, so the
 * page can reload its billing data.
 */
export function usePlanPaymentReturn(onPaid?: () => void): PlanPaymentReturn {
  const [status, setStatus] = useState<PlanPaymentStatus | null>(null);
  const [watching, setWatching] = useState(false);
  const onPaidRef = useRef(onPaid);
  const restart = useRef<() => void>(() => {});

  useEffect(() => {
    onPaidRef.current = onPaid;
  });

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const reference = params.get("ref");
    if (!reference) return;
    let cancelled = false;
    let timer: number | undefined;
    let until = 0;

    async function check() {
      const next = params.get("cancelled") === "1" ? "Cancelled" : await verify(reference!);
      if (cancelled) return;
      const again = next === "Pending" && Date.now() < until;
      setStatus(next);
      setWatching(again);
      if (next === "Paid") onPaidRef.current?.();
      if (again) timer = window.setTimeout(check, RECHECK_EVERY_MS);
    }

    restart.current = () => {
      window.clearTimeout(timer);
      until = Date.now() + RECHECK_FOR_MS;
      void check();
    };
    restart.current();
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, []);

  const checkAgain = useCallback(() => {
    setWatching(true);
    restart.current();
  }, []);

  return { status, watching, checkAgain };
}

/** The company account to pay a plan into, offered while online payment is off. `reference` goes in the transfer note. */
export interface BankTransfer {
  bankName: string;
  accountNumber: string;
  accountName: string;
  reference: string;
}

/** A bank transfer this business said it sent, waiting for LinkSuite to confirm it. */
export interface PendingTransfer {
  reference: string;
  planCode: string;
  planName: string;
  amountMinor: number;
  currency: string;
  createdAt: string;
}

/**
 * "I've sent the transfer": records the claim for the plan (the amount comes from the plan on the server), with the reference the
 * page showed; the API answers 409 when that reference is out of date, so the transfer note and the claim always match.
 */
export function claimTransfer(planCode: string, reference: string): Promise<PendingTransfer> {
  return api<PendingTransfer>("/api/billing/transfer", { method: "POST", body: { planCode, reference } });
}

/** Withdraws this business's pending transfer claim. */
export function cancelTransfer(): Promise<void> {
  return api<void>("/api/billing/transfer", { method: "DELETE" });
}

/** ₦3,000 (kobo in, no decimals unless there are some). */
export function formatTransferAmount(minor: number, currency = "NGN"): string {
  return new Intl.NumberFormat("en-NG", { style: "currency", currency: currency || "NGN", minimumFractionDigits: 0, maximumFractionDigits: 2 })
    .format(minor / 100);
}
