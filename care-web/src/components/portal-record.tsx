"use client";

// The patient's own record in the portal (Task 20): signed notes and their addenda only, exactly as `GET .../record` returns them. The
// API leaves out drafts, staff ids, reasons and the ward charts; this page shows what comes back and adds nothing. Nothing from the
// record is written to the address, to storage or to the console.

import { age, formatDay } from "@/lib/format";
import type {
  PortalAddendumDto,
  PortalAdmissionDto,
  PortalConsultationDto,
  PortalDischargeSummaryDto,
  PortalPrescriptionDto,
  PortalProfileDto,
  PortalRecordDto,
  PortalResultDto,
  PortalVitalsDto,
  PortalWardRoundDto,
} from "@/lib/types/portal";
import { DISCHARGE, FREQUENCY_WORDS, Field, Nothing, Section, lagosShort } from "./portal-parts";
import { Badge, Card, cx } from "./ui";

function Addenda({ list }: { list: PortalAddendumDto[] }) {
  if (list.length === 0) return null;
  return (
    <div className="mt-3 space-y-2 border-t border-rule-soft pt-3">
      {list.map((a, i) => (
        <div key={i} className="rounded-control bg-gauze px-3 py-2">
          <p className="text-xs font-bold text-muted">
            Added later by {a.authorName}, {lagosShort(a.at)}
          </p>
          <p className="mt-0.5 text-sm whitespace-pre-line text-ink">{a.text}</p>
        </div>
      ))}
    </div>
  );
}

function Signed({ by, at }: { by: string; at: string }) {
  return (
    <p className="text-sm text-muted">
      {by}, signed {lagosShort(at)}
    </p>
  );
}

export function ProfileCard({ p }: { p: PortalProfileDto }) {
  const name = [p.firstName, p.middleName, p.lastName].filter(Boolean).join(" ");
  return (
    <Card>
      <dl className="grid gap-3 sm:grid-cols-2">
        <Field label="Name" value={name} />
        <Field label="Date of birth" value={`${formatDay(p.dateOfBirth)} (age ${age(p.dateOfBirth)})`} />
        <Field label="Sex" value={p.sex} />
        <Field label="Blood group" value={p.bloodGroup} />
        <Field label="Genotype" value={p.genotype} />
        <Field
          label="Next of kin"
          value={p.nextOfKinName ? [p.nextOfKinName, p.nextOfKinRelationship, p.nextOfKinPhone].filter(Boolean).join(", ") : null}
        />
      </dl>
      <dl className="mt-4 grid gap-3 border-t border-rule-soft pt-4 sm:grid-cols-2">
        <div>
          <dt className="text-xs font-bold text-muted">Allergies</dt>
          <dd className="mt-1 flex flex-wrap gap-1.5">
            {p.allergies.length === 0 ? (
              <span className="text-sm text-ink">None recorded</span>
            ) : (
              p.allergies.map((a) => (
                <span key={a} className="rounded-[4px] border border-arterial px-1.5 py-0.5 text-sm font-bold text-arterial-dark">
                  {a}
                </span>
              ))
            )}
          </dd>
        </div>
        <div>
          <dt className="text-xs font-bold text-muted">Long-term conditions</dt>
          <dd className="mt-1 text-sm text-ink">{p.conditions.length === 0 ? "None recorded" : p.conditions.join(", ")}</dd>
        </div>
      </dl>
    </Card>
  );
}

function Consultation({ c }: { c: PortalConsultationDto }) {
  return (
    <Card>
      <Signed by={c.doctorName} at={c.signedAt} />
      {c.diagnoses.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-1.5" aria-label="Diagnoses">
          {c.diagnoses.map((d, i) => (
            <li key={i}>
              <Badge tone={d.primary ? "scrub" : "neutral"}>
                {d.text}
                {d.code ? ` (${d.code})` : ""}
              </Badge>
            </li>
          ))}
        </ul>
      )}
      <dl className="mt-3 space-y-3">
        <Field label="Why you came" value={c.complaint} />
        <Field label="History" value={c.history} />
        <Field label="Examination" value={c.examination} />
        <Field label="Plan" value={c.plan} />
      </dl>
      <Addenda list={c.addenda} />
    </Card>
  );
}

