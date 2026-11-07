import { appHref, hubUrl, products, type Product } from "@/lib/suite";
import type { NavLink } from "./suite-header";
import { SuiteMark } from "./suite-mark";

const footLink = "inline-flex min-h-11 items-center gap-2.5 py-1 text-ls-on-night hover:text-white hover:underline underline-offset-4";

/**
 * The LinkSuite footer, the same on all eight sites: a night band with the suite's apps, this site's own links and,
 * on an app site, "Part of LinkSuite" back to the hub. On the hub it also lists where to sign in to each app.
 */
export function SuiteFooter({ app, links, note }: { app?: Product; links: NavLink[]; note?: string }) {
  const others = products.filter((p) => p.key !== app?.key);
  return (
    <footer className="ls-on-dark mt-auto bg-ls-night text-ls-on-night">
      <div className="ls-container grid gap-10 py-12 sm:py-16 md:grid-cols-[minmax(0,1.2fr)_repeat(3,minmax(0,1fr))]">
        <div className="max-w-xs">
          <a href={hubUrl} className="inline-flex min-h-11 items-center gap-2.5 rounded-ls-control">
            <SuiteMark size={24} />
            <span className="font-ls-display text-xl font-extrabold text-white">{app ? "Part of LinkSuite" : "LinkSuite"}</span>
          </a>
          <p className="ls-small mt-3">
            {note ??
              (app
                ? `${app.name} is one of seven LinkSuite apps for Nigerian small businesses. One sign-in works in all of them.`
                : "Seven apps for Nigerian small businesses, with one sign-in. Made in Lagos.")}
          </p>
        </div>

        <nav aria-labelledby="ls-foot-apps">
          <h2 id="ls-foot-apps" className="ls-label text-white">
            {app ? "Other LinkSuite apps" : "The apps"}
          </h2>
          <ul className="mt-2">
            {others.map((p) => (
              <li key={p.key}>
                <a href={appHref(p, p.landingPath)} className={footLink}>
                  <span aria-hidden="true" className="h-2.5 w-2.5 shrink-0 rounded-[2px]" style={{ background: p.accent }} />
                  {p.name}
                </a>
              </li>
            ))}
          </ul>
        </nav>

        {!app && (
          <nav aria-labelledby="sign-in" id="sign-in-list" className="scroll-mt-6">
            <h2 id="sign-in" className="ls-label text-white">
              Sign in
            </h2>
            <ul className="mt-2">
              {products.map((p) => (
                <li key={p.key}>
                  <a href={appHref(p, p.signInPath)} className={footLink}>
                    {p.name}
                  </a>
                </li>
              ))}
            </ul>
          </nav>
        )}

        <nav aria-labelledby="ls-foot-site">
          <h2 id="ls-foot-site" className="ls-label text-white">
            {app ? app.name : "LinkSuite"}
          </h2>
          <ul className="mt-2">
            {links.map((l) => (
              <li key={l.href}>
                <a href={l.href} className={footLink}>
                  {l.label}
                </a>
              </li>
            ))}
          </ul>
        </nav>
      </div>
      <div className="border-t border-white/10">
        <p className="ls-container ls-small py-5">© LinkSuite. Online payments go through Paystack.</p>
      </div>
    </footer>
  );
}
