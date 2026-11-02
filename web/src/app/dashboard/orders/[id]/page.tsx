"use client";

import { useParams } from "next/navigation";
import { useState } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { formatMoney } from "@/lib/money";
import { useApi } from "@/lib/use-api";
import type { OrderDto, OrderStatus, PaymentStatus } from "@/lib/types";
import { OrderTicket, type TicketRow } from "@/components/order-ticket";
import {
  ApiErrorView,
  Button,
  ErrorBox,
  Input,
  PageHeader,
  PageLoader,
  Panel,
  PaymentBadge,
  StatusBadge,
  buttonClass,
  formatDate,
} from "@/components/ui";
import { CheckIcon, WhatsAppIcon } from "@/components/icons";

const ACTIONS: { status: OrderStatus; label: string; variant: "primary" | "secondary" | "danger" }[] = [
  { status: "Confirmed", label: "Confirm", variant: "primary" },
  { status: "Delivered", label: "Mark delivered", variant: "secondary" },
  { status: "Cancelled", label: "Cancel order", variant: "danger" },
];

export default function OrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data: order, error, loading, reload, setData } = useApi<OrderDto>(`/api/orders/${id}`);
  const [busy, setBusy] = useState<string | null>(null);
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const [note, setNote] = useState("");

  async function act(key: string, fn: () => Promise<OrderDto>) {
    setBusy(key);
    setActionError(null);
    try {
      setData(await fn());
      setNote("");
    } catch (e) {
      setActionError(toApiError(e));
    } finally {
      setBusy(null);
    }
  }

  const changeStatus = (status: OrderStatus) => {
    if (
      status === "Cancelled" &&
      !window.confirm("Cancel this order? Its items go back into stock, and a cancelled order can't be reopened.")
    )
      return;
    act(status, () =>
      api<OrderDto>(`/api/orders/${id}/status`, { method: "PATCH", body: { status, note: note.trim() || null } }),
    );
  };
  const setPayment = (paymentStatus: PaymentStatus) =>
    act("payment", () => api<OrderDto>(`/api/orders/${id}/payment`, { method: "PATCH", body: { paymentStatus } }));

  if (!order) {
    return (
      <div>
        <PageHeader title="Order" back="/dashboard/orders" />
        {error ? <ApiErrorView error={error} onRetry={reload} /> : loading && <PageLoader label="Loading order…" rows={3} />}
      </div>
    );
  }

  const c = order.currency;

  const rows: TicketRow[] = [{ label: "Subtotal", value: formatMoney(order.subtotalMinor, c) }];
  if (order.fulfilment === "Delivery") {
    rows.push({
      label: `Delivery${order.deliveryZoneName ? ` (${order.deliveryZoneName})` : ""}`,
      value: formatMoney(order.deliveryFeeMinor, c),
    });
  }
  if (order.discountMinor > 0) {
    rows.push({
      label: `Discount${order.discountCode ? ` (${order.discountCode})` : ""}`,
      value: `−${formatMoney(order.discountMinor, c)}`,
      tone: "discount",
    });
  }

  return (
    <div>
      <PageHeader
        title={`Order #${order.number}`}
        back="/dashboard/orders"
        subtitle={
          <span className="flex flex-wrap gap-x-4 gap-y-1">
            <span>{formatDate(order.createdAt)}</span>
            <span>
              Code <span className="font-medium text-ink-2">{order.code}</span>
            </span>
          </span>
        }
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem] lg:items-start">
        {/* Ticket */}
        <div className="lg:col-start-1 lg:row-start-1">
          <OrderTicket
            number={order.number}
            kicker={order.customerName}
            status={
              <>
                <StatusBadge status={order.status} />
                <PaymentBadge status={order.paymentStatus} />
              </>
            }
            meta={<span className="text-ink-2">{order.fulfilment}</span>}
            lines={order.items.map((it, i) => ({
              key: `${it.productId}-${it.variantId ?? ""}-${i}`,
              quantity: it.quantity,
              name: it.name,
              variant: it.variant,
              detail: `${formatMoney(it.unitPriceMinor, c)} each`,
              amount: formatMoney(it.lineTotalMinor, c),
            }))}
            rows={rows}
            total={formatMoney(order.totalMinor, c)}
          />
        </div>

        {/* Actions */}
        <div className="space-y-4 lg:sticky lg:top-8 lg:col-start-2 lg:row-span-3 lg:row-start-1">
          <a
            href={order.customerWhatsappUrl}
            target="_blank"
            rel="noopener noreferrer"
            className={buttonClass("whatsapp", "w-full min-h-12 text-base")}
          >
            <WhatsAppIcon /> Chat customer on WhatsApp
          </a>

          <Panel>
            <h2 className="font-display text-lg font-bold text-ink">Update order</h2>
            <div className="mt-3 grid gap-2">
              {ACTIONS.map((a) => (
                <Button
                  key={a.status}
                  variant={a.variant}
                  loading={busy === a.status}
                  disabled={!!busy || order.status === a.status || order.status === "Cancelled"}
                  onClick={() => changeStatus(a.status)}
                >
                  {order.status === a.status ? (
                    <>
                      <CheckIcon width={18} height={18} /> {a.status}
                    </>
                  ) : (
                    a.label
                  )}
                </Button>
              ))}
            </div>
            {order.status === "Cancelled" && (
              <p className="mt-3 text-sm text-muted">This order was cancelled and its items went back into stock. It can&apos;t be reopened.</p>
            )}
            <div className="mt-4">
              <Input label="Note for status change (optional)" value={note} onChange={(e) => setNote(e.target.value)} placeholder="e.g. Rider on the way" />
            </div>
            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-line pt-4">
              <span className="flex items-center gap-2 text-sm text-ink-2">
                Payment <PaymentBadge status={order.paymentStatus} />
              </span>
              {order.paymentStatus === "Paid" ? (
                <Button variant="secondary" loading={busy === "payment"} disabled={!!busy} onClick={() => setPayment("Unpaid")}>
                  Mark unpaid
                </Button>
              ) : (
                <Button variant="secondary" loading={busy === "payment"} disabled={!!busy} onClick={() => setPayment("Paid")}>
                  Mark paid
                </Button>
              )}
            </div>
            {actionError && (
              <div className="mt-3">
                <ErrorBox error={actionError} />
              </div>
            )}
          </Panel>
        </div>

        {/* Customer */}
        <Panel className="lg:col-start-1 lg:row-start-2">
          <h2 className="font-display text-lg font-bold text-ink">Customer</h2>
          <dl className="mt-3 grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
            <div>
              <dt className="text-xs text-subtle">Name</dt>
              <dd className="font-medium text-ink">{order.customerName}</dd>
            </div>
            <div>
              <dt className="text-xs text-subtle">Phone</dt>
              <dd>
                <a href={`tel:${order.phone}`} className="-my-2 inline-flex min-h-11 items-center font-medium text-ink underline underline-offset-4">
                  {order.phone}
                </a>
              </dd>
            </div>
            {order.fulfilment === "Delivery" ? (
              <div className="sm:col-span-2">
                <dt className="text-xs text-subtle">Delivery address</dt>
                <dd className="whitespace-pre-line text-ink">{order.address || "Not given"}</dd>
              </div>
            ) : (
              <div className="sm:col-span-2">
                <dt className="text-xs text-subtle">Fulfilment</dt>
                <dd className="text-ink">Customer will pick up</dd>
              </div>
            )}
            {order.note && (
              <div className="sm:col-span-2">
                <dt className="text-xs text-subtle">Note</dt>
                <dd className="whitespace-pre-line text-ink">{order.note}</dd>
              </div>
            )}
          </dl>
        </Panel>

        {/* History */}
        {order.history.length > 0 && (
          <Panel className="lg:col-start-1 lg:row-start-3">
            <h2 className="font-display text-lg font-bold text-ink">History</h2>
            <ol className="mt-3 space-y-4 border-l-2 border-line pl-5">
              {order.history.map((h, i) => (
                <li key={i} className="relative text-sm">
                  <span className="absolute top-1.5 -left-[1.6rem] h-2.5 w-2.5 rounded-full bg-ink ring-4 ring-surface" aria-hidden="true" />
                  <p className="flex flex-wrap items-baseline gap-x-3">
                    <span className="font-medium text-ink">{h.status}</span>
                    <span className="text-xs text-subtle">{formatDate(h.createdAt)}</span>
                  </p>
                  {h.note && <p className="mt-0.5 text-ink-2">{h.note}</p>}
                </li>
              ))}
            </ol>
          </Panel>
        )}
      </div>
    </div>
  );
}
