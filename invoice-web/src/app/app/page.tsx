"use client";

import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { useCan } from "@/lib/permissions";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { InvoiceDashboardDto } from "@/lib/types";
import { InvoiceList } from "@/components/doc-rows";
import { ApiErrorView, EmptyState, PageHeader, PageLoader, SummaryStrip, buttonClass, cx, linkClass, panelClass } from "@/components/ui";
import { InvoiceIcon, PlusIcon } from "@/components/icons";

export default function DashboardPage() {
  const auth = useAuth();
  const canEdit = useCan("docs.edit");
  const { data, error, reload } = useApi<InvoiceDashboardDto>("/api/invoice-dashboard");
  const firstName = auth?.user.fullName.split(" ")[0];
  const limit = data?.invoiceLimit ?? -1;

  return (
    <div className="space-y-6">
      <PageHeader
        title={firstName ? `Hi, ${firstName}` : "Dashboard"}
        subtitle={auth?.tenant.name}
        action={
          canEdit && (
            <Link href="/app/invoices/new" className={buttonClass("primary", "hidden lg:inline-flex")}>
              <PlusIcon className="h-4 w-4" /> New invoice
            </Link>
          )
        }
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && (
        <>
          <SummaryStrip
            label="Money at a glance"
            items={[
              { label: "Outstanding", value: formatMoney(data.outstandingMinor, data.currency), sub: "Waiting to be paid", href: "/app/invoices" },
              {
                label: "Overdue",
                value: formatMoney(data.overdueMinor, data.currency),
                sub: `${data.overdueCount} invoice${data.overdueCount === 1 ? "" : "s"}`,
                tone: data.overdueCount > 0 ? "red" : undefined,
                href: "/app/invoices?status=Overdue",
              },
              { label: "Paid this month", value: formatMoney(data.paidThisMonthMinor, data.currency), tone: "green", href: "/app/invoices?status=Paid" },
              { label: "Drafts", value: String(data.draftCount), sub: "Not sent yet", href: "/app/invoices?status=Draft" },
            ]}
          />

          <div className={cx(panelClass, "flex flex-col gap-3 px-4 py-3.5 sm:flex-row sm:items-center sm:gap-6 sm:px-5")}>
            <div className="flex shrink-0 items-baseline justify-between gap-4 text-sm sm:justify-start">
              <span className="text-slate-700">Invoices this month</span>
              <span className="amount font-semibold">
                {data.invoicesThisMonth} / {limit === -1 ? "Unlimited" : limit}
              </span>
            </div>
            {limit > 0 && (
              <>
                <div
                  className="h-1.5 flex-1 overflow-hidden rounded-full bg-desk"
                  role="progressbar"
                  aria-label="Invoices this month"
                  aria-valuemin={0}
                  aria-valuemax={limit}
                  aria-valuenow={data.invoicesThisMonth}
                >
                  <div
                    className={cx(
                      "h-full rounded-full",
                      data.invoicesThisMonth >= limit ? "bg-overdue" : data.invoicesThisMonth / limit >= 0.8 ? "bg-amber" : "bg-ink",
                    )}
                    style={{ width: `${Math.min(100, Math.round((data.invoicesThisMonth / limit) * 100))}%` }}
                  />
                </div>
                {data.invoicesThisMonth >= limit * 0.8 && (
                  <p className="shrink-0 text-xs text-slate-600">
                    Running low?{" "}
                    <Link href="/app/billing" className={linkClass}>
                      Go unlimited with Pro
                    </Link>
                  </p>
                )}
              </>
            )}
          </div>

          <section aria-labelledby="recent-heading">
            <div className="mb-3 flex items-baseline justify-between gap-3">
              <h2 id="recent-heading" className="font-display text-xl font-[560] text-graphite">
                Recent invoices
              </h2>
              <Link href="/app/invoices" className={cx(linkClass, "text-sm")}>
                See all invoices
              </Link>
            </div>
            {data.recentInvoices.length === 0 ? (
              <EmptyState
                icon={<InvoiceIcon width={24} height={24} />}
                title="No invoices yet"
                text="Create your first invoice and send it by email or WhatsApp."
                action={
                  canEdit && (
                    <Link href="/app/invoices/new" className={buttonClass("primary")}>
                      Create invoice
                    </Link>
                  )
                }
              />
            ) : (
              <InvoiceList invoices={data.recentInvoices} baseCurrency={data.currency} />
            )}
          </section>
        </>
      )}
    </div>
  );
}
