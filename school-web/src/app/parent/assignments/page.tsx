"use client";

import { formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { ParentAssignmentDto } from "@/lib/types/academic";
import { ApiErrorView, Badge, EmptyState, PageHeader, PageLoader, RuledSheet } from "@/components/ui";
import { ChildGate, score, useChild, useChildPath } from "../_shared";

export default function ParentAssignmentsPage() {
  return (
    <ChildGate>
      <Homework />
    </ChildGate>
  );
}

function Homework() {
  const { child } = useChild();
  const path = useChildPath("/assignments");
  const list = useApi<ParentAssignmentDto[]>(path);
  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Homework"
        subtitle={child ? `${child.name}${child.className ? ` · ${child.className}` : ""}` : undefined}
      />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {list.loading && !list.data && <PageLoader />}
      {list.data && list.data.length === 0 && <EmptyState title="No homework yet" text="When a teacher sets work for this class, the title, the due date and the mark show here." />}
      {list.data && list.data.length > 0 && (
        <RuledSheet label="Homework" busy={list.loading}>
          {list.data.map((item) => (
            <li key={item.id} className="ruled-pad min-w-0 space-y-1 py-3">
              <p className="font-bold break-words text-ink">{item.title}</p>
              <p className="text-xs text-muted">
                Due <span className="tnum">{formatStamp(item.dueAt)}</span>
              </p>
              <div className="flex flex-wrap items-center gap-2 pt-1">
                <Badge tone={item.submitted ? "blazer" : "neutral"}>{item.submitted ? "Submitted" : "Not submitted"}</Badge>
                <span className="text-sm text-slate-700">{item.mark === null ? "Not marked" : `Mark ${score(item.mark)}`}</span>
              </div>
            </li>
          ))}
        </RuledSheet>
      )}
    </div>
  );
}
