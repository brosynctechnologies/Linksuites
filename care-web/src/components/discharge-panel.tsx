"use client";

// The discharge (Task 19): the discharge summary (an autosaving draft, signed, then printable; the medical director can take over or
// discard someone else's draft) and the discharge itself (home, against advice, died, transfer out). A refused discharge shows the API's
// sentence; unsigned ward-round drafts come back as a list (409 `unsigned_ward_rounds`) and can be acknowledged.

import Link from "next/link";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, downloadFile, toApiError, type ApiError } from "@/lib/api";
import { formatDay, formatStamp, lagosToday, money } from "@/lib/format";
import { DISCHARGE_TYPES, dischargeLabel, lagosLocalToIso, lagosNowLocal } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import type { DischargeType } from "@/lib/types/clinical";
import type { AdmissionDto, DischargedDto, DischargeRequest, DischargeSummaryDto, SaveDischargeSummaryRequest, UnsignedRoundDto } from "@/lib/types/inpatient";
import { useAutosave, UnsavedBox } from "./autosave";
import { Addenda, type ClinicalRun } from "./clinical-parts";
import { ApiErrorView, Badge, Button, Card, ErrorBox, Input, Notice, PageLoader, SectionTitle, Textarea, buttonClass, cx } from "./ui";

type SummaryState = { kind: "loading" } | { kind: "none" } | { kind: "ok"; summary: DischargeSummaryDto } | { kind: "error"; error: ApiError };

const toBody = (s: DischargeSummaryDto | null): SaveDischargeSummaryRequest => ({
  diagnoses: s?.diagnoses ?? "",
  treatment: s?.treatment ?? "",
  condition: s?.condition ?? "",
  takeHome: s?.takeHome ?? "",
  followUp: s?.followUp ?? null,
});

