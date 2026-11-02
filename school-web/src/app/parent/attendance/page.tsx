"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalAttendanceDto } from "@/lib/types/portals";
import { AttendanceCard, ChildGate, useChild, useChildPath } from "../_shared";

export default function ParentAttendancePage() {
  return (
    <ChildGate>
      <Attendance />
    </ChildGate>
  );
}

function Attendance() {
  const { child } = useChild();
  const { data, error, loading, reload } = useApi<PortalAttendanceDto | null>(useChildPath("/attendance"));
  return (
    <div>
      <PageHeader title="Attendance" subtitle={child ? `${child.name}${child.className ? ` · ${child.className}` : ""}` : undefined} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && !error && <AttendanceCard data={data} />}
    </div>
  );
}
