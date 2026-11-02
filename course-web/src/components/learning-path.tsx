import Link from "next/link";
import type { ReactNode } from "react";
import { formatLength } from "@/lib/format";
import type { LessonType } from "@/lib/types";
import { AwardIcon, CheckIcon, FileIcon, LockIcon, PlayIcon, TextIcon } from "./icons";
import { cx } from "./styles";

/*
 * THE LEARNING PATH: the one memorable element of LearnLink.
 * A course's curriculum drawn as a vertical route: a line connecting lesson nodes, filled in lime up to
 * where the learner has reached, the current lesson as a ringed node, quizzes as diamonds, and the
 * certificate as the final node. Used empty on the course page (a preview of the journey), live in the
 * player sidebar, and as a slim horizontal strip on each course in My learning.
 *
 * No hooks here, so Server Components can render it (actions are passed in as rendered nodes).
 */

export interface PathLesson {
  id: string;
  title: string;
  type: LessonType;
  durationSec?: number | null;
  done?: boolean;
  /** Quizzes draw as diamonds. */
  isQuiz?: boolean;
  passed?: boolean | null;
  isPreview?: boolean;
}
export interface PathSection {
  id: string;
  title: string;
  lessons: PathLesson[];
}

type Row =
  | { kind: "section"; id: string; title: string; index: number; nextNode: number }
  | { kind: "lesson"; lesson: PathLesson; node: number }
  | { kind: "certificate"; node: number };

const TYPE_LABEL: Record<LessonType, string> = { Video: "Video", Text: "Reading", File: "Download", Quiz: "Quiz" };

export function lessonTypeLabel(type: LessonType) {
  return TYPE_LABEL[type] ?? type;
}

function TypeGlyph({ type, className }: { type: LessonType; className?: string }) {
  if (type === "Video") return <PlayIcon className={className} />;
  if (type === "File") return <FileIcon className={className} />;
  if (type === "Quiz") return null;
  return <TextIcon className={className} />;
}

function Node({ lesson, current, fresh }: { lesson: PathLesson; current: boolean; fresh: boolean }) {
  const quiz = lesson.isQuiz || lesson.type === "Quiz";
  const done = quiz ? !!(lesson.passed ?? lesson.done) : !!lesson.done;
  if (quiz) {
    return (
      <span className="relative flex h-5 w-5 items-center justify-center" aria-hidden="true">
        <span
          className={cx(
            "block h-[13px] w-[13px] rotate-45 rounded-[2px] border-2",
            done ? "border-lime bg-lime" : current ? "border-lagoon bg-surface ring-4 ring-lagoon-wash" : "border-slate-300 bg-surface",
            fresh && "animate-node-fill",
          )}
        />
      </span>
    );
  }
  if (done) {
    return (
      <span
        className={cx("flex h-5 w-5 items-center justify-center rounded-full bg-lime text-white", current && "ring-4 ring-lagoon-wash", fresh && "animate-node-fill")}
        aria-hidden="true"
      >
        <CheckIcon className="h-3 w-3" strokeWidth={3.2} />
      </span>
    );
  }
  if (current) {
    return (
      <span className="flex h-5 w-5 items-center justify-center rounded-full border-2 border-lagoon bg-surface ring-4 ring-lagoon-wash" aria-hidden="true">
        <span className="h-2 w-2 rounded-full bg-lagoon" />
      </span>
    );
  }
  return (
    <span className="flex h-5 w-5 items-center justify-center" aria-hidden="true">
      <span className="h-3 w-3 rounded-full border-2 border-slate-300 bg-surface" />
    </span>
  );
}

/** One gutter of the route: the incoming and outgoing halves of the line around a node. */
function Gutter({ inLime, outLime, first, last, children, tone = "light" }: { inLime: boolean; outLime: boolean; first?: boolean; last?: boolean; children?: ReactNode; tone?: "light" | "dark" }) {
  const base = tone === "dark" ? "bg-white/15" : "bg-line";
  return (
    <div className="relative flex w-7 shrink-0 justify-center" aria-hidden={children ? undefined : true}>
      {!first && <span className={cx("absolute top-0 h-[1.125rem] w-[2px]", inLime ? "bg-lime" : base)} />}
      {!last && <span className={cx("absolute top-[1.125rem] bottom-0 w-[2px]", outLime ? "bg-lime" : base)} />}
      <span className="relative z-[1] mt-2">{children}</span>
    </div>
  );
}

