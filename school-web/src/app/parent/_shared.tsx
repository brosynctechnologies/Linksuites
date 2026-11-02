"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
  type SVGProps,
} from "react";
import { DownloadIcon } from "@/components/icons";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  EmptyState,
  Notice,
  PageLoader,
  SectionTitle,
  cx,
  linkClass,
  panelClass,
} from "@/components/ui";
import type { ApiError } from "@/lib/api";
import { downloadFile } from "@/lib/api";
import { rememberSchool, useHydrated } from "@/lib/auth";
import { STORAGE_PREFIX } from "@/lib/config";
import { attendanceLine, formatDay, formatStamp, naira } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useApi } from "@/lib/use-api";
import type { ChildRef } from "@/lib/types";
import type { PortalAnnouncementDto } from "@/lib/types/communication";
import type {
  PortalAttendanceDto,
  PortalOverviewDto,
  PortalResultEntryDto,
  PortalResultsDto,
  PortalTermDto,
  PortalTimetableDto,
} from "@/lib/types/portals";
import { SCHOOL_DAYS, type PortalExamTimetableDto } from "@/lib/types/academic";

export const RESULTS_HELD = "Results are held. Please see the school office.";
export const PORTAL_UNAVAILABLE = "This school's portal is unavailable.";
export const STUDENT_NOT_ACTIVE = "Your account isn't active. Please see the school office.";
export const ABANDONED_COPY = "Payment not confirmed. If you were charged, it will update automatically.";
export const CHILD_KEY = `${STORAGE_PREFIX}parentChild`;

export function isPortalUnavailable(error: ApiError) {
  return error.status === 403 && (error.detail === PORTAL_UNAVAILABLE || error.message === PORTAL_UNAVAILABLE);
}

export function isResultsHeld(error: ApiError) {
  return error.status === 403 && (error.detail === RESULTS_HELD || error.message === RESULTS_HELD);
}

export function isStudentInactive(error: ApiError) {
  return error.status === 403 && (error.detail === STUDENT_NOT_ACTIVE || error.message === STUDENT_NOT_ACTIVE);
}

