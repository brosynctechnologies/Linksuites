"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useRole } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { AssessmentDto, ComponentDto } from "@/lib/types";
import type { AssignmentDto, AssignmentSubmissionDto, CreateAssignmentRequest, MarkAssignmentRequest } from "@/lib/types/academic";
import {
  ApiErrorView,
  Badge,
  Button,
  EmptyState,
  ErrorBox,
  Input,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  RuledSheet,
  Select,
  Textarea,
  linkClass,
} from "@/components/ui";
import { PlusIcon } from "@/components/icons";
import { AcademicNoAccess, score, useReadOnly } from "../scores/academic-shared";
import { SubjectTermBar, useSubjectTerm } from "../teaching-shared";

const MAX_TITLE = 150;
const MAX_INSTRUCTIONS = 5000;
const MAX_FEEDBACK = 500;
const FILE_HINT = "PDF, or a JPEG, PNG or WebP image, up to 5 MB.";

export default function AssignmentsPage() {
  const role = useRole();
  const readOnly = useReadOnly();
  const state = useSubjectTerm();
  const [creating, setCreating] = useState(false);
  const assessment = useApi<AssessmentDto>(
    state.levelId && state.levelLoaded && !state.developmental ? `/api/school/assessment?levelId=${state.levelId}` : null,
  );

  if (role === "Accountant") return <AcademicNoAccess what="Assignments" />;

  const error = state.academic.error ?? state.terms.error;
  const loaded = state.academic.loaded && state.terms.loaded;
  const canWrite = !readOnly && !!state.classSubjectId;

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Assignments"
        subtitle="Set work for a subject you teach, then mark it or record that nursery work was received."
        action={
          canWrite && state.levelLoaded && (
            <Button onClick={() => setCreating(true)}>
              <PlusIcon className="h-4 w-4" /> Set an assignment
            </Button>
          )
        }
      />
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
          title="No subjects to set work for"
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
          <ApiErrorView error={state.lookupError} onRetry={state.reloadLookups} />
          <ApiErrorView error={assessment.error} onRetry={assessment.reload} />
          {!state.classId && <p className="text-sm text-muted">Choose a class to see its assignments.</p>}
          {state.classId && !state.subjectsLoading && state.subjects.length === 0 && (
            <Notice>This class has no subjects you can set work for. A subject teacher or the principal sets assignments.</Notice>
          )}
          {state.classSubjectId && state.termId && (
            <AssignmentList
              key={`${state.classSubjectId}:${state.termId}`}
              classSubjectId={state.classSubjectId}
              termId={state.termId}
              developmental={state.developmental}
              levelLoaded={state.levelLoaded}
              components={assessment.data?.components ?? []}
              canWrite={canWrite}
              creating={creating}
              onCloseCreate={() => setCreating(false)}
            />
          )}
        </>
      )}
    </div>
  );
}

function AssignmentList({
  classSubjectId,
  termId,
  developmental,
  levelLoaded,
  components,
  canWrite,
  creating,
  onCloseCreate,
}: {
  classSubjectId: string;
  termId: string;
  developmental: boolean;
  levelLoaded: boolean;
  components: ComponentDto[];
  canWrite: boolean;
  creating: boolean;
  onCloseCreate: () => void;
}) {
  const list = useApi<AssignmentDto[]>(`/api/school/assignments${qs({ classSubjectId, termId })}`);
  if (list.error) return <ApiErrorView error={list.error} onRetry={list.reload} />;
  if (!list.data) return <PageLoader label="Opening assignments…" />;
  const rows = list.data;

  return (
    <div className="min-w-0 space-y-4">
      {developmental && levelLoaded && (
        <Notice>This class uses developmental assessment. Record whether the work was received, and leave feedback. It is not a score.</Notice>
      )}
      {rows.length === 0 && <EmptyState title="No assignments yet" text="Set the title, the instructions and when the work is due." />}
      {rows.length > 0 && (
        <RuledSheet label="Assignments" busy={list.loading}>
          {rows.map((a) => (
            <li key={a.id} className="ruled-pad min-w-0 space-y-3 py-4">
              <AssignmentHead assignment={a} components={components} />
              <Roster
                assignment={a}
                developmental={developmental}
                levelLoaded={levelLoaded}
                components={components}
                canWrite={canWrite}
                onSaved={(saved) => list.setData((current) => (current ?? []).map((row) => (row.id === saved.id ? saved : row)))}
                onImported={() => list.reload()}
              />
            </li>
          ))}
        </RuledSheet>
      )}
      {creating && (
        <CreateDialog
          classSubjectId={classSubjectId}
          termId={termId}
          developmental={developmental}
          components={components}
          onClose={onCloseCreate}
          onCreated={() => list.reload()}
        />
      )}
    </div>
  );
}

