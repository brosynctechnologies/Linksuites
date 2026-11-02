"use client";

import { useEffect, useState } from "react";
import { RequireArea, useQueues } from "@/components/console";
import { FilterSelect } from "@/components/filters";
import { ExternalIcon, FileIcon } from "@/components/icons";
import { ErrorPanel, Facts, Loading, Mono, PageHeader, StatusBadge, cx } from "@/components/ui";
import { Queue } from "@/components/queue";
import { api, apiBlob, toApiError, type ApiError } from "@/lib/api";
import { stamp } from "@/lib/format";
import type { AdminVerificationDto, VerificationDocument } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useUrlFilters } from "@/lib/use-filters";

const KEYS = ["status"] as const;

export default function VerificationsPage() {
  return (
    <RequireArea area="moderation">
      <Verifications />
    </RequireArea>
  );
}

function Verifications() {
  const f = useUrlFilters(KEYS);
  const status = f.values.status || "Pending";
  const { data, error, reload, setData } = useApi<AdminVerificationDto[]>(`/api/admin/property/verifications${qs({ status })}`);
  const { refresh } = useQueues();

  return (
    <>
      <PageHeader title="Verifications" subtitle="Agency identity and business documents. Approval verifies the agency and releases its waiting listings." />
      <Queue
        key={status}
        items={data}
        error={error}
        reload={reload}
        toolbar={
          <FilterSelect
            label="Status"
            value={status}
            onChange={(v) => f.set({ status: v === "Pending" ? "" : v })}
            options={[
              { value: "Pending", label: "Pending" },
              { value: "Approved", label: "Approved" },
              { value: "Rejected", label: "Rejected" },
            ]}
          />
        }
        config={{
          label: "Verifications queue",
          getId: (v) => v.id,
          getTitle: (v) => v.agencyName,
          approveLabel: "Approve",
          rejectLabel: "Reject",
          canDecide: (v) => v.status === "Pending",
          rejectNote: { label: "Note to the agency", prompt: (v) => `Tell ${v.agencyName} what is wrong so they can send new documents.`, required: true },
          decide: async (v, approve, note) => {
            await api(`/api/admin/property/verifications/${v.id}/decision`, { method: "POST", body: { approve, note } });
            setData((xs) => (xs ?? []).filter((x) => x.id !== v.id));
            refresh();
            return approve ? "Verification approved" : "Verification rejected";
          },
          emptyTitle: status === "Pending" ? "No verifications waiting" : `No ${status.toLowerCase()} verifications`,
          emptyText: status === "Pending" ? "Documents agencies send for verification appear here." : undefined,
          renderRow: (v) => (
            <>
              <span className="flex items-center gap-2">
                <StatusBadge status={v.status} />
                <span className="text-xs text-muted">{v.kind}</span>
              </span>
              <span className="mt-1 block truncate font-medium">{v.agencyName}</span>
              <span className="block truncate text-xs text-muted">
                {v.submittedByName} · {stamp(v.createdAt)}
              </span>
            </>
          ),
          renderDetail: (v) => <VerificationDetail key={v.id} v={v} />,
        }}
      />
    </>
  );
}

function VerificationDetail({ v }: { v: AdminVerificationDto }) {
  const [docIndex, setDocIndex] = useState<number | null>(v.documents[0]?.index ?? null);
  const doc = v.documents.find((d) => d.index === docIndex) ?? null;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <h2 className="text-lg font-semibold">{v.agencyName}</h2>
        <StatusBadge status={v.status} />
      </div>
      <Facts
        items={[
          { label: "Kind", value: v.kind },
          { label: "Sent by", value: `${v.submittedByName} (${v.submittedByEmail})` },
          { label: "Sent", value: stamp(v.createdAt) },
          { label: "Licence no.", value: v.licenceNo ? <Mono>{v.licenceNo}</Mono> : "—" },
          { label: "Agency slug", value: <Mono>{v.agencySlug}</Mono> },
          ...(v.note ? [{ label: "Note", value: v.note }] : []),
        ]}
      />
      {v.documents.length === 0 ? (
        <p className="text-sm text-muted">No documents attached.</p>
      ) : (
        <div className="space-y-2">
          <div className="flex flex-wrap gap-2" role="group" aria-label="Documents">
            {v.documents.map((d) => (
              <button
                key={d.index}
                type="button"
                onClick={() => setDocIndex(d.index)}
                aria-pressed={d.index === docIndex}
                className={cx(
                  "inline-flex min-h-10 items-center gap-1.5 rounded-control border px-3 text-sm font-medium",
                  d.index === docIndex ? "border-graphite bg-graphite text-white" : "border-line bg-surface hover:bg-concrete-2",
                )}
              >
                <FileIcon className="h-4 w-4" /> {d.label}
              </button>
            ))}
          </div>
          {doc && <DocumentViewer key={`${v.id}:${doc.index}`} verificationId={v.id} doc={doc} />}
        </div>
      )}
    </div>
  );
}

/** Fetched with the token and shown from a blob URL (never a public link); PDFs in an iframe of the blob. */
function DocumentViewer({ verificationId, doc }: { verificationId: string; doc: VerificationDocument }) {
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  useEffect(() => {
    let objectUrl: string | null = null;
    let live = true;
    apiBlob(`/api/admin/property/verifications/${verificationId}/documents/${doc.index}`).then(
      (blob) => {
        if (!live) return;
        objectUrl = URL.createObjectURL(blob.type ? blob : new Blob([blob], { type: doc.contentType }));
        setUrl(objectUrl);
      },
      (e) => live && setError(toApiError(e)),
    );
    return () => {
      live = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [verificationId, doc.index, doc.contentType]);

  if (error) return <ErrorPanel error={error} />;
  if (!url) return <Loading label={`Opening ${doc.fileName}…`} />;
  const isPdf = doc.contentType === "application/pdf" || doc.fileName.toLowerCase().endsWith(".pdf");
  return (
    <figure className="space-y-2">
      {isPdf ? (
        <iframe src={url} title={`${doc.label}: ${doc.fileName}`} className="h-[70dvh] w-full rounded-control border border-line bg-concrete" />
      ) : (
        <img src={url} alt={`${doc.label} (${doc.fileName})`} className="max-h-[70dvh] w-full rounded-control border border-line bg-concrete object-contain" />
      )}
      <figcaption className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
        <Mono>{doc.fileName}</Mono>
        <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 font-medium text-graphite underline underline-offset-2">
          Open in a new tab <ExternalIcon className="h-3.5 w-3.5" />
        </a>
      </figcaption>
    </figure>
  );
}
