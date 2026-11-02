"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { api, toApiError } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { brandVars } from "@/lib/color";
import { coursePath, formatLength } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { LearnCourseDto, LessonPlaybackDto, ProgressResponse, QuizResultDto } from "@/lib/types";
import { BackIcon, CheckIcon, ChevronLeftIcon, ChevronRightIcon, PathIcon, StarIcon } from "@/components/icons";
import { LearningPath, lessonTypeLabel, type PathSection } from "@/components/learning-path";
import { Markdown } from "@/components/markdown";
import { ApiErrorView, Button, Drawer, EmptyState, ErrorBox, PageLoader, buttonClass } from "@/components/ui";
import { VideoPlayer } from "@/components/video-player";
import { CertificateMoment, ReviewModal } from "./course-moments";
import { LessonTabs, Resources } from "./lesson-tabs";
import { QuizPlayer } from "./quiz-player";

const SAVE_EVERY_MS = 15_000;

function isCourseComplete(c: LearnCourseDto) {
  if (c.progressPercent >= 100) return true;
  const lessons = c.sections.flatMap((s) => s.lessons);
  return lessons.length > 0 && lessons.every((l) => (l.isQuiz || l.type === "Quiz" ? !!l.passed : l.completed));
}

function toPath(c: LearnCourseDto): PathSection[] {
  return c.sections.map((s) => ({
    id: s.id,
    title: s.title,
    lessons: s.lessons.map((l) => ({
      id: l.id,
      title: l.title,
      type: l.type,
      durationSec: l.durationSec,
      done: l.completed,
      isQuiz: l.isQuiz || l.type === "Quiz",
      passed: l.passed,
    })),
  }));
}

