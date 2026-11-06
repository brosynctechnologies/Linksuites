"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import type { WardRoundDto } from "@/lib/types/inpatient";
import { useAutosave, UnsavedBox } from "./autosave";
import { Addenda, type ClinicalRun } from "./clinical-parts";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Notice, PageLoader, SectionTitle, Textarea, cx } from "./ui";

/**
 * Ward rounds on an admission (clinical; the read is a logged open through `run`). Doctors and the director start a round; the draft is
 * the author's and autosaves (components/autosave.tsx); signing makes it final and corrections are addenda; the author discards an
 * unsigned draft. The API decides who is on the care team (403 "This patient isn't on your ward.").
 */
export function WardRounds({
  admissionId,
  run,
  meId,
  admitted,
  canStart,
  readOnly,
}: {
  admissionId: string;
  run: ClinicalRun;
  meId: string;
  /** The patient is still admitted: after discharge every draft is read-only (the API refuses ward-round writes). */
  admitted: boolean;
  /** A doctor (or the director), the patient still admitted, not the support view. */
  canStart: boolean;
  readOnly: boolean;
}) {
  const [rounds, setRounds] = useState<WardRoundDto[] | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [nonce, setNonce] = useState(0);
  const [banner, setBanner] = useState<string | null>(null);
  const [unsaved, setUnsaved] = useState<string | null>(null);
  // Fix round 1 (I2, the Task 18 pattern): after a lock, the next fresh list remounts that round's editor (a new key), so its lock clears
  // and autosave resumes on a draft that is still the caller's. Final fix pass (Task 19 R1): the generation is per round, so one round's
  // lock never remounts the doctor's other open drafts.
  const [lockGens, setLockGens] = useState<Record<string, number>>({});
  const lockPending = useRef<Set<string>>(new Set());
  const [dischargedHere, setDischargedHere] = useState(false);
  const open = admitted && !dischargedHere;
  const start = useAction();

  useEffect(() => {
    let live = true;
    run((headers) => api<WardRoundDto[]>(`/api/care/admissions/${admissionId}/ward-rounds`, { headers })).then(
      (r) => {
        if (!live) return;
        setRounds(r);
        if (lockPending.current.size > 0) {
          const locked = [...lockPending.current];
          lockPending.current = new Set();
          setLockGens((g) => {
            const next = { ...g };
            for (const id of locked) next[id] = (next[id] ?? 0) + 1;
            return next;
          });
        }
        setError(null);
      },
      // A failed load keeps the pending rounds: their editors stay mounted (locked) and the retry's fresh list remounts them.
      (e) => live && setError(toApiError(e)),
    );
    return () => {
      live = false;
    };
  }, [admissionId, run, nonce]);

  const reload = useCallback(() => setNonce((n) => n + 1), []);
  const replace = useCallback((r: WardRoundDto) => setRounds((list) => (list ?? []).map((x) => (x.id === r.id ? { ...x, ...r } : x))), []);

  async function startRound() {
    const res = await start.run(() => api<WardRoundDto>(`/api/care/admissions/${admissionId}/ward-rounds`, { method: "POST", body: { note: "" } }));
    if (res.ok) setRounds((list) => [res.data, ...(list ?? [])]);
  }

  return (
    <div className="space-y-4">
      <SectionTitle
        action={
          canStart && open && (
            <Button loading={start.busy} onClick={startRound}>
              Start a ward round
            </Button>
          )
        }
      >
        Ward rounds
      </SectionTitle>
      <ApiErrorView error={start.error} />
      {banner && <Notice tone="warning">{banner}</Notice>}
      {unsaved && <UnsavedBox text={unsaved} />}
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!rounds && !error && <PageLoader label="Loading ward rounds…" />}
      {rounds && rounds.length === 0 && <p className="text-sm text-muted">No ward rounds yet.</p>}
      {rounds?.map((r) =>
        r.status === "Draft" && r.doctorUserId === meId && !readOnly && open ? (
          <RoundEditor
            key={`${r.id}-${lockGens[r.id] ?? 0}`}
            round={r}
            onSaved={replace}
            onDone={reload}
            onLocked={(message, lost, code) => {
              // Discharged meanwhile: every draft is read-only from now on (no remount into another refused save).
              if (code === "admission_discharged") setDischargedHere(true);
              setBanner(message);
              if (lost?.trim()) setUnsaved(lost);
              lockPending.current.add(r.id);
              reload();
            }}
          />
        ) : (
          <Card key={r.id} className={cx("space-y-2", r.status === "Draft" && "border-dashed")}>
            <p className="flex flex-wrap items-center gap-2 text-sm text-muted">
              {r.status === "Signed" ? <Badge tone="pass">Signed</Badge> : <Badge tone="iodine">Draft</Badge>}
              {r.doctorName}, started {formatStamp(r.createdAt)}
              {r.signedAt && `, signed ${formatStamp(r.signedAt)}`}
            </p>
            {r.note ? <p className="whitespace-pre-wrap text-sm text-ink">{r.note}</p> : <p className="text-sm text-muted">Nothing written yet.</p>}
            {r.status === "Draft" &&
              (open ? (
                <p className="text-xs text-muted">This draft is {r.doctorName}&apos;s. Only they can change, sign or discard it.</p>
              ) : (
                <p className="text-xs text-muted">The patient has been discharged: this draft stays unsigned and can no longer be changed.</p>
              ))}
            {r.status === "Signed" && (
              <Addenda
                kind="WardRound"
                noteId={r.id}
                addenda={r.addenda}
                run={run}
                readOnly={readOnly}
                onAdded={(a) => setRounds((list) => (list ?? []).map((x) => (x.id === r.id ? { ...x, addenda: [...x.addenda, a] } : x)))}
              />
            )}
          </Card>
        ),
      )}
    </div>
  );
}

