"use client";

// Pieces shared by the consultation and the record timeline (Task 18). Everything here is clinical: it is rendered only on pages that
// have checked clinical access, and every read or write goes through `run` from useClinicalRead, which carries an accepted emergency
// reason in the X-Care-Reason header and asks for one when the API needs it.

import { useState, type FormEvent, type ReactNode } from "react";
import { api, downloadFile } from "@/lib/api";
import { bp, flagTone, FREQUENCIES, TEST_KINDS } from "@/lib/clinical";
import { age, formatDay, formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs, useApi } from "@/lib/use-api";
import type { PatientDto, PriceItemDto } from "@/lib/types";
import type {
  AddendumDto,
  CreatePrescriptionRequest,
  CreateTestRequest,
  DoseFrequency,
  NoteKind,
  PrescriptionDto,
  PrescriptionItemInput,
  TestFileLinkDto,
  TestKind,
  TestRequestDto,
  TestResultDto,
  TestReviewDto,
  VitalSignsDto,
} from "@/lib/types/clinical";
import { useCare } from "@/lib/care-me";
import { canReviewResults } from "@/lib/roles";
import { ShieldIcon } from "./icons";
import { Badge, Button, Checkbox, ErrorBox, Input, Modal, Notice, PatientStatusBadge, Select, Textarea, Wristband, linkClass } from "./ui";

export type ClinicalRun = <R>(fn: (headers: Record<string, string> | undefined) => Promise<R>) => Promise<R>;

// ---------- The patient summary banner ----------

/** The allergy sticker: a red label, the way a case folder carries one on its cover. Clinical render path only. */
export function AllergySticker({ allergies }: { allergies: string[] | null | undefined }) {
  const list = allergies ?? [];
  if (list.length === 0)
    return <p className="rounded-control border border-dashed border-rule px-3 py-2 text-sm text-muted">No allergies recorded. Ask the patient.</p>;
  return (
    <div role="note" className="rounded-control border-2 border-arterial bg-arterial-wash px-3 py-2">
      <p className="text-sm font-bold text-arterial-dark">Allergic to</p>
      <ul className="mt-1 flex flex-wrap gap-1.5">
        {list.map((a) => (
          <li key={a} className="rounded-[4px] bg-surface px-2 py-0.5 text-sm font-bold text-arterial-dark">
            {a}
          </li>
        ))}
      </ul>
    </div>
  );
}

/** The latest set of vital signs in one line, with their flags. */
export function VitalsLine({ vitals }: { vitals: VitalSignsDto }) {
  const parts = [
    vitals.temperatureC !== null && `${vitals.temperatureC} °C`,
    bp(vitals.systolic, vitals.diastolic) && `BP ${bp(vitals.systolic, vitals.diastolic)}`,
    vitals.pulse !== null && `pulse ${vitals.pulse}`,
    vitals.respRate !== null && `resp ${vitals.respRate}`,
    vitals.spO2 !== null && `SpO2 ${vitals.spO2}%`,
    vitals.weightKg !== null && `${vitals.weightKg} kg`,
    vitals.bmi !== null && `BMI ${vitals.bmi}`,
  ].filter(Boolean);
  return (
    <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
      <span className="tnum text-ink">{parts.join(", ")}</span>
      {vitals.flags.map((f) => (
        <Badge key={f} tone={flagTone(f)}>
          {f}
        </Badge>
      ))}
      <span className="text-xs text-muted">
        {formatStamp(vitals.takenAt)}, {vitals.takenByName}
      </span>
    </span>
  );
}

/**
 * The patient summary first (spec, consultation screen): who, the allergy sticker, key conditions, blood group and genotype, the latest
 * vitals, and current medicines when given.
 */
