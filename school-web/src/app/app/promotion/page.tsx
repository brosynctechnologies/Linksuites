"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { plural } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { ClassDto, PromotionAction, PromotionPreviewDto, PromotionResult, SchoolSetupDto } from "@/lib/types";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, NoAccess, Notice, PageHeader, PageLoader, RuledSheet, SectionTitle, Select, buttonClass, type BadgeTone } from "@/components/ui";

interface Choice {
  action: PromotionAction;
  classId: string;
}

const TONE: Record<PromotionAction, BadgeTone> = { Promote: "pass", Repeat: "crest", Graduate: "blazer" };

/** "2026/2027" → "2027/2028"; anything else is left for the user to type. */
function nextSessionName(name: string | undefined) {
  const m = name?.match(/^(\d{4})\s*\/\s*(\d{4})$/);
  return m ? `${Number(m[1]) + 1}/${Number(m[2]) + 1}` : "";
}

function addDays(date: string | undefined, days: number) {
  if (!date) return "";
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** "Start next session": each class moves up a level (the final level graduates); hold back or move named students; one confirm. */
export default function PromotionPage() {
  const canManage = useCan("school.manage");
  const preview = useApi<PromotionPreviewDto>(canManage ? "/api/school/promotion/preview" : null);
  const setup = useApi<SchoolSetupDto>(canManage ? "/api/school/setup" : null);
  const classes = useApi<ClassDto[]>(canManage ? "/api/school/classes" : null);
  const [result, setResult] = useState<PromotionResult | null>(null);

  if (!canManage) return <NoAccess />;
  if (result) {
    return (
      <div className="space-y-5">
        <PageHeader title="Next session" />
        <Card>
          <SectionTitle>{result.sessionName} has started</SectionTitle>
          <p className="text-slate-700">
            {plural(result.promoted, "student")} moved up, {plural(result.repeated, "student")} repeating and {plural(result.graduated, "student")} graduated.
            Classes and their subjects were copied to the new session.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            <Link href="/app/setup/classes" className={buttonClass("primary")}>
              Check the new classes
            </Link>
            <Link href="/app" className={buttonClass("secondary")}>
              Back to home
            </Link>
          </div>
        </Card>
      </div>
    );
  }

  const loadError = preview.error ?? setup.error ?? classes.error;
  const ready = preview.data && setup.data && classes.data;
  return (
    <div className="space-y-5">
      <PageHeader title="Start the next session" subtitle="Usually done once a year, after the third term's results." />
      {loadError && <ApiErrorView error={loadError} onRetry={preview.reload} />}
      {!ready && !loadError && <PageLoader />}
      {ready && <Wizard preview={preview.data!} setup={setup.data!} classes={classes.data!} onDone={setResult} />}
    </div>
  );
}

function Wizard({ preview, setup, classes, onDone }: { preview: PromotionPreviewDto; setup: SchoolSetupDto; classes: ClassDto[]; onDone: (r: PromotionResult) => void }) {
  const current = setup.sessions.find((s) => s.isCurrent) ?? setup.sessions[0];
  const [name, setName] = useState(nextSessionName(current?.name));
  const [start, setStart] = useState(addDays(current?.endDate, 1));
  const [end, setEnd] = useState(addDays(addDays(current?.endDate, 1), 364));
  const [choices, setChoices] = useState<Record<string, Choice>>({});
  const { busy, error, run } = useAction();

  const setChoice = (studentId: string, c: Choice | null) =>
    setChoices((prev) => {
      const next = { ...prev };
      if (c) next[studentId] = c;
      else delete next[studentId];
      return next;
    });

  const unplacedOpen = preview.unplaced.filter((u) => {
    const c = choices[u.studentId];
    return !c || (c.action !== "Graduate" && !c.classId);
  });

  async function submit(e: FormEvent) {
    e.preventDefault();
    const overrides = Object.entries(choices).map(([studentId, c]) => ({ studentId, action: c.action, classId: c.classId || null }));
    const res = await run(() => api<PromotionResult>("/api/school/promotion", { method: "POST", body: { sessionName: name.trim(), start, end, overrides } }));
    if (res.ok) onDone(res.data);
  }

  const total = preview.classes.reduce((n, c) => n + c.students.length, 0);
  return (
    <form onSubmit={submit} className="space-y-5">
      <Card>
        <SectionTitle>The new session</SectionTitle>
        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="Name" required value={name} onChange={(e) => setName(e.target.value)} placeholder="2027/2028" error={error?.field("sessionName")} />
          <Input label="Starts" type="date" required value={start} onChange={(e) => setStart(e.target.value)} error={error?.field("start")} />
          <Input label="Ends" type="date" required value={end} onChange={(e) => setEnd(e.target.value)} error={error?.field("end")} />
        </div>
        <p className="mt-3 text-sm text-muted">Three terms are proposed inside these dates; you can change them afterwards. The current session becomes history.</p>
      </Card>

      {preview.unplaced.length > 0 && (
        <section aria-labelledby="unplaced" className="space-y-3">
          <h2 id="unplaced" className="slab text-xl font-bold text-ink">
            Students without a class
          </h2>
          <Notice tone="warning">Place each of these students in a class, or graduate them, before starting the new session.</Notice>
          <RuledSheet label="Students without a class">
            {preview.unplaced.map((u) => (
              <StudentChoice key={u.studentId} name={u.name} proposed={null} classes={classes} choice={choices[u.studentId]} onChange={(c) => setChoice(u.studentId, c)} />
            ))}
          </RuledSheet>
        </section>
      )}

      <section aria-labelledby="classes" className="space-y-3">
        <h2 id="classes" className="slab text-xl font-bold text-ink">
          Where each class goes
        </h2>
        <p className="text-sm text-slate-700">
          {plural(total, "student")} in {plural(preview.classes.length, "class", "classes")}. Change a student only if they should repeat, graduate or move to a
          different class.
        </p>
        {preview.classes.length === 0 && <p className="text-sm text-muted">No classes with students in this session.</p>}
        {preview.classes.map((c) => (
          <details key={c.fromClassId} className="ruled-sheet group">
            <summary className="ruled-pad flex min-h-12 cursor-pointer list-none items-center justify-between gap-3 py-3">
              <span>
                <span className="slab text-lg font-bold text-ink">{c.fromClassName}</span>
                <span className="text-slate-700"> to </span>
                <span className="slab text-lg font-bold text-ink">{c.toClassName}</span>
              </span>
              <span className="text-sm text-muted">
                {plural(c.students.length, "student")}
                {c.students.some((s) => choices[s.studentId]) && <Badge tone="crest" className="ml-2">Changed</Badge>}
              </span>
            </summary>
            <ul className="ruled-rows border-t border-rule">
              {c.students.map((s) => (
                <StudentChoice key={s.studentId} name={s.name} proposed={s.action} classes={classes} choice={choices[s.studentId]} onChange={(ch) => setChoice(s.studentId, ch)} />
              ))}
            </ul>
          </details>
        ))}
      </section>

      <ErrorBox error={error && !["sessionName", "start", "end"].some((f) => error.field(f)) ? error : null} />
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" loading={busy} disabled={unplacedOpen.length > 0 || !name.trim() || !start || !end}>
          Start {name.trim() || "the new session"}
        </Button>
        {unplacedOpen.length > 0 && <span className="text-sm text-crest">{plural(unplacedOpen.length, "student")} still need a class.</span>}
      </div>
    </form>
  );
}

function StudentChoice({
  name,
  proposed,
  classes,
  choice,
  onChange,
}: {
  name: string;
  proposed: PromotionAction | null;
  classes: ClassDto[];
  choice: Choice | undefined;
  onChange: (c: Choice | null) => void;
}) {
  const value = choice ? (choice.action === "Graduate" ? "Graduate" : choice.action === "Repeat" ? "Repeat" : `move:${choice.classId}`) : "";
  return (
    <li className="ruled-pad flex flex-wrap items-center justify-between gap-x-4 gap-y-2 py-2.5">
      <span className="flex items-center gap-2">
        <span className="font-bold text-ink">{name}</span>
        {proposed && !choice && <Badge tone={TONE[proposed]}>{proposed === "Promote" ? "Moves up" : proposed === "Graduate" ? "Graduates" : "Repeats"}</Badge>}
      </span>
      <div className="w-56">
        <Select
          label={`What happens to ${name}`}
          srLabel
          value={value}
          onChange={(e) => {
            const v = e.target.value;
            if (!v) onChange(null);
            else if (v === "Graduate" || v === "Repeat") onChange({ action: v, classId: "" });
            else onChange({ action: "Promote", classId: v.slice(5) });
          }}
        >
          <option value="">{proposed ? "As proposed" : "Choose"}</option>
          {proposed && <option value="Repeat">Repeat the class</option>}
          <option value="Graduate">Graduate</option>
          {classes.map((c) => (
            <option key={c.id} value={`move:${c.id}`}>
              Put in {c.name}
            </option>
          ))}
        </Select>
      </div>
    </li>
  );
}
