import type { ReactNode } from "react";
import type { Tone } from "@/lib/status";
import { CheckIcon, StarIcon } from "./icons";
import { cx } from "./styles";

// Server-safe badges and small display pieces (no hooks), shared by public and app pages.

export function Badge({ children, className }: { children: ReactNode; className?: string }) {
  return <span className={cx("inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap", className)}>{children}</span>;
}

const toneClass: Record<Tone, { text: string; dot: string }> = {
  neutral: { text: "bg-plaster-2 text-slate-700", dot: "bg-faint" },
  adire: { text: "bg-adire-wash text-adire", dot: "bg-adire" },
  seal: { text: "bg-seal-wash text-seal", dot: "bg-seal" },
  ochre: { text: "bg-ochre-wash text-ochre font-semibold", dot: "bg-ochre" },
  brick: { text: "bg-brick-wash text-brick-dark", dot: "bg-brick" },
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

/**
 * The verified seal: a small rounded-square Seal-green badge with a check and the word "Verified".
 * Unverified agents and agencies show nothing.
 */
export function VerifiedSeal({ verified, className, title }: { verified: boolean | null | undefined; className?: string; title?: string }) {
  if (!verified) return null;
  return (
    <span
      className={cx("inline-flex shrink-0 items-center gap-1 rounded-[4px] bg-seal px-1.5 py-0.5 text-[0.75rem] leading-none font-semibold text-white", className)}
      title={title ?? "Documents checked by PropertyLink"}
    >
      <CheckIcon className="h-3 w-3" strokeWidth={3} />
      Verified
    </span>
  );
}

/** "Featured" Adire tag on cards. */
export function FeaturedTag({ className }: { className?: string }) {
  return <span className={cx("inline-flex items-center rounded-[4px] bg-adire px-1.5 py-0.5 text-xs font-semibold text-white", className)}>Featured</span>;
}

/** "4.7 ★ (128)" with a screen-reader sentence. */
export function Rating({ average, count, className }: { average: number | null | undefined; count: number; className?: string }) {
  if (!average || count <= 0) return <span className={cx("text-xs text-muted", className)}>No reviews yet</span>;
  return (
    <span className={cx("inline-flex items-center gap-1 text-sm", className)}>
      <span className="sr-only">
        Rated {average.toFixed(1)} out of 5 from {count} review{count === 1 ? "" : "s"}
      </span>
      <span aria-hidden="true" className="tnum font-semibold text-asphalt">
        {average.toFixed(1)}
      </span>
      <StarIcon filled aria-hidden="true" className="h-3.5 w-3.5 text-asphalt" />
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
        <StarIcon key={n} filled={n <= v} className={cx("h-3.5 w-3.5", n <= v ? "text-asphalt" : "text-slate-300")} />
      ))}
    </span>
  );
}

/** Round avatar: a photo, or initials on Plaster. */
export function Avatar({ src, name, size = 48, className }: { src?: string | null; name: string; size?: number; className?: string }) {
  const letters = name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((p) => p[0] ?? "")
    .join("")
    .toUpperCase();
  return src ? (
    <img src={src} alt="" width={size} height={size} className={cx("shrink-0 rounded-full object-cover", className)} style={{ width: size, height: size }} />
  ) : (
    <span
      aria-hidden="true"
      className={cx("flex shrink-0 items-center justify-center rounded-full bg-plaster-2 font-semibold text-slate-700", className)}
      style={{ width: size, height: size, fontSize: size / 2.6 }}
    >
      {letters || "?"}
    </span>
  );
}
