"use client";

import Link from "next/link";
import { useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import { api, ApiError, toApiError } from "@/lib/api";
import { useHydrated } from "@/lib/auth";
import { clearCart, lastOrderKey, setQuantity, useCart } from "@/lib/cart";
import { formatMoney } from "@/lib/money";
import type { Fulfilment, PlaceOrderRequest, PlaceOrderResponse, PublicStoreDto } from "@/lib/types";
import { StoreIcon, TruckIcon, WhatsAppIcon } from "../icons";
import { clampCartToStock, unsellableLines, useLastOrder, useQuote } from "./use-quote";

const inputCls =
  "block min-h-12 w-full rounded-control border border-line-strong bg-surface px-3 py-3 text-base text-ink placeholder:text-subtle focus:border-ink";

function Field({ label, children, hint }: { label: string; hint?: string; children: (id: string, describedBy?: string) => ReactNode }) {
  const id = useId();
  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-ink">
        {label}
      </label>
      {children(id, hint ? `${id}-hint` : undefined)}
      {hint && (
        <p id={`${id}-hint`} className="text-xs text-subtle">
          {hint}
        </p>
      )}
    </div>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-4 border-t border-line pt-5 first:border-t-0 first:pt-0">
      <h2 className="font-display text-lg font-bold text-ink">{title}</h2>
      {children}
    </section>
  );
}

