"use client";

import { useState } from "react";
import { asList } from "@/lib/api";
import { useCan } from "@/lib/permissions";
import { qs, useApi } from "@/lib/use-api";
import type { QuestionDto } from "@/lib/types";
import { CourseFilter } from "@/components/course-filter";
import { MessageIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, Segmented } from "@/components/ui";
import { QuestionThread } from "@/components/question-thread";

/** Q&A inbox: every learner question across the academy's courses, unanswered first by default. */
export default function QuestionsPage() {
  const canAnswer = useCan("questions.answer");
  const [courseId, setCourseId] = useState("");
  const [show, setShow] = useState<"unanswered" | "all">("unanswered");
  const { data, error, reload, setData } = useApi<QuestionDto[]>(`/api/studio/questions${qs({ courseId, unanswered: show === "unanswered" ? true : undefined })}`);
  const list = asList(data);

  return (
    <div>
      <PageHeader title="Questions" subtitle="Learners ask under each lesson. Answers are visible to everyone on the course." />
      <div className="mb-4 grid gap-3 sm:grid-cols-[14rem_minmax(0,16rem)]">
        <Segmented
          label="Show"
          value={show}
          onChange={setShow}
          items={[
            { value: "unanswered", label: "Unanswered" },
            { value: "all", label: "All" },
          ]}
        />
        <CourseFilter value={courseId} onChange={setCourseId} />
      </div>
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState icon={<MessageIcon />} title={show === "unanswered" ? "You're all caught up" : "No questions yet"} text={show === "unanswered" ? "Every question has an answer." : "Questions from learners appear here."} />
      )}
      {list.length > 0 && (
        <ul className="space-y-3">
          {list.map((q) =>
            canAnswer ? (
              <QuestionThread
                key={q.id}
                question={q}
                answerPath={`/api/studio/questions/${q.id}/answers`}
                onChange={(nq) => setData((xs) => asList(xs).map((x) => (x.id === nq.id ? nq : x)))}
                context={
                  <p className="mb-2 text-xs text-muted">
                    {q.courseTitle}
                    {q.lessonTitle ? `: ${q.lessonTitle}` : ""}
                  </p>
                }
              />
            ) : (
              <li key={q.id} className="rounded-panel border border-line bg-surface p-4">
                <p className="mb-2 text-xs text-muted">
                  {q.courseTitle}
                  {q.lessonTitle ? `: ${q.lessonTitle}` : ""}
                </p>
                <p className="text-sm font-semibold text-ink">{q.authorName}</p>
                <p className="mt-1 text-[0.9375rem] whitespace-pre-line text-slate-800">{q.body}</p>
                <p className="mt-2 text-xs text-muted">{q.answers.length} answer{q.answers.length === 1 ? "" : "s"}</p>
              </li>
            ),
          )}
        </ul>
      )}
    </div>
  );
}
