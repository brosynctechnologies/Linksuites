"use client";

import Link from "next/link";
import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatShortDay, formatTime, phoneDigits } from "@/lib/format";
import { INSPECTION_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { InspectionDto } from "@/lib/types";
import { CalendarIcon, PhoneIcon } from "@/components/icons";
import { ApiErrorView, Button, EmptyState, ErrorBox, PageHeader, PageLoader, StatusBadge, buttonClass } from "@/components/ui";

export default function MyViewingsPage() {
  const { data, error, reload } = useApi<InspectionDto[]>("/api/me/inspections");
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [nowMs] = useState(() => Date.now());
  const t = (v: InspectionDto) => new Date(v.scheduledAt).getTime();
  const list = [...asList(data)].sort((a, b) => t(a) - t(b));

  const upcoming = list.filter((v) => t(v) >= nowMs && (v.status === "Requested" || v.status === "Confirmed"));
  const past = list.filter((v) => !upcoming.includes(v)).reverse();

  async function cancel(v: InspectionDto) {
    if (!window.confirm(`Cancel your viewing of ${v.listingTitle} on ${formatShortDay(v.scheduledAt)} at ${formatTime(v.scheduledAt)}?`)) return;
    setBusy(v.id);
    setActionError(null);
    try {
      await api(`/api/me/inspections/${v.id}/cancel`, { method: "POST" });
      reload();
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const row = (v: InspectionDto, canCancel: boolean) => {
    const st = statusOf(INSPECTION_STATUS, v.status);
    return (
      <li key={v.id} className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
        <div className="w-28 shrink-0">
          <p className="font-semibold text-asphalt">{formatShortDay(v.scheduledAt)}</p>
          <p className="tnum text-sm text-slate-700">{formatTime(v.scheduledAt)}</p>
        </div>
        <div className="min-w-0 flex-1">
          <p className="font-semibold text-asphalt">{v.listingTitle}</p>
          <p className="text-sm text-muted">
            {v.areaName}. With {v.agentName}
          </p>
          <div className="mt-1">
            <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {v.agentPhone && (
            <a href={`tel:+${phoneDigits(v.agentPhone)}`} className={buttonClass("secondary")}>
              <PhoneIcon className="h-4 w-4" /> Call agent
            </a>
          )}
          {canCancel && (
            <Button variant="danger" loading={busy === v.id} onClick={() => cancel(v)}>
              Cancel
            </Button>
          )}
        </div>
      </li>
    );
  };

  return (
    <div>
      <PageHeader title="Viewings" subtitle="Confirmed viewings get a reminder 24 hours before." />
      {actionError && <ErrorBox error={actionError} />}
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<CalendarIcon />}
          title="No viewings booked"
          text="Pick a time on any listing page with Book a viewing. Viewings are always free."
          action={
            <Link href="/rent" className={buttonClass("primary")}>
              Find a home
            </Link>
          }
        />
      )}
      {upcoming.length > 0 && (
        <section aria-labelledby="up-h" className="mb-6">
          <h2 id="up-h" className="mb-2 font-semibold text-asphalt">
            Coming up
          </h2>
          <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">{upcoming.map((v) => row(v, true))}</ul>
        </section>
      )}
      {past.length > 0 && (
        <section aria-labelledby="past-h">
          <h2 id="past-h" className="mb-2 font-semibold text-asphalt">
            Past and cancelled
          </h2>
          <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">{past.map((v) => row(v, false))}</ul>
        </section>
      )}
    </div>
  );
}
