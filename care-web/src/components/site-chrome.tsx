import { cx } from "./styles";

/** The CareLink wordmark: Red Hat Display, with a small wristband mark (a band with its punched hole). */
export function Wordmark({ className, dark }: { className?: string; dark?: boolean }) {
  return (
    <span className={cx("display inline-flex items-center gap-1.5 text-[1.375rem] leading-none font-bold", dark ? "text-white" : "text-ink", className)}>
      <svg viewBox="0 0 22 14" width="22" height="14" aria-hidden="true">
        <rect x="1" y="1" width="20" height="12" rx="6" fill={dark ? "#9fe0d5" : "#0f766e"} />
        <circle cx="7" cy="7" r="2.2" fill={dark ? "#0a3a36" : "#fff"} />
      </svg>
      <span>
        Care<span className={dark ? "text-on-scrub" : "text-scrub"}>Link</span>
      </span>
    </span>
  );
}
