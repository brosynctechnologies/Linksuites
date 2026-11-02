"use client";

import Link from "next/link";
import { useState, type DragEvent, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatLength } from "@/lib/format";
import { VIDEO_STATUS, statusOf } from "@/lib/status";
import type { LessonSummaryDto, LessonType, StudioCourseDto, StudioLessonDto, StudioSectionDto } from "@/lib/types";
import { IconButton } from "@/components/editors";
import { ArrowDownIcon, ArrowUpIcon, EditIcon, FileIcon, GripIcon, PlayIcon, PlusIcon, QuizIcon, TextIcon, TrashIcon } from "@/components/icons";
import { Badge, Button, ErrorBox, Modal, StatusBadge, cx, inputClass } from "@/components/ui";

const LESSON_TYPES: { value: LessonType; label: string; hint: string }[] = [
  { value: "Video", label: "Video", hint: "Upload a video; learners stream it at a quality their data allows." },
  { value: "Text", label: "Reading", hint: "A written lesson in Markdown." },
  { value: "File", label: "Download", hint: "Worksheets, past questions, slides." },
  { value: "Quiz", label: "Quiz", hint: "Questions with a pass mark. Learners must pass to finish." },
];

function TypeIcon({ type, className }: { type: LessonType; className?: string }) {
  if (type === "Video") return <PlayIcon className={className} />;
  if (type === "File") return <FileIcon className={className} />;
  if (type === "Quiz") return <QuizIcon className={className} />;
  return <TextIcon className={className} />;
}

type Sections = StudioSectionDto[];

function sorted(course: StudioCourseDto): Sections {
  return [...course.sections]
    .sort((a, b) => a.position - b.position)
    .map((s) => ({ ...s, lessons: [...s.lessons].sort((a, b) => a.position - b.position) }));
}

/** Remove a lesson and insert it in `toSection` at `toIndex` (index in the list without it). */
function placeLesson(sections: Sections, lessonId: string, toSection: string, toIndex: number): Sections {
  let moving: LessonSummaryDto | undefined;
  const without = sections.map((s) => {
    const hit = s.lessons.find((l) => l.id === lessonId);
    if (hit) moving = hit;
    return { ...s, lessons: s.lessons.filter((l) => l.id !== lessonId) };
  });
  if (!moving) return sections;
  return without.map((s) => {
    if (s.id !== toSection) return s;
    const lessons = s.lessons.slice();
    lessons.splice(Math.max(0, Math.min(toIndex, lessons.length)), 0, moving!);
    return { ...s, lessons };
  });
}

/**
 * Sections with lesson rows. Reorder by drag and drop (HTML5) or with the move buttons; lessons can move
 * between sections. Every change saves the full order with PUT /order.
 */