export function money(minor: number): string {
  if (minor % 100 === 0) return naira(minor);
  const sign = minor < 0 ? "-" : "";
  return `${sign}₦${(Math.abs(minor) / 100).toLocaleString("en-NG", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

/** 1st, 2nd, 3rd, 11th, 21st. */
export function ordinal(n: number): string {
  const lastTwo = Math.abs(n) % 100;
  const suffix = lastTwo >= 11 && lastTwo <= 13 ? "th" : ({ 1: "st", 2: "nd", 3: "rd" } as Record<number, string>)[Math.abs(n) % 10] ?? "th";
  return `${n}${suffix}`;
}

/** A score total: whole numbers as they are, otherwise one decimal. Averages use `average()`. */
export function score(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1).replace(/\.0$/, "");
}

/** An average to 2 decimals (67.25, 54.50): the same as the staff results page and the report card PDF (ReportCardText.Average). */
export function average(value: number): string {
  return value.toFixed(2);
}

function readStoredChild(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return window.localStorage.getItem(CHILD_KEY);
  } catch {
    return null;
  }
}

export function writeStoredChild(id: string) {
  try {
    window.localStorage.setItem(CHILD_KEY, id);
  } catch {
    /* storage unavailable */
  }
}

// ---------- Child switcher ----------

interface ChildState {
  list: ChildRef[];
  child: ChildRef | null;
  error?: ApiError;
  loading: boolean;
  select: (id: string) => void;
  reload: () => void;
}

const ChildCtx = createContext<ChildState | null>(null);

export function ParentChildProvider({ children }: { children: ReactNode }) {
  const hydrated = useHydrated();
  const list = useApi<ChildRef[]>("/api/parent/children");
  const [picked, setPicked] = useState<string | null>(null);

  // A notification link names its child (?child=<studentId>): that child is selected when it is linked to this account.
  // Handled once per link value, so the switcher still works while the parameter stays in the URL.
  const fromLink = useSearchParams().get("child");
  const [seenLink, setSeenLink] = useState<string | null>(null);
  if (list.data && fromLink !== seenLink) {
    setSeenLink(fromLink);
    if (fromLink && list.data.some((c) => c.studentId === fromLink)) setPicked(fromLink);
  }
  useEffect(() => {
    if (picked) writeStoredChild(picked);
  }, [picked]);
  const stored = hydrated ? readStoredChild() : null;
  const preferred = picked ?? stored;
  const available = useMemo(() => list.data ?? [], [list.data]);
  const preferredMissing = !!preferred && !!list.data && !available.some((c) => c.studentId === preferred);
  const inList = available.find((c) => c.studentId === preferred) ?? (preferredMissing ? undefined : available[0]);
  const probeId = preferredMissing ? preferred : null;
  const probe = useApi<ChildRef>(probeId ? `/api/parent/children/${probeId}` : null);

  const select = useCallback((id: string) => {
    setPicked(id);
    writeStoredChild(id);
    const hit = (list.data ?? []).find((c) => c.studentId === id);
    if (hit?.schoolSlug) rememberSchool(hit.schoolSlug);
  }, [list.data]);

  const child = inList ?? probe.data ?? null;
  const error = list.error ?? (probeId ? probe.error : undefined);
  const loading = list.loading || (!!probeId && probe.loading && !probe.data && !probe.error);

  const value = useMemo<ChildState>(
    () => ({ list: available, child, error, loading, select, reload: list.reload }),
    [available, child, error, loading, select, list.reload],
  );

  return <ChildCtx.Provider value={value}>{children}</ChildCtx.Provider>;
}

export function useChild(): ChildState {
  const ctx = useContext(ChildCtx);
  if (!ctx) throw new Error("useChild must be used inside ParentChildProvider");
  return ctx;
}

export function useChildPath(suffix: string) {
  const { child } = useChild();
  return child ? `/api/parent/children/${child.studentId}${suffix}` : null;
}

/** Pages that need a linked child. 404 (leaver) and 403 (suspended school) are shown in the school's words. */
export function ChildGate({ children }: { children: ReactNode }) {
  const ctx = useChild();
  if (ctx.loading) return <PageLoader />;
  if (ctx.error) {
    return (
      <PortalBlocked
        error={ctx.error}
        childGone={ctx.error.status === 404}
        onRetry={ctx.reload}
        extra={ctx.list.length > 0 ? <ChildSwitcher className="mt-4" /> : null}
      />
    );
  }
  if (!ctx.child) {
    return (
      <EmptyState
        title="No children on this account"
        text="Ask the school office to send you an invite. Once you accept it, your children will show here."
      />
    );
  }
  return <>{children}</>;
}

export function ChildSwitcher({ className, dark }: { className?: string; dark?: boolean }) {
  const { list, child, select } = useChild();
  if (list.length === 0) return null;
  const manySchools = new Set(list.map((c) => c.schoolName)).size > 1;
  return (
    <label className={cx("block min-w-0", className)}>
      <span className="sr-only">Child</span>
      <select
        value={child?.studentId ?? list[0].studentId}
        onChange={(e) => select(e.target.value)}
        className={cx(
          "max-w-full min-h-11 rounded-control border px-3 py-2 text-sm font-semibold",
          dark
            ? "border-white/20 bg-blazer-deep-2 text-white"
            : "border-rule bg-surface text-ink",
        )}
      >
        {list.map((c) => (
          <option key={c.studentId} value={c.studentId}>
            {c.name}
            {c.className ? ` · ${c.className}` : ""}
            {manySchools ? ` · ${c.schoolName}` : ""}
          </option>
        ))}
      </select>
    </label>
  );
}

export function PortalBlocked({
  error,
  onRetry,
  extra,
  childGone,
}: {
  error: ApiError;
  onRetry?: () => void;
  extra?: ReactNode;
  /** True only when GET /api/parent/children/{id} (or the list) said this child is gone. */
  childGone?: boolean;
}) {
  if (isPortalUnavailable(error)) {
    return (
      <div className={cx(panelClass, "p-5")} role="alert">
        <h1 className="slab text-xl font-bold text-ink">{PORTAL_UNAVAILABLE}</h1>
        <p className="mt-2 max-w-prose text-sm text-slate-700">The school office can tell you when it will open again.</p>
        {extra}
      </div>
    );
  }
  if (isStudentInactive(error)) {
    return (
      <div className={cx(panelClass, "p-5")} role="alert">
        <h1 className="slab text-xl font-bold text-ink">{STUDENT_NOT_ACTIVE}</h1>
        {extra}
      </div>
    );
  }
  if (childGone && error.status === 404) {
    return (
      <div className={cx(panelClass, "p-5")} role="alert">
        <h1 className="slab text-xl font-bold text-ink">This child isn&apos;t on your account</h1>
        <p className="mt-2 max-w-prose text-sm text-slate-700">They may have left the school. Choose another child, or ask the school office.</p>
        <p className="mt-2 text-xs text-muted">{error.message}</p>
        {extra}
      </div>
    );
  }
  if (isResultsHeld(error)) {
    return (
      <Notice tone="warning" className="max-w-prose">
        {RESULTS_HELD}
      </Notice>
    );
  }
  return <ApiErrorView error={error} onRetry={onRetry} />;
}

// ---------- Results, timetable, announcements (parent and student) ----------

export function HeldNotice() {
  return (
    <Notice tone="warning" className="max-w-prose">
      {RESULTS_HELD}
    </Notice>
  );
}

export function TermResults({
  overview,
  selectedId,
  onSelect,
  details,
  detailsError,
  detailsLoading,
  onRetry,
  pdfPath,
}: {
  overview: PortalOverviewDto;
  selectedId: string;
  onSelect: (termId: string) => void;
  details?: PortalResultsDto;
  detailsError?: ApiError;
  detailsLoading?: boolean;
  onRetry?: () => void;
  /** Authenticated PDF path, with `{termId}` and optional `{classId}` placeholders. */
  pdfPath: (termId: string, classId?: string) => string;
}) {
  const terms = overview.results;
  const selected = terms.find((t) => t.termId === selectedId) ?? terms[0];
  if (terms.length === 0) {
    return <EmptyState title="No results yet" text="When the school publishes a term, it will show here." />;
  }
  return (
    <div className="space-y-4">
      <label className="block max-w-sm">
        <span className="mb-1.5 block text-sm font-medium text-slate-700">Term</span>
        <select
          value={selected?.termId ?? ""}
          onChange={(e) => onSelect(e.target.value)}
          className="block min-h-11 w-full rounded-control border border-rule bg-surface px-3 py-2 text-sm text-ink"
        >
          {terms.map((t) => (
            <option key={t.termId} value={t.termId}>
              {t.termName}, {t.sessionName}
              {t.isCurrent ? " (current)" : ""}
            </option>
          ))}
        </select>
      </label>
      {selected && <TermBody term={selected} details={details} detailsError={detailsError} detailsLoading={detailsLoading} onRetry={onRetry} pdfPath={pdfPath} />}
    </div>
  );
}

function TermBody({
  term,
  details,
  detailsError,
  detailsLoading,
  onRetry,
  pdfPath,
}: {
  term: PortalTermDto;
  details?: PortalResultsDto;
  detailsError?: ApiError;
  detailsLoading?: boolean;
  onRetry?: () => void;
  pdfPath: (termId: string, classId?: string) => string;
}) {
  if (term.held) return <HeldNotice />;
  if (!term.published) {
    return (
      <EmptyState
        title={`${term.termName} isn't published yet`}
        text="The school will let you know when this term's results are out."
      />
    );
  }
  if (detailsError) return <PortalBlocked error={detailsError} onRetry={onRetry} />;
  if (detailsLoading || !details) return <PageLoader />;
  if (details.entries.length === 0) {
    return <EmptyState title="No published row for this term" text="Ask the school office if you expected a report card." />;
  }
  return (
    <div className="space-y-5">
      {details.entries.map((e) => (
        <ResultCard key={e.classId} entry={e} pdfPath={pdfPath(details.termId, details.entries.length > 1 ? e.classId : undefined)} />
      ))}
    </div>
  );
}

