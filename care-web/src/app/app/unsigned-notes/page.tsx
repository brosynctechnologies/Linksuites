"use client";

import Link from "next/link";
import { useCare } from "@/lib/care-me";
import { formatDay, plural } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { UnsignedDraftDto } from "@/lib/types/clinical";
import { ApiErrorView, Badge, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, Wristband, caseRowClass } from "@/components/ui";

const KIND: Record<string, string> = {
  consultation: "Consultation",
  "ward-round": "Ward round",
  "discharge-summary": "Discharge summary",
};

/**
 * The medical director's list of unsigned drafts (`GET /api/care/reports/unsigned-drafts`): consultations on closed visits and every
 * unsigned ward round or discharge summary, oldest first. Patient number, doctor, date and age only: never the note text or the name.
 */
export default function UnsignedNotesPage() {
  const { access } = useCare();
  const drafts = useApi<UnsignedDraftDto[]>(access.isDirector ? "/api/care/reports/unsigned-drafts" : null);
  if (!access.isDirector) return <NoAccess text="The unsigned notes list is for the medical director." />;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Unsigned notes"
        subtitle="Drafts that should have been signed: consultations on closed visits, ward rounds and discharge summaries. Ask the doctor to sign them."
      />
      {drafts.error && <ApiErrorView error={drafts.error} onRetry={drafts.reload} />}
      {!drafts.data && !drafts.error && <PageLoader />}
      {drafts.data && drafts.data.length === 0 && <EmptyState title="Every note is signed" />}
      {drafts.data && drafts.data.length > 0 && (
        <CaseSheet
          label="Unsigned notes"
          template="minmax(0,1fr) minmax(0,1fr) 9rem 8rem"
          head={[{ label: "Patient and note" }, { label: "Doctor" }, { label: "Visit or admission" }, { label: "Waiting", align: "right" }]}
        >
          {drafts.data.map((d) => (
            <li key={`${d.kind}-${d.id}`} className={caseRowClass}>
              <span className="flex min-w-0 flex-wrap items-center gap-2">
                <Wristband number={d.patientNumber} />
                <Badge>{KIND[d.kind] ?? d.kind}</Badge>
              </span>
              <span className="tnum text-right text-sm md:hidden">{plural(d.ageDays, "day")}</span>
              <span className="text-sm text-ink">{d.doctorName}</span>
              <span className="tnum text-sm text-slate-700">{formatDay(d.date)}</span>
              <span className="tnum hidden text-right text-sm md:block">
                {d.ageDays >= 7 ? <Badge tone="iodine">{plural(d.ageDays, "day")}</Badge> : plural(d.ageDays, "day")}
              </span>
            </li>
          ))}
        </CaseSheet>
      )}
      {drafts.data && drafts.data.length > 0 && !access.readOnly && (
        <p className="text-sm text-muted">
          Opening a patient&apos;s record from{" "}
          <Link href="/app/patients" className="font-semibold text-scrub hover:underline">
            Patients
          </Link>{" "}
          is logged like any other open.
        </p>
      )}
    </div>
  );
}
