"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalExamTimetableDto } from "@/lib/types/academic";
import { ExamTimetableList } from "../../parent/_shared";
import { useStudentMe } from "../student-shell";

export default function StudentExamsPage() {
  const me = useStudentMe();
  const { data, error, loading, reload } = useApi<PortalExamTimetableDto>("/api/student/exam-timetable");
  return (
    <div className="min-w-0">
      <PageHeader title="Exam timetable" subtitle={me ? `${me.name}${me.className ? ` · ${me.className}` : ""}` : undefined} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && !error && <ExamTimetableList data={data} />}
    </div>
  );
}
