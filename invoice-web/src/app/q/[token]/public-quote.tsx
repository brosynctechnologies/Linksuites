"use client";

import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { api, apiUrl, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import type { PublicQuoteDto } from "@/lib/types";
import { previewFromDoc } from "@/components/doc-view-parts";
import { InvoicePreview } from "@/components/invoice-preview";
import { PublicDocShell } from "@/components/public-doc";
import { Button, Card, ErrorBox, Notice, PageLoader, QuoteStatusBadge, buttonClass } from "@/components/ui";
import { DownloadIcon } from "@/components/icons";

export function PublicQuote() {
  const { token } = useParams<{ token: string }>();
  const [data, setData] = useState<PublicQuoteDto | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState<"accept" | "decline" | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const base = `/api/public/quotes/${encodeURIComponent(token)}`;

  useEffect(() => {
    let live = true;
    api<PublicQuoteDto>(base, { auth: false }).then(
      (q) => live && setData(q),
      (e) => live && setError(toApiError(e)),
    );
    return () => {
      live = false;
    };
  }, [base]);

  async function respond(action: "accept" | "decline") {
    if (action === "decline" && !window.confirm("Decline this quote?")) return;
    setBusy(action);
    setActionError(null);
    try {
      setData(await api<PublicQuoteDto>(`${base}/${action}`, { method: "POST", auth: false }));
      setDone(action === "accept" ? "Thanks — you accepted this quote. The sender has been notified." : "You declined this quote. The sender has been notified.");
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  if (error) {
    return (
      <PublicDocShell business={null}>
        <Card>
          <h1 className="font-display text-xl font-semibold text-graphite">{error.status === 404 ? "Quote not found" : "Couldn't load this quote"}</h1>
          <p className="mt-1 text-sm text-slate-600">{error.status === 404 ? "The link may be wrong or the quote was removed." : error.message}</p>
        </Card>
      </PublicDocShell>
    );
  }
  if (!data) return <PageLoader label="Loading quote…" />;

  return (
    <PublicDocShell business={data.business} showBranding={data.showBranding}>
      {done && <Notice tone="success">{done}</Notice>}
      <Card>
        <div className="flex flex-col-reverse items-start gap-3 sm:flex-row sm:justify-between">
          <div className="min-w-0">
            <h1 className="text-sm text-muted">
              Quote <span className="tabular-nums">{data.number}</span>
              {data.business?.name && <span className="sr-only"> from {data.business.name}</span>}
            </h1>
            <p className="font-figure amount mt-1 text-[clamp(2rem,9vw,2.5rem)] leading-none text-graphite">{formatMoney(data.totalMinor, data.currency)}</p>
            <p className="mt-2 text-sm text-slate-600">Valid until {formatDay(data.expiryDate)}</p>
          </div>
          <QuoteStatusBadge status={data.status} />
        </div>
        <div className="mt-5 flex flex-col gap-2 sm:flex-row">
          {data.canRespond && (
            <>
              <Button onClick={() => respond("accept")} loading={busy === "accept"} disabled={!!busy} className="sm:flex-1">
                Accept quote
              </Button>
              <Button variant="secondary" onClick={() => respond("decline")} loading={busy === "decline"} disabled={!!busy}>
                Decline
              </Button>
            </>
          )}
          <a href={apiUrl(`${base}/pdf`)} target="_blank" rel="noopener noreferrer" className={buttonClass("secondary")}>
            <DownloadIcon className="h-4 w-4" /> Download PDF
          </a>
        </div>
        {actionError && (
          <div className="mt-3">
            <ErrorBox error={actionError} />
          </div>
        )}
      </Card>
      <InvoicePreview doc={previewFromDoc(data, data.business, false)} animateStamp={!!done} />
    </PublicDocShell>
  );
}
