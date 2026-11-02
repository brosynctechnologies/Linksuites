import Link from "next/link";
import { adminUrl, products, type Product } from "@/lib/products";

export default function Home() {
  return (
    <>
      <header className="mx-auto flex max-w-6xl items-center justify-between px-4 pt-6 sm:px-8">
        <Wordmark />
        <nav aria-label="Jump to a product" className="hidden gap-1 md:flex">
          {products.map((p) => (
            <a key={p.key} href={`#${p.key}`} className="rounded-full px-3 py-2 text-sm font-medium text-ink-2 hover:bg-surface hover:text-ink">
              {p.name}
            </a>
          ))}
        </nav>
      </header>

      <main>
        <section className="mx-auto max-w-6xl px-4 pt-14 pb-10 sm:px-8 sm:pt-20">
          <h1 className="max-w-3xl font-display text-[2.6rem] leading-[1.02] font-extrabold tracking-tight text-ink sm:text-7xl">
            Seven tools for running a business in Nigeria.
          </h1>
          <p className="mt-5 max-w-xl text-lg leading-relaxed text-ink-2">
            Each one does one job well, takes payment through Paystack and starts on a free plan. Pick the one your
            business needs and open it below.
          </p>
          <Spectrum />
        </section>

        <section aria-label="Products" className="mx-auto max-w-6xl px-4 pb-20 sm:px-8">
          <ul className="overflow-hidden rounded-2xl border border-line bg-surface">
            {products.map((p, i) => (
              <ProductRow key={p.key} product={p} first={i === 0} />
            ))}
          </ul>
          <p className="mt-6 text-sm text-muted">
            Each tool has its own account. Already signed up? Use the Sign in link on that tool&apos;s row.
          </p>
        </section>
      </main>

      <footer className="border-t border-line">
        <div className="mx-auto flex max-w-6xl flex-col gap-3 px-4 py-8 text-sm text-muted sm:flex-row sm:items-center sm:justify-between sm:px-8">
          <p>LinkSuite · Built in Lagos</p>
          <a href={`${adminUrl}/login`} className="font-medium text-ink-2 underline-offset-4 hover:underline">
            Platform team sign in
          </a>
        </div>
      </footer>
    </>
  );
}

function Wordmark() {
  return (
    <Link href="/" className="flex items-center gap-2.5" aria-label="LinkSuite home">
      <span aria-hidden="true" className="grid grid-cols-3 gap-0.5">
        {products.map((p) => (
          <span key={p.key} className="h-2.5 w-2.5 rounded-[3px]" style={{ background: p.accent }} />
        ))}
      </span>
      <span className="font-display text-xl font-bold tracking-tight text-ink">LinkSuite</span>
    </Link>
  );
}

/** The seven accents side by side; each segment jumps to its product's row. */
function Spectrum() {
  return (
    <div className="mt-10 flex h-3 overflow-hidden rounded-full" aria-hidden="true">
      {products.map((p) => (
        <a key={p.key} href={`#${p.key}`} tabIndex={-1} className="flex-1" style={{ background: p.accent }} />
      ))}
    </div>
  );
}

function ProductRow({ product: p, first }: { product: Product; first: boolean }) {
  return (
    <li id={p.key} className={`group relative scroll-mt-6 ${first ? "" : "border-t border-line"}`}>
      <span aria-hidden="true" className="key-bar absolute inset-y-0 left-0 w-2 group-hover:w-3" style={{ background: p.accent }} />
      <div className="grid gap-5 py-7 pr-5 pl-8 sm:pr-8 sm:pl-12 md:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)_auto] md:items-center md:gap-10">
        <div>
          <h2 className="font-display text-3xl font-bold tracking-tight text-ink sm:text-4xl">{p.name}</h2>
          <p className="mt-2 text-base leading-snug text-ink">{p.job}</p>
          <p className="mt-1 text-sm text-muted">For {p.audience.charAt(0).toLowerCase() + p.audience.slice(1)}</p>
        </div>
        <ul className="space-y-1.5 text-sm text-ink-2">
          {p.points.map((point) => (
            <li key={point} className="flex gap-2.5">
              <span aria-hidden="true" className="mt-[0.45rem] h-1.5 w-1.5 shrink-0 rounded-full" style={{ background: p.accent }} />
              {point}
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap items-center gap-2 md:flex-col md:items-stretch">
          <a
            href={p.url}
            className="inline-flex min-h-11 items-center justify-center rounded-lg px-5 text-sm font-semibold"
            style={{ background: p.accent, color: p.onAccent }}
          >
            Open {p.name}
          </a>
          <a
            href={`${p.url}/login`}
            className="inline-flex min-h-11 items-center justify-center rounded-lg px-5 text-sm font-semibold text-ink ring-1 ring-line ring-inset hover:bg-paper"
          >
            Sign in
          </a>
        </div>
      </div>
    </li>
  );
}
