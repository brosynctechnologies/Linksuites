"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { canWriteNotes } from "@/lib/clinical";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import { useDebounced } from "@/lib/use-debounced";
import type { ConsultationDto, DiagnosisCode, DiagnosisInput, PatientRecordDto, PrescriptionDto, SaveConsultationRequest, TestRequestDto } from "@/lib/types/clinical";
import { Addenda, PatientBanner, PrescriptionCard, PrescriptionForm, TestRequestForm, TestRow, withResult, type ClinicalRun } from "@/components/clinical-parts";
import { EmergencyReason, useClinicalRead } from "@/components/emergency-reason";
import { lagosTime } from "@/components/check-in";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  NoAccess,
  Notice,
  PageHeader,
  PageLoader,
  SearchField,
  SectionTitle,
  Textarea,
  buttonClass,
  cx,
  inputClass,
} from "@/components/ui";

const AUTOSAVE_MS = 1500;
/** Stay under the browsers' 64 KB keepalive cap. */
const KEEPALIVE_MAX = 60_000;

type RecordState = { patientId: string; data?: PatientRecordDto; error?: ApiError };

/**
 * The consultation. Opening it (`POST /api/care/visits/{id}/consultation`) is the visit doctor's (or the director's) logged open of the
 * record; without a care relationship the API asks for a reason. The page then reads the patient record once for the summary banner and
 * this consultation's prescriptions and tests. The draft autosaves to the API (never to the browser); signing locks it, and corrections
 * are addenda.
 */
export default function ConsultationPage() {
  const { id } = useParams<{ id: string }>();
  const { me, access } = useCare();
  const allowed = canWriteNotes(access);
  const open = useClinicalRead<ConsultationDto>(allowed ? `/api/care/visits/${encodeURIComponent(id)}/consultation` : null, "POST");
  const [record, setRecord] = useState<RecordState | null>(null);
  const [banner, setBanner] = useState<string | null>(null);
  /** Text that didn't reach the API when the note locked, kept on screen (read-only) so it isn't lost. */
  const [unsaved, setUnsaved] = useState<string | null>(null);
  /** The note as it was when the editor locked, and a counter that remounts the editor when a fresh draft replaces it. */
  const [lockedOn, setLockedOn] = useState<ConsultationDto | null>(null);
  const [editorGen, setEditorGen] = useState(0);
  const lockedNotice = (
    <>
      {banner && <Notice tone="warning">{banner}</Notice>}
      {unsaved && <UnsavedBox text={unsaved} />}
    </>
  );
  const { run, gate } = open;
  const patientId = gate.kind === "ok" ? gate.data.patientId : null;

  useEffect(() => {
    if (!patientId) return;
    let live = true;
    run((headers) => api<PatientRecordDto>(`/api/care/patients/${patientId}/record`, { headers })).then(
      (data) => live && setRecord({ patientId, data }),
      (e) => live && setRecord({ patientId, error: toApiError(e) }),
    );
    return () => {
      live = false;
    };
  }, [patientId, run]);

  if (!allowed) return <NoAccess text={access.isClinical ? "Only doctors can write consultation notes." : "Only doctors and nurses can see clinical notes."} />;
  if (gate.kind === "loading") return <PageLoader label="Opening the consultation…" />;
  if (gate.kind === "reason") return <EmergencyReason onSubmit={open.submitReason} back="/app/queue" />;
  if (gate.kind === "error")
    return (
      <div className="space-y-4">
        <PageHeader title="Consultation" back="/app/queue" />
        {lockedNotice}
        <ApiErrorView error={gate.error} onRetry={open.reload} />
      </div>
    );

  const c = gate.data;
  // A fresh draft from the reopen (not the one the lock happened on) gets a fresh editor, so autosave resumes.
  if (lockedOn && c !== lockedOn) {
    setLockedOn(null);
    setEditorGen((g) => g + 1);
  }
  const rec = record?.patientId === c.patientId ? record : null;
  const isAuthor = c.doctorUserId === me.userId;
  const editable = c.status === "Draft" && isAuthor && !access.readOnly;
  const latest = rec?.data?.vitals.length ? rec.data.vitals[rec.data.vitals.length - 1] : null;
  const medicines = rec?.data
    ? Array.from(new Set(rec.data.prescriptions.slice(0, 5).flatMap((p) => p.items.map((i) => [i.drug, i.strength].filter(Boolean).join(" ")))))
    : undefined;

  return (
    <div className="space-y-5">
      {open.dialog}
      <PageHeader
        back="/app/queue"
        title="Consultation"
        subtitle={
          <span className="flex flex-wrap items-center gap-2">
            {c.status === "Signed" ? <Badge tone="pass">Signed</Badge> : <Badge tone="iodine">Draft</Badge>}
            <span>
              {c.doctorName}, started {formatStamp(c.createdAt)}
              {c.signedAt && `, signed ${formatStamp(c.signedAt)}`}
            </span>
          </span>
        }
        action={
          <Link href={`/app/patients/${c.patientId}/record`} className={buttonClass("secondary")}>
            Whole record
          </Link>
        }
      />

      {rec?.data ? (
        <PatientBanner patient={rec.data.patient} latestVitals={latest} medicines={medicines} />
      ) : rec?.error ? (
        <ErrorBox error={rec.error} />
      ) : (
        <PageLoader label="Loading the patient summary…" />
      )}

      {lockedNotice}

      {editable ? (
        <DraftEditor
          key={`note-${c.id}-${editorGen}`}
          consultation={c}
          onChange={(next) => open.setData(() => next)}
          onLocked={(message, lost) => {
            setBanner(message);
            setLockedOn(c);
            const text = lost ? unsavedText(lost) : "";
            if (text) setUnsaved(text);
            open.reload();
          }}
        />
      ) : (
        <SignedNote consultation={c} run={run} readOnly={access.readOnly || c.status === "Draft"} onAddendum={(a) => open.setData((x) => ({ ...x, addenda: [...x.addenda, a] }))}>
          {c.status === "Draft" && <Notice>This draft is {c.doctorName}&apos;s. Only they can change or sign it.</Notice>}
        </SignedNote>
      )}

      {rec?.data && (
        <Orders
          key={`orders-${c.id}`}
          consultation={c}
          record={rec.data}
          run={run}
          canOrder={isAuthor && !access.readOnly}
          canRecord={!access.readOnly}
          myName={me.fullName}
        />
      )}
    </div>
  );
}

