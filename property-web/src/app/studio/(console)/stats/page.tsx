"use client";

import { useState } from "react";
import { formatCount } from "@/lib/format";
import { LISTING_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { ListingStatsDto } from "@/lib/types";
import { ApiErrorView, DataList, PageHeader, PageLoader, Segmented, StatusBadge, SummaryStrip, UpgradePrompt, dataRowClass } from "@/components/ui";

export default function StatsPage() {
  const [days, setDays] = useState<"7" | "30" | "90">("30");
  const { data, error, reload, loading } = useApi<ListingStatsDto>(`/api/property/stats?days=${days}`);

  if (error?.isPlanLimit) {
    return (
      <div>
        <PageHeader title="Stats" />
        <UpgradePrompt plan="Pro" message="Views, WhatsApp taps, calls and enquiries per listing come with Pro and Agency." />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title="Stats"
        subtitle="How seekers respond to your listings."
        action={
          <Segmented
            label="Period"
            className="w-60"
            value={days}
            onChange={setDays}
            items={[
              { value: "7", label: "7 days" },
              { value: "30", label: "30 days" },
              { value: "90", label: "90 days" },
            ]}
          />
        }
      />
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <>
          <SummaryStrip
            label={`Last ${data.days} days`}
            items={[
              { label: "Views", value: formatCount(data.views) },
              { label: "WhatsApp taps", value: formatCount(data.whatsAppClicks) },
              { label: "Calls", value: formatCount(data.callClicks) },
              { label: "Enquiries", value: formatCount(data.enquiries) },
            ]}
          />
          {data.listings.length > 0 && (
            <DataList
              busy={loading}
              template="minmax(0,1.6fr) 5.5rem 5.5rem 5.5rem 5.5rem"
              head={[{ label: "Listing" }, { label: "Views", align: "right" }, { label: "WhatsApp", align: "right" }, { label: "Calls", align: "right" }, { label: "Enquiries", align: "right" }]}
            >
              {data.listings.map((r) => {
                const st = statusOf(LISTING_STATUS, r.status);
                return (
                  <li key={r.listingId} className={dataRowClass}>
                    <div className="min-w-0">
                      <p className="truncate font-medium text-asphalt">{r.title}</p>
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                    </div>
                    <p className="tnum text-right font-semibold text-asphalt">{formatCount(r.views)}</p>
                    <p className="tnum col-span-2 text-sm text-muted md:col-span-1 md:text-right md:text-base md:text-asphalt">
                      <span className="md:hidden">WhatsApp </span>
                      {formatCount(r.whatsAppClicks)}
                      <span className="md:hidden"> · Calls {formatCount(r.callClicks)} · Enquiries {formatCount(r.enquiries)}</span>
                    </p>
                    <p className="tnum hidden text-right md:block">{formatCount(r.callClicks)}</p>
                    <p className="tnum hidden text-right md:block">{formatCount(r.enquiries)}</p>
                  </li>
                );
              })}
            </DataList>
          )}
        </>
      )}
    </div>
  );
}
