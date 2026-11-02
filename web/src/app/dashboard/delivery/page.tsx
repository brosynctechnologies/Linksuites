"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { DeliveryZoneDto, SaveDeliveryZone, StoreDto } from "@/lib/types";
import { ApiErrorView, Badge, Button, EmptyState, Input, PageHeader, PageLoader, Panel, Toggle, cx, listClass } from "@/components/ui";
import { TrashIcon, TruckIcon } from "@/components/icons";

interface Draft {
  id?: string;
  name: string;
  fee: string;
  etaText: string;
  isActive: boolean;
}
const EMPTY: Draft = { name: "", fee: "", etaText: "", isActive: true };

export default function DeliveryPage() {
  const { data, error, loading, reload, setData } = useApi<DeliveryZoneDto[]>("/api/delivery-zones");
  const { data: store } = useApi<StoreDto>("/api/store");
  const currency = store?.currency ?? "NGN";
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<ApiError | null>(null);

  async function save(e: FormEvent) {
    e.preventDefault();
    const feeMinor = toMinor(draft.fee) ?? 0;
    const body: SaveDeliveryZone = {
      name: draft.name.trim(),
      feeMinor,
      etaText: draft.etaText.trim() || null,
      isActive: draft.isActive,
    };
    setBusy(true);
    setFormError(null);
    try {
      const saved = draft.id
        ? await api<DeliveryZoneDto>(`/api/delivery-zones/${draft.id}`, { method: "PUT", body })
        : await api<DeliveryZoneDto>("/api/delivery-zones", { method: "POST", body });
      setData((list = []) => (draft.id ? list.map((z) => (z.id === saved.id ? saved : z)) : [...list, saved]));
      setDraft(EMPTY);
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove(z: DeliveryZoneDto) {
    if (!window.confirm(`Delete zone “${z.name}”?`)) return;
    try {
      await api(`/api/delivery-zones/${z.id}`, { method: "DELETE" });
      setData((list = []) => list.filter((x) => x.id !== z.id));
      if (draft.id === z.id) setDraft(EMPTY);
    } catch (err) {
      setFormError(toApiError(err));
    }
  }

  return (
    <div>
      <PageHeader title="Delivery zones" back="/dashboard/more" subtitle="Areas you deliver to and what each costs." />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        <Panel className="lg:sticky lg:top-8 lg:col-start-2">
          <form onSubmit={save} className="space-y-4">
            <h2 className="font-display text-lg font-bold text-ink">{draft.id ? "Edit zone" : "Add zone"}</h2>
            <Input label="Zone name" required value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} placeholder="e.g. Lekki / Ajah" />
            <div className="grid grid-cols-2 gap-3">
              <Input label="Delivery fee" inputMode="decimal" value={draft.fee} onChange={(e) => setDraft({ ...draft, fee: e.target.value })} placeholder="0" />
              <Input label="Delivery time" value={draft.etaText} onChange={(e) => setDraft({ ...draft, etaText: e.target.value })} placeholder="1–2 days" />
            </div>
            <Toggle label="Active" checked={draft.isActive} onChange={(v) => setDraft({ ...draft, isActive: v })} />
            <ApiErrorView error={formError} />
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={busy} disabled={!draft.name.trim()}>
                {draft.id ? "Save zone" : "Add zone"}
              </Button>
              {draft.id && (
                <Button variant="ghost" onClick={() => setDraft(EMPTY)}>
                  Cancel
                </Button>
              )}
            </div>
          </form>
        </Panel>

        <div className="min-w-0 lg:col-start-1 lg:row-start-1">
          {error && <ApiErrorView error={error} onRetry={reload} />}
          {!data && loading && <PageLoader label="Loading delivery zones…" rows={3} />}
          {data &&
            (data.length === 0 ? (
              <EmptyState
                icon={<TruckIcon />}
                title="No delivery zones"
                text="Without zones, customers can still choose delivery and you agree the fee on WhatsApp. Add a zone to charge a set fee."
              />
            ) : (
              <div className={listClass}>
                <div className="hidden grid-cols-[minmax(0,1fr)_7rem_8rem] gap-3 border-b border-line py-2.5 pr-[4.75rem] pl-4 text-xs font-medium text-subtle sm:grid" aria-hidden="true">
                  <span>Zone</span>
                  <span className="text-right">Fee</span>
                  <span>Delivery time</span>
                </div>
                <ul className="divide-y divide-line">
                  {data.map((z) => (
                    <li key={z.id} className={cx("flex items-center gap-2 pr-2", draft.id === z.id && "bg-marigold-soft/60")}>
                      <button
                        type="button"
                        className="grid min-h-14 min-w-0 flex-1 grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 px-4 py-3 text-left focus-visible:-outline-offset-3 sm:grid-cols-[minmax(0,1fr)_7rem_8rem]"
                        onClick={() => setDraft({ id: z.id, name: z.name, fee: toMajorInput(z.feeMinor), etaText: z.etaText ?? "", isActive: z.isActive })}
                        aria-label={`Edit ${z.name}`}
                      >
                        <span className="flex min-w-0 flex-wrap items-center gap-2">
                          <span className="truncate font-medium text-ink">{z.name}</span>
                          {!z.isActive && <Badge tone="outline">Inactive</Badge>}
                        </span>
                        <span className="text-right font-semibold text-ink num">
                          {z.feeMinor === 0 ? "Free" : formatMoney(z.feeMinor, currency)}
                        </span>
                        <span className="col-span-2 text-xs text-subtle sm:col-span-1 sm:text-sm sm:text-ink-2">{z.etaText || "No time set"}</span>
                      </button>
                      <button
                        type="button"
                        onClick={() => remove(z)}
                        className="flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-hibiscus-deep hover:bg-hibiscus-soft"
                        aria-label={`Delete ${z.name}`}
                      >
                        <TrashIcon />
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
        </div>
      </div>
    </div>
  );
}
