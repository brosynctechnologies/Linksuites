"use client";

import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan, useRole } from "@/lib/roles";
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

function WordListForm({ levels, readOnly }: { levels: LevelDto[]; readOnly: boolean }) {
  const [scope, setScope] = useState<Scope>("school");
  const [levelId, setLevelId] = useState("");
  const [name, setName] = useState("");
  const [text, setText] = useState("");
  const lists = useApi<WordListDto[]>("/api/school/play/words");
  const create = useAction();
  const words = parseWords(text);
  const pickedLevel = levels.some((l) => l.id === levelId) ? levelId : (levels[0]?.id ?? "");

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: SaveWordListRequest = {
      classSubjectId: null,
      levelId: scope === "level" ? pickedLevel || null : null,
      name: name.trim(),
      words,
    };
    const res = await create.run(() => api<WordListDto>("/api/school/play/words", { method: "POST", body }));
    if (res.ok) {
      lists.setData((current) => [res.data, ...(current ?? [])]);
      setName("");
      setText("");
    }
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
      </p>
      {lists.error && <ApiErrorView error={lists.error} onRetry={lists.reload} />}
      {!lists.data && !lists.error && <PageLoader />}
      {lists.data && lists.data.length === 0 && <p className="text-sm text-muted">No word lists yet. Students get the built-in list.</p>}
      {lists.data && lists.data.length > 0 && (
        <ul className="min-w-0 space-y-3" aria-label="Word lists">
          {lists.data.map((list) => (
            <li key={list.id} className="min-w-0 space-y-1">
              <p className="text-sm font-bold break-words text-ink">
                {list.name} <span className="font-medium text-muted">· {scopeLabel(list, levels)}</span>
              </p>
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
        <form className="w-full min-w-0 space-y-3 border-t border-rule-soft pt-3" onSubmit={submit} aria-label="Add a word list">
          <ErrorBox error={create.error} />
          <Input label="Name" required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
          <Select label="For" value={scope} onChange={(e) => setScope(e.target.value as Scope)}>
            <option value="school">The whole school</option>
            <option value="level">One level</option>
          </Select>
          {scope === "level" && (
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
          <Button type="submit" loading={create.busy} disabled={words.length === 0}>
            Save word list
          </Button>
        </form>
      )}
    </section>
  );
}

function TeaserForm({ levels, readOnly }: { levels: LevelDto[]; readOnly: boolean }) {
  const [levelId, setLevelId] = useState("");
  const [prompt, setPrompt] = useState("");
  const [answer, setAnswer] = useState("");
  const teasers = useApi<BrainTeaserDto[]>("/api/school/play/teasers");
  const create = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: SaveBrainTeaserRequest = { levelId: levelId || null, prompt: prompt.trim(), answer: answer.trim() };
    const res = await create.run(() => api<BrainTeaserDto>("/api/school/play/teasers", { method: "POST", body }));
    if (res.ok) {
      teasers.setData((current) => [res.data, ...(current ?? [])]);
      setPrompt("");
      setAnswer("");
    }
  }

  return (
    <section className={cx(panelClass, "w-full min-w-0 space-y-3 p-4")} aria-labelledby="teaser">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="teaser" className="font-semibold text-ink">
          Brain teasers
        </h2>
        {!readOnly && <ImportDialog kind="teasers" title="Import brain teasers" onDone={teasers.reload} />}
      </div>
      <p className="text-sm text-muted">Students see the teaser only, a few at a time. The answer stays with the staff.</p>
      {teasers.error && <ApiErrorView error={teasers.error} onRetry={teasers.reload} />}
      {!teasers.data && !teasers.error && <PageLoader />}
      {teasers.data && teasers.data.length === 0 && <p className="text-sm text-muted">No teasers yet.</p>}
      {teasers.data && teasers.data.length > 0 && (
        <ul className="min-w-0 space-y-2" aria-label="Brain teasers">
          {teasers.data.map((t) => (
            <li key={t.id} className="min-w-0 text-sm">
              <p className="break-words whitespace-pre-line text-ink">{t.prompt}</p>
              <p className="break-words text-muted">
                Answer: {t.answer} · {levels.find((l) => l.id === t.levelId)?.name ?? "Every level"}
                {t.archived ? " · Archived" : ""}
              </p>
            </li>
          ))}
        </ul>
      )}
      {!readOnly && (
        <form className="w-full min-w-0 space-y-3 border-t border-rule-soft pt-3" onSubmit={submit} aria-label="Add a brain teaser">
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
          <Button type="submit" loading={create.busy}>
            Save teaser
          </Button>
        </form>
      )}
    </section>
  );
}
