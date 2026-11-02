"use client";

import Link from "next/link";
import { useState } from "react";
import { asList } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { BOOST_STATUS, statusOf } from "@/lib/status";
import { qs, useApi } from "@/lib/use-api";
import type { BoostDto, StudioListingSummaryDto } from "@/lib/types";
import { BoostPicker } from "@/components/boost-picker";
import { BoltIcon } from "@/components/icons";
import { ApiErrorView, Card, EmptyState, PageHeader, PageLoader, SectionTitle, Select, StatusBadge } from "@/components/ui";

export default function BoostsPage() {
  const { data, error, reload } = useApi<BoostDto[]>("/api/property/boosts");
  const canBuy = useCan("money.manage");
  const active = useApi<StudioListingSummaryDto[]>(canBuy ? `/api/property/listings${qs({ status: "Active" })}` : null);
  const [listingId, setListingId] = useState("");
  const list = [...asList(data)].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const live = asList(active.data);

  return (
    <div className="space-y-5">
      <PageHeader title="Boosts" subtitle="Featured listings get an Adire tag and appear first on the home page, from every city; Top listings sit above others in search." />
      {canBuy && (
        <Card>
          <SectionTitle>Boost a listing</SectionTitle>
          {active.data && live.length === 0 ? (
            <p className="text-sm text-muted">Only live listings can be boosted. You have none right now.</p>
          ) : (
            <div className="space-y-4">
              <Select label="Listing" value={listingId} onChange={(e) => setListingId(e.target.value)} className="max-w-md">
                <option value="">Choose a live listing</option>
                {live.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.title}
                  </option>
                ))}
              </Select>
              {listingId && <BoostPicker key={listingId} listingId={listingId} />}
            </div>
          )}
        </Card>
      )}

      <section aria-labelledby="hist-h">
        <h2 id="hist-h" className="mb-3 font-semibold text-asphalt">
          Your boosts
        </h2>
        {!data && error && <ApiErrorView error={error} onRetry={reload} />}
        {!data && !error && <PageLoader />}
        {data && list.length === 0 && <EmptyState icon={<BoltIcon />} title="No boosts yet" text="Boosts last a set number of days and are paid once." />}
        {list.length > 0 && (
          <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
            {list.map((b) => {
              const st = statusOf(BOOST_STATUS, b.status);
              return (
                <li key={b.id}>
                  <Link href={`/studio/boosts/${b.id}`} className="flex flex-wrap items-center justify-between gap-2 p-4 hover:bg-slate-50">
                    <span className="min-w-0">
                      <span className="block font-semibold text-asphalt">{b.listingTitle}</span>
                      <span className="block text-sm text-muted">
                        {b.kind === "Top" ? "Top of search" : "Featured"} for {b.days} days
                        {b.endsAt ? `, until ${formatDay(b.endsAt)}` : ""}
                      </span>
                    </span>
                    <span className="flex items-center gap-3">
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      <span className="price text-asphalt">{formatMoney(b.amountMinor)}</span>
                    </span>
                  </Link>
                </li>
              );
            })}
          </ul>
        )}
      </section>
    </div>
  );
}
