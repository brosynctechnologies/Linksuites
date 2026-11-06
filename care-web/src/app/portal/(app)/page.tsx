"use client";

import Link from "next/link";
import { useState } from "react";
import { BillIcon, CalendarIcon, NoteIcon, ShieldIcon } from "@/components/icons";
import { PortalError, StatusBadge, APPOINTMENT_STATUS, lagosClock, lagosDate } from "@/components/portal-parts";
import { Card, Skeleton, buttonClass, cx, linkClass } from "@/components/ui";
import { money } from "@/lib/format";
import { portalHref, usePatientApi, usePortal, whose } from "@/lib/portal";
import { useApi } from "@/lib/use-api";
import type { PortalAppointmentsDto, PortalBillDto } from "@/lib/types/portal";

/** The portal home for the chosen patient: the next appointment, what is owed, and the way into the record. */
export default function PortalHome() {
  const { patient } = usePortal();
  const appointments = useApi<PortalAppointmentsDto>(usePatientApi("/appointments"));
  const bills = useApi<PortalBillDto[]>(usePatientApi("/bills"));
  const [now] = useState(Date.now);
  if (!patient) return null;
  const id = patient.id;
  const next = (appointments.data?.appointments ?? [])
    .filter((a) => a.status === "Booked" && new Date(a.start).getTime() > now)
    .sort((a, b) => a.start.localeCompare(b.start))[0];
  const owed = (bills.data ?? []).reduce((sum, b) => sum + Math.max(0, b.balanceMinor), 0);
  const unpaid = (bills.data ?? []).filter((b) => b.balanceMinor > 0).length;

  return (
    <div className="space-y-5">
      <h1 className="display text-2xl font-bold text-ink">{patient.relationship === "Self" ? `Hello, ${patient.firstName}` : `${patient.firstName}'s portal`}</h1>

      <div className="grid gap-4 sm:grid-cols-2">
        <Card>
          <p className="flex items-center gap-2 text-sm font-bold text-muted">
            <CalendarIcon className="h-4 w-4" /> Next appointment
          </p>
          {appointments.error ? (
            <div className="mt-3">
              <PortalError error={appointments.error} onRetry={appointments.reload} />
            </div>
          ) : !appointments.data ? (
            <Skeleton className="mt-3 h-12" />
          ) : next ? (
            <div className="mt-2">
              <p className="display text-xl font-bold text-ink">{lagosDate(next.start)}</p>
              <p className="tnum text-sm text-slate-700">
                {lagosClock(next.start)} with {next.doctorName} <StatusBadge map={APPOINTMENT_STATUS} value={next.status} />
              </p>
            </div>
          ) : (
            <p className="mt-2 text-sm text-slate-700">Nothing booked.</p>
          )}
          {/* Final fix pass (Task 20 carry): no booking for a patient recorded as deceased (the API refuses it too). */}
          <Link href={portalHref("/portal/appointments", id)} className={cx(linkClass, "mt-3 inline-flex min-h-11 items-center text-sm")}>
            {next || patient.status === "Deceased" ? "See appointments" : "Book an appointment"}
          </Link>
        </Card>

        <Card>
          <p className="flex items-center gap-2 text-sm font-bold text-muted">
            <BillIcon className="h-4 w-4" /> Bills
          </p>
          {bills.error ? (
            <div className="mt-3">
              <PortalError error={bills.error} onRetry={bills.reload} />
            </div>
          ) : !bills.data ? (
            <Skeleton className="mt-3 h-12" />
          ) : owed > 0 ? (
            <div className="mt-2">
              <p className="display tnum text-xl font-bold text-ink">{money(owed)} to pay</p>
              <p className="text-sm text-slate-700">{unpaid === 1 ? "On one bill." : `Across ${unpaid} bills.`}</p>
            </div>
          ) : (
            <p className="mt-2 text-sm text-slate-700">Nothing to pay.</p>
          )}
          <Link href={portalHref("/portal/bills", id)} className={cx(owed > 0 ? buttonClass("primary", "mt-3") : cx(linkClass, "mt-3 inline-flex min-h-11 items-center text-sm"))}>
            {owed > 0 ? "See and pay bills" : "See bills and receipts"}
          </Link>
        </Card>
      </div>

      <div className="case-sheet">
        <ul className="case-rows">
          <li>
            <Link href={portalHref("/portal/record", id)} className="case-pad flex min-h-14 items-center gap-3 py-3 hover:bg-slate-50">
              <NoteIcon className="h-5 w-5 shrink-0 text-scrub" />
              <span className="min-w-0">
                <span className="block font-bold text-ink">{whose(patient, "record")}</span>
                <span className="block text-sm text-muted">Signed notes, prescriptions, results, vital signs and hospital stays.</span>
              </span>
            </Link>
          </li>
          <li>
            <Link href={portalHref("/portal/access-log", id)} className="case-pad flex min-h-14 items-center gap-3 py-3 hover:bg-slate-50">
              <ShieldIcon className="h-5 w-5 shrink-0 text-scrub" />
              <span className="min-w-0">
                <span className="block font-bold text-ink">Who opened the record</span>
                <span className="block text-sm text-muted">Every time clinic staff open it, with their name and role.</span>
              </span>
            </Link>
          </li>
        </ul>
      </div>
    </div>
  );
}
