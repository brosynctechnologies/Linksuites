"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { canWriteNotes } from "@/lib/clinical";
import { formatStamp } from "@/lib/format";
import { useAction } from "@/lib/mutate";
import { canTakeMoney } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { DoctorDto, PatientRecordDto, PrescriptionDto, TestRequestDto } from "@/lib/types/clinical";
import type { AdmissionDto, AdmissionSummaryDto, CareTeamMemberDto } from "@/lib/types/inpatient";
import { AssignBedDialog, TransferDialog } from "@/components/bed-dialogs";
import { PatientBanner, PrescriptionCard, PrescriptionForm, TestRequestForm, TestRow, withResult, type ClinicalRun } from "@/components/clinical-parts";
import { DischargePanel } from "@/components/discharge-panel";
import { EmergencyReason, useClinicalRead } from "@/components/emergency-reason";
import { FluidChart, MedicationChart, NursingNotes } from "@/components/ward-care";
import { WardRounds } from "@/components/ward-rounds";
import {
  ApiErrorView,
  Badge,
  Button,
  Card,
  ErrorBox,
  NoAccess,
  PageHeader,
  PageLoader,
  SectionTitle,
  Select,
  Tabs,
  Wristband,
  buttonClass,
} from "@/components/ui";

type Tab = "medication" | "nursing" | "fluids" | "rounds" | "orders" | "discharge";

/**
 * One admission (clinical staff only). Opening it is a logged read (`GET /api/care/admissions/{id}`; without a care relationship the API
 * asks for a reason, sent in a header and kept in memory). The tabs are the ward's work: the medication chart, nursing notes, the fluid chart,
 * ward rounds, orders (prescriptions and tests on the admission) and the discharge. Each tab's read is its own logged open. Logistics
 * (bed, transfer) and the bill link sit in the header.
 */