function Prescription({ p }: { p: PortalPrescriptionDto }) {
  return (
    <Card>
      <p className="text-sm text-muted">
        {p.doctorName}, {lagosShort(p.createdAt)}
      </p>
      <ul className="mt-2 divide-y divide-rule-soft">
        {p.items.map((i, n) => (
          <li key={n} className="py-2">
            <p className="font-bold text-ink">
              {i.drug}
              {i.strength ? ` ${i.strength}` : ""}
            </p>
            <p className="text-sm text-slate-700">
              {i.dose}, {FREQUENCY_WORDS[i.frequency] ?? i.frequency}
              {i.route ? `, ${i.route}` : ""}
              {i.durationDays ? `, for ${i.durationDays} day${i.durationDays === 1 ? "" : "s"}` : ""}
              {i.quantity ? `. Quantity ${i.quantity}` : ""}
            </p>
            {i.instructions && <p className="mt-0.5 text-sm whitespace-pre-line text-slate-700">{i.instructions}</p>}
          </li>
        ))}
      </ul>
    </Card>
  );
}

export function ResultsList({ results }: { results: PortalResultDto[] }) {
  if (results.length === 0) return <Nothing>No results yet. When the clinic records one, it will show here.</Nothing>;
  return (
    <div className="space-y-3">
      {results.map((t) => (
        <Card key={t.id}>
          <p className="flex flex-wrap items-center gap-2">
            <span className="font-bold text-ink">{t.description}</span>
            <Badge>{t.kind === "Lab" ? "Lab test" : t.kind === "Imaging" ? "Scan or X-ray" : "Test"}</Badge>
          </p>
          <p className="text-sm text-muted">Result {lagosShort(t.resultedAt)}</p>
          {t.resultText && <p className="mt-2 text-sm whitespace-pre-line text-ink">{t.resultText}</p>}
          {t.hasFile && <p className="mt-2 text-sm text-slate-700">There is a result file too. Ask the clinic for a copy.</p>}
        </Card>
      ))}
    </div>
  );
}

function bp(v: PortalVitalsDto) {
  return v.systolic && v.diastolic ? `${v.systolic}/${v.diastolic}` : "—";
}
function num(n: number | null, unit = "") {
  return n === null || n === undefined ? "—" : `${n}${unit}`;
}