export function DischargePanel({
  admission,
  run,
  meId,
  isDirector,
  canDoctor,
  readOnly,
  showBill,
  onDischarged,
}: {
  admission: AdmissionDto;
  run: ClinicalRun;
  meId: string;
  isDirector: boolean;
  /** A doctor or the director. */
  canDoctor: boolean;
  readOnly: boolean;
  showBill: boolean;
  onDischarged: () => void;
}) {
  const [state, setState] = useState<SummaryState>({ kind: "loading" });
  const [nonce, setNonce] = useState(0);
  const [starting, setStarting] = useState(false);
  const [banner, setBanner] = useState<string | null>(null);
  const [unsaved, setUnsaved] = useState<string | null>(null);
  const [editorGen, setEditorGen] = useState(0);
  const [lockGen, setLockGen] = useState(0);
  const lockPending = useRef(false);
  const act = useAction();
  const pdf = useAction();
  const base = `/api/care/admissions/${admission.id}/discharge-summary`;

  useEffect(() => {
    let live = true;
    run((headers) => api<DischargeSummaryDto>(base, { headers })).then(
      (summary) => {
        if (!live) return;
        setState({ kind: "ok", summary });
        // Fix round 1 (I2, the Task 18 pattern): a fresh draft after a lock gets a fresh editor, so the lock clears and autosave resumes.
        if (lockPending.current) {
          lockPending.current = false;
          setLockGen((g) => g + 1);
        }
      },
      (e) => {
        if (!live) return;
        // Final fix pass (Task 19 R2): any settled load clears the flag. No editor is mounted on a 404 or an error, and the next editor
        // mounts fresh anyway, so a later successful load must not bump the generation again.
        lockPending.current = false;
        const error = toApiError(e);
        setState(error.status === 404 ? { kind: "none" } : { kind: "error", error });
      },
    );
    return () => {
      live = false;
    };
  }, [run, base, nonce]);
  const reload = useCallback(() => {
    setStarting(false);
    setNonce((n) => n + 1);
  }, []);
  const saved = useCallback((s: DischargeSummaryDto) => setState({ kind: "ok", summary: s }), []);

  async function takeOver() {
    if (!window.confirm("Take over this draft? It becomes yours to finish and sign; the text is kept.")) return;
    const res = await act.run(() => api<DischargeSummaryDto>(`${base}/take-over`, { method: "POST" }));
    if (res.ok) {
      setState({ kind: "ok", summary: res.data });
      setEditorGen((g) => g + 1);
    }
  }
  async function discard() {
    if (!window.confirm("Discard this draft discharge summary? It is removed for good.")) return;
    const res = await act.run(() => api(`${base}/discard`, { method: "POST" }));
    if (res.ok) setState({ kind: "none" });
  }

  const summary = state.kind === "ok" ? state.summary : null;
  const mineDraft = summary?.status === "Draft" && summary.doctorUserId === meId;
  const editing = canDoctor && !readOnly && (mineDraft || (state.kind === "none" && starting));

  return (
    <div className="space-y-5">
      <section className="space-y-3">
        <SectionTitle
          action={
            summary?.status === "Signed" && (
              <Button
                variant="secondary"
                loading={pdf.busy}
                onClick={() =>
                  pdf.run(() => run((headers) => downloadFile(`/api/care/admissions/${admission.id}/discharge.pdf`, `discharge-${admission.patientNumber}.pdf`, { headers })))
                }
              >
                Print (PDF)
              </Button>
            )
          }
        >
          Discharge summary
        </SectionTitle>
        <ErrorBox error={pdf.error} />
        {banner && <Notice tone="warning">{banner}</Notice>}
        {unsaved && <UnsavedBox text={unsaved} />}
        {state.kind === "loading" && <PageLoader label="Loading the summary…" />}
        {state.kind === "error" && <ApiErrorView error={state.error} onRetry={reload} />}
        {state.kind === "none" && !starting && (
          <div className="space-y-2">
            <p className="text-sm text-muted">No discharge summary yet.</p>
            {canDoctor && !readOnly && <Button onClick={() => setStarting(true)}>Write the discharge summary</Button>}
          </div>
        )}
        {editing ? (
          <SummaryEditor
            key={`summary-${editorGen}-${lockGen}`}
            url={base}
            summary={summary}
            onSaved={saved}
            onDone={reload}
            onLocked={(message, lost) => {
              setBanner(message);
              if (lost) setUnsaved(lost);
              lockPending.current = true;
              reload();
            }}
          />
        ) : (
          summary && (
            <Card className={cx("space-y-3", summary.status === "Draft" && "border-dashed")}>
              <p className="flex flex-wrap items-center gap-2 text-sm text-muted">
                {summary.status === "Signed" ? <Badge tone="pass">Signed</Badge> : <Badge tone="iodine">Draft</Badge>}
                {summary.doctorName}
                {summary.signedAt ? `, signed ${formatStamp(summary.signedAt)}` : `, started ${formatStamp(summary.createdAt)}`}
              </p>
              <SummaryFields s={summary} />
              {summary.status === "Draft" && (
                <>
                  <p className="text-xs text-muted">This draft is {summary.doctorName}&apos;s. Only they can change or sign it.</p>
                  {isDirector && !readOnly && (
                    <div className="flex flex-wrap gap-2">
                      <Button variant="secondary" loading={act.busy} onClick={takeOver}>
                        Take over the draft
                      </Button>
                      <Button variant="ghost" disabled={act.busy} onClick={discard}>
                        Discard the draft
                      </Button>
                    </div>
                  )}
                  <ErrorBox error={act.error} />
                </>
              )}
              {summary.status === "Signed" && (
                <Addenda
                  kind="Discharge"
                  noteId={summary.id}
                  addenda={summary.addenda}
                  run={run}
                  readOnly={readOnly}
                  onAdded={(a) => setState({ kind: "ok", summary: { ...summary, addenda: [...summary.addenda, a] } })}
                />
              )}
            </Card>
          )
        )}
      </section>

      {admission.status === "Admitted" ? (
        canDoctor && !readOnly ? (
          <DischargeForm admission={admission} summarySigned={summary?.status === "Signed"} showBill={showBill} onDischarged={onDischarged} />
        ) : (
          <p className="text-sm text-muted">A doctor on the care team discharges the patient.</p>
        )
      ) : (
        <Card className="space-y-1 text-sm">
          <p className="font-bold text-ink">
            Discharged {admission.dischargedAt && formatStamp(admission.dischargedAt)}: {dischargeLabel(admission.dischargeType).toLowerCase()}
          </p>
          {admission.amaReason && <p className="text-slate-700">Reason given: {admission.amaReason}</p>}
          {admission.deathTime && <p className="text-slate-700">Time of death: {formatStamp(admission.deathTime)}</p>}
          {admission.deathCause && <p className="text-slate-700">Cause: {admission.deathCause}</p>}
        </Card>
      )}
    </div>
  );
}

