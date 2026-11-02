"use client";

import Link from "next/link";
import { useState } from "react";
import { downloadFile, toApiError, type ApiError } from "@/lib/api";
import { formatDay } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import { useCan } from "@/lib/permissions";
import { useApi } from "@/lib/use-api";
import { useDebounced } from "@/lib/use-debounced";
import { usePlanFeature } from "@/lib/use-plan";
import type { BusinessDto, ExpenseDto, Paged } from "@/lib/types";
import { PeriodPicker, periodFrom, type Period } from "@/components/period-picker";
import {
  ApiErrorView,
  Button,
  DataList,
  EmptyState,
  ErrorBox,
  PageHeader,
  PageLoader,
  Pager,
  SearchField,
  UpgradePrompt,
  buttonClass,
  cx,
  dataRowClass,
  inputClass,
} from "@/components/ui";
import { DownloadIcon, ImageIcon, PlusIcon, ReceiptIcon } from "@/components/icons";

const PAGE_SIZE = 20;

export default function ExpensesPage() {
  const canEdit = useCan("expenses.edit");
  const canExport = useCan("reports.view");
  const hasFeature = usePlanFeature("expenses");
  const [period, setPeriod] = useState<Period>(() => periodFrom("this-month"));
  const [category, setCategory] = useState("");
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const dq = useDebounced(q.trim());
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<ApiError | null>(null);

  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
  if (period.from) params.set("from", period.from);
  if (period.to) params.set("to", period.to);
  if (category) params.set("category", category);
  if (dq) params.set("q", dq);
  const blocked = hasFeature === false;
  const { data, error, loading, reload } = useApi<Paged<ExpenseDto>>(blocked ? null : `/api/expenses?${params}`);
  const { data: categories } = useApi<string[]>(blocked ? null : "/api/expenses/categories");
  const { data: business } = useApi<BusinessDto>("/api/business");
  const base = business?.currency ?? "NGN";

  async function exportCsv() {
    setExporting(true);
    setExportError(null);
    try {
      const p = new URLSearchParams();
      if (period.from) p.set("from", period.from);
      if (period.to) p.set("to", period.to);
      await downloadFile(`/api/expenses/export.csv?${p}`, `expenses-${period.from}-to-${period.to}.csv`, { accept: "text/csv" });
    } catch (e) {
      setExportError(toApiError(e));
    } finally {
      setExporting(false);
    }
  }

  if (blocked || error?.isPlanLimit) {
    return (
      <div>
        <PageHeader title="Expenses" back="/app/more" />
        <UpgradePrompt plan="Business" message={error?.detail || "Track expenses, attach receipts and see your profit with the Business plan."} />
      </div>
    );
  }

  const filtered = !!(dq || category);
  const pageTotal = (data?.items ?? []).reduce((sum, x) => sum + (x.baseAmountMinor ?? 0), 0);

  return (
    <div>
      <PageHeader
        title="Expenses"
        back="/app/more"
        action={
          <>
            {canExport && (
              <Button variant="secondary" onClick={exportCsv} loading={exporting}>
                <DownloadIcon className="h-4 w-4" /> <span className="hidden sm:inline">Export</span> CSV
              </Button>
            )}
            {canEdit && (
              <Link href="/app/expenses/new" className={buttonClass("primary")}>
                <PlusIcon className="h-4 w-4" /> Add expense
              </Link>
            )}
          </>
        }
      />
      {exportError && (
        <div className="mb-3">
          <ErrorBox error={exportError} />
        </div>
      )}

      <div className="mb-3">
        <PeriodPicker
          presets={["this-month", "last-month", "this-year", "custom"]}
          value={period}
          onChange={(p) => {
            setPeriod(p);
            setPage(1);
          }}
        />
      </div>

      <div className="mb-4 grid gap-3 sm:grid-cols-[minmax(0,1fr)_14rem] lg:max-w-2xl">
        <SearchField
          id="exp-search"
          label="Search expenses"
          placeholder="Search vendor, description or reference"
          value={q}
          onChange={(v) => {
            setQ(v);
            setPage(1);
          }}
        />
        <div>
          <label htmlFor="exp-category" className="sr-only">
            Category
          </label>
          <select
            id="exp-category"
            className={inputClass}
            value={category}
            onChange={(e) => {
              setCategory(e.target.value);
              setPage(1);
            }}
          >
            <option value="">All categories</option>
            {(Array.isArray(categories) ? categories : []).map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </div>
      </div>

      {error && <ApiErrorView error={error} onRetry={reload} plan="Business" />}
      {!data && !error && <PageLoader />}
      {data && (
        <div>
          {data.items.length === 0 ? (
            <EmptyState
              icon={<ReceiptIcon width={24} height={24} />}
              title={filtered ? "No matching expenses" : "No expenses in this period"}
              text={filtered ? "Nothing matches. Try another category or clear the search." : "Record what you spend to see your real profit."}
              action={
                !filtered && canEdit ? (
                  <Link href="/app/expenses/new" className={buttonClass("primary")}>
                    Add expense
                  </Link>
                ) : undefined
              }
            />
          ) : (
            <>
              <DataList
                busy={loading}
                template="minmax(0,1fr) 10rem 7rem minmax(8rem,auto)"
                head={[{ label: "Paid to" }, { label: "Category" }, { label: "Date" }, { label: "Amount", align: "right" }]}
              >
                {data.items.map((x) => (
                  <li key={x.id}>
                    <Link href={`/app/expenses/${x.id}`} className={cx(dataRowClass, "hover:bg-slate-50")}>
                      <span className="min-w-0">
                        <span className="flex items-center gap-1.5 font-medium text-graphite">
                          <span className="truncate">{x.vendor || x.description || x.category}</span>
                          {x.receiptUrl && (
                            <>
                              <ImageIcon className="h-4 w-4 shrink-0 text-faint" />
                              <span className="sr-only">Has receipt</span>
                            </>
                          )}
                        </span>
                        {x.clientName && <span className="block truncate text-xs text-muted">For {x.clientName}</span>}
                        <span className="block truncate text-xs text-muted md:hidden">
                          {x.category}, {formatDay(x.date)}
                        </span>
                      </span>
                      <span className="hidden truncate text-sm text-slate-600 md:block">{x.category}</span>
                      <span className="hidden text-sm text-slate-600 tabular-nums md:block">{formatDay(x.date)}</span>
                      <span className="self-start text-right md:self-center">
                        <span className="amount block font-semibold text-graphite">{formatMoney(x.amountMinor, x.currency)}</span>
                        {x.currency !== base && <span className="amount block text-xs text-muted">≈ {formatMoney(x.baseAmountMinor, base)}</span>}
                      </span>
                    </Link>
                  </li>
                ))}
              </DataList>
              <div className="mt-3 flex items-baseline justify-between gap-3 rounded-panel border border-rule bg-paper px-4 py-3 text-sm">
                <span className="text-muted">Total on this page</span>
                <span className="font-figure amount text-xl text-graphite">{formatMoney(pageTotal, base)}</span>
              </div>
              <Pager page={data.page} pageSize={data.pageSize || PAGE_SIZE} total={data.total} onPage={setPage} />
            </>
          )}
        </div>
      )}
    </div>
  );
}
