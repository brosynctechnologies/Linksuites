"use client";

import { useEffect, useRef, useState } from "react";
import { discardSupportView, endSupportView, getAuth, useAuth } from "@/lib/auth";
import { endSupportSession, lagosTime } from "@/lib/support";

/** End the session on the server, then close the support tab; when the browser won't let us, leave for the home page. */
async function endView() {
  const auth = getAuth();
  if (auth?.support && !auth.support.ended) await endSupportSession(auth.accessToken);
  discardSupportView();
  window.close();
  window.location.replace("/");
}

/**
 * Fixed banner shown while an admin views this account read only (above sticky chrome, below modals). It publishes its height as
 * `--support-bar` so the page (and sticky chrome) moves down instead of being covered.
 * Once the token has expired it is replaced by the "Support view ended" screen.
 */
export function SupportBanner() {
  const support = useAuth()?.support;
  const active = !!support && !support.ended;
  const expiresAt = support?.expiresAt;
  const bar = useRef<HTMLDivElement>(null);
  const [ending, setEnding] = useState(false);

  useEffect(() => {
    const el = bar.current;
    if (!active || !el) return;
    const root = document.documentElement;
    const measure = () => root.style.setProperty("--support-bar", `${el.offsetHeight}px`);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => {
      observer.disconnect();
      root.style.removeProperty("--support-bar");
    };
  }, [active]);

  useEffect(() => {
    if (!active || !expiresAt) return;
    const timer = window.setTimeout(endSupportView, Math.max(0, Date.parse(expiresAt) - Date.now()));
    return () => window.clearTimeout(timer);
  }, [active, expiresAt]);

  if (!support) return null;

  if (support.ended) {
    return (
      <div className="fixed inset-0 z-[70] flex items-center justify-center bg-mist px-4">
        <div role="alert" className="w-full max-w-[26rem] rounded-panel border border-line bg-surface p-5 sm:p-6">
          <h1 className="text-2xl leading-tight font-bold tracking-[-0.01em] text-aubergine">Support view ended</h1>
          <p className="mt-2 text-sm text-slate-700">
            The read-only view of this business has ended. Open the business again from the admin console for a new link.
          </p>
          <button
            type="button"
            onClick={endView}
            className="mt-5 inline-flex min-h-10 items-center justify-center rounded-control bg-rose px-4 py-2 text-sm font-semibold text-white hover:bg-rose-dark"
          >
            Close tab
          </button>
        </div>
      </div>
    );
  }

  return (
    <div
      ref={bar}
      role="region"
      aria-label="Support view"
      className="fixed inset-x-0 top-0 z-[45] flex flex-wrap items-center justify-between gap-x-4 gap-y-1 bg-rose px-4 py-1.5 text-sm text-white sm:px-6"
    >
      <p className="font-semibold">
        Viewing as support (read only). Ends at <span className="font-expanded font-bold tnum">{lagosTime(support.expiresAt)}</span>.
      </p>
      <button
        type="button"
        onClick={() => {
          setEnding(true);
          void endView();
        }}
        disabled={ending}
        aria-busy={ending || undefined}
        className="inline-flex min-h-9 items-center rounded-control bg-white px-3 font-semibold text-rose hover:bg-rose-wash focus-visible:outline-white disabled:opacity-70"
      >
        End view
      </button>
    </div>
  );
}
