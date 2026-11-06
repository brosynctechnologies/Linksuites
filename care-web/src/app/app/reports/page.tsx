"use client";

import Link from "next/link";
import { useState } from "react";
import { downloadFile } from "@/lib/api";
import { PRICE_CATEGORIES } from "@/lib/care";
import { useCare } from "@/lib/care-me";
import { formatDay, formatStamp, lagosToday, money, plural } from "@/lib/format";
import { addDays, methodLabel } from "@/lib/inpatient";
import { useAction } from "@/lib/mutate";
import { canTakeMoney } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { CareDebtorsReportDto, DepositsReportDto, RevenueReportDto, TakingsReportDto } from "@/lib/types/billing";
import { DownloadIcon } from "@/components/icons";
import { ApiErrorView, Button, Card, CaseSheet, ErrorBox, Input, NoAccess, PageHeader, PageLoader, Tabs, WhatsAppLink, Wristband, caseRowClass } from "@/components/ui";

type Tab = "takings" | "revenue" | "deposits" | "debtors";

/**
 * Reports (the cashier and leadership; the support view reads). Money, payment methods, price categories and demographics only. Dates are
 * Lagos days; each report has its CSV.
 */
export default function ReportsPage() {
  const { access } = useCare();
  const [tab, setTab] = useState<Tab>("takings");
  if (!canTakeMoney(access)) return <NoAccess text="Reports are for the cashier, admins and the medical director." />;
  return (
    <div className="space-y-5">
      <PageHeader title="Reports" subtitle="Lagos days. Every report downloads as CSV." />
      <Tabs
        label="Report"
        idBase="report"
        value={tab}
        onChange={setTab}
        items={[
          { value: "takings", label: "Takings" },
          { value: "revenue", label: "Revenue" },
          { value: "deposits", label: "Deposits held" },
          { value: "debtors", label: "Debtors" },
        ]}
      />
      <div role="tabpanel" id={`report-panel-${tab}`} aria-labelledby={`report-tab-${tab}`}>
        {tab === "takings" && <Takings />}
        {tab === "revenue" && <Revenue />}
        {tab === "deposits" && <Deposits />}
        {tab === "debtors" && <Debtors />}
      </div>
    </div>
  );
}

function Csv({ path, name }: { path: string; name: string }) {
  const { busy, error, run } = useAction();
  return (
    <div className="space-y-1">
      <Button variant="secondary" loading={busy} onClick={() => run(() => downloadFile(path, name))}>
        <DownloadIcon className="h-4 w-4" /> Download CSV
      </Button>
      <ErrorBox error={error} />
    </div>
  );
}

function useRange() {
  const today = lagosToday();
  const [from, setFrom] = useState(addDays(today, -6));
  const [to, setTo] = useState(today);
  const picker = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="w-44">
        <Input label="From" type="date" max={to} value={from} onChange={(e) => setFrom(e.target.value)} />
      </div>
      <div className="w-44">
        <Input label="To" type="date" max={today} value={to} onChange={(e) => setTo(e.target.value)} />
      </div>
      <div className="flex flex-wrap gap-1">
        {[
          ["Today", 0],
          ["7 days", 6],
          ["30 days", 29],
        ].map(([label, back]) => (
          <Button
            key={label}
            variant="ghost"
            className="min-h-10 px-2.5"
            onClick={() => {
              setFrom(addDays(today, -(back as number)));
              setTo(today);
            }}
          >
            {label}
          </Button>
        ))}
      </div>
    </div>
  );
  return { from, to, picker, query: qs({ from, to }) };
}

function Figure({ label, value, strong }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="rounded-control border border-rule bg-surface px-3 py-2">
      <dt className="text-xs text-muted">{label}</dt>
      <dd className={strong ? "display tnum text-xl font-bold text-ink" : "display tnum text-lg font-bold text-ink"}>{value}</dd>
    </div>
  );
}

