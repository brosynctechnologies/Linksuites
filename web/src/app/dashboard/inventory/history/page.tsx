"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { useApi } from "@/lib/use-api";
import type { InventoryDto, Paged, StockAdjustmentDto, StockAdjustmentKind } from "@/lib/types";
import { ApiErrorView, Badge, Button, EmptyState, PageHeader, PageLoader, cx, formatDate, listClass } from "@/components/ui";
import { StockIcon } from "@/components/icons";
import { StockTabs, StockUpsell, formatDelta, itemName } from "../stock-ui";

const PAGE_SIZE = 30;

const KIND_LABEL: Record<StockAdjustmentKind, string> = {
  Sale: "Sale",
  Cancellation: "Cancelled order",
  Adjustment: "Adjustment",
  Count: "Stock count",
};

export default function StockHistoryPage() {
  const searchParams = useSearchParams();
  const productId = searchParams.get("productId");
  const variantId = searchParams.get("variantId");
  const [page, setPage] = useState(1);

  // The plan check: the log endpoint may answer 402 on Free; the inventory summary says it plainly.
  const { data: summary } = useApi<InventoryDto>("/api/inventory?filter=low");
  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  if (productId) params.set("productId", productId);
  if (variantId) params.set("variantId", variantId);
  const { data, error, loading, reload } = useApi<Paged<StockAdjustmentDto>>(summary?.enabled === false ? null : `/api/inventory/adjustments?${params}`);
  const totalPages = data ? Math.max(1, Math.ceil(data.total / (data.pageSize || PAGE_SIZE))) : 1;

  if (summary && !summary.enabled) {
    return (
      <div>
        <PageHeader title="Stock" back="/dashboard/more" />
        <StockUpsell />
      </div>
    );
  }

  const first = data?.items[0];
  const filteredName = productId && first ? itemName(first.productName, variantId ? first.variantTitle : null) : null;

  return (
    <div>
      <PageHeader title="Stock" back="/dashboard/more" subtitle="Every change to your stock: sales, cancelled orders and changes made by hand." />
      <StockTabs current="history" />

      {productId && (
        <p className="mb-4 flex flex-wrap items-center gap-x-3 text-sm text-ink-2">
          <span>
            Showing <span className="font-semibold text-ink">{filteredName ?? "one item"}</span>
          </span>
          <Link href="/dashboard/inventory/history" className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
            Show all items
          </Link>
        </p>
      )}

      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader label="Loading stock history…" rows={6} />}
      {data &&
        (data.items.length === 0 ? (
          <EmptyState icon={<StockIcon />} title="No changes yet" text="Sales, cancelled orders and your own adjustments show here." />
        ) : (
          <>
            <div className={cx(listClass, loading && "opacity-60 transition-opacity")}>
              <div
                className="hidden grid-cols-[8.5rem_minmax(0,1fr)_4.5rem_4.5rem_minmax(0,1fr)] gap-3 border-b border-line px-4 py-2.5 text-xs font-medium text-subtle md:grid"
                aria-hidden="true"
              >
                <span>When</span>
                <span>Item</span>
                <span className="text-right">Change</span>
                <span className="text-right">After</span>
                <span>Reason</span>
              </div>
              <ul className="divide-y divide-line">
                {data.items.map((a) => (
                  <li
                    key={a.id}
                    className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-0.5 px-4 py-3 text-sm md:grid-cols-[8.5rem_minmax(0,1fr)_4.5rem_4.5rem_minmax(0,1fr)] md:items-center"
                  >
                    <span className="order-3 text-xs text-subtle md:order-none md:text-sm md:text-ink-2">{formatDate(a.createdAt)}</span>
                    <span className="order-1 min-w-0 font-medium break-words text-ink md:order-none">{itemName(a.productName, a.variantTitle)}</span>
                    <span
                      className={cx(
                        "order-2 text-right font-display text-base font-bold num md:order-none",
                        a.delta < 0 ? "text-hibiscus-deep" : "text-ink",
                      )}
                    >
                      {formatDelta(a.delta)}
                      <span className="block text-xs font-normal text-subtle md:hidden">now {a.quantityAfter}</span>
                    </span>
                    <span className="hidden text-right text-ink num md:block">{a.quantityAfter}</span>
                    <span className="order-4 col-span-2 min-w-0 text-ink-2 md:order-none md:col-span-1">
                      <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                        <Badge tone={a.kind === "Sale" ? "outline" : a.kind === "Cancellation" ? "neutral" : "ink"}>{KIND_LABEL[a.kind] ?? a.kind}</Badge>
                        {a.reason && a.reason !== KIND_LABEL[a.kind] && <span className="break-words">{a.reason}</span>}
                      </span>
                      <span className="mt-0.5 block text-xs text-subtle">
                        {a.orderId ? (
                          <Link href={`/dashboard/orders/${a.orderId}`} className="-my-3 inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
                            Order #{a.orderNumber ?? ""}
                          </Link>
                        ) : (
                          a.byName && <>By {a.byName}</>
                        )}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            </div>
            {totalPages > 1 && (
              <nav className="mt-4 flex items-center justify-between gap-3" aria-label="Pages">
                <Button variant="secondary" disabled={page <= 1 || loading} onClick={() => setPage((p) => p - 1)}>
                  Newer
                </Button>
                <span className="text-sm text-muted num">
                  Page {page} of {totalPages}
                </span>
                <Button variant="secondary" disabled={page >= totalPages || loading} onClick={() => setPage((p) => p + 1)}>
                  Older
                </Button>
              </nav>
            )}
          </>
        ))}
    </div>
  );
}
