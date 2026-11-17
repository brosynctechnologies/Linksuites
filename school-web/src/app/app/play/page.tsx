"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
import { useAuth } from "@/lib/auth";
import { useApi } from "@/lib/use-api";
import type { LevelDto } from "@/lib/types";
import type {
  BrainTeaserDto,
  PlaySettingsDto,
  PlaySettingsRequest,
  SaveBrainTeaserRequest,
  SaveWordListRequest,
  WordListDto,
} from "@/lib/types/academic";
import { ApiErrorView, Badge, Button, ErrorBox, Input, PageHeader, PageLoader, Select, Textarea, Toggle, cx, panelClass } from "@/components/ui";
import { ImportDialog } from "@/components/import-dialog";
import { AcademicNoAccess, useReadOnly } from "../scores/academic-shared";

const MAX_WORDS = 20;

type Scope = "school" | "level";

function parseWords(text: string): string[] {
  return text
    .split(/[\s,;]+/)
    .map((w) => w.trim())
    .filter(Boolean);
}

export default function PlayPage() {
  const role = useRole();
  const readOnly = useReadOnly();
  const canManage = useCan("school.manage");
  const levels = useApi<LevelDto[]>("/api/school/levels");

  if (role === "Accountant") return <AcademicNoAccess what="Play" />;

  const levelList = [...(levels.data ?? [])].sort((a, b) => a.order - b.order);

  return (
    <div className="min-w-0 space-y-4">
      <PageHeader
        title="Play"
        subtitle="Word lists for the student word search, and brain teasers. Play never writes a score or appears on a report card."
      />
      {canManage && <PlaySetting />}
      {levels.error && <ApiErrorView error={levels.error} onRetry={levels.reload} />}
      {!levels.data && !levels.error && <PageLoader />}
      {levels.data && (
        <>
          <WordListForm levels={levelList} readOnly={readOnly} />
          <TeaserForm levels={levelList} readOnly={readOnly} />
        </>
      )}
      {readOnly && <p className="text-sm text-muted">A support view can&apos;t add word lists or teasers.</p>}
    </div>
  );
}

function PlaySetting() {
  const setting = useApi<PlaySettingsDto>("/api/school/settings/play");
  const save = useAction();

  async function set(enabled: boolean) {
    const body: PlaySettingsRequest = { enabled };
    const res = await save.run(() => api<PlaySettingsDto>("/api/school/settings/play", { method: "PUT", body }));
    if (res.ok) setting.setData(res.data);
  }

  return (
    <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="play-setting">
      <h2 id="play-setting" className="font-semibold text-ink">
        Play for students
      </h2>
      {setting.error && <ApiErrorView error={setting.error} onRetry={setting.reload} />}
      {!setting.data && !setting.error && <PageLoader />}
      {setting.data && (
        <Toggle
          checked={setting.data.enabled}
          disabled={save.busy}
          onChange={set}
          label={setting.data.enabled ? "Play is on" : "Play is off"}
          description="When it is off, students see “Play is turned off for this school.” in place of the games."
        />
      )}
      <ErrorBox error={save.error} />
    </section>
  );
}

function scopeLabel(list: { classSubjectId: string | null; levelId: string | null }, levels: LevelDto[]): string {
  if (list.classSubjectId) return "One class subject";
  if (list.levelId) return levels.find((l) => l.id === list.levelId)?.name ?? "One level";
  return "Whole school";
}


/** The writer of an item, or leadership, can edit and archive it. A support view never can. */
function useCanChange(readOnly: boolean): (createdBy: string) => boolean {
  const canManage = useCan("school.manage");
  const me = useAuth()?.user.id;
  return (createdBy) => !readOnly && (canManage || createdBy === me);
}

