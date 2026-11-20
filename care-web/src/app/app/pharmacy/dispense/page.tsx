"use client";

import Link from "next/link";
import { useCare } from "@/lib/care-me";
import { formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { DispenseQueueRowDto } from "@/lib/types/lab-pharmacy";
import { ApiErrorView, Badge, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, Wristband, caseRowClass } from "@/components/ui";

/**
 * Wave 4: prescriptions of the last 30 days still to dispense (`GET /api/care/pharmacy/queue`): pharmacists and clinical staff. The patient,
 * the prescriber and how many drugs are done; never a drug (opening the prescription is a logged read).
 */
export default function DispenseQueuePage() {
  const { access } = useCare();
  const queue = useApi<DispenseQueueRowDto[]>(access.canDispense ? "/api/care/pharmacy/queue" : null);
  if (!access.canDispense) return <NoAccess text="Dispensing is for pharmacists, doctors and nurses." />;

  return (
    <div className="space-y-5">
      <PageHeader title="Dispense" subtitle="Prescriptions with drugs still to give. Open one to dispense from stock." />
      {queue.error && <ApiErrorView error={queue.error} onRetry={queue.reload} />}
      {!queue.data && !queue.error && <PageLoader />}
      {queue.data && queue.data.length === 0 && <EmptyState title="Every prescription is dispensed" />}
      {queue.data && queue.data.length > 0 && (
        <CaseSheet label="Prescriptions to dispense" template="minmax(0,1fr) minmax(0,1fr) 8rem 10rem" head={[{ label: "Patient" }, { label: "Prescriber" }, { label: "Drugs done" }, { label: "Written", align: "right" }]}>
          {queue.data.map((r) => (
            <li key={r.prescriptionId} className={caseRowClass}>
              <Link href={`/app/pharmacy/dispense/${r.prescriptionId}`} className="flex min-w-0 flex-wrap items-center gap-2 font-semibold text-scrub hover:underline">
                <Wristband number={r.patientNumber} />
                <span className="truncate">{r.patientName}</span>
                {r.inpatient && <Badge>Inpatient</Badge>}
              </Link>
              <span className="text-sm">{r.doctorName}</span>
              <span className="tnum text-sm">
                {r.itemsDone} of {r.items}
              </span>
              <span className="tnum text-right text-sm text-slate-700">{formatStamp(r.writtenAt)}</span>
            </li>
          ))}
        </CaseSheet>
      )}
    </div>
  );
}
