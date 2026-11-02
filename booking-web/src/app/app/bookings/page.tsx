"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { asList } from "@/lib/api";
import { staffColor } from "@/lib/color";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { ALL_STATUSES, SOURCE_LABEL, STATUS_LABEL, isLive } from "@/lib/status";
import { dateKey, formatPlainDate, formatTime, todayIn } from "@/lib/time";
import type { BookingStatus, BookingSummaryDto, Paged } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { FALLBACK_TZ, useBusiness, useStaff } from "@/lib/use-business";
import { useDebounced } from "@/lib/use-debounced";
import { useNewBooking } from "@/components/new-booking";
import { PeriodPicker, periodFrom, type Period } from "@/components/period-picker";
import { ApiErrorView, BookingStatusBadge, Button, EmptyState, PageHeader, PageLoader, Pager, SearchField, Select, cx } from "@/components/ui";
import { ListIcon, PlusIcon } from "@/components/icons";

const PAGE_SIZE = 50;

export default function BookingsPage() {
  const sp = useSearchParams();
  const initialStatus = sp.get("status");
  const { tz, currency } = useBusiness();
  const { staff } = useStaff();
  const { open, version } = useNewBooking();
  const canCreate = useCan("bookings.edit");

  const [period, setPeriod] = useState<Period>(() => periodFrom("upcoming", todayIn(FALLBACK_TZ)));
  const [status, setStatus] = useState<string>(initialStatus && (ALL_STATUSES as string[]).includes(initialStatus) ? initialStatus : "");
  const [staffId, setStaffId] = useState("");
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const dq = useDebounced(q.trim());

  const path = `/api/bookings${qs({ from: period.from, to: period.to, staffId, status, q: dq, page, pageSize: PAGE_SIZE })}`;
  const { data, error, loading, reload } = useApi<Paged<BookingSummaryDto>>(period.from && period.to ? path : null, { nonce: version });
  const list = asList(data);
  const filtered = !!(status || staffId || dq);

  // Group rows under their date (in the business zone).
  const groups: { date: string; items: BookingSummaryDto[] }[] = [];
  for (const b of list) {
    const d = dateKey(b.startsAt, tz);
    const last = groups[groups.length - 1];
    if (last && last.date === d) last.items.push(b);
    else groups.push({ date: d, items: [b] });
  }

  const resetPage = <T,>(fn: (v: T) => void) => (v: T) => {
    fn(v);
    setPage(1);
  };

  return (
    <div className="space-y-4">
      <PageHeader
        title="Bookings"
        action={
          canCreate && (
            <Button onClick={() => open()}>
              <PlusIcon className="h-4 w-4" /> New booking
            </Button>
          )
        }
      />
      <PeriodPicker
        presets={["upcoming", "today", "this-week", "this-month", "last-month", "custom"]}
        value={period}
        today={todayIn(tz)}
        onChange={resetPage(setPeriod)}
      />
      <div className="grid gap-3 sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1fr)]">
        <SearchField id="booking-search" label="Search bookings" placeholder="Customer name, phone or booking ref" value={q} onChange={resetPage(setQ)} />
        <Select label="Status" srLabel value={status} onChange={(e) => resetPage(setStatus)(e.target.value)}>
          <option value="">Any status</option>
          {ALL_STATUSES.map((s) => (
            <option key={s} value={s}>
              {STATUS_LABEL[s]}
            </option>
          ))}
        </Select>
        <Select label="Staff" srLabel value={staffId} onChange={(e) => resetPage(setStaffId)(e.target.value)}>
          <option value="">All staff</option>
          {staff.map((s) => (
            <option key={s.id} value={s.id}>
              {s.displayName}
            </option>
          ))}
        </Select>
      </div>

      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<ListIcon />}
          title={filtered ? "No bookings match" : "No bookings in this period"}
          text={
            filtered
              ? "Try another status, staff member or search, or widen the dates."
              : "Share your booking link so customers can book themselves, or add a phone booking now."
          }
          action={!filtered && canCreate ? <Button onClick={() => open()}>New booking</Button> : undefined}
        />
      )}
      {groups.length > 0 && (
        <div className={cx("overflow-hidden rounded-panel border border-line bg-surface", loading && "opacity-60 transition-opacity")}>
          {groups.map((g) => (
            <section key={g.date} aria-label={formatPlainDate(g.date, "long")}>
              <h2 className="border-b border-line-soft bg-mist px-4 py-2 font-expanded text-sm font-bold sm:px-5">{formatPlainDate(g.date, "long")}</h2>
              <ul className="divide-y divide-line-soft">
                {g.items.map((b) => (
                  <li key={b.id}>
                    <Link
                      href={`/app/bookings/${b.id}`}
                      className="grid grid-cols-[4rem_minmax(0,1fr)_auto] items-center gap-x-4 px-4 py-3 hover:bg-mist sm:grid-cols-[4.5rem_minmax(0,1.2fr)_minmax(0,1fr)_minmax(0,9rem)_auto] sm:px-5"
                    >
                      <span className={cx("font-expanded text-lg font-extrabold tnum", !isLive(b.status) && "text-faint")}>{formatTime(b.startsAt, tz)}</span>
                      <span className="min-w-0">
                        <span className="block truncate font-semibold">{b.customerName}</span>
                        <span className="block truncate text-sm text-slate-700 sm:hidden">{b.serviceName}</span>
                        <span className="block truncate text-xs text-muted">
                          {b.customerPhone}, {SOURCE_LABEL[b.source].toLowerCase()}
                        </span>
                      </span>
                      <span className="hidden min-w-0 text-sm sm:block">
                        <span className="block truncate">{b.serviceName}</span>
                        <span className="flex items-center gap-1.5 truncate text-muted">
                          <span className="h-2 w-2 shrink-0 rounded-full" style={{ background: staffColor(b.staffColor, b.staffId) }} aria-hidden="true" />
                          {b.staffName}
                        </span>
                      </span>
                      <span className="hidden sm:block">
                        <BookingStatusBadge status={b.status as BookingStatus} />
                      </span>
                      <span className="text-right">
                        <span className="block text-sm font-semibold tnum">{formatMoney(b.priceMinor, currency)}</span>
                        <span className={cx("block text-xs tnum", b.paidMinor > 0 ? "text-mint-dark" : "text-muted")}>
                          {b.paidMinor > 0 ? `${formatMoney(b.paidMinor, currency)} paid` : "Nothing paid"}
                        </span>
                        <span className="mt-1 block sm:hidden">
                          <BookingStatusBadge status={b.status} />
                        </span>
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
      {data && <Pager page={page} pageSize={PAGE_SIZE} total={data.total ?? list.length} onPage={setPage} />}
    </div>
  );
}