// ---------- The draft ----------

interface DiagRow extends DiagnosisInput {
  key: number;
}
type Fields = { complaint: string; history: string; examination: string; plan: string };

function fieldsOf(c: ConsultationDto): Fields {
  return { complaint: c.complaint ?? "", history: c.history ?? "", examination: c.examination ?? "", plan: c.plan ?? "" };
}

function DraftEditor({
  consultation,
  onChange,
  onLocked,
}: {
  consultation: ConsultationDto;
  onChange: (c: ConsultationDto) => void;
  onLocked: (message: string, unsaved: SaveConsultationRequest | null) => void;
}) {
  const [fields, setFields] = useState<Fields>(() => fieldsOf(consultation));
  const [diagnoses, setDiagnoses] = useState<DiagRow[]>(() => consultation.diagnoses.map((d, i) => ({ key: i, text: d.text, code: d.code, primary: d.primary })));
  const [nextKey, setNextKey] = useState(consultation.diagnoses.length);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<ApiError | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(consultation.updatedAt);
  /** While signing, the fields are read-only, so nothing typed can miss the signature. */
  const [signingNow, setSigningNow] = useState(false);
  /** Bumped to restart the autosave timer (after a sign that didn't go through). */
  const [retry, setRetry] = useState(0);
  const edits = useRef(0);
  const chain = useRef<Promise<boolean>>(Promise.resolve(true));
  const signing = useRef(false);
  /** Set once the note is signed or the API refuses the draft (409 signed, 403): no further save is attempted, on leave either. */
  const locked = useRef(false);
  /** Final fix pass (Task 18 carry R1): the lock as state, so the fields go read-only the moment it is set (text typed after it is never
   *  silently dropped on the remount that follows). */
  const [isLocked, setIsLocked] = useState(false);
  const sign = useAction();
  // The parent's callbacks change on every render; keep the latest without restarting the autosave timer.
  const callbacks = useRef({ onChange, onLocked });
  useEffect(() => {
    callbacks.current = { onChange, onLocked };
  }, [onChange, onLocked]);

  const body = useCallback(
    (): SaveConsultationRequest => ({
      complaint: fields.complaint,
      history: fields.history,
      examination: fields.examination,
      plan: fields.plan,
      diagnoses: diagnoses.map(({ text, code, primary }) => ({ text: text?.trim() || null, code, primary })),
    }),
    [fields, diagnoses],
  );
  // The latest draft and whether it is unsaved, for the save on leaving (unmount and pagehide).
  const latest = useRef<{ body: SaveConsultationRequest; dirty: boolean }>({ body: body(), dirty });
  useEffect(() => {
    latest.current = { body: body(), dirty };
  }, [body, dirty]);

  const url = `/api/care/consultations/${consultation.id}`;

  /** Saves the whole draft (a full replace). Saves run one after another, so the last edit always lands last. */
  const persist = useCallback(
    (request: SaveConsultationRequest): Promise<boolean> => {
      const at = edits.current;
      chain.current = chain.current.then(async () => {
        if (locked.current) return false;
        setSaving(true);
        try {
          const saved = await api<ConsultationDto>(url, { method: "PUT", body: request });
          setSaveError(null);
          setSavedAt(saved.updatedAt);
          if (edits.current === at) setDirty(false);
          callbacks.current.onChange(saved);
          return true;
        } catch (e) {
          const error = toApiError(e);
          // Signed meanwhile (another tab) or no longer the author: stop editing, show the API's sentence and keep what wasn't saved.
          if (error.code === "note_signed" || error.status === 403) {
            locked.current = true;
            setIsLocked(true);
            setIsLocked(true);
            callbacks.current.onLocked(error.message, request);
            return false;
          }
          setSaveError(error);
          return false;
        } finally {
          setSaving(false);
        }
      });
      return chain.current;
    },
    [url],
  );

  useEffect(() => {
    if (!dirty) return;
    const t = setTimeout(() => {
      if (!signing.current && !locked.current) void persist(body());
    }, AUTOSAVE_MS);
    return () => clearTimeout(t);
  }, [dirty, body, persist, retry]);

  // Leaving in-app (a link, the back arrow, the nav): save what is left, including edits after a failed save. The chain outlives the editor.
  useEffect(() => {
    const draft = latest;
    const lock = locked;
    return () => {
      if (draft.current.dirty && !lock.current) void persist(draft.current.body);
    };
  }, [persist]);

  // Closing the tab or the browser: one last save that outlives the page, and the browser's own "leave?" prompt meanwhile.
  useEffect(() => {
    const onHide = () => {
      if (!latest.current.dirty || locked.current) return;
      // Browsers cap keepalive bodies at 64 KB; a bigger draft relies on the beforeunload warning instead.
      if (new Blob([JSON.stringify(latest.current.body)]).size > KEEPALIVE_MAX) return;
      void api(url, { method: "PUT", body: latest.current.body, keepalive: true }).catch(() => undefined);
    };
    window.addEventListener("pagehide", onHide);
    return () => window.removeEventListener("pagehide", onHide);
  }, [url]);

  useEffect(() => {
    if (!dirty) return;
    const warn = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  function edit(next: Partial<Fields>) {
    edits.current++;
    setFields((f) => ({ ...f, ...next }));
    setDirty(true);
  }
  function editDiagnoses(update: (rows: DiagRow[]) => DiagRow[]) {
    edits.current++;
    setDiagnoses(update);
    setDirty(true);
  }
  function addDiagnosis(d: DiagnosisInput) {
    editDiagnoses((rows) => [...rows, { ...d, key: nextKey, primary: rows.length === 0 }]);
    setNextKey((k) => k + 1);
  }

  async function doSign() {
    if (!window.confirm("Sign this note? A signed note can't be changed: corrections are added as addenda.")) return;
    // The latest text lands before the signature (the API signs what is stored). The fields are read-only and no autosave starts
    // meanwhile; anything that still slipped in during the save is saved again before signing.
    signing.current = true;
    setSigningNow(true);
    try {
      let at = edits.current;
      if (!(dirty ? await persist(body()) : await chain.current)) return;
      for (let i = 0; i < 3 && edits.current !== at; i++) {
        at = edits.current;
        if (!(await persist(latest.current.body))) return;
      }
      if (edits.current !== at) return;
      const res = await sign.run(() => api<ConsultationDto>(`${url}/sign`, { method: "POST" }));
      if (res.ok) {
        locked.current = true;
        setIsLocked(true);
        setIsLocked(true);
        onChange(res.data);
      } else if (res.error.code === "note_signed") {
        locked.current = true;
        setIsLocked(true);
        setIsLocked(true);
        onLocked(res.error.message, null);
      }
    } finally {
      signing.current = false;
      setSigningNow(false);
      // Edits still unsaved (a refused sign, a failed save) get the autosave timer again.
      setRetry((n) => n + 1);
    }
  }

  const err = (f: string) => saveError?.field(f);
  const status = saving ? "Saving…" : saveError ? "Not saved" : dirty ? "Unsaved changes" : savedAt ? `Saved at ${lagosTime(savedAt)}` : "Not saved yet";

  return (
    <Card className="space-y-4">
      <SectionTitle
        action={
          <span role="status" className={cx("text-sm", saveError ? "font-bold text-arterial" : "text-muted")}>
            {signingNow ? "Signing…" : status}
          </span>
        }
      >
        Note
      </SectionTitle>
      <Textarea label="Complaint" rows={2} maxLength={10000} readOnly={signingNow || isLocked} value={fields.complaint} onChange={(e) => edit({ complaint: e.target.value })} error={err("complaint")} />
      <Textarea label="History" rows={4} maxLength={10000} readOnly={signingNow || isLocked} value={fields.history} onChange={(e) => edit({ history: e.target.value })} error={err("history")} />
      <Textarea label="Examination" rows={4} maxLength={10000} readOnly={signingNow || isLocked} value={fields.examination} onChange={(e) => edit({ examination: e.target.value })} error={err("examination")} />

      <DiagnosesEditor
        rows={diagnoses}
        error={saveError}
        disabled={signingNow || isLocked}
        onAdd={addDiagnosis}
        onChange={(key, patch) => editDiagnoses((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)))}
        onPrimary={(key) => editDiagnoses((rows) => rows.map((r) => ({ ...r, primary: r.key === key })))}
        onRemove={(key) =>
          editDiagnoses((rows) => {
            const left = rows.filter((r) => r.key !== key);
            return left.length > 0 && !left.some((r) => r.primary) ? left.map((r, i) => ({ ...r, primary: i === 0 })) : left;
          })
        }
      />

      <Textarea label="Plan" rows={4} maxLength={10000} readOnly={signingNow || isLocked} value={fields.plan} onChange={(e) => edit({ plan: e.target.value })} error={err("plan")} />

      <ErrorBox error={saveError && !saveError.errors ? saveError : null} onRetry={() => void persist(body())} />
      <ErrorBox error={sign.error} />
      <div className="flex flex-wrap items-center gap-3 border-t border-rule-soft pt-4">
        <Button loading={sign.busy || signingNow} disabled={isLocked} onClick={doSign}>
          Sign the note
        </Button>
        <p className="text-xs text-muted">The draft saves itself as you type and when you leave. Signing makes it final and visible on the patient&apos;s portal.</p>
      </div>
    </Card>
  );
}

/** What didn't reach the API when the note locked, as plain text to copy into an addendum or pass to the note's doctor. */
function unsavedText(b: SaveConsultationRequest): string {
  const parts: [string, string | null][] = [
    ["Complaint", b.complaint],
    ["History", b.history],
    ["Examination", b.examination],
    ["Plan", b.plan],
  ];
  const diagnoses = (b.diagnoses ?? []).map((d) => [d.text, d.code && `(${d.code})`].filter(Boolean).join(" ")).filter(Boolean);
  return [...parts.filter(([, v]) => v && v.trim()).map(([l, v]) => `${l}:\n${v!.trim()}`), diagnoses.length ? `Diagnoses:\n${diagnoses.join("\n")}` : ""]
    .filter(Boolean)
    .join("\n\n");
}

function UnsavedBox({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Card className="space-y-2 border-l-[3px] border-l-iodine">
      <h2 className="font-semibold text-ink">Your last version of the draft wasn&apos;t saved</h2>
      <p className="text-sm text-slate-700">
        The note locked before it reached the record. It is shown here only, and goes when you leave this page. Copy what you need into an addendum, or pass it to the
        note&apos;s doctor.
      </p>
      <Textarea label="Unsaved text" srLabel readOnly rows={8} value={text} onFocus={(e) => e.currentTarget.select()} />
      <Button
        variant="secondary"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(text);
            setCopied(true);
          } catch {
            setCopied(false);
          }
        }}
      >
        {copied ? "Copied" : "Copy the text"}
      </Button>
    </Card>
  );
}


