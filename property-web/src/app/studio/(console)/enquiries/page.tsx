"use client";

import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { formatStamp, phoneDigits } from "@/lib/format";
import { useCan } from "@/lib/permissions";
import { ENQUIRY_STATUS, statusOf } from "@/lib/status";
import { qs, useApi } from "@/lib/use-api";
import type { EnquiryDto, EnquiryStatus } from "@/lib/types";
import { MessageIcon, PhoneIcon, WhatsAppIcon } from "@/components/icons";
import { ApiErrorView, EmptyState, ErrorBox, PageHeader, PageLoader, StatusBadge, Tabs, buttonClass, cx, inputClass } from "@/components/ui";

const TABS: { value: "" | EnquiryStatus; label: string }[] = [
  { value: "New", label: "New" },
  { value: "Replied", label: "Replied" },
  { value: "Closed", label: "Closed" },
  { value: "", label: "All" },
];

export default function EnquiriesPage() {
  const [status, setStatus] = useState<"" | EnquiryStatus>("New");
  const { data, error, reload, setData } = useApi<EnquiryDto[]>(`/api/property/enquiries${qs({ status })}`);
  const canManage = useCan("leads.manage");
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const list = asList(data);

  async function change(e: EnquiryDto, next: EnquiryStatus) {
    setActionError(null);
    try {
      await api(`/api/property/enquiries/${e.id}/status`, { method: "POST", body: { status: next } });
      setData((xs) => asList(xs).flatMap((x) => (x.id !== e.id ? [x] : status && status !== next ? [] : [{ ...x, status: next }])));
    } catch (err) {
      setActionError(toApiError(err));
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="Enquiries" subtitle="Messages from the enquiry form on your listings. Reply by phone or WhatsApp, then mark them replied." />
      <Tabs label="Filter enquiries" value={status} onChange={setStatus} items={TABS} />
      {actionError && <ErrorBox error={actionError} />}
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState icon={<MessageIcon />} title={status === "New" ? "No new enquiries" : "Nothing here"} text="New enquiries also arrive by email." />
      )}
      {list.length > 0 && (
        <ul className="space-y-3">
          {list.map((e) => {
            const st = statusOf(ENQUIRY_STATUS, e.status);
            const wa = `https://wa.me/${phoneDigits(e.phone)}?text=${encodeURIComponent(`Hello ${e.name}, thanks for your enquiry about "${e.listingTitle}" on PropertyLink.`)}`;
            return (
              <li key={e.id} className={cx("rounded-panel border bg-surface p-4", e.status === "New" ? "border-adire-line" : "border-line")}>
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="font-semibold text-asphalt">{e.name}</p>
                    <p className="text-sm text-muted">
                      About {e.listingTitle}, {formatStamp(e.createdAt)}
                    </p>
                  </div>
                  <StatusBadge tone={st.tone}>{st.label}</StatusBadge>
                </div>
                {e.message && <p className="mt-2 text-slate-700">{e.message}</p>}
                <div className="mt-3 flex flex-wrap items-center gap-2">
                  <a href={wa} target="_blank" rel="noopener noreferrer" className={buttonClass("whatsapp")}>
                    <WhatsAppIcon className="h-5 w-5" /> WhatsApp
                  </a>
                  <a href={`tel:+${phoneDigits(e.phone)}`} className={buttonClass("secondary")}>
                    <PhoneIcon className="h-4 w-4" /> {e.phone}
                  </a>
                  {e.email && (
                    <a href={`mailto:${e.email}`} className={buttonClass("ghost")}>
                      {e.email}
                    </a>
                  )}
                  {canManage && (
                    <div className="ml-auto">
                      <label htmlFor={`st-${e.id}`} className="sr-only">
                        Status
                      </label>
                      <select id={`st-${e.id}`} className={cx(inputClass, "w-36")} value={e.status} onChange={(ev) => change(e, ev.target.value as EnquiryStatus)}>
                        <option value="New">New</option>
                        <option value="Replied">Replied</option>
                        <option value="Closed">Closed</option>
                      </select>
                    </div>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
