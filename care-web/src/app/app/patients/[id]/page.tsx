"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { BLOOD_GROUPS, GENOTYPES, reasonHeaders } from "@/lib/care";
import { useCare } from "@/lib/care-me";
import { age, formatDay, formatStamp } from "@/lib/format";
import { canWriteNotes } from "@/lib/clinical";
import { useAction } from "@/lib/mutate";
import { canInvitePortal, canTakeMoney } from "@/lib/roles";
import type { PatientClinicalFacts, PatientDemographics, PatientDto, SaveClinicalFactsRequest } from "@/lib/types";
import { AdmitForm } from "@/components/admit-form";
import { CheckInDialog } from "@/components/check-in";
import { EmergencyReason } from "@/components/emergency-reason";
import { ShieldIcon } from "@/components/icons";
import { PortalAccess } from "@/components/portal-invites";
import {
  ApiErrorView,
  Button,
  Card,
  ErrorBox,
  Modal,
  Notice,
  PageHeader,
  PageLoader,
  PatientStatusBadge,
  SectionTitle,
  Select,
  Textarea,
  Wristband,
  buttonClass,
} from "@/components/ui";
import { PatientForm, fromPatient } from "../patient-form";

/** Splits a patient into demographics and clinical facts. Clinical facts are dropped unless the member has clinical access. */
function split(p: PatientDto, clinical: boolean): { demo: PatientDemographics; facts: PatientClinicalFacts | null } {
  const { bloodGroup, genotype, allergies, conditions, factsVersion, ...demo } = p;
  return { demo, facts: clinical ? { bloodGroup, genotype, allergies, conditions, factsVersion } : null };
}

type Read = { ok: true; patient: PatientDto } | { ok: false; error: ApiError };

/** A clinical member's read opens the record (logged by the API); without a care relationship it needs a reason, sent in the header. */
async function readPatient(id: string, reason?: string): Promise<Read> {
  try {
    return { ok: true, patient: await api<PatientDto>(`/api/care/patients/${encodeURIComponent(id)}`, { headers: reasonHeaders(reason) }) };
  } catch (e) {
    return { ok: false, error: toApiError(e) };
  }
}

type Load = { kind: "loading" } | { kind: "ok" } | { kind: "reason" } | { kind: "error"; error: ApiError };

