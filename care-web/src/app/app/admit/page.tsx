"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { useCare } from "@/lib/care-me";
import { canWriteNotes } from "@/lib/clinical";
import type { PatientSummaryDto } from "@/lib/types";
import { AdmitForm } from "@/components/admit-form";
import { PatientPicker } from "@/components/patient-picker";
import { Card, NoAccess, PageHeader } from "@/components/ui";

/** Admit a patient: doctors and the medical director. The patient is found by search (kept in the component, never the address). */
export default function AdmitPage() {
  const { access } = useCare();
  const router = useRouter();
  const [patient, setPatient] = useState<PatientSummaryDto | null>(null);
  if (!canWriteNotes(access) || access.readOnly) return <NoAccess text="Only doctors admit patients." />;
  return (
    <div className="space-y-5">
      <PageHeader title="Admit a patient" back="/app/admissions" />
      <Card className="max-w-2xl">
        {patient ? (
          <AdmitForm patient={{ id: patient.id, number: patient.number, fullName: patient.fullName }} onCancel={() => setPatient(null)} />
        ) : (
          <PatientPicker onPick={setPatient} label="Find the patient to admit" />
        )}
      </Card>
      {!patient && (
        <p className="text-sm text-muted">
          You can also admit from the patient&apos;s page.{" "}
          <button type="button" className="font-semibold text-scrub hover:underline" onClick={() => router.push("/app/patients")}>
            Go to patients
          </button>
        </p>
      )}
    </div>
  );
}
