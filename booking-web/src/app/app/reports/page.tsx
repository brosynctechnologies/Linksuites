"use client";

import { useState } from "react";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { formatPlainDate, todayIn } from "@/lib/time";
import type { BookingReportDto } from "@/lib/types";
import { qs, useApi } from "@/lib/use-api";
import { FALLBACK_TZ, useBusiness } from "@/lib/use-business";
import { PeriodPicker, periodFrom, type Period } from "@/components/period-picker";
import { ApiErrorView, Card, EmptyState, NoAccess, PageHeader, PageLoader, SectionTitle, SummaryStrip, cx } from "@/components/ui";
import { ChartIcon } from "@/components/icons";

export default function ReportsPage() {
  const allowed = useCan("reports.view");
  const { tz } = useBusiness();
  const [period, setPeriod] = useState<Period>(() => periodFrom("this-month", todayIn(FALLBACK_TZ)));
  const { data, error, loading, reload } = useApi<BookingReportDto>(
    allowed && period.from && period.to ? `/api/booking-reports${qs({ from: period.from, to: period.to })}` : null,
  );

  if (!allowed) {
    return (
      <div>
        <PageHeader title="Reports" back="/app/more" />
        <NoAccess text="Reports are for the owner, admins and the accountant." />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader title="Reports" back="/app/more" subtitle="Bookings, no-shows and money received in a period." />
      <PeriodPicker
        presets={["this-week", "this-month", "last-month", "last-30", "this-year", "custom"]}
        value={period}
        today={todayIn(tz)}
        onChange={setPeriod}
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <div className={cx("space-y-5", loading && "opacity-60 transition-opacity")}>
          <SummaryStrip
            label="Figures for the period"
            cols={3}
            items={[
              { label: "Bookings", value: String(data.bookings), sub: `${data.completed} completed, ${data.cancelled} cancelled` },
              {
                label: "No-shows",
                value: String(data.noShows),
                sub: `${Math.round((data.noShowRate || 0) * 100)}% of finished appointments`,
                tone: data.noShows > 0 ? "red" : undefined,
              },
              { label: "Money received", value: formatMoney(data.revenueMinor, data.currency), sub: `${formatMoney(data.depositsMinor, data.currency)} of it in deposits, net of refunds`, tone: "green" },
            ]}
          />
          {data.bookings === 0 && data.byService.length === 0 ? (
            <EmptyState icon={<ChartIcon />} title="No bookings in this period" text="Pick a longer period, or share your booking link to start filling the diary." />
          ) : (
            <>
              <DayChart data={data} />
              <div className="grid gap-5 lg:grid-cols-2">
                <Breakdown
                  title="By service"
                  head={["Service", "Bookings", "Received"]}
                  rows={[...data.byService]
                    .sort((a, b) => b.bookings - a.bookings)
                    .map((s) => [s.name, String(s.bookings), formatMoney(s.revenueMinor, data.currency)])}
                />
                <Breakdown
                  title="By staff"
                  head={["Staff", "Bookings", "No-shows", "Received"]}
                  rows={[...data.byStaff]
                    .sort((a, b) => b.bookings - a.bookings)
                    .map((s) => [s.name, String(s.bookings), s.noShows, formatMoney(s.revenueMinor, data.currency)])}
                />
              </div>
              <p className="text-xs text-muted">
                Bookings are counted by appointment date. Received is the money paid in this period, less refunds, so it adds up to Money received.
              </p>
            </>
          )}
        </div>
      )}
    </div>
  );
}

function DayChart({ data }: { data: BookingReportDto }) {
  const days = [...data.byDay].sort((a, b) => a.date.localeCompare(b.date));
  const max = Math.max(1, ...days.map((d) => d.bookings));
  const every = Math.max(1, Math.ceil(days.length / 10));
  return (
    <Card>
      <SectionTitle>Bookings by day</SectionTitle>
      <div className="flex h-48 items-end gap-[2px]" role="img" aria-label={`Bookings by day, busiest day had ${max}`}>
        {days.map((d) => (
          <div key={d.date} className="flex h-full min-w-0 flex-1 flex-col justify-end" title={`${formatPlainDate(d.date)}: ${d.bookings} booking${d.bookings === 1 ? "" : "s"}, ${formatMoney(d.revenueMinor, data.currency)}`}>
            <div className="rounded-t-[3px] bg-rose" style={{ height: `${(d.bookings / max) * 100}%`, minHeight: d.bookings ? 3 : 0 }} />
          </div>
        ))}
      </div>
      <div className="mt-1 flex gap-[2px] border-t border-line pt-1" aria-hidden="true">
        {days.map((d, i) => (
          <span key={d.date} className="min-w-0 flex-1 overflow-visible text-[0.6875rem] whitespace-nowrap text-muted tnum">
            {i % every === 0 ? Number(d.date.slice(8)) : ""}
          </span>
        ))}
      </div>
      <table className="sr-only">
        <caption>Bookings by day</caption>
        <thead>
          <tr>
            <th>Date</th>
            <th>Bookings</th>
          </tr>
        </thead>
        <tbody>
          {days.map((d) => (
            <tr key={d.date}>
              <td>{formatPlainDate(d.date)}</td>
              <td>{d.bookings}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  );
}

function Breakdown({ title, head, rows }: { title: string; head: string[]; rows: (string | number)[][] }) {
  return (
    <Card>
      <SectionTitle>{title}</SectionTitle>
      {rows.length === 0 ? (
        <p className="text-sm text-muted">Nothing to show.</p>
      ) : (
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-line text-left text-xs text-muted">
              {head.map((h, i) => (
                <th key={h} className={cx("py-2 font-medium", i > 0 && "text-right")}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-line-soft">
            {rows.map((r, ri) => (
              <tr key={ri}>
                {r.map((c, i) => (
                  <td key={i} className={cx("py-2.5", i === 0 ? "pr-3 font-medium" : "text-right tnum", typeof c === "number" && c > 0 && "font-semibold text-brick")}>
                    {String(c)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  );
}
