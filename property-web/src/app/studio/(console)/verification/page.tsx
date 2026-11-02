"use client";

import { useState, type FormEvent } from "react";
import { postForm, toApiError, type ApiError } from "@/lib/api";
import { formatBytes, formatDay } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { VERIFICATION_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { AgencyProfileDto, VerificationDto } from "@/lib/types";
import { VerifiedSeal } from "@/components/badges";
import { FileIcon } from "@/components/icons";
import { ApiErrorView, Button, Card, ErrorBox, Input, Notice, PageHeader, PageLoader, SectionTitle, StatusBadge, labelClass } from "@/components/ui";

const ACCEPT = "image/jpeg,image/png,image/webp,application/pdf";
const MAX = 10 * 1024 * 1024;

export default function VerificationPage() {
  const latest = useApi<VerificationDto | undefined>("/api/property/verification");
  const agency = useApi<AgencyProfileDto>("/api/property/profile/agency");
  const canSubmit = useCan("agency.edit");
  const v = latest.data && typeof latest.data === "object" && "id" in latest.data ? latest.data : null;
  const st = v ? statusOf(VERIFICATION_STATUS, v.status) : null;

  return (
    <div className="space-y-5">
      <PageHeader title="Verification" subtitle="Send your documents once to earn the green Verified seal. Verified agencies' listings go live without waiting for review." />
      <Card>
        <SectionTitle>Status</SectionTitle>
        {latest.loading || agency.loading ? (
          <PageLoader />
        ) : latest.error ? (
          <ApiErrorView error={latest.error} onRetry={latest.reload} />
        ) : agency.data?.verified ? (
          <p className="flex flex-wrap items-center gap-2 text-slate-700">
            <VerifiedSeal verified /> {agency.data.name} is verified{agency.data.verifiedAt ? ` since ${formatDay(agency.data.verifiedAt)}` : ""}.
          </p>
        ) : v && st ? (
          <div className="space-y-2 text-sm text-slate-700">
            <p className="flex flex-wrap items-center gap-2">
              <StatusBadge tone={st.tone}>{st.label}</StatusBadge> Sent {formatDay(v.createdAt)}
              {v.reviewedAt ? `, reviewed ${formatDay(v.reviewedAt)}` : ""}.
            </p>
            {v.note && (
              <Notice tone={v.status === "Rejected" ? "error" : "info"}>
                <span className="font-semibold">Moderator note:</span> {v.note}
              </Notice>
            )}
            {v.documents.length > 0 && (
              <ul className="space-y-1">
                {v.documents.map((d) => (
                  <li key={d.index} className="flex items-center gap-2">
                    <FileIcon className="h-4 w-4 text-muted" /> {d.label}: {d.fileName}
                  </li>
                ))}
              </ul>
            )}
            {v.status === "Pending" && <p>A moderator usually reviews documents within one working day.</p>}
          </div>
        ) : (
          <p className="text-sm text-slate-700">You haven&apos;t sent documents yet. Until you do, each listing waits for a moderator before it goes live.</p>
        )}
      </Card>

      {!agency.data?.verified && v?.status !== "Pending" && (
        <Card>
          <SectionTitle>{v?.status === "Rejected" ? "Send new documents" : "Send documents"}</SectionTitle>
          {canSubmit ? (
            <SubmitForm onDone={() => latest.reload()} />
          ) : (
            <p className="text-sm text-muted">Only the agency owner or an admin can send verification documents.</p>
          )}
        </Card>
      )}
    </div>
  );
}

function FileField({ id, label, hint, file, onFile, required }: { id: string; label: string; hint: string; file: File | null; onFile: (f: File | null) => void; required?: boolean }) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className={labelClass}>
        {label}
      </label>
      <input
        id={id}
        type="file"
        accept={ACCEPT}
        required={required}
        onChange={(e) => onFile(e.target.files?.[0] ?? null)}
        className="block min-h-11 w-full text-sm file:mr-3 file:min-h-11 file:rounded-control file:border file:border-line file:bg-surface file:px-4 file:text-sm file:font-semibold file:text-asphalt"
      />
      <p className="text-xs text-muted">{file ? `${file.name}, ${formatBytes(file.size)}` : hint}</p>
    </div>
  );
}

function SubmitForm({ onDone }: { onDone: () => void }) {
  const [idDoc, setIdDoc] = useState<File | null>(null);
  const [bizDoc, setBizDoc] = useState<File | null>(null);
  const [licenceNo, setLicenceNo] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!idDoc) return setError("Add a photo or scan of your ID.");
    if (idDoc.size > MAX || (bizDoc && bizDoc.size > MAX)) return setError("Each file must be 10 MB or smaller.");
    const fd = new FormData();
    fd.append("idDocument", idDoc);
    if (bizDoc) fd.append("businessDocument", bizDoc);
    if (licenceNo.trim()) fd.append("licenceNo", licenceNo.trim());
    setBusy(true);
    setError(null);
    try {
      await postForm<VerificationDto>("/api/property/verification", fd);
      onDone();
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <FileField id="id-doc" label="Government ID (required)" hint="National ID, passport, driver's licence or voter's card. JPEG, PNG, WebP or PDF, up to 10 MB." file={idDoc} onFile={setIdDoc} required />
      <FileField id="biz-doc" label="Business document (optional)" hint="CAC certificate or another document showing the business name." file={bizDoc} onFile={setBizDoc} />
      <Input label="Licence number (optional)" value={licenceNo} onChange={(e) => setLicenceNo(e.target.value)} hint="ESVARBON or another professional licence, if you have one." />
      <ErrorBox error={typeof error === "string" ? error : error?.status === 409 ? "Your documents are already being reviewed." : error} />
      <Button type="submit" loading={busy}>
        Send for review
      </Button>
    </form>
  );
}
