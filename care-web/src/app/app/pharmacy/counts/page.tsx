"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { StockBatchDto, StockCountResultDto, StockItemDto } from "@/lib/types/lab-pharmacy";
import { Badge, Button, Card, ErrorBox, Input, NoAccess, Notice, PageHeader, PageLoader, Select } from "@/components/ui";

/**
 * Wave 4: a stock count (`POST /api/care/pharmacy/counts`): pharmacists and leadership. Choose an item, enter what is on the shelf for each
 * batch and say why; every difference becomes an adjustment with that reason, and the count is audited. Expired stock is counted out here.
 */
export default function StockCountPage() {
  const { access } = useCare();
  const ok = access.canManageStock;
  const items = useApi<StockItemDto[]>(ok ? "/api/care/pharmacy/items" : null);
  const [itemId, setItemId] = useState("");
  const batches = useApi<StockBatchDto[]>(ok && itemId ? `/api/care/pharmacy/items/${itemId}/batches` : null);
  const [counted, setCounted] = useState<Record<string, string>>({});
  const [reason, setReason] = useState("");
  const [result, setResult] = useState<StockCountResultDto | null>(null);
  const { busy, error, run } = useAction();
  if (!ok) return <NoAccess text="Stock is counted by pharmacists and the clinic's leadership." />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    const lines = (batches.data ?? [])
      .filter((b) => counted[b.id] !== undefined && counted[b.id].trim() !== "")
      .map((b) => ({ batchId: b.id, counted: Number(counted[b.id]) }));
    const res = await run(() => api<StockCountResultDto>("/api/care/pharmacy/counts", { method: "POST", body: { reason: reason.trim(), lines } }));
    if (res.ok) {
      setResult(res.data);
      setCounted({});
      batches.reload();
      items.reload();
    }
  }

  return (
    <div className="space-y-5">
      <PageHeader title="Stock count" subtitle="Enter what is on the shelf. Each difference is recorded as an adjustment with your reason." />
      {result && (
        <Notice tone="success">{result.adjusted === 0 ? "Every count matched. Nothing was adjusted." : `${result.adjusted} batch${result.adjusted === 1 ? "" : "es"} adjusted.`}</Notice>
      )}
      <Select
        label="Item"
        value={itemId}
        onChange={(e) => {
          setItemId(e.target.value);
          setCounted({});
          setResult(null);
        }}
      >
        <option value="">Choose an item to count</option>
        {(items.data ?? []).map((i) => (
          <option key={i.id} value={i.id}>
            {i.name} ({i.onHand + i.expiredOnHand} {i.unit} recorded)
          </option>
        ))}
      </Select>
      {itemId && !batches.data && <PageLoader />}
      {batches.data && batches.data.length === 0 && <p className="text-sm text-muted">No batch of this item holds stock.</p>}
      {batches.data && batches.data.length > 0 && (
        <Card>
          <form onSubmit={submit} className="space-y-4" noValidate>
            {batches.data.map((b, i) => (
              <div key={b.id} className="grid items-end gap-3 sm:grid-cols-[minmax(0,1fr)_9rem]">
                <div className="text-sm">
                  <p className="font-bold text-ink">
                    Batch {b.batchNumber} {b.expired && <Badge tone="arterial">Expired</Badge>}
                  </p>
                  <p className="text-muted">
                    Expires {formatDay(b.expiresOn)}. Recorded: {b.onHand} {b.unit}
                  </p>
                </div>
                <Input
                  label="Counted"
                  inputMode="numeric"
                  value={counted[b.id] ?? ""}
                  placeholder={String(b.onHand)}
                  onChange={(e) => setCounted((c) => ({ ...c, [b.id]: e.target.value }))}
                  error={error?.field(`lines[${i}].counted`)}
                  className="tnum"
                />
              </div>
            ))}
            <Input label="Reason" required value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} placeholder="e.g. Monthly count; expired stock destroyed" />
            <ErrorBox error={error && !error.field("reason") ? error : null} />
            <div className="flex justify-end">
              <Button type="submit" loading={busy} disabled={access.readOnly}>
                Save count
              </Button>
            </div>
          </form>
        </Card>
      )}
    </div>
  );
}
