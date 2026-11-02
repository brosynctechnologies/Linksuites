"use client";

import Link from "next/link";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { staffColor } from "@/lib/color";
import { isLive } from "@/lib/status";
import { formatPlainDate, formatTime, todayIn } from "@/lib/time";
import type { BookingDashboardDto, BookingDto, BookingSummaryDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useNow } from "@/lib/use-now";
import { QuickActions } from "@/components/booking-actions";
import { useNewBooking } from "@/components/new-booking";
import { ApiErrorView, BookingStatusBadge, Button, EmptyState, PageHeader, PageLoader, SummaryStrip, cx } from "@/components/ui";
import { CalendarIcon, PlusIcon } from "@/components/icons";

export default function TodayPage() {
  const { open, version } = useNewBooking();
  const canCreate = useCan("bookings.edit");
  const { data, error, reload, setData } = useApi<BookingDashboardDto>("/api/booking-dashboard", { nonce: version });
  const now = useNow();

  if (!data) {
    return (
      <div>
        <PageHeader title="Today" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : <PageLoader />}
      </div>
    );
  }

  const tz = data.timezone;
  const today = todayIn(tz);
  const list = [...data.today].sort((a, b) => a.startsAt.localeCompare(b.startsAt));
  const nextId = list.find((b) => isLive(b.status) && now && new Date(b.endsAt).getTime() > now)?.id;
  const limit = data.bookingLimit ?? -1;

  function replace(b: BookingDto) {
    setData((d) => (d ? { ...d, today: d.today.map((x) => (x.id === b.id ? { ...x, ...b } : x)) } : d!));
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={<span className="font-expanded font-extrabold">Today, {formatPlainDate(today)}</span>}
        subtitle={
          list.length === 0
            ? "Nothing booked yet today."
            : `${list.filter((b) => isLive(b.status)).length} of ${list.length} appointment${list.length === 1 ? "" : "s"} still to come or in the chair.`
        }
        action={
          canCreate && (
            <Button onClick={() => open({ date: today })}>
              <PlusIcon className="h-4 w-4" /> New booking
            </Button>
          )
        }
      />

      <SummaryStrip
        label="This month"
        cols={5}
        items={[
          { label: "Upcoming", value: String(data.upcomingCount), href: "/app/bookings" },
          {
            label: "Waiting for deposit",
            value: String(data.pendingCount),
            href: "/app/bookings?status=Pending",
          },
          { label: "Revenue this month", value: formatMoney(data.revenueThisMonthMinor, data.currency) },
          {
            label: "No-show rate",
            value: `${Math.round((data.noShowRate || 0) * 100)}%`,
            tone: data.noShowRate > 0.15 ? "red" : undefined,
          },
          {
            label: "Bookings this month",
            value: limit > 0 ? `${data.bookingsThisMonth} / ${limit}` : String(data.bookingsThisMonth),
            sub: limit > 0 ? (data.bookingsThisMonth >= limit ? <Link href="/app/billing" className="font-semibold text-brick underline">Limit reached. Upgrade</Link> : `${limit - data.bookingsThisMonth} left on your plan`) : "No monthly limit",
            tone: limit > 0 && data.bookingsThisMonth >= limit ? "red" : undefined,
          },
        ]}
      />

      <section aria-labelledby="agenda">
        <div className="mb-3 flex items-center justify-between gap-3">
          <h2 id="agenda" className="text-lg font-bold">
            Agenda
          </h2>
          <Link href="/app/calendar" className="text-sm font-semibold text-rose underline-offset-2 hover:underline">
            Open calendar
          </Link>
        </div>
        {list.length === 0 ? (
          <EmptyState
            icon={<CalendarIcon />}
            title="A free day so far"
            text="Bookings made online, by phone or at the counter show up here, time first, with what to do next."
            action={canCreate ? <Button onClick={() => open({ date: today })}>Add a booking</Button> : undefined}
          />
        ) : (
          <ol className="divide-y divide-line-soft overflow-hidden rounded-panel border border-line bg-surface">
            {list.map((b) => (
              <AgendaRow key={b.id} b={b} tz={tz} currency={data.currency} next={b.id === nextId} past={!!now && new Date(b.startsAt).getTime() <= now} onChanged={replace} />
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}

function AgendaRow({
  b,
  tz,
  currency,
  next,
  past,
  onChanged,
}: {
  b: BookingSummaryDto;
  tz: string;
  currency: string;
  next: boolean;
  past: boolean;
  onChanged: (b: BookingDto) => void;
}) {
  const color = staffColor(b.staffColor, b.staffId);
  const dim = !isLive(b.status);
  return (
    <li className={cx("relative grid grid-cols-[4.75rem_minmax(0,1fr)] gap-x-4 px-4 py-4 sm:grid-cols-[6.5rem_minmax(0,1fr)_auto] sm:px-5", next && "bg-rose-wash/50")}>
      {next && <span className="absolute inset-y-0 left-0 w-1 bg-rose" aria-hidden="true" />}
      <div className={cx(dim && "text-faint")}>
        <p className="font-expanded text-[1.75rem] leading-none font-extrabold tnum">{formatTime(b.startsAt, tz)}</p>
        <p className="mt-1 text-xs text-muted tnum">to {formatTime(b.endsAt, tz)}</p>
        {next && <p className="mt-1 text-xs font-semibold text-rose">Next</p>}
      </div>
      <div className="min-w-0">
        <Link href={`/app/bookings/${b.id}`} className="block font-semibold hover:underline">
          {b.customerName}
        </Link>
        <p className="text-sm text-slate-700">{b.serviceName}</p>
        <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-slate-700">
          <span className="flex items-center gap-1.5">
            <span className="h-2.5 w-2.5 rounded-full" style={{ background: color }} aria-hidden="true" />
            {b.staffName}
          </span>
          <BookingStatusBadge status={b.status} />
          {b.paidMinor > 0 && <span className="text-xs text-mint-dark">{formatMoney(b.paidMinor, currency)} paid</span>}
        </p>
        <div className="mt-3 sm:hidden">
          <QuickActions booking={b} onChanged={onChanged} compact startsInPast={past} />
        </div>
      </div>
      <div className="hidden self-center sm:block">
        <QuickActions booking={b} onChanged={onChanged} compact startsInPast={past} />
      </div>
    </li>
  );
}
