"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatDay, lagosToday, money, toKobo } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { StockItemDto, StockReceiptDto } from "@/lib/types/lab-pharmacy";
import { Button, Card, CaseSheet, ErrorBox, Input, NoAccess, Notice, PageHeader, SectionTitle, Select, caseRowClass } from "@/components/ui";

interface Line {
  itemId: string;
  batchNumber: string;
  expiresOn: string;
  quantity: string;
  unitCost: string;
}
const blank: Line = { itemId: "", batchNumber: "", expiresOn: "", quantity: "", unitCost: "" };

/**
 * Wave 4: a purchase receipt (`POST /api/care/pharmacy/receipts`): pharmacists and leadership. Each line is a new batch with its batch
 * number, expiry date, quantity and unit cost. Recent receipts are listed below.
 */
export default function ReceiveStockPage() {
  const { access } = useCare();
  const ok = access.canManageStock;
  const items = useApi<StockItemDto[]>(ok ? "/api/care/pharmacy/items?active=true" : null);
  const receipts = useApi<StockReceiptDto[]>(ok ? "/api/care/pharmacy/receipts" : null);
  const [supplier, setSupplier] = useState("");
  const [reference, setReference] = useState("");
  const [receivedOn, setReceivedOn] = useState(lagosToday());
  const [lines, setLines] = useState<Line[]>([{ ...blank }]);
  const [saved, setSaved] = useState<StockReceiptDto | null>(null);
  const { busy, error, run } = useAction();
  if (!ok) return <NoAccess text="Stock is received by pharmacists and the clinic's leadership." />;

  const setLine = (i: number, patch: Partial<Line>) => setLines((list) => list.map((l, j) => (j === i ? { ...l, ...patch } : l)));

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body = {
      supplier: supplier.trim() || null,
      reference: reference.trim() || null,
      receivedOn,
      lines: lines.map((l) => ({
        itemId: l.itemId || null,
        batchNumber: l.batchNumber.trim(),
        expiresOn: l.expiresOn || null,
        quantity: Number(l.quantity) || null,
        unitCostMinor: l.unitCost.trim() ? toKobo(l.unitCost) : 0,
      })),
    };
    const res = await run(() => api<StockReceiptDto>("/api/care/pharmacy/receipts", { method: "POST", body }));
    if (res.ok) {
      setSaved(res.data);
      setLines([{ ...blank }]);
      setReference("");
      receipts.reload();
    }
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Receive stock" subtitle="Record a delivery from a supplier. Each line becomes a batch with its own expiry date." />
      {saved && <Notice tone="success">Received {saved.batches.length} batches, {money(saved.totalCostMinor)} at cost.</Notice>}
      {access.readOnly ? (
        <Notice>The support view can&apos;t record deliveries.</Notice>
      ) : (
        <Card>
          <form onSubmit={submit} className="space-y-4" noValidate>
            <div className="grid gap-3 sm:grid-cols-3">
              <Input label="Supplier" value={supplier} onChange={(e) => setSupplier(e.target.value)} error={error?.field("supplier")} />
              <Input label="Invoice or delivery note" value={reference} onChange={(e) => setReference(e.target.value)} error={error?.field("reference")} />
              <Input label="Received on" type="date" value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} error={error?.field("receivedOn")} />
            </div>
            {lines.map((l, i) => (
              <div key={i} className="grid gap-2 rounded-control border border-rule p-3 sm:grid-cols-2 lg:grid-cols-[minmax(0,1fr)_8rem_10rem_6rem_7rem_auto] lg:items-end">
                <Select label="Item" value={l.itemId} onChange={(e) => setLine(i, { itemId: e.target.value })} error={error?.field(`lines[${i}].itemId`)}>
                  <option value="">Choose an item</option>
                  {(items.data ?? []).map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </Select>
                <Input label="Batch" value={l.batchNumber} onChange={(e) => setLine(i, { batchNumber: e.target.value })} error={error?.field(`lines[${i}].batchNumber`)} />
                <Input label="Expires" type="date" value={l.expiresOn} onChange={(e) => setLine(i, { expiresOn: e.target.value })} error={error?.field(`lines[${i}].expiresOn`)} />
                <Input label="Quantity" inputMode="numeric" value={l.quantity} onChange={(e) => setLine(i, { quantity: e.target.value })} error={error?.field(`lines[${i}].quantity`)} className="tnum" />
                <Input label="Unit cost (₦)" inputMode="decimal" value={l.unitCost} onChange={(e) => setLine(i, { unitCost: e.target.value })} error={error?.field(`lines[${i}].unitCostMinor`)} className="tnum" />
                <Button variant="ghost" disabled={lines.length === 1} onClick={() => setLines((list) => list.filter((_, j) => j !== i))}>
                  Remove
                </Button>
              </div>
            ))}
            <ErrorBox error={error} />
            <div className="flex flex-wrap justify-between gap-2">
              <Button variant="secondary" onClick={() => setLines((list) => [...list, { ...blank }])}>
                Add a line
              </Button>
              <Button type="submit" loading={busy}>
                Receive stock
              </Button>
            </div>
          </form>
        </Card>
      )}

      <section className="space-y-3">
        <SectionTitle>Recent receipts</SectionTitle>
        {receipts.data && receipts.data.length === 0 && <p className="text-sm text-muted">No deliveries recorded yet.</p>}
        {receipts.data && receipts.data.length > 0 && (
          <CaseSheet label="Receipts" template="8rem minmax(0,1fr) minmax(0,1fr) 8rem" head={[{ label: "Received" }, { label: "Supplier" }, { label: "Batches" }, { label: "Cost", align: "right" }]}>
            {receipts.data.map((r) => (
              <li key={r.id} className={caseRowClass}>
                <span className="text-sm">{formatDay(r.receivedOn)}</span>
                <span className="text-sm">
                  {r.supplier ?? "No supplier"}
                  {r.reference && <span className="block text-xs text-muted">{r.reference}</span>}
                </span>
                <span className="text-sm">{r.batches.map((b) => `${b.itemName} (${b.batchNumber}) × ${b.received}`).join(", ")}</span>
                <span className="tnum text-right text-sm font-bold">{money(r.totalCostMinor)}</span>
              </li>
            ))}
          </CaseSheet>
        )}
      </section>
    </div>
  );
}
