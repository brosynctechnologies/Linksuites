"use client";

import Link from "next/link";
import { asList } from "@/lib/api";
import { formatCount, formatDay } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { COURSE_STATUS, MARKETPLACE_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { StudioCourseSummaryDto } from "@/lib/types";
import { Thumb } from "@/components/course-card";
import { BookIcon, PlusIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, StatusBadge, buttonClass } from "@/components/ui";

export default function StudioCoursesPage() {
  const canCreate = useCan("courses.edit");
  const { data, error, reload } = useApi<StudioCourseSummaryDto[]>("/api/studio/courses");
  const list = asList(data);

  return (
    <div>
      <PageHeader
        title="Courses"
        action={
          canCreate && (
            <Link href="/studio/courses/new" className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> New course
            </Link>
          )
        }
      />
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<BookIcon />}
          title="No courses yet"
          text="Start with a title. You can add sections, lessons and quizzes, then publish when one lesson is ready."
          action={
            canCreate && (
              <Link href="/studio/courses/new" className={buttonClass("primary")}>
                <PlusIcon className="h-4 w-4" /> New course
              </Link>
            )
          }
        />
      )}
      {list.length > 0 && (
        <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
          {list.map((c) => {
            const st = statusOf(COURSE_STATUS, c.status);
            const mk = statusOf(MARKETPLACE_STATUS, c.marketplaceStatus);
            return (
              <li key={c.id}>
                <Link href={`/studio/courses/${c.id}`} className="grid grid-cols-[5rem_minmax(0,1fr)] gap-x-4 gap-y-2 p-4 hover:bg-slate-50 sm:grid-cols-[7rem_minmax(0,1fr)_auto] sm:items-center">
                  <span className="block overflow-hidden rounded-[8px]">
                    <Thumb src={c.thumbnailUrl} title={c.title} />
                  </span>
                  <span className="min-w-0">
                    <span className="block font-serif text-[1.0625rem] leading-snug font-semibold text-ink">{c.title}</span>
                    <span className="mt-1.5 flex flex-wrap gap-1.5">
                      <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                      <StatusBadge tone={mk.tone}>{mk.label}</StatusBadge>
                    </span>
                    <span className="tnum mt-1.5 block text-xs text-muted">
                      {formatCount(c.lessonsCount)} lessons, {formatCount(c.studentsCount)} students
                      {c.ratingCount > 0 && c.ratingAverage ? `, rated ${c.ratingAverage.toFixed(1)}` : ""}. Updated {formatDay(c.updatedAt)}
                    </span>
                  </span>
                  <span className="col-span-2 flex items-baseline justify-between gap-4 sm:col-span-1 sm:block sm:text-right">
                    <span className="tnum block font-semibold text-ink">{c.isFree ? "Free" : formatMoney(c.priceMinor)}</span>
                    <span className="tnum block text-xs text-muted">{formatMoney(c.revenueMinor)} earned</span>
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