function SummaryFields({ s }: { s: DischargeSummaryDto }) {
  const rows: [string, string | null][] = [
    ["Diagnoses", s.diagnoses],
    ["Treatment", s.treatment],
    ["Condition at discharge", s.condition],
    ["Take-home medicines and advice", s.takeHome],
    ["Follow-up", s.followUp ? formatDay(s.followUp) : null],
  ];
  return (
    <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[12rem_minmax(0,1fr)]">
      {rows.map(([label, value]) => (
        <div key={label} className="contents">
          <dt className="text-muted">{label}</dt>
          <dd className="whitespace-pre-wrap text-ink">{value || "—"}</dd>
        </div>
      ))}
    </dl>
  );
}

function summaryText(b: SaveDischargeSummaryRequest): string {
  return (
    [
      ["Diagnoses", b.diagnoses],
      ["Treatment", b.treatment],
      ["Condition", b.condition],
      ["Take home", b.takeHome],
    ] as [string, string | null][]
  )
    .filter(([, v]) => v && v.trim())
    .map(([l, v]) => `${l}:\n${v!.trim()}`)
    .join("\n\n");
}

function SummaryEditor({
  url,
  summary,
  onSaved,
  onDone,
  onLocked,
}: {
  url: string;
  summary: DischargeSummaryDto | null;
  onSaved: (s: DischargeSummaryDto) => void;
  onDone: () => void;
  onLocked: (message: string, unsaved: string | null) => void;
}) {
  // Final fix pass (Task 19 carry): saves name the draft they edit, so one landing after the director discarded it is refused (409
  // note_discarded) instead of bringing it back. The first save names none and starts the draft.
  const saveUrl = summary ? `${url}?draftId=${encodeURIComponent(summary.id)}` : url;
  const draft = useAutosave<SaveDischargeSummaryRequest, DischargeSummaryDto>(saveUrl, toBody(summary), summary?.updatedAt ?? summary?.createdAt ?? null, onSaved, (message, lost) =>
    onLocked(message, lost ? summaryText(lost) : null),
  );
  const [signingNow, setSigningNow] = useState(false);
  const sign = useAction();
  const discard = useAction();
  const b = draft.body;
  const set = (patch: Partial<SaveDischargeSummaryRequest>) => draft.edit({ ...b, ...patch });
  const err = (f: string) => draft.saveError?.field(f);

  async function doSign() {
    if (!window.confirm("Sign the discharge summary? It can't be changed afterwards: corrections are added as addenda. The patient's portal shows it.")) return;
    setSigningNow(true);
    try {
      if (!(await draft.flushForSigning())) return;
      const res = await sign.run(() => api<DischargeSummaryDto>(`${url}/sign`, { method: "POST" }));
      if (res.ok) {
        draft.lock();
        onSaved(res.data);
        onDone();
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
    if (!window.confirm("Discard this draft discharge summary? It is removed for good.")) return;
    // A summary save is an upsert: a save landing after the discard would bring the draft back. Stop the queue and wait for one in flight.
    await draft.stopAndWait();
    const res = await discard.run(() => api(`${url}/discard`, { method: "POST" }));
    if (res.ok) onDone();
    else draft.unlock();
  }

  return (
    <Card className="space-y-3 border-scrub-line">
      <p role="status" className={cx("text-right text-sm", draft.saveError ? "font-bold text-arterial" : "text-muted")}>
        {signingNow ? "Signing…" : draft.status}
      </p>
      <Textarea label="Diagnoses" rows={2} maxLength={2000} readOnly={signingNow || draft.locked} value={b.diagnoses ?? ""} onChange={(e) => set({ diagnoses: e.target.value })} error={err("diagnoses")} />
      <Textarea label="Treatment" rows={4} maxLength={10000} readOnly={signingNow || draft.locked} value={b.treatment ?? ""} onChange={(e) => set({ treatment: e.target.value })} error={err("treatment")} />
      <Textarea
        label="Condition at discharge"
        rows={2}
        maxLength={1000}
        readOnly={signingNow || draft.locked}
        value={b.condition ?? ""}
        onChange={(e) => set({ condition: e.target.value })}
        error={err("condition")}
      />
      <Textarea
        label="Take-home medicines and advice"
        rows={3}
        maxLength={4000}
        readOnly={signingNow || draft.locked}
        value={b.takeHome ?? ""}
        onChange={(e) => set({ takeHome: e.target.value })}
        error={err("takeHome")}
      />
      <Input
        label="Follow-up date"
        type="date"
        min={lagosToday()}
        readOnly={signingNow || draft.locked}
        value={b.followUp ?? ""}
        onChange={(e) => set({ followUp: e.target.value || null })}
        error={err("followUp")}
      />
      <ErrorBox error={draft.saveError && !draft.saveError.errors ? draft.saveError : null} onRetry={draft.saveNow} />
      <ErrorBox error={sign.error} />
      <ErrorBox error={discard.error} />
      <div className="flex flex-wrap items-center gap-2 border-t border-rule-soft pt-3">
        <Button loading={sign.busy || signingNow} onClick={doSign}>
          Sign the summary
        </Button>
        {summary && (
          <Button variant="ghost" loading={discard.busy} onClick={doDiscard}>
            Discard the draft
          </Button>
        )}
        <p className="text-xs text-muted">Saves itself as you type. Diagnoses and the condition at discharge are needed to sign.</p>
      </div>
    </Card>
  );
}

function DischargeForm({
  admission,
  summarySigned,
  showBill,
  onDischarged,
}: {
  admission: AdmissionDto;
  summarySigned: boolean;
  showBill: boolean;
  onDischarged: () => void;
}) {
  const [type, setType] = useState<DischargeType>("Home");
  const [amaReason, setAmaReason] = useState("");
  const [deathTime, setDeathTime] = useState(lagosNowLocal());
  const [deathCause, setDeathCause] = useState("");
  const [drafts, setDrafts] = useState<UnsignedRoundDto[] | null>(null);
  const [done, setDone] = useState<DischargedDto | null>(null);
  const { busy, error, setError, run } = useAction();

  async function send(acknowledge: boolean) {
    const body: DischargeRequest = {
      type,
      amaReason: type === "AgainstAdvice" ? amaReason.trim() : null,
      deathTime: type === "Died" ? lagosLocalToIso(deathTime) : null,
      deathCause: type === "Died" ? deathCause.trim() : null,
      acknowledgeUnsignedNotes: acknowledge,
    };
    const res = await run(() => api<DischargedDto>(`/api/care/admissions/${admission.id}/discharge`, { method: "POST", body }));
    if (res.ok) {
      setDrafts(null);
      setDone(res.data);
      onDischarged();
    } else if (res.error.code === "unsigned_ward_rounds") {
      const list = res.error.problem.unsignedWardRounds;
      setDrafts(Array.isArray(list) ? (list as UnsignedRoundDto[]) : []);
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const label = DISCHARGE_TYPES.find((t) => t.value === type)?.label ?? type;
    if (!window.confirm(`Discharge ${admission.patientName} (${label.toLowerCase()})? The bed goes to cleaning and the bill becomes final.`)) return;
    setDrafts(null);
    await send(false);
  }

  if (done)
    return (
      <Notice tone="success">
        <p className="font-bold">
          {done.admission.patientName} was discharged ({dischargeLabel(done.type).toLowerCase()}) at {formatStamp(done.dischargedAt)}.
        </p>
        <p className="tnum">
          Bill balance left: {money(done.balanceMinor)}.
          {done.stoppedDoses > 0 && ` ${done.stoppedDoses} future doses stopped.`}
          {done.missedDoses > 0 && ` ${done.missedDoses} unrecorded doses marked missed.`}
        </p>
        {showBill && (
          <Link href={`/app/bills/${done.billId}`} className="font-semibold text-scrub underline-offset-2 hover:underline">
            Open the bill
          </Link>
        )}
      </Notice>
    );

  const needsSummary = (type === "Home" || type === "TransferredOut") && !summarySigned;
  return (
    <section aria-labelledby="discharge-title" className="space-y-3">
      <SectionTitle id="discharge-title">Discharge the patient</SectionTitle>
      <form onSubmit={submit} className="space-y-4 rounded-panel border border-rule bg-surface p-4">
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium text-slate-700">How the patient is leaving</legend>
          {DISCHARGE_TYPES.map((t) => (
            <label key={t.value} className="flex items-start gap-2.5 rounded-control px-1 py-1 text-sm">
              <input type="radio" name="discharge-type" className="mt-1 accent-[#0f766e]" checked={type === t.value} onChange={() => setType(t.value)} />
              <span>
                <span className="font-bold text-ink">{t.label}</span>
                <span className="block text-xs text-muted">{t.text}</span>
              </span>
            </label>
          ))}
        </fieldset>
        {type === "AgainstAdvice" && (
          <Textarea label="Why the patient is leaving against advice" rows={2} maxLength={1000} value={amaReason} onChange={(e) => setAmaReason(e.target.value)} error={error?.field("amaReason")} />
        )}
        {type === "Died" && (
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Time of death (Lagos time)"
              type="datetime-local"
              value={deathTime}
              max={lagosNowLocal()}
              onChange={(e) => setDeathTime(e.target.value)}
              error={error?.field("deathTime")}
            />
            <Input label="Cause of death" maxLength={500} value={deathCause} onChange={(e) => setDeathCause(e.target.value)} error={error?.field("deathCause")} />
          </div>
        )}
        {needsSummary && <Notice tone="warning">Sign the discharge summary first: a discharge home or a transfer out needs it.</Notice>}
        {drafts && (
          <div role="alert" className="space-y-2 rounded-control border-l-[3px] border-iodine bg-iodine-wash px-3 py-2.5 text-sm">
            <p className="font-bold text-ink">{error?.message}</p>
            <ul className="list-disc pl-5">
              {drafts.map((d) => (
                <li key={d.id}>
                  Ward round by {d.doctorName}, started {formatStamp(d.createdAt)}
                </li>
              ))}
            </ul>
            <p className="text-slate-700">Their authors can sign or discard them on the ward rounds tab. You can also discharge now: the drafts stay unsigned and this is recorded.</p>
            <div className="flex flex-wrap gap-2">
              <Button variant="danger" loading={busy} onClick={() => send(true)}>
                Discharge anyway
              </Button>
              <Button
                variant="ghost"
                onClick={() => {
                  setDrafts(null);
                  setError(null);
                }}
              >
                Cancel
              </Button>
            </div>
          </div>
        )}
        <ErrorBox error={error && !drafts && !["amaReason", "deathTime", "deathCause", "type"].some((f) => error.field(f)) ? error : null} />
        {!drafts && (
          <Button type="submit" variant={type === "Died" ? "danger" : "primary"} loading={busy}>
            Discharge
          </Button>
        )}
        <p className="text-xs text-muted">
          Discharging ends the ward nurses&apos; access, stops future doses, sends the bed to cleaning, charges the remaining bed days and makes the bill final. Any balance
          stays as a debt.{" "}
          {showBill && admission.billId && (
            <Link href={`/app/bills/${admission.billId}`} className={buttonClass("ghost", "inline-flex min-h-0 px-1 py-0")}>
              See the bill
            </Link>
          )}
        </p>
      </form>
    </section>
  );
}
