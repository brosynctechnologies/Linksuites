"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { age, formatDay } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { qs } from "@/lib/use-api";
import type { PatientSummaryDto, RegisterPatientResult, SavePatientRequest } from "@/lib/types";
import { Button, CaseSheet, NoAccess, Notice, PageHeader, PatientStatusBadge, Wristband, buttonClass, linkClass } from "@/components/ui";
import { PatientForm, emptyPatient } from "../patient-form";

/** Last ten digits, so 0803… and +234803… compare equal. */
function phoneKey(phone: string | null | undefined): string | null {
  const digits = (phone ?? "").replace(/\D/g, "");
  return digits.length >= 7 ? digits.slice(-10) : null;
}

/**
 * The API's duplicate rule, checked before saving: the same first name, last name and date of birth, or the same phone. It uses the
 * ordinary patient search, so nothing is registered until the clerk has seen the possible matches.
 */
async function findDuplicates(v: SavePatientRequest): Promise<PatientSummaryDto[]> {
  const searches = [`${v.firstName} ${v.lastName}`];
  if (v.phone) searches.push(v.phone);
  const results = await Promise.all(searches.map((q) => api<PatientSummaryDto[]>(`/api/care/patients${qs({ q })}`)));
  const name = `${v.firstName} ${v.lastName}`.toLowerCase();
  const phone = phoneKey(v.phone);
  const seen = new Map<string, PatientSummaryDto>();
  for (const p of results.flat()) {
    const sameName = p.fullName.toLowerCase() === name && p.dateOfBirth === v.dateOfBirth;
    const samePhone = !!phone && phoneKey(p.phone) === phone;
    if (sameName || samePhone) seen.set(p.id, p);
  }
  return [...seen.values()];
}

function DuplicateList({ rows, label }: { rows: PatientSummaryDto[]; label: string }) {
  return (
    <CaseSheet label={label}>
      {rows.map((p) => (
        <li key={p.id} className="case-pad flex flex-wrap items-center justify-between gap-x-4 gap-y-2 py-3">
          <span className="flex min-w-0 flex-wrap items-center gap-2">
            <Wristband number={p.number} />
            <span className="font-bold text-ink">{p.fullName}</span>
            <PatientStatusBadge status={p.status} />
            <span className="tnum text-sm text-muted">
              {p.sex}, born {formatDay(p.dateOfBirth)} ({age(p.dateOfBirth)}){p.phone ? `, ${p.phone}` : ""}
            </span>
          </span>
          <Link href={`/app/patients/${p.id}`} className={linkClass}>
            Open<span className="sr-only"> {p.fullName}</span>
          </Link>
        </li>
      ))}
    </CaseSheet>
  );
}

export default function RegisterPatientPage() {
  const { access } = useCare();
  const { busy, error, run } = useAction();
  const [pending, setPending] = useState<{ value: SavePatientRequest; matches: PatientSummaryDto[] } | null>(null);
  const [done, setDone] = useState<RegisterPatientResult | null>(null);
  const [formKey, setFormKey] = useState(0);
  // What was typed, so "Back to the form" after the duplicate warning keeps it.
  const [draft, setDraft] = useState<SavePatientRequest>(emptyPatient);

  if (!access.canRegister) return <NoAccess text="Registering patients is for the front desk, clinical staff and leadership." />;

  async function register(value: SavePatientRequest) {
    const res = await run(() => api<RegisterPatientResult>("/api/care/patients", { method: "POST", body: value }));
    // Success shows the confirmation. A failure (400, 403, network) goes back to the form with the entries kept (the draft) and the
    // API's sentence shown, by the field or in the error box, so a refused registration is never silent.
    setPending(null);
    if (res.ok) setDone(res.data);
  }

  async function check(value: SavePatientRequest) {
    setDraft(value);
    // Only check once the fields the rule needs are in; the API returns the field errors otherwise.
    if (value.firstName && value.lastName && value.dateOfBirth) {
      const res = await run(() => findDuplicates(value));
      if (!res.ok) return;
      if (res.data.length > 0) return setPending({ value, matches: res.data });
    }
    await register(value);
  }

  if (done) {
    const others = done.possibleDuplicates.filter((p) => p.id !== done.patient.id);
    return (
      <div className="space-y-5">
        <PageHeader title="Patient registered" back="/app/patients" />
        <div className="flex flex-wrap items-center gap-3 rounded-panel border border-rule bg-surface p-5">
          <Wristband number={done.patient.number} size="lg" />
          <span className="display text-xl font-bold text-ink">{done.patient.fullName}</span>
        </div>
        <p className="text-sm text-slate-700">Write this number on the patient&apos;s card. It finds them at every visit.</p>
        {others.length > 0 && (
          <div className="space-y-3">
            <Notice tone="warning">
              The clinic already has a patient with the same name and date of birth, or the same phone. If this is the same person, open the earlier record
              and make this new one inactive.
            </Notice>
            <DuplicateList rows={others} label="Possible duplicates" />
          </div>
        )}
        <div className="flex flex-wrap gap-2">
          <Link href={`/app/patients/${done.patient.id}`} className={buttonClass("primary")}>
            Open the patient
          </Link>
          <Button
            variant="secondary"
            onClick={() => {
              setDone(null);
              setDraft(emptyPatient());
              setFormKey((k) => k + 1);
            }}
          >
            Register another
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader title="Register a patient" back="/app/patients" subtitle="The patient number is given when you save." />
      {pending ? (
        <div className="space-y-4">
          <Notice tone="warning">
            <p className="font-bold">This patient may already be registered.</p>
            <p className="mt-1">
              {pending.matches.length === 1 ? "A patient has" : `${pending.matches.length} patients have`} the same name and date of birth, or the same
              phone. Open the existing record instead of registering the same person twice.
            </p>
          </Notice>
          <DuplicateList rows={pending.matches} label="Possible duplicates" />
          <div className="flex flex-wrap gap-2">
            <Button variant="secondary" onClick={() => setPending(null)}>
              Back to the form
            </Button>
            <Button loading={busy} onClick={() => register(pending.value)}>
              Register as a new patient anyway
            </Button>
          </div>
        </div>
      ) : (
        <div className="rounded-panel border border-rule bg-surface p-4 sm:p-6">
          <PatientForm
            key={formKey}
            initial={draft}
            error={error}
            onSubmit={(v) => void check(v)}
            actions={
              <>
                <Button type="submit" loading={busy}>
                  Register patient
                </Button>
                <Link href="/app/patients" className={buttonClass("ghost")}>
                  Cancel
                </Link>
              </>
            }
          />
        </div>
      )}
    </div>
  );
}