function DiagnosesEditor({
  rows,
  error,
  disabled,
  onAdd,
  onChange,
  onPrimary,
  onRemove,
}: {
  rows: DiagRow[];
  error: ApiError | null;
  disabled?: boolean;
  onAdd: (d: DiagnosisInput) => void;
  onChange: (key: number, patch: Partial<DiagnosisInput>) => void;
  onPrimary: (key: number) => void;
  onRemove: (key: number) => void;
}) {
  const [q, setQ] = useState("");
  const term = useDebounced(q.trim(), 250);
  const codes = useApi<DiagnosisCode[]>(term.length >= 2 ? `/api/care/diagnosis-codes${qs({ q: term })}` : null);
  const matches = term.length >= 2 ? (codes.data ?? []).slice(0, 8) : [];

  return (
    <fieldset className="space-y-2" disabled={disabled}>
      <legend className="text-sm font-medium text-slate-700">Diagnoses</legend>
      {rows.length > 0 && (
        <ul className="space-y-2">
          {rows.map((r, i) => (
            <li key={r.key} className="flex flex-wrap items-center gap-2 rounded-control border border-rule p-2">
              <label className="flex min-h-10 items-center gap-1.5 text-sm">
                <input type="radio" name="primary-diagnosis" className="h-4 w-4 accent-[#0f766e]" checked={r.primary} onChange={() => onPrimary(r.key)} />
                Primary
              </label>
              <label htmlFor={`diag-${r.key}`} className="sr-only">
                Diagnosis {i + 1}
              </label>
              <input
                id={`diag-${r.key}`}
                className={cx(inputClass, "min-h-10 flex-1 py-1.5")}
                maxLength={200}
                value={r.text ?? ""}
                onChange={(e) => onChange(r.key, { text: e.target.value })}
                aria-invalid={!!error?.field(`diagnoses[${i}].text`) || undefined}
              />
              {r.code && <Badge tone="scrub">{r.code}</Badge>}
              <Button variant="ghost" className="min-h-10 px-2.5" onClick={() => onRemove(r.key)}>
                Remove
              </Button>
              {(error?.field(`diagnoses[${i}].text`) || error?.field(`diagnoses[${i}].code`)) && (
                <p className="w-full text-xs font-medium text-arterial">{error?.field(`diagnoses[${i}].text`) ?? error?.field(`diagnoses[${i}].code`)}</p>
              )}
            </li>
          ))}
        </ul>
      )}
      {error?.field("diagnoses") && <p className="text-xs font-medium text-arterial">{error.field("diagnoses")}</p>}
      <SearchField id="diagnosis-search" label="Search the diagnosis list" placeholder="Search by name or code, such as malaria" value={q} onChange={setQ} />
      {codes.error && <ErrorBox error={codes.error} />}
      {(matches.length > 0 || term.length >= 2) && (
        <ul className="divide-y divide-rule-soft rounded-control border border-rule bg-surface" aria-label="Diagnosis matches">
          {matches.map((m) => (
            <li key={m.code}>
              <button
                type="button"
                className="flex min-h-11 w-full items-center gap-2 px-3 text-left text-sm hover:bg-scrub-wash/50"
                onClick={() => {
                  onAdd({ text: m.name, code: m.code, primary: false });
                  setQ("");
                }}
              >
                <Badge tone="scrub">{m.code}</Badge> {m.name}
              </button>
            </li>
          ))}
          <li>
            <button
              type="button"
              className="flex min-h-11 w-full items-center px-3 text-left text-sm font-semibold text-scrub hover:bg-scrub-wash/50"
              onClick={() => {
                onAdd({ text: q.trim(), code: null, primary: false });
                setQ("");
              }}
            >
              Add &ldquo;{q.trim()}&rdquo; as written, without a code
            </button>
          </li>
        </ul>
      )}
    </fieldset>
  );
}