export function CheckoutForm({ store }: { store: PublicStoreDto }) {
  const slug = store.slug;
  const lines = useCart(slug);
  const hydrated = useHydrated();
  const lastOrder = useLastOrder(slug);
  const zones = store.deliveryZones;

  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [fulfilment, setFulfilment] = useState<Fulfilment>("Delivery");
  const [zoneId, setZoneId] = useState(zones.length === 1 ? zones[0].id : "");
  const [address, setAddress] = useState("");
  const [note, setNote] = useState("");
  const [codeInput, setCodeInput] = useState("");
  const [appliedCode, setAppliedCode] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<ApiError | string | null>(null);

  const isDelivery = fulfilment === "Delivery";
  // Until a delivery area is picked, price the cart without delivery so the summary still shows.
  const zonePending = isDelivery && zones.length > 0 && !zoneId;
  const { quote, error: quoteError, loading: quoting } = useQuote(slug, {
    items: lines.map((l) => ({ productId: l.productId, variantId: l.variantId, quantity: l.quantity })),
    fulfilment: zonePending ? "Pickup" : fulfilment,
    deliveryZoneId: isDelivery && zoneId ? zoneId : null,
    discountCode: appliedCode,
  });
  const c = quote?.currency ?? store.currency;
  // Cart lines the server won't sell. They aren't in the summary or total, and placing the order with them fails,
  // so they are listed with a Remove button and the order can't be sent until they are gone.
  const unsellable = unsellableLines(lines, quote);

  useEffect(() => {
    if (quote) clampCartToStock(slug, quote);
  }, [slug, quote]);

  if (!hydrated)
    return (
      <div className="px-4 py-16 text-sm text-muted" role="status">
        Loading…
      </div>
    );

  if (lines.length === 0) {
    return (
      <div className="px-4 py-14">
        <p className="font-display text-xl font-bold text-ink">Your cart is empty</p>
        <p className="mt-1 text-sm text-muted">Add something from {store.name} to place an order.</p>
        <Link href={`/${slug}`} className="mt-5 inline-flex min-h-12 items-center rounded-control bg-accent px-6 font-semibold text-accent-ink">
          Browse products
        </Link>
        {lastOrder && (
          <p className="mt-6 text-sm text-muted">
            Already ordered?{" "}
            <Link href={`/o/${lastOrder}`} className="inline-flex min-h-11 items-center font-semibold text-ink underline underline-offset-2">
              Track order {lastOrder}
            </Link>
          </p>
        )}
      </div>
    );
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return setError("Please enter your name.");
    if (phone.replace(/\D/g, "").length < 7) return setError("Please enter a valid phone number.");
    if (isDelivery && zones.length > 0 && !zoneId) return setError("Please choose a delivery area.");
    if (isDelivery && !address.trim()) return setError("Please enter your delivery address.");
    if (unsellable.length > 0) return setError("Remove the items that are no longer available first.");

    const body: PlaceOrderRequest = {
      items: lines.map((l) => ({ productId: l.productId, variantId: l.variantId, quantity: l.quantity })),
      fulfilment,
      deliveryZoneId: isDelivery && zoneId ? zoneId : null,
      discountCode: appliedCode,
      customerName: name.trim(),
      phone: phone.trim(),
      address: isDelivery ? address.trim() : null,
      note: note.trim() || null,
    };
    setSubmitting(true);
    try {
      const res = await api<PlaceOrderResponse>(`/api/public/stores/${encodeURIComponent(slug)}/orders`, {
        method: "POST",
        body,
        auth: false,
      });
      try {
        window.localStorage.setItem(lastOrderKey(slug), res.code);
      } catch {
        /* ignore */
      }
      clearCart(slug);
      window.location.href = res.whatsappUrl;
    } catch (err) {
      setError(toApiError(err));
      setSubmitting(false);
    }
  }

  const discountFailed = appliedCode && quote && quote.discountMinor === 0;

  return (
    <form onSubmit={submit} className="space-y-6 px-4 pt-5" noValidate>
      {!store.isOpen && (
        <p className="rounded-control bg-marigold-soft p-3.5 text-sm font-medium text-marigold-deep">
          This store is closed right now, so orders can&apos;t be placed.
        </p>
      )}

      <Section title="Your details">
        <Field label="Full name">
          {(id) => <input id={id} className={inputCls} autoComplete="name" value={name} onChange={(e) => setName(e.target.value)} required />}
        </Field>
        <Field label="Phone number" hint="The seller will contact you on this number.">
          {(id, describedBy) => (
            <input
              id={id}
              className={inputCls}
              type="tel"
              inputMode="tel"
              autoComplete="tel"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              aria-describedby={describedBy}
              required
            />
          )}
        </Field>
      </Section>

      <Section title="Delivery">
        <div className="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Delivery or pickup">
          {(["Delivery", "Pickup"] as Fulfilment[]).map((f) => (
            <label
              key={f}
              className={`flex min-h-12 cursor-pointer items-center justify-center gap-2 rounded-control px-3 text-sm font-semibold transition-colors has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-marigold ${
                fulfilment === f ? "bg-accent text-accent-ink" : "bg-surface text-ink ring-1 ring-line-strong ring-inset hover:ring-ink"
              }`}
            >
              <input type="radio" name="fulfilment" value={f} checked={fulfilment === f} onChange={() => setFulfilment(f)} className="sr-only" />
              {f === "Delivery" ? <TruckIcon width={18} height={18} /> : <StoreIcon width={18} height={18} />}
              {f}
            </label>
          ))}
        </div>
        {isDelivery && (
          <>
            {zones.length > 0 && (
              <Field label="Delivery area">
                {(id) => (
                  <select id={id} className={inputCls} value={zoneId} onChange={(e) => setZoneId(e.target.value)} required>
                    <option value="">Choose your area…</option>
                    {zones.map((z) => (
                      <option key={z.id} value={z.id}>
                        {z.name} — {z.feeMinor === 0 ? "Free" : formatMoney(z.feeMinor, store.currency)}
                        {z.etaText ? ` (${z.etaText})` : ""}
                      </option>
                    ))}
                  </select>
                )}
              </Field>
            )}
            <Field label="Delivery address">
              {(id) => (
                <textarea
                  id={id}
                  className={inputCls}
                  rows={3}
                  autoComplete="street-address"
                  value={address}
                  onChange={(e) => setAddress(e.target.value)}
                  placeholder="House number, street, landmark"
                  required
                />
              )}
            </Field>
          </>
        )}
        <Field label="Note to seller (optional)">
          {(id) => <textarea id={id} className={inputCls} rows={2} value={note} onChange={(e) => setNote(e.target.value)} />}
        </Field>
      </Section>

      {store.hasDiscounts && (
        <section className="border-t border-line pt-5">
          <Field label="Discount code">
            {(id) => (
              <div className="flex gap-2">
                <input
                  id={id}
                  className={inputCls + " uppercase"}
                  value={codeInput}
                  onChange={(e) => setCodeInput(e.target.value.toUpperCase().replace(/\s/g, ""))}
                  placeholder="Enter code"
                />
                {appliedCode ? (
                  <button
                    type="button"
                    className="min-h-12 shrink-0 rounded-control px-4 text-sm font-semibold text-ink ring-1 ring-line-strong ring-inset hover:bg-paper"
                    onClick={() => {
                      setAppliedCode(null);
                      setCodeInput("");
                    }}
                  >
                    Remove
                  </button>
                ) : (
                  <button
                    type="button"
                    className="min-h-12 shrink-0 rounded-control bg-ink px-4 text-sm font-semibold text-white disabled:opacity-40"
                    disabled={!codeInput}
                    onClick={() => setAppliedCode(codeInput)}
                  >
                    Apply
                  </button>
                )}
              </div>
            )}
          </Field>
          {appliedCode && quote?.discountMessage && (
            <p className={`mt-1.5 text-sm font-medium ${discountFailed ? "text-hibiscus-deep" : "text-ink"}`} role="status">
              {quote.discountMessage}
            </p>
          )}
        </section>
      )}

      <section className="rounded-panel bg-paper p-4">
        <h2 className="mb-3 font-display text-lg font-bold text-ink">Order summary</h2>
        <ul className="space-y-1.5 text-sm">
          {(quote?.lines ?? []).filter((l) => l.quantity > 0).map((l, i) => (
            <li key={`${l.productId}:${l.variantId ?? ""}:${i}`} className="grid grid-cols-[2rem_minmax(0,1fr)_auto] gap-x-2">
              <span className="font-semibold text-ink">{l.quantity}×</span>
              <span className="min-w-0 text-ink-2">
                {l.name}
                {l.variant ? <span className="text-muted"> ({l.variant})</span> : null}
              </span>
              <span className="text-ink">{formatMoney(l.lineTotalMinor, c)}</span>
            </li>
          ))}
        </ul>
        {unsellable.length > 0 && (
          <div className="mt-3 rounded-control bg-hibiscus-soft p-3 text-sm" role="alert">
            <p className="font-medium text-hibiscus-deep">
              {unsellable.length === 1 ? "This item is" : "These items are"} no longer available. Remove{" "}
              {unsellable.length === 1 ? "it" : "them"} to send your order.
            </p>
            <ul className="mt-2 space-y-1">
              {unsellable.map((l) => (
                <li key={`${l.productId}:${l.variantId ?? ""}`} className="flex items-center justify-between gap-3">
                  <span className="min-w-0 text-ink-2">
                    {l.name}
                    {l.variantTitle ? <span className="text-muted"> ({l.variantTitle})</span> : null}
                  </span>
                  <button
                    type="button"
                    onClick={() => setQuantity(slug, l.productId, l.variantId, 0)}
                    className="min-h-11 shrink-0 px-2 font-semibold text-hibiscus-deep hover:underline"
                  >
                    Remove
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
        {quote ? (
          <dl className={`mt-3 space-y-1.5 border-t border-line-strong pt-3 text-sm ${quoting ? "opacity-50" : ""}`}>
            <div className="flex justify-between gap-3">
              <dt className="text-ink-2">Subtotal</dt>
              <dd className="text-ink">{formatMoney(quote.subtotalMinor, c)}</dd>
            </div>
            {isDelivery && (
              <div className="flex justify-between gap-3">
                <dt className="text-ink-2">Delivery</dt>
                <dd className="text-right text-ink">
                  {zones.length > 0 && !zoneId
                    ? "Choose area"
                    : zones.length === 0
                      ? "Agreed on WhatsApp"
                      : quote.deliveryFeeMinor === 0
                        ? "Free"
                        : formatMoney(quote.deliveryFeeMinor, c)}
                </dd>
              </div>
            )}
            {quote.discountMinor > 0 && (
              <div className="flex justify-between gap-3 font-medium text-ink">
                <dt>Discount</dt>
                <dd>−{formatMoney(quote.discountMinor, c)}</dd>
              </div>
            )}
            <div className="flex items-baseline justify-between gap-3 border-t border-line-strong pt-2.5">
              <dt className="font-semibold text-ink">Total{zonePending ? " (before delivery)" : ""}</dt>
              <dd className="font-display text-2xl font-bold text-ink">{formatMoney(quote.totalMinor, c)}</dd>
            </div>
          </dl>
        ) : (
          !quoteError && <p className="text-sm text-muted">Calculating…</p>
        )}
        {quoteError && (
          <p className="mt-2 text-sm font-medium text-hibiscus-deep" role="alert">
            {quoteError.message}
          </p>
        )}
      </section>

      {error && (
        <p className="rounded-control bg-hibiscus-soft p-3.5 text-sm font-medium text-hibiscus-deep" role="alert">
          {typeof error === "string" ? error : error.message}
          {typeof error !== "string" && error.fieldMessages.length > 1 && (
            <span className="mt-1 block font-normal">{error.fieldMessages.filter((m) => m !== error.message).join(" ")}</span>
          )}
        </p>
      )}

      <div className="fixed inset-x-0 bottom-0 z-20">
        <div className="mx-auto max-w-[560px] border-t border-line bg-surface px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] shadow-float sm:border-x">
          <button
            type="submit"
            disabled={!store.isOpen || submitting || unsellable.length > 0}
            className="flex min-h-13 w-full items-center justify-center gap-2.5 rounded-control bg-leaf px-5 font-semibold text-white transition-colors hover:bg-leaf-hover disabled:opacity-50"
          >
            <WhatsAppIcon />
            {!store.isOpen ? (
              "Store is closed"
            ) : submitting ? (
              "Placing order…"
            ) : unsellable.length > 0 ? (
              "Remove unavailable items first"
            ) : (
              <>
                <span>Send order on WhatsApp</span>
                {quote && <span className="ml-auto font-display text-lg font-bold">{formatMoney(quote.totalMinor, c)}</span>}
              </>
            )}
          </button>
        </div>
      </div>
    </form>
  );
}
