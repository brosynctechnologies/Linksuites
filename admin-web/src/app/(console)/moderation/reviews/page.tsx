"use client";

import { RequireArea, useQueues } from "@/components/console";
import { Facts, Mono, PageHeader } from "@/components/ui";
import { Queue } from "@/components/queue";
import { api } from "@/lib/api";
import { plural, stamp } from "@/lib/format";
import type { HiddenReviewRow } from "@/lib/types";
import { useApi } from "@/lib/use-api";

function Stars({ rating }: { rating: number }) {
  return (
    <span className="tnum" aria-label={`${rating} out of 5`}>
      <span aria-hidden="true">
        {"★".repeat(Math.max(0, Math.min(5, rating)))}
        <span className="text-line">{"★".repeat(Math.max(0, 5 - rating))}</span>
      </span>
    </span>
  );
}

export default function ReviewsPage() {
  return (
    <RequireArea area="moderation">
      <Reviews />
    </RequireArea>
  );
}

function Reviews() {
  const { data, error, reload, setData } = useApi<HiddenReviewRow[]>("/api/admin/property/reviews");
  const { refresh } = useQueues();

  return (
    <>
      <PageHeader title="Reviews" subtitle="Agent reviews hidden after a report. Restore puts the review back on the agent's page; Keep hidden leaves it off." />
      <Queue
        items={data}
        error={error}
        reload={reload}
        config={{
          label: "Reviews queue",
          getId: (r) => r.id,
          getTitle: (r) => `Review of ${r.agentName}`,
          approveLabel: "Restore",
          rejectLabel: "Keep hidden",
          decide: async (r, restore) => {
            await api(`/api/admin/property/reviews/${r.id}/decision`, { method: "POST", body: { restore } });
            setData((xs) => (xs ?? []).filter((x) => x.id !== r.id));
            refresh();
            return restore ? "Review restored" : "Review kept hidden";
          },
          emptyTitle: "No hidden reviews",
          emptyText: "Reviews hidden after a report appear here.",
          renderRow: (r) => (
            <>
              <span className="flex items-center gap-2 text-xs">
                <Stars rating={r.rating} />
                <span className="font-semibold text-stop">{plural(r.reportCount, "report")}</span>
              </span>
              <span className="mt-1 block truncate font-medium">{r.agentName}</span>
              <span className="block truncate text-xs text-muted">
                by {r.userName} · {stamp(r.createdAt)}
              </span>
            </>
          ),
          renderDetail: (r) => (
            <div className="space-y-4">
              <h2 className="text-lg font-semibold">Review of {r.agentName}</h2>
              <blockquote className="rounded-control border-l-4 border-line bg-concrete-2 px-4 py-3 text-base whitespace-pre-line">
                {r.comment || <span className="text-muted">No comment, rating only.</span>}
              </blockquote>
              <Facts
                items={[
                  { label: "Rating", value: <Stars rating={r.rating} /> },
                  { label: "Reviewer", value: r.userName },
                  { label: "Reports", value: <span className="tnum">{r.reportCount}</span> },
                  { label: "Written", value: stamp(r.createdAt) },
                  { label: "Agent slug", value: <Mono>{r.agentSlug}</Mono> },
                ]}
              />
            </div>
          ),
        }}
      />
    </>
  );
}
