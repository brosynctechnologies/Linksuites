"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { ImportDialog } from "@/components/import-dialog";
import { useCare } from "@/lib/care-me";
import { age, formatDay } from "@/lib/format";
import { qs, useApi } from "@/lib/use-api";
import { useDebounced } from "@/lib/use-debounced";
import type { PatientSummaryDto } from "@/lib/types";
import { PatientsIcon } from "@/components/icons";
import { ApiErrorView, CaseSheet, EmptyState, PageHeader, PageLoader, PatientStatusBadge, SearchField, Wristband, buttonClass, caseRowClass } from "@/components/ui";

/** The API returns at most this many matches. */
const SEARCH_LIMIT = 50;

/** Find a patient by name, number or phone. Everyone reads demographics; front desk and above register and import. */
export default function PatientsPage() {
  const { access } = useCare();
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const [q, setQ] = useState(params.get("q") ?? "");
  const term = useDebounced(q.trim(), 300);
  const list = useApi<PatientSummaryDto[]>(`/api/care/patients${qs({ q: term })}`);

  function change(v: string) {
    setQ(v);
    // Keep the search in the address so Back returns to the same list (names and numbers only, never clinical text).
    router.replace(v.trim() ? `${pathname}?q=${encodeURIComponent(v.trim())}` : pathname, { scroll: false });
  }

  const rows = list.data ?? [];
  return (
    <div className="space-y-5">
      <PageHeader
        title="Patients"
        subtitle="Search by name, patient number or phone."
        action={
          access.canRegister && (
            <div className="flex flex-wrap items-start gap-2">
              <ImportDialog kind="care-patients" title="Import patients" onDone={list.reload} />
              <Link href="/app/patients/new" className={buttonClass("primary")}>
                Register a patient
              </Link>
            </div>
          )
        }
      />
      <SearchField id="patient-search" label="Search patients" placeholder="Name, patient number or phone" value={q} onChange={change} className="max-w-xl" />

      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && rows.length === 0 && (
        <EmptyState
          icon={<PatientsIcon />}
          title={term ? "No patient matches" : "No patients yet"}
          text={
            term
              ? "Check the spelling, or search by phone or patient number instead."
              : access.canRegister
                ? "Register your first patient, or import your existing patients from Excel."
                : "Patients appear here once the front desk registers them."
          }
          action={
            access.canRegister && (
              <Link href="/app/patients/new" className={buttonClass("primary")}>
                Register a patient
              </Link>
            )
          }
        />
      )}
      {rows.length > 0 && (
        <>
          {rows.length >= SEARCH_LIMIT && (
            <p className="text-sm text-muted">Showing the first {SEARCH_LIMIT}. Type more of the name, or the number or phone, to narrow it down.</p>
          )}
          <CaseSheet
            label="Patients"
            busy={list.loading}
            template="9.5rem minmax(0,1fr) 9rem 9rem"
            head={[{ label: "Number" }, { label: "Name" }, { label: "Born" }, { label: "Phone" }]}
          >
            {rows.map((p) => (
              <li key={p.id}>
                <Link href={`/app/patients/${p.id}`} className={`${caseRowClass} hover:bg-slate-50`}>
                  <span className="order-2 md:order-none">
                    <Wristband number={p.number} />
                  </span>
                  <span className="order-1 min-w-0 md:order-none">
                    <span className="flex flex-wrap items-center gap-2 font-bold text-ink">
                      {p.fullName} <PatientStatusBadge status={p.status} />
                    </span>
                    <span className="block text-xs text-muted md:hidden">
                      {p.sex}, {age(p.dateOfBirth)}
                      {p.phone ? `, ${p.phone}` : ""}
                    </span>
                    <span className="hidden text-xs text-muted md:block">{p.sex}</span>
                  </span>
                  <span className="tnum hidden text-sm text-slate-700 md:block">
                    {formatDay(p.dateOfBirth)} <span className="text-muted">({age(p.dateOfBirth)})</span>
                  </span>
                  <span className="tnum hidden text-sm text-slate-700 md:block">{p.phone ?? "—"}</span>
                </Link>
              </li>
            ))}
          </CaseSheet>
        </>
      )}
    </div>
  );
}
