"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatCount, formatDay } from "@/lib/format";
import { formatPrice } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { LISTING_STATUS, renewNotice, statusOf } from "@/lib/status";
import { qs, useApi } from "@/lib/use-api";
import type { ListingStatus, StudioListingDto, StudioListingSummaryDto } from "@/lib/types";
import { BuildingIcon, ImageIcon, PlusIcon } from "@/components/icons";
import { ApiErrorView, Badge, Button, EmptyState, Notice, PageHeader, PageLoader, StatusBadge, Tabs, buttonClass } from "@/components/ui";

const FILTERS: { value: "" | ListingStatus; label: string }[] = [
  { value: "", label: "All" },
  { value: "Active", label: "Live" },
  { value: "Pending", label: "Pending" },
  { value: "Draft", label: "Drafts" },
  { value: "Expired", label: "Expired" },
  { value: "Rented", label: "Rented" },
  { value: "Sold", label: "Sold" },
  { value: "Rejected", label: "Rejected" },
  { value: "Hidden", label: "Hidden" },
];

export default function StudioListingsPage() {
  const sp = useSearchParams();
  const initial = FILTERS.find((f) => f.value === sp.get("status"))?.value ?? "";
  const [status, setStatus] = useState<"" | ListingStatus>(initial);
  const { data, error, reload } = useApi<StudioListingSummaryDto[]>(`/api/property/listings${qs({ status })}`);
  const canEdit = useCan("listings.edit");
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [nowMs] = useState(() => Date.now());
  const list = asList(data);

  async function renew(l: StudioListingSummaryDto) {
    setBusy(l.id);
    setActionError(null);
    setNotice(null);
    try {
      const fresh = await api<StudioListingDto | undefined>(`/api/property/listings/${l.id}/renew`, { method: "POST" });
      setNotice(fresh && typeof fresh === "object" && "status" in fresh ? renewNotice(fresh, true) : `"${l.title}" renewed.`);
      reload();
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Listings"
        action={
          canEdit && (
            <Link href="/studio/listings/new" className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> New listing
            </Link>
          )
        }
      />
      <Tabs label="Filter by status" value={status} onChange={setStatus} items={FILTERS} />
      {notice && <Notice tone="success">{notice}</Notice>}
      {actionError && <ApiErrorView error={actionError} />}
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<BuildingIcon />}
          title={status ? "Nothing with this status" : "No listings yet"}
          text={status ? "Try another tab." : "Post your first listing in five short steps."}
          action={
            !status && canEdit ? (
              <Link href="/studio/listings/new" className={buttonClass("primary")}>
                New listing
              </Link>
            ) : undefined
          }
        />
      )}
      {list.length > 0 && (
        <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
          {list.map((l) => {
            const st = statusOf(LISTING_STATUS, l.status);
            const expiringSoon = l.status === "Active" && l.expiresAt && new Date(l.expiresAt).getTime() - nowMs < 7 * 86_400_000;
            const featured = l.featuredUntil && new Date(l.featuredUntil).getTime() > nowMs;
            const top = l.topUntil && new Date(l.topUntil).getTime() > nowMs;
            const renewable = canEdit && (l.status === "Expired" || expiringSoon);
            return (
              <li key={l.id} className="flex flex-col gap-3 p-3 sm:flex-row sm:items-center sm:p-4">
                <Link href={`/studio/listings/${l.id}`} className="flex min-w-0 flex-1 items-center gap-3">
                  {l.coverUrl ? (
                    <img src={l.coverUrl} alt="" className="h-16 w-20 shrink-0 rounded-[4px] object-cover" />
                  ) : (
                    <span className="flex h-16 w-20 shrink-0 items-center justify-center rounded-[4px] bg-plaster-2 text-faint">
                      <ImageIcon className="h-5 w-5" />
                    </span>
                  )}
                  <span className="min-w-0">
                    <span className="block truncate font-semibold text-asphalt">{l.title}</span>
                    <span className="block text-sm text-muted">
                      {l.areaName} · <span className="price text-asphalt">{formatPrice(l.priceMinor, l.pricePeriod, l.currency)}</span>
                    </span>
                    <span className="mt-1 flex flex-wrap items-center gap-1.5">
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      {featured && <Badge className="bg-adire text-white">Featured</Badge>}
                      {top && <Badge className="bg-adire-wash text-adire">Top of search</Badge>}
                    </span>
                  </span>
                </Link>
                <div className="flex items-center justify-between gap-4 text-sm sm:justify-end">
                  <span className="text-muted">
                    <span className="tnum font-semibold text-asphalt">{formatCount(l.viewsCount)}</span> views
                  </span>
                  {l.expiresAt && (l.status === "Active" || l.status === "Expired") && (
                    <span className={expiringSoon || l.status === "Expired" ? "font-semibold text-ochre" : "text-muted"}>
                      {l.status === "Expired" ? "Expired" : "Expires"} {formatDay(l.expiresAt)}
                    </span>
                  )}
                  {renewable && (
                    <Button variant="secondary" loading={busy === l.id} onClick={() => renew(l)}>
                      Renew listing
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
