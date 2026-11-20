"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, toApiError } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import type { PublicOrderDto } from "@/lib/types";

/** Messages for the API's 409 codes when it sends no detail of its own. */
const PAY_ERRORS: Record<string, string> = {
  online_payments_unavailable: "This store isn't taking online payments right now. Pay the seller the way you agreed on WhatsApp.",
  order_paid: "This order is already paid.",
  order_cancelled: "This order was cancelled, so it can't be paid.",
  delivery_fee_not_agreed: "Agree the delivery fee with the seller on WhatsApp first. Then you can pay here.",
};

type Check = { state: "checking" } | { state: "paid" } | { state: "waiting" } | { state: "error"; message: string };

/**
 * The customer's "Pay online" on the order page (the store's own Paystack). The button is in the store's colour:
 * Leaf green stays for WhatsApp only. Paystack sends the customer back here with `?reference=`, which is checked once.
 * `?pay=1` (the link in the WhatsApp message) brings the button into view.
 */
export function PayOnline({ order }: { order: PublicOrderDto }) {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const reference = searchParams.get("reference") ?? searchParams.get("trxref");
  const wantsPay = searchParams.get("pay") === "1";
  const [email, setEmail] = useState("");
  const [emailError, setEmailError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [check, setCheck] = useState<Check | null>(reference ? { state: "checking" } : null);
  const button = useRef<HTMLButtonElement>(null);
  const checked = useRef<string | null>(null);

  // Back from Paystack: confirm the payment with the API (once per reference).
  useEffect(() => {
    if (!reference || checked.current === reference) return;
    checked.current = reference;
    api<PublicOrderDto>(`/api/public/orders/${encodeURIComponent(order.code)}/verify`, {
      method: "POST",
      body: { reference },
      auth: false,
    }).then(
      (res) => {
        setCheck(res.paymentStatus === "Paid" ? { state: "paid" } : { state: "waiting" });
        // Show the order as it is now (status, Paid badge) and drop the reference from the address.
        router.replace(pathname, { scroll: false });
      },
      (e) => setCheck({ state: "error", message: toApiError(e).message }),
    );
  }, [reference, order.code, pathname, router]);

  // The WhatsApp pay link: bring the button into view and focus it.
  useEffect(() => {
    if (!wantsPay || !button.current) return;
    button.current.scrollIntoView({ block: "center", behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
    button.current.focus({ preventScroll: true });
  }, [wantsPay]);

  async function pay(e: FormEvent) {
    e.preventDefault();
    const value = email.trim();
    if (value && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
      setEmailError("Enter an email like name@example.com, or leave it empty.");
      return;
    }
    setEmailError(null);
    setError(null);
    setBusy(true);
    try {
      const res = await api<{ authorizationUrl: string; reference: string }>(`/api/public/orders/${encodeURIComponent(order.code)}/pay`, {
        method: "POST",
        body: value ? { email: value } : {},
        auth: false,
      });
      window.location.assign(res.authorizationUrl);
    } catch (err) {
      const apiErr = toApiError(err);
      setError(apiErr.detail || (apiErr.code && PAY_ERRORS[apiErr.code]) || apiErr.message);
      setBusy(false);
      if (apiErr.code === "order_paid" || apiErr.code === "order_cancelled") router.refresh();
    }
  }

  const status = check && (
    <div role={check.state === "error" ? "alert" : "status"} className="rounded-panel border border-line bg-surface p-4 text-sm">
      {check.state === "checking" && (
        <p className="flex items-center gap-2 font-medium text-ink">
          <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <circle cx="12" cy="12" r="10" stroke="currentColor" strokeOpacity="0.25" strokeWidth="4" />
            <path d="M22 12a10 10 0 0 0-10-10" stroke="currentColor" strokeWidth="4" strokeLinecap="round" />
          </svg>
          Checking your payment…
        </p>
      )}
      {check.state === "paid" && (
        <>
          <p className="font-display text-lg font-bold text-ink">Paid</p>
          <p className="mt-0.5 text-ink-2">Thank you. The seller can see your payment.</p>
        </>
      )}
      {check.state === "waiting" && (
        <>
          <p className="font-semibold text-ink">We haven&apos;t seen your payment yet</p>
          <p className="mt-0.5 text-ink-2">If you paid, it can take a minute to show. Refresh this page.</p>
        </>
      )}
      {check.state === "error" && (
        <>
          <p className="font-semibold text-hibiscus-deep">We couldn&apos;t check your payment</p>
          <p className="mt-0.5 text-ink-2">{check.message} If you paid, it can take a minute to show. Refresh this page.</p>
        </>
      )}
    </div>
  );

  const canPay = !!order.canPayOnline && order.paymentStatus !== "Paid" && order.status !== "Cancelled";
  if (!canPay) return status || null;

  return (
    <div className="space-y-3">
      {status}
      <form onSubmit={pay} className="space-y-3 rounded-panel border border-line bg-surface p-4 sm:p-5" noValidate>
        <h2 className="font-display text-lg font-bold text-ink">Pay online</h2>
        <div className="space-y-1.5">
          <label htmlFor="pay-email" className="block text-sm font-medium text-ink">
            Email for your receipt (optional)
          </label>
          <input
            id="pay-email"
            type="email"
            autoComplete="email"
            inputMode="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aria-invalid={!!emailError || undefined}
            aria-describedby={emailError ? "pay-email-error" : undefined}
            className="block min-h-11 w-full rounded-control border border-field bg-surface px-3 py-2.5 text-base text-ink placeholder:text-subtle focus:border-ink aria-[invalid=true]:border-hibiscus"
            placeholder="name@example.com"
          />
          {emailError && (
            <p id="pay-email-error" className="text-xs font-medium text-hibiscus-deep">
              {emailError}
            </p>
          )}
        </div>
        {error && (
          <p role="alert" className="rounded-control bg-hibiscus-soft p-3 text-sm text-hibiscus-deep">
            {error}
          </p>
        )}
        <button
          ref={button}
          type="submit"
          disabled={busy}
          aria-busy={busy || undefined}
          className="flex min-h-13 w-full items-center justify-center gap-2 rounded-control bg-accent px-5 font-semibold text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-60"
        >
          {busy ? "Opening secure payment…" : `Pay ${formatMoney(order.totalMinor, order.currency)} online`}
        </button>
        <p className="text-xs text-muted">Card, bank transfer or USSD through Paystack. You come back here when you&apos;re done.</p>
      </form>
    </div>
  );
}
