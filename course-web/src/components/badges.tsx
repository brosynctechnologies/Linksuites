import type { ReactNode } from "react";
import type { Tone } from "@/lib/status";
import { StarIcon } from "./icons";
import { cx } from "./styles";

// Server-safe badges and small display pieces (no hooks), shared by public and app pages.

export function Badge({ children, className }: { children: ReactNode; className?: string }) {
  return <span className={cx("inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap", className)}>{children}</span>;
}

const toneClass: Record<Tone, { text: string; dot: string }> = {
  neutral: { text: "bg-mist-2 text-slate-700", dot: "bg-faint" },
  lagoon: { text: "bg-lagoon-wash text-lagoon-text", dot: "bg-lagoon" },
  lime: { text: "bg-lime-wash text-lime-dark", dot: "bg-lime" },
  brick: { text: "bg-brick-wash text-brick-dark", dot: "bg-brick" },
  coral: { text: "bg-coral-wash text-coral-dark", dot: "bg-coral" },
};

/** Quiet status badge: a coloured dot plus the word. */
export function StatusBadge({ tone, children }: { tone: Tone; children: ReactNode }) {
  const t = toneClass[tone] ?? toneClass.neutral;
  return (
    <Badge className={t.text}>
      <span className={cx("h-1.5 w-1.5 shrink-0 rounded-full", t.dot)} aria-hidden="true" />
      {children}
    </Badge>
  );
}

/** "4.7 ★ (128)" with a screen-reader sentence. */
export function Rating({ average, count, className }: { average: number | null | undefined; count: number; className?: string }) {
  if (!average || count <= 0) return <span className={cx("text-xs text-muted", className)}>No ratings yet</span>;
  return (
    <span className={cx("inline-flex items-center gap-1 text-sm", className)}>
      <span className="sr-only">
        Rated {average.toFixed(1)} out of 5 from {count} review{count === 1 ? "" : "s"}
      </span>
      <span aria-hidden="true" className="tnum font-semibold text-ink">
        {average.toFixed(1)}
      </span>
      <StarIcon filled aria-hidden="true" className="h-3.5 w-3.5 text-ink" />
      <span aria-hidden="true" className="tnum text-muted">
        ({count})
      </span>
    </span>
  );
}

/** Five stars, read as "4 out of 5". */
export function Stars({ value, className }: { value: number; className?: string }) {
  const v = Math.round(value);
  return (
    <span className={cx("inline-flex items-center gap-0.5", className)} role="img" aria-label={`${v} out of 5 stars`}>
      {[1, 2, 3, 4, 5].map((n) => (
        <StarIcon key={n} filled={n <= v} className={cx("h-3.5 w-3.5", n <= v ? "text-ink" : "text-slate-300")} />
      ))}
    </span>
  );
}
