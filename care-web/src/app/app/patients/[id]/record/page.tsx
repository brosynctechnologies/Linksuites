"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState, type ReactNode } from "react";
import { useCare } from "@/lib/care-me";
import { canWriteNotes, stageLabel, urgencyTone } from "@/lib/clinical";
import { formatDay, formatStamp } from "@/lib/format";
import type {
  AddendumDto,
  ConsultationDto,
  PatientRecordDto,
  PrescriptionDto,
  RecordAdmissionDto,
  RecordBillDto,
  RecordDischargeSummaryDto,
  RecordNursingNoteDto,
  RecordVisitDto,
  RecordWardRoundDto,
  TestRequestDto,
  VitalSignsDto,
} from "@/lib/types/clinical";
import { Addenda, PatientBanner, PrescriptionCard, TestRow, VitalsLine, withResult, type ClinicalRun } from "@/components/clinical-parts";
import { EmergencyReason, useClinicalRead } from "@/components/emergency-reason";
import { VitalsChart } from "@/components/vitals-chart";
import { ApiErrorView, Badge, Card, NoAccess, PageHeader, PageLoader, SectionTitle, buttonClass, chipClass, cx } from "@/components/ui";

type Filter = "all" | "notes" | "medicines" | "tests" | "vitals" | "admissions" | "bills";
const FILTERS: { value: Filter; label: string }[] = [
  { value: "all", label: "Everything" },
  { value: "notes", label: "Visits and notes" },
  { value: "medicines", label: "Prescriptions" },
  { value: "tests", label: "Tests" },
  { value: "vitals", label: "Vitals" },
  { value: "admissions", label: "Admissions" },
  { value: "bills", label: "Bills" },
];

type Event =
  | { kind: "visit"; at: string; visit: RecordVisitDto; consultation: ConsultationDto | null }
  | { kind: "prescription"; at: string; prescription: PrescriptionDto }
  | { kind: "test"; at: string; test: TestRequestDto }
  | { kind: "vitals"; at: string; vitals: VitalSignsDto }
  | { kind: "admission"; at: string; admission: RecordAdmissionDto }
  | { kind: "round"; at: string; round: RecordWardRoundDto }
  | { kind: "nursing"; at: string; note: RecordNursingNoteDto }
  | { kind: "discharge"; at: string; summary: RecordDischargeSummaryDto }
  | { kind: "bill"; at: string; bill: RecordBillDto };

const FILTER_OF: Record<Event["kind"], Filter> = {
  visit: "notes",
  prescription: "medicines",
  test: "tests",
  vitals: "vitals",
  admission: "admissions",
  round: "admissions",
  nursing: "admissions",
  discharge: "admissions",
  bill: "bills",
};

function events(r: PatientRecordDto): Event[] {
  const byId = new Map(r.consultations.map((c) => [c.id, c]));
  const list: Event[] = [
    ...r.visits.map((v) => ({ kind: "visit" as const, at: v.arrivedAt, visit: v, consultation: v.consultationId ? (byId.get(v.consultationId) ?? null) : null })),
    ...r.prescriptions.map((p) => ({ kind: "prescription" as const, at: p.createdAt, prescription: p })),
    ...r.results.map((t) => ({ kind: "test" as const, at: t.createdAt, test: t })),
    ...r.vitals.map((v) => ({ kind: "vitals" as const, at: v.takenAt, vitals: v })),
    ...r.admissions.map((a) => ({ kind: "admission" as const, at: a.admittedAt, admission: a })),
    ...r.wardRounds.map((w) => ({ kind: "round" as const, at: w.createdAt, round: w })),
    ...r.nursingNotes.map((n) => ({ kind: "nursing" as const, at: n.at, note: n })),
    ...r.dischargeSummaries.map((s) => ({ kind: "discharge" as const, at: s.createdAt, summary: s })),
    ...r.bills.map((b) => ({ kind: "bill" as const, at: b.createdAt, bill: b })),
  ];
  return list.sort((a, b) => b.at.localeCompare(a.at));
}

const lagosDay = (iso: string) => new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Lagos" }).format(new Date(iso));

/**
 * The patient record (`GET /api/care/patients/{id}/record`): clinical staff only, a logged open, with a reason when the member isn't
 * caring for the patient. The allergy and condition banner first, the vitals trend, the diagnoses, then a newest-first timeline of visits
 * with their notes, prescriptions, tests (results entered here), vitals, admissions and bills.
 */