// ---------- A signed note (or someone else's draft) ----------

function SignedNote({
  consultation: c,
  run,
  readOnly,
  onAddendum,
  children,
}: {
  consultation: ConsultationDto;
  run: ClinicalRun;
  readOnly: boolean;
  onAddendum: Parameters<typeof Addenda>[0]["onAdded"];
  children?: ReactNode;
}) {
  const parts: [string, string | null][] = [
    ["Complaint", c.complaint],
    ["History", c.history],
    ["Examination", c.examination],
    ["Plan", c.plan],
  ];
  return (
    <Card className="space-y-4">
      <SectionTitle>Note</SectionTitle>
      {children}
      <dl className="space-y-3 text-sm">
        {parts.map(([label, value]) => (
          <div key={label}>
            <dt className="font-semibold text-muted">{label}</dt>
            <dd className="mt-0.5 whitespace-pre-wrap text-ink">{value || "—"}</dd>
          </div>
        ))}
        <div>
          <dt className="font-semibold text-muted">Diagnoses</dt>
          <dd className="mt-1">
            {c.diagnoses.length === 0 ? (
              "—"
            ) : (
              <ul className="space-y-1">
                {c.diagnoses.map((d) => (
                  <li key={d.id} className="flex flex-wrap items-center gap-2 text-ink">
                    {d.text} {d.code && <Badge tone="scrub">{d.code}</Badge>} {d.primary && <Badge>Primary</Badge>}
                  </li>
                ))}
              </ul>
            )}
          </dd>
        </div>
      </dl>
      {c.status === "Signed" && (
        <div className="border-t border-rule-soft pt-4">
          <h3 className="mb-2 text-sm font-semibold text-ink">Addenda</h3>
          <Addenda kind="Consultation" noteId={c.id} addenda={c.addenda} run={run} readOnly={readOnly} onAdded={onAddendum} />
        </div>
      )}
    </Card>
  );
}