export default function LessonPlayerPage() {
  const { courseId, lessonId } = useParams<{ courseId: string; lessonId: string }>();
  const auth = useAuth();
  const course = useApi<LearnCourseDto>(`/api/learn/courses/${courseId}`);
  const lessonRes = useApi<LessonPlaybackDto>(`/api/learn/lessons/${lessonId}`);
  const lesson = lessonRes.data?.id === lessonId ? lessonRes.data : undefined;

  const [freshId, setFreshId] = useState<string | null>(null);
  const [moment, setMoment] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [pathOpen, setPathOpen] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [marking, setMarking] = useState(false);

  // Close the path sheet when a lesson in it is opened.
  const [seenLesson, setSeenLesson] = useState(lessonId);
  if (seenLesson !== lessonId) {
    setSeenLesson(lessonId);
    setPathOpen(false);
    setSaveError(null);
  }

  const setCourse = course.setData;
  const courseRef = useRef(course.data);
  useEffect(() => {
    courseRef.current = course.data;
  });

  /** Record a lesson as done in the local course state, and celebrate if that finished the course. */
  const markLocal = useCallback(
    (id: string, patch: { completed?: boolean; passed?: boolean }, res?: Partial<ProgressResponse>) => {
      const before = courseRef.current;
      if (!before) return;
      const wasComplete = isCourseComplete(before);
      const wasDone = before.sections.some((s) => s.lessons.some((l) => l.id === id && (l.completed || l.passed)));
      const next: LearnCourseDto = {
        ...before,
        progressPercent: res?.progressPercent ?? before.progressPercent,
        certificateCode: res?.certificateCode ?? before.certificateCode,
        sections: before.sections.map((s) => ({
          ...s,
          lessons: s.lessons.map((l) => (l.id === id ? { ...l, completed: patch.completed ?? l.completed, passed: patch.passed ?? l.passed } : l)),
        })),
      };
      setCourse(next);
      if (!wasDone && (patch.completed || patch.passed)) setFreshId(id);
      if (!wasComplete && (res?.courseCompleted || isCourseComplete(next))) setMoment(true);
    },
    [setCourse],
  );

  /** PUT progress. Returns false when it couldn't be saved. */
  const saveProgress = useCallback(
    async (id: string, positionSec: number, completed: boolean) => {
      try {
        const res = await api<ProgressResponse>(`/api/learn/lessons/${id}/progress`, {
          method: "PUT",
          body: { positionSec: Math.max(0, Math.floor(positionSec)), completed },
        });
        setSaveError(null);
        if (completed) markLocal(id, { completed: true }, res);
        else if (res) {
          setCourse((c) => (c ? { ...c, progressPercent: res.progressPercent ?? c.progressPercent } : c!));
        }
        return true;
      } catch (e) {
        const err = toApiError(e);
        setSaveError(err.status === 0 ? "Progress not saved: you're offline. It saves again when you reconnect." : err.message);
        return false;
      }
    },
    [markLocal, setCourse],
  );

  async function markComplete() {
    if (!lesson) return;
    setMarking(true);
    await saveProgress(lesson.id, lesson.positionSec ?? 0, true);
    setMarking(false);
  }

  /** After a quiz attempt: a pass completes the node; the course is re-read for progress and any certificate. */
  async function onQuizResult(r: QuizResultDto) {
    if (!r.passed) return;
    markLocal(lessonId, { passed: true, completed: true });
    try {
      const fresh = await api<LearnCourseDto>(`/api/learn/courses/${courseId}`);
      const before = courseRef.current;
      setCourse(fresh);
      if (before && !isCourseComplete(before) && isCourseComplete(fresh)) setMoment(true);
      else if (fresh.certificateCode && !before?.certificateCode) setMoment(true);
    } catch {
      /* the path already shows the pass */
    }
  }

  // ---------- States before the lesson can show ----------
  if (course.error && !course.data) {
    return (
      <PlayerFrame>
        <div className="mx-auto max-w-lg px-4 py-12">
          {course.error.status === 403 ? (
            <EmptyState
              title="You're not enrolled in this course"
              text="Enrol from the course page to open its lessons."
              action={
                <Link href="/courses" className={buttonClass("primary")}>
                  Browse courses
                </Link>
              }
            />
          ) : (
            <ApiErrorView error={course.error} onRetry={course.reload} />
          )}
        </div>
      </PlayerFrame>
    );
  }

  const c = course.data;
  const flat = c ? c.sections.flatMap((s) => s.lessons) : [];
  const index = flat.findIndex((l) => l.id === lessonId);
  const doneCount = flat.filter((l) => (l.isQuiz || l.type === "Quiz" ? !!l.passed : l.completed)).length;
  const current = flat[index];
  const isDone = current ? (current.isQuiz || current.type === "Quiz" ? !!current.passed : current.completed) : !!lesson?.completed;
  const pct = Math.round(c?.progressPercent ?? 0);

  const path = c ? (
    <LearningPath
      sections={toPath(c)}
      currentId={lessonId}
      freshId={freshId}
      hrefFor={(id) => `/learn/${courseId}/${id}`}
      certificate={{
        earned: !!c.certificateCode,
        label: c.certificateCode ? "Certificate earned" : "Finish line",
        href: c.certificateCode ? `/certificates/${encodeURIComponent(c.certificateCode)}` : null,
        note: c.certificateCode ? "View and share it" : "Every lesson done, every quiz passed",
      }}
    />
  ) : null;

  return (
    <PlayerFrame
      brand={c?.brandColor}
      bar={
        <>
          <Link href="/learn" className="-ml-2 flex h-10 w-10 shrink-0 items-center justify-center rounded-control text-on-ink hover:bg-white/10 hover:text-white" aria-label="Back to My learning">
            <BackIcon />
          </Link>
          <div className="min-w-0 flex-1">
            <p className="truncate font-serif text-[0.975rem] font-semibold text-white">{c?.title ?? " "}</p>
            {c && (
              <p className="tnum truncate text-xs text-on-ink">
                {doneCount} of {flat.length} done
              </p>
            )}
          </div>
          {c && (
            <div className="hidden w-40 items-center gap-2 sm:flex" role="progressbar" aria-label="Course progress" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
              <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-white/15">
                <span className="block h-full rounded-full bg-lime transition-[width] duration-500" style={{ width: `${pct}%` }} />
              </span>
              <span className="tnum text-xs font-semibold text-white">{pct}%</span>
            </div>
          )}
          <button
            type="button"
            onClick={() => setPathOpen(true)}
            className="flex h-10 shrink-0 items-center gap-1.5 rounded-control px-2.5 text-sm font-semibold text-white hover:bg-white/10 lg:hidden"
          >
            <PathIcon className="h-5 w-5" />
            Path
          </button>
        </>
      }
    >
      <div className="lg:grid lg:grid-cols-[minmax(0,1fr)_22rem]">
        <main className="min-w-0 pb-16">
          {!lesson ? (
            lessonRes.error ? (
              <div className="mx-auto max-w-2xl px-4 py-10">
                <ApiErrorView error={lessonRes.error} onRetry={lessonRes.reload} />
              </div>
            ) : (
              <PageLoader label="Loading lesson…" />
            )
          ) : (
            <>
              {lesson.type === "Video" && (
                <div className="bg-ink">
                  <div className="mx-auto w-full max-w-[min(100%,calc((100dvh-9rem)*16/9))]">
                    {lesson.video?.url && lesson.video.status === "Ready" ? (
                      <VideoStage key={lesson.id} lesson={lesson} done={isDone} watermark={lesson.watermark ?? auth?.user.email ?? null} onSave={saveProgress} />
                    ) : (
                      <div className="flex aspect-video items-center justify-center px-6 text-center text-sm text-on-ink">
                        {lesson.video?.status === "Failed" ? "This video couldn't be prepared. The tutor has been told; check back later." : "This video is still being prepared. Check back in a few minutes."}
                      </div>
                    )}
                  </div>
                </div>
              )}

              <div className="mx-auto max-w-4xl px-4 pt-5 sm:px-6 lg:px-10 lg:pt-8">
                <p className="text-sm text-muted">
                  {index >= 0 ? `Lesson ${index + 1} of ${flat.length}` : lessonTypeLabel(lesson.type)}
                  {formatLength(lesson.durationSec) ? `, ${formatLength(lesson.durationSec)}` : ""}
                </p>
                <h1 className="mt-1 font-serif text-[1.625rem] leading-tight font-semibold tracking-[-0.015em] text-ink sm:text-[1.875rem]">{lesson.title}</h1>

                {lesson.type === "Text" && <Markdown source={lesson.content} variant="lesson" className="mt-6" />}

                {lesson.type === "File" && (
                  <div className="mt-6">
                    <Resources resources={lesson.resources} />
                  </div>
                )}

                {lesson.type === "Quiz" && (
                  <div className="mt-6">
                    {lesson.quiz ? (
                      <QuizPlayer key={lesson.id} lessonId={lesson.id} info={lesson.quiz} intro={lesson.content} onResult={onQuizResult} />
                    ) : (
                      <p className="text-sm text-muted">This quiz has no questions yet.</p>
                    )}
                  </div>
                )}

                {/* Lesson actions */}
                <div className="mt-7 flex flex-wrap items-center gap-2 border-y border-line py-3">
                  {lesson.previousLessonId ? (
                    <Link href={`/learn/${courseId}/${lesson.previousLessonId}`} className={buttonClass("ghost")}>
                      <ChevronLeftIcon className="h-4 w-4" /> Previous
                    </Link>
                  ) : (
                    <span />
                  )}
                  <div className="ml-auto flex flex-wrap items-center gap-2">
                    {lesson.type !== "Quiz" &&
                      (isDone ? (
                        <span className="flex items-center gap-1.5 px-2 text-sm font-semibold text-lime-dark">
                          <CheckIcon className="h-4 w-4" /> Completed
                        </span>
                      ) : (
                        <Button variant="progress" loading={marking} onClick={markComplete}>
                          <CheckIcon className="h-4 w-4" /> Mark complete
                        </Button>
                      ))}
                    {lesson.nextLessonId ? (
                      <Link href={`/learn/${courseId}/${lesson.nextLessonId}`} className={buttonClass(isDone ? "primary" : "secondary")}>
                        Next lesson <ChevronRightIcon className="h-4 w-4" />
                      </Link>
                    ) : (
                      c && (
                        <button type="button" className={buttonClass("secondary")} onClick={() => (isCourseComplete(c) ? setMoment(true) : setPathOpen(true))}>
                          {isCourseComplete(c) ? "See your finish" : "Last lesson"}
                        </button>
                      )
                    )}
                  </div>
                </div>
                {saveError && (
                  <div className="mt-3">
                    <ErrorBox error={saveError} />
                  </div>
                )}

                <div className="mt-6">
                  <LessonTabs key={lesson.id} lesson={lesson} />
                </div>
              </div>
            </>
          )}
        </main>

        <aside className="hidden border-l border-line bg-surface lg:block" aria-label="Course path">
          <div className="sticky top-14 max-h-[calc(100dvh-3.5rem)] overflow-y-auto px-5 py-6">
            {path ?? <PageLoader />}
            {c && (
              <div className="mt-6 flex flex-wrap gap-x-4 gap-y-2 border-t border-line-soft pt-4 text-sm">
                <button type="button" onClick={() => setReviewOpen(true)} className="flex items-center gap-1.5 font-semibold text-lagoon-text hover:underline">
                  <StarIcon className="h-4 w-4" /> Rate this course
                </button>
                <Link href={coursePath(c.academySlug, c.slug)} className="font-semibold text-slate-700 hover:underline">
                  Course page
                </Link>
              </div>
            )}
          </div>
        </aside>
      </div>

      <Drawer open={pathOpen} title="Course path" onClose={() => setPathOpen(false)}>
        {path}
        {c && (
          <button type="button" onClick={() => setReviewOpen(true)} className="mt-6 flex items-center gap-1.5 text-sm font-semibold text-lagoon-text hover:underline">
            <StarIcon className="h-4 w-4" /> Rate this course
          </button>
        )}
      </Drawer>

      {c && (
        <CertificateMoment
          open={moment}
          onClose={() => setMoment(false)}
          courseTitle={c.title}
          certificateCode={c.certificateCode}
          onRate={() => {
            setMoment(false);
            setReviewOpen(true);
          }}
        />
      )}
      <ReviewModal open={reviewOpen} onClose={() => setReviewOpen(false)} courseId={courseId} />
    </PlayerFrame>
  );
}