export function PatientBanner({
  patient,
  latestVitals,
  medicines,
  children,
}: {
  patient: PatientDto;
  latestVitals?: VitalSignsDto | null;
  medicines?: string[];
  children?: ReactNode;
}) {
  const conditions = patient.conditions ?? [];
  return (
    <section aria-label="Patient summary" className="rounded-panel border border-rule bg-surface p-4 sm:p-5">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <Wristband number={patient.number} size="lg" />
        <h2 className="display text-xl font-bold text-ink">{patient.fullName}</h2>
        <PatientStatusBadge status={patient.status} />
        <span className="tnum text-sm text-muted">
          {patient.sex}, {age(patient.dateOfBirth)}, born {formatDay(patient.dateOfBirth)}
        </span>
        {children && <span className="ml-auto flex flex-wrap gap-2">{children}</span>}
      </div>
      <div className="mt-3 grid gap-3 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
        <AllergySticker allergies={patient.allergies} />
        <dl className="grid content-start gap-x-4 gap-y-1.5 text-sm sm:grid-cols-[9rem_minmax(0,1fr)]">
          <dt className="text-muted">Key conditions</dt>
          <dd className="text-ink">{conditions.length > 0 ? conditions.join(", ") : "—"}</dd>
          <dt className="text-muted">Blood group, genotype</dt>
          <dd className="tnum text-ink">
            {patient.bloodGroup || "—"}, {patient.genotype || "—"}
          </dd>
          <dt className="text-muted">Latest vitals</dt>
          <dd>{latestVitals ? <VitalsLine vitals={latestVitals} /> : <span className="text-muted">None recorded</span>}</dd>
          {medicines && (
            <>
              <dt className="text-muted">Recent medicines</dt>
              <dd className="text-ink">{medicines.length > 0 ? medicines.join(", ") : "—"}</dd>
            </>
          )}
        </dl>
      </div>
      <p className="mt-3 flex items-center gap-1.5 text-xs text-muted">
        <ShieldIcon className="h-3.5 w-3.5" /> Only doctors and nurses see this. Opening this record is logged.
      </p>
    </section>
  );
}

// ---------- Addenda ----------

/** A signed note's addenda (oldest first) and, for clinical staff, a form to add one. */
export function Addenda({
  kind,
  noteId,
  addenda,
  run,
  readOnly,
  onAdded,
}: {
  kind: NoteKind;
  noteId: string;
  addenda: AddendumDto[];
  run: ClinicalRun;
  readOnly?: boolean;
  onAdded: (a: AddendumDto) => void;
}) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState("");
  const { busy, error, run: act } = useAction();
  const route = kind === "Consultation" ? "consultation" : kind === "WardRound" ? "ward-round" : "discharge";

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await act(() => run((headers) => api<AddendumDto>(`/api/care/notes/${route}/${noteId}/addenda`, { method: "POST", body: { text }, headers })));
    if (res.ok) {
      onAdded(res.data);
      setText("");
      setOpen(false);
    }
  }

  return (
    <div className="space-y-2">
      {addenda.length > 0 && (
        <ul className="space-y-2">
          {addenda.map((a) => (
            <li key={a.id} className="rounded-control border-l-[3px] border-scrub-line bg-slate-50 px-3 py-2 text-sm">
              <p className="text-xs text-muted">
                Addendum by {a.authorName}, {formatStamp(a.at)}
              </p>
              <p className="mt-0.5 whitespace-pre-wrap text-ink">{a.text}</p>
            </li>
          ))}
        </ul>
      )}
      {!readOnly &&
        (open ? (
          <form onSubmit={submit} className="space-y-2">
            <Textarea label="Addendum" rows={3} maxLength={4000} value={text} onChange={(e) => setText(e.target.value)} error={error?.field("text")} />
            <ErrorBox error={error && !error.field("text") ? error : null} />
            <div className="flex gap-2">
              <Button type="submit" loading={busy} disabled={!text.trim()}>
                Add addendum
              </Button>
              <Button variant="ghost" onClick={() => setOpen(false)}>
                Cancel
              </Button>
            </div>
          </form>
        ) : (
          <Button variant="secondary" className="min-h-10" onClick={() => setOpen(true)}>
            Add an addendum
          </Button>
        ))}
    </div>
  );
}

// ---------- Prescriptions ----------