function ResultCard({ entry, pdfPath }: { entry: PortalResultEntryDto; pdfPath: string }) {
  const pdf = useAction();
  const marks = entry.subjects.length > 0;
  return (
    <Card>
      <div className="mb-3 flex flex-wrap items-end justify-between gap-3">
        <div className="min-w-0">
          <h2 className="slab text-lg font-bold text-ink">{entry.className}</h2>
          {marks && (
            <p className="mt-0.5 text-sm text-muted">
              Total {score(entry.total)}, average {average(entry.average)}. {ordinal(entry.position)} of {entry.classSize}.
            </p>
          )}
          <p className="mt-0.5 text-sm break-words text-muted">{attendanceLine(entry)}.</p>
        </div>
        <div className="flex flex-col items-end">
          <Button
            variant="secondary"
            loading={pdf.busy}
            onClick={() => pdf.run(() => downloadFile(pdfPath, "report-card.pdf"))}
          >
            <DownloadIcon className="h-4 w-4" /> Report card
          </Button>
          {pdf.error && <p className="mt-1 max-w-xs text-right text-xs text-brick">{pdf.error.message}</p>}
        </div>
      </div>
      {marks && (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[28rem] text-left text-sm">
            <thead>
              <tr className="border-b border-rule text-xs text-muted">
                <th className="py-2 pr-3 font-bold">Subject</th>
                <th className="tnum py-2 pr-3 text-right font-bold">Total</th>
                <th className="py-2 pr-3 font-bold">Grade</th>
                <th className="tnum py-2 pr-3 text-right font-bold">Class avg</th>
                <th className="tnum py-2 pr-3 text-right font-bold">Pos</th>
                <th className="py-2 font-bold">Remark</th>
              </tr>
            </thead>
            <tbody>
              {entry.subjects.map((s) => (
                <tr key={s.subjectCode} className="border-b border-rule-soft">
                  <td className="py-2 pr-3 font-medium text-ink">
                    {s.subject}
                    <span className="mt-0.5 block text-xs text-muted">{s.subjectCode}</span>
                  </td>
                  <td className="slab tnum py-2 pr-3 text-right font-bold">{score(s.total)}</td>
                  <td className="slab py-2 pr-3 font-bold">{s.grade}</td>
                  <td className="tnum py-2 pr-3 text-right text-muted">{average(s.classAverage)}</td>
                  <td className="tnum py-2 pr-3 text-right">{ordinal(s.position)}</td>
                  <td className="py-2 text-slate-700">{s.remark ?? ""}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {(entry.teacherRemark || entry.principalRemark) && (
        <dl className="mt-4 grid gap-2 text-sm">
          {entry.teacherRemark && (
            <>
              <dt className="text-muted">Class teacher</dt>
              <dd className="text-ink">{entry.teacherRemark}</dd>
            </>
          )}
          {entry.principalRemark && (
            <>
              <dt className="text-muted">Principal</dt>
              <dd className="text-ink">{entry.principalRemark}</dd>
            </>
          )}
        </dl>
      )}
    </Card>
  );
}

export function AttendanceCard({ data }: { data: PortalAttendanceDto | null | undefined }) {
  if (!data) {
    return <EmptyState title="No register this term" text="Attendance shows once this child has a class in the current term." />;
  }
  const pct = data.open > 0 ? Math.round((data.present / data.open) * 100) : null;
  const line = attendanceLine({
    daysPresent: data.present,
    daysLate: data.late,
    daysAbsent: data.absent,
    daysExcused: data.excused,
    daysOpen: data.open,
  });
  return (
    <Card>
      <SectionTitle>
        {data.termName} · {data.className}
      </SectionTitle>
      <p className="slab tnum text-3xl font-bold text-ink">
        {data.present}
        <span className="text-lg font-semibold text-muted"> / {data.open}</span>
      </p>
      <p className="mt-1 text-sm text-muted">Days present of days the class was open{pct !== null ? ` (${pct}%)` : ""}. Late days are not counted as present.</p>
      <p className="mt-1 text-sm break-words text-slate-700">{line}.</p>
    </Card>
  );
}

export function TimetableWeek({ data }: { data: PortalTimetableDto | null | undefined }) {
  if (!data || data.slots.length === 0) {
    return <EmptyState title="No timetable yet" text="The school hasn't published this class's lessons for the week." />;
  }
  const days = SCHOOL_DAYS.filter((d) => d.value !== "Sat" || data.slots.some((s) => s.day === "Sat"));
  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
      {days.map((d) => {
        const slots = data.slots.filter((s) => s.day === d.value).sort((a, b) => a.period - b.period);
        return (
          <section key={d.value} className={cx(panelClass, "p-4")} aria-labelledby={`day-${d.value}`}>
            <SectionTitle id={`day-${d.value}`}>{d.label}</SectionTitle>
            {slots.length === 0 ? (
              <p className="text-sm text-muted">No lessons.</p>
            ) : (
              <ul className="space-y-2">
                {slots.map((s) => (
                  <li key={s.id} className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-x-3 text-sm">
                    <span className="tnum text-muted">
                      {s.start}–{s.end}
                    </span>
                    <span className="min-w-0">
                      <span className="font-bold text-ink">{s.subject}</span>
                      <span className="block text-xs text-muted">
                        Period {s.period}
                        {s.teacher ? `, ${s.teacher}` : ""}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        );
      })}
    </div>
  );
}

/** Published exam sittings of the child's class, by period. */
export function ExamTimetableList({ data }: { data: PortalExamTimetableDto | null | undefined }) {
  const periods = (data?.periods ?? []).filter((p) => p.sittings.length > 0);
  if (periods.length === 0) {
    return <EmptyState title="No exams published yet" text="When the school publishes an exam timetable for this class, it shows here." />;
  }
  return (
    <div className="min-w-0 space-y-5">
      {periods.map((period) => (
        <section key={period.id} className="min-w-0 space-y-3" aria-labelledby={`exam-${period.id}`}>
          <div className="min-w-0">
            <h2 id={`exam-${period.id}`} className="font-semibold break-words text-ink">
              {period.name}
            </h2>
            <p className="tnum text-xs text-muted">
              {period.kind === "Test" ? "Test" : "Examination"} · {formatDay(period.start)} – {formatDay(period.end)}
            </p>
          </div>
          <ul className="min-w-0 space-y-3" aria-label={`${period.name} sittings`}>
            {period.sittings.map((s) => (
              <li key={s.id} className={cx(panelClass, "w-full min-w-0 space-y-1 p-4")}>
                <p className="font-bold break-words text-ink">
                  {s.subject} <span className="font-medium text-muted">· {s.subjectCode}</span>
                </p>
                <p className="tnum text-sm text-slate-700">
                  {formatDay(s.date)} · {s.start}–{s.end} · {s.durationMinutes} min
                </p>
                {s.venue && <p className="text-sm break-words text-slate-700">{s.venue}</p>}
                {s.instructions && <p className="text-sm break-words whitespace-pre-line text-slate-800">{s.instructions}</p>}
                {s.invigilator && <p className="text-xs text-muted">Invigilator: {s.invigilator}</p>}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  );
}

export function AnnouncementList({ items }: { items: PortalAnnouncementDto[] }) {
  if (items.length === 0) {
    return <EmptyState title="No announcements yet" text="News from the school will show here." />;
  }
  return (
    <div className="ruled-sheet">
      <ul className="ruled-rows" aria-label="Announcements">
        {items.map((a) => (
          <li key={a.id} className="ruled-pad space-y-2 py-4">
            <div>
              <h2 className="font-bold text-ink">{a.title}</h2>
              <p className="tnum mt-0.5 text-xs text-muted">{formatStamp(a.postedAt)}</p>
            </div>
            <p className="text-sm break-words whitespace-pre-line text-slate-800">{a.body}</p>
            {a.attachmentUrl && (
              <a href={a.attachmentUrl} target="_blank" rel="noopener noreferrer" className={`${linkClass} inline-flex min-h-9 items-center gap-1.5 text-sm`}>
                <DownloadIcon className="h-4 w-4" />
                {a.attachmentName ?? "Attachment"}
              </a>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}

export function OverviewResults({ results, href }: { results: PortalTermDto[]; href: string }) {
  const current = results.find((t) => t.isCurrent) ?? results[0];
  if (!current) {
    return (
      <Card>
        <SectionTitle>Results</SectionTitle>
        <p className="text-sm text-muted">No terms on record yet.</p>
      </Card>
    );
  }
  return (
    <Card>
      <SectionTitle action={<Link href={href} className={linkClass}>See all</Link>}>
        Results
      </SectionTitle>
      <p className="text-sm text-ink">
        {current.termName}, {current.sessionName}
      </p>
      {current.held ? (
        <p className="mt-2 text-sm text-slate-700">{RESULTS_HELD}</p>
      ) : !current.published ? (
        <p className="mt-2 text-sm text-muted">Not published yet.</p>
      ) : current.classes.length === 0 ? (
        <p className="mt-2 text-sm text-muted">Published, with no class row yet.</p>
      ) : (
        <ul className="mt-2 space-y-1 text-sm">
          {current.classes.map((c) => (
            <li key={c.classId}>
              <span className="font-bold text-ink">{c.className}</span>
              {c.position > 0 && (
                <span className="text-muted">
                  {" "}
                  · average {average(c.average)}, {ordinal(c.position)} of {c.classSize}
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

export function invoiceStatus(status: string): { label: string; tone: "brick" | "crest" | "pass" | "neutral" } {
  switch (status) {
    case "Unpaid":
      return { label: "Unpaid", tone: "brick" };
    case "PartlyPaid":
      return { label: "Part paid", tone: "crest" };
    case "Paid":
      return { label: "Paid", tone: "pass" };
    case "Cancelled":
      return { label: "Cancelled", tone: "neutral" };
    default:
      return { label: status, tone: "neutral" };
  }
}

export function InvoiceStatusBadge({ status }: { status: string }) {
  const s = invoiceStatus(status);
  return <Badge tone={s.tone}>{s.label}</Badge>;
}

export function DueLine({ dueDate }: { dueDate: string }) {
  return <span className="tnum text-sm text-muted">Due {formatDay(dueDate)}</span>;
}

function Glyph({ children, ...p }: SVGProps<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      width={20}
      height={20}
      aria-hidden="true"
      {...p}
    >
      {children}
    </svg>
  );
}

export const AttendanceIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <rect x="4" y="3" width="16" height="18" rx="2" />
    <path d="m8 9 1.5 1.5L12 8M8 15l1.5 1.5L12 14M15 9h2M15 15h2" />
  </Glyph>
);
export const ResultsIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <path d="M5 4h10l4 4v12H5z" />
    <path d="M15 4v4h4M8 12h8M8 16h5" />
  </Glyph>
);
export const TimetableIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <rect x="3" y="5" width="18" height="16" rx="2" />
    <path d="M3 10h18M8 5V3M16 5V3M8 14h2M12 14h2M16 14h2M8 17h2M12 17h2" />
  </Glyph>
);
export const FeesIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <rect x="2.5" y="6" width="19" height="12" rx="2" />
    <circle cx="12" cy="12" r="2.5" />
    <path d="M6 9.5v5M18 9.5v5" />
  </Glyph>
);
/** A clipboard: homework in the parent and student portals. */
export const HomeworkIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <rect x="6" y="3" width="12" height="18" rx="2" />
    <path d="M9 3.5h6V6H9zM8 11h8M8 15h5" />
  </Glyph>
);
/** A calendar with a tick: the exam timetable in the parent and student portals. */
export const ExamsIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <rect x="3" y="5" width="18" height="16" rx="2" />
    <path d="M3 10h18M8 3v4M16 3v4M8 15l2 2 4-4" />
  </Glyph>
);
/** A screen with ticked lines: computer-based tests in the student portal. */
export const TestsIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <rect x="3" y="4" width="18" height="13" rx="2" />
    <path d="M8 21h8M12 17v4M7 9l1.5 1.5L11 8M13 9.5h4" />
  </Glyph>
);
/** A puzzle piece: Play in the student portal. */
export const PlayIcon = (p: SVGProps<SVGSVGElement>) => (
  <Glyph {...p}>
    <path d="M10 4a2 2 0 1 1 4 0v2h4v4h-2a2 2 0 1 0 0 4h2v4h-4v-2a2 2 0 1 0-4 0v2H6v-4h2a2 2 0 1 0 0-4H6V6h4z" />
  </Glyph>
);
