import { tint } from "@/lib/templates";
import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Classic (free): a plain business letterhead.
 * - Header: two columns. Left: logo (margin below 0.75rem), name 1.75rem bold ink, line under the name.
 *   Right, right-aligned: title 2rem semibold in accent text colour, then number and dates as a
 *   label/value grid (labels muted). Stacks to one column on narrow screens.
 * - A 2px accent rule under the whole header (1.5rem below the header content, 1.5rem above bill to).
 * - Table: header row filled with the accent at 10% (on white), ink #1e2430 text, 0.75rem semibold;
 *   item rows separated by hairlines #e8ebf0.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: right-aligned column 20rem wide; label/value rows; the invoice "Total" row has a 2px accent
 *   rule above it; the emphasised figure (balance due, or total on a quote) below it at 1.375rem bold in
 *   accent text colour, label 0.875rem semibold.
 * - Payment details and contact, notes and terms under a hairline; footer line with a hairline.
 */
export function Classic({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={className}>
      <header className="mb-6 flex flex-col gap-4 border-b-2 pb-6 @md:flex-row @md:items-start @md:justify-between" style={{ borderColor: v.accent }}>
        <div className="min-w-0 @md:max-w-[60%]">
          <Logo v={v} className="mb-3" />
          <BusinessName v={v} />
          <LetterLine v={v} />
        </div>
        <div className="shrink-0 @md:text-right">
          <p className={`${headingFont} mb-2 text-[2rem] leading-none font-semibold`} style={{ color: v.accentText }}>
            {v.title}
          </p>
          <Meta v={v} />
        </div>
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable v={v} headStyle={{ background: tint(v.accent, 0.1), color: "#1e2430" }} headClass="rounded-[2px]" />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} totalClass="mt-1.5 border-t-2 pt-2 font-semibold text-graphite" totalStyle={{ borderColor: v.accent }} />
          <div
            className={`flex items-baseline justify-between gap-4 ${v.doc.kind === "quote" ? "mt-1.5 border-t-2 pt-2.5" : "pt-1.5"}`}
            style={{ borderColor: v.accent, color: v.accentText }}
          >
            <dt className="text-[0.875rem] font-semibold">{v.grand.label}</dt>
            <dd className="amount text-[1.375rem] leading-none font-bold">{v.grand.value}</dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
