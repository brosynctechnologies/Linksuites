"use client";

import { RequireArea, useQueues } from "@/components/console";
import { Facts, Mono, PageHeader, StatusBadge } from "@/components/ui";
import { Queue } from "@/components/queue";
import { api } from "@/lib/api";
import { stamp } from "@/lib/format";
import { statusLabel } from "@/lib/status";
import type { ReportRow } from "@/lib/types";
import { useApi } from "@/lib/use-api";

export default function ReportsPage() {
  return (
    <RequireArea area="moderation">
      <Reports />
    </RequireArea>
  );
}

function Reports() {
  const { data, error, reload, setData } = useApi<ReportRow[]>("/api/admin/property/reports?status=Open");
  const { refresh } = useQueues();

  return (
    <>
      <PageHeader
        title="Reports"
        subtitle="Open reports from people about listings. Upholding keeps the listing down; dismissing the last open report restores a hidden listing."
      />
      <Queue
        items={data}
        error={error}
        reload={reload}
        config={{
          label: "Reports queue",
          getId: (r) => r.id,
          getTitle: (r) => r.listingTitle,
          approveLabel: "Uphold",
          rejectLabel: "Dismiss",
          decide: async (r, uphold) => {
            await api(`/api/admin/property/reports/${r.id}/decision`, { method: "POST", body: { uphold } });
            setData((xs) => (xs ?? []).filter((x) => x.id !== r.id));
            refresh();
            return uphold ? "Report upheld" : "Report dismissed";
          },
          emptyTitle: "No open reports",
          emptyText: "Reports people send about listings appear here.",
          renderRow: (r) => (
            <>
              <span className="flex items-center gap-2">
                <span className="text-xs font-semibold text-stop">{statusLabel(r.reason)}</span>
                <StatusBadge status={r.listingStatus} />
              </span>
              <span className="mt-1 block truncate font-medium">{r.listingTitle}</span>
              <span className="block truncate text-xs text-muted">
                {r.agencyName} · {stamp(r.createdAt)}
              </span>
            </>
          ),
          renderDetail: (r) => (
            <div className="space-y-4">
              <h2 className="text-lg font-semibold">{r.listingTitle}</h2>
              <Facts
                items={[
                  { label: "Reason", value: <span className="font-semibold text-stop">{statusLabel(r.reason)}</span> },
                  { label: "Details", value: r.details ? <span className="whitespace-pre-line">{r.details}</span> : <span className="text-muted">None given</span> },
                  { label: "Agency", value: r.agencyName },
                  { label: "Listing status", value: <StatusBadge status={r.listingStatus} /> },
                  { label: "Reported", value: stamp(r.createdAt) },
                  { label: "Listing id", value: <Mono>{r.listingId}</Mono> },
                  { label: "Report id", value: <Mono>{r.id}</Mono> },
                ]}
              />
            </div>
          ),
        }}
      />
    </>
  );
}
