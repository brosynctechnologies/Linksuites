"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { formatMoney, toMajorInput, toMinor } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { DiscountCodeDto, DiscountType, SaveDiscountCode, StoreDto } from "@/lib/types";
import { ApiErrorView, Badge, Button, EmptyState, Input, PageHeader, PageLoader, Panel, Select, Toggle, cx, formatDate, listClass } from "@/components/ui";
import { PercentIcon, TrashIcon } from "@/components/icons";

interface Draft {
  id?: string;
  code: string;
  type: DiscountType;
  value: string;
  minOrder: string;
  expiresAt: string; // yyyy-mm-dd
  maxUses: string;
  isActive: boolean;
}
const EMPTY: Draft = { code: "", type: "Percent", value: "", minOrder: "", expiresAt: "", maxUses: "", isActive: true };

export default function DiscountsPage() {
  const { data, error, loading, reload, setData } = useApi<DiscountCodeDto[]>("/api/discount-codes");
  const { data: store } = useApi<StoreDto>("/api/store");
  const currency = store?.currency ?? "NGN";
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<ApiError | null>(null);

  const describe = (d: DiscountCodeDto) => (d.type === "Percent" ? `${d.value}% off` : `${formatMoney(d.value, currency)} off`);

  async function save(e: FormEvent) {
    e.preventDefault();
    setFormError(null);
    const value = draft.type === "Percent" ? Math.round(Number(draft.value)) : toMinor(draft.value);
    if (!value || value <= 0 || (draft.type === "Percent" && value > 100)) {
      setFormError(new ApiError(400, { detail: draft.type === "Percent" ? "Percent must be between 1 and 100." : "Enter a discount amount." }));
      return;
    }
    const body: SaveDiscountCode = {
      code: draft.code.trim().toUpperCase(),
      type: draft.type,
      value,
      minOrderMinor: toMinor(draft.minOrder) ?? 0, // empty "Minimum order" means no minimum
      // End of the chosen day, local time.
      expiresAt: draft.expiresAt ? new Date(`${draft.expiresAt}T23:59:59`).toISOString() : null,
      maxUses: draft.maxUses ? Number(draft.maxUses) : null,
      isActive: draft.isActive,
    };
    setBusy(true);
    try {
      const saved = draft.id
        ? await api<DiscountCodeDto>(`/api/discount-codes/${draft.id}`, { method: "PUT", body })
        : await api<DiscountCodeDto>("/api/discount-codes", { method: "POST", body });
      setData((list = []) => (draft.id ? list.map((d) => (d.id === saved.id ? saved : d)) : [...list, saved]));
      setDraft(EMPTY);
    } catch (err) {
      setFormError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove(d: DiscountCodeDto) {
    if (!window.confirm(`Delete code ${d.code}?`)) return;
    try {
      await api(`/api/discount-codes/${d.id}`, { method: "DELETE" });
      setData((list = []) => list.filter((x) => x.id !== d.id));
      if (draft.id === d.id) setDraft(EMPTY);
    } catch (err) {
      setFormError(toApiError(err));
    }
  }

  function edit(d: DiscountCodeDto) {
    setDraft({
      id: d.id,
      code: d.code,
      type: d.type,
      value: d.type === "Percent" ? String(d.value) : toMajorInput(d.value),
      minOrder: d.minOrderMinor ? toMajorInput(d.minOrderMinor) : "",
      expiresAt: d.expiresAt ? d.expiresAt.slice(0, 10) : "",
      maxUses: d.maxUses != null ? String(d.maxUses) : "",
      isActive: d.isActive,
    });
  }

  return (
    <div>
      <PageHeader title="Discount codes" back="/dashboard/more" subtitle="Promo codes customers enter at checkout." />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        <Panel className="lg:sticky lg:top-8 lg:col-start-2">
          <form onSubmit={save} className="space-y-4">
            <h2 className="font-display text-lg font-bold text-ink">{draft.id ? "Edit code" : "New code"}</h2>
            <Input
              label="Code"
              required
              value={draft.code}
              onChange={(e) => setDraft({ ...draft, code: e.target.value.toUpperCase().replace(/\s/g, "") })}
              placeholder="e.g. WELCOME10"
            />
            <div className="grid grid-cols-2 gap-3">
              <Select label="Type" value={draft.type} onChange={(e) => setDraft({ ...draft, type: e.target.value as DiscountType })}>
                <option value="Percent">Percent (%)</option>
                <option value="Fixed">Fixed amount</option>
              </Select>
              <Input
                label={draft.type === "Percent" ? "Percent off" : "Amount off"}
                inputMode="decimal"
                required
                value={draft.value}
                onChange={(e) => setDraft({ ...draft, value: e.target.value })}
                placeholder={draft.type === "Percent" ? "10" : "1000"}
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Input label="Minimum order" inputMode="decimal" value={draft.minOrder} onChange={(e) => setDraft({ ...draft, minOrder: e.target.value })} placeholder="Optional" />
              <Input
                label="Max uses"
                inputMode="numeric"
                value={draft.maxUses}
                onChange={(e) => setDraft({ ...draft, maxUses: e.target.value.replace(/\D/g, "") })}
                placeholder="Unlimited"
              />
            </div>
            <Input label="Expires on" type="date" value={draft.expiresAt} onChange={(e) => setDraft({ ...draft, expiresAt: e.target.value })} hint="Optional" />
            <Toggle label="Active" checked={draft.isActive} onChange={(v) => setDraft({ ...draft, isActive: v })} />
            <ApiErrorView error={formError} />
            <div className="flex flex-wrap gap-2">
              <Button type="submit" loading={busy} disabled={!draft.code.trim()}>
                {draft.id ? "Save code" : "Create code"}
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
          {!data && loading && <PageLoader label="Loading discount codes…" rows={3} />}
          {data &&
            (data.length === 0 ? (
              <EmptyState
                icon={<PercentIcon />}
                title="No discount codes"
                text="Create a code with the form, then post it on your WhatsApp status or Instagram to run a promo."
              />
            ) : (
              <ul className={listClass}>
                {data.map((d) => (
                  <li key={d.id} className={cx("flex items-center gap-2 pr-2", draft.id === d.id && "bg-marigold-soft/60")}>
                    <button
                      type="button"
                      className="min-h-14 min-w-0 flex-1 px-4 py-3 text-left focus-visible:-outline-offset-3"
                      onClick={() => edit(d)}
                      aria-label={`Edit ${d.code}`}
                    >
                      <span className="flex flex-wrap items-center gap-2">
                        <span className="font-display text-lg font-bold tracking-wide text-ink">{d.code}</span>
                        {!d.isActive && <Badge tone="outline">Inactive</Badge>}
                      </span>
                      <span className="mt-0.5 flex flex-wrap gap-x-4 gap-y-0.5 text-sm text-ink-2">
                        <span className="font-medium">{describe(d)}</span>
                        {d.minOrderMinor ? <span className="num">Min {formatMoney(d.minOrderMinor, currency)}</span> : null}
                      </span>
                      <span className="mt-0.5 flex flex-wrap gap-x-4 gap-y-0.5 text-xs text-subtle">
                        <span className="num">
                          Used {d.uses}
                          {d.maxUses != null ? ` / ${d.maxUses}` : ""} times
                        </span>
                        {d.expiresAt && <span>Expires {formatDate(d.expiresAt, false)}</span>}
                      </span>
                    </button>
                    <button
                      type="button"
                      onClick={() => remove(d)}
                      className="flex h-11 w-11 shrink-0 items-center justify-center rounded-control text-hibiscus-deep hover:bg-hibiscus-soft"
                      aria-label={`Delete ${d.code}`}
                    >
                      <TrashIcon />
                    </button>
                  </li>
                ))}
              </ul>
            ))}
        </div>
      </div>
    </div>
  );
}
