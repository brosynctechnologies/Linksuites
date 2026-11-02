"use client";

import Link from "next/link";
import { api } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { LessonPlanDto, LessonPlanSettingsDto, LessonPlanStatus } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, Notice, PageHeader, PageLoader, RuledSheet, buttonClass, type BadgeTone } from "@/components/ui";
import { PlusIcon } from "@/components/icons";
import { AcademicNoAccess, useReadOnly } from "../scores/academic-shared";
import { SubjectTermBar, useSubjectTerm } from "../teaching-shared";

const STATUS: Record<LessonPlanStatus, { label: string; tone: BadgeTone }> = {
  Draft: { label: "Draft", tone: "neutral" },
  Submitted: { label: "Submitted", tone: "blazer" },
  Accepted: { label: "Accepted", tone: "pass" },
  Returned: { label: "Returned", tone: "crest" },
};

export default function LessonPlansPage() {
  const role = useRole();
  const readOnly = useReadOnly();
  const canManage = useCan("school.manage");
  const state = useSubjectTerm();

  if (role === "Accountant") return <AcademicNoAccess what="Lesson plans" />;

  const error = state.academic.error ?? state.terms.error;
  const loaded = state.academic.loaded && state.terms.loaded;
  const canWrite = !readOnly && !!state.classSubjectId;

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Lesson plans"
        subtitle="A weekly plan for one subject. A draft stays with the teacher who wrote it until they submit it."
        action={
          canWrite && (
            <Link href={`/app/lesson-plans/new?classSubjectId=${state.classSubjectId}&termId=${state.termId}`} className={buttonClass("primary")}>
              <PlusIcon className="h-4 w-4" /> Write a lesson plan
            </Link>
          )
        }
      />
      {canManage && <ReviewSetting />}
      {error && (
        <ApiErrorView
          error={error}
          onRetry={() => {
            state.academic.reload();
            state.terms.reload();
          }}
        />
      )}
      {!loaded && !error && <PageLoader />}
      {loaded && state.classes.length === 0 && (
        <EmptyState
          title="No subjects for a lesson plan"
          text={
            state.academic.leadership
              ? "This session has no classes yet. Add them in Setup."
              : "You don't teach a subject in any class this session. Ask the principal to assign you."
          }
        />
      )}
      {loaded && state.classes.length > 0 && (
        <>
          <SubjectTermBar state={state} />
          <ApiErrorView error={state.subjectsError} onRetry={state.reloadSubjects} />
          {!state.classId && <p className="text-sm text-muted">Choose a class to see its lesson plans.</p>}
          {state.classId && !state.subjectsLoading && state.subjects.length === 0 && (
            <Notice>This class has no subjects you can write a plan for.</Notice>
          )}
          {state.classSubjectId && state.termId && (
            <PlanList key={`${state.classSubjectId}:${state.termId}`} classSubjectId={state.classSubjectId} termId={state.termId} />
          )}
        </>
      )}
    </div>
  );
}

function ReviewSetting() {
  const setting = useApi<LessonPlanSettingsDto>("/api/school/settings/lesson-plans");
  const needReview = setting.data?.needReview ?? null;
  const save = useAction();

  async function set(value: boolean) {
    const res = await save.run(() => api<LessonPlanSettingsDto>("/api/school/settings/lesson-plans", { method: "PUT", body: { needReview: value } }));
    if (res.ok) setting.setData(res.data);
  }

  return (
    <div className="w-full min-w-0 space-y-3 rounded-panel border border-rule bg-surface p-4">
      <p className="text-sm text-slate-700">
        New schools ask the principal to review each lesson plan. You can accept a plan as soon as the teacher submits it.
      </p>
      <ApiErrorView error={setting.error} onRetry={setting.reload} />
      {needReview === true && <Notice tone="success">The principal reviews each plan before it is accepted.</Notice>}
      {needReview === false && <Notice tone="success">A plan is accepted as soon as the teacher submits it.</Notice>}
      <ErrorBox error={save.error} />
      <div className="flex w-full min-w-0 flex-wrap gap-2">
        <Button variant={needReview === true ? "secondary" : "primary"} loading={save.busy} onClick={() => set(true)}>
          Ask the principal to review
        </Button>
        <Button variant={needReview === false ? "secondary" : "primary"} loading={save.busy} onClick={() => set(false)}>
          Accept on submit
        </Button>
      </div>
    </div>
  );
}

function PlanList({ classSubjectId, termId }: { classSubjectId: string; termId: string }) {
  const list = useApi<LessonPlanDto[]>(`/api/school/lesson-plans${qs({ classSubjectId, termId })}`);
  if (list.error) return <ApiErrorView error={list.error} onRetry={list.reload} />;
  if (!list.data) return <PageLoader label="Opening lesson plans…" />;
  if (list.data.length === 0) {
    return <EmptyState title="No lesson plans yet" text="Write this week's plan, then submit it. A draft another teacher wrote is not listed here." />;
  }
  return (
    <RuledSheet label="Lesson plans" template="minmax(0,1fr) 8rem 7rem" head={[{ label: "Plan" }, { label: "Date" }, { label: "Status" }]} busy={list.loading}>
      {list.data.map((plan) => {
        const status = STATUS[plan.status] ?? { label: plan.status, tone: "neutral" as BadgeTone };
        return (
          <li key={plan.id}>
            <Link href={`/app/lesson-plans/${plan.id}`} className="ruled-pad grid grid-cols-1 items-center gap-1 py-3 hover:bg-slate-50 md:grid-cols-[minmax(0,1fr)_8rem_7rem] md:gap-4">
              <span className="min-w-0">
                <span className="block font-bold break-words text-ink">
                  Week {plan.week}: {plan.topic}
                </span>
              </span>
              <span className="tnum text-sm text-muted">{formatDay(plan.date)}</span>
              <Badge tone={status.tone}>{status.label}</Badge>
            </Link>
          </li>
        );
      })}
    </RuledSheet>
  );
}
