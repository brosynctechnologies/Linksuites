"use client";

import Link from "next/link";
import { useState } from "react";
import { useApi } from "@/lib/use-api";
import { useCan } from "@/lib/permissions";
import { useDebounced } from "@/lib/use-debounced";
import type { Paged, QuoteStatus, QuoteSummaryDto } from "@/lib/types";
import { QuoteList } from "@/components/doc-rows";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, Pager, SearchField, Tabs, buttonClass } from "@/components/ui";
import { PlusIcon, QuoteIcon } from "@/components/icons";

const TABS: { value: QuoteStatus | ""; label: string }[] = [
  { value: "", label: "All" },
  { value: "Draft", label: "Draft" },
  { value: "Sent", label: "Sent" },
  { value: "Accepted", label: "Accepted" },
  { value: "Declined", label: "Declined" },
  { value: "Converted", label: "Converted" },
  { value: "Expired", label: "Expired" },
];
const PAGE_SIZE = 20;

export default function QuotesPage() {
  const [status, setStatus] = useState("");
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const canEdit = useCan("docs.edit");
  const dq = useDebounced(q.trim());

  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  if (status) params.set("status", status);
  if (dq) params.set("q", dq);
  const { data, error, loading, reload } = useApi<Paged<QuoteSummaryDto>>(`/api/quotes?${params}`);

  return (
    <div>
      <PageHeader
        title="Quotes"
        back="/app/more"
        action={
          canEdit && (
            <Link href="/app/quotes/new" className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> New quote
            </Link>
          )
        }
      />
      <Tabs
        className="mb-4"
        label="Filter by status"
        items={TABS}
        value={status as QuoteStatus | ""}
        onChange={(v) => {
          setStatus(v);
          setPage(1);
        }}
      />
      <SearchField
        id="quote-search"
        label="Search quotes"
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
              icon={<QuoteIcon width={24} height={24} />}
              title={dq || status ? "No matching quotes" : "No quotes yet"}
              text={dq || status ? "Nothing matches. Try another status tab or clear the search." : "Send a quote, let your client accept it online, then turn it into an invoice in one tap."}
              action={
                !dq && !status && canEdit ? (
                  <Link href="/app/quotes/new" className={buttonClass("primary")}>
                    Create quote
                  </Link>
                ) : undefined
              }
            />
          ) : (
            <>
              <QuoteList quotes={data.items} busy={loading} />
              <Pager page={data.page} pageSize={data.pageSize || PAGE_SIZE} total={data.total} onPage={setPage} />
            </>
          )}
        </div>
      )}
    </div>
  );
}
