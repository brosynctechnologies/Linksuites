"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { AssignmentDto, LessonPlanDto } from "@/lib/types/academic";
import { AcademicNoAccess, useReadOnly } from "../../scores/academic-shared";
import { ApiErrorView, Button, EmptyState, ErrorBox, PageHeader } from "@/components/ui";
import { PlanForm, emptyPlan, toPlanRequest, type PlanDraft } from "../plan-form";

export default function NewLessonPlanPage() {
  const role = useRole();
  const readOnly = useReadOnly();
  const params = useSearchParams();
  const classSubjectId = params.get("classSubjectId") ?? "";
  const termId = params.get("termId") ?? "";

  if (role === "Accountant") return <AcademicNoAccess what="Lesson plans" />;
  if (!classSubjectId || !termId) {
    return (
      <div className="min-w-0">
        <PageHeader title="New lesson plan" back="/app/lesson-plans" />
        <EmptyState title="Choose a class and subject" text="Open lesson plans, pick the class, subject and term, then write the plan." />
      </div>
    );
  }
  if (readOnly) {
    return (
      <div className="min-w-0">
        <PageHeader title="New lesson plan" back="/app/lesson-plans" />
        <EmptyState title="Read only" text="A support view can look at lesson plans, not write one." />
      </div>
    );
  }
  return <NewPlan classSubjectId={classSubjectId} termId={termId} />;
}

function NewPlan({ classSubjectId, termId }: { classSubjectId: string; termId: string }) {
  const router = useRouter();
  const [draft, setDraft] = useState<PlanDraft>(emptyPlan);
  const create = useAction();
  const assignments = useApi<AssignmentDto[]>(`/api/school/assignments${qs({ classSubjectId, termId })}`);

  async function save() {
    const res = await create.run(() =>
      api<LessonPlanDto>("/api/school/lesson-plans", { method: "POST", body: toPlanRequest(classSubjectId, termId, draft) }),
    );
    if (res.ok) router.push(`/app/lesson-plans/${res.data.id}`);
  }

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader title="New lesson plan" subtitle="This starts as a draft. Submit it when it is ready." back="/app/lesson-plans" />
      <ApiErrorView error={assignments.error} onRetry={assignments.reload} />
      <ErrorBox error={create.error} />
      <PlanForm draft={draft} onChange={setDraft} assignments={assignments.data ?? []} />
      <Button loading={create.busy} onClick={save}>
        Save draft
      </Button>
    </div>
  );
}