export interface LearningPathProps {
  sections: PathSection[];
  /** The lesson being studied (ringed). */
  currentId?: string | null;
  /** Make lessons links (player sidebar). */
  hrefFor?: (lessonId: string) => string;
  /** Extra control on a lesson row (e.g. a Preview button on the course page). */
  renderAction?: (lesson: PathLesson) => ReactNode;
  /** The final node. */
  certificate?: { earned: boolean; label?: string; href?: string | null; note?: string };
  /** The lesson that just became complete: its node fills with a short pulse. */
  freshId?: string | null;
  /** Number the sections ("Section 2") above their titles. */
  numberSections?: boolean;
  className?: string;
  /** Lessons without access show a lock (course page, not enrolled). */
  locked?: boolean;
}

/** The vertical learning path. */
export function LearningPath({ sections, currentId, hrefFor, renderAction, certificate, freshId, numberSections = true, className, locked }: LearningPathProps) {
  // Flatten into rows and number the nodes along the route.
  const rows: Row[] = [];
  let node = 0;
  sections.forEach((s, i) => {
    rows.push({ kind: "section", id: s.id, title: s.title, index: i, nextNode: node });
    for (const l of s.lessons) rows.push({ kind: "lesson", lesson: l, node: node++ });
  });
  if (certificate) rows.push({ kind: "certificate", node: node++ });
  const lessons = rows.filter((r): r is Extract<Row, { kind: "lesson" }> => r.kind === "lesson");
  const totalNodes = node;

  // How far along the route the learner has reached: the current lesson, else just past the last done one.
  const isDone = (l: PathLesson) => (l.isQuiz || l.type === "Quiz" ? !!(l.passed ?? l.done) : !!l.done);
  let reached = -1;
  lessons.forEach((r) => {
    if (isDone(r.lesson)) reached = Math.max(reached, r.node);
  });
  const currentNode = lessons.find((r) => r.lesson.id === currentId)?.node ?? -1;
  reached = Math.max(reached, currentNode);
  if (certificate?.earned) reached = totalNodes - 1;

  return (
    <ol className={cx("relative", className)} aria-label="Course path">
      {rows.map((row, idx) => {
        const first = idx === 0;
        const last = idx === rows.length - 1;
        if (row.kind === "section") {
          const lime = row.nextNode <= reached && row.nextNode > 0;
          return (
            <li key={`s-${row.id}`} className="flex gap-3">
              <Gutter inLime={lime} outLime={lime} first={first} last={last} />
              <div className={cx("min-w-0 flex-1 pb-2", first ? "pt-0.5" : "pt-4")}>
                {numberSections && <p className="text-xs font-medium text-muted">Section {row.index + 1}</p>}
                <p className="font-semibold text-ink">{row.title}</p>
              </div>
            </li>
          );
        }
        if (row.kind === "certificate") {
          const earned = !!certificate?.earned;
          const body = (
            <>
              <p className={cx("font-serif text-[0.975rem] leading-snug", earned ? "font-semibold text-ink" : "text-slate-700")}>
                {certificate?.label ?? "Certificate of completion"}
              </p>
              {certificate?.note && <p className="mt-0.5 text-xs text-muted">{certificate.note}</p>}
            </>
          );
          return (
            <li key="certificate" className="flex gap-3">
              <Gutter inLime={earned} outLime={false} first={first} last>
                <span
                  className={cx(
                    "-mt-1 flex h-7 w-7 items-center justify-center rounded-full",
                    earned ? "animate-cert-in bg-lime text-white" : "border-2 border-dashed border-slate-300 bg-surface text-faint",
                  )}
                >
                  <AwardIcon className="h-4 w-4" />
                </span>
              </Gutter>
              <div className="min-w-0 flex-1 py-2">
                {earned && certificate?.href ? (
                  <Link href={certificate.href} className="block rounded-control hover:underline">
                    {body}
                  </Link>
                ) : (
                  body
                )}
              </div>
            </li>
          );
        }
        const l = row.lesson;
        const current = l.id === currentId;
        const done = isDone(l);
        const inLime = row.node <= reached && row.node > 0;
        const outLime = row.node + 1 <= reached;
        const meta = [lessonTypeLabel(l.type), formatLength(l.durationSec)].filter(Boolean).join(", ");
        const status = done ? (l.type === "Quiz" ? "passed" : "completed") : current ? "current lesson" : null;
        const content = (
          <>
            <span className={cx("block font-serif text-[0.975rem] leading-snug", current ? "font-semibold text-ink" : done ? "text-slate-700" : "text-ink")}>
              {l.title}
            </span>
            <span className="mt-0.5 flex items-center gap-1.5 text-xs text-muted">
              <TypeGlyph type={l.type} className="h-3 w-3" />
              {meta}
              {status && <span className="sr-only">, {status}</span>}
            </span>
          </>
        );
        return (
          <li key={l.id} className="flex gap-3" aria-current={current ? "step" : undefined}>
            <Gutter inLime={inLime} outLime={outLime} first={first} last={last}>
              <Node lesson={l} current={current} fresh={freshId === l.id} />
            </Gutter>
            <div className="flex min-w-0 flex-1 items-start gap-2 py-1.5">
              {hrefFor ? (
                <Link
                  href={hrefFor(l.id)}
                  className={cx("-mx-2 block min-w-0 flex-1 rounded-control px-2 py-0.5", current ? "bg-lagoon-wash" : "hover:bg-ink/[0.04]")}
                >
                  {content}
                </Link>
              ) : (
                <div className="min-w-0 flex-1 py-0.5">{content}</div>
              )}
              {renderAction?.(l)}
              {locked && !l.isPreview && <LockIcon className="mt-1 h-3.5 w-3.5 shrink-0 text-faint" aria-hidden="true" />}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

/**
 * The slim horizontal path for My learning: one node per lesson (or a continuous track for long courses),
 * lime up to the lessons done, the next lesson ringed, and the certificate at the end.
 */
export function PathStrip({ done, total, certified, className }: { done: number; total: number; certified?: boolean; className?: string }) {
  const safeTotal = Math.max(0, total);
  const safeDone = Math.max(0, Math.min(done, safeTotal));
  const showNodes = safeTotal > 0 && safeTotal <= 36;
  const pct = safeTotal ? (safeDone / safeTotal) * 100 : 0;
  const finished = safeTotal > 0 && safeDone >= safeTotal;
  return (
    <div className={cx("flex items-center gap-2", className)}>
      <div
        className="relative h-5 flex-1"
        role="img"
        aria-label={`${safeDone} of ${safeTotal} lessons done${certified ? ", certificate earned" : ""}`}
      >
        <span className="absolute inset-x-0 top-1/2 h-[2px] -translate-y-1/2 rounded-full bg-line" />
        <span className="absolute top-1/2 left-0 h-[2px] -translate-y-1/2 rounded-full bg-lime" style={{ width: `${pct}%` }} />
        {showNodes && (
          <span className="absolute inset-0 flex items-center justify-between">
            {Array.from({ length: safeTotal }, (_, i) => {
              const isDone = i < safeDone;
              const isNext = i === safeDone;
              return (
                <span
                  key={i}
                  className={cx(
                    "block rounded-full",
                    isDone ? "h-2 w-2 bg-lime" : isNext ? "h-2.5 w-2.5 border-2 border-lagoon bg-surface" : "h-2 w-2 border-2 border-slate-300 bg-surface",
                  )}
                />
              );
            })}
          </span>
        )}
        {!showNodes && safeTotal > 0 && !finished && (
          <span className="absolute top-1/2 h-2.5 w-2.5 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-lagoon bg-surface" style={{ left: `${pct}%` }} />
        )}
      </div>
      <span
        className={cx(
          "flex h-6 w-6 shrink-0 items-center justify-center rounded-full",
          certified ? "bg-lime text-white" : finished ? "border-2 border-lime text-lime-dark" : "border-2 border-dashed border-slate-300 text-faint",
        )}
        aria-hidden="true"
      >
        <AwardIcon className="h-3.5 w-3.5" />
      </span>
    </div>
  );
}
