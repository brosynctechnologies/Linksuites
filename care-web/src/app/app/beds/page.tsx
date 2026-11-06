"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { useCare } from "@/lib/care-me";
import { formatStamp, plural } from "@/lib/format";
import { BED_STATUS, wardKindLabel } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import { canTakeMoney } from "@/lib/roles";
import { useApi } from "@/lib/use-api";
import type { AdmissionSummaryDto, BedBoardBedDto, BedBoardDto } from "@/lib/types/inpatient";
import { AssignBedDialog, TransferDialog } from "@/components/bed-dialogs";
import { BedIcon } from "@/components/icons";
import { ApiErrorView, Badge, Button, EmptyState, PageHeader, PageLoader, SectionTitle, Wristband, buttonClass, cx } from "@/components/ui";

/**
 * The bed board: every ward's active beds (free, occupied, cleaning) with the patient in each (demographics only), and the admissions still
 * waiting for a bed. Every member sees it. Any member but the support view assigns a bed with the deposit (Task 8 ruling); front desk,
 * clinical staff and leadership mark a cleaned bed ready and transfer; clinical staff open the admission; the cashier and leadership open
 * its bill.
 */
export default function BedBoardPage() {
  const { access } = useCare();
  const board = useApi<BedBoardDto>("/api/care/bed-board");
  const [assigning, setAssigning] = useState<AdmissionSummaryDto | null>(null);
  const [moving, setMoving] = useState<AdmissionSummaryDto | null>(null);
  const ready = useAction();
  const desk = !access.isCashier && !access.readOnly;

  async function markReady(bed: BedBoardBedDto) {
    const res = await ready.run(() => api(`/api/care/beds/${bed.id}/ready`, { method: "POST" }));
    if (res.ok) board.reload();
  }

  const data = board.data;
  const totals = data?.wards.flatMap((w) => w.beds) ?? [];
  return (
    <div className="space-y-6">
      <PageHeader
        title="Beds"
        subtitle={
          data &&
          `${plural(totals.filter((b) => b.status === "Free").length, "bed")} free, ${totals.filter((b) => b.status === "Occupied").length} occupied, ${totals.filter((b) => b.status === "Cleaning").length} being cleaned`
        }
        action={
          <>
            <Button variant="secondary" onClick={board.reload}>
              Refresh
            </Button>
            {!access.isCashier && (
              <Link href="/app/admissions" className={buttonClass("secondary")}>
                Inpatients
              </Link>
            )}
            {access.isLeadership && (
              <Link href="/app/wards" className={buttonClass("secondary")}>
                Wards and beds
              </Link>
            )}
          </>
        }
      />
      {board.error && <ApiErrorView error={board.error} onRetry={board.reload} />}
      {!data && !board.error && <PageLoader />}
      <ApiErrorView error={ready.error} />

      {data && data.awaitingBed.length > 0 && (
        <section aria-labelledby="waiting-title" className="rounded-panel border border-iodine/40 bg-iodine-wash/50 p-4">
          <SectionTitle id="waiting-title">Waiting for a bed</SectionTitle>
          <ul className="divide-y divide-rule-soft">
            {data.awaitingBed.map((a) => (
              <li key={a.id} className="flex flex-wrap items-center gap-x-3 gap-y-2 py-2.5">
                <Wristband number={a.patientNumber} />
                <span className="font-bold text-ink">{a.patientName}</span>
                <span className="tnum text-sm text-muted">
                  {a.sex}, {a.ageYears}. Admitted {formatStamp(a.admittedAt)} by {a.doctorName ?? "a doctor"}
                </span>
                <span className="ml-auto flex flex-wrap gap-2">
                  {access.isClinical && (
                    <Link href={`/app/admissions/${a.id}`} className={buttonClass("ghost", "min-h-10")}>
                      Open
                    </Link>
                  )}
                  {!access.readOnly && <Button onClick={() => setAssigning(a)}>Assign a bed</Button>}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {data && data.wards.length === 0 && (
        <EmptyState
          icon={<BedIcon />}
          title="No wards yet"
          text={access.isLeadership ? "Set up your wards and beds to admit patients." : "The medical director or an admin sets up wards and beds."}
          action={
            access.isLeadership && (
              <Link href="/app/wards" className={buttonClass("primary")}>
                Set up wards
              </Link>
            )
          }
        />
      )}

      {data?.wards.map((w) => (
        <section key={w.id} aria-labelledby={`ward-${w.id}`}>
          <SectionTitle id={`ward-${w.id}`}>
            {w.name} <span className="text-sm font-normal text-muted">{wardKindLabel(w.kind)}</span>
          </SectionTitle>
          {w.beds.length === 0 ? (
            <p className="text-sm text-muted">No beds in use in this ward.</p>
          ) : (
            <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {w.beds.map((b) => (
                <li
                  key={b.id}
                  className={cx(
                    "flex min-h-28 flex-col gap-2 rounded-panel border bg-surface p-3",
                    b.status === "Free" ? "border-pass/40" : b.status === "Cleaning" ? "border-iodine/50 bg-iodine-wash/40" : "border-scrub-line",
                  )}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="display tnum text-lg font-bold text-ink">{b.label}</span>
                    <Badge tone={BED_STATUS[b.status].tone}>{BED_STATUS[b.status].label}</Badge>
                  </div>
                  {b.patient ? (
                    <>
                      <div className="space-y-1 text-sm">
                        <Wristband number={b.patient.patientNumber} />
                        <p className="font-bold text-ink">{b.patient.patientName}</p>
                        <p className="tnum text-muted">
                          {b.patient.sex}, {b.patient.ageYears}. {b.patient.doctorName ?? ""}
                        </p>
                      </div>
                      <div className="mt-auto flex flex-wrap gap-1">
                        {access.isClinical && (
                          <Link href={`/app/admissions/${b.patient.id}`} className={buttonClass("secondary", "min-h-9 px-2.5")}>
                            Open
                          </Link>
                        )}
                        {desk && (
                          <Button variant="ghost" className="min-h-9 px-2.5" onClick={() => setMoving(b.patient)}>
                            Transfer
                          </Button>
                        )}
                        {canTakeMoney(access) && b.patient.billId && (
                          <Link href={`/app/bills/${b.patient.billId}`} className={buttonClass("ghost", "min-h-9 px-2.5")}>
                            Bill
                          </Link>
                        )}
                      </div>
                    </>
                  ) : b.status === "Cleaning" ? (
                    desk && (
                      <Button variant="secondary" className="mt-auto min-h-9" loading={ready.busy} onClick={() => markReady(b)}>
                        Mark ready
                      </Button>
                    )
                  ) : (
                    <p className="mt-auto text-sm text-muted">Ready for a patient.</p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </section>
      ))}

      {assigning && (
        <AssignBedDialog
          admission={assigning}
          onClose={() => {
            setAssigning(null);
            board.reload();
          }}
          onDone={() => board.reload()}
        />
      )}
      {moving && (
        <TransferDialog
          admission={moving}
          onClose={() => setMoving(null)}
          onDone={() => {
            setMoving(null);
            board.reload();
          }}
        />
      )}
    </div>
  );
}