export default function PatientPage() {
  const { id } = useParams<{ id: string }>();
  const { access } = useCare();
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [demo, setDemo] = useState<PatientDemographics | null>(null);
  const [facts, setFacts] = useState<PatientClinicalFacts | null>(null);
  const [editing, setEditing] = useState(false);
  const [editingFacts, setEditingFacts] = useState(false);
  const [checkingIn, setCheckingIn] = useState(false);
  const [admitting, setAdmitting] = useState(false);
  const status = useAction();

  /** Applies a read's outcome. The reason screen is returned to with the error when an emergency open fails (e.g. a too-long reason). */
  const apply = useCallback(
    (r: Read, fromReason: boolean): ApiError | null => {
      if (r.ok) {
        const parts = split(r.patient, access.isClinical);
        setDemo(parts.demo);
        setFacts(parts.facts);
        setLoad({ kind: "ok" });
        return null;
      }
      if (r.error.code === "emergency_reason_required" && access.isClinical) {
        setLoad({ kind: "reason" });
        return null;
      }
      if (!fromReason) setLoad({ kind: "error", error: r.error });
      return r.error;
    },
    [access.isClinical],
  );

  useEffect(() => {
    let live = true;
    void readPatient(id).then((r) => {
      if (live) apply(r, false);
    });
    return () => {
      live = false;
    };
  }, [id, apply]);

  const reasonRef = useRef<string | undefined>(undefined);
  const openWithReason = async (reason: string) => {
    reasonRef.current = reason;
    return apply(await readPatient(id, reason), true);
  };
  /** Final review I1: the facts dialog's "load the latest" after a 409 (someone else saved meanwhile). Re-reads the patient (a logged open). */
  const latestFacts = async (): Promise<PatientClinicalFacts | null> => {
    const r = await readPatient(id, reasonRef.current);
    if (!r.ok) return null;
    const parts = split(r.patient, access.isClinical);
    setFacts(parts.facts);
    return parts.facts;
  };

  if (load.kind === "loading") return <PageLoader />;
  if (load.kind === "error")
    return (
      <div className="space-y-4">
        <PageHeader title="Patient" back="/app/patients" />
        <ApiErrorView error={load.error} onRetry={async () => apply(await readPatient(id), false)} />
      </div>
    );
  if (load.kind === "reason") return <EmergencyReason onSubmit={openWithReason} />;
  if (!demo) return <PageLoader />;

  async function setInactive(inactive: boolean) {
    if (inactive && !window.confirm(`Make ${demo!.fullName} inactive? Their record is kept and you can make them active again.`)) return;
    const res = await status.run(() => api<PatientDemographics>(`/api/care/patients/${demo!.id}/${inactive ? "inactive" : "active"}`, { method: "POST" }));
    if (res.ok) setDemo(res.data);
  }

  const canEdit = access.canRegister;
  return (
    <div className="space-y-5">
      <PageHeader
        back="/app/patients"
        title={
          <span className="flex flex-wrap items-center gap-3">
            {demo.fullName}
            <PatientStatusBadge status={demo.status} />
          </span>
        }
        subtitle={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Wristband number={demo.number} size="lg" />
            <span className="tnum">
              {demo.sex}, {age(demo.dateOfBirth)}, born {formatDay(demo.dateOfBirth)}
            </span>
          </span>
        }
        action={
          <>
            {canEdit && demo.status === "Active" && (
              <Button onClick={() => setCheckingIn(true)}>Check in</Button>
            )}
            {canWriteNotes(access) && !access.readOnly && demo.status === "Active" && (
              <Button variant="secondary" onClick={() => setAdmitting(true)}>
                Admit
              </Button>
            )}
            {access.isClinical && (
              <Link href={`/app/patients/${demo.id}/record`} className={buttonClass("secondary")}>
                Clinical record
              </Link>
            )}
            {canTakeMoney(access) && (
              <Link href={`/app/bills?patient=${demo.id}`} className={buttonClass("secondary")}>
                Bills
              </Link>
            )}
            {access.isDirector && !access.readOnly && (
              <Link href={`/app/patients/${demo.id}/access-log`} className={buttonClass("secondary")}>
                Who opened this record
              </Link>
            )}
          </>
        }
      />
      {checkingIn && (
        <CheckInDialog patient={{ id: demo.id, number: demo.number, fullName: demo.fullName }} onClose={() => setCheckingIn(false)} />
      )}
      {admitting && (
        <Modal open title="Admit" onClose={() => setAdmitting(false)}>
          <AdmitForm patient={{ id: demo.id, number: demo.number, fullName: demo.fullName }} onCancel={() => setAdmitting(false)} />
        </Modal>
      )}

      {/* Clinical facts exist only in the clinical render path: front desk, the cashier and support views never get this component. */}
      {access.isClinical && facts && (
        <ClinicalFacts facts={facts} readOnly={access.readOnly} onEdit={() => setEditingFacts(true)} />
      )}

      <Card>
        <SectionTitle
          action={
            canEdit &&
            !editing && (
              <Button variant="ghost" className="-mr-2 px-2.5" onClick={() => setEditing(true)}>
                Edit details
              </Button>
            )
          }
        >
          Details
        </SectionTitle>
        {editing ? (
          <EditDemographics
            patient={demo}
            onCancel={() => setEditing(false)}
            onSaved={(p) => {
              setDemo(p);
              setEditing(false);
            }}
          />
        ) : (
          <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[10rem_minmax(0,1fr)]">
            <Row label="Phone" value={demo.phone} tnum />
            <Row label="Email" value={demo.email} />
            <Row label="Address" value={demo.address} />
            <Row
              label="Next of kin"
              value={
                demo.nextOfKinName
                  ? [demo.nextOfKinName, demo.nextOfKinRelationship && `(${demo.nextOfKinRelationship.toLowerCase()})`, demo.nextOfKinPhone].filter(Boolean).join(" ")
                  : null
              }
            />
            <Row label="Registered" value={formatStamp(demo.createdAt)} tnum />
          </dl>
        )}
      </Card>

      {canInvitePortal(access) && (
        <PortalAccess patientId={demo.id} patientName={demo.fullName} deceased={demo.status === "Deceased"} email={demo.email} phone={demo.phone} leadership={access.isLeadership} />
      )}

      {canEdit && demo.status !== "Deceased" && (
        <Card>
          <SectionTitle>Status</SectionTitle>
          <p className="-mt-1 mb-3 max-w-prose text-sm text-slate-700">
            {demo.status === "Active"
              ? "Patients are never deleted. Make a record inactive if it was registered by mistake or the patient has moved on."
              : "This record is inactive. Make it active again to book or check in the patient."}
          </p>
          <ErrorBox error={status.error} />
          <Button variant={demo.status === "Active" ? "danger" : "secondary"} loading={status.busy} onClick={() => setInactive(demo.status === "Active")}>
            {demo.status === "Active" ? "Make inactive" : "Make active"}
          </Button>
        </Card>
      )}

      {editingFacts && access.isClinical && facts && (
        <EditClinicalFacts
          patientId={demo.id}
          facts={facts}
          latest={latestFacts}
          onClose={() => setEditingFacts(false)}
          onSaved={(p) => {
            setFacts(split(p, access.isClinical).facts);
            setEditingFacts(false);
          }}
        />
      )}
    </div>
  );
}

