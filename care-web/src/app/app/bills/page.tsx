"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { useCare } from "@/lib/care-me";
import { formatStamp, money } from "@/lib/format";
import { BILL_STATUS } from "@/lib/inpatient";
import { canTakeMoney } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { PatientSummaryDto } from "@/lib/types";
import type { BillSummaryDto } from "@/lib/types/billing";
import type { BillStatus, QueueDto } from "@/lib/types/clinical";
import { BillIcon } from "@/components/icons";
import { PatientPicker } from "@/components/patient-picker";
import {
  ApiErrorView,
  Badge,
  Button,
  CaseSheet,
  EmptyState,
  NoAccess,
  PageHeader,
  PageLoader,
  SectionTitle,
  Wristband,
  caseRowClass,
  chipClass,
  cx,
} from "@/components/ui";

const FILTERS: { value: BillStatus | "all"; label: string }[] = [
  { value: "all", label: "All" },
  { value: "Unpaid", label: "Unpaid" },
  { value: "PartPaid", label: "Part paid" },
  { value: "InCredit", label: "In credit" },
  { value: "Paid", label: "Paid" },
  { value: "Waived", label: "Waived" },
];

/**
 * Bills (the cashier and leadership; the support view reads). Today's visits waiting to pay come first, from the queue; then the bills,
 * newest first (the API's latest 200), by status or for one patient. Nothing clinical: lines show the price-item name only.
 */
export default function BillsPage() {
  const { access } = useCare();
  const allowed = canTakeMoney(access);
  const [status, setStatus] = useState<BillStatus | "all">("all");
  const params = useSearchParams();
  // A patient id may arrive in the address (from the debtors report): ids only, never a name.
  const [patient, setPatient] = useState<Pick<PatientSummaryDto, "id" | "number" | "fullName"> | null>(() => {
    const pid = params.get("patient");
    return pid && /^[0-9a-f-]{36}$/i.test(pid) ? { id: pid, number: "", fullName: "" } : null;
  });
  const [picking, setPicking] = useState(false);
  const bills = useApi<BillSummaryDto[]>(allowed ? `/api/care/bills${qs({ status: status === "all" ? null : status, patientId: patient?.id })}` : null);
  const queue = useApi<QueueDto>(allowed ? "/api/care/queue?mine=false" : null);

  if (!allowed) return <NoAccess text="Bills are for the cashier, admins and the medical director." />;
  const waiting = queue.data?.stages.find((s) => s.stage === "WaitingPayment")?.visits ?? [];

  return (
    <div className="space-y-6">
      <PageHeader title="Bills" subtitle="Take payments, print receipts, and close visits once they are paid." />

      <section aria-labelledby="waiting-pay">
        <SectionTitle id="waiting-pay" action={<Button variant="ghost" onClick={queue.reload}>Refresh</Button>}>
          Waiting to pay today
        </SectionTitle>
        {queue.error && <ApiErrorView error={queue.error} onRetry={queue.reload} />}
        {queue.data && waiting.length === 0 && <p className="text-sm text-muted">No one is waiting to pay.</p>}
        {waiting.length > 0 && (
          <ul className="divide-y divide-rule-soft rounded-panel border border-rule bg-surface">
            {waiting.map((v) => (
              <li key={v.id} className="case-pad flex flex-wrap items-center gap-x-3 gap-y-2 py-2.5">
                <Wristband number={v.patientNumber} />
                <span className="font-bold text-ink">{v.patientName}</span>
                <span className="text-sm text-muted">
                  {v.doctorName ? `Seen by ${v.doctorName}. ` : ""}Waiting {v.minutesWaited} min
                </span>
                {v.billId ? (
                  <Link href={`/app/bills/${v.billId}`} className="ml-auto font-bold text-scrub hover:underline">
                    Open the bill
                  </Link>
                ) : (
                  <span className="ml-auto text-sm text-muted">No bill</span>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="all-bills" className="space-y-3">
        <SectionTitle id="all-bills">All bills</SectionTitle>
        <div className="no-scrollbar -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0" role="group" aria-label="Status">
          {FILTERS.map((f) => (
            <button key={f.value} type="button" aria-pressed={status === f.value} onClick={() => setStatus(f.value)} className={chipClass(status === f.value)}>
              {f.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          {patient ? (
            <>
              <span className="text-muted">For</span>
              {(patient.number || bills.data?.[0]?.patientNumber) && <Wristband number={patient.number || bills.data![0].patientNumber} />}
              <span className="font-bold text-ink">{patient.fullName || bills.data?.[0]?.patientName || "one patient"}</span>
              <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => setPatient(null)}>
                Show every patient
              </Button>
            </>
          ) : picking ? (
            <div className="w-full max-w-xl">
              <PatientPicker
                label="Find a patient's bills"
                onPick={(p) => {
                  setPatient(p);
                  setPicking(false);
                }}
              />
            </div>
          ) : (
            <Button variant="secondary" onClick={() => setPicking(true)}>
              One patient&apos;s bills
            </Button>
          )}
        </div>
        {bills.error && <ApiErrorView error={bills.error} onRetry={bills.reload} />}
        {bills.loading && !bills.data && <PageLoader />}
        {bills.data && bills.data.length === 0 && <EmptyState icon={<BillIcon />} title="No bills" text="No bill matches these filters." />}
        {bills.data && bills.data.length > 0 && (
          <CaseSheet
            busy={bills.loading}
            label="Bills"
            template="minmax(0,1.1fr) minmax(0,2fr) minmax(0,1fr) minmax(0,1fr) auto"
            head={[{ label: "Bill" }, { label: "Patient" }, { label: "Total", align: "right" }, { label: "Balance", align: "right" }, { label: "Status" }]}
          >
            {bills.data.map((b) => (
              <li key={b.id}>
                <Link href={`/app/bills/${b.id}`} className={cx(caseRowClass, "hover:bg-slate-50")}>
                  <span className="min-w-0">
                    <span className="tnum block font-bold text-ink">{b.number}</span>
                    <span className="block text-xs text-muted">
                      {b.admissionId ? "Admission" : "Visit"}, {formatStamp(b.createdAt)}
                      {b.finalisedAt && ", final"}
                    </span>
                  </span>
                  <span className="col-span-2 flex min-w-0 flex-wrap items-center gap-2 md:col-span-1">
                    <Wristband number={b.patientNumber} />
                    <span className="truncate text-sm font-bold text-ink">{b.patientName}</span>
                  </span>
                  <span className="tnum text-sm text-slate-700 md:text-right">{money(b.totalMinor)}</span>
                  <span className="tnum text-sm font-bold text-ink md:text-right">
                    {b.creditMinor > 0 ? <span className="text-scrub">{money(b.creditMinor)} credit</span> : money(b.balanceMinor)}
                  </span>
                  <span className="flex flex-wrap gap-1">
                    <Badge tone={BILL_STATUS[b.status].tone}>{BILL_STATUS[b.status].label}</Badge>
                    {b.needsReview && <Badge tone="arterial">Needs review</Badge>}
                  </span>
                </Link>
              </li>
            ))}
          </CaseSheet>
        )}
        {bills.data && bills.data.length >= 200 && <p className="text-xs text-muted">Showing the latest 200. Choose a status or a patient to narrow it.</p>}
      </section>
    </div>
  );
}
