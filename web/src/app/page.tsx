import Link from "next/link";
import { APP_NAME } from "@/lib/config";
import { OrderTicket } from "@/components/order-ticket";
import { CheckIcon, WhatsAppIcon } from "@/components/icons";

const steps = [
  { title: "Create your store", text: "Sign up with your WhatsApp number. Your store link is ready straight away." },
  { title: "Add products", text: "Snap photos, set prices, sizes and colours. Add the areas you deliver to and what each costs." },
  { title: "Share your link", text: "Put it in your Instagram bio and WhatsApp status. Orders arrive in WhatsApp, neatly written out." },
];

const features = [
  { title: "Orders straight to WhatsApp", text: "Customers check out and a pre-filled message opens in WhatsApp. No more back-and-forth about prices." },
  { title: "Every order is saved", text: "Orders are recorded before WhatsApp opens, so nothing gets lost in your chats." },
  { title: "Sizes, colours and stock", text: "Variants and stock tracking, with clear out-of-stock labels in your store." },
  { title: "Delivery zones", text: "Set a fee per area, or let customers pick up. Totals are worked out for you." },
  { title: "Discount codes", text: "Run promos with percent or fixed-amount codes, a minimum order and an expiry date." },
  { title: "A store that loads fast", text: "Your storefront opens quickly on any phone, even on slow mobile data." },
];

const plans = [
  {
    name: "Free",
    price: "₦0",
    period: "",
    items: ["Up to 20 products", "1 delivery zone", "Orders to WhatsApp", `${APP_NAME} branding`],
    cta: "Start free",
    primary: false,
  },
  {
    name: "Pro",
    price: "₦3,000",
    period: "/month",
    items: ["Unlimited products", "Unlimited delivery zones", "Discount codes", "Remove branding"],
    cta: "Get started with Pro",
    primary: true,
  },
];

const cart = [
  { qty: 2, name: "Jollof rice", variant: "Large", amount: "₦6,000" },
  { qty: 1, name: "Chapman", variant: null, amount: "₦1,500" },
];

const message = [
  "New order #1042",
  "2 x Jollof rice (large) - ₦6,000",
  "1 x Chapman - ₦1,500",
  "Delivery (Lekki) - ₦1,500",
  "Total: ₦9,000",
  "Name: Ada, 0803 000 0000",
  "Address: 12 Admiralty Way",
];

function Wordmark() {
  return (
    <Link href="/" className="inline-flex min-h-11 items-center gap-2 font-display text-xl font-extrabold text-ink">
      <span className="h-3 w-3 rounded-full bg-marigold" aria-hidden="true" />
      {APP_NAME}
    </Link>
  );
}

