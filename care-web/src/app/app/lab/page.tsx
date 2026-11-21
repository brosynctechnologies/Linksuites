"use client";

import Link from "next/link";
import { useState } from "react";
import { useCare } from "@/lib/care-me";
import { formatStamp } from "@/lib/format";
import { LAB_STAGES, labStageLabel } from "@/lib/lab-pharmacy";
import { qs, useApi } from "@/lib/use-api";
import type { LabStage, LabWorklistRowDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, Badge, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, Tabs, Wristband, caseRowClass } from "@/components/ui";

type Filter = "Open" | LabStage;

/**
 * Wave 4: the lab worklist (`GET /api/care/lab/worklist`): lab scientists and clinical staff. Orders not yet reviewed, oldest first, or one
 * stage. The patient, the catalogue test and the sample: never a free-text description or a result (opening an order is a logged read).
 */
export default function LabWorklistPage() {
  const { access } = useCare();
  const [filter, setFilter] = useState<Filter>("Open");
  const list = useApi<LabWorklistRowDto[]>(access.canWorkLab ? `/api/care/lab/worklist${qs({ stage: filter === "Open" ? null : filter })}` : null);
  if (!access.canWorkLab)
    return <NoAccess text="The worklist is for lab scientists, doctors and nurses. The catalogue and turnaround are in the links above." />;

  return (
    <div className="space-y-5">
      <PageHeader title="Lab worklist" subtitle="Lab orders waiting for a sample, a result or a doctor's review. Open one to collect the sample, print its label and enter the result." />
      <Tabs
        label="Stage"
        value={filter}
        onChange={setFilter}
        items={[{ value: "Open" as Filter, label: "All open" }, ...LAB_STAGES.map((s) => ({ value: s.value as Filter, label: s.label }))]}
      />
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && <EmptyState title={filter === "Done" ? "Nothing reviewed in the last 7 days" : "Nothing waiting"} />}
      {list.data && list.data.length > 0 && (
        <CaseSheet
          label="Lab orders"
          template="minmax(0,1.2fr) minmax(0,1fr) 9rem 10rem 9rem"
          head={[{ label: "Patient" }, { label: "Test" }, { label: "Sample" }, { label: "Stage" }, { label: "Ordered", align: "right" }]}
        >
          {list.data.map((r) => (
            <li key={r.id} className={caseRowClass}>
              <Link href={`/app/lab/orders/${r.id}`} className="flex min-w-0 flex-wrap items-center gap-2 font-semibold text-scrub hover:underline">
                <Wristband number={r.patientNumber} />
                <span className="truncate">{r.patientName}</span>
                <span className="text-xs font-normal text-muted">
                  {r.sex}, {r.ageYears} y
                </span>
              </Link>
              <span className="min-w-0 text-sm text-ink">
                <span className="block truncate font-bold">{r.testName ?? "Lab test (free text)"}</span>
                {r.sampleType && <span className="block text-xs text-muted">{r.sampleType}</span>}
              </span>
              <span className="tnum text-sm text-slate-700">{r.sampleNumber ?? "Not collected"}</span>
              <span className="flex flex-wrap gap-1">
                <Badge tone={r.stage === "Done" ? "pass" : "scrub"}>{labStageLabel(r.stage)}</Badge>
                {r.overdue && r.stage !== "Done" && <Badge tone="arterial">Overdue</Badge>}
              </span>
              <span className="tnum text-right text-sm text-slate-700">
                {formatStamp(r.orderedAt)}
                <span className="block text-xs text-muted">{r.requestedByName}</span>
              </span>
            </li>
          ))}
        </CaseSheet>
      )}
      {list.data && list.data.length > 0 && <p className="text-sm text-muted">Opening an order is logged on the patient&apos;s access log.</p>}
    </div>
  );
}
