"use client";

import { useState } from "react";
import { formatDuration, formatStamp, formatTime } from "@/lib/time";
import { useApi } from "@/lib/use-api";
import { FALLBACK_TZ } from "@/lib/use-business";
import type { SupportViewDto } from "@/lib/types";
import { ApiErrorView, Card, SectionTitle, Skeleton } from "./ui";

/** "Ended early at 14:05", "Access for 30 min, ended at expiry" or "Active until 14:30", in the business time zone. */
function viewStatus(v: SupportViewDto, now: number, tz: string) {
  if (v.endedAt) return `Ended early at ${formatTime(v.endedAt, tz)}`;
  if (Date.parse(v.endsAt) <= now) {
    return `Access for ${formatDuration(Math.max(0, Math.round((Date.parse(v.endsAt) - Date.parse(v.startedAt)) / 60000)))}, ended at expiry`;
  }
  return `Active until ${formatTime(v.endsAt, tz)}`;
}

/** Read-only support views of this business in the last 90 days (`GET /api/account/activity`), in the business time zone. */
export function AccountActivity({ tz }: { tz?: string }) {
  const zone = tz || FALLBACK_TZ;
  const { data, error, reload } = useApi<SupportViewDto[]>("/api/account/activity");
  const [now] = useState(Date.now);
  return (
    <Card>
      <SectionTitle>Account activity</SectionTitle>
      <p className="-mt-2 mb-3 text-sm text-muted">Times our support team viewed this business, read only, in the last 90 days.</p>
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
                <p className="text-sm font-semibold text-aubergine">{v.adminName}</p>
                {v.reason && <p className="mt-0.5 text-sm break-words text-slate-700">{v.reason}</p>}
              </div>
              <div className="text-sm text-muted sm:text-right">
                <p className="tnum">
                  <time dateTime={v.startedAt}>{formatStamp(v.startedAt, zone)}</time>
                </p>
                <p className="tnum">{viewStatus(v, now, zone)}</p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
