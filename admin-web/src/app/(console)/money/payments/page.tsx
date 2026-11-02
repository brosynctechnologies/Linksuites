"use client";

import Link from "next/link";
import { useState } from "react";
import { RequireArea } from "@/components/console";
import { FilterBar, FilterInput, FilterSelect } from "@/components/filters";
import { DownloadIcon } from "@/components/icons";
import { useToast } from "@/components/toast";
import { Button, EmptyState, ErrorPanel, Loading, Mono, PageHeader, Pager, Panel, ProductChip, StatusBadge, TableScroll } from "@/components/ui";
import { apiBlob, saveBlob, toApiError, type ApiError } from "@/lib/api";
import { lagosDayBound, money, stamp, todayLagos } from "@/lib/format";
import { PRODUCTS } from "@/lib/products";
import type { Paged, PaymentRow } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const PAGE_SIZE = 50;
const KEYS = ["product", "status", "from", "to"] as const;

export default function PaymentsPage() {
  return (
    <RequireArea area="money">
      <Payments />
    </RequireArea>
  );
}

function Payments() {
  const toast = useToast();
  const f = useUrlFilters(KEYS);
  const { product, status, from, to } = f.values;
  const filter = { product, status, from: from ? lagosDayBound(from, "start") : "", to: to ? lagosDayBound(to, "end") : "" };
  const { data, error, loading, reload } = useApi<Paged<PaymentRow>>(`/api/platform/payments${qs({ ...filter, page: f.page, pageSize: PAGE_SIZE })}`);
  const [downloading, setDownloading] = useState(false);
  const [csvError, setCsvError] = useState<ApiError | null>(null);

  async function downloadCsv() {
    setDownloading(true);
    setCsvError(null);
    try {
      const blob = await apiBlob(`/api/platform/payments.csv${qs(filter)}`, { accept: "text/csv" });
      saveBlob(blob, `payments-${from || "all"}-to-${to || todayLagos()}.csv`);
      toast("CSV downloaded");
    } catch (e) {
      setCsvError(toApiError(e));
    } finally {
      setDownloading(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Payments"
        subtitle="Every payment through the platform: plan subscriptions, course sales, short-let stays and boosts. Collected is money actually received, less refunds, for every source; unpaid rows collect nothing."
        action={
          <Button variant="secondary" onClick={downloadCsv} loading={downloading}>
            <DownloadIcon className="h-4 w-4" /> Download CSV
          </Button>
        }
      />
      <FilterBar onClear={f.clear} active={f.active}>
        <FilterSelect
          label="Product"
          value={product}
          onChange={(v) => f.set({ product: v })}
          options={[{ value: "", label: "All products" }, ...PRODUCTS.map((p) => ({ value: p.key, label: p.name }))]}
        />
        <FilterInput label="Status" value={status} onCommit={(v) => f.set({ status: v })} placeholder="e.g. Succeeded" className="basis-36" />
        <FilterInput label="From" type="date" value={from} onCommit={(v) => f.set({ from: v })} />
        <FilterInput label="To" type="date" value={to} onCommit={(v) => f.set({ to: v })} />
      </FilterBar>
      {csvError && <ErrorPanel error={csvError} className="mb-3" />}

      <Panel flush>
        {error && <ErrorPanel error={error} onRetry={reload} className="m-3" />}
        {!data && !error && <Loading />}
        {data && data.items.length === 0 && (
          <EmptyState
            title={f.active ? "No payments match these filters" : "No payments yet"}
            text="Filter by product, an exact status word (as shown in the Status column), or a date range in Lagos time."
          />
        )}
        {data && data.items.length > 0 && (
          <>
            <TableScroll label="Payments">
              <table className="data-table min-w-[1120px]" aria-busy={loading || undefined}>
                <thead>
                  <tr>
                    <th>Created</th>
                    <th>Kind</th>
                    <th>Product</th>
                    <th>Business</th>
                    <th>Status</th>
                    <th>Reference</th>
                    <th>Paid</th>
                    <th className="num">Amount</th>
                    <th className="num">Refunded</th>
                    <th className="num">Collected</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((p) => (
                    <tr key={`${p.kind}-${p.id}`}>
                      <td className="tnum">{stamp(p.createdAt)}</td>
                      <td>{p.kind}</td>
                      <td>{p.product ? <ProductChip product={p.product} /> : "—"}</td>
                      <td className="max-w-56">
                        <Link href={`/businesses/${p.businessId}`} className="underline decoration-line underline-offset-2 hover:decoration-graphite">
                          {p.businessName || "(unnamed)"}
                        </Link>
                      </td>
                      <td>
                        <StatusBadge status={p.status} />
                      </td>
                      <td>{p.reference ? <Mono>{p.reference}</Mono> : "—"}</td>
                      <td className="tnum">{stamp(p.paidAt)}</td>
                      <td className="num">{money(p.amountMinor, p.currency)}</td>
                      <td className="num">{p.refundedMinor > 0 ? money(-p.refundedMinor, p.currency) : "—"}</td>
                      <td className="num font-medium">{money(p.collectedMinor, p.currency)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableScroll>
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => f.set({ page: p })} />
          </>
        )}
      </Panel>
    </>
  );
}
