import { tint } from "@/lib/templates";
import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Ledger (premium): an accountant's ledger. Tabular figures throughout.
 * - Compact letterhead: logo (left, top-aligned, 0.75rem gap) beside the name 1.75rem bold and the line
 *   under the name. Top right: the registration number made prominent in a box (1px ink border,
 *   2px radius, 0.25rem × 0.5rem padding, 0.875rem semibold ink, "Reg. no. …"). The box shows when the
 *   registration number is set and shown and is not already the line under the name; the contact row
 *   then leaves it out. The tax ID stays in the contact row.
 * - Title bar: 1px ink rule above, 2px accent rule below, 0.5rem padding; title 1.5rem semibold ink on
 *   the left, number and dates as a row of three columns on the right (wrapping under on narrow screens).
 * - Table: every row ruled. Header row filled with the accent at 6%, ink text, 2px ink rule under it;
 *   item rows with 1px #c3cad5 rules; light vertical rules #d9dee6 between columns; cells 0.5rem padding.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: a box 20rem wide on the right with a 3px double ink border and 0.75rem padding; every row
 *   ruled with hairlines; invoice "Total" row semibold; the emphasised figure: label 0.875rem semibold,
 *   figure 1.25rem bold ink with a double underline (3px double ink rule under the figure only).
 * - Payment details and contact, notes and terms under a 1px #c3cad5 rule; footer line with a
 *   1px #c3cad5 rule.
 */
export function Ledger({ v, className, animateStamp }: DesignProps) {
  const regBox = v.registrationText && !(v.headerLine === "registration" && v.letterLine) ? v.registrationText : null;
  const contact = regBox ? v.contact.filter((c) => c !== regBox) : v.contact;
  return (
    <Sheet v={v} className={className} footerRule="#c3cad5">
      <header className="mb-4 flex flex-col gap-3 @md:flex-row @md:items-start @md:justify-between">
        <div className="flex min-w-0 items-start gap-3">
          <Logo v={v} className="shrink-0" />
          <div className="min-w-0">
            <BusinessName v={v} />
            <LetterLine v={v} />
          </div>
        </div>
        {regBox && (
          <p className="shrink-0 self-start rounded-[2px] border border-graphite px-2 py-1 text-[0.875rem] font-semibold tabular-nums">{regBox}</p>
        )}
      </header>

      <div className="mb-6 flex flex-col gap-2 border-t border-b-2 border-t-graphite py-2 @md:flex-row @md:items-end @md:justify-between" style={{ borderBottomColor: v.accent }}>
        <p className={`${headingFont} text-[1.5rem] leading-tight font-semibold text-graphite`}>{v.title}</p>
        <Meta v={v} layout="row" style={{ columnGap: "1.5rem" }} />
      </div>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable
        v={v}
        headClass="border-b-2 border-graphite text-graphite"
        headStyle={{ background: tint(v.accent, 0.06) }}
        rowStyle={{ borderColor: "#c3cad5" }}
        columnRules="#d9dee6"
      />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] border-[3px] border-double border-graphite p-3 tabular-nums">
          <TotalsRows v={v} rowClass="border-b border-rule-soft py-1" totalClass="font-semibold text-graphite" />
          <div className="flex items-baseline justify-between gap-4 pt-2">
            <dt className="text-[0.875rem] font-semibold text-graphite">{v.grand.label}</dt>
            <dd className="amount border-b-[3px] border-double border-graphite text-[1.25rem] leading-tight font-bold text-graphite">{v.grand.value}</dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} contact={contact} className="border-t border-slate-300 pt-3.5" />
    </Sheet>
  );
}
