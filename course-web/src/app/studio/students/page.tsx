"use client";

import { useState } from "react";
import { formatDay, formatCount } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { useApi, qs } from "@/lib/use-api";
import { useDebounced } from "@/lib/use-debounced";
import type { Paged, StudentDto } from "@/lib/types";
import { CourseFilter } from "@/components/course-filter";
import { UsersIcon } from "@/components/icons";
import { ApiErrorView, Badge, DataList, EmptyState, NoAccess, PageHeader, PageLoader, Pager, ProgressBar, SearchField, dataRowClass } from "@/components/ui";

export default function StudentsPage() {
  const allowed = useCan("learners.view");
  const [courseId, setCourseId] = useState("");
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const dq = useDebounced(q.trim(), 350);
  const { data, error, loading, reload } = useApi<Paged<StudentDto>>(allowed ? `/api/studio/students${qs({ courseId, q: dq, page })}` : null);

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Students" />
        <NoAccess />
      </div>
    );
  }

  const items = data?.items ?? [];
  return (
    <div>
      <PageHeader title="Students" subtitle={data ? `${formatCount(data.total)} enrolment${data.total === 1 ? "" : "s"}` : undefined} />
      <div className="mb-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_16rem]">
        <SearchField
          id="student-search"
          label="Search students"
          placeholder="Search by name or email"
          value={q}
          onChange={(v) => {
            setQ(v);
            setPage(1);
          }}
        />
        <CourseFilter
          value={courseId}
          onChange={(v) => {
            setCourseId(v);
            setPage(1);
          }}
        />
      </div>
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && items.length === 0 && (
        <EmptyState icon={<UsersIcon />} title={dq || courseId ? "No students match" : "No students yet"} text={dq || courseId ? "Try another name or course." : "Learners appear here as soon as they enrol."} />
      )}
      {items.length > 0 && (
        <>
          <DataList
            busy={loading}
            template="minmax(0,1.4fr) minmax(0,1.4fr) 9rem 7rem"
            head={[{ label: "Student" }, { label: "Course" }, { label: "Progress" }, { label: "Enrolled", align: "right" }]}
          >
            {items.map((s) => (
              <li key={s.enrolmentId} className={dataRowClass}>
                <div className="min-w-0">
                  <p className="truncate font-medium text-ink">{s.name}</p>
                  <p className="truncate text-xs text-muted">{s.email}</p>
                </div>
                <div className="min-w-0 text-right md:text-left">
                  <p className="truncate text-sm text-slate-700">{s.courseTitle}</p>
                  <Badge className={s.channel === "Academy" ? "bg-lagoon-wash text-lagoon-text" : "bg-mist-2 text-slate-700"}>{enrolmentLabel(s)}</Badge>
                </div>
                <div className="col-span-2 md:col-span-1">
                  {s.completedAt ? <p className="text-sm font-semibold text-lime-dark">Finished {formatDay(s.completedAt)}</p> : <ProgressBar percent={s.progressPercent} label={`${s.name}'s progress`} />}
                </div>
                <p className="col-span-2 text-xs text-muted md:col-span-1 md:text-right md:text-sm">{formatDay(s.enrolledAt)}</p>
              </li>
            ))}
          </DataList>
          <Pager page={data!.page} pageSize={data!.pageSize} total={data!.total} onPage={setPage} />
        </>
      )}
    </div>
  );
}

/** Paid enrolments show where the sale was made; the rest show how the learner got in. */
function enrolmentLabel(s: StudentDto): string {
  if (s.channel) return s.channel;
  return s.source === "Free" ? "Free" : s.source === "Coupon" ? "Coupon" : "Purchase";
}
