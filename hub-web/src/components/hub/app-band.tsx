import { appHref, appStyle, type Product } from "@/lib/products";
import { PhoneFrame } from "@/components/suite/phone";
import { lsButton, lsLink } from "@/components/suite/ui";
import { screens } from "./screens";

/**
 * One app on the hub: what it does in the owner's words, who it is for, three things it does, and its real screen on
 * a phone that rises out of the band's floor. The band takes the app's colour context, so its buttons are that app's.
 */
export function AppBand({ product: p }: { product: Product }) {
  const { label, Screen } = screens[p.key];
  return (
    <article id={p.key} aria-labelledby={`${p.key}-name`} className="scroll-mt-4 border-t border-ls-line" style={appStyle(p)}>
      <div className="grid gap-8 pt-10 md:grid-cols-[minmax(0,1fr)_minmax(0,22rem)] md:gap-12 md:pt-12 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
        <div className="md:pb-12">
          <p className="flex items-center gap-2.5">
            <span aria-hidden="true" className="h-4 w-7 rounded-ls-sign bg-app" />
            <span className="ls-label text-ls-ink-2">For {p.audience.charAt(0).toLowerCase() + p.audience.slice(1)}</span>
          </p>
          <h3 id={`${p.key}-name`} className="ls-h2 mt-3 text-app-text">
            {p.name}
          </h3>
          <p className="ls-lead mt-3 max-w-[30rem] text-ls-ink">{p.job}</p>
          <ul className="mt-5 space-y-2 text-ls-ink-2">
            {p.points.map((point) => (
              <li key={point} className="flex gap-3">
                <span aria-hidden="true" className="mt-[0.6rem] h-2 w-2 shrink-0 rounded-[2px] bg-app" />
                {point}
              </li>
            ))}
          </ul>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            <a href={appHref(p, p.startPath)} className={lsButton("app")}>
              Start {p.name} free
            </a>
            <a href={appHref(p, p.landingPath)} className={lsButton("secondary")}>
              See how {p.name} works
            </a>
          </div>
          <p className="ls-small mt-4 text-ls-muted">
            Already using it?{" "}
            <a href={appHref(p, p.signInPath)} className={lsLink}>
              Sign in to {p.name}
            </a>
          </p>
        </div>
        <div className="relative flex h-[26rem] justify-center overflow-hidden rounded-t-ls-panel bg-app-wash pt-8 md:self-end">
          <PhoneFrame label={label}>
            <Screen />
          </PhoneFrame>
        </div>
      </div>
    </article>
  );
}