function WordListForm({ levels, readOnly }: { levels: LevelDto[]; readOnly: boolean }) {
  const [scope, setScope] = useState<Scope>("school");
  const [levelId, setLevelId] = useState("");
  const [name, setName] = useState("");
  const [text, setText] = useState("");
  const [editing, setEditing] = useState<WordListDto | null>(null);
  const lists = useApi<WordListDto[]>("/api/school/play/words");
  const create = useAction();
  const archive = useAction();
  const canChange = useCanChange(readOnly);
  const words = parseWords(text);
  const pickedLevel = levels.some((l) => l.id === levelId) ? levelId : (levels[0]?.id ?? "");

  function reset() {
    setEditing(null);
    setName("");
    setText("");
    setScope("school");
    create.setError(null);
  }

  function startEdit(list: WordListDto) {
    setEditing(list);
    setName(list.name);
    setText(list.words.join("\n"));
    setScope(list.levelId ? "level" : "school");
    setLevelId(list.levelId ?? "");
    create.setError(null);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const keepsClassSubject = editing?.classSubjectId ?? null;
    const body: SaveWordListRequest = {
      classSubjectId: keepsClassSubject,
      levelId: !keepsClassSubject && scope === "level" ? pickedLevel || null : null,
      name: name.trim(),
      words,
    };
    const res = await create.run(() =>
      editing
        ? api<WordListDto>(`/api/school/play/words/${editing.id}`, { method: "PUT", body })
        : api<WordListDto>("/api/school/play/words", { method: "POST", body }),
    );
    if (res.ok) {
      lists.setData((current) =>
        editing ? (current ?? []).map((l) => (l.id === res.data.id ? res.data : l)) : [res.data, ...(current ?? [])],
      );
      reset();
    }
  }

  async function setArchived(list: WordListDto, archived: boolean) {
    const res = await archive.run(() =>
      api<WordListDto>(`/api/school/play/words/${list.id}/${archived ? "archive" : "unarchive"}`, { method: "POST" }),
    );
    if (res.ok) lists.setData((current) => (current ?? []).map((l) => (l.id === res.data.id ? res.data : l)));
  }

  return (
    <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="word-list">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="word-list" className="font-semibold text-ink">
          Word lists
        </h2>
        {!readOnly && <ImportDialog kind="word-lists" title="Import word lists" onDone={lists.reload} />}
      </div>
      <p className="text-sm text-muted">
        A student gets the newest list for their level, else the newest list for the whole school. With no list, they get a built-in list for their stage.
        Archived lists are kept here but students don&apos;t get them.
      </p>
      {lists.error && <ApiErrorView error={lists.error} onRetry={lists.reload} />}
      {!lists.data && !lists.error && <PageLoader />}
      {lists.data && lists.data.length === 0 && <p className="text-sm text-muted">No word lists yet. Students get the built-in list.</p>}
      <ErrorBox error={archive.error} />
      {lists.data && lists.data.length > 0 && (
        <ul className="min-w-0 space-y-3" aria-label="Word lists">
          {lists.data.map((list) => (
            <li key={list.id} className={cx("min-w-0 space-y-1", list.archived && "opacity-70")}>
              <div className="flex min-w-0 flex-wrap items-start justify-between gap-2">
                <p className="min-w-0 text-sm font-bold break-words text-ink">
                  {list.name} <span className="font-medium text-muted">· {scopeLabel(list, levels)}</span>
                  {list.archived && (
                    <>
                      {" "}
                      <Badge>Archived</Badge>
                    </>
                  )}
                </p>
                {canChange(list.createdBy) && (
                  <span className="flex flex-wrap gap-1.5">
                    <Button variant="ghost" onClick={() => startEdit(list)} aria-label={`Edit ${list.name}`}>
                      Edit
                    </Button>
                    <Button
                      variant="ghost"
                      disabled={archive.busy}
                      onClick={() => setArchived(list, !list.archived)}
                      aria-label={`${list.archived ? "Bring back" : "Archive"} ${list.name}`}
                    >
                      {list.archived ? "Bring back" : "Archive"}
                    </Button>
                  </span>
                )}
              </div>
              <ul className="flex flex-wrap gap-1.5">
                {list.words.map((w) => (
                  <li key={w}>
                    <Badge>{w}</Badge>
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}
      {!readOnly && (
        <form
          className="w-full min-w-0 space-y-3 border-t border-rule-soft pt-3"
          onSubmit={submit}
          aria-label={editing ? `Edit ${editing.name}` : "Add a word list"}
        >
          {editing && <p className="text-sm font-bold text-ink">Editing {editing.name}</p>}
          <ErrorBox error={create.error} />
          <Input label="Name" required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
          {editing?.classSubjectId ? (
            <p className="text-sm text-muted">This list stays with its class subject.</p>
          ) : (
            <Select label="For" value={scope} onChange={(e) => setScope(e.target.value as Scope)}>
              <option value="school">The whole school</option>
              <option value="level">One level</option>
            </Select>
          )}
          {!editing?.classSubjectId && scope === "level" && (
            <Select label="Level" value={pickedLevel} onChange={(e) => setLevelId(e.target.value)}>
              {levels.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </Select>
          )}
          <Textarea
            label="Words"
            required
            rows={4}
            value={text}
            onChange={(e) => setText(e.target.value)}
            hint={`One word per line, or separated by commas. Letters only, 3 to 15 letters each, up to ${MAX_WORDS} words. ${words.length} so far.`}
          />
          <div className="flex flex-wrap gap-2">
            <Button type="submit" loading={create.busy} disabled={words.length === 0}>
              {editing ? "Save changes" : "Save word list"}
            </Button>
            {editing && (
              <Button type="button" variant="secondary" onClick={reset}>
                Cancel
              </Button>
            )}
          </div>
        </form>
      )}
    </section>
  );
}

function TeaserForm({ levels, readOnly }: { levels: LevelDto[]; readOnly: boolean }) {
  const [levelId, setLevelId] = useState("");
  const [prompt, setPrompt] = useState("");
  const [answer, setAnswer] = useState("");
  const [editing, setEditing] = useState<BrainTeaserDto | null>(null);
  const teasers = useApi<BrainTeaserDto[]>("/api/school/play/teasers");
  const create = useAction();
  const archive = useAction();
  const canChange = useCanChange(readOnly);

  function reset() {
    setEditing(null);
    setLevelId("");
    setPrompt("");
    setAnswer("");
    create.setError(null);
  }

  function startEdit(teaser: BrainTeaserDto) {
    setEditing(teaser);
    setLevelId(teaser.levelId ?? "");
    setPrompt(teaser.prompt);
    setAnswer(teaser.answer);
    create.setError(null);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: SaveBrainTeaserRequest = { levelId: levelId || null, prompt: prompt.trim(), answer: answer.trim() };
    const res = await create.run(() =>
      editing
        ? api<BrainTeaserDto>(`/api/school/play/teasers/${editing.id}`, { method: "PUT", body })
        : api<BrainTeaserDto>("/api/school/play/teasers", { method: "POST", body }),
    );
    if (res.ok) {
      teasers.setData((current) =>
        editing ? (current ?? []).map((t) => (t.id === res.data.id ? res.data : t)) : [res.data, ...(current ?? [])],
      );
      reset();
    }
  }

  async function setArchived(teaser: BrainTeaserDto, archived: boolean) {
    const res = await archive.run(() =>
      api<BrainTeaserDto>(`/api/school/play/teasers/${teaser.id}/${archived ? "archive" : "unarchive"}`, { method: "POST" }),
    );
    if (res.ok) teasers.setData((current) => (current ?? []).map((t) => (t.id === res.data.id ? res.data : t)));
  }

  return (
    <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="teaser">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="teaser" className="font-semibold text-ink">
          Brain teasers
        </h2>
        {!readOnly && <ImportDialog kind="teasers" title="Import brain teasers" onDone={teasers.reload} />}
      </div>
      <p className="text-sm text-muted">
        Students see the teaser only, a few at a time. The answer stays with the staff. Archived teasers are kept here but students don&apos;t see them.
      </p>
      {teasers.error && <ApiErrorView error={teasers.error} onRetry={teasers.reload} />}
      {!teasers.data && !teasers.error && <PageLoader />}
      {teasers.data && teasers.data.length === 0 && <p className="text-sm text-muted">No teasers yet.</p>}
      <ErrorBox error={archive.error} />
      {teasers.data && teasers.data.length > 0 && (
        <ul className="min-w-0 space-y-2" aria-label="Brain teasers">
          {teasers.data.map((t) => (
            <li key={t.id} className={cx("flex min-w-0 flex-wrap items-start justify-between gap-2 text-sm", t.archived && "opacity-70")}>
              <div className="min-w-0 flex-1">
                <p className="break-words whitespace-pre-line text-ink">{t.prompt}</p>
                <p className="break-words text-muted">
                  Answer: {t.answer} · {levels.find((l) => l.id === t.levelId)?.name ?? "Every level"}
                  {t.archived ? " · Archived" : ""}
                </p>
              </div>
              {canChange(t.createdBy) && (
                <span className="flex flex-wrap gap-1.5">
                  <Button variant="ghost" onClick={() => startEdit(t)} aria-label="Edit this teaser">
                    Edit
                  </Button>
                  <Button
                    variant="ghost"
                    disabled={archive.busy}
                    onClick={() => setArchived(t, !t.archived)}
                    aria-label={t.archived ? "Bring back this teaser" : "Archive this teaser"}
                  >
                    {t.archived ? "Bring back" : "Archive"}
                  </Button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
      {!readOnly && (
        <form
          className="w-full min-w-0 space-y-3 border-t border-rule-soft pt-3"
          onSubmit={submit}
          aria-label={editing ? "Edit the brain teaser" : "Add a brain teaser"}
        >
          {editing && <p className="text-sm font-bold text-ink">Editing a teaser</p>}
          <ErrorBox error={create.error} />
          <Select label="Level" value={levelId} onChange={(e) => setLevelId(e.target.value)}>
            <option value="">Every level</option>
            {levels.map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </Select>
          <Textarea label="Teaser" required maxLength={500} value={prompt} onChange={(e) => setPrompt(e.target.value)} />
          <Input label="Answer" required maxLength={200} value={answer} onChange={(e) => setAnswer(e.target.value)} />
          <div className="flex flex-wrap gap-2">
            <Button type="submit" loading={create.busy}>
              {editing ? "Save changes" : "Save teaser"}
            </Button>
            {editing && (
              <Button type="button" variant="secondary" onClick={reset}>
                Cancel
              </Button>
            )}
          </div>
        </form>
      )}
    </section>
  );
}
