"use client";

import Link from "next/link";
import { PortalError } from "@/components/portal-parts";
import { ResultsList } from "@/components/portal-record";
import { PageHeader, PageLoader, cx, linkClass } from "@/components/ui";
import { portalHref, usePatientApi, usePortal, whose } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalResultDto } from "@/lib/types/portal";

/** Where a "new result" notice lands: the reviewed results only (`GET /api/care-portal/patients/{id}/results`, Wave 1), newest first. */
export default function PortalResultsPage() {
  const { patient } = usePortal();
  const list = useApi<PortalResultDto[]>(usePatientApi("/results"));
  const results = [...(list.data ?? [])].sort((a, b) => (b.resultedAt ?? "").localeCompare(a.resultedAt ?? ""));
  return (
    <div className="space-y-4">
      <PageHeader title={whose(patient, "results")} back={portalHref("/portal/record", patient?.id)} />
      {list.error && !list.data && <PortalError error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && <ResultsList results={results} />}
      <Link href={portalHref("/portal/record", patient?.id)} className={cx(linkClass, "inline-flex min-h-11 items-center text-sm")}>
        Open the whole record
      </Link>
    </div>
  );
}
