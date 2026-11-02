"use client";

import { useParams, useSearchParams } from "next/navigation";
import { useState } from "react";
import { api } from "@/lib/api";
import { attendanceLine } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto, DevelopmentalScaleDto, DevelopmentalScaleItemDto, TraitKind } from "@/lib/types";
import type {
  ChildResultDto,
  CommentSuggestionsDto,
  DevelopmentalRatingItemDto,
  ResultSubjectDto,
  SetRemarksRequest,
  SetSubjectCommentRequest,
  SubjectCommentDto,
  TraitRatingItemDto,
} from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, EmptyState, ErrorBox, PageHeader, PageLoader, SectionTitle, Select, Textarea } from "@/components/ui";
import { ordinal, score, useAcademicClasses, useReadOnly } from "../../../scores/academic-shared";

export default function ChildResultPage() {
  const { studentId } = useParams<{ studentId: string }>();
  const params = useSearchParams();
  const termId = params.get("termId") ?? "";
  const classId = params.get("classId") ?? "";
  if (!termId) {
    return (
      <div className="min-w-0">
        <PageHeader title="Child results" back="/app/results" />
        <EmptyState title="Choose a term" text="Open this child from the class results page." />
      </div>
    );
  }
  return <ChildResult studentId={studentId} termId={termId} classId={classId} />;
}

function ChildResult({ studentId, termId, classId }: { studentId: string; termId: string; classId: string }) {
  const path = `/api/school/results/students/${studentId}${qs({ termId, classId, suggestions: true })}`;
  const { data, error, reload } = useApi<ChildResultDto>(path);
  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader label="Working out the results…" />;
  return <ChildEditor key={`${data.enrolmentId}:${data.classId}:${data.publishedAt ?? ""}:${data.locked}`} data={data} />;
}

function ChildEditor({ data }: { data: ChildResultDto }) {
  const leadership = useCan("school.manage");
  const academic = useAcademicClasses();
  const readOnly = useReadOnly();
  const published = !!data.publishedAt;
  const classTeacher = academic.classTeacherOf.has(data.classId);
  const canRate = !readOnly && (leadership || classTeacher);
  const canWriteRemarks = !readOnly && (leadership || classTeacher);
  const taught = new Set(academic.subjects.map((s) => s.classSubjectId));
  const areas = [...data.developmental].sort((a, b) => a.order - b.order);
  const psychomotor = [...data.psychomotor].sort((a, b) => a.order - b.order);
  const affective = [...data.affective].sort((a, b) => a.order - b.order);

  return (
    <div className="min-w-0 space-y-6">
      <PageHeader
        back="/app/results"
        title={data.name}
        subtitle={`${data.admissionNo} · ${data.className} · ${data.termName}`}
      />
      <div className="flex flex-wrap items-center gap-2">
        {published ? <Badge tone="pass">Published</Badge> : data.locked ? <Badge tone="blazer">Locked</Badge> : <Badge tone="crest">Draft</Badge>}
        <p className="min-w-0 text-sm break-words text-slate-700">{attendanceLine(data)}.</p>
      </div>
      {published && (
        <p className="max-w-prose text-sm text-muted">
          Subject comments and remarks stay as they are while results are published.
          {canRate ? (areas.length > 0 ? " Psychomotor and affective ratings can still change." : " Ratings can still change.") : ""}
        </p>
      )}
      {!published && data.locked && areas.length > 0 && canRate && (
        <p className="max-w-prose text-sm text-muted">This term is locked, so the developmental ratings stay as they are. Ask the principal to unlock it.</p>
      )}

      {data.subjects.length > 0 && (
        <section className="min-w-0 space-y-4">
          <SectionTitle>Subjects</SectionTitle>
          {data.subjects.map((subject) => (
            <SubjectComment
              key={subject.classSubjectId}
              subject={subject}
              classId={data.classId}
              termId={data.termId}
              enrolmentId={data.enrolmentId}
              canWrite={!readOnly && !published && (leadership || taught.has(subject.classSubjectId))}
              published={published}
            />
          ))}
        </section>
      )}

      {areas.length > 0 && (
        <DevelopmentalSection studentId={data.studentId} termId={data.termId} classId={data.classId} ratings={areas} canRate={canRate && !data.locked} />
      )}

      {(psychomotor.length > 0 || canRate) && (
        <TraitSection studentId={data.studentId} termId={data.termId} kind="Psychomotor" title="Psychomotor" items={psychomotor} canRate={canRate} />
      )}
      {(affective.length > 0 || canRate) && (
        <TraitSection studentId={data.studentId} termId={data.termId} kind="Affective" title="Affective" items={affective} canRate={canRate} />
      )}

      <Remarks data={data} canWriteTeacher={canWriteRemarks} canWritePrincipal={leadership && !readOnly} published={published} />
    </div>
  );
}

