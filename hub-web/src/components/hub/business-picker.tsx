import { appStyle, products } from "@/lib/products";

/**
 * The hub's main action and its one memorable element: a row of signboards, one per kind of business, each painted in
 * its app's colour. Choosing one jumps to that app's band on this page (no network, no JavaScript). The signboards are
 * painted on once at load (.ls-paint in suite.css); with reduced motion they are simply there.
 */
export function BusinessPicker() {
  return (
    <div>
      <h2 id="pick-title" className="ls-h3 text-ls-ink">
        What do you run?
      </h2>
      <ul className="mt-4 grid gap-2.5 sm:grid-cols-2">
        {products.map((p, i) => (
          <li key={p.key} style={appStyle(p)}>
            <a
              href={`#${p.key}`}
              className="ls-sign ls-paint group flex min-h-[5.5rem] flex-col justify-between gap-3 px-5 py-4 hover:brightness-[0.94]"
              style={{ ["--ls-i" as string]: i }}
            >
              <span className="ls-sign-text">{p.business}</span>
              <span className="ls-small flex items-baseline justify-between gap-3 font-medium">
                <span>
                  <span className="font-semibold">{p.name}</span>
                  <span className="opacity-85">: {p.tagline.charAt(0).toLowerCase() + p.tagline.slice(1)}</span>
                </span>
                <span aria-hidden="true" className="inline-block transition-transform duration-150 group-hover:translate-y-0.5">
                  <svg viewBox="0 0 16 16" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M8 3v10M3.5 8.5 8 13l4.5-4.5" />
                  </svg>
                </span>
              </span>
            </a>
          </li>
        ))}
        <li>
          <a
            href="#apps"
            className="ls-paint flex min-h-[5.5rem] flex-col justify-between gap-3 rounded-ls-sign border-2 border-dashed border-ls-field px-5 py-4 text-ls-ink hover:border-ls-ink"
            style={{ ["--ls-i" as string]: products.length }}
          >
            <span className="ls-sign-text">Something else, or more than one</span>
            <span className="ls-small font-medium text-ls-ink-2">See all seven apps side by side</span>
          </a>
        </li>
      </ul>
    </div>
  );
}