/** One prescription: its drugs as written, the allergy override when there was one, and the printable PDF. */
export function PrescriptionCard({ prescription: p, patientNumber, run }: { prescription: PrescriptionDto; patientNumber: string; run: ClinicalRun }) {
  const pdf = useAction();
  return (
    <div className="rounded-control border border-rule p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted">
          {p.doctorName}, {formatStamp(p.createdAt)}
        </p>
        <Button
          variant="ghost"
          className="min-h-9 px-2.5"
          loading={pdf.busy}
          onClick={() =>
            pdf.run(() => run((headers) => downloadFile(`/api/care/prescriptions/${p.id}/pdf`, `prescription-${patientNumber}.pdf`, { headers })))
          }
        >
          Print (PDF)
        </Button>
      </div>
      {p.allergyAcknowledged && (
        <p className="mt-1 text-xs font-bold text-arterial-dark">Prescribed over a recorded allergy: {p.overriddenAllergens.join(", ")}</p>
      )}
      <ul className="mt-2 space-y-1.5 text-sm">
        {p.items.map((i) => (
          <li key={i.id}>
            <span className="font-bold text-ink">
              {i.drug}
              {i.strength && ` ${i.strength}`}
            </span>{" "}
            <span className="text-slate-700">
              {i.dose}, {i.frequency}
              {i.durationDays ? ` for ${i.durationDays} day${i.durationDays === 1 ? "" : "s"}` : ""}
              {i.route ? `, ${i.route}` : ""}
              {i.quantity ? `. Quantity ${i.quantity}` : ""}
            </span>
            {i.instructions && <span className="block text-xs text-muted">{i.instructions}</span>}
          </li>
        ))}
      </ul>
      <ErrorBox error={pdf.error} />
    </div>
  );
}

interface ItemRow {
  key: number;
  drug: string;
  strength: string;
  dose: string;
  frequency: DoseFrequency | "";
  durationDays: string;
  quantity: string;
  route: string;
  instructions: string;
  priceItemId: string;
}
const blankItem = (key: number): ItemRow => ({
  key,
  drug: "",
  strength: "",
  dose: "",
  frequency: "",
  durationDays: "",
  quantity: "",
  route: "",
  instructions: "",
  priceItemId: "",
});
const intOrNull = (s: string) => (s.trim() && Number.isFinite(Number(s)) ? Math.trunc(Number(s)) : null);
const textOrNull = (s: string) => s.trim() || null;

/**
 * Writes a prescription from a consultation. A drug that matches a recorded allergy comes back 409 `allergy_unacknowledged` with the
 * API's sentence ("{Patient} is recorded as allergic to {allergen}."): the dialog shows it as it is and prescribing anyway needs an explicit
 * tick, which the API stores and audits.
 */
