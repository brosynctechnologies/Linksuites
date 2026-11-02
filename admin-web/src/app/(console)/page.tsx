"use client";

import Link from "next/link";
import { RequireArea, useAdmin } from "@/components/console";
import { ErrorPanel, Loading, PageHeader, Panel, StatusBadge, TableScroll, cx } from "@/components/ui";
import { ago, money, num } from "@/lib/format";
import { productName } from "@/lib/products";
import { can } from "@/lib/roles";
import { jobHealth } from "@/lib/status";
import type { OverviewDto } from "@/lib/types";
import { useApi } from "@/lib/use-api";
import { useNow } from "@/lib/use-now";

export default function OverviewPage() {
  return (
    <RequireArea area="overview">
      <Overview />
    </RequireArea>
  );
}

function Overview() {
  const { data, error, reload } = useApi<OverviewDto>("/api/platform/overview");
  const { user } = useAdmin();
  const now = useNow(60_000);
  const role = user.platformRole;

  if (!data) {
    return (
      <>
        <PageHeader title="Overview" />
        {error ? <ErrorPanel error={error} onRetry={reload} /> : <Loading />}
      </>
    );
  }

  const totals = data.products.reduce(
    (t, p) => ({ businesses: t.businesses + p.businesses, newThisMonth: t.newThisMonth + p.newThisMonth, paying: t.paying + p.paying, suspended: t.suspended + p.suspended }),
    { businesses: 0, newThisMonth: 0, paying: 0, suspended: 0 },
  );
  const q = data.queues;
  const queues = [
    { label: "Listings", n: q.listings, href: "/moderation/listings", ok: can(role, "moderation") },
    { label: "Courses", n: q.courses, href: "/moderation/courses", ok: can(role, "moderation") },
    { label: "Verifications", n: q.verifications, href: "/moderation/verifications", ok: can(role, "moderation") },
    { label: "Reports", n: q.reports, href: "/moderation/reports", ok: can(role, "moderation") },
    { label: "Reviews", n: q.reviews, href: "/moderation/reviews", ok: can(role, "moderation") },
    { label: "Payouts", n: q.payouts, href: "/money/payouts", ok: can(role, "money"), extra: q.payouts ? money(q.payoutsMinor) : undefined },
  ];

  return (
    <>
      <PageHeader title="Overview" subtitle="Figures across every product. Fees are for this calendar month." />
      {error && <ErrorPanel error={error} onRetry={reload} className="mb-4" />}
      <div className="grid gap-4 xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <Panel title="Businesses by product" flush>
          <TableScroll label="Businesses by product">
            <table className="data-table min-w-[520px]">
              <thead>
                <tr>
                  <th>Product</th>
                  <th className="num">Businesses</th>
                  <th className="num">New this month</th>
                  <th className="num">Paying</th>
                  <th className="num">Suspended</th>
                </tr>
              </thead>
              <tbody>
                {data.products.map((p) => (
                  <tr key={p.product}>
                    <td>
                      {can(role, "businesses") ? (
                        <Link href={`/businesses?product=${p.product}`} className="font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
                          {productName(p.product)}
                        </Link>
                      ) : (
                        <span className="font-medium">{productName(p.product)}</span>
                      )}
                    </td>
                    <td className="num">{num(p.businesses)}</td>
                    <td className="num">{num(p.newThisMonth)}</td>
                    <td className="num">{num(p.paying)}</td>
                    <td className={cx("num", p.suspended > 0 && "font-semibold text-stop")}>{num(p.suspended)}</td>
                  </tr>
                ))}
                <tr className="font-semibold">
                  <td>All products</td>
                  <td className="num">{num(totals.businesses)}</td>
                  <td className="num">{num(totals.newThisMonth)}</td>
                  <td className="num">{num(totals.paying)}</td>
                  <td className="num">{num(totals.suspended)}</td>
                </tr>
              </tbody>
            </table>
          </TableScroll>
        </Panel>

        <Panel title="Revenue">
          <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <MrrFigure data={data} />
            <Figure label="Course fees" value={money(data.fees.courseFeesMinor)} />
            <Figure label="Short-let commission" value={money(data.fees.shortLetCommissionMinor)} />
            <Figure label="Boosts" value={money(data.fees.boostsMinor)} />
            <Figure label="Fees this month" value={money(data.fees.totalMinor)} strong />
          </dl>
        </Panel>

        <Panel title="Queues" flush>
          <ul className="grid grid-cols-2 sm:grid-cols-3">
            {queues.map((x) => {
              const body = (
                <>
                  <span className="text-sm text-muted">{x.label}</span>
                  <span className={cx("tnum block text-xl font-semibold", x.n > 0 ? "text-graphite" : "text-muted")}>{num(x.n)}</span>
                  {x.extra && <span className="tnum block text-xs text-muted">{x.extra}</span>}
                </>
              );
              return (
                <li key={x.label} className="border-b border-line-soft sm:border-r [&:nth-child(3n)]:sm:border-r-0">
                  {x.ok ? (
                    <Link href={x.href} className="block min-h-16 px-4 py-3 hover:bg-concrete-2">
                      {body}
                    </Link>
                  ) : (
                    <div className="min-h-16 px-4 py-3">{body}</div>
                  )}
                </li>
              );
            })}
          </ul>
        </Panel>

        <Panel
          title="Jobs and email"
          flush
          action={
            <Link href="/health" className="text-sm font-medium underline decoration-line underline-offset-2 hover:decoration-graphite">
              Health
            </Link>
          }
        >
          <div className="flex items-center justify-between gap-2 border-b border-line-soft px-4 py-2.5 text-sm">
            <span>Failed emails, last 24 hours</span>
            <span className={cx("tnum font-semibold", data.failedEmails24h > 0 ? "text-stop" : "text-ok")}>{num(data.failedEmails24h)}</span>
          </div>
          {data.jobs.length === 0 ? (
            <p className="px-4 py-3 text-sm text-muted">No job runs recorded yet.</p>
          ) : (
            <ul>
              {data.jobs.map((j) => {
                const h = jobHealth(j, now || undefined);
                return (
                  <li key={j.name} className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line-soft px-4 py-2 text-sm last:border-b-0">
                    <span className="min-w-20 font-medium capitalize">{j.name}</span>
                    <StatusBadge tone={h.tone}>{h.label}</StatusBadge>
                    <span className="ml-auto text-xs text-muted">Last success {now ? ago(j.lastSucceededAt, now) : "—"}</span>
                  </li>
                );
              })}
            </ul>
          )}
        </Panel>
      </div>
    </>
  );
}