/** The product's characteristic moment: a storefront cart and the WhatsApp message it produces. */
function HeroVisual() {
  return (
    <div className="relative mx-auto w-full max-w-[34rem] lg:mx-0" aria-label="Example: a customer's cart and the WhatsApp message it sends" role="img">
      {/* Phone with storefront cart */}
      <div className="w-[15.5rem] rounded-[2.25rem] bg-ink p-2.5 shadow-lift sm:w-[16.5rem]">
        <div className="overflow-hidden rounded-[1.75rem] bg-surface">
          <div className="flex items-center gap-2 border-b border-line px-4 pt-5 pb-3">
            <span className="flex h-8 w-8 items-center justify-center rounded-control bg-[#8a3b12] font-display text-sm font-bold text-white">
              M
            </span>
            <span className="min-w-0">
              <span className="block truncate font-display text-sm font-bold text-ink">Mama Tee&apos;s Kitchen</span>
              <span className="block text-[0.6875rem] text-muted">Your cart</span>
            </span>
          </div>
          <ul className="divide-y divide-line px-4">
            {cart.map((l) => (
              <li key={l.name} className="flex items-center gap-2.5 py-3">
                <span className="h-10 w-10 shrink-0 rounded-control bg-[#f3e1cf]" />
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-xs font-medium text-ink">{l.name}</span>
                  <span className="block text-[0.6875rem] text-muted">
                    {l.variant ? `${l.variant}, ` : ""}qty {l.qty}
                  </span>
                </span>
                <span className="font-display text-sm font-bold text-ink">{l.amount}</span>
              </li>
            ))}
          </ul>
          <dl className="space-y-1 border-t border-line px-4 py-3 text-[0.6875rem]">
            <div className="flex justify-between text-ink-2">
              <dt>Delivery (Lekki)</dt>
              <dd>₦1,500</dd>
            </div>
            <div className="flex items-baseline justify-between pt-1">
              <dt className="text-xs font-semibold text-ink">Total</dt>
              <dd className="font-display text-lg font-bold text-ink">₦9,000</dd>
            </div>
          </dl>
          <div className="px-3 pb-4">
            <span className="flex min-h-10 items-center justify-center gap-1.5 rounded-control bg-leaf text-xs font-semibold text-white">
              <WhatsAppIcon width={14} height={14} />
              Send order on WhatsApp
            </span>
          </div>
        </div>
      </div>

      {/* The WhatsApp message it produces */}
      <div className="relative -mt-4 ml-auto w-[17.5rem] animate-reveal sm:-mt-40 sm:w-[19rem] lg:absolute lg:top-20 lg:right-0 lg:mt-0">
        <div className="rounded-panel bg-[#efeae2] p-3 shadow-lift">
          <p className="mb-2 flex items-center gap-2 px-1 text-xs font-semibold text-ink-2">
            <WhatsAppIcon width={14} height={14} className="text-leaf" />
            WhatsApp, to Mama Tee&apos;s Kitchen
          </p>
          <div className="relative ml-4 rounded-[0.75rem] rounded-tr-sm bg-[#d9fdd3] px-3 pt-2 pb-1.5 text-[0.8125rem] leading-snug text-[#111b21]">
            {message.map((line, i) => (
              <span key={line} className={i === 0 || line.startsWith("Total") ? "block font-semibold" : "block"}>
                {line}
              </span>
            ))}
            <span className="mt-1 flex items-center justify-end gap-1 text-[0.6875rem] text-[#667781]">
              14:05
              <svg viewBox="0 0 18 12" width={16} height={11} fill="none" stroke="#53bdeb" strokeWidth={1.6} aria-hidden="true">
                <path d="m1 6.5 3 3L10.5 2.5M7 9.5l.5.5 7-7.5" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

export default function Home() {
  return (
    <div className="flex min-h-screen flex-col bg-paper">
      <header className="sticky top-0 z-20 border-b border-line/80 bg-paper/90 backdrop-blur">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-2 sm:px-6">
          <Wordmark />
          <nav className="flex items-center gap-1 text-sm font-semibold" aria-label="Main">
            <a href="#pricing" className="hidden min-h-11 items-center rounded-control px-3 text-ink-2 hover:text-ink sm:inline-flex">
              Pricing
            </a>
            <Link href="/login" className="inline-flex min-h-11 items-center rounded-control px-3 text-ink-2 hover:text-ink">
              Log in
            </Link>
            <Link href="/signup" className="inline-flex min-h-11 items-center rounded-control bg-marigold px-4 text-ink hover:bg-marigold-hover">
              Start free
            </Link>
          </nav>
        </div>
      </header>

      <main className="flex-1">
        {/* Hero */}
        <section className="mx-auto grid max-w-6xl items-center gap-12 px-4 pt-12 pb-16 sm:px-6 sm:pt-16 lg:grid-cols-[minmax(0,1fr)_minmax(0,34rem)] lg:gap-10 lg:pt-20 lg:pb-24">
          <div className="max-w-xl">
            <p className="text-sm font-medium text-ink-2">For sellers who take orders on Instagram and WhatsApp</p>
            <h1 className="mt-4 font-display text-3xl leading-[1.05] font-extrabold text-ink sm:text-4xl">
              Customers pick from your link. The order lands in your WhatsApp.
            </h1>
            <p className="mt-5 max-w-[34rem] text-lg text-ink-2">
              One store link for your bio. Customers choose items and delivery, then send you a complete order with the total already
              added up. No more back-and-forth about prices.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
              <Link
                href="/signup"
                className="inline-flex min-h-12 whitespace-nowrap items-center justify-center rounded-control bg-marigold px-6 text-base font-semibold text-ink hover:bg-marigold-hover"
              >
                Create my free store
              </Link>
              <a
                href="#how"
                className="inline-flex min-h-12 whitespace-nowrap items-center justify-center rounded-control px-6 text-base font-semibold text-ink ring-1 ring-line-strong ring-inset hover:bg-surface"
              >
                See how it works
              </a>
            </div>
            <p className="mt-4 text-sm text-muted">Free plan, no card needed. Set up takes about 10 minutes.</p>
          </div>
          <HeroVisual />
        </section>

        {/* How it works */}
        <section id="how" className="scroll-mt-16 border-t border-line bg-surface">
          <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6 lg:py-20">
            <h2 className="font-display text-2xl font-bold text-ink sm:text-3xl">How it works</h2>
            <ol className="mt-10 grid gap-10 sm:grid-cols-3 sm:gap-8">
              {steps.map((s, i) => (
                <li key={s.title} className="border-t-2 border-ink pt-4">
                  <span className="font-display text-4xl font-extrabold text-marigold-deep num" aria-hidden="true">
                    {i + 1}
                  </span>
                  <h3 className="mt-2 font-display text-lg font-bold text-ink">
                    <span className="sr-only">Step {i + 1}: </span>
                    {s.title}
                  </h3>
                  <p className="mt-1 max-w-[32ch] text-ink-2">{s.text}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        {/* Features */}
        <section className="border-t border-line">
          <div className="mx-auto grid max-w-6xl gap-12 px-4 py-16 sm:px-6 lg:grid-cols-[22rem_minmax(0,1fr)] lg:gap-16 lg:py-20">
            <div>
              <h2 className="font-display text-2xl font-bold text-ink sm:text-3xl">Everything you need to sell</h2>
              <p className="mt-3 max-w-prose text-ink-2">
                Each order is kept as a ticket in your dashboard, so you can confirm it, mark it paid and track delivery.
              </p>
              <div className="mt-8 max-w-[20rem]">
                <OrderTicket
                  number={1042}
                  kicker="Ada"
                  status={
                    <span className="inline-flex items-center gap-1.5 rounded-full bg-marigold-soft px-2.5 py-0.5 text-xs font-medium text-marigold-deep">
                      <span className="h-1.5 w-1.5 rounded-full bg-marigold" aria-hidden="true" />
                      New
                    </span>
                  }
                  lines={[
                    { key: "a", quantity: 2, name: "Jollof rice", variant: "Large", amount: "₦6,000" },
                    { key: "b", quantity: 1, name: "Chapman", amount: "₦1,500" },
                  ]}
                  rows={[{ label: "Delivery (Lekki)", value: "₦1,500" }]}
                  total="₦9,000"
                  compact
                />
              </div>
            </div>
            <ul className="grid content-start gap-x-10 sm:grid-cols-2">
              {features.map((f) => (
                <li key={f.title} className="border-t border-line py-5">
                  <h3 className="font-display text-lg font-bold text-ink">{f.title}</h3>
                  <p className="mt-1 text-ink-2">{f.text}</p>
                </li>
              ))}
            </ul>
          </div>
        </section>

        {/* Pricing */}
        <section id="pricing" className="scroll-mt-16 border-t border-line bg-surface">
          <div className="mx-auto max-w-6xl px-4 py-16 sm:px-6 lg:py-20">
            <h2 className="font-display text-2xl font-bold text-ink sm:text-3xl">Simple pricing</h2>
            <p className="mt-2 text-ink-2">Start free. Upgrade when your store grows.</p>
            <div className="mt-10 grid max-w-4xl gap-6 md:grid-cols-2">
              {plans.map((p) => (
                <div
                  key={p.name}
                  className={`flex flex-col rounded-panel p-6 ${p.primary ? "bg-ink text-white" : "bg-paper text-ink ring-1 ring-line ring-inset"}`}
                >
                  <h3 className="font-display text-xl font-bold">{p.name}</h3>
                  <p className="mt-2 font-display text-4xl font-extrabold">
                    {p.price}
                    {p.period && <span className={`font-sans text-base font-medium tracking-normal ${p.primary ? "text-white/70" : "text-muted"}`}>{p.period}</span>}
                  </p>
                  <ul className={`mt-6 flex-1 space-y-2.5 ${p.primary ? "text-white/85" : "text-ink-2"}`}>
                    {p.items.map((t) => (
                      <li key={t} className="flex gap-2.5">
                        <CheckIcon width={18} height={18} className={`mt-1 shrink-0 ${p.primary ? "text-marigold" : "text-ink"}`} />
                        {t}
                      </li>
                    ))}
                  </ul>
                  <Link
                    href="/signup"
                    className={`mt-8 inline-flex min-h-12 items-center justify-center rounded-control px-5 font-semibold ${
                      p.primary ? "bg-marigold text-ink hover:bg-marigold-hover" : "bg-surface text-ink ring-1 ring-line-strong ring-inset hover:ring-ink"
                    }`}
                  >
                    {p.cta}
                  </Link>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* Closing call to action */}
        <section className="bg-ink text-white">
          <div className="mx-auto flex max-w-6xl flex-col items-start gap-6 px-4 py-14 sm:px-6 md:flex-row md:items-center md:justify-between">
            <h2 className="max-w-xl font-display text-2xl font-bold sm:text-3xl">Your next order is one link away</h2>
            <Link
              href="/signup"
              className="inline-flex min-h-12 shrink-0 items-center rounded-control bg-marigold px-6 font-semibold text-ink hover:bg-marigold-hover"
            >
              Create my store
            </Link>
          </div>
        </section>
      </main>

      <footer className="border-t border-line bg-paper">
        <div className="mx-auto flex max-w-6xl flex-col gap-4 px-4 py-8 text-sm text-muted sm:flex-row sm:items-center sm:justify-between sm:px-6">
          <span>© {APP_NAME}</span>
          <nav className="-ml-3 flex flex-wrap" aria-label="Footer">
            <a href="#pricing" className="inline-flex min-h-11 items-center px-3 hover:text-ink">
              Pricing
            </a>
            <Link href="/login" className="inline-flex min-h-11 items-center px-3 hover:text-ink">
              Log in
            </Link>
            <Link href="/signup" className="inline-flex min-h-11 items-center px-3 hover:text-ink">
              Create a store
            </Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