/** Deep-ink player chrome; the academy's colour replaces Lagoon inside the player. */
function PlayerFrame({ children, bar, brand }: { children: React.ReactNode; bar?: React.ReactNode; brand?: string | null }) {
  return (
    <div className="min-h-screen bg-mist" style={brand ? (brandVars(brand) as React.CSSProperties) : undefined}>
      <header className="on-ink sticky top-[var(--support-bar,0px)] z-30 flex h-14 items-center gap-3 bg-ink px-4 sm:px-6">{bar}</header>
      {children}
    </div>
  );
}

/**
 * The video, with progress saving: every 15 s while playing, on pause and at the end; resume from the
 * saved position; marked complete automatically near the end.
 */
function VideoStage({
  lesson,
  done,
  watermark,
  onSave,
}: {
  lesson: LessonPlaybackDto;
  /** Marked complete elsewhere (the button): later saves keep it complete. */
  done: boolean;
  watermark: string | null;
  onSave: (id: string, positionSec: number, completed: boolean) => Promise<boolean>;
}) {
  const pos = useRef(lesson.positionSec ?? 0);
  const lastSaved = useRef(lesson.positionSec ?? 0);
  const completed = useRef(lesson.completed);
  const saveRef = useRef(onSave);
  useEffect(() => {
    saveRef.current = onSave;
    if (done) completed.current = true;
  });

  const save = useCallback(
    (force = false) => {
      const p = pos.current;
      if (!force && Math.abs(p - lastSaved.current) < 1) return;
      lastSaved.current = p;
      void saveRef.current(lesson.id, p, completed.current);
    },
    [lesson.id],
  );

  useEffect(() => {
    const t = setInterval(() => save(), SAVE_EVERY_MS);
    const onHide = () => {
      if (document.visibilityState === "hidden") save();
    };
    document.addEventListener("visibilitychange", onHide);
    return () => {
      clearInterval(t);
      document.removeEventListener("visibilitychange", onHide);
      save();
    };
  }, [save]);

  return (
    <VideoPlayer
      src={lesson.video!.url!}
      format={lesson.video!.format}
      poster={lesson.video!.thumbnailUrl}
      startAt={lesson.completed ? 0 : lesson.positionSec}
      watermark={watermark}
      title={lesson.title}
      onTime={(sec, duration) => {
        pos.current = sec;
        if (!completed.current && duration > 0 && sec >= duration - Math.max(8, duration * 0.05)) {
          completed.current = true;
          save(true);
        }
      }}
      onPause={(sec) => {
        pos.current = sec;
        save();
      }}
      onEnded={(sec) => {
        pos.current = sec;
        completed.current = true;
        save(true);
      }}
    />
  );
}
