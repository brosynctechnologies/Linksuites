"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect } from "react";
import { useApi } from "@/lib/use-api";
import type { LearnCourseDto } from "@/lib/types";
import { ApiErrorView, EmptyState, PageLoader, buttonClass } from "@/components/ui";

/** /learn/{courseId}: jump to where the learner left off (or the first lesson). */
export default function ResumeCoursePage() {
  const { courseId } = useParams<{ courseId: string }>();
  const router = useRouter();
  const { data, error, reload } = useApi<LearnCourseDto>(`/api/learn/courses/${courseId}`);
  const target = data ? (data.resumeLessonId ?? data.sections.flatMap((s) => s.lessons)[0]?.id ?? null) : null;

  useEffect(() => {
    if (target) router.replace(`/learn/${courseId}/${target}`);
  }, [target, courseId, router]);

  if (error) {
    return (
      <div className="mx-auto max-w-lg px-4 py-12">
        {error.status === 403 ? (
          <EmptyState
            title="You're not enrolled in this course"
            text="Enrol from the course page to start learning."
            action={
              <Link href="/courses" className={buttonClass("primary")}>
                Browse courses
              </Link>
            }
          />
        ) : (
          <ApiErrorView error={error} onRetry={reload} />
        )}
      </div>
    );
  }
  if (data && !target) {
    return (
      <div className="mx-auto max-w-lg px-4 py-12">
        <EmptyState title="No lessons yet" text="The tutor hasn't added lessons to this course yet." action={<Link href="/learn" className={buttonClass("secondary")}>Back to My learning</Link>} />
      </div>
    );
  }
  return <PageLoader label="Opening your course…" />;
}
