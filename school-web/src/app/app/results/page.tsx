"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { api, apiUrl, downloadFile } from "@/lib/api";
import { STORAGE_PREFIX } from "@/lib/config";
import { attendanceLine, formatStamp, plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type {
  PublishResultDto,
  ReportCardBundleDto,
  ReportCardBundleRequestedDto,
  ResultsDto,
  ResultStudentDto,
  SetRemarksRequest,
} from "@/lib/types/academic";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorBox,
  Notice,
  PageHeader,
  PageLoader,
  SectionTitle,
  Textarea,
  buttonClass,
  cx,
  linkClass,
  panelClass,
} from "@/components/ui";
import { ImportDialog } from "@/components/import-dialog";
import { AcademicNoAccess, ClassPicker, TermPicker, ordinal, score, useAcademicClasses, useReadOnly, useSessionTerms } from "../scores/academic-shared";

const ZERO_WARNING = "Subjects with no scores count as 0 once results are locked.";

export default function ResultsPage() {
  const role = useRole();
  const academic = useAcademicClasses();
  const t = useSessionTerms();
  const [classPick, setClassPick] = useState("");
  const [termPick, setTermPick] = useState("");
  // "Report cards ready" links here with ?bundle=<id>: that bundle's class and term are opened, on any device.
  const bundleParam = useSearchParams().get("bundle");
  const linked = useApi<ReportCardBundleDto>(bundleParam ? `/api/school/report-cards/bundles/${bundleParam}` : null);
  const [seededFrom, setSeededFrom] = useState<string | null>(null);
  if (linked.data && bundleParam && seededFrom !== bundleParam) {
    setSeededFrom(bundleParam);
    setClassPick(linked.data.classId);
    setTermPick(linked.data.termId);
  }
  const termId = termPick || t.current?.id || "";
  // Leadership reads every class. A teacher reads a class they take, or one they teach a subject in.
  const classes = academic.classes;
  const classId = classes.some((c) => c.id === classPick) ? classPick : classes.length === 1 ? classes[0].id : "";

  if (role === "Accountant") return <AcademicNoAccess what="Results" />;
  const error = academic.error ?? t.error;
  const ready = academic.loaded && t.loaded;
  return (
    <div className="space-y-4">
      <PageHeader title="Results" subtitle="Term results from the score sheets: totals, positions, remarks and report cards." />
      {error && (
        <ApiErrorView
          error={error}
          onRetry={() => {
            academic.reload();
            t.reload();
          }}
        />
      )}
      {!ready && !error && <PageLoader />}
      {bundleParam && linked.error && (
        <Notice tone="warning">
          Those report cards are no longer available. They are kept for 7 days; choose the class below and make them again.
        </Notice>
      )}
      {ready && classes.length === 0 && (
        <EmptyState
          title="No class results for you"
          text="Results show for a class you take or teach. Subject scores are entered on the score sheet."
          action={
            <Link href="/app/scores" className={linkClass}>
              Open the score sheet
            </Link>
          }
        />
      )}
      {ready && classes.length > 0 && (
        <>
          <div className="flex flex-wrap items-end gap-2">
            <ClassPicker classes={classes} value={classId} onChange={setClassPick} />
            <TermPicker terms={t.terms} value={termId} onChange={setTermPick} />
          </div>
          {!classId && <p className="text-sm text-muted">Choose a class to see its results.</p>}
          {classId && termId && (
            <ClassResults
              key={`${classId}:${termId}`}
              classId={classId}
              termId={termId}
              leadership={academic.leadership}
              canWriteRemarks={academic.leadership || academic.classTeacherOf.has(classId)}
              linkedBundle={bundleParam && linked.data?.classId === classId && linked.data.termId === termId ? bundleParam : null}
            />
          )}
        </>
      )}
    </div>
  );
}

