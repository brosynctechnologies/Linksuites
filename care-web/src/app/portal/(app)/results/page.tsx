"use client";

import Link from "next/link";
import { PortalError } from "@/components/portal-parts";
import { ResultsList } from "@/components/portal-record";
import { PageHeader, PageLoader, cx, linkClass } from "@/components/ui";
import { portalHref, usePatientApi, usePortal, whose } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalRecordDto } from "@/lib/types/portal";

/** Where a "new result" notice lands: the results part of the record (the API has no results-only route, so the record is read). */
export default function PortalResultsPage() {
  const { patient } = usePortal();
  const record = useApi<PortalRecordDto>(usePatientApi("/record"));
  const results = [...(record.data?.results ?? [])].sort((a, b) => (b.resultedAt ?? "").localeCompare(a.resultedAt ?? ""));
  return (
    <div className="space-y-4">
      <PageHeader title={whose(patient, "results")} back={portalHref("/portal/record", patient?.id)} />
      {record.error && !record.data && <PortalError error={record.error} onRetry={record.reload} />}
      {!record.data && !record.error && <PageLoader />}
      {record.data && <ResultsList results={results} />}
      <Link href={portalHref("/portal/record", patient?.id)} className={cx(linkClass, "inline-flex min-h-11 items-center text-sm")}>
        Open the whole record
      </Link>
    </div>
  );
}
