"use client";

import { useState, type FormEvent } from "react";
import { useApi } from "@/lib/use-api";
import type { OrderStatus, OrderSummaryDto, Paged } from "@/lib/types";
import { OrderRow, OrderTableHead } from "@/components/order-row";
import { ApiErrorView, Button, EmptyState, PageHeader, PageLoader, Spinner, cx, inputClass, listClass } from "@/components/ui";
import { OrdersIcon, SearchIcon } from "@/components/icons";

const TABS: ("All" | OrderStatus)[] = ["All", "New", "Confirmed", "Paid", "Delivered", "Cancelled"];
const PAGE_SIZE = 20;

function initialStatus(): "All" | OrderStatus {
  if (typeof window === "undefined") return "All";
  const s = new URLSearchParams(window.location.search).get("status");
  return TABS.includes(s as OrderStatus) ? (s as OrderStatus) : "All";
}

export default function OrdersPage() {
  const [status, setStatus] = useState<"All" | OrderStatus>(initialStatus);
  const [q, setQ] = useState("");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);

  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  // "Paid" lists every order marked paid, whatever its stage ("Mark paid" sets the payment status, not the stage).
  if (status === "Paid") params.set("paymentStatus", "Paid");
  else if (status !== "All") params.set("status", status);
  if (query) params.set("q", query);
  const { data, error, loading, reload } = useApi<Paged<OrderSummaryDto>>(`/api/orders?${params}`);

  const totalPages = data ? Math.max(1, Math.ceil(data.total / (data.pageSize || PAGE_SIZE))) : 1;

  function search(e: FormEvent) {
    e.preventDefault();
    setPage(1);
    setQuery(q.trim());
  }

  return (
    <div>
      <PageHeader title="Orders" subtitle={data ? `${data.total} order${data.total === 1 ? "" : "s"}` : undefined} />

      <div className="mb-5 space-y-3 lg:flex lg:items-center lg:gap-4 lg:space-y-0">
        <form onSubmit={search} className="relative lg:w-80 lg:shrink-0" role="search">
          <label htmlFor="order-search" className="sr-only">
            Search orders
          </label>
          <SearchIcon className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-subtle" />
          <input
            id="order-search"
            type="search"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            onBlur={() => {
              if (q.trim() !== query) {
                setPage(1);
                setQuery(q.trim());
              }
            }}
            placeholder="Search name, phone or order code"
            className={cx(inputClass, "pl-10")}
          />
        </form>

        <div className="no-scrollbar -mx-4 flex items-center gap-1.5 overflow-x-auto px-4 sm:-mx-6 sm:px-6 lg:mx-0 lg:px-0" role="tablist" aria-label="Filter by status">
          {TABS.map((t) => (
            <button
              key={t}
              type="button"
              role="tab"
              aria-selected={status === t}
              onClick={() => {
                setStatus(t);
                setPage(1);
              }}
              className={cx(
                "min-h-11 shrink-0 rounded-full px-4 text-sm font-medium transition-colors",
                status === t ? "bg-ink text-white" : "text-ink-2 hover:bg-ink-soft hover:text-ink",
              )}
            >
              {t}
            </button>
          ))}
          {loading && data && <Spinner className="ml-1 h-4 w-4 shrink-0 text-subtle" />}
        </div>
      </div>

      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && loading && <PageLoader label="Loading orders…" rows={6} />}
      {data &&
        (data.items.length === 0 ? (
          <EmptyState
            icon={<OrdersIcon />}
            title={query || status !== "All" ? "No matching orders" : "No orders yet"}
            text={
              query || status !== "All"
                ? "Try another status, or search by a different name, phone number or order code."
                : "Orders from your store show up here the moment a customer sends one."
            }
          />
        ) : (
          <>
            <div className={listClass}>
              <OrderTableHead />
              <ul className="divide-y divide-line">
                {data.items.map((o) => (
                  <OrderRow key={o.id} order={o} />
                ))}
              </ul>
            </div>
            {totalPages > 1 && (
              <nav className="mt-4 flex items-center justify-between gap-3" aria-label="Pages">
                <Button variant="secondary" disabled={page <= 1 || loading} onClick={() => setPage((p) => p - 1)}>
                  Previous
                </Button>
                <span className="text-sm text-muted num">
                  Page {page} of {totalPages}
                </span>
                <Button variant="secondary" disabled={page >= totalPages || loading} onClick={() => setPage((p) => p + 1)}>
                  Next
                </Button>
              </nav>
            )}
          </>
        ))}
    </div>
  );
}
