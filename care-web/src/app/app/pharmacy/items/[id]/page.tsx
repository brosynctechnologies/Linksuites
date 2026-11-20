"use client";

import { useParams } from "next/navigation";
import { useCare } from "@/lib/care-me";
import { formatDay, formatStamp, money } from "@/lib/format";
import { MOVEMENT_KINDS } from "@/lib/lab-pharmacy";
import { qs, useApi } from "@/lib/use-api";
import type { StockBatchDto, StockItemDto, StockMovementDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, Badge, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, SectionTitle, caseRowClass } from "@/components/ui";

/** Wave 4: one stock item: its batches (first expiry first) and its movements over the last 30 days. Pharmacists and leadership. */
export default function StockItemPage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const ok = access.canManageStock;
  const item = useApi<StockItemDto>(ok ? `/api/care/pharmacy/items/${id}` : null);
  const batches = useApi<StockBatchDto[]>(ok ? `/api/care/pharmacy/items/${id}/batches` : null);
  const movements = useApi<StockMovementDto[]>(ok ? `/api/care/pharmacy/movements${qs({ itemId: id })}` : null);
  if (!ok) return <NoAccess text="Stock is managed by pharmacists and the clinic's leadership." />;
  if (item.error) return <ApiErrorView error={item.error} onRetry={item.reload} />;
  if (!item.data) return <PageLoader />;
  const i = item.data;

  return (
    <div className="space-y-6">
      <PageHeader
        back="/app/pharmacy"
        title={i.name}
        subtitle={`${i.onHand} ${i.unit} usable, reorder at ${i.reorderLevel}${i.priceItemName ? `. Sold as ${i.priceItemName}` : ""}.`}
      />
      <section className="space-y-3">
        <SectionTitle>Batches</SectionTitle>
        {batches.data && batches.data.length === 0 && <EmptyState title="No stock" text="Receive stock to add a batch." />}
        {batches.data && batches.data.length > 0 && (
          <CaseSheet label="Batches" template="minmax(0,1fr) 9rem 7rem 7rem" head={[{ label: "Batch" }, { label: "Expires" }, { label: "On hand", align: "right" }, { label: "Unit cost", align: "right" }]}>
            {batches.data.map((b) => (
              <li key={b.id} className={caseRowClass}>
                <span className="font-bold text-ink">{b.batchNumber}</span>
                <span className="text-sm">
                  {formatDay(b.expiresOn)}{" "}
                  {b.expired ? <Badge tone="arterial">Expired</Badge> : b.daysToExpiry <= 90 && <Badge tone="iodine">{b.daysToExpiry} days</Badge>}
                </span>
                <span className="tnum text-right text-sm font-bold">{b.onHand}</span>
                <span className="tnum text-right text-sm">{money(b.unitCostMinor)}</span>
              </li>
            ))}
          </CaseSheet>
        )}
      </section>
      <section className="space-y-3">
        <SectionTitle>Movements, last 30 days</SectionTitle>
        {movements.data && movements.data.length === 0 && <p className="text-sm text-muted">No movements.</p>}
        {movements.data && movements.data.length > 0 && (
          <CaseSheet
            label="Movements"
            template="10rem 9rem minmax(0,1fr) 6rem 6rem"
            head={[{ label: "When" }, { label: "What" }, { label: "Batch and reason" }, { label: "Change", align: "right" }, { label: "Left", align: "right" }]}
          >
            {movements.data.map((m) => (
              <li key={m.id} className={caseRowClass}>
                <span className="text-sm">{formatStamp(m.at)}</span>
                <span className="text-sm">
                  {MOVEMENT_KINDS[m.kind]}
                  <span className="block text-xs text-muted">{m.byName}</span>
                </span>
                <span className="text-sm">
                  {m.batchNumber}
                  {m.reason && <span className="block text-xs text-muted">{m.reason}</span>}
                </span>
                <span className="tnum text-right text-sm font-bold">{m.quantity > 0 ? `+${m.quantity}` : m.quantity}</span>
                <span className="tnum text-right text-sm">{m.balanceAfter}</span>
              </li>
            ))}
          </CaseSheet>
        )}
      </section>
    </div>
  );
}
