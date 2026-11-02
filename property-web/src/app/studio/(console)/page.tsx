"use client";

import Link from "next/link";
import { useState } from "react";
import { asList } from "@/lib/api";
import { useAuth, firstName } from "@/lib/auth";
import { addDays, formatDay, formatTime, lagosToday } from "@/lib/format";
import { formatPrice } from "@/lib/money";
import { qs, useApi } from "@/lib/use-api";
import type { BillingDto, EnquiryDto, InspectionDto, StudioListingSummaryDto, StudioStayDto } from "@/lib/types";
import { Card, ErrorBox, PageHeader, SectionTitle, UsageMeter, buttonClass, linkClass } from "@/components/ui";

export default function DashboardPage() {
  const auth = useAuth();
  const [today] = useState(() => lagosToday());
  const listings = useApi<StudioListingSummaryDto[]>("/api/property/listings");
  const billing = useApi<BillingDto>("/api/billing");
  const enquiries = useApi<EnquiryDto[]>(`/api/property/enquiries${qs({ status: "New" })}`);
  const viewings = useApi<InspectionDto[]>(`/api/property/inspections${qs({ from: today, to: addDays(today, 1) })}`);
  const stays = useApi<StudioStayDto[]>(`/api/property/stays${qs({ from: today, to: addDays(today, 7) })}`);
  const [nowMs] = useState(() => Date.now());

  const all = asList(listings.data);
  const live = all.filter((l) => l.status === "Active").length;
  const pending = all.filter((l) => l.status === "Pending").length;
  const hidden = all.filter((l) => l.status === "Hidden").length;
  // The plan meter's definition (the server's PropertyUsage): live, waiting for review and hidden after reports all count.
  const counted = live + pending + hidden;
  const countedSub = [`${live} live`, pending ? `${pending} in review` : null, hidden ? `${hidden} hidden` : null].filter(Boolean).join(", ");
  const expiring = all
    .filter((l) => l.status === "Active" && l.expiresAt && new Date(l.expiresAt).getTime() - nowMs < 7 * 86_400_000)
    .sort((a, b) => (a.expiresAt ?? "").localeCompare(b.expiresAt ?? ""));
  const usage = billing.data?.usage?.items?.find((u) => u.key === "listings");
  const todayViewings = asList(viewings.data)
    .filter((v) => v.status === "Requested" || v.status === "Confirmed")
    .sort((a, b) => new Date(a.scheduledAt).getTime() - new Date(b.scheduledAt).getTime());
  const weekStays = asList(stays.data).filter((s) => s.status === "Confirmed" || s.status === "Pending");
  const newEnquiries = asList(enquiries.data);

  return (
    <div className="space-y-5">
      <PageHeader title={`Hello, ${firstName(auth?.user.fullName)}`} subtitle={auth?.tenant?.name} />

      <section aria-label="Summary" className="overflow-hidden rounded-panel border border-line bg-surface">
        <div className="-mr-px -mb-px grid grid-cols-2 md:grid-cols-4">
          {[
            { label: "Listings on your plan", value: listings.data ? String(counted) : "…", sub: listings.data ? countedSub : "Live, in review or hidden", href: "/studio/listings" },
            { label: "New enquiries", value: enquiries.data ? String(newEnquiries.length) : "…", sub: "Not yet replied", href: "/studio/enquiries" },
            { label: "Viewings today", value: viewings.data ? String(todayViewings.length) : "…", sub: "Requested or confirmed", href: "/studio/viewings" },
            { label: "Stays this week", value: stays.data ? String(weekStays.length) : "…", sub: "Next 7 days", href: "/studio/stays" },
          ].map((it) => (
            <Link key={it.label} href={it.href} className="block border-r border-b border-line px-4 py-4 hover:bg-slate-50 sm:px-5">
              <p className="text-sm text-muted">{it.label}</p>
              <p className="price mt-1 text-[1.75rem] leading-tight text-asphalt">{it.value}</p>
              <p className="mt-1 text-xs text-muted">{it.sub}</p>
            </Link>
          ))}
        </div>
      </section>

      {(listings.error || enquiries.error) && <ErrorBox error={listings.error ?? enquiries.error} onRetry={() => {
            listings.reload();
            enquiries.reload();
          }} />}

      <div className="grid gap-5 lg:grid-cols-2">
        <Card>
          <SectionTitle action={<Link href="/studio/billing" className={linkClass}>Plan</Link>}>Listings on your plan</SectionTitle>
          {usage ? (
            <UsageMeter label={`Listings counted on ${billing.data?.currentPlan.name ?? "your plan"}`} used={usage.used} limit={usage.limit} />
          ) : (
            <p className="text-sm text-muted">{billing.error ? billing.error.message : "Loading…"}</p>
          )}
          <p className="mt-2 text-xs text-muted">Listings that are live, waiting for review or hidden after reports all count toward your plan.</p>
        </Card>

        <Card>
          <SectionTitle action={<Link href="/studio/listings?status=Active" className={linkClass}>All live</Link>}>Expiring soon</SectionTitle>
          {expiring.length === 0 ? (
            <p className="text-sm text-muted">Nothing expires in the next 7 days.</p>
          ) : (
            <ul className="divide-y divide-line-soft">
              {expiring.slice(0, 5).map((l) => (
                <li key={l.id} className="flex items-center justify-between gap-3 py-2.5">
                  <Link href={`/studio/listings/${l.id}`} className="min-w-0 truncate font-medium text-asphalt hover:underline">
                    {l.title}
                  </Link>
                  <span className="shrink-0 text-sm font-semibold text-ochre">{formatDay(l.expiresAt)}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card>
          <SectionTitle action={<Link href="/studio/enquiries" className={linkClass}>Inbox</Link>}>New enquiries</SectionTitle>
          {newEnquiries.length === 0 ? (
            <p className="text-sm text-muted">{enquiries.data ? "You're up to date." : "Loading…"}</p>
          ) : (
            <ul className="divide-y divide-line-soft">
              {newEnquiries.slice(0, 5).map((e) => (
                <li key={e.id} className="py-2.5">
                  <p className="font-medium text-asphalt">{e.name}</p>
                  <p className="truncate text-sm text-muted">{e.listingTitle}</p>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card>
          <SectionTitle action={<Link href="/studio/viewings" className={linkClass}>Agenda</Link>}>Today&apos;s viewings</SectionTitle>
          {todayViewings.length === 0 ? (
            <p className="text-sm text-muted">{viewings.data ? "No viewings today." : "Loading…"}</p>
          ) : (
            <ul className="divide-y divide-line-soft">
              {todayViewings.map((v) => (
                <li key={v.id} className="flex gap-3 py-2.5">
                  <span className="tnum w-12 shrink-0 font-semibold text-asphalt">{formatTime(v.scheduledAt)}</span>
                  <span className="min-w-0">
                    <span className="block truncate font-medium text-asphalt">{v.listingTitle}</span>
                    <span className="block text-sm text-muted">{v.seekerName}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      {weekStays.length > 0 && (
        <Card>
          <SectionTitle action={<Link href="/studio/stays" className={linkClass}>Short-lets</Link>}>Stays this week</SectionTitle>
          <ul className="divide-y divide-line-soft">
            {weekStays.map((s) => (
              <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 py-2.5">
                <span className="min-w-0">
                  <span className="block font-medium text-asphalt">{s.guestName}</span>
                  <span className="block text-sm text-muted">
                    {s.listingTitle}, {formatDay(s.checkIn)} to {formatDay(s.checkOut)}
                  </span>
                </span>
                <span className="price text-sm text-asphalt">{formatPrice(s.totalMinor, "Total")}</span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      {listings.data && all.length === 0 && (
        <Card className="text-center">
          <p className="font-semibold text-asphalt">Post your first listing</p>
          <p className="mt-1 text-sm text-muted">Five short steps: basics, location, costs, photos, then submit for review.</p>
          <Link href="/studio/listings/new" className={buttonClass("primary", "mt-4")}>
            New listing
          </Link>
        </Card>
      )}
    </div>
  );
}