export function PrescriptionForm({
  patientId,
  consultationId,
  admissionId,
  onCreated,
  onCancel,
}: {
  patientId: string;
  consultationId: string | null;
  /** Task 19: a prescription written on an admission (its doses go on the medication chart). */
  admissionId?: string | null;
  onCreated: (p: PrescriptionDto) => void;
  onCancel: () => void;
}) {
  const drugs = useApi<PriceItemDto[]>(`/api/care/price-list${qs({ active: true, category: "Drug" })}`);
  const [rows, setRows] = useState<ItemRow[]>([blankItem(0)]);
  const [nextKey, setNextKey] = useState(1);
  const [warning, setWarning] = useState<string | null>(null);
  const [ack, setAck] = useState(false);
  const { busy, error, setError, run } = useAction();

  function update(key: number, patch: Partial<ItemRow>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function send(acknowledged: boolean) {
    const items: PrescriptionItemInput[] = rows.map((r) => ({
      drug: r.drug.trim(),
      strength: textOrNull(r.strength),
      dose: r.dose.trim(),
      frequency: r.frequency || null,
      durationDays: intOrNull(r.durationDays),
      quantity: intOrNull(r.quantity),
      route: textOrNull(r.route),
      instructions: textOrNull(r.instructions),
      priceItemId: r.priceItemId || null,
    }));
    const body: CreatePrescriptionRequest = { patientId, consultationId, admissionId: admissionId ?? null, allergyAcknowledged: acknowledged, items };
    const res = await run(() => api<PrescriptionDto>("/api/care/prescriptions", { method: "POST", body }));
    if (res.ok) {
      setWarning(null);
      onCreated(res.data);
      return;
    }
    if (res.error.code === "allergy_unacknowledged") {
      // The dialog carries the sentence; keep it out of the form's error box.
      setError(null);
      setAck(false);
      setWarning(res.error.message);
    } else if (acknowledged) setWarning(null);
  }

  const itemError = (i: number, f: string) => error?.field(`items[${i}].${f}`);
  const shownElsewhere = (e: NonNullable<typeof error>) => e.field("items") || rows.some((_, i) => ["drug", "strength", "dose", "frequency", "durationDays", "quantity", "route", "instructions", "priceItemId"].some((f) => itemError(i, f)));

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        void send(false);
      }}
      className="@container space-y-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3"
    >
      {rows.map((r, i) => (
        <fieldset key={r.key} className="min-w-0 space-y-3 rounded-control border border-rule bg-surface p-3">
          <legend className="px-1 text-sm font-bold text-ink">Drug {rows.length > 1 ? i + 1 : ""}</legend>
          <div className="grid gap-3 @md:grid-cols-2 @3xl:grid-cols-3">
            <Input label="Drug" value={r.drug} maxLength={200} onChange={(e) => update(r.key, { drug: e.target.value })} error={itemError(i, "drug")} />
            <Input label="Strength" value={r.strength} maxLength={60} placeholder="500 mg" onChange={(e) => update(r.key, { strength: e.target.value })} error={itemError(i, "strength")} />
            <Input label="Dose" value={r.dose} maxLength={100} placeholder="1 tablet" onChange={(e) => update(r.key, { dose: e.target.value })} error={itemError(i, "dose")} />
            <Select label="How often" value={r.frequency} onChange={(e) => update(r.key, { frequency: e.target.value as DoseFrequency })} error={itemError(i, "frequency")}>
              <option value="">Choose</option>
              {FREQUENCIES.map((f) => (
                <option key={f.value} value={f.value}>
                  {f.label}
                </option>
              ))}
            </Select>
            <Input label="Days" type="number" min={1} max={365} value={r.durationDays} onChange={(e) => update(r.key, { durationDays: e.target.value })} error={itemError(i, "durationDays")} />
            <Input label="Quantity" type="number" min={1} max={10000} value={r.quantity} onChange={(e) => update(r.key, { quantity: e.target.value })} error={itemError(i, "quantity")} />
            <Input label="Route" value={r.route} maxLength={60} placeholder="Oral" onChange={(e) => update(r.key, { route: e.target.value })} error={itemError(i, "route")} />
            {drugs.data && drugs.data.length > 0 && (
              <Select label="Charge as" value={r.priceItemId} onChange={(e) => update(r.key, { priceItemId: e.target.value })} error={itemError(i, "priceItemId")} hint="Billed when the clinic dispenses.">
                <option value="">Not charged</option>
                {drugs.data.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </Select>
            )}
          </div>
          <Input label="Instructions" value={r.instructions} maxLength={500} placeholder="After meals" onChange={(e) => update(r.key, { instructions: e.target.value })} error={itemError(i, "instructions")} />
          {rows.length > 1 && (
            <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}>
              Remove this drug
            </Button>
          )}
        </fieldset>
      ))}
      {error?.field("items") && <p className="text-sm font-medium text-arterial">{error.field("items")}</p>}
      <ErrorBox error={error && !shownElsewhere(error) ? error : null} />
      <div className="flex flex-wrap gap-2">
        <Button
          variant="secondary"
          onClick={() => {
            setRows((rs) => [...rs, blankItem(nextKey)]);
            setNextKey((k) => k + 1);
          }}
          disabled={rows.length >= 20}
        >
          Add another drug
        </Button>
        <Button type="submit" loading={busy && !warning}>
          Prescribe
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>

      {warning && (
        <Modal open title="Recorded allergy" onClose={() => setWarning(null)}>
          <div className="space-y-4">
            <div role="alert" className="rounded-control border-2 border-arterial bg-arterial-wash px-3 py-2.5 text-sm font-bold text-arterial-dark">
              {warning}
            </div>
            <p className="text-sm text-slate-700">
              The check matches drug names against the recorded allergies. It is an aid, not a guarantee. Prescribing anyway is recorded on the prescription and in the audit log.
            </p>
            <Checkbox checked={ack} onChange={setAck} label="I have checked this with the patient and still want to prescribe." />
            <ErrorBox error={error} />
            <div className="flex flex-wrap justify-end gap-2">
              <Button variant="secondary" onClick={() => setWarning(null)}>
                Change the prescription
              </Button>
              <Button variant="danger" disabled={!ack} loading={busy} onClick={() => send(true)}>
                Prescribe anyway
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </form>
  );
}

// ---------- Tests ----------