export default function AdmissionPage() {
  const { id } = useParams<{ id: string }>();
  const { me, access } = useCare();
  const open = useClinicalRead<AdmissionDto>(access.isClinical ? `/api/care/admissions/${encodeURIComponent(id)}` : null);
  const [tab, setTab] = useState<Tab>("medication");
  const [assigning, setAssigning] = useState(false);
  const [moving, setMoving] = useState(false);
  const { gate, run } = open;

  if (!access.isClinical) return <NoAccess text="Only doctors and nurses can see clinical notes. The bed board and the ward list show where the patient is." />;
  if (gate.kind === "loading") return <PageLoader label="Opening the admission…" />;
  if (gate.kind === "reason") return <EmergencyReason onSubmit={open.submitReason} back="/app/admissions" />;
  if (gate.kind === "error")
    return (
      <div className="space-y-4">
        <PageHeader title="Admission" back="/app/admissions" />
        <ApiErrorView error={gate.error} onRetry={open.reload} />
      </div>
    );

  const a = gate.data;
  const admitted = a.status === "Admitted";
  const writable = !access.readOnly;
  const doctor = canWriteNotes(access);
  const summary: AdmissionSummaryDto = a;

  return (
    <div className="space-y-5">
      {open.dialog}
      <PageHeader
        back="/app/admissions"
        title={
          <span className="flex flex-wrap items-center gap-3">
            {a.patientName}
            {admitted ? <Badge tone="scrub">Admitted</Badge> : <Badge>Discharged</Badge>}
          </span>
        }
        subtitle={
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <Wristband number={a.patientNumber} size="lg" />
            <span className="tnum">
              {a.sex}, {a.ageYears}. {a.bedLabel ? `${a.wardName}, ${a.bedLabel}` : admitted ? "Waiting for a bed" : "No bed"}. Admitted {formatStamp(a.admittedAt)}
            </span>
          </span>
        }
        action={
          <>
            {admitted && writable && !a.bedId && <Button onClick={() => setAssigning(true)}>Assign a bed</Button>}
            {admitted && writable && a.bedId && (
              <Button variant="secondary" onClick={() => setMoving(true)}>
                Transfer
              </Button>
            )}
            <Link href={`/app/patients/${a.patientId}/record`} className={buttonClass("secondary")}>
              Whole record
            </Link>
            {canTakeMoney(access) && a.billId && (
              <Link href={`/app/bills/${a.billId}`} className={buttonClass("secondary")}>
                Bill
              </Link>
            )}
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <Card className="space-y-2">
          <SectionTitle>Admission</SectionTitle>
          <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-[10rem_minmax(0,1fr)]">
            <dt className="text-muted">Reason</dt>
            <dd className="whitespace-pre-wrap text-ink">{a.reason}</dd>
            <dt className="text-muted">Admitting diagnosis</dt>
            <dd className="whitespace-pre-wrap text-ink">{a.admittingDiagnosis}</dd>
            <dt className="text-muted">Admitting doctor</dt>
            <dd className="text-ink">{a.doctorName ?? "—"}</dd>
            {a.depositOverrideBy && (
              <>
                <dt className="text-muted">Deposit</dt>
                <dd className="text-ink">Below the minimum, by the medical director&apos;s override</dd>
              </>
            )}
          </dl>
        </Card>
        <CareTeam admission={a} meId={me.userId} canManage={doctor && writable && (access.isDirector || a.admittingDoctorUserId === me.userId)} />
      </div>

      <Tabs
        label="Ward care"
        idBase="ward"
        value={tab}
        onChange={setTab}
        items={[
          { value: "medication", label: "Medication chart" },
          { value: "nursing", label: "Nursing notes" },
          { value: "fluids", label: "Fluid chart" },
          { value: "rounds", label: "Ward rounds" },
          { value: "orders", label: "Orders" },
          { value: "discharge", label: "Discharge" },
        ]}
      />
      <div role="tabpanel" id={`ward-panel-${tab}`} aria-labelledby={`ward-tab-${tab}`}>
        {tab === "medication" && <MedicationChart admissionId={a.id} run={run} canRecord={admitted && writable} />}
        {tab === "nursing" && <NursingNotes admissionId={a.id} run={run} canWrite={admitted && writable} />}
        {tab === "fluids" && <FluidChart admissionId={a.id} run={run} canWrite={admitted && writable} />}
        {tab === "rounds" && <WardRounds admissionId={a.id} run={run} meId={me.userId} admitted={admitted} canStart={admitted && writable && doctor} readOnly={access.readOnly} />}
        {tab === "orders" && <Orders admission={a} run={run} canOrder={admitted && writable && doctor} canRecord={writable} myName={me.fullName} />}
        {tab === "discharge" && (
          <DischargePanel
            admission={a}
            run={run}
            meId={me.userId}
            isDirector={access.isDirector}
            canDoctor={doctor}
            readOnly={access.readOnly}
            showBill={canTakeMoney(access)}
            onDischarged={open.reload}
          />
        )}
      </div>

      {assigning && (
        <AssignBedDialog
          admission={summary}
          onClose={() => {
            setAssigning(false);
            open.reload();
          }}
          onDone={() => open.reload()}
        />
      )}
      {moving && (
        <TransferDialog
          admission={summary}
          onClose={() => setMoving(false)}
          onDone={() => {
            setMoving(false);
            open.reload();
          }}
        />
      )}
    </div>
  );
}

// ---------- Care team ----------

/**
 * The admission's doctors (`GET /api/care/admissions/{id}/care-team`, names only, Task 19). The admitting doctor or the director adds a
 * covering doctor (a live care relationship, ending like the admitting doctor's) or removes one; the admitting doctor can't be removed.
 */
function CareTeam({ admission, meId, canManage }: { admission: AdmissionDto; meId: string; canManage: boolean }) {
  const team = useApi<CareTeamMemberDto[]>(`/api/care/admissions/${admission.id}/care-team`);
  const doctors = useApi<DoctorDto[]>(canManage ? "/api/care/doctors" : null);
  const [pick, setPick] = useState("");
  const add = useAction();
  const remove = useAction();
  const onTeam = new Set((team.data ?? []).map((m) => m.userId));
  const choices = (doctors.data ?? []).filter((d) => !onTeam.has(d.userId));

  async function addDoctor() {
    const res = await add.run(() => api<CareTeamMemberDto[]>(`/api/care/admissions/${admission.id}/care-team`, { method: "POST", body: { userId: pick } }));
    if (res.ok) {
      setPick("");
      team.setData(res.data);
    }
  }
  async function removeDoctor(m: CareTeamMemberDto) {
    if (!window.confirm(`Take ${m.name} off the care team? They then need a reason to open this record.`)) return;
    const res = await remove.run(() => api(`/api/care/admissions/${admission.id}/care-team/${m.userId}`, { method: "DELETE" }));
    if (res.ok) team.reload();
  }

  return (
    <Card className="space-y-3">
      <SectionTitle>Care team</SectionTitle>
      {team.error && <ApiErrorView error={team.error} onRetry={team.reload} />}
      {team.data && (
        <ul className="space-y-1.5 text-sm">
          {team.data.map((m) => (
            <li key={`${m.userId}-${m.kind}`} className="flex flex-wrap items-center gap-2">
              <span className="font-bold text-ink">
                {m.name}
                {m.userId === meId && " (you)"}
              </span>
              <Badge tone={m.kind === "AdmittingDoctor" ? "scrub" : "neutral"}>{m.kind === "AdmittingDoctor" ? "Admitting" : "Covering"}</Badge>
              {m.endsAt && <span className="text-xs text-muted">until {formatStamp(m.endsAt)}</span>}
              {canManage && m.kind === "CoveringDoctor" && (
                <Button variant="ghost" className="ml-auto min-h-9 px-2.5" loading={remove.busy} onClick={() => removeDoctor(m)}>
                  Remove
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
      <p className="text-xs text-muted">Ward nurses chart without a reason while the patient is in their ward.</p>
      {canManage && admission.status === "Admitted" && (
        <div className="flex flex-wrap items-end gap-2">
          <div className="min-w-48 flex-1">
            <Select label="Add a covering doctor" value={pick} onChange={(e) => setPick(e.target.value)} error={add.error?.field("userId")}>
              <option value="">Choose a doctor</option>
              {choices.map((d) => (
                <option key={d.userId} value={d.userId}>
                  {d.name}
                </option>
              ))}
            </Select>
          </div>
          <Button variant="secondary" loading={add.busy} disabled={!pick} onClick={addDoctor}>
            Add
          </Button>
        </div>
      )}
      <ErrorBox error={add.error && !add.error.field("userId") ? add.error : null} />
      <ErrorBox error={remove.error} />
    </Card>
  );
}

// ---------- Orders ----------

/**
 * Prescriptions and tests on the admission. The list comes from the patient record (a logged open, read only when this tab is opened)
 * filtered to this admission; an admission prescription's doses appear on the medication chart.
 */
function Orders({ admission, run, canOrder, canRecord, myName }: { admission: AdmissionDto; run: ClinicalRun; canOrder: boolean; canRecord: boolean; myName: string }) {
  const [record, setRecord] = useState<{ data?: PatientRecordDto; error?: ApiError } | null>(null);
  const [nonce, setNonce] = useState(0);
  const [prescriptions, setPrescriptions] = useState<PrescriptionDto[]>([]);
  const [tests, setTests] = useState<TestRequestDto[]>([]);
  const [form, setForm] = useState<"rx" | "test" | null>(null);

  useEffect(() => {
    let live = true;
    run((headers) => api<PatientRecordDto>(`/api/care/patients/${admission.patientId}/record`, { headers })).then(
      (data) => {
        if (!live) return;
        setRecord({ data });
        setPrescriptions(data.prescriptions.filter((p) => p.admissionId === admission.id));
        setTests(data.results.filter((t) => t.admissionId === admission.id));
      },
      (e) => live && setRecord({ error: toApiError(e) }),
    );
    return () => {
      live = false;
    };
  }, [run, admission.patientId, admission.id, nonce]);
  const reload = useCallback(() => setNonce((n) => n + 1), []);

  if (!record) return <PageLoader label="Loading orders…" />;
  if (record.error) return <ApiErrorView error={record.error} onRetry={reload} />;
  const latest = record.data?.vitals.length ? record.data.vitals[record.data.vitals.length - 1] : null;

  return (
    <div className="space-y-5">
      {record.data && <PatientBanner patient={record.data.patient} latestVitals={latest} />}
      <section className="space-y-3">
        <SectionTitle
          action={
            canOrder &&
            form !== "rx" && (
              <Button variant="secondary" onClick={() => setForm("rx")}>
                Prescribe
              </Button>
            )
          }
        >
          Prescriptions
        </SectionTitle>
        {form === "rx" && (
          <PrescriptionForm
            patientId={admission.patientId}
            consultationId={null}
            admissionId={admission.id}
            onCancel={() => setForm(null)}
            onCreated={(p) => {
              setPrescriptions((list) => [p, ...list]);
              setForm(null);
            }}
          />
        )}
        {prescriptions.length === 0 && form !== "rx" && <p className="text-sm text-muted">No prescriptions on this admission yet.</p>}
        <div className="space-y-2">
          {prescriptions.map((p) => (
            <PrescriptionCard key={p.id} prescription={p} patientNumber={admission.patientNumber} run={run} />
          ))}
        </div>
        {prescriptions.length > 0 && <p className="text-xs text-muted">Scheduled doses are on the medication chart. As-needed drugs are listed there too.</p>}
      </section>
      <section className="space-y-3">
        <SectionTitle
          action={
            canOrder &&
            form !== "test" && (
              <Button variant="secondary" onClick={() => setForm("test")}>
                Request a test
              </Button>
            )
          }
        >
          Tests
        </SectionTitle>
        {form === "test" && (
          <TestRequestForm
            patientId={admission.patientId}
            consultationId={null}
            admissionId={admission.id}
            onCancel={() => setForm(null)}
            onCreated={(t) => {
              setTests((list) => [t, ...list]);
              setForm(null);
            }}
          />
        )}
        {tests.length === 0 && form !== "test" && <p className="text-sm text-muted">No tests on this admission yet.</p>}
        <ul className="space-y-2">
          {tests.map((t) => (
            <li key={t.id}>
              <TestRow test={t} run={run} canRecord={canRecord} onResulted={(r) => setTests((list) => withResult(list, r, myName))} />
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
