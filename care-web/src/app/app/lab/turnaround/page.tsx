"use client";

import { useState } from "react";
import { useCare } from "@/lib/care-me";
import { formatDay, lagosToday } from "@/lib/format";
import { minutesText } from "@/lib/lab-pharmacy";
import { qs, useApi } from "@/lib/use-api";
import type { LabTurnaroundDto, LabTurnaroundRowDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, CaseSheet, EmptyState, Input, NoAccess, PageHeader, PageLoader, caseRowClass, cx } from "@/components/ui";

function daysBefore(day: string, n: number): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() - n);
  return d.toISOString().slice(0, 10);
}

/**
 * Wave 4: lab turnaround (`GET /api/care/lab/turnaround`): the lab and leadership. Orders placed in the period by catalogue test: how many
 * reached each step, the average time between steps, and how many were reported within the test's target.
 */
export default function LabTurnaroundPage() {
  const { access } = useCare();
  const allowed = access.canWorkLab || access.isLeadership;
  const [to, setTo] = useState(lagosToday());
  const [from, setFrom] = useState(daysBefore(lagosToday(), 29));
  const report = useApi<LabTurnaroundDto>(allowed ? `/api/care/lab/turnaround${qs({ from, to })}` : null);
  if (!allowed) return <NoAccess text="Turnaround is for the lab and the clinic's leadership." />;

  return (
    <div className="space-y-5">
      <PageHeader title="Lab turnaround" subtitle="Time from order to sample, result and review, for the tests ordered in the period." />
      <div className="flex flex-wrap gap-3">
        <Input label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        <Input label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
      </div>
      {report.error && <ApiErrorView error={report.error} onRetry={report.reload} />}
      {!report.data && !report.error && <PageLoader />}
      {report.data && report.data.rows.length === 0 && <EmptyState title="No lab orders in this period" />}
      {report.data && report.data.rows.length > 0 && (
        <>
          <p className="text-sm text-muted">
            {formatDay(report.data.from)} to {formatDay(report.data.to)}
          </p>
          <CaseSheet
            label="Turnaround by test"
            template="minmax(0,1fr) 6rem 7rem 7rem 7rem 7rem 8rem"
            head={[
              { label: "Test" },
              { label: "Ordered", align: "right" },
              { label: "To sample", align: "right" },
              { label: "To result", align: "right" },
              { label: "To review", align: "right" },
              { label: "Order to result", align: "right" },
              { label: "On time", align: "right" },
            ]}
          >
            {[...report.data.rows, report.data.total].map((r, i) => (
              <Row key={r.labTestId ?? `row-${i}`} r={r} total={i === report.data!.rows.length} />
            ))}
          </CaseSheet>
        </>
      )}
    </div>
  );
}

function Row({ r, total }: { r: LabTurnaroundRowDto; total: boolean }) {
  return (
    <li className={cx(caseRowClass, total && "bg-slate-50")}>
      <span className="min-w-0">
        <span className="block font-bold text-ink">{r.name}</span>
        {!total && <span className="block text-xs text-muted">Target {r.targetHours} h</span>}
      </span>
      <span className="tnum text-right text-sm">{r.ordered}</span>
      <span className="tnum text-right text-sm">{minutesText(r.avgOrderToCollectMinutes)}</span>
      <span className="tnum text-right text-sm">{minutesText(r.avgCollectToReportMinutes)}</span>
      <span className="tnum text-right text-sm">{minutesText(r.avgReportToReviewMinutes)}</span>
      <span className="tnum text-right text-sm">{minutesText(r.avgOrderToReportMinutes)}</span>
      <span className="tnum text-right text-sm">
        {r.withinTarget} of {r.reported}
        {r.late > 0 && <span className="block text-xs text-arterial">{r.late} late</span>}
      </span>
    </li>
  );
}
