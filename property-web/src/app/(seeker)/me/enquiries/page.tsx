"use client";

import Link from "next/link";
import { asList } from "@/lib/api";
import { formatStamp, localPath } from "@/lib/format";
import { ENQUIRY_STATUS, statusOf } from "@/lib/status";
import { useApi } from "@/lib/use-api";
import type { MyEnquiryDto } from "@/lib/types";
import { MessageIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, StatusBadge, buttonClass, linkClass } from "@/components/ui";

export default function MyEnquiriesPage() {
  const { data, error, reload } = useApi<MyEnquiryDto[]>("/api/me/enquiries");
  const list = asList(data);
  return (
    <div>
      <PageHeader title="Enquiries" subtitle="Messages you sent to agents from listing pages." />
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<MessageIcon />}
          title="No enquiries yet"
          text="Send an enquiry from any listing and it will show here with the agent's reply status."
          action={
            <Link href="/rent" className={buttonClass("primary")}>
              Find a home
            </Link>
          }
        />
      )}
      {list.length > 0 && (
        <ul className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
          {list.map((e) => {
            const st = statusOf(ENQUIRY_STATUS, e.status);
            const href = localPath(e.listingUrl);
            return (
              <li key={e.id} className="p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  {href ? (
                    <Link href={href} className={linkClass}>
                      {e.listingTitle}
                    </Link>
                  ) : (
                    <span className="font-semibold text-asphalt">{e.listingTitle}</span>
                  )}
                  <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                </div>
                <p className="text-sm text-muted">
                  To {e.agencyName}, {formatStamp(e.createdAt)}
                </p>
                {e.message && <p className="mt-1.5 text-sm text-slate-700">{e.message}</p>}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
