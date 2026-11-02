"use client";

import Link from "next/link";
import { useState } from "react";
import { api } from "@/lib/api";
import { useAction } from "@/lib/mutate";
import { useCan } from "@/lib/roles";
import { qs, useApi } from "@/lib/use-api";
import type { ClassDto } from "@/lib/types";
import type { GenerateInvoicesResult, GeneratedInvoiceRefDto, InvoiceSummaryDto } from "@/lib/types/fees";
import {
  ApiErrorView,
  Button,
  EmptyState,
  ErrorBox,
  Notice,
  PageLoader,
  RuledSheet,
  SearchField,
  Select,
  linkClass,
  ruledRowClass,
} from "@/components/ui";
import { InvoiceStatusBadge, TermSelect, dueText, money, useTermChoice } from "../fees-shared";

const TAKE = 2000;

export default function InvoicesPage() {
  const canEdit = useCan("fees.manage");
  const t = useTermChoice();
  const [classId, setClassId] = useState("");
  const [status, setStatus] = useState("");
  const [q, setQ] = useState("");
  const [result, setResult] = useState<GenerateInvoicesResult | null>(null);
  const classes = useApi<ClassDto[]>(t.term ? `/api/school/classes${qs({ sessionId: t.term.sessionId })}` : null);
  const list = useApi<InvoiceSummaryDto[]>(t.termId ? `/api/school/invoices${qs({ termId: t.termId, classId, status, take: TAKE })}` : null);
  const gen = useAction();

  async function generate() {
    if (!t.term) return;
    const ok = window.confirm(
      `Generate invoices for ${t.term.label}? Each active student gets one from their level's fee structure. Invoices with no payments are refreshed; invoices with payments are left alone.`,
    );
    if (!ok) return;
    const res = await gen.run(() => api<GenerateInvoicesResult>("/api/school/invoices/generate", { method: "POST", body: { termId: t.termId } }));
    if (res.ok) {
      setResult(res.data);
      list.reload();
    }
  }

  if (t.error) return <ApiErrorView error={t.error} onRetry={t.reload} />;
  if (!t.loaded) return <PageLoader />;
  if (t.terms.length === 0) return <EmptyState title="No terms yet" text="Add the session and its terms on the setup page first." />;

  const needle = q.trim().toLowerCase();
  const rows = (list.data ?? []).filter((i) => !needle || i.studentName.toLowerCase().includes(needle) || i.admissionNo.toLowerCase().includes(needle) || i.number.toLowerCase().includes(needle));
  // Built from the result itself, not the filtered list, so every invoice it mentions gets a link.
  const named = (ids: string[]) => (result?.invoices ?? []).filter((i) => ids.includes(i.id));
  const review = result ? named(result.needsReview) : [];
  const leftAlone = result ? named(result.withPayments) : [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-2">
        <TermSelect terms={t.terms} value={t.termId} onChange={(id) => { t.setTermId(id); setClassId(""); setResult(null); }} />
        <div className="w-40">
          <Select label="Class" srLabel value={classId} onChange={(e) => setClassId(e.target.value)}>
            <option value="">All classes</option>
            {classes.data?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </div>
        <div className="w-36">
          <Select label="Status" srLabel value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">Any status</option>
            <option value="Unpaid">Unpaid</option>
            <option value="PartlyPaid">Part paid</option>
            <option value="Paid">Paid</option>
            <option value="Cancelled">Cancelled</option>
          </Select>
        </div>
        <SearchField id="invoice-search" label="Search invoices" placeholder="Name, admission or invoice number" value={q} onChange={setQ} className="min-w-52 flex-1" />
        {canEdit && (
          <Button onClick={generate} loading={gen.busy}>
            Generate invoices
          </Button>
        )}
      </div>

      <ErrorBox error={gen.error} />
      {result && (
        <Notice tone={result.needsReview.length || result.withPayments.length ? "warning" : "success"}>
          <p>
            {result.created} new, {result.refreshed} refreshed, {result.skipped} skipped (their level has no fees for this term).
          </p>
          {result.needsReview.length > 0 && (
            <>
              <p className="mt-1">
                {result.needsReview.length} {result.needsReview.length === 1 ? "invoice needs" : "invoices need"} checking: a discount no longer fits, the
                instalments were cleared, or the level has no fees for this term. Open each one, make any change, then mark it reviewed.
              </p>
              <InvoiceLinks items={review} />
            </>
          )}
          {result.withPayments.length > 0 && (
            <>
              <p className="mt-1">
                {result.withPayments.length} {result.withPayments.length === 1 ? "invoice was" : "invoices were"} left alone because{" "}
                {result.withPayments.length === 1 ? "it has" : "they have"} payments, and the fee structure has changed since. Edit{" "}
                {result.withPayments.length === 1 ? "it" : "them"} by hand if the family should pay the new amounts.
              </p>
              <InvoiceLinks items={leftAlone} />
            </>
          )}
        </Notice>
      )}

      {list.error && <ApiErrorView error={list.error} onRetry={list.reload} />}
      {!list.data && !list.error && <PageLoader />}
      {list.data && list.data.length === 0 && !classId && !status && (
        <EmptyState
          title="No invoices for this term"
          text="Set the fee structure for each level, then generate invoices. Each active student gets one."
          action={
            <Link href="/app/fees/structure" className={linkClass}>
              Set the fee structure
            </Link>
          }
        />
      )}
      {list.data && rows.length === 0 && (list.data.length > 0 || classId || status) && <p className="text-sm text-muted">No invoices match. Try another class, status or name.</p>}
      {rows.length > 0 && (
        <>
          <p className="tnum text-sm text-muted" aria-live="polite">
            {rows.length.toLocaleString("en-NG")} {rows.length === 1 ? "invoice" : "invoices"}, balance {money(rows.reduce((s, i) => s + (i.status === "Cancelled" ? 0 : i.balance), 0))}
          </p>
          {list.data && list.data.length >= TAKE && <Notice tone="warning">Showing the first {TAKE.toLocaleString("en-NG")} invoices. Filter by class to see the rest.</Notice>}
          <RuledSheet
            label="Invoices"
            busy={list.loading}
            template="minmax(0,1fr) 8rem 8rem 8rem 7rem"
            head={[{ label: "Student" }, { label: "Class" }, { label: "Total", align: "right" }, { label: "Balance", align: "right" }, { label: "Status" }]}
          >
            {rows.map((i) => (
              <li key={i.id} className={ruledRowClass}>
                <span className="min-w-0">
                  <Link href={`/app/fees/invoices/${i.id}`} className="font-bold text-ink hover:text-blazer hover:underline">
                    {i.studentName}
                  </Link>
                  <span className="tnum block text-xs text-muted">
                    {i.number} · {i.admissionNo} · {dueText(i.dueDate)}
                  </span>
                </span>
                <span className="col-start-1 text-sm text-slate-700 md:col-start-auto">{i.className}</span>
                <span className="tnum hidden text-right text-sm md:block">{money(i.total)}</span>
                <span className="tnum col-start-2 row-start-1 text-right text-sm font-semibold md:col-start-auto md:row-start-auto">
                  {i.status === "Cancelled" ? <span aria-label="No balance: cancelled">—</span> : money(i.balance)}
                </span>
                <span className="col-start-2 row-start-2 justify-self-end md:col-start-auto md:row-start-auto md:justify-self-start">
                  <InvoiceStatusBadge status={i.status} />
                </span>
              </li>
            ))}
          </RuledSheet>
        </>
      )}
    </div>
  );
}

function InvoiceLinks({ items }: { items: GeneratedInvoiceRefDto[] }) {
  if (items.length === 0) return null;
  return (
    <ul className="mt-1 list-disc pl-5">
      {items.map((i) => (
        <li key={i.id}>
          <Link href={`/app/fees/invoices/${i.id}`} className={linkClass}>
            {i.studentName}, {i.number}
          </Link>
        </li>
      ))}
    </ul>
  );
}