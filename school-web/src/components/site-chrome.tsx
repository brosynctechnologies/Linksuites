import { cx } from "./styles";

/** The SchoolLink wordmark: Zilla Slab, with a small register tick. */
export function Wordmark({ className, dark }: { className?: string; dark?: boolean }) {
  return (
    <span className={cx("slab inline-flex items-center gap-1.5 text-[1.375rem] leading-none font-bold", dark ? "text-white" : "text-ink", className)}>
      <svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true">
        <rect x="1" y="1" width="18" height="18" rx="3" fill={dark ? "#f3c9d4" : "#6b1d33"} />
        <path d="m5.5 10.5 3 3 6-7" fill="none" stroke={dark ? "#40111f" : "#fff"} strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      <span>
        School<span className={dark ? "text-on-blazer" : "text-blazer"}>Link</span>
      </span>
    </span>
  );
}