function VitalsTable({ vitals }: { vitals: PortalVitalsDto[] }) {
  if (vitals.length === 0) return <Nothing>No vital signs recorded yet.</Nothing>;
  const rows = [...vitals].reverse();
  return (
    <div className="case-sheet overflow-x-auto">
      <table className="w-full min-w-[36rem] text-left text-sm">
        <thead>
          <tr className="border-b border-rule bg-slate-50 text-xs text-muted">
            <th className="case-pad py-2.5 font-bold">Taken</th>
            <th className="px-2 py-2.5 font-bold">Temperature</th>
            <th className="px-2 py-2.5 font-bold">Blood pressure</th>
            <th className="px-2 py-2.5 font-bold">Pulse</th>
            <th className="px-2 py-2.5 font-bold">Breathing</th>
            <th className="px-2 py-2.5 font-bold">Oxygen</th>
            <th className="case-pad py-2.5 font-bold">Weight</th>
          </tr>
        </thead>
        <tbody className="tnum">
          {rows.map((v, i) => (
            <tr key={i} className="border-b border-rule-soft last:border-0">
              <td className="case-pad py-2.5 whitespace-nowrap text-ink">
                {lagosShort(v.takenAt)}
                {v.flags.length > 0 && (
                  <span className="mt-1 flex flex-wrap gap-1">
                    {v.flags.map((f) => (
                      <Badge key={f} tone="iodine">
                        {f}
                      </Badge>
                    ))}
                  </span>
                )}
              </td>
              <td className="px-2 py-2.5">{num(v.temperatureC, " °C")}</td>
              <td className="px-2 py-2.5">{bp(v)}</td>
              <td className="px-2 py-2.5">{num(v.pulse, " /min")}</td>
              <td className="px-2 py-2.5">{num(v.respRate, " /min")}</td>
              <td className="px-2 py-2.5">{num(v.spO2, "%")}</td>
              <td className="case-pad py-2.5">{num(v.weightKg, " kg")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Stay({ a, rounds, summaries }: { a: PortalAdmissionDto; rounds: PortalWardRoundDto[]; summaries: PortalDischargeSummaryDto[] }) {
  return (
    <Card>
      <p className="flex flex-wrap items-center gap-2">
        <span className="font-bold text-ink">
          {lagosShort(a.admittedAt)}
          {a.dischargedAt ? ` to ${lagosShort(a.dischargedAt)}` : ""}
        </span>
        {a.status === "Admitted" ? <Badge tone="scrub">In hospital</Badge> : a.dischargeType ? <Badge>{DISCHARGE[a.dischargeType]}</Badge> : null}
      </p>
      <p className="text-sm text-muted">{[a.wardName, a.doctorName].filter(Boolean).join(", ")}</p>
      <dl className="mt-3 space-y-3">
        <Field label="Why admitted" value={a.reason} />
        <Field label="Diagnosis on admission" value={a.admittingDiagnosis} />
      </dl>
      {summaries.map((s) => (
        <div key={s.id} className="mt-4 border-t border-rule-soft pt-3">
          <p className="font-bold text-ink">Discharge summary</p>
          <Signed by={s.doctorName} at={s.signedAt} />
          <dl className="mt-2 space-y-3">
            <Field label="Diagnoses" value={s.diagnoses} />
            <Field label="Treatment" value={s.treatment} />
            <Field label="Condition on leaving" value={s.condition} />
            <Field label="To take home" value={s.takeHome} />
            <Field label="Follow-up" value={s.followUp ? formatDay(s.followUp) : null} />
          </dl>
          <Addenda list={s.addenda} />
        </div>
      ))}
      {rounds.length > 0 && (
        <div className="mt-4 border-t border-rule-soft pt-3">
          <p className="font-bold text-ink">Ward rounds</p>
          <ul className="mt-2 space-y-3">
            {rounds.map((r) => (
              <li key={r.id}>
                <Signed by={r.doctorName} at={r.signedAt} />
                {r.note && <p className="mt-1 text-sm whitespace-pre-line text-ink">{r.note}</p>}
                <Addenda list={r.addenda} />
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  );
}

const SECTIONS = [
  { id: "details", label: "Details" },
  { id: "visits", label: "Visits" },
  { id: "prescriptions", label: "Prescriptions" },
  { id: "results", label: "Results" },
  { id: "vitals", label: "Vital signs" },
  { id: "stays", label: "Hospital stays" },
];

/** The whole record, newest first within each section. */
export function RecordView({ record }: { record: PortalRecordDto }) {
  const byAdmission = <T extends { admissionId: string }>(list: T[], id: string) => list.filter((x) => x.admissionId === id);
  return (
    <div className="space-y-7">
      <nav aria-label="Sections" className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:px-0">
        {SECTIONS.map((s) => (
          <a
            key={s.id}
            href={`#${s.id}`}
            className={cx("inline-flex min-h-9 shrink-0 items-center rounded-full border border-rule bg-surface px-3 text-sm font-semibold text-ink hover:border-scrub-line")}
          >
            {s.label}
          </a>
        ))}
      </nav>
      <Section title="Details" id="details">
        <ProfileCard p={record.patient} />
      </Section>
      <Section title="Visits" id="visits">
        {record.consultations.length === 0 ? (
          <Nothing>No signed visit notes yet. A note shows here once the doctor signs it.</Nothing>
        ) : (
          <div className="space-y-3">
            {record.consultations.map((c) => (
              <Consultation key={c.id} c={c} />
            ))}
          </div>
        )}
      </Section>
      <Section title="Prescriptions" id="prescriptions">
        {record.prescriptions.length === 0 ? (
          <Nothing>No prescriptions yet.</Nothing>
        ) : (
          <div className="space-y-3">
            {[...record.prescriptions].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).map((p) => (
              <Prescription key={p.id} p={p} />
            ))}
          </div>
        )}
      </Section>
      <Section title="Results" id="results">
        <ResultsList results={[...record.results].sort((a, b) => (b.resultedAt ?? "").localeCompare(a.resultedAt ?? ""))} />
      </Section>
      <Section title="Vital signs" id="vitals">
        <VitalsTable vitals={record.vitals} />
      </Section>
      <Section title="Hospital stays" id="stays">
        {record.admissions.length === 0 ? (
          <Nothing>No hospital stays.</Nothing>
        ) : (
          <div className="space-y-3">
            {[...record.admissions].sort((a, b) => b.admittedAt.localeCompare(a.admittedAt)).map((a) => (
              <Stay key={a.id} a={a} rounds={byAdmission(record.wardRounds, a.id)} summaries={byAdmission(record.dischargeSummaries, a.id)} />
            ))}
          </div>
        )}
      </Section>
    </div>
  );
}
