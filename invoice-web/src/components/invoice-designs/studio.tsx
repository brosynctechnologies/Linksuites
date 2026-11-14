import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Studio (premium): for creative agencies.
 * - Corner shape, top right of the sheet, touching the top and right edges (clipped to the sheet's
 *   4px radius): behind, a second-accent rectangle 32% of the sheet width wide and 18% of the sheet
 *   width tall; in front, an accent quarter circle 24% of the sheet width in radius, its centre at the
 *   sheet's top-right corner. Decorative only; text never runs under it.
 * - Letterhead, left, kept clear of the shape (max width 58% of the sheet): logo (0.75rem below, at most the medium
 *   height), then an oversized name 2.5rem extra bold (800), tracking -0.02em, line height 1.05 (a name
 *   too long for two lines steps down to 22pt); line under the name.
 * - Title row 1rem below, the full width: title 1.5rem semibold ink, and beside it number and dates as a
 *   row of three columns (label above value), wrapping under the title when they run out of room.
 * - Table: no fills. Header row muted 0.75rem labels with a 2px ink rule under it; item rows with
 *   hairlines and 0.5rem vertical padding.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: right column 20rem; label/value rows (invoice "Total" semibold with a hairline above), then a
 *   card for the emphasised figure: filled with the accent, on-accent text, 8px radius, 1rem × 1.25rem
 *   padding, label 0.875rem semibold above the figure 1.75rem extra bold, both left-aligned.
 * - Payment details and contact, notes and terms under a hairline; footer line with a hairline.
 */
export function Studio({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={className}>
      <div aria-hidden="true" className="pointer-events-none absolute top-[calc(var(--pad)*-1)] right-[calc(var(--pad)*-1)] h-[24cqw] w-[32cqw] overflow-hidden rounded-tr-doc">
        <span className="absolute top-0 right-0 h-[18cqw] w-[32cqw]" style={{ background: v.second }} />
        <span className="absolute top-0 right-0 size-[24cqw] rounded-bl-full" style={{ background: v.accent }} />
      </div>

      <header className="relative mb-4">
        <div className="max-w-[58cqw]">
          <Logo v={v} className="mb-3" style={{ maxHeight: "3.5rem" }} />
          <BusinessName v={v} sizePt={30} stepToPt={22} weight={800} lineHeight={1.05} className="tracking-[-0.02em]" />
          <LetterLine v={v} />
        </div>
        <div className="mt-4 flex flex-wrap items-end gap-x-8 gap-y-2">
          <p className={`${headingFont} text-[1.5rem] leading-tight font-semibold text-graphite`}>{v.title}</p>
          <Meta v={v} layout="row" />
        </div>
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable v={v} headClass="border-b-2 border-graphite text-muted" rowStyle={{ paddingTop: "0.5rem", paddingBottom: "0.5rem" }} />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} totalClass="mt-1.5 border-t border-rule-soft pt-2 font-semibold text-graphite" />
          <div className="mt-3 rounded-[8px] px-5 py-4" style={{ background: v.accent, color: v.onAccent }}>
            <dt className="text-[0.875rem] font-semibold">{v.grand.label}</dt>
            <dd className="amount mt-1 text-[1.75rem] leading-none font-extrabold">{v.grand.value}</dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
