"use client";

import { useState } from "react";
import { useCare } from "@/lib/care-me";
import { formatDay, formatStamp, lagosToday, money } from "@/lib/format";
import { MOVEMENT_KINDS } from "@/lib/lab-pharmacy";
import { qs, useApi } from "@/lib/use-api";
import type { StockBatchDto, StockItemDto, StockMovementDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, CaseSheet, Input, NoAccess, PageHeader, PageLoader, Select, Tabs, caseRowClass } from "@/components/ui";

type Report = "onHand" | "expiring" | "movements";

/**
 * Wave 4: pharmacy reports for pharmacists and leadership: stock on hand with its value, batches expiring within a window, and stock
 * movements over a period (receipts, dispensing and count adjustments; never a patient).
 */
export default function PharmacyReportsPage() {
  const { access } = useCare();
  const [report, setReport] = useState<Report>("onHand");
  if (!access.canManageStock) return <NoAccess text="Pharmacy reports are for pharmacists and the clinic's leadership." />;
  return (
    <div className="space-y-5">
      <PageHeader title="Pharmacy reports" />
      <Tabs
        label="Report"
        value={report}
        onChange={setReport}
        items={[
          { value: "onHand" as Report, label: "Stock on hand" },
          { value: "expiring" as Report, label: "Expiring" },
          { value: "movements" as Report, label: "Movements" },
        ]}
      />
      {report === "onHand" && <OnHand />}
      {report === "expiring" && <Expiring />}
      {report === "movements" && <Movements />}
    </div>
  );
}

function OnHand() {
  const items = useApi<StockItemDto[]>("/api/care/pharmacy/items?active=true");
  if (items.error) return <ApiErrorView error={items.error} onRetry={items.reload} />;
  if (!items.data) return <PageLoader />;
  const total = items.data.reduce((s, i) => s + i.valueMinor, 0);
  return (
    <CaseSheet label="Stock on hand" template="minmax(0,1fr) 8rem 8rem 9rem" head={[{ label: "Item" }, { label: "Usable", align: "right" }, { label: "Expired", align: "right" }, { label: "Value at cost", align: "right" }]}>
      {items.data.map((i) => (
        <li key={i.id} className={caseRowClass}>
          <span className="font-bold text-ink">{i.name}</span>
          <span className="tnum text-right text-sm">
            {i.onHand} {i.unit}
          </span>
          <span className="tnum text-right text-sm">{i.expiredOnHand}</span>
          <span className="tnum text-right text-sm">{money(i.valueMinor)}</span>
        </li>
      ))}
      <li className={caseRowClass}>
        <span className="font-bold">Total</span>
        <span />
        <span />
        <span className="tnum text-right text-sm font-bold">{money(total)}</span>
      </li>
    </CaseSheet>
  );
}

function Expiring() {
  const [days, setDays] = useState("90");
  const list = useApi<StockBatchDto[]>(`/api/care/pharmacy/expiring${qs({ days })}`);
  return (
    <div className="space-y-3">
      <Select label="Within" value={days} onChange={(e) => setDays(e.target.value)} className="max-w-xs">
        {["30", "60", "90", "180", "365"].map((d) => (
          <option key={d} value={d}>
            {d} days
          </option>
        ))}
      </Select>
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && <p className="text-sm text-muted">No stock expires in that time.</p>}
      {list.data && list.data.length > 0 && (
        <CaseSheet label="Expiring batches" template="minmax(0,1fr) 8rem 9rem 7rem" head={[{ label: "Item" }, { label: "Batch" }, { label: "Expires" }, { label: "On hand", align: "right" }]}>
          {list.data.map((b) => (
            <li key={b.id} className={caseRowClass}>
              <span className="font-bold text-ink">{b.itemName}</span>
              <span className="text-sm">{b.batchNumber}</span>
              <span className="text-sm">
                {formatDay(b.expiresOn)}
                <span className="block text-xs text-muted">{b.expired ? "Expired" : `${b.daysToExpiry} days left`}</span>
              </span>
              <span className="tnum text-right text-sm">
                {b.onHand} {b.unit}
              </span>
            </li>
          ))}
        </CaseSheet>
      )}
    </div>
  );
}

function daysBefore(day: string, n: number): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() - n);
  return d.toISOString().slice(0, 10);
}

function Movements() {
  const [to, setTo] = useState(lagosToday());
  const [from, setFrom] = useState(daysBefore(lagosToday(), 29));
  const list = useApi<StockMovementDto[]>(`/api/care/pharmacy/movements${qs({ from, to })}`);
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-3">
        <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
      </div>
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && <p className="text-sm text-muted">No movements in this period.</p>}
      {list.data && list.data.length > 0 && (
        <CaseSheet
          label="Movements"
          template="10rem minmax(0,1fr) 9rem 6rem 6rem"
          head={[{ label: "When" }, { label: "Item and batch" }, { label: "What" }, { label: "Change", align: "right" }, { label: "Left", align: "right" }]}
        >
          {list.data.map((m) => (
            <li key={m.id} className={caseRowClass}>
              <span className="text-sm">{formatStamp(m.at)}</span>
              <span className="text-sm">
                <span className="font-bold text-ink">{m.itemName}</span> {m.batchNumber}
                {m.reason && <span className="block text-xs text-muted">{m.reason}</span>}
              </span>
              <span className="text-sm">
                {MOVEMENT_KINDS[m.kind]}
                <span className="block text-xs text-muted">{m.byName}</span>
              </span>
              <span className="tnum text-right text-sm font-bold">{m.quantity > 0 ? `+${m.quantity}` : m.quantity}</span>
              <span className="tnum text-right text-sm">{m.balanceAfter}</span>
            </li>
          ))}
        </CaseSheet>
      )}
    </div>
  );
}