// ---------- Prescriptions and tests from this consultation ----------

function Orders({
  consultation: c,
  record,
  run,
  canOrder,
  canRecord,
  myName,
}: {
  consultation: ConsultationDto;
  record: PatientRecordDto;
  run: ClinicalRun;
  canOrder: boolean;
  canRecord: boolean;
  myName: string;
}) {
  const [prescriptions, setPrescriptions] = useState<PrescriptionDto[]>(() => record.prescriptions.filter((p) => p.consultationId === c.id));
  const [tests, setTests] = useState<TestRequestDto[]>(() => record.results.filter((t) => t.consultationId === c.id));
  const [writing, setWriting] = useState(false);
  const [requesting, setRequesting] = useState(false);

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <SectionTitle
          action={
            canOrder &&
            !writing && (
              <Button variant="secondary" className="min-h-10" onClick={() => setWriting(true)}>
                Prescribe
              </Button>
            )
          }
        >
          Prescriptions
        </SectionTitle>
        {writing && (
          <PrescriptionForm
            patientId={c.patientId}
            consultationId={c.id}
            onCancel={() => setWriting(false)}
            onCreated={(p) => {
              setPrescriptions((list) => [p, ...list]);
              setWriting(false);
            }}
          />
        )}
        {prescriptions.length === 0 && !writing && <p className="text-sm text-muted">Nothing prescribed in this consultation.</p>}
        {prescriptions.map((p) => (
          <PrescriptionCard key={p.id} prescription={p} patientNumber={record.patient.number} run={run} />
        ))}
      </Card>
      <Card className="space-y-3">
        <SectionTitle
          action={
            canOrder &&
            !requesting && (
              <Button variant="secondary" className="min-h-10" onClick={() => setRequesting(true)}>
                Request a test
              </Button>
            )
          }
        >
          Tests
        </SectionTitle>
        {requesting && (
          <TestRequestForm
            patientId={c.patientId}
            consultationId={c.id}
            onCancel={() => setRequesting(false)}
            onCreated={(t) => {
              setTests((list) => [t, ...list]);
              setRequesting(false);
            }}
          />
        )}
        {tests.length === 0 && !requesting && <p className="text-sm text-muted">No tests requested in this consultation.</p>}
        {tests.map((t) => (
          <TestRow key={t.id} test={t} run={run} canRecord={canRecord} onResulted={(r) => setTests((list) => withResult(list, r, myName))} />
        ))}
      </Card>
    </div>
  );
}
