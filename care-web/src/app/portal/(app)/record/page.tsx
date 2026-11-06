"use client";

import { PortalError } from "@/components/portal-parts";
import { RecordView } from "@/components/portal-record";
import { PageHeader, PageLoader } from "@/components/ui";
import { usePatientApi, usePortal, whose } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalRecordDto } from "@/lib/types/portal";

/** The patient's own record: signed notes and addenda only, as the API returns them. Reading it here is not logged as staff access. */
export default function PortalRecordPage() {
  const { patient } = usePortal();
  const record = useApi<PortalRecordDto>(usePatientApi("/record"));
  return (
    <div>
      <PageHeader title={whose(patient, "record")} subtitle="Notes show here once the doctor signs them." />
      {record.error && !record.data && <PortalError error={record.error} onRetry={record.reload} />}
      {!record.data && !record.error && <PageLoader />}
      {record.data && <RecordView record={record.data} />}
    </div>
  );
}
