import type { ReactNode } from "react";
import { OrderTicket } from "@/components/order-ticket";
import { CheckIcon, ChevronDownIcon, CopyIcon, SearchIcon, StoreIcon, TruckIcon, WhatsAppIcon } from "@/components/icons";
import { SITE_URL } from "@/lib/config";

/**
 * OrderLink's real screens, redrawn in HTML for the landing page so it needs no image files. The frame around them speaks
 * LinkSuite; inside, the app's own palette and faces stay, because this is what the product looks like. Each one mirrors:
 * - StoreScreen: the storefront, src/app/[slug]/page.tsx (cover, logo, Open badge, search, category chips, product grid).
 * - CheckoutScreen: src/components/store/checkout-form.tsx (delivery area, order summary, the WhatsApp send bar).
 * - WhatsAppScreen: the message the API pre-fills, WhatsAppMessage.Build in api/.../Orders/OrderPricing.cs, as the seller sees it.
 * - The feature crops: OrderTicket itself, the checkout's delivery choice, product-purchase.tsx options, dashboard/share.
 * Names and naira amounts are a business's own sample figures, never a LinkSuite price. All drawings are decorative: the
 * PhoneFrame or the feature text around them carries the meaning.
 */

const tiny = "text-[0.6875rem] leading-snug";
/** Mama Tee's store colour (a store's themeColor), with white text on it at 7.7:1. */
const MAMA = "#8a3b12";
/** Ada's Closet, a boutique, in its own colour. */
const ADA = "#6d2e7a";

type Dish = "jollof" | "fried" | "chapman" | "chops";

/** A product photo, drawn: a plate of jollof with dodo, fried rice, a glass of Chapman, a plate of small chops. */
function DishArt({ dish, className }: { dish: Dish; className?: string }) {
  const plate = (
    <>
      <circle cx="24" cy="25" r="17" fill="#ffffff" />
      <circle cx="24" cy="25" r="16.5" fill="none" stroke="#e4d2bc" />
      <circle cx="24" cy="25" r="12.5" fill="#fbf6ef" />
    </>
  );
  return (
    <svg viewBox="0 0 48 48" aria-hidden="true" className={className} preserveAspectRatio="xMidYMid slice">
      {dish === "jollof" && (
        <>
          <rect width="48" height="48" fill="#f3e1cf" />
          {plate}
          <ellipse cx="21.5" cy="25.5" rx="9.5" ry="8.2" fill="#cf5527" />
          <ellipse cx="18.5" cy="23" rx="3" ry="1.6" fill="#e2733f" />
          <ellipse cx="23" cy="28.5" rx="3.2" ry="1.5" fill="#e2733f" />
          <ellipse cx="31.5" cy="21.5" rx="3.6" ry="2.3" fill="#e9a43a" transform="rotate(-25 31.5 21.5)" />
          <ellipse cx="32" cy="28" rx="3.6" ry="2.3" fill="#e39a2f" transform="rotate(20 32 28)" />
          <circle cx="17" cy="27" r="0.9" fill="#4f8a3a" />
          <circle cx="24.5" cy="22.5" r="0.9" fill="#4f8a3a" />
        </>
      )}
      {dish === "fried" && (
        <>
          <rect width="48" height="48" fill="#eef0dc" />
          {plate}
          <ellipse cx="24" cy="25.5" rx="10.5" ry="8.6" fill="#e6c565" />
          {[
            [19, 23, "#5f9a3c"],
            [25, 21.5, "#e27a35"],
            [28.5, 26, "#5f9a3c"],
            [21.5, 28.5, "#e27a35"],
            [26, 29, "#9c5a2e"],
            [17.5, 26.5, "#9c5a2e"],
          ].map(([x, y, c]) => (
            <circle key={`${x}-${y}`} cx={x as number} cy={y as number} r="1.1" fill={c as string} />
          ))}
        </>
      )}
      {dish === "chapman" && (
        <>
          <rect width="48" height="48" fill="#f6dccd" />
          <path d="M15.5 11h17l-2 27a2 2 0 0 1-2 1.8h-9a2 2 0 0 1-2-1.8z" fill="#c63d2f" />
          <path d="M15.5 11h17l-.3 4H15.8z" fill="#de624c" />
          <rect x="19" y="18" width="4" height="4" rx="0.8" fill="#ffffff" opacity="0.35" />
          <rect x="24.5" y="23" width="4" height="4" rx="0.8" fill="#ffffff" opacity="0.35" />
          <path d="M22 4.5 26.5 16" stroke="#1b1f3b" strokeWidth="1.4" strokeLinecap="round" />
          <circle cx="31" cy="11.5" r="4.2" fill="#a8cf6a" stroke="#5f8f35" />
          <circle cx="31" cy="11.5" r="2.2" fill="#d6ebb2" />
        </>
      )}
      {dish === "chops" && (
        <>
          <rect width="48" height="48" fill="#f1e4cf" />
          {plate}
          <circle cx="18.5" cy="22" r="4.2" fill="#c9873b" />
          <circle cx="16.5" cy="29" r="4" fill="#bf7d33" />
          <circle cx="23.5" cy="28.5" r="3.8" fill="#c9873b" />
          <rect x="25" y="17" width="11" height="4.5" rx="2.2" fill="#d9a04a" transform="rotate(18 30.5 19)" />
          <path d="M27 26.5 35 25l-3.5 8z" fill="#cf9440" />
        </>
      )}
    </svg>
  );
}

