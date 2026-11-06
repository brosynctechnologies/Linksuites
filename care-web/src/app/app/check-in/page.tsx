"use client";

import Link from "next/link";
import { useState } from "react";
import { useCare } from "@/lib/care-me";
import type { PatientSummaryDto } from "@/lib/types";
import { CheckInDialog } from "@/components/check-in";
import { PatientPicker } from "@/components/patient-picker";
import { Card, NoAccess, PageHeader, linkClass } from "@/components/ui";

/** Check-in: find the patient, then check them in against today's appointment or as a walk-in. Front desk and above, never the cashier. */
export default function CheckInPage() {
  const { access } = useCare();
  const [patient, setPatient] = useState<PatientSummaryDto | null>(null);
  if (access.isCashier || access.readOnly) return <NoAccess text="Check-in is for the front desk, clinical staff and leadership." />;

  return (
    <div className="space-y-5">
      <PageHeader title="Check in a patient" back="/app/queue" subtitle="The patient joins today's queue, waiting for vitals." />
      <Card className="max-w-2xl space-y-3">
        <PatientPicker onPick={setPatient} />
        <p className="text-sm text-muted">
          Not registered yet?{" "}
          <Link href="/app/patients/new" className={linkClass}>
            Register the patient
          </Link>{" "}
          first.
        </p>
      </Card>
      {patient && (
        <CheckInDialog
          patient={{ id: patient.id, number: patient.number, fullName: patient.fullName }}
          onClose={() => setPatient(null)}
        />
      )}
    </div>
  );
}
