"use client";

import { useParams, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, apiUrl, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import type { PublicInvoiceDto } from "@/lib/types";
import { previewFromDoc } from "@/components/doc-view-parts";
import { InvoicePreview } from "@/components/invoice-preview";
import { PublicDocShell } from "@/components/public-doc";
import { Button, Card, ErrorBox, Input, InvoiceStatusBadge, Notice, PageLoader, SectionTitle, buttonClass } from "@/components/ui";
import { DownloadIcon } from "@/components/icons";

type Banner = { tone: "success" | "info" | "error"; text: string } | null;

const RECEIVED = "Payment received — thank you! A receipt is listed below.";
const CHECKING = "We're checking your payment. This page will update when it's confirmed.";
/** When to ask again while a payment is not confirmed yet (spaced out to stay well inside the public rate limit). */
const RECHECK_DELAYS_MS = [5_000, 15_000, 30_000, 60_000, 120_000];

export function PublicInvoice() {
  const { token } = useParams<{ token: string }>();
  const reference = useSearchParams().get("reference");
  const [data, setData] = useState<PublicInvoiceDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [banner, setBanner] = useState<Banner>(null);
  // The Paid stamp lands with a brief animation when this visit just confirmed a payment.
  const [justPaid, setJustPaid] = useState(false);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    const base = `/api/public/invoices/${encodeURIComponent(token)}`;
    const get = () => api<PublicInvoiceDto>(base, { auth: false });
    const verify = (ref: string) => api<PublicInvoiceDto>(`${base}/verify`, { method: "POST", body: { reference: ref }, auth: false });
    let timer: number | undefined;

    // A 200 from verify only means the page loaded: "received" is shown only when the server confirms this payment
    // was recorded (or the invoice is now paid). Otherwise the page keeps checking for a short while, since Paystack
    // or its webhook can lag behind the payer's return.
    const settle = (inv: PublicInvoiceDto, ref: string, attempt: number) => {
      setData(inv);
      if (inv.paymentConfirmed) {
        setBanner({ tone: "success", text: RECEIVED });
        setJustPaid(true);
        return;
      }
      setBanner({ tone: "info", text: CHECKING });
      const delay = RECHECK_DELAYS_MS[attempt];
      if (delay === undefined) return;
      timer = window.setTimeout(() => verify(ref).then((next) => settle(next, ref, attempt + 1), () => undefined), delay);
    };

    if (reference) {
      verify(reference).then(
        (inv) => settle(inv, reference, 0),
        (e) => {
          setBanner({ tone: "error", text: `We couldn't confirm that payment. ${toApiError(e).message}` });
          get().then(setData, (err) => setError(toApiError(err)));
        },
      );
      // Drop ?reference= so a refresh doesn't verify again.
      window.history.replaceState(null, "", window.location.pathname);
    } else {
      get().then(setData, (e) => setError(toApiError(e)));
    }
    return () => window.clearTimeout(timer);
  }, [token, reference]);

  if (error) {
    return (
      <PublicDocShell business={null}>
        <Card>
          <h1 className="font-display text-xl font-semibold text-graphite">{error.status === 404 ? "Invoice not found" : "Couldn't load this invoice"}</h1>
          <p className="mt-1 text-sm text-slate-600">
            {error.status === 404 ? "The link may be wrong or the invoice was removed. Please contact the sender." : error.message}
          </p>
        </Card>
      </PublicDocShell>
    );
  }
  if (!data) return <PageLoader label="Loading invoice…" />;

  const c = data.currency;
  const isVoid = data.status === "Void";
  // Never shown below zero to the client: an overpayment is the business's to sort out.
  const balance = Math.max(0, data.balanceMinor);
  const payable = data.canPayOnline && balance > 0 && !isVoid;

  return (
    <PublicDocShell business={data.business} showBranding={data.showBranding}>
      {banner && <Notice tone={banner.tone}>{banner.text}</Notice>}

      <Card>
        <div className="flex flex-col-reverse items-start gap-3 sm:flex-row sm:justify-between">
          <div className="min-w-0">
            <h1 className="text-sm text-muted">
              Invoice <span className="tabular-nums">{data.number}</span>
              {data.business?.name && <span className="sr-only"> from {data.business.name}</span>}
            </h1>
            {isVoid ? (
              // A cancelled invoice is not owed, so it shows no amount due and no due date.
              <p className="font-figure mt-1 text-[clamp(2rem,9vw,2.5rem)] leading-none text-graphite">Cancelled</p>
            ) : (
              <>
                <p className="font-figure amount mt-1 text-[clamp(2rem,9vw,2.5rem)] leading-none text-graphite">{formatMoney(balance, c)}</p>
                <p className="mt-2 text-sm text-slate-600">{balance > 0 ? `Due ${formatDay(data.dueDate)}` : "Nothing left to pay"}</p>
              </>
            )}
            {!isVoid && data.amountPaidMinor > 0 && (
              <p className="text-sm text-muted">
                <span className="amount">{formatMoney(data.amountPaidMinor, c)}</span> paid of{" "}
                <span className="amount">{formatMoney(data.totalMinor, c)}</span>
              </p>
            )}
          </div>
          <InvoiceStatusBadge status={data.status} />
        </div>
        <div className="mt-5 flex flex-col gap-2 sm:flex-row">
          {payable && <PayNow token={token} hasEmail={!!data.client.email} amount={formatMoney(data.balanceMinor, c)} />}
          <a
            href={apiUrl(`/api/public/invoices/${encodeURIComponent(token)}/pdf`)}
            target="_blank"
            rel="noopener noreferrer"
            className={buttonClass("secondary", "sm:w-auto")}
          >
            <DownloadIcon className="h-4 w-4" /> Download PDF
          </a>
        </div>
        {data.status === "Void" && <p className="mt-3 text-sm text-slate-600">This invoice has been cancelled by the sender.</p>}
      </Card>

      <InvoicePreview doc={previewFromDoc(data, data.business, false)} animateStamp={justPaid} />

      {data.payments.length > 0 && (
        <Card>
          <SectionTitle>Payments &amp; receipts</SectionTitle>
          <ul className="divide-y divide-rule-soft">
            {data.payments.map((p) => (
              <li key={p.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                <div>
                  <p className="amount font-semibold">{formatMoney(p.amountMinor, c)}</p>
                  <p className="flex flex-wrap gap-x-2 text-xs text-muted">
                    <span className="tabular-nums">{formatDay(p.paidAt)}</span>
                    {p.receiptNumber && <span>Receipt {p.receiptNumber}</span>}
                  </p>
                </div>
                <a
                  href={apiUrl(`/api/public/invoices/${encodeURIComponent(token)}/receipts/${p.id}/pdf`)}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="shrink-0 font-semibold text-ink underline-offset-2 hover:underline"
                >
                  Download receipt
                </a>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </PublicDocShell>
  );
}

function PayNow({ token, hasEmail, amount }: { token: string; hasEmail: boolean; amount: string }) {
  const [askEmail, setAskEmail] = useState(false);
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function pay(e?: FormEvent) {
    e?.preventDefault();
    if (!hasEmail && !askEmail) {
      setAskEmail(true);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const res = await api<{ authorizationUrl: string }>(`/api/public/invoices/${encodeURIComponent(token)}/pay`, {
        method: "POST",
        body: hasEmail ? {} : { email: email.trim() },
        auth: false,
      });
      window.location.assign(res.authorizationUrl);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  if (askEmail) {
    return (
      <form onSubmit={pay} className="w-full space-y-2">
        <Input
          label="Your email (for the payment receipt)"
          type="email"
          autoComplete="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <ErrorBox error={error} />
        <Button type="submit" loading={busy} disabled={!email.includes("@")} className="w-full">
          Continue to pay {amount}
        </Button>
      </form>
    );
  }
  return (
    <div className="flex-1 space-y-2">
      <Button onClick={() => pay()} loading={busy} className="w-full">
        Pay {amount} now
      </Button>
      <ErrorBox error={error} />
    </div>
  );
}
