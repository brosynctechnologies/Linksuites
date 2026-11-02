"use client";

import { useState } from "react";
import { asList } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { qs, useApi } from "@/lib/use-api";
import type { StudioReviewDto } from "@/lib/types";
import { Stars } from "@/components/badges";
import { CourseFilter } from "@/components/course-filter";
import { StarIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, NoAccess, PageHeader, PageLoader } from "@/components/ui";

export default function ReviewsPage() {
  const allowed = useCan("learners.view");
  const [courseId, setCourseId] = useState("");
  const { data, error, reload } = useApi<StudioReviewDto[]>(allowed ? `/api/studio/reviews${qs({ courseId })}` : null);
  const list = asList(data);
  const avg = list.length ? list.reduce((n, r) => n + r.rating, 0) / list.length : 0;

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Reviews" back="/studio/more" />
        <NoAccess />
      </div>
    );
  }

  return (
    <div>
      <PageHeader title="Reviews" back="/studio/more" subtitle={list.length ? `${avg.toFixed(1)} average from ${list.length} review${list.length === 1 ? "" : "s"}` : undefined} />
      <CourseFilter value={courseId} onChange={setCourseId} className="mb-4 max-w-xs" />
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && <EmptyState icon={<StarIcon />} title="No reviews yet" text="Learners can rate a course from the lesson player. Reviews show on your course pages." />}
      {list.length > 0 && (
        <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
          {list.map((r) => (
            <li key={r.id} className="p-4">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="font-semibold text-ink">{r.studentName}</p>
                <p className="text-xs text-muted">{formatDay(r.createdAt)}</p>
              </div>
              <p className="text-xs text-muted">{r.courseTitle}</p>
              <Stars value={r.rating} className="mt-1.5" />
              {r.comment && <p className="mt-2 max-w-[68ch] text-[0.9375rem] text-slate-700">{r.comment}</p>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
