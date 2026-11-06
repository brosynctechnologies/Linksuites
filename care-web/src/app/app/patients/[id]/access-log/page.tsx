"use client";

import { useParams } from "next/navigation";
import { useCare } from "@/lib/care-me";
import { formatStamp } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { RecordAccessDto } from "@/lib/types";
import { ApiErrorView, Badge, CaseSheet, EmptyState, NoAccess, PageHeader, PageLoader, caseRowClass } from "@/components/ui";

/** Who opened a patient's clinical record (`GET /api/care/patients/{id}/access-log`): the medical director only. The emergency notice links here. */
export default function AccessLogPage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const log = useApi<RecordAccessDto[]>(access.isDirector && !access.readOnly ? `/api/care/patients/${encodeURIComponent(id)}/access-log` : null);
  // The API refuses support views (403), so a support view running as the director gets the plain no-access message.
  if (!access.isDirector || access.readOnly) return <NoAccess text="The access log is for the medical director." />;

  return (
    <div className="space-y-5">
      <PageHeader title="Who opened this record" back={`/app/patients/${id}`} subtitle="Every opening of the patient's clinical record, by staff and through the patient portal, newest first, in Lagos time." />
      {log.error && <ApiErrorView error={log.error} onRetry={log.reload} />}
      {!log.data && !log.error && <PageLoader />}
      {log.data && log.data.length === 0 && <EmptyState title="Nobody has opened this record yet" />}
      {log.data && log.data.length > 0 && (
        <CaseSheet
          label="Access log"
          template="11rem minmax(0,1fr) 10rem"
          head={[{ label: "When" }, { label: "Who and why" }, { label: "What" }]}
        >
          {log.data.map((a) => (
            <li key={a.id} className={caseRowClass}>
              <span className="tnum text-sm text-slate-700">{formatStamp(a.at)}</span>
              <span className="col-span-2 min-w-0 md:col-span-1">
                <span className="flex flex-wrap items-center gap-2 font-bold text-ink">
                  {a.portal ? (
                    // A read through the patient portal by a linked account: "Nkem W. (parent), patient portal".
                    <>
                      {a.who} <Badge tone="iodine">Portal</Badge>
                    </>
                  ) : (
                    <>
                      {a.staffName} <span className="font-normal text-muted">{a.role}</span>
                    </>
                  )}
                  {a.emergency && <Badge tone="arterial">Emergency</Badge>}
                </span>
                {a.reason && <span className="mt-0.5 block text-sm break-words text-slate-700">Reason: {a.reason}</span>}
              </span>
              <span className="text-sm text-slate-700">{a.areaLabel}</span>
            </li>
          ))}
        </CaseSheet>
      )}
    </div>
  );
}
