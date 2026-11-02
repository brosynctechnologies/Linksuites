"use client";

import Link from "next/link";
import { asList } from "@/lib/api";
import { firstName, useAuth } from "@/lib/auth";
import { coursePath, formatDay } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { MyCourseDto } from "@/lib/types";
import { Thumb } from "@/components/course-card";
import { AwardIcon, BookIcon } from "@/components/icons";
import { PathStrip } from "@/components/learning-path";
import { ApiErrorView, EmptyState, PageLoader, buttonClass, cx } from "@/components/ui";

function resumeHref(c: MyCourseDto) {
  return c.lastLessonId ? `/learn/${c.courseId}/${c.lastLessonId}` : `/learn/${c.courseId}`;
}

export default function MyLearningPage() {
  const auth = useAuth();
  const { data, error, reload } = useApi<MyCourseDto[]>("/api/learn/enrollments");
  const courses = asList(data);
  const inProgress = courses.filter((c) => !c.completedAt);
  const finished = courses.filter((c) => !!c.completedAt);
  const certificates = courses.filter((c) => c.certificateCode).length;

  return (
    <div>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p className="text-sm text-muted">Welcome back, {firstName(auth?.user.fullName)}</p>
          <h1 className="mt-0.5 font-serif text-2xl font-semibold tracking-[-0.01em] text-ink sm:text-3xl">My learning</h1>
        </div>
        <Link href="/learn/certificates" className={buttonClass("secondary")}>
          <AwardIcon className="h-4 w-4" />
          Certificates{certificates ? ` (${certificates})` : ""}
        </Link>
      </div>

      <div className="mt-6">
        {!data && error && <ApiErrorView error={error} onRetry={reload} />}
        {!data && !error && <PageLoader />}
        {data && courses.length === 0 && (
          <EmptyState
            icon={<BookIcon />}
            title="No courses yet"
            text="Courses you enrol in appear here, with your progress saved on every device."
            action={
              <Link href="/courses" className={buttonClass("primary")}>
                Browse courses
              </Link>
            }
          />
        )}
        {inProgress.length > 0 && (
          <section aria-labelledby="in-progress">
            <h2 id="in-progress" className="sr-only">
              In progress
            </h2>
            <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
              {inProgress.map((c, i) => (
                <CourseLine key={c.courseId} course={c} highlight={i === 0} />
              ))}
            </ul>
          </section>
        )}
        {finished.length > 0 && (
          <section aria-labelledby="finished" className="mt-9">
            <h2 id="finished" className="mb-3 text-lg font-semibold text-ink">
              Finished
            </h2>
            <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
              {finished.map((c) => (
                <CourseLine key={c.courseId} course={c} />
              ))}
            </ul>
          </section>
        )}
      </div>
    </div>
  );
}

function CourseLine({ course: c, highlight }: { course: MyCourseDto; highlight?: boolean }) {
  const started = c.lessonsDone > 0;
  const pct = Math.round(c.progressPercent ?? 0);
  return (
    <li className="grid grid-cols-[5.5rem_minmax(0,1fr)] gap-x-4 gap-y-3 p-4 sm:grid-cols-[8.5rem_minmax(0,1fr)_auto] sm:items-center sm:p-5">
      <Link href={resumeHref(c)} className="block overflow-hidden rounded-[8px]" tabIndex={-1} aria-hidden="true">
        <Thumb src={c.thumbnailUrl} title={c.title} />
      </Link>
      <div className="min-w-0">
        <h3 className="font-serif text-[1.0625rem] leading-snug font-semibold text-ink">
          <Link href={resumeHref(c)} className="hover:underline">
            {c.title}
          </Link>
        </h3>
        <p className="mt-0.5 truncate text-sm text-muted">{c.tutorName}</p>
        <PathStrip done={c.lessonsDone} total={c.lessonsTotal} certified={!!c.certificateCode} className="mt-2.5 max-w-md" />
        <p className="mt-1 text-xs text-muted">
          <span className="tnum font-semibold text-lime-dark">{pct}%</span>
          <span className="tnum">
            {" "}
            {c.lessonsDone} of {c.lessonsTotal} lessons
          </span>
          {c.completedAt && <span>. Finished {formatDay(c.completedAt)}</span>}
        </p>
      </div>
      <div className="col-span-2 flex flex-wrap gap-2 sm:col-span-1 sm:flex-col sm:items-stretch">
        <Link href={resumeHref(c)} className={buttonClass(highlight || !c.completedAt ? "primary" : "secondary", "flex-1 sm:flex-none")}>
          {c.completedAt ? "Review course" : started ? "Resume" : "Start course"}
        </Link>
        {c.certificateCode ? (
          <Link href={`/certificates/${encodeURIComponent(c.certificateCode)}`} className={cx(buttonClass("secondary"), "flex-1 sm:flex-none")}>
            <AwardIcon className="h-4 w-4 text-lime-dark" /> Certificate
          </Link>
        ) : (
          <Link href={coursePath(c.academySlug, c.slug)} className={cx(buttonClass("ghost"), "flex-1 sm:flex-none")}>
            Course page
          </Link>
        )}
      </div>
    </li>
  );
}