/** Request a test from the consultation: lab, imaging or other, a description, and the price item it is charged as. */
export function TestRequestForm({
  patientId,
  consultationId,
  admissionId,
  onCreated,
  onCancel,
}: {
  patientId: string;
  consultationId: string | null;
  admissionId?: string | null;
  onCreated: (t: TestRequestDto) => void;
  onCancel: () => void;
}) {
  const tests = useApi<PriceItemDto[]>(`/api/care/price-list${qs({ active: true, category: "Test" })}`);
  const [kind, setKind] = useState<TestKind>("Lab");
  const [description, setDescription] = useState("");
  const [priceItemId, setPriceItemId] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: CreateTestRequest = { patientId, consultationId, admissionId: admissionId ?? null, kind, description: description.trim(), priceItemId: priceItemId || null };
    const res = await run(() => api<TestRequestDto>("/api/care/tests", { method: "POST", body }));
    if (res.ok) onCreated(res.data);
  }

  return (
    <form onSubmit={submit} className="@container space-y-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3">
      <div className="grid gap-3 @md:grid-cols-[10rem_minmax(0,1fr)]">
        <Select label="Kind" value={kind} onChange={(e) => setKind(e.target.value as TestKind)} error={error?.field("kind")}>
          {TEST_KINDS.map((k) => (
            <option key={k.value} value={k.value}>
              {k.label}
            </option>
          ))}
        </Select>
        <Input label="Test" value={description} maxLength={300} placeholder="Full blood count" onChange={(e) => setDescription(e.target.value)} error={error?.field("description")} />
      </div>
      {tests.data && tests.data.length > 0 && (
        <Select label="Charge as" value={priceItemId} onChange={(e) => setPriceItemId(e.target.value)} error={error?.field("priceItemId")}>
          <option value="">Not charged</option>
          {tests.data.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </Select>
      )}
      <ErrorBox error={error && !["kind", "description", "priceItemId"].some((f) => error.field(f)) ? error : null} />
      <div className="flex gap-2">
        <Button type="submit" loading={busy} disabled={!description.trim()}>
          Request test
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

const kindLabel = (k: TestKind) => TEST_KINDS.find((x) => x.value === k)?.label ?? k;

/**
 * One test with its result: entry (text and/or a PDF or image file, once) for doctors and nurses, and a short-lived link to the result
 * file, issued after a logged read. Post-build: a result shows "Awaiting review" until a doctor or the medical director presses
 * "Review result" (`POST /api/care/tests/{id}/review`), which releases it to the patient's portal and tells the patient; then
 * "Reviewed by {name}".
 */
export function TestRow({ test, run, canRecord, onResulted }: { test: TestRequestDto; run: ClinicalRun; canRecord: boolean; onResulted: (r: TestResultDto) => void }) {
  const { access } = useCare();
  const [entering, setEntering] = useState(false);
  const [link, setLink] = useState<TestFileLinkDto | null>(null);
  const [review, setReview] = useState<TestReviewDto | null>(null);
  const file = useAction();
  const reviewing = useAction();
  const reviewedAt = review?.reviewedAt ?? test.reviewedAt;
  const reviewedByName = review ? review.reviewedByName : test.reviewedByName;

  async function openFile() {
    const res = await file.run(() => run((headers) => api<TestFileLinkDto>(`/api/care/tests/${test.id}/file`, { headers })));
    if (res.ok) setLink(res.data);
  }

  async function markReviewed() {
    const res = await reviewing.run(() => api<TestReviewDto>(`/api/care/tests/${test.id}/review`, { method: "POST" }));
    if (res.ok) setReview(res.data);
  }

  return (
    <div className="rounded-control border border-rule p-3 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone="neutral">{kindLabel(test.kind)}</Badge>
        <span className="font-bold text-ink">{test.description}</span>
        <Badge tone={test.status === "Resulted" ? "pass" : "iodine"}>{test.status === "Resulted" ? "Result in" : "Waiting for result"}</Badge>
      </div>
      <p className="mt-1 text-xs text-muted">
        Requested by {test.requestedByName}, {formatStamp(test.createdAt)}
      </p>
      {test.status === "Resulted" && (
        <div className="mt-2 space-y-1.5">
          {test.resultText && <p className="whitespace-pre-wrap text-ink">{test.resultText}</p>}
          <p className="text-xs text-muted">
            Result by {test.resultedByName ?? "staff"}, {formatStamp(test.resultedAt)}
          </p>
          {reviewedAt ? (
            <p className="text-xs text-muted">
              {reviewedByName ? `Reviewed by ${reviewedByName}, ${formatStamp(reviewedAt)}` : `Reviewed, ${formatStamp(reviewedAt)}`}. The patient can see it.
            </p>
          ) : (
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="iodine">Awaiting review</Badge>
              <span className="text-xs text-muted">The patient can&apos;t see it until a doctor reviews it.</span>
              {canReviewResults(access) && (
                <Button variant="secondary" className="min-h-10" loading={reviewing.busy} onClick={markReviewed}>
                  Review result
                </Button>
              )}
            </div>
          )}
          <ErrorBox error={reviewing.error} />
          {test.hasFile &&
            (link ? (
              <a href={link.url} target="_blank" rel="noopener noreferrer" className={linkClass}>
                Open the result file
              </a>
            ) : (
              <Button variant="secondary" className="min-h-10" loading={file.busy} onClick={openFile}>
                Get the result file
              </Button>
            ))}
          {link && <p className="text-xs text-muted">The link works for two minutes. Opening it is logged.</p>}
          <ErrorBox error={file.error} />
        </div>
      )}
      {test.status === "Requested" &&
        canRecord &&
        (entering ? (
          <ResultForm
            testId={test.id}
            run={run}
            onCancel={() => setEntering(false)}
            onDone={(r) => {
              setEntering(false);
              onResulted(r);
            }}
          />
        ) : (
          <Button variant="secondary" className="mt-2 min-h-10" onClick={() => setEntering(true)}>
            Record the result
          </Button>
        ))}
    </div>
  );
}

const ACCEPT = "application/pdf,image/jpeg,image/png,image/webp";

/** Result entry: typed text, a file (PDF, JPEG, PNG or WebP, at most 10 MB), or both. A test takes one result. */
function ResultForm({ testId, run, onCancel, onDone }: { testId: string; run: ClinicalRun; onCancel: () => void; onDone: (r: TestResultDto) => void }) {
  const [text, setText] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [tooBig, setTooBig] = useState(false);
  const { busy, error, run: act } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const res = await act(() =>
      run((headers) => {
        if (file) {
          const fd = new FormData();
          fd.append("file", file);
          fd.append("text", text);
          return api<TestResultDto>(`/api/care/tests/${testId}/result`, { method: "POST", body: fd, headers });
        }
        return api<TestResultDto>(`/api/care/tests/${testId}/result`, { method: "POST", body: { text }, headers });
      }),
    );
    if (res.ok) onDone(res.data);
  }

  return (
    <form onSubmit={submit} className="mt-2 space-y-3 rounded-control border border-scrub-line bg-scrub-wash/40 p-3">
      <Textarea label="Result" rows={4} maxLength={10000} value={text} onChange={(e) => setText(e.target.value)} error={error?.field("text")} />
      <div className="space-y-1.5">
        <label htmlFor={`file-${testId}`} className="block text-sm font-medium text-slate-700">
          Result file
        </label>
        <input
          id={`file-${testId}`}
          type="file"
          accept={ACCEPT}
          className="block w-full text-sm file:mr-3 file:min-h-10 file:rounded-control file:border file:border-rule file:bg-surface file:px-3 file:font-semibold"
          onChange={(e) => {
            const f = e.target.files?.[0] ?? null;
            setTooBig(!!f && f.size > 10 * 1024 * 1024);
            setFile(f);
          }}
        />
        {(tooBig || error?.field("file")) && (
          <p className="text-xs font-medium text-arterial">{tooBig ? "Result files must be 10 MB or smaller." : error?.field("file")}</p>
        )}
        {!tooBig && !error?.field("file") && <p className="text-xs text-muted">A PDF, or a JPEG, PNG or WebP photo. Images are re-saved without their metadata.</p>}
      </div>
      <Notice>
        The patient sees the result only after a doctor reviews it; their portal then says only that a new result is ready. A result can&apos;t be
        changed after saving.
      </Notice>
      <ErrorBox error={error && !["text", "file"].some((f) => error.field(f)) ? error : null} />
      <div className="flex gap-2">
        <Button type="submit" loading={busy} disabled={tooBig || (!text.trim() && !file)}>
          Save the result
        </Button>
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}

/** Applies a recorded result to a test in a list. */
export function withResult(tests: TestRequestDto[], r: TestResultDto, by: string): TestRequestDto[] {
  return tests.map((t) =>
    t.id === r.id ? { ...t, status: r.status, resultText: r.resultText, hasFile: r.hasFile, resultedBy: r.resultedBy, resultedAt: r.resultedAt, resultedByName: by } : t,
  );
}
