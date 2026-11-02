"use client";

import Link from "next/link";
import { asList } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { STAY_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { MyStayDto } from "@/lib/types";
import { KeyIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, StatusBadge, buttonClass } from "@/components/ui";

export default function TripsPage() {
  const { data, error, reload } = useApi<MyStayDto[]>("/api/me/stays");
  const list = [...asList(data)].sort((a, b) => b.checkIn.localeCompare(a.checkIn));
  return (
    <div>
      <PageHeader title="Trips" subtitle="Your short-let bookings." />
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<KeyIcon />}
          title="No trips yet"
          text="Book a short-let and your dates, address and host's number will be here."
          action={
            <Link href="/shortlet" className={buttonClass("primary")}>
              Find a short-let
            </Link>
          }
        />
      )}
      {list.length > 0 && (
        <ul className="space-y-3">
          {list.map((s) => {
            const st = statusOf(STAY_STATUS, s.status);
            return (
              <li key={s.id}>
                <Link href={`/trips/${s.id}`} className="flex gap-3 overflow-hidden rounded-panel border border-line bg-surface hover:border-slate-300">
                  {s.coverUrl ? <img src={s.coverUrl} alt="" className="h-auto w-28 shrink-0 object-cover sm:w-40" /> : <span className="w-28 shrink-0 bg-plaster-2 sm:w-40" />}
                  <div className="min-w-0 flex-1 py-3 pr-3">
                    <p className="font-semibold text-asphalt">{s.listingTitle}</p>
                    <p className="text-sm text-muted">{s.areaName}</p>
                    <p className="mt-1 text-sm text-slate-700">
                      {formatDay(s.checkIn)} to {formatDay(s.checkOut)}, {s.nights} night{s.nights === 1 ? "" : "s"}
                    </p>
                    <div className="mt-1.5 flex flex-wrap items-center gap-2">
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      {/* The same figure as the trip page: what you paid less refunds, or the price when nothing was paid. */}
                      <span className="price text-sm text-asphalt">
                        {s.paid ? formatMoney(s.totalMinor - s.refundMinor, s.currency) : formatMoney(s.totalMinor, s.currency)}
                      </span>
                      {s.paid && s.refundMinor > 0 && <span className="text-xs text-muted">net, after {formatMoney(s.refundMinor, s.currency)} refunded</span>}
                      {!s.paid && s.status !== "Pending" && <span className="text-xs text-muted">not paid</span>}
                    </div>
                  </div>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