function Takings() {
  const range = useRange();
  const report = useApi<TakingsReportDto>(`/api/care/reports/takings${range.query}`);
  const r = report.data;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        {range.picker}
        <Csv path={`/api/care/reports/takings.csv${range.query}`} name={`takings-${range.from}-${range.to}.csv`} />
      </div>
      {report.error && <ApiErrorView error={report.error} onRetry={report.reload} />}
      {!r && !report.error && <PageLoader />}
      {r && (
        <>
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Figure label={`Taken, ${plural(r.count, "payment")}`} value={money(r.totalMinor)} strong />
            {r.byMethod.map((m) => (
              <Figure key={m.method} label={`${methodLabel(m.method)}, ${m.count}`} value={money(m.amountMinor)} />
            ))}
          </dl>
          <p className="text-xs text-muted">Received payments only. A reversed payment is left out, including on the day it was taken.</p>
          {r.days.length > 0 && (
            <CaseSheet label="Takings by day" template="minmax(0,1fr) minmax(0,2fr) auto" head={[{ label: "Day" }, { label: "By method" }, { label: "Total", align: "right" }]}>
              {r.days.map((d) => (
                <li key={d.date} className={caseRowClass}>
                  <span className="tnum font-bold text-ink">{formatDay(d.date)}</span>
                  <span className="tnum col-span-2 text-sm text-slate-700 md:col-span-1">
                    {d.byMethod.map((m) => `${methodLabel(m.method)} ${money(m.amountMinor)}`).join(", ")}
                  </span>
                  <span className="tnum font-bold text-ink md:text-right">{money(d.totalMinor)}</span>
                </li>
              ))}
            </CaseSheet>
          )}
        </>
      )}
    </div>
  );
}

function Revenue() {
  const range = useRange();
  const report = useApi<RevenueReportDto>(`/api/care/reports/revenue${range.query}`);
  const r = report.data;
  const label = (c: string) => PRICE_CATEGORIES.find((x) => x.value === c)?.label ?? c;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        {range.picker}
        <Csv path={`/api/care/reports/revenue.csv${range.query}`} name={`revenue-${range.from}-${range.to}.csv`} />
      </div>
      {report.error && <ApiErrorView error={report.error} onRetry={report.reload} />}
      {!r && !report.error && <PageLoader />}
      {r && (
        <>
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Figure label="Charged" value={money(r.chargedMinor)} />
            <Figure label="Discounts" value={`−${money(r.discountsMinor)}`} />
            <Figure label="Waivers" value={`−${money(r.waiversMinor)}`} />
            <Figure label="Net" value={money(r.netMinor)} strong />
          </dl>
          <CaseSheet label="Revenue by category" template="minmax(0,1fr) auto auto" head={[{ label: "Category" }, { label: "Lines", align: "right" }, { label: "Amount", align: "right" }]}>
            {r.byCategory.map((c) => (
              <li key={c.category} className={caseRowClass}>
                <span className="font-bold text-ink">{label(c.category)}</span>
                <span className="tnum text-sm text-muted md:text-right">{c.lines}</span>
                <span className="tnum font-bold text-ink md:text-right">{money(c.amountMinor)}</span>
              </li>
            ))}
          </CaseSheet>
          <p className="text-xs text-muted">Charges by the day they were made (bed days by their bed day), removed lines left out. Discounts and waivers by the day they were granted.</p>
        </>
      )}
    </div>
  );
}

