"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalTimetableDto } from "@/lib/types/portals";
import { TimetableWeek } from "../../parent/_shared";
import { useStudentMe } from "../student-shell";

export default function StudentTimetablePage() {
  const me = useStudentMe();
  const { data, error, loading, reload } = useApi<PortalTimetableDto | null>("/api/student/timetable");
  return (
    <div>
      <PageHeader
        title="Timetable"
        subtitle={me ? `${me.name}${data?.className ? ` · ${data.className}` : me.className ? ` · ${me.className}` : ""}` : undefined}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && !error && <TimetableWeek data={data} />}
    </div>
  );
}
