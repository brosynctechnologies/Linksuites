import type { ReactNode } from "react";
import { cx } from "./ui";

/**
 * A phone drawn in code, holding a faithful copy of one real app screen. The drawing is decorative (aria-hidden); the
 * label describes what the screen shows, for people who cannot see it. Copy verbatim into each app.
 */
export function PhoneFrame({ label, children, className }: { label: string; children: ReactNode; className?: string }) {
  return (
    <div role="img" aria-label={label} className={cx("w-[15.5rem] shrink-0", className)}>
      <div aria-hidden="true" className="rounded-[2.1rem] bg-ls-ink p-[0.45rem] shadow-ls-phone">
        <div className="relative overflow-hidden rounded-[1.7rem] bg-white">
          <span className="absolute top-1.5 left-1/2 z-10 h-[1.1rem] w-[4.5rem] -translate-x-1/2 rounded-full bg-ls-ink" />
          {children}
        </div>
      </div>
    </div>
  );
}