function Row({ label, value, tnum }: { label: string; value: string | null | undefined; tnum?: boolean }) {
  return (
    <>
      <dt className="text-muted">{label}</dt>
      <dd className={tnum ? "tnum text-ink" : "break-words text-ink"}>{value || "—"}</dd>
    </>
  );
}

// ---------- Demographics ----------

function EditDemographics({ patient, onCancel, onSaved }: { patient: PatientDemographics; onCancel: () => void; onSaved: (p: PatientDemographics) => void }) {
  const { busy, error, run } = useAction();
  return (
    <PatientForm
      initial={fromPatient(patient)}
      error={error}
      onSubmit={async (value) => {
        // The PUT answers with demographics only.
        const res = await run(() => api<PatientDemographics>(`/api/care/patients/${patient.id}`, { method: "PUT", body: value }));
        if (res.ok) onSaved(res.data);
      }}
      actions={
        <>
          <Button type="submit" loading={busy}>
            Save details
          </Button>
          <Button variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
        </>
      }
    />
  );
}

// ---------- Clinical facts (clinical roles only) ----------

function ClinicalFacts({ facts, readOnly, onEdit }: { facts: PatientClinicalFacts; readOnly: boolean; onEdit: () => void }) {
  const allergies = facts.allergies ?? [];
  const conditions = facts.conditions ?? [];
  return (
    <section aria-labelledby="clinical-title" className="rounded-panel border border-rule bg-surface p-4 sm:p-5">
      <div className="mb-3 flex items-center justify-between gap-3">
        <h2 id="clinical-title" className="flex items-center gap-2 font-semibold text-ink">
          <ShieldIcon className="h-[18px] w-[18px] text-scrub" /> Clinical
        </h2>
        {!readOnly && (
          <Button variant="ghost" className="-mr-2 px-2.5" onClick={onEdit}>
            Edit clinical facts
          </Button>
        )}
      </div>
      {allergies.length > 0 ? (
        // The allergy sticker: a red label, the way a case folder carries one on its cover.
        <div role="note" className="mb-4 rounded-control border-2 border-arterial bg-arterial-wash px-3 py-2.5">
          <p className="text-sm font-bold text-arterial-dark">Allergic to</p>
          <ul className="mt-1 flex flex-wrap gap-1.5">
            {allergies.map((a) => (
              <li key={a} className="rounded-[4px] bg-surface px-2 py-0.5 text-sm font-bold text-arterial-dark">
                {a}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <p className="mb-4 text-sm text-muted">No allergies recorded. Ask the patient and record them, or record &ldquo;No known allergies&rdquo;.</p>
      )}
      <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[10rem_minmax(0,1fr)]">
        <dt className="text-muted">Key conditions</dt>
        <dd className="text-ink">{conditions.length > 0 ? conditions.join(", ") : "—"}</dd>
        <dt className="text-muted">Blood group</dt>
        <dd className="tnum text-ink">{facts.bloodGroup || "—"}</dd>
        <dt className="text-muted">Genotype</dt>
        <dd className="tnum text-ink">{facts.genotype || "—"}</dd>
      </dl>
      <p className="mt-3 text-xs text-muted">Only doctors and nurses see this. Opening this record is logged.</p>
    </section>
  );
}

/** One entry per line, blanks dropped. */
function lines(s: string): string[] {
  return s
    .split("\n")
    .map((l) => l.trim())
    .filter(Boolean);
}

/** The lines of `latest` plus any line of `typed` not already there (case aside), so an entry added meanwhile is never dropped. */
function mergeLines(latest: string[], typed: string[]): string[] {
  const out = [...latest];
  for (const t of typed) if (!out.some((l) => l.toLowerCase() === t.toLowerCase())) out.push(t);
  return out;
}

function EditClinicalFacts({
  patientId,
  facts,
  latest,
  onClose,
  onSaved,
}: {
  patientId: string;
  facts: PatientClinicalFacts;
  /** Re-reads the patient's facts (after a 409); null when the read failed. */
  latest: () => Promise<PatientClinicalFacts | null>;
  onClose: () => void;
  onSaved: (p: PatientDto) => void;
}) {
  const [allergies, setAllergies] = useState((facts.allergies ?? []).join("\n"));
  const [conditions, setConditions] = useState((facts.conditions ?? []).join("\n"));
  const [bloodGroup, setBloodGroup] = useState(facts.bloodGroup ?? "");
  const [genotype, setGenotype] = useState(facts.genotype ?? "");
  // Final review I1: the version these details were loaded at. A save from a stale version gets 409 and nothing is overwritten.
  const [version, setVersion] = useState(facts.factsVersion ?? 0);
  const [merged, setMerged] = useState(false);
  const [reloading, setReloading] = useState(false);
  const { busy, error, run } = useAction();
  const stale = error?.code === "clinical_facts_stale" && !merged;

  async function submit(e: FormEvent) {
    e.preventDefault();
    // A full replace of what was loaded at `version`: every field is sent.
    const body: SaveClinicalFactsRequest = { allergies: lines(allergies), conditions: lines(conditions), bloodGroup: bloodGroup || null, genotype: genotype || null, version };
    setMerged(false);
    const res = await run(() => api<PatientDto>(`/api/care/patients/${patientId}/clinical-facts`, { method: "PUT", body }));
    if (res.ok) onSaved(res.data);
  }

  /** Loads what is saved now and keeps what was typed on top, for the clinician to check and save again. */
  async function loadLatest() {
    setReloading(true);
    try {
      const now = await latest();
      if (!now) return;
      setAllergies(mergeLines(now.allergies ?? [], lines(allergies)).join("\n"));
      setConditions(mergeLines(now.conditions ?? [], lines(conditions)).join("\n"));
      setBloodGroup(bloodGroup || now.bloodGroup || "");
      setGenotype(genotype || now.genotype || "");
      setVersion(now.factsVersion ?? 0);
      setMerged(true);
    } finally {
      setReloading(false);
    }
  }

  const fields = ["allergies", "conditions", "bloodGroup", "genotype"];
  return (
    <Modal open title="Clinical facts" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Textarea
          label="Allergies"
          rows={3}
          value={allergies}
          onChange={(e) => setAllergies(e.target.value)}
          error={error?.field("allergies")}
          hint="One per line, such as Penicillin. Prescriptions are checked against these, but the check is an aid, not a guarantee."
        />
        <Textarea
          label="Key conditions"
          rows={3}
          value={conditions}
          onChange={(e) => setConditions(e.target.value)}
          error={error?.field("conditions")}
          hint="One per line, such as Hypertension or Asthma."
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Blood group" value={bloodGroup} onChange={(e) => setBloodGroup(e.target.value)} error={error?.field("bloodGroup")}>
            <option value="">Not known</option>
            {BLOOD_GROUPS.map((b) => (
              <option key={b} value={b}>
                {b}
              </option>
            ))}
          </Select>
          <Select label="Genotype" value={genotype} onChange={(e) => setGenotype(e.target.value)} error={error?.field("genotype")}>
            <option value="">Not known</option>
            {GENOTYPES.map((g) => (
              <option key={g} value={g}>
                {g}
              </option>
            ))}
          </Select>
        </div>
        <Notice>
          These are encrypted and only doctors and nurses see them. <span className="text-muted">Saved as a whole: what you see here replaces what was there.</span>
        </Notice>
        <ErrorBox error={error && !merged && !fields.some((f) => error.field(f)) ? error : null} />
        {stale && (
          <Button variant="secondary" loading={reloading} onClick={loadLatest}>
            Load the latest details
          </Button>
        )}
        {merged && <Notice tone="warning">The latest saved details are loaded, with what you typed added. Check them, then save again.</Notice>}
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={busy} disabled={stale}>
            Save
          </Button>
        </div>
      </form>
    </Modal>
  );
}
