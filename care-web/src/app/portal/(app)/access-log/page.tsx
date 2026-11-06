"use client";

import { PortalError, lagosClock, lagosDate } from "@/components/portal-parts";
import { ShieldIcon } from "@/components/icons";
import { EmptyState, PageHeader, PageLoader } from "@/components/ui";
import { usePatientApi, usePortal } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalAccessDto } from "@/lib/types/portal";

/**
 * Who opened the record: each time clinic staff opened it, the staff member's name, their role and what they opened (the API's own area
 * labels), and each read through the portal by a linked account, shown as "Nkem W. (parent), patient portal" (final review C1). Newest
 * first. The API never sends a reason or a staff id.
 */
export default function PortalAccessLogPage() {
  const { patient } = usePortal();
  const log = useApi<PortalAccessDto[]>(usePatientApi("/access-log"));
  return (
    <div>
      <PageHeader
        title="Who opened the record"
        subtitle={`Every time staff at ${patient?.clinicName ?? "the clinic"} or a linked portal account open the record, it is written down here.`}
      />
      {log.error && !log.data && <PortalError error={log.error} onRetry={log.reload} />}
      {!log.data && !log.error && <PageLoader />}
      {log.data && log.data.length === 0 && (
        <EmptyState icon={<ShieldIcon />} title="No one has opened the record yet" text="When a doctor or nurse opens it, you will see who and when." />
      )}
      {log.data && log.data.length > 0 && (
        <div className="case-sheet">
          <ul className="case-rows" aria-label="Record openings">
            {log.data.map((a, i) => (
              <li key={i} className="case-pad grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-0.5 py-3">
                <span className="font-bold text-ink">{a.portal ? a.who : a.staffName || "A staff member"}</span>
                <span className="tnum text-right text-sm text-muted">{lagosDate(a.at)}</span>
                <span className="text-sm text-slate-700">
                  {a.portal ? (
                    a.what
                  ) : (
                    <>
                      {a.role} <span aria-hidden="true">·</span> {a.what}
                    </>
                  )}
                </span>
                <span className="tnum text-right text-sm text-muted">{lagosClock(a.at)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
