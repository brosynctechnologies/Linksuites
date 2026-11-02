"use client";

import { useState } from "react";
import { formatLength, formatStamp } from "@/lib/format";
import { lagosTime } from "@/lib/support";
import { useApi } from "@/lib/use-api";
import type { SupportViewDto } from "@/lib/types";
import { ApiErrorView, Card, SectionTitle, Skeleton } from "./ui";

/** "Ended early at 14:05", "Access for 30 min, ended at expiry" or "Active until 14:30" (Lagos time, like the dates beside it). */
function viewStatus(v: SupportViewDto, now: number) {
  if (v.endedAt) return `Ended early at ${lagosTime(v.endedAt)}`;
  if (Date.parse(v.endsAt) <= now) return `Access for ${formatLength((Date.parse(v.endsAt) - Date.parse(v.startedAt)) / 1000) || "0 min"}, ended at expiry`;
  return `Active until ${lagosTime(v.endsAt)}`;
}

/** Read-only support views of this academy in the last 90 days (`GET /api/account/activity`). */
export function AccountActivity() {
  const { data, error, reload } = useApi<SupportViewDto[]>("/api/account/activity");
  const [now] = useState(Date.now);
  return (
    <Card>
      <SectionTitle>Account activity</SectionTitle>
      <p className="-mt-2 mb-3 text-sm text-muted">Times our support team viewed this academy, read only, in the last 90 days.</p>
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && (
        <div role="status" className="space-y-2">
          <span className="sr-only">Loading account activity…</span>
          <Skeleton className="h-3.5 w-2/3" />
          <Skeleton className="h-3.5 w-1/2" />
        </div>
      )}
      {data && data.length === 0 && <p className="text-sm text-slate-700">Support hasn&apos;t viewed your account.</p>}
      {data && data.length > 0 && (
        <ul className="divide-y divide-line-soft border-t border-line-soft">
          {data.map((v) => (
            <li key={`${v.sessionId}-${v.startedAt}`} className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1 py-3">
              <div className="min-w-0">
                <p className="text-sm font-semibold text-ink">{v.adminName}</p>
                {v.reason && <p className="mt-0.5 text-sm break-words text-slate-700">{v.reason}</p>}
              </div>
              <div className="text-sm text-muted sm:text-right">
                <p className="tnum">
                  <time dateTime={v.startedAt}>{formatStamp(v.startedAt)}</time>
                </p>
                <p className="tnum">{viewStatus(v, now)}</p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
