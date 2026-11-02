"use client";

import { useState } from "react";
import { useApi } from "@/lib/use-api";
import type { SupportViewDto } from "@/lib/types";
import { ApiErrorView, FormSection, Skeleton, formatDate } from "./ui";

/** "30 min", "1 h 15 min" between two ISO timestamps. */
function viewLength(from: string, to: string) {
  const minutes = Math.max(0, Math.round((Date.parse(to) - Date.parse(from)) / 60000));
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? `${h} h ${m} min` : `${h} h`;
}

/** "14:05", in the viewer's time zone like the dates beside it. */
function clockTime(iso: string) {
  return new Date(iso).toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
}

/** "Ended early at 14:05", "Access for 30 min, ended at expiry" or "Active until 14:30". */
function viewStatus(v: SupportViewDto, now: number) {
  if (v.endedAt) return `Ended early at ${clockTime(v.endedAt)}`;
  if (Date.parse(v.endsAt) <= now) return `Access for ${viewLength(v.startedAt, v.endsAt)}, ended at expiry`;
  return `Active until ${clockTime(v.endsAt)}`;
}

/** Read-only support views of this store in the last 90 days (`GET /api/account/activity`). */
export function AccountActivity() {
  const { data, error, reload } = useApi<SupportViewDto[]>("/api/account/activity");
  const [now] = useState(Date.now);
  return (
    <FormSection title="Account activity" description="Times our support team viewed your store, read only, in the last 90 days.">
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && (
        <div role="status" className="space-y-2">
          <span className="sr-only">Loading account activity…</span>
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-4 w-1/2 bg-line/60" />
        </div>
      )}
      {data && data.length === 0 && <p className="text-sm text-muted">Support hasn&apos;t viewed your account.</p>}
      {data && data.length > 0 && (
        <ul className="-my-4 divide-y divide-line sm:-my-5">
          {data.map((v) => (
            <li key={`${v.sessionId}-${v.startedAt}`} className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1 py-3.5">
              <div className="min-w-0">
                <p className="font-medium text-ink">{v.adminName}</p>
                {v.reason && <p className="mt-0.5 text-sm break-words text-ink-2">{v.reason}</p>}
              </div>
              <div className="text-sm text-muted sm:text-right">
                <p className="num">
                  <time dateTime={v.startedAt}>{formatDate(v.startedAt)}</time>
                </p>
                <p className="num">{viewStatus(v, now)}</p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </FormSection>
  );
}
