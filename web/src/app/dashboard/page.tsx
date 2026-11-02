"use client";

import Link from "next/link";
import { useAuth } from "@/lib/auth";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { DashboardDto } from "@/lib/types";
import { OrderRow, OrderTableHead } from "@/components/order-row";
import { OrderTicket } from "@/components/order-ticket";
import {
  ApiErrorView,
  EmptyState,
  PageHeader,
  SectionHeading,
  Skeleton,
  StatusBadge,
  buttonClass,
  cx,
  formatDate,
  listClass,
} from "@/components/ui";
import { BoxIcon, OrdersIcon, ShareIcon } from "@/components/icons";

function Stat({ label, value, href, highlight }: { label: string; value: string | number; href?: string; highlight?: boolean }) {
  const body = (
    <>
      <span className="block text-xs font-medium text-muted">{label}</span>
      <span className={cx("mt-1 block font-display text-xl font-bold text-ink num sm:text-2xl", highlight && "text-marigold-deep")}>
        {value}
      </span>
    </>
  );
  const cls = "block min-h-11 px-4 py-3.5 sm:px-5";
  return href ? (
    <Link href={href} className={cx(cls, "transition-colors hover:bg-paper focus-visible:-outline-offset-3")}>
      {body}
    </Link>
  ) : (
    <div className={cls}>{body}</div>
  );
}

export default function DashboardHome() {
  const auth = useAuth();
  const { data, error, loading, reload } = useApi<DashboardDto>("/api/dashboard");
  const first = auth?.user.fullName.split(" ")[0];
  const fresh = data?.recentOrders.filter((o) => o.status === "New") ?? [];

  return (
    <div>
      <PageHeader
        title={first ? `Hi, ${first}` : "Dashboard"}
        subtitle="Here's how your store is doing."
        action={
          <Link href="/dashboard/share" className={buttonClass("secondary")}>
            <ShareIcon width={18} height={18} />
            Share store
          </Link>
        }
      />
      {error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && loading && (
        <div role="status" className="space-y-6">
          <span className="sr-only">Loading your dashboard…</span>
          <Skeleton className="h-20 w-full rounded-panel" />
          <div className="flex gap-4">
            <Skeleton className="h-44 w-64 shrink-0 rounded-panel" />
            <Skeleton className="h-44 w-64 shrink-0 rounded-panel" />
          </div>
        </div>
      )}
      {data && (
        <>
          <div className="grid grid-cols-2 gap-px overflow-hidden rounded-panel border border-line bg-line lg:grid-cols-5 [&>*]:bg-surface">
            <div>
              <Stat label="Orders today" value={data.todayOrders} />
            </div>
            <div>
              <Stat label="Sales today" value={formatMoney(data.todaySalesMinor, data.currency)} />
            </div>
            <div>
              <Stat label="Sales this month" value={formatMoney(data.monthSalesMinor, data.currency)} />
            </div>
            <div>
              <Stat label="New orders" value={data.newOrders} href="/dashboard/orders?status=New" highlight={data.newOrders > 0} />
            </div>
            <div className="col-span-2 lg:col-span-1">
              <Stat label="Products" value={data.productCount} href="/dashboard/products" />
            </div>
          </div>

          {data.productCount === 0 && (
            <div className="mt-6">
              <EmptyState
                icon={<BoxIcon />}
                title="Add your first product"
                text="Your store is live. Add a product with a photo and price so customers can order."
                action={
                  <Link href="/dashboard/products/new" className={buttonClass("primary")}>
                    Add product
                  </Link>
                }
              />
            </div>
          )}

          <section className="mt-8" aria-labelledby="new-orders">
            <SectionHeading
              id="new-orders"
              title="New orders"
              action={
                data.newOrders > 0 ? (
                  <Link href="/dashboard/orders?status=New" className="flex min-h-11 items-center text-sm font-semibold text-ink underline-offset-4 hover:underline">
                    View all new orders
                  </Link>
                ) : undefined
              }
            />
            {fresh.length === 0 ? (
              <p className="rounded-panel border border-line bg-surface px-4 py-4 text-sm text-muted sm:px-5">
                Nothing waiting. New orders land here as soon as a customer sends one.
              </p>
            ) : (
              <ul
                className="no-scrollbar -mx-4 flex snap-x snap-mandatory gap-4 overflow-x-auto px-4 pt-1 pb-4 sm:-mx-6 sm:px-6 lg:mx-0 lg:grid lg:grid-cols-3 lg:overflow-visible lg:px-0"
                aria-label="New orders"
              >
                {fresh.map((o) => (
                  <li key={o.id} className="w-[17rem] shrink-0 snap-start lg:w-auto">
                    <OrderTicket
                      compact
                      href={`/dashboard/orders/${o.id}`}
                      number={o.number}
                      kicker={formatDate(o.createdAt)}
                      status={<StatusBadge status={o.status} />}
                      meta={
                        <span className="block">
                          <span className="block truncate font-medium text-ink">{o.customerName}</span>
                          <span className="mt-0.5 flex gap-3">
                            <span>
                              {o.itemCount} item{o.itemCount === 1 ? "" : "s"}
                            </span>
                            <span>{o.fulfilment}</span>
                          </span>
                        </span>
                      }
                      total={formatMoney(o.totalMinor, o.currency)}
                    />
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="mt-6" aria-labelledby="recent-orders">
            <SectionHeading
              id="recent-orders"
              title="Recent orders"
              action={
                <Link href="/dashboard/orders" className="flex min-h-11 items-center text-sm font-semibold text-ink underline-offset-4 hover:underline">
                  All orders
                </Link>
              }
            />
            {data.recentOrders.length === 0 ? (
              <EmptyState
                icon={<OrdersIcon />}
                title="No orders yet"
                text="Share your store link on your WhatsApp status and Instagram bio to get your first order."
                action={
                  <Link href="/dashboard/share" className={buttonClass("primary")}>
                    Get my store link
                  </Link>
                }
              />
            ) : (
              <div className={listClass}>
                <OrderTableHead />
                <ul className="divide-y divide-line">
                  {data.recentOrders.map((o) => (
                    <OrderRow key={o.id} order={o} />
                  ))}
                </ul>
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}
