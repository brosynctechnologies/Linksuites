"use client";

import Link from "next/link";
import { apiUrl, asList } from "@/lib/api";
import { formatDay } from "@/lib/format";
import { useApi } from "@/lib/use-api";
import type { MyCertificateDto } from "@/lib/types";
import { AwardIcon, DownloadIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, buttonClass } from "@/components/ui";
import { CopyButton } from "@/components/copy-button";

function verifyHref(c: MyCertificateDto) {
  return `/certificates/${encodeURIComponent(c.code)}`;
}

export default function MyCertificatesPage() {
  const { data, error, reload } = useApi<MyCertificateDto[]>("/api/learn/certificates");
  const list = asList(data);

  return (
    <div>
      <PageHeader title="Certificates" back="/learn" subtitle="Share the verify link: anyone can check it's genuine." />
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<AwardIcon />}
          title="No certificates yet"
          text="Finish every lesson and pass each quiz in a course. If the academy offers certificates, yours appears here."
          action={
            <Link href="/learn" className={buttonClass("primary")}>
              Back to My learning
            </Link>
          }
        />
      )}
      {list.length > 0 && (
        <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
          {list.map((c) => (
            <li key={c.code} className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:p-5">
              <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-lime text-white" aria-hidden="true">
                <AwardIcon className="h-5 w-5" />
              </span>
              <div className="min-w-0 flex-1">
                <p className="font-serif text-[1.0625rem] leading-snug font-semibold text-ink">{c.courseTitle}</p>
                <p className="text-sm text-muted">
                  Issued {formatDay(c.issuedAt)}. Code <span className="font-mono">{c.code}</span>
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <a href={apiUrl(c.pdfUrl || `/api/certificates/${encodeURIComponent(c.code)}/pdf`)} target="_blank" rel="noopener noreferrer" className={buttonClass("secondary")}>
                  <DownloadIcon className="h-4 w-4" /> PDF
                </a>
                <Link href={verifyHref(c)} className={buttonClass("secondary")}>
                  Verify page
                </Link>
                <CopyButton text={c.verifyUrl || (typeof window !== "undefined" ? `${window.location.origin}${verifyHref(c)}` : verifyHref(c))} label="Copy link" />
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