function CommentChips({ lines, onPick }: { lines: string[]; onPick: (line: string) => void }) {
  if (lines.length === 0) return null;
  return (
    <div className="min-w-0 space-y-1.5">
      <p className="text-xs text-muted">Suggestions</p>
      <div className="flex min-w-0 flex-wrap gap-2">
        {lines.map((line, i) => (
          <button
            key={`${i}:${line}`}
            type="button"
            className="max-w-full min-w-0 rounded-full border border-rule bg-surface px-3 py-2 text-left text-sm font-medium break-words whitespace-normal text-slate-700 hover:border-blazer-rule hover:text-ink"
            onClick={() => onPick(line)}
          >
            {line}
          </button>
        ))}
      </div>
    </div>
  );
}

function SubjectComment({
  subject,
  classId,
  termId,
  enrolmentId,
  canWrite,
  published,
}: {
  subject: ResultSubjectDto;
  classId: string;
  termId: string;
  enrolmentId: string;
  canWrite: boolean;
  published: boolean;
}) {
  const [comment, setComment] = useState(subject.remark ?? "");
  const [saved, setSaved] = useState(subject.remark ?? "");
  const save = useAction();
  const empty = comment.trim() === "";
  const suggestions = useApi<CommentSuggestionsDto>(
    canWrite && empty ? `/api/school/results/${classId}/${termId}/suggestions${qs({ enrolmentId, classSubjectId: subject.classSubjectId })}` : null,
  );

  async function submit() {
    const body: SetSubjectCommentRequest = { enrolmentId, classSubjectId: subject.classSubjectId, comment };
    const res = await save.run(() => api<SubjectCommentDto>(`/api/school/results/${classId}/${termId}/subject-comments`, { method: "PUT", body }));
    if (res.ok) setSaved(res.data.comment ?? "");
  }

  return (
    <div className="min-w-0 space-y-2 border-b border-rule-soft pb-4">
      <div className="min-w-0">
        <p className="font-bold break-words text-ink">{subject.subject}</p>
        <p className="text-sm break-words text-muted">
          {subject.subjectCode} · total {score(subject.total)} · {subject.grade} · {ordinal(subject.position)} · class average {subject.classAverage.toFixed(2)}
          {subject.incomplete ? " · scores missing" : ""}
        </p>
      </div>
      <Textarea
        label={`${subject.subject} comment`}
        maxLength={500}
        className="w-full min-w-0"
        disabled={!canWrite}
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        hint={published ? "Comments stay as they are while results are published." : !canWrite ? "The subject teacher writes this comment." : undefined}
        error={save.error?.field("comment")}
      />
      {canWrite && empty && <CommentChips lines={suggestions.data?.subject ?? []} onPick={setComment} />}
      <ErrorBox error={save.error && !save.error.field("comment") ? save.error : null} />
      {canWrite && (
        <Button onClick={submit} loading={save.busy} disabled={comment === saved}>
          Save comment
        </Button>
      )}
    </div>
  );
}

function DevelopmentalSection({
  studentId,
  termId,
  classId,
  ratings,
  canRate,
}: {
  studentId: string;
  termId: string;
  classId: string;
  ratings: DevelopmentalRatingItemDto[];
  canRate: boolean;
}) {
  const classes = useApi<ClassDto[]>(canRate ? "/api/school/classes" : null);
  const levelId = classes.data?.find((c) => c.id === classId)?.levelId ?? "";
  const scale = useApi<DevelopmentalScaleDto>(canRate && !classes.loading ? `/api/school/developmental/scale${qs({ levelId })}` : null);
  return (
    <section className="min-w-0 space-y-3">
      <SectionTitle>Developmental</SectionTitle>
      {!canRate ? (
        <RatingList rows={ratings.map((r) => ({ id: r.areaId, name: r.name, value: r.scale, note: r.comment }))} />
      ) : classes.error ? (
        <ApiErrorView error={classes.error} onRetry={classes.reload} />
      ) : scale.error ? (
        <ApiErrorView error={scale.error} onRetry={scale.reload} />
      ) : !scale.data ? (
        <PageLoader />
      ) : (
        <DevelopmentalForm studentId={studentId} termId={termId} ratings={ratings} points={scale.data.scale} />
      )}
    </section>
  );
}