/** The customer's phone: Mama Tee's storefront, the link from her Instagram bio. */
export function StoreScreen() {
  const products: { name: string; price: string; dish: Dish }[] = [
    { name: "Jollof rice", price: "₦3,000", dish: "jollof" },
    { name: "Fried rice", price: "₦3,000", dish: "fried" },
    { name: "Chapman", price: "₦1,500", dish: "chapman" },
    { name: "Small chops, 20 pieces", price: "₦4,500", dish: "chops" },
  ];
  return (
    <div className="min-h-[28rem] bg-surface pb-6 font-sans text-ink">
      <div className="h-[4.5rem]" style={{ background: MAMA }} />
      <div className="px-3.5">
        <div
          className="-mt-7 flex h-14 w-14 items-center justify-center rounded-[12px] border-[3px] border-surface font-display text-2xl font-bold text-white"
          style={{ background: MAMA }}
        >
          M
        </div>
        <div className="mt-1.5 flex items-center gap-2">
          <span className="font-display text-[1.0625rem] leading-tight font-bold">Mama Tee&apos;s Kitchen</span>
          <span className="inline-flex items-center gap-1 rounded-full bg-ink-soft px-1.5 text-[0.625rem] leading-4 font-semibold">
            <span className="h-1 w-1 rounded-full bg-ink" />
            Open
          </span>
        </div>
        <p className={`${tiny} text-muted`}>Jollof, fried rice and small chops. Lekki and VI.</p>
        <div className={`mt-2.5 flex h-8 items-center gap-1.5 rounded-[8px] border border-field bg-paper px-2.5 ${tiny} text-subtle`}>
          <SearchIcon width={12} height={12} />
          Search Mama Tee&apos;s Kitchen
        </div>
        <div className="mt-2 flex gap-1.5 overflow-hidden">
          {["All", "Rice", "Drinks", "Small chops"].map((c, i) => (
            <span
              key={c}
              className={`shrink-0 rounded-full px-2.5 py-1 text-[0.625rem] font-medium ${i === 0 ? "text-white" : "text-ink-2 ring-1 ring-line ring-inset"}`}
              style={i === 0 ? { background: MAMA } : undefined}
            >
              {c}
            </span>
          ))}
        </div>
        <ul className="mt-3 grid grid-cols-2 gap-x-2.5 gap-y-3">
          {products.map((p) => (
            <li key={p.name}>
              <DishArt dish={p.dish} className="block aspect-square w-full rounded-[8px]" />
              <p className={`mt-1 truncate ${tiny} font-medium`}>{p.name}</p>
              <p className="font-display text-[0.8125rem] leading-tight font-bold">{p.price}</p>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

/** The customer's checkout: delivery to Lekki, the order summary, and the button that opens WhatsApp. */
export function CheckoutScreen() {
  return (
    <div className="min-h-[28rem] bg-surface font-sans text-ink">
      <div className="flex items-center gap-1 border-b border-line px-2 pt-7 pb-2">
        <svg viewBox="0 0 24 24" width={16} height={16} fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true">
          <path d="m15 18-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        <span className="font-display text-[0.9375rem] font-bold">Checkout</span>
      </div>
      <div className="space-y-3 px-3.5 pt-3">
        <div className="grid grid-cols-2 gap-1.5">
          <span className="flex h-8 items-center justify-center gap-1 rounded-[8px] text-[0.6875rem] font-semibold text-white" style={{ background: MAMA }}>
            <TruckIcon width={12} height={12} />
            Delivery
          </span>
          <span className="flex h-8 items-center justify-center gap-1 rounded-[8px] text-[0.6875rem] font-semibold ring-1 ring-line-strong ring-inset">
            <StoreIcon width={12} height={12} />
            Pickup
          </span>
        </div>
        <div>
          <p className="text-[0.6875rem] font-medium">Delivery area</p>
          <span className="mt-1 flex h-8 items-center justify-between rounded-[8px] border border-field px-2.5 text-[0.6875rem]">
            Lekki — ₦1,500 (Same day)
            <ChevronDownIcon width={12} height={12} className="text-muted" />
          </span>
        </div>
        <div className="rounded-[10px] bg-paper p-3">
          <p className="font-display text-[0.8125rem] font-bold">Order summary</p>
          <ul className={`mt-1.5 space-y-1 ${tiny}`}>
            <li className="grid grid-cols-[1.25rem_minmax(0,1fr)_auto] gap-x-1">
              <span className="font-semibold">2×</span>
              <span className="text-ink-2">
                Jollof rice <span className="text-muted">(Large)</span>
              </span>
              <span className="tnum">₦6,000</span>
            </li>
            <li className="grid grid-cols-[1.25rem_minmax(0,1fr)_auto] gap-x-1">
              <span className="font-semibold">1×</span>
              <span className="text-ink-2">Chapman</span>
              <span className="tnum">₦1,500</span>
            </li>
          </ul>
          <dl className={`mt-2 space-y-0.5 border-t border-line-strong pt-1.5 ${tiny}`}>
            <div className="flex justify-between">
              <dt className="text-ink-2">Subtotal</dt>
              <dd className="tnum">₦7,500</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-ink-2">Delivery</dt>
              <dd className="tnum">₦1,500</dd>
            </div>
            <div className="flex items-baseline justify-between border-t border-line-strong pt-1">
              <dt className="font-semibold">Total</dt>
              <dd className="tnum font-display text-[1.0625rem] font-bold">₦9,000</dd>
            </div>
          </dl>
        </div>
      </div>
      <div className="mt-3 border-t border-line px-3 pt-2.5">
        <span className="flex h-10 items-center gap-2 rounded-[8px] bg-leaf px-3 text-[0.75rem] font-semibold text-white">
          <WhatsAppIcon width={14} height={14} />
          Send order on WhatsApp
          <span className="tnum ml-auto font-display text-[0.875rem] font-bold">₦9,000</span>
        </span>
      </div>
    </div>
  );
}

/** The seller's phone: the order arrives in WhatsApp, written out exactly as OrderLink writes it, and the chat goes on. */
export function WhatsAppScreen() {
  const message = [
    "New order #1042",
    "2 x Jollof rice (Large) - ₦6,000",
    "1 x Chapman - ₦1,500",
    "Delivery (Lekki) - ₦1,500",
    "Total: ₦9,000",
    "Name: Ada Okafor, 0803 000 0000",
    "Address: 12 Admiralty Way, Lekki",
  ];
  const ticks = (
    <svg viewBox="0 0 18 12" width={14} height={10} fill="none" stroke="#53bdeb" strokeWidth={1.6} aria-hidden="true">
      <path d="m1 6.5 3 3L10.5 2.5M7 9.5l.5.5 7-7.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
  return (
    <div className="flex min-h-[28rem] flex-col bg-[#efeae2] font-sans text-[#111b21]">
      <div className="flex items-center gap-2 bg-[#008069] px-2.5 pt-7 pb-2 text-white">
        <svg viewBox="0 0 24 24" width={16} height={16} fill="none" stroke="currentColor" strokeWidth={2} aria-hidden="true">
          <path d="M19 12H5m6-6-6 6 6 6" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
        <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[#dfe5e7] text-[0.625rem] font-bold text-[#54656f]">AO</span>
        <span className="min-w-0">
          <span className="block text-[0.8125rem] leading-tight font-semibold">Ada Okafor</span>
          <span className="block text-[0.625rem] text-white/80">online</span>
        </span>
      </div>
      <div className="flex-1 space-y-2 px-2.5 pt-3">
        <p className="mx-auto w-fit rounded-[6px] bg-white/90 px-2 py-0.5 text-[0.625rem] text-[#54656f]">Today</p>
        <div className="mr-5 rounded-[8px] rounded-tl-[2px] bg-white px-2.5 pt-1.5 pb-1 shadow-[0_1px_0.5px_rgb(11_20_26/0.13)]">
          {message.map((line) => (
            <span key={line} className="block text-[0.6875rem] leading-[1.4]">
              {line}
            </span>
          ))}
          <span className="block text-right text-[0.5625rem] text-[#667781]">14:05</span>
        </div>
        <div className="ml-7 rounded-[8px] rounded-tr-[2px] bg-[#d9fdd3] px-2.5 pt-1.5 pb-1 shadow-[0_1px_0.5px_rgb(11_20_26/0.13)]">
          <span className="block text-[0.6875rem] leading-[1.4]">Thank you Ada. It&apos;s on the way, about 40 minutes.</span>
          <span className="flex items-center justify-end gap-1 text-[0.5625rem] text-[#667781]">14:07 {ticks}</span>
        </div>
      </div>
    </div>
  );
}

/** A crop that sits at the top of a feature stage, so the floor cuts it off like a phone rising. */
function Crop({ children, className = "w-[16.5rem]" }: { children: ReactNode; className?: string }) {
  return (
    <div aria-hidden="true" className={`self-start ${className}`}>
      {children}
    </div>
  );
}

/** Feature: the order as a ticket in the seller's dashboard (the real OrderTicket component). */
export function TicketCrop() {
  return (
    <Crop>
      <OrderTicket
        compact
        number={1042}
        kicker="Ada Okafor"
        status={
          <span className="inline-flex items-center gap-1.5 rounded-full bg-marigold-soft px-2.5 py-0.5 text-[0.75rem] font-medium text-marigold-deep">
            <span className="h-1.5 w-1.5 rounded-full bg-marigold" />
            New
          </span>
        }
        lines={[
          { key: "a", quantity: 2, name: "Jollof rice", variant: "Large", amount: "₦6,000" },
          { key: "b", quantity: 1, name: "Chapman", amount: "₦1,500" },
        ]}
        rows={[{ label: "Delivery (Lekki)", value: "₦1,500" }]}
        total="₦9,000"
      />
    </Crop>
  );
}

/** Feature: delivery or pickup, and the seller's own areas and fees, as the customer chooses them at checkout. */
export function DeliveryCrop() {
  const zones = [
    ["Lekki", "₦1,500", "Same day"],
    ["Victoria Island", "₦2,000", "Same day"],
    ["Ajah", "₦2,500", "Next day"],
    ["Ikeja", "₦3,500", "Next day"],
  ];
  return (
    <Crop>
      <div className="rounded-t-[14px] border border-b-0 border-line bg-surface px-4 pt-4 pb-6 font-sans text-ink">
        <p className="font-display text-[0.9375rem] font-bold">Delivery</p>
        <div className="mt-2 grid grid-cols-2 gap-1.5">
          <span className="flex h-9 items-center justify-center gap-1.5 rounded-[8px] text-[0.75rem] font-semibold text-white" style={{ background: MAMA }}>
            <TruckIcon width={14} height={14} />
            Delivery
          </span>
          <span className="flex h-9 items-center justify-center gap-1.5 rounded-[8px] text-[0.75rem] font-semibold ring-1 ring-line-strong ring-inset">
            <StoreIcon width={14} height={14} />
            Pickup
          </span>
        </div>
        <p className="mt-3 text-[0.75rem] font-medium">Delivery area</p>
        <ul className="mt-1 overflow-hidden rounded-[8px] border border-field text-[0.75rem]">
          {zones.map(([name, fee, eta], i) => (
            <li key={name} className={`flex items-center gap-2 px-2.5 py-1.5 ${i === 0 ? "bg-ink-soft font-semibold" : "border-t border-line"}`}>
              <span className="min-w-0 flex-1 truncate">
                {name} — {fee} <span className="font-normal text-muted">({eta})</span>
              </span>
              {i === 0 && <CheckIcon width={13} height={13} />}
            </li>
          ))}
        </ul>
      </div>
    </Crop>
  );
}

/** Feature: a boutique's dress with sizes and colours; an option that is sold out is crossed out. */
export function OptionsCrop() {
  const sizes = [
    { v: "S", ok: true },
    { v: "M", ok: true, on: true },
    { v: "L", ok: true },
    { v: "XL", ok: false },
  ];
  return (
    <Crop>
      <div className="rounded-t-[14px] border border-b-0 border-line bg-surface font-sans text-ink">
        <div className="flex items-center gap-3 px-4 pt-4">
          <svg viewBox="0 0 48 48" aria-hidden="true" className="h-16 w-16 shrink-0 rounded-[8px]">
            <rect width="48" height="48" fill="#f4e9df" />
            <path d="M19 7h10l2 6 7 28H10l7-28z" fill="#24489c" />
            {[
              [20, 16],
              [27, 19],
              [22, 26],
              [30, 30],
              [16, 34],
              [24, 37],
              [33, 38],
            ].map(([x, y]) => (
              <circle key={`${x}-${y}`} cx={x} cy={y} r="2.2" fill="#f08a24" />
            ))}
            <path d="M19 7h10l2 6 7 28H10l7-28z" fill="none" stroke="#1b2f6b" strokeWidth="0.8" />
          </svg>
          <div className="min-w-0">
            <p className="text-[0.6875rem] text-muted">Ada&apos;s Closet</p>
            <p className="font-display text-[0.9375rem] leading-tight font-bold">Ankara wrap dress</p>
            <p className="font-display text-[1.0625rem] font-bold">₦18,500</p>
          </div>
        </div>
        <div className="px-4 pt-3 pb-6">
          <p className="text-[0.75rem] font-semibold">
            Size<span className="font-normal text-muted">: M</span>
          </p>
          <div className="mt-1.5 flex gap-1.5">
            {sizes.map((s) => (
              <span
                key={s.v}
                className={`flex h-8 min-w-8 items-center justify-center rounded-[8px] px-2 text-[0.75rem] font-medium ${
                  s.on ? "text-white" : "ring-1 ring-line-strong ring-inset"
                } ${s.ok ? "" : "text-subtle line-through opacity-60"}`}
                style={s.on ? { background: ADA } : undefined}
              >
                {s.v}
              </span>
            ))}
          </div>
          <p className="mt-2.5 text-[0.75rem] font-semibold">Colour</p>
          <div className="mt-1.5 flex gap-1.5">
            {["Blue and orange", "Green"].map((c, i) => (
              <span
                key={c}
                className={`flex h-8 items-center rounded-[8px] px-2.5 text-[0.75rem] font-medium ${i === 0 ? "text-white" : "ring-1 ring-line-strong ring-inset"}`}
                style={i === 0 ? { background: ADA } : undefined}
              >
                {c}
              </span>
            ))}
          </div>
          <span className="mt-3 flex h-9 items-center justify-between rounded-[8px] px-3 text-[0.75rem] font-semibold text-white" style={{ background: ADA }}>
            Add to cart
            <span className="font-display text-[0.875rem] font-bold">₦18,500</span>
          </span>
        </div>
      </div>
    </Crop>
  );
}

/** A QR-like grid of modules with the three finder squares. Decorative: the real QR code is generated in the dashboard. */
function QrArt({ className }: { className?: string }) {
  const n = 21;
  const finder = (x: number, y: number) =>
    [0, 14].includes(x - (x % 7)) && [0, 14].includes(y - (y % 7)) && !(x >= 14 && y >= 14);
  const cells: [number, number][] = [];
  for (let y = 0; y < n; y++) {
    for (let x = 0; x < n; x++) {
      if (finder(x, y)) continue;
      if ((x * 7 + y * 13 + x * y * 3) % 5 < 2) cells.push([x, y]);
    }
  }
  return (
    <svg viewBox="-1 -1 23 23" aria-hidden="true" className={className} shapeRendering="crispEdges">
      <rect x="-1" y="-1" width="23" height="23" fill="#ffffff" />
      {[
        [0, 0],
        [14, 0],
        [0, 14],
      ].map(([x, y]) => (
        <g key={`${x}-${y}`}>
          <rect x={x} y={y} width="7" height="7" fill="#1b1f3b" />
          <rect x={x + 1} y={y + 1} width="5" height="5" fill="#ffffff" />
          <rect x={x + 2} y={y + 2} width="3" height="3" fill="#1b1f3b" />
        </g>
      ))}
      {cells.map(([x, y]) => (
        <rect key={`${x}-${y}`} x={x} y={y} width="1" height="1" fill="#1b1f3b" />
      ))}
    </svg>
  );
}

/** Feature: the seller's share screen, with the store link, a WhatsApp share button and the QR code. */
export function ShareCrop() {
  const host = SITE_URL.replace(/^https?:\/\//, "");
  return (
    <Crop>
      <div className="rounded-t-[14px] border border-b-0 border-line bg-surface px-4 pt-4 pb-6 font-sans text-ink">
        <p className="font-display text-[0.9375rem] font-bold">Your store link</p>
        <div className="mt-2 flex items-center gap-1.5">
          <span className="flex h-9 min-w-0 flex-1 items-center rounded-[8px] bg-paper px-2.5 text-[0.75rem] font-medium ring-1 ring-line ring-inset">
            <span className="truncate">{host}/mama-tees-kitchen</span>
          </span>
          <span className="flex h-9 items-center gap-1 rounded-[8px] border border-line-strong px-2.5 text-[0.75rem] font-semibold">
            <CopyIcon width={13} height={13} />
            Copy
          </span>
        </div>
        <span className="mt-2 flex h-9 items-center justify-center gap-1.5 rounded-[8px] bg-leaf text-[0.75rem] font-semibold text-white">
          <WhatsAppIcon width={14} height={14} />
          Share on WhatsApp
        </span>
        <div className="mt-2.5 flex items-center gap-3">
          <QrArt className="h-16 w-16 shrink-0 rounded-[6px] border border-line" />
          <p className="text-[0.6875rem] leading-snug text-muted">Print it on flyers, packaging or your shop counter. Customers scan it to open your store.</p>
        </div>
      </div>
    </Crop>
  );
}
