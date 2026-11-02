import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { APP_NAME } from "@/lib/config";
import { formatMoney } from "@/lib/money";
import { getPublicOrder } from "@/lib/storefront";
import type { OrderStatus, PublicOrderDto } from "@/lib/types";
import { StoreSkeleton } from "@/components/store/store-shell";
import { PaymentBadge, StatusBadge } from "@/components/badges";
import { WhatsAppIcon } from "@/components/icons";
import { OrderTicket, type TicketRow } from "@/components/order-ticket";

type Props = PageProps<"/o/[code]">;

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { code } = await params;
  return { title: `Order ${code}`, robots: { index: false } };
}

export default function OrderPage(props: Props) {
  return (
    <Suspense fallback={<StoreSkeleton />}>
      <Order {...props} />
    </Suspense>
  );
}

const STEPS: { status: OrderStatus; label: string; text: string }[] = [
  { status: "New", label: "Order placed", text: "The seller has received your order." },
  { status: "Confirmed", label: "Confirmed", text: "The seller confirmed your order." },
  { status: "Paid", label: "Paid", text: "Payment received." },
  { status: "Delivered", label: "Delivered", text: "Your order is complete." },
];

function stepDone(order: PublicOrderDto, step: OrderStatus) {
  // Paid reflects the payment itself: an order can be delivered before it is paid (pay on delivery).
  if (step === "Paid") return order.paymentStatus === "Paid";
  // The "Paid" stage ranks like Confirmed, so it never ticks Delivered.
  const rank: Record<OrderStatus, number> = { New: 0, Confirmed: 1, Paid: 1, Delivered: 2, Cancelled: -1 };
  return rank[order.status] >= rank[step];
}

async function Order({ params }: Props) {
  const { code } = await params;
  const order = await getPublicOrder(code);
  if (!order) notFound();
  const c = order.currency;
  const cancelled = order.status === "Cancelled";
  const placed = new Date(order.createdAt);

  const rows: TicketRow[] = [{ label: "Subtotal", value: formatMoney(order.subtotalMinor, c) }];
  if (order.fulfilment === "Delivery") {
    rows.push({ label: "Delivery", value: order.deliveryFeeMinor === 0 ? "—" : formatMoney(order.deliveryFeeMinor, c) });
  } else {
    rows.push({ label: "Fulfilment", value: "Pickup" });
  }
  if (order.discountMinor > 0) {
    rows.push({ label: "Discount", value: `−${formatMoney(order.discountMinor, c)}`, tone: "discount" });
  }

  return (
    <div className="min-h-screen bg-paper">
      <main className="mx-auto max-w-[560px] space-y-6 px-4 pt-4 pb-10">
        <Link
          href={`/${order.storeSlug}`}
          className="-ml-2 inline-flex min-h-11 items-center gap-1 rounded-control pr-2 text-sm font-semibold text-ink-2 hover:text-ink"
        >
          <svg viewBox="0 0 24 24" width={20} height={20} fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true">
            <path d="m15 18-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          {order.storeName}
        </Link>

        <h1 className="sr-only">Order #{order.number}</h1>

        <OrderTicket
          number={order.number}
          kicker={order.storeName}
          status={
            <>
              <StatusBadge status={order.status} />
              <PaymentBadge status={order.paymentStatus} />
            </>
          }
          meta={
            <span className="flex flex-wrap gap-x-4 gap-y-0.5">
              <span>
                Code <span className="font-semibold text-ink-2">{order.code}</span>
              </span>
              {!Number.isNaN(placed.getTime()) && (
                <span>{placed.toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</span>
              )}
            </span>
          }
          lines={order.items.map((it, i) => ({
            key: String(i),
            quantity: it.quantity,
            name: it.name,
            variant: it.variant,
            amount: formatMoney(it.lineTotalMinor, c),
          }))}
          rows={rows}
          total={formatMoney(order.totalMinor, c)}
          footer={
            <p className="flex justify-between text-xs text-subtle">
              <span>Payment</span>
              <span>{order.paymentStatus}</span>
            </p>
          }
        />

        <div>
          <a
            href={order.whatsappUrl}
            className="flex min-h-13 w-full items-center justify-center gap-2.5 rounded-control bg-leaf px-5 font-semibold text-white transition-colors hover:bg-leaf-hover"
          >
            <WhatsAppIcon />
            Open WhatsApp again
          </a>
          <p className="mt-2 text-sm text-muted">Didn&apos;t send the message? Tap the button to send your order to the seller.</p>
        </div>

        <section className="rounded-panel border border-line bg-surface p-4 sm:p-5" aria-labelledby="status-heading">
          <h2 id="status-heading" className="mb-4 font-display text-lg font-bold text-ink">
            Status
          </h2>
          {cancelled ? (
            <p className="rounded-control bg-hibiscus-soft p-3.5 text-sm text-hibiscus-deep">
              This order was cancelled. Contact the seller on WhatsApp if this is unexpected.
            </p>
          ) : (
            <ol className="space-y-5">
              {STEPS.map((s, i) => {
                const done = stepDone(order, s.status);
                return (
                  <li key={s.status} className="relative flex gap-3">
                    {i < STEPS.length - 1 && (
                      <span
                        className={`absolute top-7 left-[13px] h-[calc(100%-0.75rem)] w-0.5 ${done ? "bg-ink" : "bg-line"}`}
                        aria-hidden="true"
                      />
                    )}
                    <span
                      className={`relative z-10 flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-bold ${
                        done ? "bg-ink text-white" : "border-2 border-line-strong bg-surface text-subtle"
                      }`}
                      aria-hidden="true"
                    >
                      {done ? (
                        <svg viewBox="0 0 24 24" width={14} height={14} fill="none" stroke="currentColor" strokeWidth={3}>
                          <path d="M20 6 9 17l-5-5" strokeLinecap="round" strokeLinejoin="round" />
                        </svg>
                      ) : (
                        i + 1
                      )}
                    </span>
                    <div className="pt-0.5">
                      <p className={`text-sm font-semibold ${done ? "text-ink" : "text-subtle"}`}>
                        {s.label}
                        <span className="sr-only">{done ? " (done)" : " (pending)"}</span>
                      </p>
                      {done && <p className="text-xs text-muted">{s.text}</p>}
                    </div>
                  </li>
                );
              })}
            </ol>
          )}
        </section>

        {order.showBranding && (
          <p className="pt-2 text-center text-xs text-subtle">
            Powered by{" "}
            <Link href="/" className="font-semibold text-ink-2 underline-offset-2 hover:underline">
              {APP_NAME}
            </Link>
          </p>
        )}
      </main>
    </div>
  );
}
