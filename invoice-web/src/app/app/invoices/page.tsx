"use client";

import Link from "next/link";
import { useState } from "react";
import { downloadFile, toApiError, type ApiError } from "@/lib/api";
import { todayISO } from "@/lib/dates";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { useDebounced, useQueryParam } from "@/lib/use-debounced";
import type { BusinessDto, InvoiceStatus, InvoiceSummaryDto, Paged } from "@/lib/types";
import { InvoiceList } from "@/components/doc-rows";
import { ApiErrorView, Button, EmptyState, ErrorBox, PageHeader, PageLoader, Pager, SearchField, Tabs, buttonClass } from "@/components/ui";
import { DownloadIcon, InvoiceIcon, PlusIcon } from "@/components/icons";

const TABS: { value: InvoiceStatus | ""; label: string }[] = [
  { value: "", label: "All" },
  { value: "Draft", label: "Draft" },
  { value: "Sent", label: "Sent" },
  { value: "Overdue", label: "Overdue" },
  { value: "PartiallyPaid", label: "Partially paid" },
  { value: "Paid", label: "Paid" },
  { value: "Void", label: "Void" },
];
const PAGE_SIZE = 20;

export default function InvoicesPage() {
  const statusParam = useQueryParam("status");
  const [status, setStatus] = useState<string>(() => {
    const s = statusParam ?? "";
    return TABS.some((t) => t.value === s) ? s : "";
  });
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const dq = useDebounced(q.trim());
  const canEdit = useCan("docs.edit");
  const canExport = useCan("reports.view");
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<ApiError | null>(null);

  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  if (status) params.set("status", status);
  if (dq) params.set("q", dq);
  const { data, error, loading, reload } = useApi<Paged<InvoiceSummaryDto>>(`/api/invoices?${params}`);
  const { data: business } = useApi<BusinessDto>("/api/business");

  async function exportCsv() {
    setExporting(true);
    setExportError(null);
    try {
      const p = new URLSearchParams();
      if (status) p.set("status", status);
      await downloadFile(`/api/invoices/export.csv${p.toString() ? `?${p}` : ""}`, `invoices-${todayISO()}.csv`, { accept: "text/csv" });
    } catch (e) {
      setExportError(toApiError(e));
    } finally {
      setExporting(false);
    }
  }

  return (
    <div>
      <PageHeader
        title="Invoices"
        action={
          <>
            {canExport && (
              <Button variant="secondary" onClick={exportCsv} loading={exporting}>
                <DownloadIcon className="h-4 w-4" /> <span className="hidden sm:inline">Export</span> CSV
              </Button>
            )}
            {canEdit && (
              <Link href="/app/invoices/new" className={buttonClass("primary")}>
                <PlusIcon className="h-4 w-4" /> New invoice
              </Link>
            )}
          </>
        }
      />
      {exportError && (
        <div className="mb-3">
          <ErrorBox error={exportError} />
        </div>
      )}

      <Tabs
        className="mb-4"
        label="Filter by status"
        items={TABS}
        value={status as InvoiceStatus | ""}
        onChange={(v) => {
          setStatus(v);
          setPage(1);
        }}
      />

      <SearchField
        id="inv-search"
        label="Search invoices"
        className="mb-4 max-w-md"
        placeholder="Search by number or client"
        value={q}
        onChange={(v) => {
          setQ(v);
          setPage(1);
        }}
      />

      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <div>
          {data.items.length === 0 ? (
            <EmptyState
              icon={<InvoiceIcon width={24} height={24} />}
              title={dq || status ? "No matching invoices" : "No invoices yet"}
              text={dq || status ? "Nothing matches. Try another status tab or clear the search." : "Create your first invoice in under a minute."}
              action={
                !dq && !status && canEdit ? (
                  <Link href="/app/invoices/new" className={buttonClass("primary")}>
                    Create invoice
                  </Link>
                ) : undefined
              }
            />
          ) : (
            <>
              <InvoiceList invoices={data.items} baseCurrency={business?.currency} busy={loading} />
              <Pager page={data.page} pageSize={data.pageSize || PAGE_SIZE} total={data.total} onPage={setPage} />
            </>
          )}
        </div>
      )}
    </div>
  );
}
