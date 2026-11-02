"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalTimetableDto } from "@/lib/types/portals";
import { ChildGate, TimetableWeek, useChild, useChildPath } from "../_shared";

export default function ParentTimetablePage() {
  return (
    <ChildGate>
      <Timetable />
    </ChildGate>
  );
}

function Timetable() {
  const { child } = useChild();
  const { data, error, loading, reload } = useApi<PortalTimetableDto | null>(useChildPath("/timetable"));
  return (
    <div>
      <PageHeader
        title="Timetable"
        subtitle={child ? `${child.name}${data?.className ? ` · ${data.className}` : child.className ? ` · ${child.className}` : ""}` : undefined}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && !error && <TimetableWeek data={data} />}
    </div>
  );
}
