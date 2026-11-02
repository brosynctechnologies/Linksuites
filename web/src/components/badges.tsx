import type { ReactNode } from "react";
import type { OrderStatus, PaymentStatus } from "@/lib/types";

/** Server-safe pills. Status always pairs colour with a word. */

export function cx(...parts: (string | false | null | undefined)[]) {
  return parts.filter(Boolean).join(" ");
}

export type Tone = "marigold" | "ink" | "leaf" | "neutral" | "hibiscus" | "outline";

const tones: Record<Tone, { pill: string; dot: string }> = {
  marigold: { pill: "bg-marigold-soft text-marigold-deep", dot: "bg-marigold" },
  ink: { pill: "bg-ink-soft text-ink", dot: "bg-ink" },
  leaf: { pill: "bg-leaf-soft text-leaf-deep", dot: "bg-leaf" },
  neutral: { pill: "bg-paper text-muted ring-1 ring-line ring-inset", dot: "bg-subtle" },
  hibiscus: { pill: "bg-hibiscus-soft text-hibiscus-deep", dot: "bg-hibiscus" },
  outline: { pill: "bg-surface text-muted ring-1 ring-line-strong ring-inset", dot: "bg-line-strong" },
};

export function Badge({
  children,
  tone = "neutral",
  dot = false,
  className,
}: {
  children: ReactNode;
  tone?: Tone;
  dot?: boolean;
  className?: string;
}) {
  const t = tones[tone];
  return (
    <span
      className={cx(
        "inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium whitespace-nowrap",
        t.pill,
        className,
      )}
    >
      {dot && <span className={cx("h-1.5 w-1.5 shrink-0 rounded-full", t.dot)} aria-hidden="true" />}
      {children}
    </span>
  );
}

export const statusTone: Record<OrderStatus, Tone> = {
  New: "marigold",
  Confirmed: "ink",
  Paid: "leaf",
  Delivered: "neutral",
  Cancelled: "hibiscus",
};

export function StatusBadge({ status, className }: { status: OrderStatus; className?: string }) {
  return (
    <Badge tone={statusTone[status] ?? "neutral"} dot className={className}>
      {status}
    </Badge>
  );
}

export function PaymentBadge({ status, className }: { status: PaymentStatus; className?: string }) {
  return (
    <Badge tone={status === "Paid" ? "leaf" : "outline"} className={className}>
      {status}
    </Badge>
  );
}
