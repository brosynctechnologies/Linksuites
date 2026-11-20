"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { StudentSubjectAttendanceDto } from "@/lib/types/academic";
import type { PortalOverviewDto } from "@/lib/types/portals";
import { AttendanceCard, SubjectAttendanceCard } from "../../parent/_shared";
import { useStudentMe } from "../student-shell";

export default function StudentAttendancePage() {
  const me = useStudentMe();
  const overview = useApi<PortalOverviewDto>("/api/student/overview");
  const subjects = useApi<StudentSubjectAttendanceDto>("/api/student/subject-attendance");
  return (
    <div className="space-y-6">
      <PageHeader title="Attendance" subtitle={me ? `${me.name}${me.className ? ` · ${me.className}` : ""}` : undefined} />
      {overview.error && <ApiErrorView error={overview.error} onRetry={overview.reload} />}
      {overview.loading && <PageLoader />}
      {!overview.loading && !overview.error && <AttendanceCard data={overview.data?.attendance} />}
      {subjects.error && <ApiErrorView error={subjects.error} onRetry={subjects.reload} />}
      {!subjects.loading && <SubjectAttendanceCard data={subjects.data} />}
    </div>
  );
}