function ClassResults({
  classId,
  termId,
  leadership,
  canWriteRemarks,
  linkedBundle,
}: {
  classId: string;
  termId: string;
  leadership: boolean;
  /** Class teacher or leadership: overall remarks and the class report card. A subject teacher writes on the child's page. */
  canWriteRemarks: boolean;
  /** The bundle a notification linked to, when it is for this class and term. */
  linkedBundle: string | null;
}) {
  const canExport = useCan("school.manage");
  const { data, error, reload, setData, loading } = useApi<ResultsDto>(`/api/school/results/${classId}/${termId}`);
  const [open, setOpen] = useState<string | null>(null);
  // True while the open student's remark boxes hold text that is not saved; the remarks import waits until it is saved or undone.
  const [typing, setTyping] = useState(false);
  if (error) return <ApiErrorView error={error} onRetry={reload} />;
  if (!data) return <PageLoader label="Working out the results…" />;
  const published = !!data.publishedAt;
  const nursery = data.students.length > 0 && data.students.every((s) => s.subjects.length === 0);
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2">
        {published ? (
          <Badge tone="pass">Published</Badge>
        ) : data.locked ? (
          <Badge tone="blazer">Locked</Badge>
        ) : (
          <Badge tone="crest">Draft</Badge>
        )}
        <span className="text-sm text-muted">
          {published ? `Published ${formatStamp(data.publishedAt)}. Parents can see these results.` : data.locked ? "Scores can't change. Not published yet." : "Scores can still change. Parents can't see these yet."}
        </span>
      </div>
      {canExport && <BroadsheetDownloads classId={classId} termId={termId} />}
      {data.pendingStudents > 0 && (
        <Notice tone="warning">
          {plural(data.pendingStudents, "student")} in the class {data.pendingStudents === 1 ? "has" : "have"} no result yet.{" "}
          {published ? "They joined after publishing. Unpublish and publish again to include them." : "Recalculate to include them."}
        </Notice>
      )}
      {leadership && <Actions results={data} onChanged={reload} />}
      {canWriteRemarks && <BundlePanel classId={classId} termId={termId} hasResults={data.students.length > 0} linkedBundle={linkedBundle} />}
      {canWriteRemarks && <RatingImports results={data} onDone={reload} />}
      {data.students.length > 0 && (canWriteRemarks || !nursery) && <RemarkImport results={data} typing={typing} onDone={reload} />}

      {data.students.length === 0 ? (
        <EmptyState title="No results yet" text="Results appear once scores are entered on the score sheets for this class and term." />
      ) : (
        <div className={cx("ruled-sheet", loading && "opacity-60")}>
          <div
            aria-hidden="true"
            className={cx(
              "ruled-pad hidden gap-4 border-b border-rule bg-slate-50 py-2.5 text-xs font-bold text-muted md:grid",
              nursery ? "md:grid-cols-[minmax(0,1fr)_auto]" : "md:grid-cols-[4rem_minmax(0,1fr)_6rem_6rem_auto]",
            )}
          >
            {!nursery && <span>Position</span>}
            <span>Student</span>
            {!nursery && <span className="text-right">Total</span>}
            {!nursery && <span className="text-right">Average</span>}
            <span />
          </div>
          <ul className="ruled-rows" aria-label="Students">
            {data.students.map((s) => (
              <li key={s.enrolmentId}>
                <div
                  className={cx(
                    "ruled-pad grid items-center gap-x-3 gap-y-2 py-3",
                    nursery ? "grid-cols-[minmax(0,1fr)_auto]" : "grid-cols-1 md:grid-cols-[4rem_minmax(0,1fr)_6rem_6rem_auto] md:gap-x-4",
                  )}
                >
                  {!nursery && <span className="slab tnum hidden text-lg font-bold text-ink md:block">{ordinal(s.position)}</span>}
                  <div className="min-w-0">
                    <Link
                      href={`/app/results/students/${s.studentId}?termId=${termId}&classId=${classId}`}
                      className={cx(linkClass, "break-words")}
                    >
                      {s.name}
                    </Link>
                    <span className="mt-0.5 flex flex-wrap items-center gap-2 text-xs text-muted">
                      <span className="tnum">{s.admissionNo}</span>
                      {s.incomplete && (
                        <Badge tone="crest">{s.subjects.length === 0 && s.developmental.length > 0 ? "Some areas not rated" : "Some scores missing"}</Badge>
                      )}
                    </span>
                    <span className="mt-0.5 block text-xs break-words text-muted">{attendanceLine(s)}.</span>
                    {!nursery && (
                      <span className="mt-1 block text-sm text-ink md:hidden">
                        {ordinal(s.position)} · total {score(s.total)} · average {s.average.toFixed(2)}
                      </span>
                    )}
                  </div>
                  {!nursery && <span className="tnum hidden text-right md:block">{score(s.total)}</span>}
                  {!nursery && <span className="slab tnum hidden text-right text-lg font-bold text-ink md:block">{s.average.toFixed(2)}</span>}
                  <button
                    type="button"
                    aria-expanded={open === s.enrolmentId}
                    onClick={() => setOpen(open === s.enrolmentId ? null : s.enrolmentId)}
                    className={buttonClass("ghost", "justify-self-start px-2.5 md:justify-self-end")}
                  >
                    {open === s.enrolmentId ? "Hide" : "Details"}
                  </button>
                </div>
                {open === s.enrolmentId && (
                  <StudentDetail
                    // The remark boxes start from the saved remarks: when an import (or a save) changes them, the boxes start again from
                    // the new text. The import is closed while the boxes hold unsaved text, so nothing typed is lost.
                    key={`${s.enrolmentId}:${s.teacherRemark ?? ""}:${s.principalRemark ?? ""}`}
                    student={s}
                    classId={classId}
                    termId={termId}
                    published={published}
                    leadership={leadership}
                    canWriteRemarks={canWriteRemarks}
                    onSaved={(r) => setData(r)}
                    onTyping={setTyping}
                  />
                )}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

/**
 * Psychomotor, affective and (for a nursery or creche class) developmental ratings for the whole class from one file, for the class teacher
 * or leadership. Ratings are typed one child at a time on the child's page; this page has no typed ratings, and the remarks typed in a
 * student's details are kept when the results reload after an import. Developmental ratings are frozen while the term is locked.
 */
function RatingImports({ results, onDone }: { results: ResultsDto; onDone: () => void }) {
  const readOnly = useReadOnly();
  if (readOnly) return null;
  const scope = { classId: results.classId, termId: results.termId };
  const developmental = results.mode === "Developmental";
  return (
    <div className={cx(panelClass, "space-y-2 p-4")}>
      <p className="text-sm font-semibold text-ink">Ratings from a spreadsheet</p>
      <p className="text-sm text-muted">
        One file per list for the whole class. Blank cells keep the ratings already saved.
      </p>
      <div className="flex flex-wrap items-start gap-2">
        <ImportDialog
          kind="traits"
          scope={{ ...scope, kind: "Psychomotor" }}
          title={`Import psychomotor ratings for ${results.className}`}
          label="Import psychomotor"
          onDone={onDone}
        />
        <ImportDialog
          kind="traits"
          scope={{ ...scope, kind: "Affective" }}
          title={`Import affective ratings for ${results.className}`}
          label="Import affective"
          onDone={onDone}
        />
        {developmental && !results.locked && (
          <ImportDialog
            kind="developmental"
            scope={scope}
            title={`Import developmental ratings for ${results.className}`}
            label="Import developmental"
            onDone={onDone}
          />
        )}
      </div>
      {developmental && results.locked && (
        <p className="text-xs text-muted">This term is locked, so the developmental ratings stay as they are. Ask the principal to unlock it.</p>
      )}
    </div>
  );
}

/**
 * Subject comments and overall remarks for the whole class from one file. Everyone who reads the class's results can use it: a subject
 * teacher's file has only the subjects they teach, the class teacher's also has the class teacher's remark, and only leadership's has the
 * principal's remark (the server applies the same rules as the remark boxes). Published results are frozen, so the button is disabled. It
 * also waits while a student's remark boxes hold unsaved text, so the reload after an import never replaces something typed.
 */
function RemarkImport({ results, typing, onDone }: { results: ResultsDto; typing: boolean; onDone: () => void }) {
  const readOnly = useReadOnly();
  if (readOnly) return null;
  const disabledReason = results.publishedAt
    ? "Unpublish the results before changing remarks."
    : typing
      ? "Save or undo the remark you are typing before importing."
      : undefined;
  return (
    <div className={cx(panelClass, "space-y-2 p-4")}>
      <p className="text-sm font-semibold text-ink">Comments and remarks from a spreadsheet</p>
      <p className="text-sm text-muted">
        One file for the whole class, with the comments and remarks you write. Blank cells keep what is already saved.
      </p>
      <ImportDialog
        kind="remarks"
        scope={{ classId: results.classId, termId: results.termId }}
        title={`Import comments and remarks for ${results.className}`}
        label="Import comments and remarks"
        disabledReason={disabledReason}
        onDone={onDone}
      />
    </div>
  );
}

/** Lock, recalculate, publish, unpublish and unlock, in the order the API allows them. */
function Actions({ results, onChanged }: { results: ResultsDto; onChanged: () => void }) {
  const readOnly = useReadOnly();
  const { busy, error, run } = useAction();
  const [notice, setNotice] = useState<string | null>(null);
  const published = !!results.publishedAt;
  const base = `/api/school/results/${results.classId}/${results.termId}`;
  if (readOnly) return null;

  async function act(path: string, done: (data: unknown) => string, confirm?: string) {
    if (confirm && !window.confirm(confirm)) return;
    setNotice(null);
    const res = await run(() => api(`${base}/${path}`, { method: "POST" }));
    if (res.ok) {
      setNotice(done(res.data));
      onChanged();
    }
  }

  const lock = () =>
    act("lock", () => "Results locked. Scores can't change until you unlock them.", `Lock the results for ${results.className}? ${ZERO_WARNING} Teachers can't change scores while locked.`);
  const compute = () => act("compute", () => "Results recalculated.", results.locked ? `Recalculate the results? ${ZERO_WARNING}` : undefined);
  const publish = () =>
    act(
      "publish",
      (d) => {
        const r = d as PublishResultDto;
        return r.emailed > 0 ? `Results published. ${plural(r.emailed, "parent")} emailed.` : "Results published. Parents with an account can see them now.";
      },
      `Publish the results for ${results.className}? Parents and students will see them and get a notification.`,
    );
  const unpublish = () =>
    act("unpublish", () => "Results unpublished. Parents can no longer see them.", "Unpublish these results? Parents and students won't see them until you publish again.");
  const unlock = () => act("unlock", () => "Results unlocked. Teachers can change scores again.");

  return (
    <Card className="space-y-3">
      <SectionTitle>Lock and publish</SectionTitle>
      {!published && <Notice tone="warning">{ZERO_WARNING}</Notice>}
      {published && <p className="text-sm text-slate-700">Published results are frozen. Unpublish them before you recalculate, change remarks or unlock.</p>}
      {notice && <Notice tone="success">{notice}</Notice>}
      <ErrorBox error={error} />
      <div className="flex flex-wrap gap-2">
        {!results.locked && (
          <>
            <Button onClick={lock} loading={busy}>
              Lock results
            </Button>
            <Button variant="secondary" onClick={compute} disabled={busy}>
              Recalculate
            </Button>
          </>
        )}
        {results.locked && !published && (
          <>
            <Button onClick={publish} loading={busy} disabled={results.students.length === 0}>
              Publish results
            </Button>
            <Button variant="secondary" onClick={compute} disabled={busy}>
              Recalculate
            </Button>
            <Button variant="secondary" onClick={unlock} disabled={busy}>
              Unlock
            </Button>
          </>
        )}
        {published && (
          <>
            <Button variant="danger" onClick={unpublish} loading={busy}>
              Unpublish
            </Button>
            <Button variant="secondary" disabled title="Unpublish the results before unlocking.">
              Unlock
            </Button>
          </>
        )}
      </div>
      {published && <p className="text-xs text-muted">To unlock, unpublish the results first.</p>}
      {!results.locked && <p className="text-xs text-muted">Lock the results before you publish them.</p>}
    </Card>
  );
}

function StudentDetail({
  student,
  classId,
  termId,
  published,
  leadership,
  canWriteRemarks,
  onSaved,
  onTyping,
}: {
  student: ResultStudentDto;
  classId: string;
  termId: string;
  published: boolean;
  leadership: boolean;
  canWriteRemarks: boolean;
  onSaved: (r: ResultsDto) => void;
  /** Told whether the remark boxes hold unsaved text (false again when the details close). */
  onTyping: (typing: boolean) => void;
}) {
  const readOnly = useReadOnly();
  const [teacherRemark, setTeacherRemark] = useState(student.teacherRemark ?? "");
  const [principalRemark, setPrincipalRemark] = useState(student.principalRemark ?? "");
  const save = useAction();
  const card = useAction();
  const canEditTeacher = canWriteRemarks && !published && !readOnly;
  const canEditPrincipal = leadership && !published && !readOnly;
  const changed =
    (canEditTeacher && teacherRemark !== (student.teacherRemark ?? "")) || (canEditPrincipal && principalRemark !== (student.principalRemark ?? ""));
  const marks = student.subjects.length > 0;

  useEffect(() => {
    onTyping(changed);
  }, [changed, onTyping]);
  useEffect(() => () => onTyping(false), [onTyping]);

  async function saveRemarks() {
    const body: SetRemarksRequest = {
      enrolmentId: student.enrolmentId,
      teacherRemark: canEditTeacher ? teacherRemark : null,
      principalRemark: canEditPrincipal ? principalRemark : null,
    };
    const res = await save.run(() => api<ResultsDto>(`/api/school/results/${classId}/${termId}/remarks`, { method: "PUT", body }));
    if (res.ok) onSaved(res.data);
  }

  return (
    <div className="ruled-pad min-w-0 space-y-4 border-t border-rule-soft bg-slate-50/60 py-4">
      {marks ? (
        <div className="overflow-x-auto">
          <table className="w-full min-w-max text-sm">
            <thead>
              <tr className="text-left text-xs font-bold text-muted">
                <th scope="col" className="py-1.5 pr-3">
                  Subject
                </th>
                <th scope="col" className="px-2 py-1.5 text-right">
                  Total
                </th>
                <th scope="col" className="px-2 py-1.5">
                  Grade
                </th>
                <th scope="col" className="px-2 py-1.5 text-right">
                  Position
                </th>
                <th scope="col" className="px-2 py-1.5 text-right">
                  Class average
                </th>
                <th scope="col" className="py-1.5 pl-2">
                  Remark
                </th>
              </tr>
            </thead>
            <tbody>
              {student.subjects.map((s) => (
                <tr key={s.classSubjectId} className="border-t border-rule-soft">
                  <td className="py-1.5 pr-3">
                    {s.subject}
                    {s.incomplete && <span className="ml-1.5 text-xs text-crest">(scores missing)</span>}
                  </td>
                  <td className="tnum px-2 py-1.5 text-right">{score(s.total)}</td>
                  <td className="slab px-2 py-1.5 font-bold">{s.grade}</td>
                  <td className="tnum px-2 py-1.5 text-right">{ordinal(s.position)}</td>
                  <td className="tnum px-2 py-1.5 text-right">{s.classAverage.toFixed(2)}</td>
                  <td className="py-1.5 pl-2 text-slate-700">{s.remark ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : student.developmental.length > 0 ? (
        <ul className="min-w-0 space-y-2 text-sm">
          {student.developmental.map((d) => (
            <li key={d.area} className="min-w-0">
              <span className="font-bold text-ink">{d.area}</span>
              <span className="text-slate-700">{d.scale ? ` · ${d.scale}` : ""}</span>
              {d.comment && <span className="mt-0.5 block break-words text-slate-700">{d.comment}</span>}
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted">No subject marks for this class.</p>
      )}
      <p className="text-sm break-words text-slate-700">
        {marks && (
          <>
            Total <span className="tnum font-semibold">{score(student.total)}</span>, average{" "}
            <span className="tnum font-semibold">{student.average.toFixed(2)}</span>, {ordinal(student.position)} of {student.classSize}.{" "}
          </>
        )}
        {attendanceLine(student)}.
      </p>

      <div className="grid min-w-0 gap-3 md:grid-cols-2">
        <Textarea
          label="Class teacher's remark"
          maxLength={500}
          className="w-full min-w-0"
          disabled={!canEditTeacher}
          hint={!canWriteRemarks ? "Only the class teacher writes this remark." : undefined}
          value={teacherRemark}
          onChange={(e) => setTeacherRemark(e.target.value)}
          error={save.error?.field("teacherRemark")}
        />
        <Textarea
          label="Principal's remark"
          maxLength={500}
          className="w-full min-w-0"
          disabled={!canEditPrincipal}
          hint={!leadership ? "Only the principal writes this remark." : undefined}
          value={principalRemark}
          onChange={(e) => setPrincipalRemark(e.target.value)}
          error={save.error?.field("principalRemark")}
        />
      </div>
      {published && <p className="text-xs text-muted">Remarks can&apos;t change while the results are published.</p>}
      <ErrorBox error={save.error && !save.error.field("teacherRemark") && !save.error.field("principalRemark") ? save.error : null} />
      <ErrorBox error={card.error} />
      <div className="flex flex-wrap gap-2">
        {(canEditTeacher || canEditPrincipal) && (
          <Button onClick={saveRemarks} loading={save.busy} disabled={!changed}>
            Save remarks
          </Button>
        )}
        {canWriteRemarks && (
          <Button
            variant="secondary"
            loading={card.busy}
            onClick={() =>
              card.run(() =>
                downloadFile(`/api/school/report-cards/${student.enrolmentId}/${termId}.pdf?classId=${classId}`, `report-card-${student.admissionNo}.pdf`),
              )
            }
          >
            {published ? "Report card" : "Draft report card"}
          </Button>
        )}
      </div>
    </div>
  );
}

function BroadsheetDownloads({ classId, termId }: { classId: string; termId: string }) {
  const pdf = useAction();
  const csv = useAction();
  const base = `/api/school/results/${classId}/${termId}`;
  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" loading={pdf.busy} disabled={csv.busy} onClick={() => pdf.run(() => downloadFile(`${base}/broadsheet.pdf`, "broadsheet.pdf"))}>
          Broadsheet PDF
        </Button>
        <Button variant="secondary" loading={csv.busy} disabled={pdf.busy} onClick={() => csv.run(() => downloadFile(`${base}/broadsheet.csv`, "broadsheet.csv"))}>
          Broadsheet CSV
        </Button>
      </div>
      <ErrorBox error={pdf.error ?? csv.error} />
    </div>
  );
}

// ---------- Class bundle ----------

const POLL_MS = 5000;
const bundleStore = (classId: string, termId: string) => `${STORAGE_PREFIX}bundle.${classId}.${termId}`;

function storedBundle(classId: string, termId: string): string | null {
  try {
    return window.localStorage.getItem(bundleStore(classId, termId));
  } catch {
    return null;
  }
}

/** The whole class's report cards in one PDF: queued here, built by the report card job, then ready to download. */
function BundlePanel({
  classId,
  termId,
  hasResults,
  linkedBundle,
}: {
  classId: string;
  termId: string;
  hasResults: boolean;
  linkedBundle: string | null;
}) {
  // A bundle named in the URL wins over the one this device remembers.
  const [bundleId, setBundleId] = useState<string | null>(() => linkedBundle ?? (typeof window === "undefined" ? null : storedBundle(classId, termId)));
  const [status, setStatus] = useState<ReportCardBundleDto | null>(null);
  const request = useAction();

  useEffect(() => {
    if (!bundleId) return;
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | null = null;
    const check = async () => {
      try {
        const s = await api<ReportCardBundleDto>(`/api/school/report-cards/bundles/${bundleId}`);
        if (stopped) return;
        setStatus(s);
        if (s.status === "Queued") timer = setTimeout(check, POLL_MS);
      } catch {
        if (stopped) return;
        // Gone (bundles are kept 7 days) or not reachable: forget it, the user can ask again.
        setStatus(null);
        setBundleId(null);
        try {
          window.localStorage.removeItem(bundleStore(classId, termId));
        } catch {
          /* storage unavailable */
        }
      }
    };
    void check();
    return () => {
      stopped = true;
      if (timer) clearTimeout(timer);
    };
  }, [bundleId, classId, termId]);

  async function make() {
    const res = await request.run(() => api<ReportCardBundleRequestedDto>(`/api/school/report-cards/${classId}/${termId}/bundle`, { method: "POST" }));
    if (!res.ok) return;
    try {
      window.localStorage.setItem(bundleStore(classId, termId), res.data.id);
    } catch {
      /* storage unavailable */
    }
    setStatus({ status: "Queued", url: null, classId, className: "", termId });
    setBundleId(res.data.id);
  }

  const state = status?.status;
  return (
    <div className={cx(panelClass, "flex flex-wrap items-center justify-between gap-3 p-4")}>
      <div className="min-w-0 text-sm">
        <p className="font-semibold text-ink">Report cards for the class</p>
        {state === "Queued" && (
          <p className="mt-0.5 flex flex-wrap items-center gap-2 text-slate-700">
            <Badge tone="crest">Queued</Badge> Being made. You&apos;ll get a notification when they&apos;re ready.
          </p>
        )}
        {state === "Ready" && (
          <p className="mt-0.5 flex flex-wrap items-center gap-2 text-slate-700">
            <Badge tone="pass">Ready</Badge> One PDF with every student&apos;s report card.
          </p>
        )}
        {state === "Failed" && (
          <p className="mt-0.5 flex flex-wrap items-center gap-2 text-slate-700">
            <Badge tone="brick">Failed</Badge> They could not be made. Check the class has results, then try again.
          </p>
        )}
        {!state && <p className="mt-0.5 text-muted">Make one PDF with every student&apos;s report card, ready to print.</p>}
        <ErrorBox error={request.error} />
      </div>
      <div className="flex flex-wrap gap-2">
        {state === "Ready" && status?.url && (
          <a href={apiUrl(status.url)} className={buttonClass("primary")} target="_blank" rel="noopener noreferrer">
            Download report cards
          </a>
        )}
        {state !== "Queued" && (
          <Button variant="secondary" onClick={make} loading={request.busy} disabled={!hasResults}>
            {state === "Ready" ? "Make them again" : "Make report cards"}
          </Button>
        )}
      </div>
    </div>
  );
}
