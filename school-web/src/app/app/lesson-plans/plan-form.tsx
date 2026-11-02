"use client";

import type { AssignmentDto, LessonPlanDto, SaveLessonPlanRequest } from "@/lib/types/academic";
import { Input, Select, Textarea } from "@/components/ui";

const MAX_TOPIC = 200;
const MAX_TEXT = 4000;
const MAX_REMARKS = 2000;

export interface PlanDraft {
  week: string;
  date: string;
  topic: string;
  objectives: string;
  materials: string;
  methods: string;
  activities: string;
  previousKnowledge: string;
  introduction: string;
  presentation: string;
  conclusion: string;
  evaluation: string;
  assignmentId: string;
  teacherRemarks: string;
}

export function emptyPlan(): PlanDraft {
  return {
    week: "1",
    date: "",
    topic: "",
    objectives: "",
    materials: "",
    methods: "",
    activities: "",
    previousKnowledge: "",
    introduction: "",
    presentation: "",
    conclusion: "",
    evaluation: "",
    assignmentId: "",
    teacherRemarks: "",
  };
}

export function planFromDto(plan: LessonPlanDto): PlanDraft {
  return {
    week: String(plan.week),
    date: plan.date,
    topic: plan.topic,
    objectives: plan.objectives,
    materials: plan.materials,
    methods: plan.methods,
    activities: plan.activities,
    previousKnowledge: plan.previousKnowledge,
    introduction: plan.introduction,
    presentation: plan.presentation,
    conclusion: plan.conclusion,
    evaluation: plan.evaluation,
    assignmentId: plan.assignmentId ?? "",
    teacherRemarks: plan.teacherRemarks ?? "",
  };
}

export function toPlanRequest(classSubjectId: string, termId: string, draft: PlanDraft): SaveLessonPlanRequest {
  return {
    classSubjectId,
    termId,
    week: Number(draft.week),
    date: draft.date,
    topic: draft.topic.trim(),
    objectives: draft.objectives.trim(),
    materials: draft.materials.trim(),
    methods: draft.methods.trim(),
    activities: draft.activities.trim(),
    previousKnowledge: draft.previousKnowledge.trim(),
    introduction: draft.introduction.trim(),
    presentation: draft.presentation.trim(),
    conclusion: draft.conclusion.trim(),
    evaluation: draft.evaluation.trim(),
    assignmentId: draft.assignmentId || null,
    teacherRemarks: draft.teacherRemarks.trim() || null,
  };
}

const FIELDS: { key: keyof PlanDraft; label: string }[] = [
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

export function PlanForm({
  draft,
  onChange,
  assignments,
}: {
  draft: PlanDraft;
  onChange: (next: PlanDraft) => void;
  assignments: AssignmentDto[];
}) {
  const set = (patch: Partial<PlanDraft>) => onChange({ ...draft, ...patch });
  const known = assignments.some((a) => a.id === draft.assignmentId);
  return (
    <div className="w-full min-w-0 space-y-3">
      <div className="grid w-full min-w-0 gap-3 sm:grid-cols-2">
        <Input label="Week" type="number" min={1} max={20} required value={draft.week} onChange={(e) => set({ week: e.target.value })} hint="From 1 to 20." />
        <Input label="Date" type="date" required value={draft.date} onChange={(e) => set({ date: e.target.value })} />
      </div>
      <Input label="Topic" required maxLength={MAX_TOPIC} value={draft.topic} onChange={(e) => set({ topic: e.target.value })} />
      {FIELDS.map((field) => (
        <Textarea
          key={field.key}
          label={field.label}
          required
          maxLength={MAX_TEXT}
          value={draft[field.key]}
          onChange={(e) => set({ [field.key]: e.target.value } as Partial<PlanDraft>)}
        />
      ))}
      <Select
        label="Assignment"
        value={draft.assignmentId}
        hint="Optional. It has to be an assignment for this same class, subject and term."
        onChange={(e) => set({ assignmentId: e.target.value })}
      >
        <option value="">No linked assignment</option>
        {!known && draft.assignmentId && <option value={draft.assignmentId}>Linked assignment</option>}
        {assignments.map((a) => (
          <option key={a.id} value={a.id}>
            {a.title}
          </option>
        ))}
      </Select>
      <Textarea
        label="Teacher remarks"
        maxLength={MAX_REMARKS}
        value={draft.teacherRemarks}
        onChange={(e) => set({ teacherRemarks: e.target.value })}
        hint="Optional."
      />
    </div>
  );
}
