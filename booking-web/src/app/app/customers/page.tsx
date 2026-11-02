"use client";

import Link from "next/link";
import { useState } from "react";
import { asList } from "@/lib/api";
import { initials } from "@/lib/color";
import { formatMoney } from "@/lib/money";
import { dateKey, formatPlainDate } from "@/lib/time";
import type { BookingCustomerDto, Paged } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { useBusiness } from "@/lib/use-business";
import { useDebounced } from "@/lib/use-debounced";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, Pager, SearchField, cx } from "@/components/ui";
import { ChevronRightIcon, UsersIcon } from "@/components/icons";

const PAGE_SIZE = 50;

export default function CustomersPage() {
  const { tz, currency } = useBusiness();
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const dq = useDebounced(q.trim());
  const { data, error, loading, reload } = useApi<Paged<BookingCustomerDto>>(`/api/booking-customers${qs({ q: dq, page, pageSize: PAGE_SIZE })}`);
  const list = asList(data);

  return (
    <div className="space-y-4">
      <PageHeader title="Customers" subtitle="Everyone who has booked, with their visits and no-shows." />
      <SearchField
        id="customer-search"
        label="Search customers"
        className="max-w-md"
        placeholder="Name, phone or email"
        value={q}
        onChange={(v) => {
          setQ(v);
          setPage(1);
        }}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<UsersIcon />}
          title={dq ? "No matching customers" : "No customers yet"}
          text={dq ? "Try part of a name or the last digits of a phone number." : "Customers are added automatically when they book online or you add a booking for them."}
        />
      )}
      {list.length > 0 && (
        <ul className={cx("divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface", loading && "opacity-60")}>
          {list.map((c) => (
            <li key={c.id}>
              <Link
                href={`/app/customers/${c.id}`}
                className="grid grid-cols-[2.5rem_minmax(0,1fr)_auto] items-center gap-x-3 px-4 py-3 hover:bg-mist sm:grid-cols-[2.5rem_minmax(0,1.3fr)_minmax(0,1fr)_minmax(0,8rem)_1rem] sm:px-5"
              >
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-mist-2 text-sm font-bold text-slate-700" aria-hidden="true">
                  {initials(c.name)}
                </span>
                <span className="min-w-0">
                  <span className="block truncate font-semibold">{c.name}</span>
                  <span className="block truncate text-sm text-muted">{c.phone}</span>
                </span>
                <span className="hidden text-sm sm:block">
                  {c.bookingsCount} visit{c.bookingsCount === 1 ? "" : "s"}
                  {c.noShowCount > 0 && (
                    <span className="block font-semibold text-brick">
                      {c.noShowCount} no-show{c.noShowCount === 1 ? "" : "s"}
                    </span>
                  )}
                </span>
                <span className="text-right text-sm">
                  <span className="block font-semibold tnum">{formatMoney(c.totalSpentMinor, currency)}</span>
                  <span className="block text-xs text-muted">{c.lastVisitAt ? `Last ${formatPlainDate(dateKey(c.lastVisitAt, tz), "medium")}` : "No visit yet"}</span>
                </span>
                <ChevronRightIcon className="hidden h-4 w-4 text-faint sm:block" />
              </Link>
            </li>
          ))}
        </ul>
      )}
      {data && <Pager page={page} pageSize={PAGE_SIZE} total={data.total ?? list.length} onPage={setPage} />}
    </div>
  );
}