function DevelopmentalForm({
  studentId,
  termId,
  ratings,
  points,
}: {
  studentId: string;
  termId: string;
  ratings: DevelopmentalRatingItemDto[];
  points: DevelopmentalScaleItemDto[];
}) {
  const [rows, setRows] = useState(() => ratings.map((r) => ({ areaId: r.areaId, scaleId: r.scaleId ?? "", comment: r.comment ?? "" })));
  const [saved, setSaved] = useState(() =>
    ratings.filter((r) => r.scaleId).map((r) => ({ areaId: r.areaId, scaleId: r.scaleId as string, comment: r.comment })),
  );
  const save = useAction();
  const options = points.slice();
  for (const rating of ratings) {
    if (rating.scaleId && !options.some((p) => p.id === rating.scaleId)) {
      options.push({ id: rating.scaleId, code: rating.code ?? "", label: rating.scale ?? "Saved rating", order: options.length + 1 });
    }
  }
  const payload = rows
    .filter((r) => r.scaleId)
    .map((r) => ({ areaId: r.areaId, scaleId: r.scaleId, comment: r.comment.trim() ? r.comment.trim() : null }));
  const dirty = JSON.stringify(payload) !== JSON.stringify(saved);

  async function submit() {
    if (payload.length === 0) return;
    const res = await save.run(() =>
      api(`/api/school/students/${studentId}/developmental${qs({ termId })}`, { method: "PUT", body: { ratings: payload } }),
    );
    if (res.ok) setSaved(payload);
  }

  return (
    <div className="min-w-0 space-y-4">
      {ratings.map((area, i) => {
        const row = rows[i];
        const started = area.scaleId != null;
        return (
          <div key={area.areaId} className="min-w-0 space-y-2">
            <Select
              label={area.name}
              value={row.scaleId}
              onChange={(e) => setRows((rs) => rs.map((x, n) => (n === i ? { ...x, scaleId: e.target.value } : x)))}
            >
              {!started && <option value="">Not rated</option>}
              {options.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.label}
                </option>
              ))}
            </Select>
            <Textarea
              label={`${area.name} comment`}
              maxLength={500}
              className="w-full min-w-0"
              disabled={!row.scaleId}
              value={row.comment}
              onChange={(e) => setRows((rs) => rs.map((x, n) => (n === i ? { ...x, comment: e.target.value } : x)))}
              hint={!row.scaleId ? "Choose a rating before the comment." : undefined}
            />
          </div>
        );
      })}
      <ErrorBox error={save.error} />
      <Button onClick={submit} loading={save.busy} disabled={!dirty || payload.length === 0}>
        Save developmental ratings
      </Button>
    </div>
  );
}

function TraitSection({
  studentId,
  termId,
  kind,
  title,
  items,
  canRate,
}: {
  studentId: string;
  termId: string;
  kind: TraitKind;
  title: string;
  items: TraitRatingItemDto[];
  canRate: boolean;
}) {
  return (
    <section className="min-w-0 space-y-3">
      <SectionTitle>{title}</SectionTitle>
      {items.length === 0 ? (
        <p className="text-sm text-muted">No items on this list.</p>
      ) : !canRate ? (
        <RatingList rows={items.map((item) => ({ id: item.criterionId, name: item.name, value: item.value == null ? null : String(item.value), note: null }))} />
      ) : (
        <TraitForm studentId={studentId} termId={termId} kind={kind} items={items} />
      )}
    </section>
  );
}

