"use client";

import Link from "next/link";
import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { filterHref } from "@/lib/search";
import { useApi } from "@/lib/use-api";
import type { AlertFrequency, ListingFilter, SavedSearchDto } from "@/lib/types";
import { BellIcon, TrashIcon } from "@/components/icons";
import { FREQUENCIES } from "@/components/search/save-search";
import { ApiErrorView, EmptyState, ErrorBox, PageHeader, PageLoader, Segmented, buttonClass, linkClass } from "@/components/ui";

function describe(f: ListingFilter): string {
  const parts: string[] = [];
  parts.push(f.purpose === "Sale" ? "For sale" : f.purpose === "ShortLet" ? "Short-let" : "To rent");
  if (f.location) parts.push(f.location.replace(/-/g, " "));
  if (f.bbox?.length === 4) parts.push("map area");
  if (f.type) parts.push(f.type.replace(/-/g, " "));
  if (f.minBeds) parts.push(`${f.minBeds}+ beds`);
  if (f.minPrice && f.maxPrice) parts.push(`${formatMoney(f.minPrice)} to ${formatMoney(f.maxPrice)}`);
  else if (f.maxPrice) parts.push(`up to ${formatMoney(f.maxPrice)}`);
  else if (f.minPrice) parts.push(`from ${formatMoney(f.minPrice)}`);
  if (f.amenities?.length) parts.push(f.amenities.map((a) => a.replace(/-/g, " ")).join(", "));
  if (f.verifiedOnly) parts.push("verified agents only");
  return parts.join(" · ");
}

export default function SavedSearchesPage() {
  const { data, error, reload, setData } = useApi<SavedSearchDto[]>("/api/me/saved-searches");
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const list = asList(data);

  async function setFrequency(s: SavedSearchDto, frequency: AlertFrequency) {
    setBusy(s.id);
    setActionError(null);
    try {
      const updated = await api<SavedSearchDto>(`/api/me/saved-searches/${s.id}`, { method: "PUT", body: { name: s.name, filter: s.filter, frequency } });
      setData((xs) => asList(xs).map((x) => (x.id === s.id ? updated : x)));
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  async function remove(s: SavedSearchDto) {
    if (!window.confirm(`Delete "${s.name}"? You'll stop getting its emails.`)) return;
    setBusy(s.id);
    setActionError(null);
    try {
      await api(`/api/me/saved-searches/${s.id}`, { method: "DELETE" });
      setData((xs) => asList(xs).filter((x) => x.id !== s.id));
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div>
      <PageHeader title="Saved searches" subtitle="Get an email when a matching home goes live. You can keep up to 20." />
      {actionError && <ErrorBox error={actionError} />}
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<BellIcon />}
          title="No saved searches yet"
          text="Search for homes, then tap Save search to get alerts for new matches."
          action={
            <Link href="/rent" className={buttonClass("primary")}>
              Start a search
            </Link>
          }
        />
      )}
      {list.length > 0 && (
        <ul className="space-y-3">
          {list.map((s) => (
            <li key={s.id} className="rounded-panel border border-line bg-surface p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <Link href={filterHref(s.filter)} className={linkClass}>
                    {s.name}
                  </Link>
                  <p className="mt-0.5 text-sm text-slate-700 first-letter:uppercase">{describe(s.filter)}</p>
                  <p className="mt-0.5 text-xs text-muted">
                    Saved {formatDay(s.createdAt)}
                    {s.lastSentAt ? `. Last email ${formatDay(s.lastSentAt)}.` : "."}
                  </p>
                </div>
                <button
                  type="button"
                  onClick={() => remove(s)}
                  disabled={busy === s.id}
                  className="flex h-11 w-11 items-center justify-center rounded-control text-brick hover:bg-brick-wash"
                  aria-label={`Delete ${s.name}`}
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              </div>
              <div className="mt-3 max-w-sm">
                <p className="mb-1 text-xs font-medium text-slate-700">Email alerts</p>
                <Segmented
                  label={`Email alerts for ${s.name}`}
                  value={s.frequency}
                  onChange={(f) => setFrequency(s, f)}
                  items={FREQUENCIES.map((f) => ({ value: f.value, label: f.label }))}
                />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
