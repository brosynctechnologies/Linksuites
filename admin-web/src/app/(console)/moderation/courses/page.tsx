"use client";

import { RequireArea, useQueues } from "@/components/console";
import { ExternalIcon } from "@/components/icons";
import { Facts, Mono, PageHeader } from "@/components/ui";
import { Queue } from "@/components/queue";
import { api } from "@/lib/api";
import { money, num, stamp } from "@/lib/format";
import type { CourseReviewItem } from "@/lib/types";
import { useApi } from "@/lib/use-api";

export default function CoursesQueuePage() {
  return (
    <RequireArea area="moderation">
      <CoursesQueue />
    </RequireArea>
  );
}

function CoursesQueue() {
  const { data, error, reload, setData } = useApi<CourseReviewItem[]>("/api/admin/course-reviews");
  const { refresh } = useQueues();

  return (
    <>
      <PageHeader title="Courses" subtitle="Courses submitted to the LearnLink marketplace. Approving lists them in the shared catalogue; rejecting sends your note to the tutor." />
      <Queue
        items={data}
        error={error}
        reload={reload}
        config={{
          label: "Courses queue",
          getId: (c) => c.courseId,
          getTitle: (c) => c.title,
          approveLabel: "Approve",
          rejectLabel: "Reject",
          rejectNote: { label: "Note to the tutor", prompt: (c) => `Tell ${c.academyName} what to change in "${c.title}" before it can be listed.`, required: true },
          decide: async (c, approve, note) => {
            await api(`/api/admin/course-reviews/${c.courseId}`, { method: "POST", body: { approve, note } });
            setData((xs) => (xs ?? []).filter((x) => x.courseId !== c.courseId));
            refresh();
            return approve ? "Course approved" : "Course rejected";
          },
          emptyTitle: "No courses waiting for review",
          emptyText: "Courses tutors submit to the marketplace appear here.",
          renderRow: (c) => (
            <>
              <span className="block truncate font-medium">{c.title}</span>
              <span className="block truncate text-xs text-muted">
                {c.academyName} · {stamp(c.submittedAt)}
              </span>
            </>
          ),
          renderDetail: (c) => (
            <div className="space-y-4">
              <h2 className="text-lg font-semibold">{c.title}</h2>
              <Facts
                items={[
                  { label: "Academy", value: c.academyName },
                  { label: "Price", value: <span className="tnum">{c.priceMinor > 0 ? money(c.priceMinor) : "Free"}</span> },
                  { label: "Lessons", value: <span className="tnum">{num(c.lessonsCount)}</span> },
                  { label: "Submitted", value: stamp(c.submittedAt) },
                  { label: "Course id", value: <Mono>{c.courseId}</Mono> },
                ]}
              />
              {c.courseUrl && (
                <a
                  href={c.courseUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex min-h-10 items-center gap-1.5 rounded-control border border-line px-3 text-sm font-medium hover:bg-concrete-2"
                >
                  Open the course page <ExternalIcon className="h-4 w-4" />
                </a>
              )}
            </div>
          ),
        }}
      />
    </>
  );
}