export default function RecordPage() {
  const { id } = useParams<{ id: string }>();
  const { me, access } = useCare();
  const read = useClinicalRead<PatientRecordDto>(access.isClinical ? `/api/care/patients/${encodeURIComponent(id)}/record` : null);
  const [filter, setFilter] = useState<Filter>("all");
  const back = `/app/patients/${id}`;

  if (!access.isClinical) return <NoAccess text="The clinical record is for doctors and nurses." />;
  const { gate } = read;
  if (gate.kind === "loading") return <PageLoader label="Opening the record…" />;
  if (gate.kind === "reason") return <EmergencyReason onSubmit={read.submitReason} back={back} />;
  if (gate.kind === "error")
    return (
      <div className="space-y-4">
        <PageHeader title="Record" back={back} />
        <ApiErrorView error={gate.error} onRetry={read.reload} />
      </div>
    );

  const r = gate.data;
  const latest = r.vitals.length ? r.vitals[r.vitals.length - 1] : null;
  const medicines = Array.from(new Set(r.prescriptions.slice(0, 5).flatMap((p) => p.items.map((i) => [i.drug, i.strength].filter(Boolean).join(" ")))));
  const all = events(r);
  const shown = filter === "all" ? all : all.filter((e) => FILTER_OF[e.kind] === filter);
  const days: { day: string; items: Event[] }[] = [];
  for (const e of shown) {
    const day = lagosDay(e.at);
    if (days[days.length - 1]?.day !== day) days.push({ day, items: [] });
    days[days.length - 1].items.push(e);
  }

  const addAddendum = (kind: "consultation" | "round" | "discharge", noteId: string, a: AddendumDto) =>
    read.setData((x) => ({
      ...x,
      consultations: kind === "consultation" ? x.consultations.map((c) => (c.id === noteId ? { ...c, addenda: [...c.addenda, a] } : c)) : x.consultations,
      wardRounds: kind === "round" ? x.wardRounds.map((w) => (w.id === noteId ? { ...w, addenda: [...w.addenda, a] } : w)) : x.wardRounds,
      dischargeSummaries:
        kind === "discharge" ? x.dischargeSummaries.map((s) => (s.id === noteId ? { ...s, addenda: [...s.addenda, a] } : s)) : x.dischargeSummaries,
    }));

  const ctx: Ctx = {
    run: read.run,
    readOnly: access.readOnly,
    canOpenConsultation: canWriteNotes(access),
    patientNumber: r.patient.number,
    addAddendum,
    onResulted: (res) => read.setData((x) => ({ ...x, results: withResult(x.results, res, me.fullName) })),
  };

  return (
    <div className="space-y-5">
      {read.dialog}
      <PageHeader
        back={back}
        title="Clinical record"
        action={
          access.isDirector &&
          !access.readOnly && (
            <Link href={`/app/patients/${id}/access-log`} className={buttonClass("secondary")}>
              Who opened this record
            </Link>
          )
        }
      />
      <PatientBanner patient={r.patient} latestVitals={latest} medicines={medicines} />

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <Card>
          <SectionTitle>Vital signs</SectionTitle>
          <VitalsChart vitals={r.vitals} />
        </Card>
        <Card>
          <SectionTitle>Diagnoses</SectionTitle>
          {r.diagnoses.length === 0 ? (
            <p className="text-sm text-muted">None recorded.</p>
          ) : (
            <ul className="space-y-1.5 text-sm">
              {r.diagnoses.slice(0, 20).map((d) => (
                <li key={d.id} className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
                  <span className="text-ink">{d.text}</span>
                  {d.code && <Badge tone="scrub">{d.code}</Badge>}
                  <span className="tnum text-xs text-muted">{formatDay(lagosDay(d.createdAt))}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <section aria-labelledby="timeline-title" className="space-y-3">
        <h2 id="timeline-title" className="font-semibold text-ink">
          Timeline
        </h2>
        <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0">
          {FILTERS.map((f) => (
            <button key={f.value} type="button" aria-pressed={filter === f.value} className={chipClass(filter === f.value, "min-h-9 px-3")} onClick={() => setFilter(f.value)}>
              {f.label}
            </button>
          ))}
        </div>
        {days.length === 0 && <p className="text-sm text-muted">Nothing here yet.</p>}
        <ol className="space-y-5">
          {days.map((d) => (
            <li key={d.day}>
              <h3 className="tnum mb-2 text-sm font-bold text-muted">{formatDay(d.day)}</h3>
              <ol className="space-y-2 border-l-2 border-rule pl-4">
                {d.items.map((e) => (
                  <li key={`${e.kind}-${keyOf(e)}`}>
                    <EventCard event={e} ctx={ctx} />
                  </li>
                ))}
              </ol>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}

function keyOf(e: Event): string {
  switch (e.kind) {
    case "visit":
      return e.visit.id;
    case "prescription":
      return e.prescription.id;
    case "test":
      return e.test.id;
    case "vitals":
      return e.vitals.id;
    case "admission":
      return e.admission.id;
    case "round":
      return e.round.id;
    case "nursing":
      return e.note.id;
    case "discharge":
      return e.summary.id;
    case "bill":
      return e.bill.id;
  }
}

interface Ctx {
  run: ClinicalRun;
  readOnly: boolean;
  canOpenConsultation: boolean;
  patientNumber: string;
  addAddendum: (kind: "consultation" | "round" | "discharge", noteId: string, a: AddendumDto) => void;
  onResulted: Parameters<typeof TestRow>[0]["onResulted"];
}

function Entry({ title, time, badge, children, tone }: { title: ReactNode; time: string; badge?: ReactNode; children?: ReactNode; tone?: "note" }) {
  return (
    <article className={cx("rounded-panel border bg-surface p-3 sm:p-4", tone === "note" ? "border-scrub-line" : "border-rule")}>
      <header className="flex flex-wrap items-center gap-2">
        <h4 className="font-semibold text-ink">{title}</h4>
        {badge}
        <span className="tnum ml-auto text-xs text-muted">{formatStamp(time).split(", ")[1]}</span>
      </header>
      {children && <div className="mt-2 space-y-2 text-sm">{children}</div>}
    </article>
  );
}

function TextBlock({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value) return null;
  return (
    <div>
      <p className="text-xs font-semibold text-muted">{label}</p>
      <p className="whitespace-pre-wrap text-ink">{value}</p>
    </div>
  );
}

function NoteBadge({ signed }: { signed: boolean }) {
  return signed ? <Badge tone="pass">Signed</Badge> : <Badge tone="iodine">Draft</Badge>;
}

function EventCard({ event: e, ctx }: { event: Event; ctx: Ctx }) {
  switch (e.kind) {
    case "visit": {
      const c = e.consultation;
      return (
        <Entry
          tone={c ? "note" : undefined}
          title={`Visit${e.visit.doctorName ? ` with ${e.visit.doctorName}` : ""}`}
          time={e.at}
          badge={
            <>
              <Badge>{stageLabel(e.visit.stage)}</Badge>
              {e.visit.urgency !== "Routine" && <Badge tone={urgencyTone(e.visit.urgency)}>{e.visit.urgency}</Badge>}
              {c && <NoteBadge signed={c.status === "Signed"} />}
            </>
          }
        >
          {c ? (
            <>
              <TextBlock label="Complaint" value={c.complaint} />
              <TextBlock label="History" value={c.history} />
              <TextBlock label="Examination" value={c.examination} />
              {c.diagnoses.length > 0 && (
                <div>
                  <p className="text-xs font-semibold text-muted">Diagnoses</p>
                  <ul className="flex flex-wrap gap-x-3 gap-y-1">
                    {c.diagnoses.map((d) => (
                      <li key={d.id} className="flex items-center gap-1.5 text-ink">
                        {d.text} {d.code && <Badge tone="scrub">{d.code}</Badge>} {d.primary && <Badge>Primary</Badge>}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
              <TextBlock label="Plan" value={c.plan} />
              <p className="text-xs text-muted">{c.status === "Signed" ? `Signed by ${c.doctorName}, ${formatStamp(c.signedAt)}` : `Draft by ${c.doctorName}`}</p>
              {c.status === "Signed" && (
                <Addenda kind="Consultation" noteId={c.id} addenda={c.addenda} run={ctx.run} readOnly={ctx.readOnly} onAdded={(a) => ctx.addAddendum("consultation", c.id, a)} />
              )}
            </>
          ) : (
            <p className="text-muted">No consultation note for this visit.</p>
          )}
          {ctx.canOpenConsultation && e.visit.stage !== "WaitingVitals" && (
            <Link href={`/app/visits/${e.visit.id}/consultation`} className="text-sm font-semibold text-scrub hover:underline">
              Open the consultation
            </Link>
          )}
        </Entry>
      );
    }
    case "prescription":
      return (
        <Entry title={e.prescription.admissionId ? "Prescription (admission)" : "Prescription"} time={e.at}>
          <PrescriptionCard prescription={e.prescription} patientNumber={ctx.patientNumber} run={ctx.run} />
        </Entry>
      );
    case "test":
      return (
        <Entry title="Test" time={e.at}>
          <TestRow test={e.test} run={ctx.run} canRecord={!ctx.readOnly} onResulted={ctx.onResulted} />
        </Entry>
      );
    case "vitals":
      return (
        <Entry title="Vital signs" time={e.at} badge={e.vitals.critical ? <Badge tone="arterial">Critical</Badge> : undefined}>
          <VitalsLine vitals={e.vitals} />
        </Entry>
      );
    case "admission": {
      const a = e.admission;
      return (
        <Entry title={`Admitted${a.wardName ? ` to ${a.wardName}` : ""}${a.bedLabel ? `, bed ${a.bedLabel}` : ""}`} time={e.at} badge={<Badge tone={a.status === "Admitted" ? "scrub" : "neutral"}>{a.status}</Badge>}>
          <TextBlock label="Reason" value={a.reason} />
          <TextBlock label="Admitting diagnosis" value={a.admittingDiagnosis} />
          <p className="text-xs text-muted">
            {a.doctorName && `Admitting doctor ${a.doctorName}. `}
            {a.dischargedAt && `Discharged ${formatStamp(a.dischargedAt)}${a.dischargeType ? ` (${DISCHARGE[a.dischargeType]})` : ""}.`}
          </p>
          <Link href={`/app/admissions/${a.id}`} className="text-sm font-semibold text-scrub underline-offset-2 hover:underline">
            Open the admission
          </Link>
        </Entry>
      );
    }
    case "round":
      return (
        <Entry tone="note" title={`Ward round, ${e.round.doctorName}`} time={e.at} badge={<NoteBadge signed={e.round.status === "Signed"} />}>
          <TextBlock label="Note" value={e.round.note} />
          {e.round.status === "Signed" && (
            <Addenda kind="WardRound" noteId={e.round.id} addenda={e.round.addenda} run={ctx.run} readOnly={ctx.readOnly} onAdded={(a) => ctx.addAddendum("round", e.round.id, a)} />
          )}
        </Entry>
      );
    case "nursing":
      return (
        <Entry title={`Nursing note, ${e.note.authorName}`} time={e.at}>
          <p className="whitespace-pre-wrap text-ink">{e.note.note}</p>
        </Entry>
      );
    case "discharge": {
      const s = e.summary;
      return (
        <Entry tone="note" title={`Discharge summary, ${s.doctorName}`} time={e.at} badge={<NoteBadge signed={s.status === "Signed"} />}>
          <TextBlock label="Diagnoses" value={s.diagnoses} />
          <TextBlock label="Treatment" value={s.treatment} />
          <TextBlock label="Condition at discharge" value={s.condition} />
          <TextBlock label="Take-home medicines" value={s.takeHome} />
          {s.followUp && <p className="text-xs text-muted">Follow-up {formatDay(s.followUp)}</p>}
          {s.status === "Signed" && (
            <Addenda kind="Discharge" noteId={s.id} addenda={s.addenda} run={ctx.run} readOnly={ctx.readOnly} onAdded={(a) => ctx.addAddendum("discharge", s.id, a)} />
          )}
        </Entry>
      );
    }
    case "bill":
      return <Entry title={`Bill ${e.bill.number}`} time={e.at} badge={<Badge tone={BILL[e.bill.status].tone}>{BILL[e.bill.status].label}</Badge>} />;
  }
}

const DISCHARGE: Record<NonNullable<RecordAdmissionDto["dischargeType"]>, string> = {
  Home: "home",
  AgainstAdvice: "against medical advice",
  TransferredOut: "transferred out",
  Died: "died",
};

const BILL: Record<RecordBillDto["status"], { label: string; tone: "pass" | "iodine" | "neutral" | "scrub" }> = {
  Unpaid: { label: "Unpaid", tone: "iodine" },
  PartPaid: { label: "Part paid", tone: "iodine" },
  Paid: { label: "Paid", tone: "pass" },
  Waived: { label: "Waived", tone: "neutral" },
  InCredit: { label: "In credit", tone: "scrub" },
};
