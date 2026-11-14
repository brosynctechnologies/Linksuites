import { formatDay } from "@/lib/dates";
import { tint } from "@/lib/templates";
import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Corporate (premium): bank and firm style.
 * - Header: two columns. Left: logo (0.75rem below), name 1.75rem bold ink, line under the name.
 *   Right, right-aligned and top-aligned: title 2rem semibold in accent text colour.
 * - Meta strip across the full content width, framed by two 1px accent rules (above and below),
 *   0.625rem padding top and bottom, four equal columns (two by two on narrow screens): number, issue
 *   date, due date (or valid until), balance due (or total on a quote). Each: label 0.75rem muted above
 *   the value 0.875rem semibold; the balance due value in accent text colour.
 * - Table: a grid. 1px #d9dee6 outer border, 2px radius; header row filled #f6f7f9, ink text,
 *   1px #d9dee6 rule under it; light vertical rules (#d9dee6) between all four columns; hairline rules
 *   between rows; cells padded 0.5rem horizontally.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: right column 20rem; every row separated by a hairline, 0.375rem padding; invoice "Total"
 *   row semibold; the emphasised figure in a block filled with the accent at 10%, 2px accent rule on top,
 *   0.5rem padding, label 0.875rem semibold ink, figure 1.25rem bold in accent text colour.
 * - Payment details and contact, notes and terms under a hairline; footer
 *   line with a hairline.
 */
export function Corporate({ v, className, animateStamp }: DesignProps) {
  const strip = [
    { label: v.numberLabel, value: v.doc.number || "—" },
    { label: "Issue date", value: formatDay(v.doc.issueDate) },
    { label: v.secondDateLabel, value: formatDay(v.doc.secondDate) },
    { label: v.grand.label, value: v.grand.value, accent: true },
  ];
  return (
    <Sheet v={v} className={className}>
      <header className="mb-5 flex flex-col gap-4 @md:flex-row @md:items-start @md:justify-between">
        <div className="min-w-0 @md:max-w-[60%]">
          <Logo v={v} className="mb-3" />
          <BusinessName v={v} />
          <LetterLine v={v} />
        </div>
        <p className={`${headingFont} shrink-0 text-[2rem] leading-none font-semibold @md:text-right`} style={{ color: v.accentText }}>
          {v.title}
        </p>
      </header>

      <dl className="mb-6 grid grid-cols-2 gap-x-4 gap-y-2 border-y py-2.5 @lg:grid-cols-4" style={{ borderColor: v.accent }}>
        {strip.map((it) => (
          <div key={it.label} className="min-w-0">
            <dt className="text-[0.75rem] text-muted">{it.label}</dt>
            <dd className="amount text-[0.875rem] font-semibold" style={it.accent ? { color: v.accentText } : undefined}>
              {it.value}
            </dd>
          </div>
        ))}
      </dl>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable
        v={v}
        className="rounded-[2px] border border-rule [&>[role=row]:last-child]:border-b-0"
        headClass="border-b border-rule bg-slate-50 text-graphite"
        columnRules="#d9dee6"
      />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem]">
          <TotalsRows v={v} rowClass="border-b border-rule-soft py-1.5" totalClass="font-semibold text-graphite" />
          <div
            className="mt-1 flex items-baseline justify-between gap-4 border-t-2 p-2"
            style={{ borderColor: v.accent, background: tint(v.accent, 0.1) }}
          >
            <dt className="text-[0.875rem] font-semibold text-graphite">{v.grand.label}</dt>
            <dd className="amount text-[1.25rem] leading-none font-bold" style={{ color: v.accentText }}>
              {v.grand.value}
            </dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
