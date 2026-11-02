import type { Metadata } from "next";
import { Suspense } from "react";
import { LoadProblem, SitePage } from "@/components/site-chrome";
import { buttonClass } from "@/components/styles";
import { AwardIcon, DownloadIcon } from "@/components/icons";
import { API_URL, APP_NAME } from "@/lib/config";
import { formatDay } from "@/lib/format";
import { serverGet } from "@/lib/server-api";
import type { CertificateDto } from "@/lib/types";

export const metadata: Metadata = { title: "Verify a certificate" };

/** Public verification page: anyone with the code (an employer, a school) can check a certificate. */
export default function CertificatePage({ params }: PageProps<"/certificates/[code]">) {
  return (
    <SitePage hideSearch>
      <div className="mx-auto max-w-3xl px-4 py-10 sm:px-6 sm:py-14">
        <Suspense fallback={<div className="h-72 animate-pulse rounded-panel bg-mist-2" aria-hidden="true" />}>
          {params.then(({ code }) => (
            <Certificate code={code} />
          ))}
        </Suspense>
      </div>
    </SitePage>
  );
}

async function Certificate({ code }: { code: string }) {
  const res = await serverGet<CertificateDto>(`/api/certificates/${encodeURIComponent(code)}`);
  if (!res.ok && res.status === 404) {
    return (
      <div className="rounded-panel border border-brick/30 bg-surface p-6" role="alert">
        <h1 className="text-xl font-semibold text-brick">No certificate matches this code</h1>
        <p className="mt-2 text-slate-700">
          Check the code <span className="font-mono font-semibold">{code}</span> against the one printed on the certificate. Codes are not case-sensitive
          when typed, but every character counts.
        </p>
      </div>
    );
  }
  if (!res.ok) return <LoadProblem status={res.status} what="this certificate" />;
  const c = res.data;
  return (
    <>
      <p className="flex items-center gap-2 text-sm font-semibold text-lime-dark" role="status">
        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-lime text-white" aria-hidden="true">
          <AwardIcon className="h-3 w-3" />
        </span>
        Verified certificate
      </p>
      <article className="mt-4 rounded-panel border border-line bg-surface p-6 sm:p-10">
        <p className="text-sm text-muted">This certifies that</p>
        <h1 className="mt-1 font-serif text-3xl font-semibold tracking-[-0.015em] text-ink">{c.studentName}</h1>
        <p className="mt-5 text-sm text-muted">completed the course</p>
        <p className="mt-1 font-serif text-xl leading-snug font-semibold text-ink sm:text-2xl">{c.courseTitle}</p>
        <dl className="mt-8 grid gap-4 border-t border-line pt-5 text-sm sm:grid-cols-3">
          <div>
            <dt className="text-xs text-muted">Taught by</dt>
            <dd className="font-semibold text-ink">{c.tutorName}</dd>
            <dd className="text-slate-700">{c.academyName}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted">Issued</dt>
            <dd className="font-semibold text-ink">{formatDay(c.issuedAt)}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted">Certificate code</dt>
            <dd className="font-mono font-semibold break-all text-ink">{c.code}</dd>
          </div>
        </dl>
      </article>
      <div className="mt-5 flex flex-wrap items-center gap-3">
        <a href={`${API_URL}/api/certificates/${encodeURIComponent(c.code)}/pdf`} className={buttonClass("secondary")} target="_blank" rel="noopener noreferrer">
          <DownloadIcon className="h-4 w-4" /> Download PDF
        </a>
        <p className="text-sm text-muted">Issued through {APP_NAME}. This page is the proof: share its link.</p>
      </div>
    </>
  );
}