function TraitForm({
  studentId,
  termId,
  kind,
  items,
}: {
  studentId: string;
  termId: string;
  kind: TraitKind;
  items: TraitRatingItemDto[];
}) {
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(items.map((item) => [item.criterionId, item.value == null ? "" : String(item.value)])),
  );
  const [saved, setSaved] = useState(() =>
    items.filter((item) => item.value != null).map((item) => ({ criterionId: item.criterionId, value: item.value as number })),
  );
  const save = useAction();
  const payload = items
    .map((item) => ({ criterionId: item.criterionId, value: Number(values[item.criterionId]) }))
    .filter((r) => r.value >= 1 && r.value <= 5);
  const dirty = JSON.stringify(payload) !== JSON.stringify(saved);

  async function submit() {
    if (payload.length === 0) return;
    const res = await save.run(() => api(`/api/school/students/${studentId}/traits${qs({ termId, kind })}`, { method: "PUT", body: { ratings: payload } }));
    if (res.ok) setSaved(payload);
  }

  return (
    <div className="min-w-0 space-y-3">
      <p className="text-sm text-muted">Rate each item from 1 to 5.</p>
      {items.map((item) => (
        <Select
          key={item.criterionId}
          label={item.name}
          value={values[item.criterionId] ?? ""}
          onChange={(e) => setValues((vs) => ({ ...vs, [item.criterionId]: e.target.value }))}
        >
          {item.value == null && <option value="">Not rated</option>}
          {[1, 2, 3, 4, 5].map((n) => (
            <option key={n} value={n}>
              {n}
            </option>
          ))}
        </Select>
      ))}
      <ErrorBox error={save.error} />
      <Button onClick={submit} loading={save.busy} disabled={!dirty || payload.length === 0}>
        Save {kind === "Psychomotor" ? "psychomotor" : "affective"}
      </Button>
    </div>
  );
}

function RatingList({ rows }: { rows: { id: string; name: string; value: string | null; note: string | null }[] }) {
  return (
    <ul className="min-w-0 space-y-2 text-sm">
      {rows.map((row) => (
        <li key={row.id} className="min-w-0">
          <p className="font-bold break-words text-ink">{row.name}</p>
          <p className="break-words text-slate-700">{row.value ?? "Not rated"}</p>
          {row.note && <p className="break-words text-slate-700">{row.note}</p>}
        </li>
      ))}
    </ul>
  );
}

function Remarks({
  data,
  canWriteTeacher,
  canWritePrincipal,
  published,
}: {
  data: ChildResultDto;
  canWriteTeacher: boolean;
  canWritePrincipal: boolean;
  published: boolean;
}) {
  const [teacher, setTeacher] = useState(data.teacherRemark ?? "");
  const [principal, setPrincipal] = useState(data.principalRemark ?? "");
  const [savedTeacher, setSavedTeacher] = useState(data.teacherRemark ?? "");
  const [savedPrincipal, setSavedPrincipal] = useState(data.principalRemark ?? "");
  const save = useAction();
  const teacherOn = canWriteTeacher && !published;
  const principalOn = canWritePrincipal && !published;
  const changed = (teacherOn && teacher !== savedTeacher) || (principalOn && principal !== savedPrincipal);

  async function submit() {
    const body: SetRemarksRequest = {
      enrolmentId: data.enrolmentId,
      teacherRemark: teacherOn ? teacher : null,
      principalRemark: principalOn ? principal : null,
    };
    const res = await save.run(() => api(`/api/school/results/${data.classId}/${data.termId}/remarks`, { method: "PUT", body }));
    if (res.ok) {
      if (teacherOn) setSavedTeacher(teacher);
      if (principalOn) setSavedPrincipal(principal);
    }
  }

  return (
    <section className="min-w-0 space-y-3">
      <SectionTitle>Remarks</SectionTitle>
      <div className="min-w-0 space-y-3">
        <Textarea
          label="Class teacher's remark"
          maxLength={500}
          className="w-full min-w-0"
          disabled={!teacherOn}
          value={teacher}
          onChange={(e) => setTeacher(e.target.value)}
          hint={!canWriteTeacher ? "Only the class teacher writes this remark." : published ? "Remarks stay as they are while results are published." : undefined}
          error={save.error?.field("teacherRemark")}
        />
        {teacherOn && teacher.trim() === "" && <CommentChips lines={data.suggestions?.teacherOverall ?? []} onPick={setTeacher} />}
        <Textarea
          label="Principal's remark"
          maxLength={500}
          className="w-full min-w-0"
          disabled={!principalOn}
          value={principal}
          onChange={(e) => setPrincipal(e.target.value)}
          hint={!canWritePrincipal ? "Only the principal writes this remark." : published ? "Remarks stay as they are while results are published." : undefined}
          error={save.error?.field("principalRemark")}
        />
        {principalOn && principal.trim() === "" && <CommentChips lines={data.suggestions?.principalOverall ?? []} onPick={setPrincipal} />}
      </div>
      <ErrorBox error={save.error && !save.error.field("teacherRemark") && !save.error.field("principalRemark") ? save.error : null} />
      {(teacherOn || principalOn) && (
        <Button onClick={submit} loading={save.busy} disabled={!changed}>
          Save remarks
        </Button>
      )}
    </section>
  );
}