function AssignmentHead({ assignment, components }: { assignment: AssignmentDto; components: ComponentDto[] }) {
  const component = components.find((c) => c.id === assignment.assessmentComponentId);
  return (
    <div className="min-w-0">
      <div className="flex min-w-0 flex-wrap items-start justify-between gap-2">
        <h2 className="min-w-0 font-bold break-words text-ink">{assignment.title}</h2>
        <div className="flex flex-wrap gap-1.5">
          {assignment.maxMarks !== null && <Badge>Out of {score(assignment.maxMarks)}</Badge>}
          <Badge tone={component ? "blazer" : "neutral"}>{component ? component.name : "Practice"}</Badge>
        </div>
      </div>
      <p className="mt-0.5 text-xs text-muted">
        Due <span className="tnum">{formatStamp(assignment.dueAt)}</span>
      </p>
      <p className="mt-2 text-sm break-words whitespace-pre-line text-slate-800">{assignment.instructions}</p>
    </div>
  );
}

function Roster({
  assignment,
  developmental,
  levelLoaded,
  components,
  canWrite,
  onSaved,
  onImported,
}: {
  assignment: AssignmentDto;
  developmental: boolean;
  levelLoaded: boolean;
  components: ComponentDto[];
  canWrite: boolean;
  onSaved: (saved: AssignmentDto) => void;
  onImported: () => void;
}) {
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  // Students whose mark or feedback is typed but not saved. An import reloads the roster, so it waits until there are none.
  const [dirty, setDirty] = useState<ReadonlySet<string>>(() => new Set());
  const markDirty = useCallback((enrolmentId: string, isDirty: boolean) => {
    setDirty((current) => {
      if (current.has(enrolmentId) === isDirty) return current;
      const next = new Set(current);
      if (isDirty) next.add(enrolmentId);
      else next.delete(enrolmentId);
      return next;
    });
  }, []);
  const importBlocked = busyId
    ? "Wait for the mark being saved, then import."
    : dirty.size > 0
      ? "Save or undo your typed marks and feedback before importing."
      : undefined;
  const component = components.find((c) => c.id === assignment.assessmentComponentId);
  const limit = assignment.maxMarks ?? component?.maxScore ?? (assignment.assessmentComponentId ? null : 1000);

  async function save(enrolmentId: string, body: MarkAssignmentRequest) {
    setBusyId(enrolmentId);
    setError(null);
    try {
      const saved = await api<AssignmentDto>(`/api/school/assignments/${assignment.id}/submissions/${enrolmentId}`, { method: "PUT", body });
      onSaved(saved);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="min-w-0 space-y-3 border-t border-rule-soft pt-3">
      <ErrorBox error={error} />
      {!levelLoaded && <p className="text-sm text-muted">Checking how this class is assessed…</p>}
      {canWrite && levelLoaded && (
        <ImportDialog
          kind="assignment-marks"
          scope={{ assignmentId: assignment.id }}
          title={`Import ${developmental ? "received work" : "marks"} for ${assignment.title}`}
          disabledReason={importBlocked}
          onDone={onImported}
        />
      )}
      <ul className="min-w-0 space-y-3" aria-label="Class roster">
        {assignment.submissions.map((row) => (
          <SubmissionRow
            key={`${row.enrolmentId}:${row.status}:${row.mark ?? ""}:${row.submittedAt ?? ""}:${row.feedback ?? ""}`}
            row={row}
            developmental={developmental && levelLoaded}
            numeric={levelLoaded && !developmental}
            limit={limit}
            canWrite={canWrite}
            busy={busyId === row.enrolmentId}
            onSave={(body) => save(row.enrolmentId, body)}
            onDirty={markDirty}
          />
        ))}
      </ul>
    </div>
  );
}

function SubmissionRow({
  row,
  developmental,
  numeric,
  limit,
  canWrite,
  busy,
  onSave,
  onDirty,
}: {
  row: AssignmentSubmissionDto;
  developmental: boolean;
  numeric: boolean;
  limit: number | null;
  canWrite: boolean;
  busy: boolean;
  onSave: (body: MarkAssignmentRequest) => void;
  onDirty: (enrolmentId: string, dirty: boolean) => void;
}) {
  const savedMark = row.mark === null || row.mark === undefined ? "" : score(row.mark);
  const [mark, setMark] = useState(savedMark);
  const [feedback, setFeedback] = useState(row.feedback ?? "");
  const isDirty = mark.trim() !== savedMark || feedback.trim() !== (row.feedback ?? "").trim();
  useEffect(() => {
    onDirty(row.enrolmentId, isDirty);
  }, [onDirty, row.enrolmentId, isDirty]);
  useEffect(() => () => onDirty(row.enrolmentId, false), [onDirty, row.enrolmentId]);
  const [localError, setLocalError] = useState<string | null>(null);

  function withFeedback(body: MarkAssignmentRequest): MarkAssignmentRequest {
    const text = feedback.trim();
    return text ? { ...body, feedback: text } : body;
  }

  function saveMark() {
    const parsed = parseMark(mark, limit);
    if ("error" in parsed) {
      setLocalError(parsed.error);
      return;
    }
    setLocalError(null);
    onSave(withFeedback({ mark: parsed.value }));
  }

  return (
    <li className="w-full min-w-0 space-y-2 rounded-control bg-slate-50/80 px-3 py-3">
      <div className="flex min-w-0 flex-wrap items-baseline justify-between gap-2">
        <p className="min-w-0 font-semibold break-words text-ink">{row.name}</p>
        <span className="tnum text-xs text-muted">{row.admissionNo}</span>
      </div>
      <div className="flex flex-wrap gap-1.5">
        <Badge tone={row.status === "Returned" ? "pass" : row.status === "Received" ? "blazer" : "neutral"}>
          {statusLabel(row.status, developmental)}
        </Badge>
        {row.late && <Badge tone="crest">Late</Badge>}
        {row.mark !== null && <Badge>Mark {score(row.mark)}</Badge>}
      </div>
      {row.submittedAt && <p className="text-xs text-muted">Sent {formatStamp(row.submittedAt)}</p>}
      {row.body && <p className="text-sm break-words whitespace-pre-line text-slate-800">{row.body}</p>}
      {row.fileUrl && (
        <a href={row.fileUrl} target="_blank" rel="noopener noreferrer" className={`${linkClass} inline-flex min-h-9 items-center text-sm`}>
          {row.fileName ?? "Attachment"}
        </a>
      )}
      {row.feedback && !canWrite && <p className="text-sm break-words text-slate-700">Feedback: {row.feedback}</p>}
      {canWrite && developmental && (
        <div className="flex w-full min-w-0 flex-wrap gap-2">
          <Button variant={row.status === "Received" ? "secondary" : "primary"} loading={busy} onClick={() => onSave(withFeedback({ received: true }))}>
            Received
          </Button>
          {row.status === "Received" && !row.submittedAt && !row.body && !row.fileUrl && (
            <Button variant="secondary" loading={busy} onClick={() => onSave({ received: false })}>
              Not received
            </Button>
          )}
        </div>
      )}
      {canWrite && numeric && (
        <div className="grid w-full min-w-0 gap-2 sm:grid-cols-[8rem_minmax(0,1fr)_auto] sm:items-end">
          <Input
            label="Mark"
            inputMode="decimal"
            value={mark}
            onChange={(e) => setMark(e.target.value)}
            hint={limit === null ? undefined : `Out of ${score(limit)}`}
            error={localError ?? undefined}
          />
          <div className="min-w-0 sm:col-span-2 sm:col-start-1">
            <Textarea label="Feedback" value={feedback} maxLength={MAX_FEEDBACK} onChange={(e) => setFeedback(e.target.value)} />
          </div>
          <div className="flex flex-wrap gap-2 sm:col-span-3">
            <Button loading={busy} onClick={saveMark}>
              Save mark
            </Button>
            {row.mark !== null && (
              <Button variant="secondary" loading={busy} onClick={() => onSave({ reopen: true })}>
                Reopen
              </Button>
            )}
            {row.mark !== null && (
              <p className="w-full text-xs text-muted">Reopen lets the student send the work again. The mark stays until you save a new one.</p>
            )}
          </div>
        </div>
      )}
      {canWrite && developmental && (
        <div className="w-full min-w-0 space-y-2">
          <Textarea label="Feedback" value={feedback} maxLength={MAX_FEEDBACK} onChange={(e) => setFeedback(e.target.value)} />
          <Button variant="secondary" loading={busy} disabled={!feedback.trim()} onClick={() => onSave({ feedback: feedback.trim() })}>
            Save feedback
          </Button>
        </div>
      )}
    </li>
  );
}

function statusLabel(status: AssignmentSubmissionDto["status"], developmental: boolean): string {
  if (status === "Received") return "Received";
  if (status === "Returned") return "Marked";
  return developmental ? "Not received" : "Not submitted";
}

function parseMark(text: string, limit: number | null): { value: number } | { error: string } {
  const clean = text.trim();
  if (!/^\d+(\.\d)?$/.test(clean)) return { error: "Enter a mark such as 8 or 8.5." };
  const value = Number(clean);
  if (limit !== null && value > limit) return { error: `The mark must be between 0 and ${score(limit)}.` };
  return { value };
}

function CreateDialog({
  classSubjectId,
  termId,
  developmental,
  components,
  onClose,
  onCreated,
}: {
  classSubjectId: string;
  termId: string;
  developmental: boolean;
  components: ComponentDto[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const [title, setTitle] = useState("");
  const [instructions, setInstructions] = useState("");
  const [due, setDue] = useState("");
  const [maxMarks, setMaxMarks] = useState("");
  const [componentId, setComponentId] = useState("");
  const create = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: CreateAssignmentRequest = {
      classSubjectId,
      termId,
      title: title.trim(),
      instructions: instructions.trim(),
      dueAt: new Date(due).toISOString(),
    };
    if (maxMarks.trim()) body.maxMarks = Number(maxMarks);
    if (!developmental && componentId) body.assessmentComponentId = componentId;
    if ((await create.run(() => api<AssignmentDto>("/api/school/assignments", { method: "POST", body }))).ok) {
      onCreated();
      onClose();
    }
  }

  return (
    <Modal
      open
      title="Set an assignment"
      onClose={onClose}
      wide
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="set-assignment" loading={create.busy}>
            Set assignment
          </Button>
        </>
      }
    >
      <form id="set-assignment" className="w-full min-w-0 space-y-3" onSubmit={submit}>
        <ErrorBox error={create.error} />
        <Input label="Title" value={title} maxLength={MAX_TITLE} required onChange={(e) => setTitle(e.target.value)} />
        <Textarea label="Instructions" value={instructions} maxLength={MAX_INSTRUCTIONS} required rows={5} onChange={(e) => setInstructions(e.target.value)} />
        <Input label="Due" type="datetime-local" value={due} required onChange={(e) => setDue(e.target.value)} />
        <Input
          label="Maximum mark"
          inputMode="decimal"
          value={maxMarks}
          hint="Optional. One decimal place at most. Leave it empty when the mark is already on the component's scale."
          onChange={(e) => setMaxMarks(e.target.value)}
        />
        {!developmental && (
          <Select
            label="Counted as"
            value={componentId}
            hint="Practice work is not written onto the score sheet."
            onChange={(e) => setComponentId(e.target.value)}
          >
            <option value="">Practice</option>
            {components.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} (out of {score(c.maxScore)})
              </option>
            ))}
          </Select>
        )}
        <p className="text-xs text-muted">Students may attach one file. {FILE_HINT}</p>
      </form>
    </Modal>
  );
}