export function Curriculum({ course, onChange, canEdit }: { course: StudioCourseDto; onChange: (c: StudioCourseDto) => void; canEdit: boolean }) {
  const [sections, setSections] = useState<Sections>(() => sorted(course));
  const [seen, setSeen] = useState(course);
  if (seen !== course) {
    setSeen(course);
    setSections(sorted(course));
  }
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [announce, setAnnounce] = useState("");
  const [drag, setDrag] = useState<{ kind: "lesson" | "section"; id: string } | null>(null);
  const [over, setOver] = useState<string | null>(null);
  const [addLessonTo, setAddLessonTo] = useState<StudioSectionDto | null>(null);
  const [newSection, setNewSection] = useState("");

  async function run(key: string, fn: () => Promise<StudioCourseDto | void>) {
    setBusy(key);
    setError(null);
    try {
      const res = await fn();
      if (res) onChange(res);
    } catch (e) {
      setError(toApiError(e));
      setSections(sorted(course));
    } finally {
      setBusy(null);
    }
  }

  function saveOrder(next: Sections, message: string) {
    setSections(next);
    setAnnounce(message);
    void run("order", () =>
      api<StudioCourseDto>(`/api/studio/courses/${course.id}/order`, {
        method: "PUT",
        body: { sections: next.map((s) => ({ id: s.id, lessonIds: s.lessons.map((l) => l.id) })) },
      }),
    );
  }

  // ----- Keyboard-accessible moves -----
  function moveLessonBy(lesson: LessonSummaryDto, delta: -1 | 1) {
    const si = sections.findIndex((s) => s.lessons.some((l) => l.id === lesson.id));
    const li = sections[si].lessons.findIndex((l) => l.id === lesson.id);
    const target = li + delta;
    if (target >= 0 && target < sections[si].lessons.length) {
      saveOrder(placeLesson(sections, lesson.id, sections[si].id, target), `${lesson.title} moved to position ${target + 1} in ${sections[si].title}.`);
      return;
    }
    // Past the edge of its section: into the neighbouring section.
    const ns = si + delta;
    if (ns < 0 || ns >= sections.length) return;
    const index = delta < 0 ? sections[ns].lessons.length : 0;
    saveOrder(placeLesson(sections, lesson.id, sections[ns].id, index), `${lesson.title} moved to ${sections[ns].title}.`);
  }

  function moveSectionBy(section: StudioSectionDto, delta: -1 | 1) {
    const i = sections.findIndex((s) => s.id === section.id);
    const j = i + delta;
    if (j < 0 || j >= sections.length) return;
    const next = sections.slice();
    [next[i], next[j]] = [next[j], next[i]];
    saveOrder(next, `${section.title} is now section ${j + 1}.`);
  }

  // ----- Drag and drop -----
  function onDragStart(e: DragEvent, kind: "lesson" | "section", id: string) {
    e.stopPropagation();
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", `${kind}:${id}`);
    setDrag({ kind, id });
  }
  function endDrag() {
    setDrag(null);
    setOver(null);
  }
  function allowDrop(e: DragEvent, key: string, accepts: "lesson" | "section" | "both") {
    if (!drag || (accepts !== "both" && drag.kind !== accepts)) return;
    e.preventDefault();
    e.stopPropagation();
    e.dataTransfer.dropEffect = "move";
    if (over !== key) setOver(key);
  }
  function dropOnLesson(e: DragEvent, section: StudioSectionDto, beforeLessonId: string) {
    e.preventDefault();
    e.stopPropagation();
    if (drag?.kind !== "lesson" || drag.id === beforeLessonId) return endDrag();
    const without = section.lessons.filter((l) => l.id !== drag.id);
    const index = without.findIndex((l) => l.id === beforeLessonId);
    const title = sections.flatMap((s) => s.lessons).find((l) => l.id === drag.id)?.title ?? "Lesson";
    saveOrder(placeLesson(sections, drag.id, section.id, index), `${title} moved in ${section.title}.`);
    endDrag();
  }
  function dropOnSectionEnd(e: DragEvent, section: StudioSectionDto) {
    e.preventDefault();
    e.stopPropagation();
    if (drag?.kind === "lesson") {
      const title = sections.flatMap((s) => s.lessons).find((l) => l.id === drag.id)?.title ?? "Lesson";
      saveOrder(placeLesson(sections, drag.id, section.id, Number.MAX_SAFE_INTEGER), `${title} moved to the end of ${section.title}.`);
    } else if (drag?.kind === "section" && drag.id !== section.id) {
      const moving = sections.find((s) => s.id === drag.id)!;
      const rest = sections.filter((s) => s.id !== drag.id);
      const at = rest.findIndex((s) => s.id === section.id);
      rest.splice(at, 0, moving);
      saveOrder(rest, `${moving.title} moved before ${section.title}.`);
    }
    endDrag();
  }

  // ----- Section and lesson actions -----
  async function renameSection(s: StudioSectionDto) {
    const title = window.prompt("Section title", s.title)?.trim();
    if (!title || title === s.title) return;
    await run(`s-${s.id}`, () => api<StudioCourseDto>(`/api/studio/sections/${s.id}`, { method: "PUT", body: { title } }));
  }
  async function deleteSection(s: StudioSectionDto) {
    if (!window.confirm(s.lessons.length ? `Delete "${s.title}" and its ${s.lessons.length} lesson(s)? This can't be undone.` : `Delete "${s.title}"?`)) return;
    await run(`s-${s.id}`, () => api<StudioCourseDto>(`/api/studio/sections/${s.id}`, { method: "DELETE" }));
  }
  async function renameLesson(l: LessonSummaryDto) {
    const title = window.prompt("Lesson title", l.title)?.trim();
    if (!title || title === l.title) return;
    await run(`l-${l.id}`, async () => {
      // PUT needs the whole lesson; read it first so the content is kept.
      const full = await api<StudioLessonDto>(`/api/studio/lessons/${l.id}`);
      await api<StudioLessonDto>(`/api/studio/lessons/${l.id}`, { method: "PUT", body: { title, type: full.type, isPreview: full.isPreview, content: full.content } });
      return api<StudioCourseDto>(`/api/studio/courses/${course.id}`);
    });
  }
  async function deleteLesson(l: LessonSummaryDto) {
    if (!window.confirm(`Delete the lesson "${l.title}"? Its video, files and quiz go too.`)) return;
    await run(`l-${l.id}`, async () => {
      await api(`/api/studio/lessons/${l.id}`, { method: "DELETE" });
      return api<StudioCourseDto>(`/api/studio/courses/${course.id}`);
    });
  }
  async function addSection(e: FormEvent) {
    e.preventDefault();
    const title = newSection.trim();
    if (!title) return;
    await run("add-section", () => api<StudioCourseDto>(`/api/studio/courses/${course.id}/sections`, { method: "POST", body: { title } }));
    setNewSection("");
  }

  const lessonCount = sections.reduce((n, s) => n + s.lessons.length, 0);

  return (
    <div className="space-y-4">
      <p className="sr-only" role="status" aria-live="polite">
        {announce}
      </p>
      {error && <ErrorBox error={error} />}
      {sections.length === 0 && <p className="rounded-panel border border-dashed border-slate-300 px-4 py-8 text-center text-sm text-muted">Add a section to start your curriculum, e.g. “Week 1: Mechanics”.</p>}

      <ol className="space-y-4">
        {sections.map((s, si) => (
          <li
            key={s.id}
            className={cx("overflow-hidden rounded-panel border bg-surface", over === `sec-${s.id}` && drag?.kind === "section" ? "border-lagoon" : "border-line", drag?.id === s.id && "opacity-50")}
            onDragOver={(e) => allowDrop(e, `sec-${s.id}`, "section")}
            onDrop={(e) => dropOnSectionEnd(e, s)}
          >
            <div className="flex items-center gap-2 border-b border-line-soft bg-slate-50 px-3 py-2">
              {canEdit && (
                <span
                  draggable
                  onDragStart={(e) => onDragStart(e, "section", s.id)}
                  onDragEnd={endDrag}
                  className="cursor-grab text-faint active:cursor-grabbing"
                  title="Drag to reorder"
                  aria-hidden="true"
                >
                  <GripIcon className="h-4 w-4" />
                </span>
              )}
              <div className="min-w-0 flex-1">
                <p className="text-xs text-muted">Section {si + 1}</p>
                <p className="truncate font-semibold text-ink">{s.title}</p>
              </div>
              {canEdit && (
                <div className="flex items-center">
                  <IconButton label={`Move section ${s.title} up`} disabled={si === 0 || !!busy} onClick={() => moveSectionBy(s, -1)}>
                    <ArrowUpIcon className="h-4 w-4" />
                  </IconButton>
                  <IconButton label={`Move section ${s.title} down`} disabled={si === sections.length - 1 || !!busy} onClick={() => moveSectionBy(s, 1)}>
                    <ArrowDownIcon className="h-4 w-4" />
                  </IconButton>
                  <IconButton label={`Rename section ${s.title}`} onClick={() => renameSection(s)} disabled={!!busy}>
                    <EditIcon className="h-4 w-4" />
                  </IconButton>
                  <IconButton label={`Delete section ${s.title}`} danger onClick={() => deleteSection(s)} disabled={!!busy}>
                    <TrashIcon className="h-4 w-4" />
                  </IconButton>
                </div>
              )}
            </div>

            <ul className="divide-y divide-line-soft">
              {s.lessons.map((l, li) => {
                const vs = statusOf(VIDEO_STATUS, l.videoStatus);
                const firstOverall = si === 0 && li === 0;
                const lastOverall = si === sections.length - 1 && li === s.lessons.length - 1;
                return (
                  <li
                    key={l.id}
                    onDragOver={(e) => allowDrop(e, `l-${l.id}`, "lesson")}
                    onDrop={(e) => dropOnLesson(e, s, l.id)}
                    className={cx(
                      "relative flex items-center gap-2 px-3 py-2.5",
                      drag?.id === l.id && "opacity-40",
                      over === `l-${l.id}` && drag?.kind === "lesson" && drag.id !== l.id && "before:absolute before:inset-x-3 before:top-0 before:h-[3px] before:rounded-full before:bg-lagoon",
                    )}
                  >
                    {canEdit && (
                      <span
                        draggable
                        onDragStart={(e) => onDragStart(e, "lesson", l.id)}
                        onDragEnd={endDrag}
                        className="cursor-grab text-faint active:cursor-grabbing"
                        title="Drag to reorder"
                        aria-hidden="true"
                      >
                        <GripIcon className="h-4 w-4" />
                      </span>
                    )}
                    <TypeIcon type={l.type} className="h-4 w-4 shrink-0 text-muted" />
                    <Link href={`/studio/lessons/${l.id}`} className="min-w-0 flex-1 rounded-control hover:underline">
                      <span className="block truncate font-serif text-[0.975rem] text-ink">{l.title}</span>
                      <span className="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs text-muted">
                        {LESSON_TYPES.find((t) => t.value === l.type)?.label ?? l.type}
                        {formatLength(l.durationSec) && <span className="tnum">{formatLength(l.durationSec)}</span>}
                        {l.isPreview && <Badge className="bg-lagoon-wash text-lagoon-text">Free preview</Badge>}
                        {l.type === "Video" && <StatusBadge tone={vs.tone}>{l.videoStatus === "None" ? "No video yet" : vs.label}</StatusBadge>}
                      </span>
                    </Link>
                    {canEdit && (
                      <div className="flex items-center">
                        <IconButton label={`Move ${l.title} up`} disabled={firstOverall || !!busy} onClick={() => moveLessonBy(l, -1)}>
                          <ArrowUpIcon className="h-4 w-4" />
                        </IconButton>
                        <IconButton label={`Move ${l.title} down`} disabled={lastOverall || !!busy} onClick={() => moveLessonBy(l, 1)}>
                          <ArrowDownIcon className="h-4 w-4" />
                        </IconButton>
                        <IconButton label={`Rename ${l.title}`} onClick={() => renameLesson(l)} disabled={!!busy} className="hidden sm:flex">
                          <EditIcon className="h-4 w-4" />
                        </IconButton>
                        <IconButton label={`Delete ${l.title}`} danger onClick={() => deleteLesson(l)} disabled={!!busy} className="hidden sm:flex">
                          <TrashIcon className="h-4 w-4" />
                        </IconButton>
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>

            {canEdit && (
              <div
                onDragOver={(e) => allowDrop(e, `end-${s.id}`, "lesson")}
                onDrop={(e) => dropOnSectionEnd(e, s)}
                className={cx("border-t border-line-soft px-3 py-2", over === `end-${s.id}` && drag?.kind === "lesson" && "bg-lagoon-wash")}
              >
                {drag?.kind === "lesson" ? (
                  <p className="py-1.5 text-center text-xs text-muted">Drop here to move to the end of this section</p>
                ) : (
                  <button type="button" onClick={() => setAddLessonTo(s)} className="flex items-center gap-1.5 rounded-control px-2 py-1.5 text-sm font-semibold text-lagoon-text hover:bg-lagoon-wash">
                    <PlusIcon className="h-4 w-4" /> Add lesson
                  </button>
                )}
              </div>
            )}
          </li>
        ))}
      </ol>

      {canEdit && (
        <form onSubmit={addSection} className="flex gap-2">
          <label htmlFor="new-section" className="sr-only">
            New section title
          </label>
          <input id="new-section" className={inputClass} placeholder="New section, e.g. Week 2: Waves" value={newSection} onChange={(e) => setNewSection(e.target.value)} />
          <Button type="submit" variant="secondary" loading={busy === "add-section"} disabled={!newSection.trim()}>
            <PlusIcon className="h-4 w-4" /> Add section
          </Button>
        </form>
      )}
      {lessonCount > 0 && canEdit && <p className="text-xs text-muted">Drag the handles, or use the arrows, to reorder. Lessons can move between sections.</p>}

      <Modal open={!!addLessonTo} title={addLessonTo ? `Add a lesson to ${addLessonTo.title}` : "Add a lesson"} onClose={() => setAddLessonTo(null)}>
        {addLessonTo && (
          <AddLessonForm
            section={addLessonTo}
            onDone={(c) => {
              setAddLessonTo(null);
              onChange(c);
            }}
          />
        )}
      </Modal>
    </div>
  );
}

function AddLessonForm({ section, onDone }: { section: StudioSectionDto; onDone: (c: StudioCourseDto) => void }) {
  const [title, setTitle] = useState("");
  const [type, setType] = useState<LessonType>("Video");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onDone(await api<StudioCourseDto>(`/api/studio/sections/${section.id}/lessons`, { method: "POST", body: { title: title.trim(), type } }));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div>
        <label htmlFor="lesson-title" className="text-sm font-medium text-slate-700">
          Lesson title
        </label>
        <input id="lesson-title" autoFocus required className={cx(inputClass, "mt-1.5")} value={title} onChange={(e) => setTitle(e.target.value)} />
      </div>
      <fieldset className="space-y-2">
        <legend className="text-sm font-medium text-slate-700">Type</legend>
        {LESSON_TYPES.map((t) => (
          <label key={t.value} className={cx("flex cursor-pointer items-start gap-3 rounded-control border p-3", type === t.value ? "border-lagoon bg-lagoon-wash" : "border-line hover:bg-slate-50")}>
            <input type="radio" name="lesson-type" value={t.value} checked={type === t.value} onChange={() => setType(t.value)} className="mt-1 accent-[var(--lagoon)]" />
            <TypeIcon type={t.value} className="mt-0.5 h-4 w-4 shrink-0 text-muted" />
            <span>
              <span className="block text-sm font-semibold text-ink">{t.label}</span>
              <span className="block text-xs text-slate-600">{t.hint}</span>
            </span>
          </label>
        ))}
      </fieldset>
      {error && <ErrorBox error={error} />}
      <Button type="submit" className="w-full" loading={busy} disabled={!title.trim()}>
        Add lesson
      </Button>
    </form>
  );
}
