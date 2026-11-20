"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { canManageSettings, useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { InventoryDto, InventoryItemDto, StockAdjustmentDto } from "@/lib/types";
import {
  ApiErrorView,
  Button,
  EmptyState,
  ErrorBox,
  Input,
  Notice,
  PageHeader,
  PageLoader,
  Panel,
  Sheet,
  Spinner,
  cx,
  inputClass,
  listClass,
} from "@/components/ui";
import { SearchIcon, StockIcon } from "@/components/icons";
import { StockStatusBadge, StockTabs, StockUpsell, formatDelta, itemName } from "./stock-ui";

type Filter = "all" | "low";
const QUICK_REASONS = ["New stock arrived", "Damaged", "Count correction", "Returned by customer", "Used or given away"];

const itemKey = (i: { productId: string; variantId: string | null }) => `${i.productId}:${i.variantId ?? ""}`;

export default function InventoryPage() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const filter: Filter = searchParams.get("filter") === "low" ? "low" : "all";
  const query = (searchParams.get("q") ?? "").trim();
  const [q, setQ] = useState(query);
  const readOnly = !!useAuth()?.support;
  const role = useRole();

  const params = new URLSearchParams({ filter });
  if (query) params.set("q", query);
  const { data, error, loading, reload } = useApi<InventoryDto>(`/api/inventory?${params}`);
  const [adjusting, setAdjusting] = useState<InventoryItemDto | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

  function setUrl(next: { filter?: Filter; q?: string }) {
    const sp = new URLSearchParams();
    const f = next.filter ?? filter;
    const text = next.q ?? query;
    if (f === "low") sp.set("filter", "low");
    if (text) sp.set("q", text);
    const s = sp.toString();
    router.replace(s ? `${pathname}?${s}` : pathname, { scroll: false });
  }

  function search(e: FormEvent) {
    e.preventDefault();
    setUrl({ q: q.trim() });
  }

  if (data && !data.enabled) {
    return (
      <div>
        <PageHeader title="Stock" back="/dashboard/more" />
        <StockUpsell />
      </div>
    );
  }

  return (
    <div>
      <PageHeader
        title="Stock"
        back="/dashboard/more"
        subtitle="How many of each item you have. Orders take stock off; cancelled orders put it back."
      />
      <StockTabs current="stock" />

      {banner && (
        <div className="mb-4">
          <Notice tone="success">{banner}</Notice>
        </div>
      )}

      <div className="mb-5 space-y-3 lg:flex lg:items-center lg:gap-4 lg:space-y-0">
        <form onSubmit={search} className="relative lg:w-80 lg:shrink-0" role="search">
          <label htmlFor="stock-search" className="sr-only">
            Search stock
          </label>
          <SearchIcon className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-subtle" />
          <input
            id="stock-search"
            type="search"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            onBlur={() => {
              if (q.trim() !== query) setUrl({ q: q.trim() });
            }}
            placeholder="Search products"
            className={cx(inputClass, "pl-10")}
          />
        </form>
        <div className="flex items-center gap-1.5" role="tablist" aria-label="Filter stock">
          {(
            [
              { value: "all", label: "All" },
              { value: "low", label: "Low stock" },
            ] as const
          ).map((t) => (
            <button
              key={t.value}
              type="button"
              role="tab"
              aria-selected={filter === t.value}
              onClick={() => setUrl({ filter: t.value })}
              className={cx(
                "min-h-11 shrink-0 rounded-full px-4 text-sm font-medium transition-colors",
                filter === t.value ? "bg-ink text-white" : "text-ink-2 hover:bg-ink-soft hover:text-ink",
              )}
            >
              {t.label}
            </button>
          ))}
          {loading && data && <Spinner className="ml-1 h-4 w-4 shrink-0 text-subtle" />}
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_18rem] lg:items-start">
        <div className="min-w-0">
          {error && <ApiErrorView error={error} onRetry={reload} />}
          {!data && loading && <PageLoader label="Loading stock…" rows={5} />}
          {data &&
            (data.items.length === 0 ? (
              <EmptyState
                icon={<StockIcon />}
                title={query || filter === "low" ? "Nothing matches" : "No stock tracked yet"}
                text={
                  query || filter === "low"
                    ? filter === "low"
                      ? "Nothing is running low right now."
                      : "Try another search."
                    : "Open a product and enter how many you have under Track stock. It shows here."
                }
                action={
                  !query && filter === "all" ? (
                    <Link href="/dashboard/products" className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
                      Go to products
                    </Link>
                  ) : undefined
                }
              />
            ) : (
              <div className={listClass}>
                <div
                  className="hidden grid-cols-[minmax(0,1fr)_5rem_7rem_11rem] gap-3 border-b border-line px-4 py-2.5 text-xs font-medium text-subtle sm:grid"
                  aria-hidden="true"
                >
                  <span>Item</span>
                  <span className="text-right">Quantity</span>
                  <span>Status</span>
                  <span />
                </div>
                <ul className="divide-y divide-line">
                  {data.items.map((item) => (
                    <li
                      key={itemKey(item)}
                      className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 px-4 py-3 sm:grid-cols-[minmax(0,1fr)_5rem_7rem_11rem]"
                    >
                      <span className="min-w-0">
                        <Link
                          href={`/dashboard/products/${item.productId}`}
                          className="font-medium break-words text-ink underline-offset-2 hover:underline"
                        >
                          {item.productName}
                        </Link>
                        {item.variantTitle && <span className="block text-sm text-muted">{item.variantTitle}</span>}
                      </span>
                      <span className="row-span-2 text-right font-display text-xl font-bold text-ink num sm:row-span-1">
                        {item.stockQty}
                        <span className="sr-only"> in stock</span>
                      </span>
                      <span>
                        <StockStatusBadge status={item.status} />
                      </span>
                      {!readOnly && (
                        <span className="col-span-2 flex gap-1 sm:col-span-1">
                          <Button variant="secondary" onClick={() => setAdjusting(item)} aria-label={`Adjust ${itemName(item.productName, item.variantTitle)}`}>
                            Adjust
                          </Button>
                          <Link
                            href={`/dashboard/inventory/history?productId=${item.productId}${item.variantId ? `&variantId=${item.variantId}` : ""}`}
                            className="inline-flex min-h-11 items-center rounded-control px-3 text-sm font-medium text-ink-2 hover:bg-ink-soft hover:text-ink"
                          >
                            History<span className="sr-only"> for {itemName(item.productName, item.variantTitle)}</span>
                          </Link>
                        </span>
                      )}
                    </li>
                  ))}
                </ul>
              </div>
            ))}
        </div>

        {data && canManageSettings(role) && !readOnly && (
          <ThresholdForm
            key={data.lowStockThreshold}
            threshold={data.lowStockThreshold}
            onSaved={(n) => {
              setBanner(`Items at ${n} or fewer now count as low stock.`);
              reload();
            }}
          />
        )}
        {data && (!canManageSettings(role) || readOnly) && (
          <p className="text-sm text-muted">Items at {data.lowStockThreshold} or fewer count as low stock.</p>
        )}
      </div>

      <Sheet open={!!adjusting} title="Adjust stock" onClose={() => setAdjusting(null)}>
        {adjusting && (
          <AdjustForm
            key={itemKey(adjusting)}
            item={adjusting}
            onCancel={() => setAdjusting(null)}
            onDone={(adj) => {
              setAdjusting(null);
              setBanner(`${itemName(adj.productName, adj.variantTitle)}: ${formatDelta(adj.delta)}, now ${adj.quantityAfter}.`);
              reload();
            }}
          />
        )}
      </Sheet>
    </div>
  );
}

