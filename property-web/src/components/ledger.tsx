import type { ReactNode } from "react";
import { formatMoney } from "@/lib/money";
import { ShieldIcon } from "./icons";
import { cx } from "./styles";

// Server-safe. The true-cost ledger: a narrow, receipt-like panel where each line is joined to its amount by a
// dotted leader, ending in a heavy rule and the total in Archivo 700, with the fixed "never pay before inspecting" line.

export interface LedgerLine {
  label: ReactNode;
  amountMinor: number | null;
  /** Small grey note under the label (e.g. "₦85,000 × 3 nights"). */
  note?: ReactNode;
  key?: string;
}

export function Ledger({
  title = "What it costs to move in",
  lines,
  totalLabel = "Move-in total",
  totalMinor,
  totalPlaceholder,
  currency = "NGN",
  footnote,
  className,
  live,
  busy,
}: {
  title?: ReactNode;
  lines: LedgerLine[];
  totalLabel?: ReactNode;
  totalMinor: number | null;
  /** Shown instead of the total while it can't be computed yet (e.g. "Pick dates"). */
  totalPlaceholder?: ReactNode;
  currency?: string;
  footnote?: ReactNode;
  className?: string;
  /** Announce total changes to screen readers (short-let quote). */
  live?: boolean;
  busy?: boolean;
}) {
  return (
    <section
      aria-label="Cost ledger"
      className={cx(
        "relative rounded-panel border border-line bg-surface px-5 pt-5 pb-4",
        // The receipt's torn top edge: a row of small notches cut from the panel.
        "before:absolute before:inset-x-3 before:-top-px before:h-[6px] before:bg-[radial-gradient(circle_at_5px_0,var(--color-plaster)_4px,transparent_4.5px)] before:bg-[length:10px_6px] before:content-['']",
        className,
      )}
    >
      <h2 className="display-wide text-sm font-bold tracking-[0.01em] text-asphalt">{title}</h2>
      <dl className={cx("mt-3 space-y-2.5 transition-opacity", busy && "opacity-60")}>
        {lines.map((line, i) => (
          <div key={line.key ?? i}>
            <div className="flex items-baseline gap-2 text-[0.9375rem]">
              <dt className="min-w-0 shrink text-slate-700">{line.label}</dt>
              <span className="leader" aria-hidden="true" />
              <dd className="price shrink-0 text-right font-semibold text-asphalt">{line.amountMinor === null ? "—" : formatMoney(line.amountMinor, currency)}</dd>
            </div>
            {line.note && <p className="mt-0.5 text-xs text-muted">{line.note}</p>}
          </div>
        ))}
      </dl>
      <div className="mt-4 border-t-[3px] border-double border-asphalt pt-3" aria-live={live ? "polite" : undefined}>
        <div className="flex items-baseline justify-between gap-3">
          <span className="font-semibold text-asphalt">{totalLabel}</span>
          {totalMinor !== null ? (
            <span className="price text-right text-[1.75rem] leading-none text-asphalt">{formatMoney(totalMinor, currency)}</span>
          ) : (
            <span className="text-right text-sm text-muted">{totalPlaceholder ?? "—"}</span>
          )}
        </div>
        {footnote && <p className="mt-1.5 text-xs text-muted">{footnote}</p>}
      </div>
      <p className="mt-4 flex gap-2 border-t border-line-soft pt-3 text-sm font-medium text-asphalt">
        <ShieldIcon className="mt-0.5 h-4 w-4 shrink-0 text-brick" />
        Never pay before you have inspected the property and met the agent.
      </p>
    </section>
  );
}

/** The first ledger line's label for a price period. */
export function priceLineLabel(purpose: string, period: string): string {
  if (purpose === "Sale") return "Asking price";
  if (period === "Month") return "First month's rent";
  if (period === "Night") return "Per night";
  return "First year's rent";
}
