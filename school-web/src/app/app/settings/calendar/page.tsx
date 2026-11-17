"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, NoAccess, Notice, PageHeader, PageLoader, SectionTitle } from "@/components/ui";
import { api } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { can, useCan, useRole } from "@/lib/roles";
import type { SchoolSetupDto, SessionDto, TermDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";

// The API's column sizes (SchoolCalendarService).
const MAX_SESSION_NAME = 20;
const MAX_TERM_NAME = 40;

/** The session's and terms' names and dates, and which term the school is in now. Owner and Admin. */
export default function CalendarSettingsPage() {
  const allowed = can(useRole(), "school.manage");
  const canEdit = useCan("school.manage"); // false in a support view, which is read only
  const setup = useApi<SchoolSetupDto>(allowed ? "/api/school/setup" : null);

  if (!allowed) return <NoAccess text="Only the proprietor and the principal can change the school's terms and dates." />;
  const session = setup.data?.sessions.find((s) => s.isCurrent);

  return (
    <div className="space-y-5">
      <PageHeader
        title="Terms and dates"
        subtitle="Correct your session and term dates, and move the school to the next term when it starts. Attendance, results and fees all follow the current term."
      />
      {setup.error && <ApiErrorView error={setup.error} onRetry={setup.reload} />}
      {!setup.data && !setup.error && <PageLoader />}
      {setup.data && !session && <Notice tone="warning">Your school has no current session. Start one from Promotion.</Notice>}
      {session && (
        <>
          <SessionForm key={`${session.id}-${session.name}-${session.startDate}-${session.endDate}`} session={session} canEdit={canEdit} onSaved={setup.reload} />
          {session.terms.map((t) => (
            <TermCard key={`${t.id}-${t.isCurrent}-${t.startDate}-${t.endDate}-${t.name}`} term={t} canEdit={canEdit} onSaved={setup.reload} />
          ))}
          <p className="text-sm text-muted">
            At the end of the last term, start the next session from{" "}
            <Link href="/app/promotion" className="font-medium underline underline-offset-4">
              Promotion
            </Link>
            : it moves every student up a class and sets up the new session&apos;s terms.
          </p>
        </>
      )}
    </div>
  );
}

function SessionForm({ session, canEdit, onSaved }: { session: SessionDto; canEdit: boolean; onSaved: () => void }) {
  const [name, setName] = useState(session.name);
  const [start, setStart] = useState(session.startDate);
  const [end, setEnd] = useState(session.endDate);
  const [saved, setSaved] = useState(false);
  const { busy, error, run } = useAction();
  const local = !name.trim() ? "Enter a name." : end <= start ? "The session must end after it starts." : null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (local) return;
    const res = await run(() =>
      api<SessionDto>(`/api/school/sessions/${session.id}`, { method: "PUT", body: { name: name.trim(), startDate: start, endDate: end } }),
    );
    if (res.ok) {
      setSaved(true);
      onSaved();
    }
  }

  return (
    <Card>
      <SectionTitle>Session</SectionTitle>
      <form onSubmit={submit} className="grid max-w-3xl gap-4 sm:grid-cols-3" noValidate>
        <Input label="Name" maxLength={MAX_SESSION_NAME} disabled={!canEdit} value={name} onChange={(e) => (setName(e.target.value), setSaved(false))} error={error?.field("name")} />
        <Input label="Starts" type="date" disabled={!canEdit} value={start} onChange={(e) => (setStart(e.target.value), setSaved(false))} error={error?.field("startDate")} />
        <Input label="Ends" type="date" disabled={!canEdit} value={end} onChange={(e) => (setEnd(e.target.value), setSaved(false))} error={error?.field("endDate") ?? (end <= start ? "The session must end after it starts." : undefined)} />
        <div className="space-y-3 sm:col-span-3">
          {error && !error.field("name") && !error.field("startDate") && !error.field("endDate") && <ErrorBox error={error} />}
          {saved && <Notice tone="success">Session saved.</Notice>}
          {canEdit && (
            <Button type="submit" loading={busy} disabled={!!local}>
              Save session
            </Button>
          )}
        </div>
      </form>
    </Card>
  );
}

function TermCard({ term, canEdit, onSaved }: { term: TermDto; canEdit: boolean; onSaved: () => void }) {
  const [name, setName] = useState(term.name);
  const [start, setStart] = useState(term.startDate);
  const [end, setEnd] = useState(term.endDate);
  const [next, setNext] = useState(term.nextTermStarts ?? "");
  const [saved, setSaved] = useState(false);
  const save = useAction();
  const move = useAction();
  const local = !name.trim() ? "Enter a name." : end <= start ? "The term must end after it starts." : next && next <= end ? "The next term must start after this one ends." : null;
  const touch = (set: (v: string) => void) => (v: string) => (set(v), setSaved(false));

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (local) return;
    const body = { name: name.trim(), startDate: start, endDate: end, nextTermStarts: next || null };
    const res = await save.run(() => api<SessionDto>(`/api/school/terms/${term.id}`, { method: "PUT", body }));
    if (res.ok) {
      setSaved(true);
      onSaved();
    }
  }

  async function makeCurrent() {
    if (!window.confirm(`Make ${term.name} the current term? Attendance, results and fees will use ${term.name} from now on.`)) return;
    const res = await move.run(() => api<SessionDto>(`/api/school/terms/${term.id}/make-current`, { method: "POST" }));
    if (res.ok) onSaved();
  }

  return (
    <Card>
      <SectionTitle
        action={
          term.isCurrent ? (
            <Badge tone="pass">Current term</Badge>
          ) : (
            canEdit && (
              <Button variant="secondary" onClick={makeCurrent} loading={move.busy}>
                Make current term
              </Button>
            )
          )
        }
      >
        {term.name}
      </SectionTitle>
      <p className="-mt-2 mb-4 text-sm text-muted">
        {formatDay(term.startDate)} to {formatDay(term.endDate)}
      </p>
      {move.error && <ErrorBox error={move.error} />}
      <form onSubmit={submit} className="grid max-w-3xl gap-4 sm:grid-cols-2 lg:grid-cols-4" noValidate>
        <Input label="Name" maxLength={MAX_TERM_NAME} disabled={!canEdit} value={name} onChange={(e) => touch(setName)(e.target.value)} error={save.error?.field("name")} />
        <Input label="Starts" type="date" disabled={!canEdit} value={start} onChange={(e) => touch(setStart)(e.target.value)} error={save.error?.field("startDate")} />
        <Input label="Ends" type="date" disabled={!canEdit} value={end} onChange={(e) => touch(setEnd)(e.target.value)} error={save.error?.field("endDate")} />
        <Input
          label="Next term starts"
          type="date"
          disabled={!canEdit}
          value={next}
          onChange={(e) => touch(setNext)(e.target.value)}
          error={save.error?.field("nextTermStarts")}
          hint="Optional. Printed on report cards."
        />
        <div className="space-y-3 sm:col-span-2 lg:col-span-4">
          {local && <p className="text-sm text-brick-dark">{local}</p>}
          {save.error && !["name", "startDate", "endDate", "nextTermStarts"].some((f) => save.error?.field(f)) && <ErrorBox error={save.error} />}
          {saved && <Notice tone="success">{term.name} saved.</Notice>}
          {canEdit && (
            <Button type="submit" loading={save.busy} disabled={!!local}>
              Save {term.name}
            </Button>
          )}
        </div>
      </form>
    </Card>
  );
}
