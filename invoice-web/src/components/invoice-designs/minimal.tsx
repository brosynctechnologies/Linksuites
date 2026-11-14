import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Minimal (free): quiet, lots of white. The only colour on the page is the emphasised figure.
 * - Header: two columns, no rule. Left: logo, name 1.75rem semibold ink, line under the name.
 *   Right, right-aligned: title 2.25rem light (weight 300) ink, then number and dates (labels #4a5262).
 *   2.5rem of space below the header.
 * - Labels in #4a5262 instead of the usual muted grey.
 * - Table: hairline rules only. Header row: ink text, 1px ink rule under it, no fill. Item rows: hairlines.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: right column 20rem; invoice "Total" row semibold with a 1px ink rule above; the emphasised
 *   figure below, label 0.875rem semibold ink, figure 1.375rem semibold in accent text colour
 *   (on a quote the 1px ink rule sits above the emphasised figure instead).
 * - Payment details and contact, notes and terms with space above and no rule; footer line
 *   without a rule.
 */
export function Minimal({ v, className, animateStamp }: DesignProps) {
  const label = "text-[0.75rem] font-semibold text-slate-600";
  return (
    <Sheet v={v} className={className} footerRule="transparent">
      <header className="mb-10 flex flex-col gap-4 @md:flex-row @md:items-start @md:justify-between">
        <div className="min-w-0 @md:max-w-[60%]">
          <Logo v={v} className="mb-3" />
          <BusinessName v={v} weight={600} />
          <LetterLine v={v} />
        </div>
        <div className="shrink-0 @md:text-right">
          <p className={`${headingFont} mb-2 text-[2.25rem] leading-none font-light text-graphite`}>{v.title}</p>
          <Meta v={v} labelClass="text-slate-600" />
        </div>
      </header>

      <BillTo v={v} animateStamp={animateStamp} labelClass={label} />

      <LineTable v={v} headClass="border-b border-graphite text-graphite" />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} labelClass="text-slate-600" totalClass="mt-1.5 border-t border-graphite pt-2 font-semibold text-graphite" />
          <div className={`flex items-baseline justify-between gap-4 ${v.doc.kind === "quote" ? "mt-1.5 border-t border-graphite pt-2.5" : "pt-1.5"}`}>
            <dt className="text-[0.875rem] font-semibold text-graphite">{v.grand.label}</dt>
            <dd className="amount text-[1.375rem] leading-none font-semibold" style={{ color: v.accentText }}>
              {v.grand.value}
            </dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} labelClass={label} style={{ marginTop: "1.75rem" }} />
    </Sheet>
  );
}