function ThresholdForm({ threshold, onSaved }: { threshold: number; onSaved: (n: number) => void }) {
  const [value, setValue] = useState(String(threshold));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function save(e: FormEvent) {
    e.preventDefault();
    const n = Number(value);
    if (value.trim() === "" || !Number.isInteger(n) || n < 0 || n > 10000) return setError("Enter a whole number from 0 to 10,000.");
    setBusy(true);
    setError(null);
    try {
      await api("/api/inventory/settings", { method: "PUT", body: { lowStockThreshold: n } });
      onSaved(n);
    } catch (err) {
      setError(toApiError(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel className="lg:sticky lg:top-8">
      <form onSubmit={save} className="space-y-3" noValidate>
        <h2 className="font-display text-lg font-bold text-ink">Low stock alert</h2>
        <Input
          label="Low when at or under"
          inputMode="numeric"
          value={value}
          onChange={(e) => setValue(e.target.value.replace(/[^\d]/g, ""))}
          hint="You get a note when an item reaches this. Customers see “Only 2 left” at or under it."
        />
        {error && (typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />)}
        <Button type="submit" variant="secondary" loading={busy} disabled={value === String(threshold)}>
          Save
        </Button>
      </form>
    </Panel>
  );
}

function AdjustForm({
  item,
  onCancel,
  onDone,
}: {
  item: InventoryItemDto;
  onCancel: () => void;
  onDone: (adj: StockAdjustmentDto) => void;
}) {
  const [direction, setDirection] = useState<"add" | "remove">("add");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);
  const [errors, setErrors] = useState<{ amount?: string; reason?: string }>({});

  const n = Number(amount);
  const valid = amount !== "" && Number.isInteger(n) && n > 0;
  const delta = valid ? (direction === "add" ? n : -n) : 0;
  const after = item.stockQty + delta;

  async function submit(e: FormEvent) {
    e.preventDefault();
    const next: typeof errors = {};
    if (!valid || n > 100000) next.amount = "Enter how many, from 1 to 100,000.";
    else if (after < 0) next.amount = `You have ${item.stockQty}. You can remove up to ${item.stockQty}.`;
    if (!reason.trim()) next.reason = "Give a reason, for example “New stock arrived”.";
    else if (reason.trim().length > 200) next.reason = "Keep the reason under 200 characters.";
    setErrors(next);
    if (next.amount || next.reason) {
      setError("Check the fields below.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const adj = await api<StockAdjustmentDto>("/api/inventory/adjustments", {
        method: "POST",
        body: { productId: item.productId, variantId: item.variantId, delta, reason: reason.trim() },
      });
      onDone(adj);
    } catch (err) {
      const apiErr = toApiError(err);
      setError(apiErr.code === "not_tracked" && !apiErr.detail ? "This item no longer tracks stock. Reload the page." : apiErr);
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      <div>
        <p className="font-medium text-ink">{itemName(item.productName, item.variantTitle)}</p>
        <p className="text-sm text-muted">
          Now <span className="font-semibold text-ink num">{item.stockQty}</span>
        </p>
      </div>
      {error && (typeof error === "string" ? <ErrorBox error={error} /> : <ApiErrorView error={error} />)}
      <fieldset>
        <legend className="mb-1.5 text-sm font-medium text-ink">Change</legend>
        <div className="grid grid-cols-2 gap-2">
          {(
            [
              { value: "add", label: "Add stock" },
              { value: "remove", label: "Take away" },
            ] as const
          ).map((d) => (
            <label
              key={d.value}
              className={cx(
                "flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-control border px-3 text-sm font-semibold has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-marigold",
                direction === d.value ? "border-ink bg-ink text-white" : "border-line-strong text-ink hover:bg-paper",
              )}
            >
              <input type="radio" name="direction" className="sr-only" checked={direction === d.value} onChange={() => setDirection(d.value)} />
              {d.label}
            </label>
          ))}
        </div>
      </fieldset>
      <Input
        label="How many"
        inputMode="numeric"
        required
        autoFocus
        value={amount}
        onChange={(e) => setAmount(e.target.value.replace(/[^\d]/g, ""))}
        error={errors.amount}
        hint={valid ? `${formatDelta(delta)}, quantity after: ${after}` : undefined}
      />
      <div className="space-y-2">
        <Input label="Reason" required maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} error={errors.reason} />
        <div className="flex flex-wrap gap-1.5" aria-label="Quick reasons" role="group">
          {QUICK_REASONS.map((r) => (
            <button
              key={r}
              type="button"
              onClick={() => setReason(r)}
              aria-pressed={reason === r}
              className={cx(
                "min-h-11 rounded-full px-3.5 text-sm font-medium ring-1 ring-inset transition-colors",
                reason === r ? "bg-ink-soft text-ink ring-ink" : "text-ink-2 ring-line-strong hover:text-ink",
              )}
            >
              {r}
            </button>
          ))}
        </div>
      </div>
      <div className="flex justify-end gap-2">
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          Save change
        </Button>
      </div>
    </form>
  );
}
