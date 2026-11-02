"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { formatDay, formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import { useAuth } from "@/lib/auth";
import type { AssignmentDto, LessonPlanDto, LessonPlanStatus } from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, ErrorBox, Notice, PageHeader, PageLoader, Textarea, type BadgeTone } from "@/components/ui";
import { AcademicNoAccess, useReadOnly } from "../../scores/academic-shared";
import { PlanForm, planFromDto, toPlanRequest, type PlanDraft } from "../plan-form";

const STATUS: Record<LessonPlanStatus, { label: string; tone: BadgeTone }> = {
  Draft: { label: "Draft", tone: "neutral" },
  Submitted: { label: "Submitted", tone: "blazer" },
  Accepted: { label: "Accepted", tone: "pass" },
  Returned: { label: "Returned", tone: "crest" },
};

const BLOCKS: { key: keyof LessonPlanDto; label: string }[] = [
  { key: "objectives", label: "Objectives" },
  { key: "materials", label: "Materials" },
  { key: "methods", label: "Methods" },
  { key: "activities", label: "Activities" },
  { key: "previousKnowledge", label: "Previous knowledge" },
  { key: "introduction", label: "Introduction" },
  { key: "presentation", label: "Presentation" },
  { key: "conclusion", label: "Conclusion" },
  { key: "evaluation", label: "Evaluation" },
];

export default function LessonPlanPage() {
  const role = useRole();
  const { id } = useParams<{ id: string }>();
  if (role === "Accountant") return <AcademicNoAccess what="Lesson plans" />;
  return <Plan id={id} />;
}

function Plan({ id }: { id: string }) {
  const plan = useApi<LessonPlanDto>(`/api/school/lesson-plans/${id}`);
  if (plan.error) {
    return (
      <div className="min-w-0 space-y-4">
        <PageHeader title="Lesson plan" back="/app/lesson-plans" />
        <ApiErrorView error={plan.error} onRetry={plan.reload} />
      </div>
    );
  }
  if (!plan.data) return <PageLoader label="Opening the lesson plan…" />;
  return <PlanBody plan={plan.data} onChange={plan.setData} />;
}

function PlanBody({ plan, onChange }: { plan: LessonPlanDto; onChange: (plan: LessonPlanDto) => void }) {
  const auth = useAuth();
  const readOnly = useReadOnly();
  const canManage = useCan("school.manage");
  const mine = !!auth?.user.id && plan.createdBy.toLowerCase() === auth.user.id.toLowerCase();
  const editable = mine && !readOnly && (plan.status === "Draft" || plan.status === "Returned");
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<PlanDraft>(() => planFromDto(plan));
  const [note, setNote] = useState("");
  const save = useAction();
  const submit = useAction();
  const review = useAction();
  const assignments = useApi<AssignmentDto[]>(`/api/school/assignments${qs({ classSubjectId: plan.classSubjectId, termId: plan.termId })}`);
  const status = STATUS[plan.status] ?? { label: plan.status, tone: "neutral" as BadgeTone };
  const linked = (assignments.data ?? []).find((a) => a.id === plan.assignmentId);

  async function saveDraft() {
    const res = await save.run(() =>
      api<LessonPlanDto>(`/api/school/lesson-plans/${plan.id}`, {
        method: "PUT",
        body: toPlanRequest(plan.classSubjectId, plan.termId, draft),
      }),
    );
    if (res.ok) {
      onChange(res.data);
      setDraft(planFromDto(res.data));
      setEditing(false);
    }
  }

  async function send() {
    const res = await submit.run(() => api<LessonPlanDto>(`/api/school/lesson-plans/${plan.id}/submit`, { method: "POST" }));
    if (res.ok) onChange(res.data);
  }

  async function accept() {
    const res = await review.run(() => api<LessonPlanDto>(`/api/school/lesson-plans/${plan.id}/accept`, { method: "POST" }));
    if (res.ok) onChange(res.data);
  }

  async function sendBack() {
    const res = await review.run(() =>
      api<LessonPlanDto>(`/api/school/lesson-plans/${plan.id}/return`, { method: "POST", body: { note: note.trim() } }),
    );
    if (res.ok) {
      onChange(res.data);
      setNote("");
    }
  }

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title={plan.topic}
        subtitle={`Week ${plan.week} · ${formatDay(plan.date)}`}
        back="/app/lesson-plans"
        action={<Badge tone={status.tone}>{status.label}</Badge>}
      />
      {plan.submittedAt && <p className="text-sm text-muted">Submitted {formatStamp(plan.submittedAt)}</p>}
      {plan.status === "Returned" && plan.reviewerNote && (
        <Notice tone="warning">
          <span className="font-semibold">Returned. </span>
          <span className="break-words">{plan.reviewerNote}</span>
        </Notice>
      )}
      {plan.status === "Accepted" && (
        <Notice tone="success">{plan.reviewedBy ? "The principal accepted this plan." : "Accepted when it was submitted."}</Notice>
      )}
      {plan.status === "Submitted" && !canManage && <Notice>Waiting for the principal to accept it or return it with a note.</Notice>}
      <ErrorBox error={save.error ?? submit.error ?? review.error} />
      <ApiErrorView error={assignments.error} onRetry={assignments.reload} />

      {editing ? (
        <div className="w-full min-w-0 space-y-3">
          <PlanForm draft={draft} onChange={setDraft} assignments={assignments.data ?? []} />
          <div className="flex flex-wrap gap-2">
            <Button loading={save.busy} onClick={saveDraft}>
              Save
            </Button>
            <Button variant="secondary" onClick={() => setEditing(false)} disabled={save.busy}>
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        <div className="w-full min-w-0 space-y-4">
          {BLOCKS.map((block) => (
            <Block key={block.key} label={block.label} text={String(plan[block.key] ?? "")} />
          ))}
          {plan.teacherRemarks && <Block label="Teacher remarks" text={plan.teacherRemarks} />}
          {plan.assignmentId && <Block label="Assignment" text={linked?.title ?? "Linked to an assignment for this subject and term."} />}
          <div className="flex flex-wrap gap-2">
            {editable && (
              <Button
                variant="secondary"
                onClick={() => {
                  setDraft(planFromDto(plan));
                  setEditing(true);
                }}
              >
                Edit
              </Button>
            )}
            {editable && (
              <Button loading={submit.busy} onClick={send}>
                Submit
              </Button>
            )}
          </div>
        </div>
      )}

      {canManage && plan.status === "Submitted" && (
        <div className="w-full min-w-0 space-y-3 border-t border-rule-soft pt-4">
          <p className="text-sm text-slate-700">Accept this plan, or return it with a note so the teacher can change it.</p>
          <Button loading={review.busy} onClick={accept}>
            Accept
          </Button>
          <Textarea label="Note" value={note} maxLength={2000} onChange={(e) => setNote(e.target.value)} hint="The teacher sees this when the plan is returned." />
          <Button variant="secondary" loading={review.busy} disabled={!note.trim()} onClick={sendBack}>
            Return to the teacher
          </Button>
        </div>
      )}
    </div>
  );
}

function Block({ label, text }: { label: string; text: string }) {
  if (!text) return null;
  return (
    <div className="min-w-0">
      <h2 className="text-xs font-bold text-muted">{label}</h2>
      <p className="mt-1 text-sm break-words whitespace-pre-line text-slate-800">{text}</p>
    </div>
  );
}
