"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { api, downloadFile, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { formatRate } from "@/lib/fx";
import { RatesAttribution } from "@/components/fx";
import { useQueryParam } from "@/lib/use-debounced";
import type { BusinessDto, InvoiceDto, QuoteDto } from "@/lib/types";
import { CopyLinkButton, SendModal, Timeline, previewFromDoc } from "@/components/doc-view-parts";
import { InvoicePreview } from "@/components/invoice-preview";
import { ApiErrorView, Button, DocumentSkeleton, Notice, PageHeader, PageLoader, QuoteStatusBadge, SectionTitle, buttonClass, cx, panelClass } from "@/components/ui";
import { DownloadIcon, EditIcon, SendIcon, TrashIcon, WhatsAppIcon } from "@/components/icons";

export default function QuotePage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { data: quote, error, reload, setData } = useApi<QuoteDto>(`/api/quotes/${id}`);
  const { data: business } = useApi<BusinessDto>("/api/business");
  const created = useQueryParam("created");
  const canEdit = useCan("docs.edit");
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [sendOpen, setSendOpen] = useState(false);
  const [banner, setBanner] = useState<string | null>(() => (created ? "Quote saved." : null));

  async function act(key: string, fn: () => Promise<void>) {
    setBusy(key);
    setActionError(null);
    try {
      await fn();
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  if (!quote) {
    return (
      <div>
        <PageHeader title="Quote" back="/app/quotes" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const converted = quote.status === "Converted" || !!quote.convertedInvoiceId;
  const isDraft = quote.status === "Draft";
  const base = quote.baseCurrency || business?.currency || quote.currency;
  const foreign = base !== quote.currency && !!quote.exchangeRate;

  return (
    <div className="space-y-4">
      <PageHeader
        title={quote.number}
        back="/app/quotes"
        subtitle={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <QuoteStatusBadge status={quote.status} />
            <span>{quote.client.name}</span>
            <span className="tabular-nums">Valid until {formatDay(quote.expiryDate)}</span>
          </span>
        }
      />
      {banner && (
        <Notice tone="success">
          <div className="flex items-start justify-between gap-3">
            <span>{banner}</span>
            <button type="button" className="text-xs font-semibold underline" onClick={() => setBanner(null)}>
              Dismiss
            </button>
          </div>
        </Notice>
      )}
      {converted && quote.convertedInvoiceId && (
        <Notice tone="info">
          This quote was converted to an invoice.{" "}
          <Link href={`/app/invoices/${quote.convertedInvoiceId}`} className="font-semibold underline">
            View invoice
          </Link>
        </Notice>
      )}
      {actionError && <ApiErrorView error={actionError} />}

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start xl:grid-cols-[minmax(0,1fr)_22rem] xl:gap-8">
        <div className="sticky top-14 z-20 -mx-4 border-b border-rule bg-desk/95 px-4 py-2.5 backdrop-blur sm:-mx-6 sm:px-6 lg:static lg:col-start-2 lg:row-start-1 lg:mx-0 lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none">
          <div className="no-scrollbar flex gap-2 overflow-x-auto lg:grid lg:grid-cols-2 lg:overflow-visible" role="group" aria-label="Quote actions">
            {canEdit && !converted && (
              <Button
                className="shrink-0 lg:col-span-2"
                loading={busy === "convert"}
                onClick={() =>
                  act("convert", async () => {
                    const inv = await api<InvoiceDto>(`/api/quotes/${quote.id}/convert`, { method: "POST" });
                    router.push(`/app/invoices/${inv.id}?created=1`);
                  })
                }
              >
                Convert to invoice
              </Button>
            )}
            {canEdit && (
              <Button variant="secondary" className="shrink-0 lg:col-span-2" onClick={() => setSendOpen(true)}>
                <SendIcon className="h-4 w-4" /> Send by email
              </Button>
            )}
            {canEdit && (
              <Button
                variant="secondary"
                className="shrink-0"
                loading={busy === "wa"}
                onClick={() => {
                  window.open(quote.whatsappShareUrl, "_blank", "noopener,noreferrer");
                  if (isDraft) act("wa", async () => setData(await api<QuoteDto>(`/api/quotes/${quote.id}/mark-sent`, { method: "POST" })));
                }}
              >
                <WhatsAppIcon className="h-4 w-4 text-[#1a9e4b]" /> WhatsApp
              </Button>
            )}
            <CopyLinkButton url={quote.publicUrl} className="shrink-0" />
            <Button
              variant="secondary"
              className="shrink-0"
              loading={busy === "pdf"}
              onClick={() => act("pdf", () => downloadFile(`/api/quotes/${quote.id}/pdf`, `${quote.number}.pdf`, { accept: "application/pdf" }))}
            >
              <DownloadIcon className="h-4 w-4" /> PDF
            </Button>
            {canEdit && !converted && (
              <Link href={`/app/quotes/${quote.id}/edit`} className={buttonClass("secondary", "shrink-0")}>
                <EditIcon className="h-4 w-4" /> Edit
              </Link>
            )}
            {canEdit && !converted && (
              <Button
                variant="danger"
                className="shrink-0"
                loading={busy === "del"}
                onClick={() => {
                  if (!window.confirm(`Delete quote ${quote.number}?`)) return;
                  act("del", async () => {
                    await api(`/api/quotes/${quote.id}`, { method: "DELETE" });
                    router.replace("/app/quotes");
                  });
                }}
              >
                <TrashIcon className="h-4 w-4" /> Delete
              </Button>
            )}
          </div>
        </div>

        <div className="min-w-0 lg:col-start-1 lg:row-span-2 lg:row-start-1">
          {business ? <InvoicePreview doc={previewFromDoc(quote, business)} /> : <DocumentSkeleton />}
        </div>

        <aside className={cx(panelClass, "divide-y divide-rule-soft lg:col-start-2 lg:row-start-2")} aria-label="Total and activity">
          <section className="p-4 sm:p-5">
            <p className="text-sm text-muted">Quote total</p>
            <p className="font-figure amount mt-0.5 text-[2.25rem] leading-none text-graphite">{formatMoney(quote.totalMinor, quote.currency)}</p>
            {foreign && (
              <div className="mt-3 space-y-0.5 text-sm">
                <p className="amount text-slate-600">≈ {formatMoney(quote.totalBaseMinor, base)}</p>
                <p className="text-xs text-muted">Exchange rate: {formatRate(quote.currency, quote.exchangeRate, base)}</p>
                <RatesAttribution />
              </div>
            )}
          </section>
          <section className="p-4 sm:p-5">
            <SectionTitle>Activity</SectionTitle>
            <Timeline events={quote.events ?? []} />
          </section>
        </aside>
      </div>

      <SendModal
        open={sendOpen}
        onClose={() => setSendOpen(false)}
        defaultTo={quote.client.email}
        kind="quote"
        onSend={async (body) => {
          setData(await api<QuoteDto>(`/api/quotes/${quote.id}/send`, { method: "POST", body }));
          setBanner("Quote sent.");
        }}
      />
    </div>
  );
}
