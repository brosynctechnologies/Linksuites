"use client";

import Link from "next/link";
import { useCare } from "@/lib/care-me";
import { TEST_KINDS } from "@/lib/clinical";
import { formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { AwaitingReviewDto } from "@/lib/types/clinical";
import { ApiErrorView, Badge, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, Wristband, caseRowClass } from "@/components/ui";

/**
 * Post-build (user decision): the doctors' and the medical director's list of test results waiting for review
 * (`GET /api/care/tests/awaiting-review`), oldest first. The patient, the kind of test and who recorded it when: never the test's
 * description or its result. Opening the record (a logged read) shows the result and its "Review result" button; the patient's portal
 * shows a result only after it is reviewed.
 */
export default function ResultsToReviewPage() {
  const { access } = useCare();
  const allowed = access.isClinical && (access.isDirector || access.clinicalRole === "Doctor");
  const results = useApi<AwaitingReviewDto[]>(allowed ? "/api/care/tests/awaiting-review" : null);
  if (!allowed) return <NoAccess text="Results are reviewed by doctors and the medical director." />;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Results to review"
        subtitle="Recorded results the patient can't see yet. Open the record, read the result and press Review result to release it to the patient's portal."
      />
      {results.error && <ApiErrorView error={results.error} onRetry={results.reload} />}
      {!results.data && !results.error && <PageLoader />}
      {results.data && results.data.length === 0 && <EmptyState title="Every result is reviewed" />}
      {results.data && results.data.length > 0 && (
        <CaseSheet
          label="Results to review"
          template="minmax(0,1fr) 6rem minmax(0,1fr) 10rem"
          head={[{ label: "Patient" }, { label: "Test" }, { label: "Recorded by" }, { label: "Recorded", align: "right" }]}
        >
          {results.data.map((r) => (
            <li key={r.id} className={caseRowClass}>
              <Link href={`/app/patients/${r.patientId}/record`} className="flex min-w-0 flex-wrap items-center gap-2 font-semibold text-scrub hover:underline">
                <Wristband number={r.patientNumber} />
                <span className="truncate">{r.patientName}</span>
              </Link>
              <span>
                <Badge>{TEST_KINDS.find((k) => k.value === r.kind)?.label ?? r.kind}</Badge>
              </span>
              <span className="text-sm text-ink">{r.recordedByName ?? "Staff"}</span>
              <span className="tnum text-right text-sm text-slate-700">{formatStamp(r.recordedAt)}</span>
            </li>
          ))}
        </CaseSheet>
      )}
      {results.data && results.data.length > 0 && <p className="text-sm text-muted">Opening a patient&apos;s record is logged like any other open.</p>}
    </div>
  );
}
