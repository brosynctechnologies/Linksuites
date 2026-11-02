"use client";

import { ApiErrorView, PageHeader, PageLoader } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { PortalExamTimetableDto } from "@/lib/types/academic";
import { ChildGate, ExamTimetableList, useChild, useChildPath } from "../_shared";

export default function ParentExamsPage() {
  return (
    <ChildGate>
      <Exams />
    </ChildGate>
  );
}

function Exams() {
  const { child } = useChild();
  const { data, error, loading, reload } = useApi<PortalExamTimetableDto>(useChildPath("/exam-timetable"));
  return (
    <div className="min-w-0">
      <PageHeader title="Exam timetable" subtitle={child ? `${child.name}${child.className ? ` · ${child.className}` : ""}` : undefined} />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {loading && <PageLoader />}
      {!loading && !error && <ExamTimetableList data={data} />}
    </div>
  );
}
