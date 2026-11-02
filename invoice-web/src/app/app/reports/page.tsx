"use client";

import { useState } from "react";
import { downloadFile, toApiError, type ApiError } from "@/lib/api";
import { formatDay, formatMonth } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import type { ProfitMonth, ProfitReportDto } from "@/lib/types";
import { PeriodPicker, periodFrom, type Period } from "@/components/period-picker";
import { Button, Card, ErrorBox, NoAccess, PageHeader, PageLoader, SectionTitle, SummaryStrip, UpgradePrompt, cx } from "@/components/ui";
import { DownloadIcon } from "@/components/icons";

export default function ReportsPage() {
  const canView = useCan("reports.view");
  const [period, setPeriod] = useState<Period>(() => periodFrom("this-year"));
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<ApiError | null>(null);

  const params = new URLSearchParams();
  if (period.from) params.set("from", period.from);
  if (period.to) params.set("to", period.to);
  const valid = !period.from || !period.to || period.from <= period.to;
  const { data, error, loading, reload } = useApi<ProfitReportDto>(canView && valid ? `/api/reports/profit?${params}` : null);

  if (!canView) {
    return (
      <div>
        <PageHeader title="Reports" back="/app/more" />
        <NoAccess text="The profit report is available to the owner, admins and accountants." />
      </div>
    );
  }

  if (error?.isPlanLimit) {
    return (
      <div>
        <PageHeader title="Reports" back="/app/more" />
        <UpgradePrompt plan="Business" message={error.detail || "See income, expenses and profit month by month with the Business plan."} />
      </div>
    );
  }

  async function downloadCsv() {
    setDownloading(true);
    setDownloadError(null);
    try {
      await downloadFile(`/api/reports/profit.csv?${params}`, `profit-${period.from}-to-${period.to}.csv`, { accept: "text/csv" });
    } catch (e) {
      setDownloadError(toApiError(e));
    } finally {
      setDownloading(false);
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Profit report"
        back="/app/more"
        subtitle={data ? `${formatDay(data.from)} – ${formatDay(data.to)}. Cash basis, in ${data.currency}.` : "Cash basis"}
        action={
          <Button variant="secondary" onClick={downloadCsv} loading={downloading} disabled={!valid}>
            <DownloadIcon className="h-4 w-4" /> Download CSV
          </Button>
        }
      />
      <PeriodPicker presets={["this-month", "last-month", "this-quarter", "this-year", "last-year", "custom"]} value={period} onChange={setPeriod} />
      {!valid && <ErrorBox error="The start date must be before the end date." />}
      {downloadError && <ErrorBox error={downloadError} />}
      {error && <ErrorBox error={error} onRetry={reload} />}
      {!data && !error && valid && <PageLoader />}
      {data && (
        <div className={cx("space-y-4", loading && "opacity-60 transition-opacity")}>
          <SummaryStrip
            cols={5}
            label="Period totals"
            items={[
              { label: "Income", value: formatMoney(data.incomeMinor, data.currency), sub: "Payments received" },
              { label: "Expenses", value: formatMoney(data.expensesMinor, data.currency), sub: "Money spent" },
              {
                label: "Profit",
                value: formatMoney(data.profitMinor, data.currency),
                tone: data.profitMinor < 0 ? "red" : data.profitMinor > 0 ? "green" : undefined,
                sub: "Income − expenses",
              },
              { label: "Invoiced", value: formatMoney(data.invoicedMinor, data.currency), sub: "Issued in the period" },
              { label: "Outstanding", value: formatMoney(data.outstandingMinor, data.currency), sub: "Unpaid today" },
            ]}
          />

          <Card>
            <SectionTitle>Income vs expenses by month</SectionTitle>
            <MonthlyChart months={data.months ?? []} currency={data.currency} />
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <SectionTitle>Expenses by category</SectionTitle>
              <CategoryBreakdown items={data.expensesByCategory ?? []} total={data.expensesMinor} currency={data.currency} />
            </Card>
            <Card>
              <SectionTitle>Tax summary</SectionTitle>
              <dl className="space-y-2 text-sm">
                <div className="flex justify-between">
                  <dt className="text-slate-600">Tax collected on invoices</dt>
                  <dd className="amount font-semibold">{formatMoney(data.taxCollectedMinor, data.currency)}</dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-slate-600">Tax paid on expenses</dt>
                  <dd className="amount font-semibold">{formatMoney(data.taxOnExpensesMinor, data.currency)}</dd>
                </div>
                <div className="flex justify-between gap-3 border-t border-rule pt-2">
                  <dt className="font-semibold text-graphite">Difference</dt>
                  <dd className="font-figure amount text-lg">{formatMoney(data.taxCollectedMinor - data.taxOnExpensesMinor, data.currency)}</dd>
                </div>
              </dl>
              <p className="mt-3 text-xs text-muted">For guidance only — check with your accountant before filing.</p>
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}

/** Paired bars per month, drawn with plain divs (no chart library). */
function MonthlyChart({ months, currency }: { months: ProfitMonth[]; currency: string }) {
  if (months.length === 0) return <p className="text-sm text-slate-500">No data for this period.</p>;
  const max = Math.max(1, ...months.flatMap((m) => [m.incomeMinor, m.expensesMinor]));
  const pct = (v: number) => `${Math.max(0, (v / max) * 100)}%`;

  return (
    <div>
      <div className="mb-4 flex flex-wrap gap-x-5 gap-y-1 text-xs text-slate-600" aria-hidden="true">
        <span className="flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-[2px] bg-ink" /> Income
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-[2px] bg-amber" /> Expenses
        </span>
        <span className="flex items-center gap-1.5">
          <span className="text-paid">+</span>/<span className="text-overdue">−</span> Profit under each month
        </span>
      </div>
      <div className="no-scrollbar overflow-x-auto" aria-hidden="true">
        <div
          className="flex h-52 items-end gap-3 border-b border-graphite/60 bg-[repeating-linear-gradient(to_top,transparent_0,transparent_calc(25%-1px),var(--color-rule-soft)_calc(25%-1px),var(--color-rule-soft)_25%)]" style={{ minWidth: `${months.length * 44}px` }}>
          {months.map((m) => (
            <div
              key={m.month}
              className="flex h-full min-w-8 flex-1 items-end justify-center gap-0.5"
              title={`${formatMonth(m.month, "long")}: income ${formatMoney(m.incomeMinor, currency)}, expenses ${formatMoney(m.expensesMinor, currency)}, profit ${formatMoney(m.profitMinor, currency)}`}
            >
              <div className="w-1/2 max-w-5 rounded-t-[2px] bg-ink" style={{ height: pct(m.incomeMinor) }} />
              <div className="w-1/2 max-w-5 rounded-t-[2px] bg-amber" style={{ height: pct(m.expensesMinor) }} />
            </div>
          ))}
        </div>
        <div className="mt-1 flex gap-3" style={{ minWidth: `${months.length * 44}px` }}>
          {months.map((m) => (
            <div key={m.month} className="min-w-8 flex-1 text-center">
              <p className="text-xs text-muted">{formatMonth(m.month)}</p>
              <p className={cx("text-xs font-semibold tabular-nums", m.profitMinor < 0 ? "text-overdue" : m.profitMinor > 0 ? "text-paid" : "text-muted")}>
                {m.profitMinor === 0 ? "—" : formatCompact(m.profitMinor, currency)}
              </p>
            </div>
          ))}
        </div>
      </div>
      <table className="sr-only">
        <caption>Income, expenses and profit by month</caption>
        <thead>
          <tr>
            <th scope="col">Month</th>
            <th scope="col">Income</th>
            <th scope="col">Expenses</th>
            <th scope="col">Profit</th>
          </tr>
        </thead>
        <tbody>
          {months.map((m) => (
            <tr key={m.month}>
              <th scope="row">{formatMonth(m.month, "long")}</th>
              <td>{formatMoney(m.incomeMinor, currency)}</td>
              <td>{formatMoney(m.expensesMinor, currency)}</td>
              <td>{formatMoney(m.profitMinor, currency)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-3 text-xs text-muted">Figures under each month are that month&apos;s profit.</p>
    </div>
  );
}

const compactFormatters = new Map<string, Intl.NumberFormat>();
function formatCompact(minor: number, currency: string) {
  let f = compactFormatters.get(currency);
  if (!f) {
    try {
      f = new Intl.NumberFormat("en", { style: "currency", currency, currencyDisplay: "narrowSymbol", notation: "compact", maximumFractionDigits: 1 });
    } catch {
      f = new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 });
    }
    compactFormatters.set(currency, f);
  }
  return f.format(minor / 100);
}

function CategoryBreakdown({ items, total, currency }: { items: { category: string; amountMinor: number }[]; total: number; currency: string }) {
  if (items.length === 0) return <p className="text-sm text-slate-500">No expenses in this period.</p>;
  const sum = total > 0 ? total : items.reduce((s, x) => s + x.amountMinor, 0) || 1;
  return (
    <ul className="space-y-3">
      {items.map((x) => {
        const share = Math.round((x.amountMinor / sum) * 100);
        return (
          <li key={x.category}>
            <div className="flex justify-between gap-3 text-sm">
              <span className="truncate text-slate-700">{x.category}</span>
              <span className="amount shrink-0 font-medium">
                {formatMoney(x.amountMinor, currency)} <span className="text-xs font-normal text-muted">{share}%</span>
              </span>
            </div>
            <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-desk" aria-hidden="true">
              <div className="h-full rounded-full bg-amber" style={{ width: `${Math.min(100, Math.max(0, share))}%` }} />
            </div>
          </li>
        );
      })}
    </ul>
  );
}
