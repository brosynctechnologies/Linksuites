import type { ReactNode } from "react";
import { cx, lsButton } from "./ui";

/**
 * LinkSuite section patterns. Copy verbatim into each app (src/components/suite/sections.tsx).
 * Every section is a <section> labelled by its own <h2>; sections are separated by a rule, never by cards or shadows.
 */

/** A page section: rule on top, an h2, an optional lead paragraph, then content. "concrete" is the one alternate ground. */
export function Section({
  id,
  title,
  intro,
  tone = "chalk",
  children,
}: {
  id: string;
  title: ReactNode;
  intro?: ReactNode;
  tone?: "chalk" | "surface" | "concrete";
  children?: ReactNode;
}) {
  return (
    <section
      id={id}
      aria-labelledby={`${id}-title`}
      className={cx(
        "scroll-mt-4 border-t border-ls-line",
        tone === "surface" && "bg-ls-surface",
        tone === "concrete" && "bg-ls-concrete",
      )}
    >
      <div className="ls-container ls-section-y">
        <h2 id={`${id}-title`} className="ls-h2 max-w-[20ch] text-ls-ink">
          {title}
        </h2>
        {intro && <div className="ls-lead mt-4 max-w-[38rem] text-ls-ink-2">{intro}</div>}
        {children}
      </div>
    </section>
  );
}

/** How it works. Only for a real sequence, so the numbers mean something. */
export function StepList({ steps }: { steps: { title: string; text: ReactNode }[] }) {
  return (
    <ol className={cx("mt-10 grid gap-x-8 gap-y-10", steps.length === 4 ? "sm:grid-cols-2 lg:grid-cols-4" : "md:grid-cols-3")}>
      {steps.map((s, i) => (
        <li key={s.title} className="border-t-2 border-ls-ink pt-4">
          <span aria-hidden="true" className="font-ls-display tnum text-5xl leading-none font-extrabold text-app-text">
            {i + 1}
          </span>
          <h3 className="ls-h4 mt-3 text-ls-ink">
            <span className="sr-only">Step {i + 1}: </span>
            {s.title}
          </h3>
          <p className="mt-1 max-w-[34ch] text-ls-ink-2">{s.text}</p>
        </li>
      ))}
    </ol>
  );
}

/**
 * Features. Each item may carry a visual: a crop of the app's real screen (see the image guide). Without visuals, items
 * sit in two columns under a rule.
 */
export function FeatureList({ items }: { items: { title: string; text: ReactNode; visual?: ReactNode }[] }) {
  return (
    <ul className="mt-10 grid gap-x-10 gap-y-2 sm:grid-cols-2">
      {items.map((f) => (
        <li key={f.title} className="border-t border-ls-line py-6">
          {f.visual && (
            <div className="mb-5 flex h-56 items-end justify-center overflow-hidden rounded-ls-panel bg-app-wash px-4 pt-6">{f.visual}</div>
          )}
          <h3 className="ls-h4 text-ls-ink">{f.title}</h3>
          <p className="mt-1.5 max-w-[46ch] text-ls-ink-2">{f.text}</p>
        </li>
      ))}
    </ul>
  );
}

function Tick({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true" className={cx("mt-[0.2rem] shrink-0", className)} fill="currentColor">
      <path d="M16.7 5.3a1 1 0 0 1 0 1.4l-8 8a1 1 0 0 1-1.4 0l-4-4a1 1 0 1 1 1.4-1.4L8 12.6l7.3-7.3a1 1 0 0 1 1.4 0z" />
    </svg>
  );
}

function Plus({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true" className={cx("mt-[0.2rem] shrink-0", className)} fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
      <path d="M10 4.5v11M4.5 10h11" />
    </svg>
  );
}

/**
 * Free and paid plans, without amounts. The free side has the main action; the paid side explains that upgrading
 * happens inside the app, where prices are shown before anyone pays.
 */
export function PlansBlock({
  free,
  paid,
  start,
  appName,
}: {
  free: string[];
  paid: string[];
  start?: { href: string; label: string };
  appName: string;
}) {
  return (
    <div className="mt-10 grid overflow-hidden rounded-ls-panel border border-ls-line bg-ls-surface md:grid-cols-2">
      <div className="flex flex-col p-6 sm:p-8">
        <h3 className="ls-h3 text-ls-ink">Free plan</h3>
        <p className="mt-1 text-ls-muted">Free for as long as it fits. No card needed.</p>
        <ul className="mt-6 flex-1 space-y-3 text-ls-ink">
          {free.map((t) => (
            <li key={t} className="flex gap-3">
              <Tick className="text-app-text" />
              {t}
            </li>
          ))}
        </ul>
        {start && (
          <a href={start.href} className={lsButton("app", "lg", "mt-8 self-start")}>
            {start.label}
          </a>
        )}
      </div>
      <div className="flex flex-col border-t border-ls-line bg-ls-concrete/60 p-6 sm:p-8 md:border-t-0 md:border-l">
        <h3 className="ls-h3 text-ls-ink">Paid plans add</h3>
        <p className="mt-1 text-ls-muted">Upgrade inside {appName} when you outgrow the free plan.</p>
        <ul className="mt-6 flex-1 space-y-3 text-ls-ink">
          {paid.map((t) => (
            <li key={t} className="flex gap-3">
              <Plus className="text-ls-ink" />
              {t}
            </li>
          ))}
        </ul>
        <p className="ls-small mt-8 text-ls-muted">
          Each plan and its price is shown in naira inside {appName} before you pay.
        </p>
      </div>
    </div>
  );
}

/**
 * Questions and answers as native disclosures: no JavaScript, keyboard and screen-reader friendly. The question is plain
 * text in <summary> (a heading inside <summary> is lost in some browser and screen-reader pairs).
 */
export function Faq({ items }: { items: { q: string; a: ReactNode }[] }) {
  return (
    <div className="ls-faq mt-8 max-w-[46rem] border-b border-ls-line">
      {items.map((it) => (
        <details key={it.q} className="group border-t border-ls-line">
          <summary className="flex min-h-14 items-center justify-between gap-4 rounded-ls-control py-4 text-left">
            <span className="ls-h4 text-ls-ink">{it.q}</span>
            <span aria-hidden="true" className="ls-faq-icon flex h-8 w-8 shrink-0 items-center justify-center rounded-full ring-1 ring-ls-field">
              <Plus className="mt-0 text-ls-ink" />
            </span>
          </summary>
          <div className="max-w-[60ch] pb-6 text-ls-ink-2">{it.a}</div>
        </details>
      ))}
    </div>
  );
}

/** The closing band: the page's signboard, painted in the app's colour (Ink on the hub), with one action. */
export function FinalCta({ title, text, action }: { title: string; text?: ReactNode; action: { href: string; label: string } }) {
  return (
    <section aria-labelledby="final-cta-title" className="ls-on-app bg-app text-app-ink">
      <div className="ls-container flex flex-col items-start gap-6 py-14 sm:py-20 md:flex-row md:items-end md:justify-between">
        <div>
          <h2 id="final-cta-title" className="ls-h2 max-w-[18ch]">
            {title}
          </h2>
          {text && <div className="ls-lead mt-3 max-w-[34rem] opacity-90">{text}</div>}
        </div>
        <a href={action.href} className={lsButton("inverse", "lg", "shrink-0 px-6")}>
          {action.label}
        </a>
      </div>
    </section>
  );
}

/** Structured data. JSON is escaped so a "<" in any string can never close the script tag. */
export function JsonLd({ data }: { data: unknown }) {
  return <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(data).replace(/</g, "\\u003c") }} />;
}