function Deposits() {
  const report = useApi<DepositsReportDto>("/api/care/reports/deposits");
  const r = report.data;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <p className="text-sm text-muted">Patients admitted now, with their deposits and what their admission bill holds.</p>
        <Csv path="/api/care/reports/deposits.csv" name={`deposits-${lagosToday()}.csv`} />
      </div>
      {report.error && <ApiErrorView error={report.error} onRetry={report.reload} />}
      {!r && !report.error && <PageLoader />}
      {r && (
        <>
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            <Figure label={`Deposits, ${plural(r.count, "admission")}`} value={money(r.depositsMinor)} strong />
            <Figure label="Credit still held" value={money(r.creditMinor)} />
          </dl>
          {r.admissions.length === 0 ? (
            <p className="text-sm text-muted">No one is admitted.</p>
          ) : (
            <CaseSheet
              label="Deposits by admission"
              template="minmax(0,2fr) minmax(0,1fr) auto auto auto"
              head={[{ label: "Patient" }, { label: "Ward" }, { label: "Deposit", align: "right" }, { label: "Charges", align: "right" }, { label: "Held or owed", align: "right" }]}
            >
              {r.admissions.map((a) => (
                <li key={a.admissionId} className={caseRowClass}>
                  <span className="col-span-2 flex min-w-0 flex-wrap items-center gap-2 md:col-span-1">
                    <Wristband number={a.patientNumber} />
                    <span className="font-bold text-ink">{a.patientName}</span>
                  </span>
                  <span className="text-sm text-slate-700">
                    {a.wardName ? `${a.wardName}, ${a.bedLabel}` : "Waiting for a bed"}
                    <span className="block text-xs text-muted">Since {formatStamp(a.admittedAt)}</span>
                  </span>
                  <span className="tnum text-sm md:text-right">{money(a.depositMinor)}</span>
                  <span className="tnum text-sm md:text-right">{money(a.chargesMinor)}</span>
                  <span className="tnum text-sm font-bold md:text-right">
                    {a.creditMinor > 0 ? <span className="text-scrub">{money(a.creditMinor)} held</span> : <span className={a.balanceMinor > 0 ? "text-arterial" : "text-ink"}>{money(a.balanceMinor)} owed</span>}
                  </span>
                </li>
              ))}
            </CaseSheet>
          )}
        </>
      )}
    </div>
  );
}

function Debtors() {
  const report = useApi<CareDebtorsReportDto>("/api/care/reports/debtors");
  const r = report.data;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <p className="text-sm text-muted">Patients with money owing on any bill. The WhatsApp reminder says only the balance and the clinic.</p>
        <Csv path="/api/care/reports/debtors.csv" name={`debtors-${lagosToday()}.csv`} />
      </div>
      {report.error && <ApiErrorView error={report.error} onRetry={report.reload} />}
      {!r && !report.error && <PageLoader />}
      {r && (
        <>
          <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            <Figure label={`Owed, ${plural(r.count, "patient")}`} value={money(r.totalMinor)} strong />
          </dl>
          {r.debtors.length === 0 ? (
            <Card>
              <p className="text-sm text-muted">No one owes the clinic money.</p>
            </Card>
          ) : (
            <CaseSheet
              label="Debtors"
              template="minmax(0,2fr) minmax(0,1fr) auto auto"
              head={[{ label: "Patient" }, { label: "Bills" }, { label: "Owed", align: "right" }, { label: "", align: "right" }]}
            >
              {r.debtors.map((d) => (
                <li key={d.patientId} className={caseRowClass}>
                  <span className="col-span-2 min-w-0 md:col-span-1">
                    <span className="flex flex-wrap items-center gap-2">
                      <Wristband number={d.patientNumber} />
                      <Link href={`/app/bills?patient=${d.patientId}`} className="font-bold text-ink hover:underline">
                        {d.patientName}
                      </Link>
                    </span>
                    <span className="tnum block text-xs text-muted">{d.phone ?? "No phone"}</span>
                  </span>
                  <span className="text-sm text-slate-700">
                    {plural(d.bills, "bill")}
                    <span className="block text-xs text-muted">Oldest {formatStamp(d.oldestBillAt)}</span>
                  </span>
                  <span className="tnum font-bold text-arterial md:text-right">{money(d.balanceMinor)}</span>
                  <span className="md:text-right">
                    {d.whatsAppUrl ? <WhatsAppLink href={d.whatsAppUrl} className="min-h-9">Remind</WhatsAppLink> : <span className="text-xs text-muted">No WhatsApp link</span>}
                  </span>
                </li>
              ))}
            </CaseSheet>
          )}
        </>
      )}
    </div>
  );
}
