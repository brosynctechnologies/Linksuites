"use client";

import Link from "next/link";
import { useState } from "react";
import { useCare } from "@/lib/care-me";
import { canWriteNotes } from "@/lib/clinical";
import { formatStamp } from "@/lib/format";
import { dischargeLabel } from "@/lib/inpatient";
import { canTakeMoney } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { AdmissionListItemDto, AdmissionStatus, AdmissionSummaryDto, WardDto } from "@/lib/types/inpatient";
import { AssignBedDialog, TransferDialog } from "@/components/bed-dialogs";
import { WardIcon } from "@/components/icons";
import {
  ApiErrorView,
  Badge,
  Button,
  CaseSheet,
  EmptyState,
  NoAccess,
  PageHeader,
  PageLoader,
  Segmented,
  Select,
  Wristband,
  buttonClass,
  caseRowClass,
} from "@/components/ui";

type Show = "Admitted" | "Discharged" | "all";

/**
 * The ward list: patients admitted now and those discharged in the last 30 days (`GET /api/care/admissions`). Front desk, clinical staff and
 * leadership (Task 10 ruling I3); the cashier works from the bed board and bills. Demographics, ward, bed and dates for everyone; how the
 * patient left and the summary's state come from the API for clinical staff only. Clinical staff open the admission.
 */
export default function AdmissionsPage() {
  const { access } = useCare();
  const allowed = !access.isCashier;
  const [show, setShow] = useState<Show>("Admitted");
  const [wardId, setWardId] = useState("");
  const list = useApi<AdmissionListItemDto[]>(allowed ? `/api/care/admissions${qs({ status: show === "all" ? null : (show as AdmissionStatus) })}` : null);
  const wards = useApi<WardDto[]>(allowed ? "/api/care/wards" : null);
  const [assigning, setAssigning] = useState<AdmissionSummaryDto | null>(null);
  const [moving, setMoving] = useState<AdmissionSummaryDto | null>(null);

  if (!allowed) return <NoAccess text="The ward list is for the front desk, nurses, doctors and leadership." />;
  const rows = (list.data ?? []).filter((a) => !wardId || a.wardId === wardId);
  const desk = !access.readOnly;

  return (
    <div className="space-y-5">
      <PageHeader
        title="Inpatients"
        subtitle="Admitted now, and discharged in the last 30 days."
        action={
          <>
            <Link href="/app/beds" className={buttonClass("secondary")}>
              Bed board
            </Link>
            {canWriteNotes(access) && !access.readOnly && (
              <Link href="/app/admit" className={buttonClass("primary")}>
                Admit a patient
              </Link>
            )}
          </>
        }
      />
      <div className="flex flex-wrap items-end gap-3">
        <Segmented
          label="Which admissions"
          className="w-full max-w-sm"
          value={show}
          onChange={setShow}
          items={[
            { value: "Admitted", label: "Admitted" },
            { value: "Discharged", label: "Discharged" },
            { value: "all", label: "All" },
          ]}
        />
        <div className="w-full max-w-56">
          <Select label="Ward" value={wardId} onChange={(e) => setWardId(e.target.value)}>
            <option value="">All wards</option>
            {(wards.data ?? []).map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
              </option>
            ))}
          </Select>
        </div>
      </div>
      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {list.loading && !list.data && <PageLoader />}
      {list.data && rows.length === 0 && <EmptyState icon={<WardIcon />} title="No one here" text="No admission matches these filters." />}
      {rows.length > 0 && (
        <CaseSheet
          busy={list.loading}
          label="Admissions"
          template="minmax(0,2fr) minmax(0,1.2fr) minmax(0,1.2fr) auto"
          head={[{ label: "Patient" }, { label: "Ward and bed" }, { label: "Dates" }, { label: "", align: "right" }]}
        >
          {rows.map((a) => (
            <li key={a.id} className={caseRowClass}>
              <div className="col-span-2 min-w-0 md:col-span-1">
                <div className="flex flex-wrap items-center gap-2">
                  <Wristband number={a.patientNumber} />
                  <span className="font-bold text-ink">{a.patientName}</span>
                </div>
                <p className="tnum text-sm text-muted">
                  {a.sex}, {a.ageYears}. {a.doctorName ?? ""}
                </p>
              </div>
              <div className="text-sm">
                {a.bedLabel ? (
                  <span className="text-ink">
                    {a.wardName}, {a.bedLabel}
                  </span>
                ) : a.status === "Admitted" ? (
                  <Badge tone="iodine">Waiting for a bed</Badge>
                ) : (
                  <span className="text-muted">No bed</span>
                )}
              </div>
              <div className="tnum text-sm text-slate-700">
                <p>In {formatStamp(a.admittedAt)}</p>
                {a.dischargedAt && (
                  <p>
                    Out {formatStamp(a.dischargedAt)}
                    {access.isClinical && a.dischargeType && `, ${dischargeLabel(a.dischargeType).toLowerCase()}`}
                  </p>
                )}
                {access.isClinical && a.dischargeSummaryStatus && (
                  <Badge tone={a.dischargeSummaryStatus === "Signed" ? "pass" : "iodine"}>Summary {a.dischargeSummaryStatus === "Signed" ? "signed" : "draft"}</Badge>
                )}
              </div>
              <div className="col-span-2 flex flex-wrap justify-end gap-1 md:col-span-1">
                {access.isClinical && (
                  <Link href={`/app/admissions/${a.id}`} className={buttonClass("secondary", "min-h-9 px-2.5")}>
                    Open
                  </Link>
                )}
                {desk && a.status === "Admitted" && !a.bedId && (
                  <Button className="min-h-9 px-2.5" onClick={() => setAssigning(a)}>
                    Assign a bed
                  </Button>
                )}
                {desk && a.status === "Admitted" && a.bedId && (
                  <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => setMoving(a)}>
                    Transfer
                  </Button>
                )}
                {canTakeMoney(access) && a.billId && (
                  <Link href={`/app/bills/${a.billId}`} className={buttonClass("ghost", "min-h-9 px-2.5")}>
                    Bill
                  </Link>
                )}
              </div>
            </li>
          ))}
        </CaseSheet>
      )}
      {assigning && (
        <AssignBedDialog
          admission={assigning}
          onClose={() => {
            setAssigning(null);
            list.reload();
          }}
          onDone={() => list.reload()}
        />
      )}
      {moving && (
        <TransferDialog
          admission={moving}
          onClose={() => setMoving(null)}
          onDone={() => {
            setMoving(null);
            list.reload();
          }}
        />
      )}
    </div>
  );
}
