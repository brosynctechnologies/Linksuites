"use client";

import Link from "next/link";
import { useState } from "react";
import { qs, useApi } from "@/lib/use-api";
import type { CollectionsBy, CollectionsDto, FinanceSummaryDto } from "@/lib/types/fees";
import { ApiErrorView, Card, EmptyState, PageLoader, RuledSheet, Segmented, SectionTitle, linkClass, ruledRowClass } from "@/components/ui";
import { TermSelect, money, useTermChoice } from "./fees-shared";

/** The finance dashboard for a term (expected, collected, outstanding, expenses) and collections by class, fee item or method. */
export default function FeesOverviewPage() {
  const t = useTermChoice();
  const [by, setBy] = useState<CollectionsBy>("class");
  const summary = useApi<FinanceSummaryDto>(t.termId ? `/api/school/finance/summary${qs({ termId: t.termId })}` : null);
  const collections = useApi<CollectionsDto>(t.termId ? `/api/school/finance/collections${qs({ termId: t.termId, by })}` : null);

  if (t.error) return <ApiErrorView error={t.error} onRetry={t.reload} />;
  if (!t.loaded) return <PageLoader />;
  if (t.terms.length === 0)
    return (
      <EmptyState
        title="No terms yet"
        text="Fees are set per term. Add the session and its terms on the setup page first."
        action={
          <Link href="/app/setup" className={linkClass}>
            Go to setup
          </Link>
        }
      />
    );

  const s = summary.data;
  const rate = s && s.expected > 0 ? Math.round((s.collected / s.expected) * 100) : null;
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <TermSelect terms={t.terms} value={t.termId} onChange={t.setTermId} />
        <Link href="/app/fees/invoices" className={linkClass}>
          Go to invoices
        </Link>
      </div>

      {summary.error && <ApiErrorView error={summary.error} onRetry={summary.reload} />}
      {!s && !summary.error && <PageLoader />}
      {s && (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          <Figure label="Expected" value={money(s.expected)} note="Invoices for the term, not counting cancelled ones" />
          <Figure label="Collected" value={money(s.collected)} note={rate === null ? "Nothing invoiced yet" : `${rate}% of expected`} />
          <Figure label="Outstanding" value={money(s.outstanding)} note="Balances still owed" />
          <Figure label="Expenses" value={money(s.expenses)} note="Dated within the term" />
        </div>
      )}

      <Card>
        <SectionTitle>Collected this term</SectionTitle>
        <Segmented
          label="Group collections by"
          className="mb-4 max-w-sm"
          value={by}
          onChange={setBy}
          items={[
            { value: "class", label: "By class" },
            { value: "item", label: "By fee item" },
            { value: "method", label: "By method" },
          ]}
        />
        {collections.error && <ApiErrorView error={collections.error} onRetry={collections.reload} />}
        {!collections.data && !collections.error && <PageLoader />}
        {collections.data && collections.data.rows.length === 0 && <p className="text-sm text-muted">No payments received for this term yet.</p>}
        {collections.data && collections.data.rows.length > 0 && (
          <RuledSheet label="Collections" template="minmax(0,1fr) 10rem" head={[{ label: by === "class" ? "Class" : by === "item" ? "Fee item" : "Method" }, { label: "Collected", align: "right" }]} busy={collections.loading}>
            {collections.data.rows.map((r) => (
              <li key={r.key} className={ruledRowClass}>
                <span className="font-bold text-ink">{r.label}</span>
                <span className="tnum text-right font-semibold">{money(r.amountMinor)}</span>
              </li>
            ))}
            <li className={ruledRowClass}>
              <span className="font-bold text-ink">Total</span>
              <span className="tnum text-right font-bold">{money(collections.data.total)}</span>
            </li>
          </RuledSheet>
        )}
        {by === "item" && (
          <p className="mt-3 text-xs text-muted">Each payment is shared across its invoice&apos;s fee items in proportion to their amounts.</p>
        )}
      </Card>
    </div>
  );
}

function Figure({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <Card className="p-4">
      <p className="text-sm text-muted">{label}</p>
      <p className="slab tnum mt-1 text-xl font-bold text-ink sm:text-2xl">{value}</p>
      <p className="mt-1 text-xs text-muted">{note}</p>
    </Card>
  );
}