/** MRR in each currency businesses pay in, NGN first. Each figure is what businesses actually paid, not list price. */
function MrrFigure({ data }: { data: OverviewDto }) {
  const rows = (data.mrrByCurrency?.length ? data.mrrByCurrency : [{ currency: "NGN", monthlyMinor: data.mrrMinor }])
    .slice()
    .sort((a, b) => (a.currency === "NGN" ? -1 : b.currency === "NGN" ? 1 : a.currency.localeCompare(b.currency)));
  const [first, ...rest] = rows;
  return (
    <div className="col-span-2 border-b border-line-soft pb-3">
      <dt className="text-muted">Monthly recurring revenue</dt>
      <dd className="tnum text-2xl font-semibold">
        {money(first.monthlyMinor, first.currency)}
        {rest.length > 0 && <span className="ml-1.5 text-sm font-medium text-muted">{first.currency}</span>}
      </dd>
      {rest.map((r) => (
        <dd key={r.currency} className="tnum text-lg font-semibold">
          {money(r.monthlyMinor, r.currency)}
          <span className="ml-1.5 text-sm font-medium text-muted">{r.currency}</span>
        </dd>
      ))}
      <dd className="text-xs text-muted">Paid plans at the price each business paid, not comps</dd>
    </div>
  );
}

function Figure({ label, value, hint, wide, strong }: { label: string; value: string; hint?: string; wide?: boolean; strong?: boolean }) {
  return (
    <div className={cx(wide && "col-span-2 border-b border-line-soft pb-3", strong && "border-t border-line-soft pt-2")}>
      <dt className="text-muted">{label}</dt>
      <dd className={cx("tnum font-semibold", wide ? "text-2xl" : "text-lg")}>{value}</dd>
      {hint && <dd className="text-xs text-muted">{hint}</dd>}
    </div>
  );
}
