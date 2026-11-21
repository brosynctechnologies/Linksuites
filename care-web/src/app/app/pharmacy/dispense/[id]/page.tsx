"use client";

import { useParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatDay, formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { DispensePrescriptionDto, StockItemDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, NoAccess, Notice, PageHeader, PageLoader, SectionTitle, Select, Wristband } from "@/components/ui";

/**
 * Wave 4: one prescription opened to dispense it (`GET /api/care/pharmacy/prescriptions/{id}`, a logged read). Each drug with what is left,
 * the stock item it is sold from and the quantity to give now. The server takes stock first-expiry-first-out and says which batch was used.
 */
export default function DispensePage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const rx = useApi<DispensePrescriptionDto>(access.canDispense ? `/api/care/pharmacy/prescriptions/${id}` : null);
  // Pharmacists and leadership see every stock item to pick from; other dispensers use the linked item.
  const stock = useApi<StockItemDto[]>(access.canManageStock ? "/api/care/pharmacy/items?active=true" : null);
  const [qty, setQty] = useState<Record<string, string>>({});
  const [itemFor, setItemFor] = useState<Record<string, string>>({});
  const { busy, error, run } = useAction();
  const [done, setDone] = useState(false);

  if (!access.canDispense) return <NoAccess text="Dispensing is for pharmacists, doctors and nurses." />;
  if (rx.error) return <ApiErrorView error={rx.error} onRetry={rx.reload} />;
  if (!rx.data) return <PageLoader />;
  const p = rx.data;
  const open = p.items.filter((i) => i.remaining === null || i.remaining > 0);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const lines = open
      .map((i) => ({
        prescriptionItemId: i.prescriptionItemId,
        itemId: itemFor[i.prescriptionItemId] || i.stockItemId || null,
        quantity: qty[i.prescriptionItemId] !== undefined ? Number(qty[i.prescriptionItemId]) || 0 : (i.remaining ?? 0),
      }))
      .filter((l) => l.quantity > 0);
    const res = await run(() => api(`/api/care/pharmacy/prescriptions/${p.prescriptionId}/dispense`, { method: "POST", body: { lines } }));
    if (res.ok) {
      setDone(true);
      setQty({});
      rx.reload();
    }
  }

  return (
    <div className="space-y-5">
      <PageHeader
        back="/app/pharmacy/dispense"
        title="Dispense prescription"
        subtitle={
          <span className="flex flex-wrap items-center gap-2">
            <Wristband number={p.patientNumber} />
            <span className="font-bold text-ink">{p.patientName}</span>
            <span>
              {p.doctorName}, {formatStamp(p.writtenAt)}
            </span>
          </span>
        }
      />
      {done && <Notice tone="success">Dispensed. The batches used are listed below.</Notice>}
      {open.length === 0 ? (
        <Notice>Every drug on this prescription has been dispensed.</Notice>
      ) : (
        <Card>
          <form onSubmit={submit} className="space-y-4" noValidate>
            {open.map((i, n) => (
              <div key={i.prescriptionItemId} className="grid gap-3 border-b border-rule pb-4 last:border-0 sm:grid-cols-[minmax(0,1fr)_14rem_7rem] sm:items-end">
                <div className="text-sm">
                  <p className="font-bold text-ink">
                    {i.drug} {i.strength}
                  </p>
                  <p className="text-muted">
                    {i.dose}, {i.frequency}
                    {i.durationDays ? `, ${i.durationDays} days` : ""}
                  </p>
                  <p className="text-muted">
                    {i.quantity !== null ? `Prescribed ${i.quantity}, dispensed ${i.dispensed}` : `No quantity written; dispensed ${i.dispensed}`}
                  </p>
                </div>
                {stock.data ? (
                  <Select
                    label="From stock"
                    value={itemFor[i.prescriptionItemId] ?? i.stockItemId ?? ""}
                    onChange={(e) => setItemFor((m) => ({ ...m, [i.prescriptionItemId]: e.target.value }))}
                    error={error?.field(`lines[${n}].itemId`)}
                  >
                    <option value="">Choose a stock item</option>
                    {stock.data.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name} ({s.onHand} {s.unit})
                      </option>
                    ))}
                  </Select>
                ) : (
                  <p className="text-sm">
                    {i.stockItemName ? (
                      <>
                        {i.stockItemName} <Badge tone={i.stockOnHand ? "pass" : "arterial"}>{i.stockOnHand ?? 0} in stock</Badge>
                      </>
                    ) : (
                      <span className="text-arterial">No stock item is linked to this drug.</span>
                    )}
                  </p>
                )}
                <Input
                  label="Give now"
                  inputMode="numeric"
                  value={qty[i.prescriptionItemId] ?? String(i.remaining ?? "")}
                  onChange={(e) => setQty((m) => ({ ...m, [i.prescriptionItemId]: e.target.value }))}
                  error={error?.field(`lines[${n}].quantity`)}
                  className="tnum"
                />
              </div>
            ))}
            <ErrorBox error={error} />
            <div className="flex justify-end">
              <Button type="submit" loading={busy}>
                Dispense
              </Button>
            </div>
          </form>
        </Card>
      )}

      {p.dispensings.length > 0 && (
        <section className="space-y-3">
          <SectionTitle>Dispensed</SectionTitle>
          {p.dispensings.map((d) => (
            <Card key={d.id} className="space-y-1 text-sm">
              <p className="font-bold text-ink">
                {formatStamp(d.at)} by {d.dispensedByName}
              </p>
              <ul className="space-y-0.5">
                {d.lines.map((l) => (
                  <li key={`${l.batchId}-${l.prescriptionItemId}`}>
                    {l.quantity} × {l.itemName}, batch {l.batchNumber} (expires {formatDay(l.expiresOn)})
                  </li>
                ))}
              </ul>
            </Card>
          ))}
        </section>
      )}
    </div>
  );
}
