"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { api } from "@/lib/api";
import { reasonHeaders } from "@/lib/care";
import { useAction } from "@/lib/mutate";
import type { AdmissionDto, AdmitRequest } from "@/lib/types/inpatient";
import { REASON_REQUIRED } from "./emergency-reason";
import { ApiErrorView, Button, Notice, Textarea, Wristband } from "./ui";

/**
 * The doctor admits (`POST /api/care/admissions`): the reason and the admitting diagnosis (both encrypted by the API). Without a care
 * relationship with the patient the API answers 409 `emergency_reason_required`; the form then asks for a reason, sent in the X-Care-Reason
 * header (never the address), and the director is told. The bed and deposit are assigned next, on the bed board or the admission page.
 */
export function AdmitForm({
  patient,
  onCancel,
}: {
  patient: { id: string; number: string; fullName: string };
  onCancel: () => void;
}) {
  const router = useRouter();
  const [reason, setReason] = useState("");
  const [diagnosis, setDiagnosis] = useState("");
  const [needsWhy, setNeedsWhy] = useState(false);
  const [why, setWhy] = useState("");
  const { busy, error, run } = useAction();

  async function submit(e: FormEvent) {
    e.preventDefault();
    const body: AdmitRequest = { patientId: patient.id, consultationId: null, reason: reason.trim(), admittingDiagnosis: diagnosis.trim() };
    const res = await run(() => api<AdmissionDto>("/api/care/admissions", { method: "POST", body, headers: needsWhy ? reasonHeaders(why.trim()) : undefined }));
    if (res.ok) router.push(`/app/admissions/${res.data.id}`);
    else if (res.error.code === REASON_REQUIRED) setNeedsWhy(true);
  }

  const shown = error && !(error.code === REASON_REQUIRED && !why) && !["reason", "admittingDiagnosis", "patientId"].some((f) => error.field(f));
  return (
    <form onSubmit={submit} className="space-y-4">
      <p className="flex flex-wrap items-center gap-2 text-sm">
        <Wristband number={patient.number} />
        <span className="font-bold text-ink">{patient.fullName}</span>
      </p>
      <Textarea label="Reason for admission" rows={3} maxLength={1000} required value={reason} onChange={(e) => setReason(e.target.value)} error={error?.field("reason")} />
      <Textarea
        label="Admitting diagnosis"
        rows={2}
        maxLength={500}
        required
        value={diagnosis}
        onChange={(e) => setDiagnosis(e.target.value)}
        error={error?.field("admittingDiagnosis")}
      />
      {needsWhy && (
        <div className="space-y-2 rounded-control border-l-[3px] border-iodine bg-iodine-wash/50 p-3">
          <p className="text-sm text-slate-700">
            You aren&apos;t caring for this patient now. You can still admit them: your reason is logged and the medical director is told.
          </p>
          <Textarea label="Your reason for opening this record" rows={2} maxLength={300} required value={why} onChange={(e) => setWhy(e.target.value)} />
        </div>
      )}
      <ApiErrorView error={shown ? error : null} />
      <Notice>Reason and diagnosis are encrypted and only doctors and nurses see them. The front desk then assigns the bed and takes the deposit.</Notice>
      <div className="flex flex-wrap justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={busy}>
          Admit
        </Button>
      </div>
    </form>
  );
}