function RoundEditor({
  round,
  onSaved,
  onDone,
  onLocked,
}: {
  round: WardRoundDto;
  onSaved: (r: WardRoundDto) => void;
  onDone: () => void;
  onLocked: (message: string, unsaved: string | null, code?: string) => void;
}) {
  const url = `/api/care/ward-rounds/${round.id}`;
  const draft = useAutosave<{ note: string }, WardRoundDto>(url, { note: round.note ?? "" }, round.updatedAt, onSaved, (message, lost, code) =>
    onLocked(message, lost?.note ?? null, code),
  );
  const [signingNow, setSigningNow] = useState(false);
  const sign = useAction();
  const discard = useAction();

  async function doSign() {
    if (!window.confirm("Sign this ward round? A signed note can't be changed: corrections are added as addenda.")) return;
    setSigningNow(true);
    try {
      if (!(await draft.flushForSigning())) return;
      const res = await sign.run(() => api<WardRoundDto>(`${url}/sign`, { method: "POST" }));
      if (res.ok) {
        draft.lock();
        onSaved(res.data);
      } else if (res.error.code === "note_signed") {
        draft.lock();
        onLocked(res.error.message, null);
      }
    } finally {
      draft.endSigning();
      setSigningNow(false);
    }
  }

  async function doDiscard() {
    if (!window.confirm("Discard this draft? It is removed for good.")) return;
    // No save may land after the discard: stop the queue and wait for one already sent.
    await draft.stopAndWait();
    const res = await discard.run(() => api(`${url}/discard`, { method: "POST" }));
    if (!res.ok) draft.unlock();
    if (res.ok) onDone();
  }

  return (
    <Card className="space-y-3 border-scrub-line">
      <SectionTitle
        action={
          <span role="status" className={cx("text-sm", draft.saveError ? "font-bold text-arterial" : "text-muted")}>
            {signingNow ? "Signing…" : draft.status}
          </span>
        }
      >
        Your ward round <span className="text-sm font-normal text-muted">started {formatStamp(round.createdAt)}</span>
      </SectionTitle>
      <Textarea
        label="Ward-round note"
        rows={6}
        maxLength={10000}
        readOnly={signingNow || draft.locked}
        value={draft.body.note}
        onChange={(e) => draft.edit({ note: e.target.value })}
        error={draft.saveError?.field("note")}
      />
      <ErrorBox error={draft.saveError && !draft.saveError.errors ? draft.saveError : null} onRetry={draft.saveNow} />
      <ErrorBox error={sign.error} />
      <ErrorBox error={discard.error} />
      <div className="flex flex-wrap items-center gap-2 border-t border-rule-soft pt-3">
        <Button loading={sign.busy || signingNow} onClick={doSign}>
          Sign the ward round
        </Button>
        <Button variant="ghost" loading={discard.busy} onClick={doDiscard}>
          Discard the draft
        </Button>
        <p className="text-xs text-muted">The draft saves itself as you type and when you leave.</p>
      </div>
    </Card>
  );
}
