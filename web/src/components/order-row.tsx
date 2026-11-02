"use client";

import Link from "next/link";
import { formatMoney } from "@/lib/money";
import type { OrderSummaryDto } from "@/lib/types";
import { PaymentBadge, StatusBadge, formatDate } from "./ui";

const cols = "md:grid md:grid-cols-[4.5rem_minmax(0,1.5fr)_minmax(0,1fr)_minmax(0,11rem)_7.5rem] md:items-center md:gap-4";

/** Column labels for the order table (desktop only; rows stack on small screens). */
export function OrderTableHead() {
  return (
    <div className={`hidden border-b border-line px-4 py-2.5 text-xs font-medium text-subtle ${cols}`} aria-hidden="true">
      <span>Order</span>
      <span>Customer</span>
      <span>Items</span>
      <span>Status</span>
      <span className="text-right">Total</span>
    </div>
  );
}

export function OrderRow({ order }: { order: OrderSummaryDto }) {
  const items = `${order.itemCount} item${order.itemCount === 1 ? "" : "s"}`;
  return (
    <li>
      <Link
        href={`/dashboard/orders/${order.id}`}
        className={`group block px-4 py-3.5 transition-colors hover:bg-paper focus-visible:-outline-offset-3 ${cols}`}
      >
        {/* Mobile: number + name + total on one line */}
        <div className="flex items-baseline justify-between gap-3 md:contents">
          <span className="flex min-w-0 items-baseline gap-2 md:contents">
            <span className="font-display text-lg font-bold text-ink num">#{order.number}</span>
            <span className="min-w-0 md:block">
              <span className="block truncate font-medium text-ink">{order.customerName}</span>
              <span className="hidden text-xs text-subtle md:block">{formatDate(order.createdAt)}</span>
            </span>
          </span>
          <span className="shrink-0 font-semibold text-ink num md:order-last md:text-right">
            {formatMoney(order.totalMinor, order.currency)}
          </span>
        </div>

        <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1.5 text-xs text-muted md:mt-0 md:contents">
          <span className="md:hidden">{formatDate(order.createdAt)}</span>
          <span className="flex gap-3 text-sm md:text-ink-2">
            <span>{items}</span>
            <span className="text-subtle">{order.fulfilment}</span>
          </span>
          <span className="flex flex-wrap gap-1.5 max-md:ml-auto">
            <StatusBadge status={order.status} />
            {order.status !== "Cancelled" && <PaymentBadge status={order.paymentStatus} />}
          </span>
        </div>
      </Link>
    </li>
  );
}
