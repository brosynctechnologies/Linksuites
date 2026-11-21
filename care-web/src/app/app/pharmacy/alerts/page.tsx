"use client";

import Link from "next/link";
import { useCare } from "@/lib/care-me";
import { formatDay } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { StockAlertsDto, StockBatchDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, SectionTitle, caseRowClass } from "@/components/ui";

/**
 * Wave 4: the pharmacy's alerts now (`GET /api/care/pharmacy/alerts`): items at or below their reorder level, batches expiring within 90
 * days and expired stock still on the shelf. Pharmacists and the medical director are also told in their notifications.
 */
export default function PharmacyAlertsPage() {
  const { access } = useCare();
  const alerts = useApi<StockAlertsDto>(access.canManageStock ? "/api/care/pharmacy/alerts" : null);
  if (!access.canManageStock) return <NoAccess text="Stock alerts are for pharmacists and the clinic's leadership." />;
  if (alerts.error) return <ApiErrorView error={alerts.error} onRetry={alerts.reload} />;
  if (!alerts.data) return <PageLoader />;
  const a = alerts.data;

  return (
    <div className="space-y-6">
      <PageHeader title="Stock alerts" subtitle="Low stock to reorder and batches to use soon or count out." />
      {a.lowStock.length + a.nearExpiry.length + a.expired.length === 0 && <EmptyState title="Nothing needs attention" />}
      {a.lowStock.length > 0 && (
        <section className="space-y-3">
          <SectionTitle>Low stock</SectionTitle>
          <CaseSheet label="Low stock" template="minmax(0,1fr) 8rem 8rem" head={[{ label: "Item" }, { label: "Usable", align: "right" }, { label: "Reorder at", align: "right" }]}>
            {a.lowStock.map((i) => (
              <li key={i.id} className={caseRowClass}>
                <Link href={`/app/pharmacy/items/${i.id}`} className="font-bold text-scrub hover:underline">
                  {i.name}
                </Link>
                <span className="tnum text-right text-sm font-bold text-arterial">
                  {i.onHand} {i.unit}
                </span>
                <span className="tnum text-right text-sm">{i.reorderLevel}</span>
              </li>
            ))}
          </CaseSheet>
        </section>
      )}
      {a.nearExpiry.length > 0 && <Batches title={`Expiring within ${a.days} days`} batches={a.nearExpiry} />}
      {a.expired.length > 0 && <Batches title="Expired, still on the shelf" batches={a.expired} note="Count these out on the stock count page, with the reason." />}
    </div>
  );
}

function Batches({ title, batches, note }: { title: string; batches: StockBatchDto[]; note?: string }) {
  return (
    <section className="space-y-3">
      <SectionTitle>{title}</SectionTitle>
      {note && <p className="text-sm text-muted">{note}</p>}
      <CaseSheet label={title} template="minmax(0,1fr) 8rem 9rem 7rem" head={[{ label: "Item" }, { label: "Batch" }, { label: "Expires" }, { label: "On hand", align: "right" }]}>
        {batches.map((b) => (
          <li key={b.id} className={caseRowClass}>
            <span className="font-bold text-ink">{b.itemName}</span>
            <span className="text-sm">{b.batchNumber}</span>
            <span className="text-sm">
              {formatDay(b.expiresOn)}
              <span className="block text-xs text-muted">{b.expired ? "Expired" : `${b.daysToExpiry} days left`}</span>
            </span>
            <span className="tnum text-right text-sm font-bold">
              {b.onHand} {b.unit}
            </span>
          </li>
        ))}
      </CaseSheet>
    </section>
  );
}
