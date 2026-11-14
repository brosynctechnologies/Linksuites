import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Bold (free): strong, poster-like.
 * - A thick accent edge down the whole left side of the sheet (0.5rem on the web, 10pt in the PDF,
 *   full page height, on every page).
 * - Letterhead: logo and name side by side (logo left, 0.75rem gap, top-aligned); name larger than the
 *   other designs, 2.25rem extra bold (800), tight tracking; line under the name below it.
 * - Title: very large, 2.75rem on narrow screens and 3.75rem from the @md width (PDF 44pt), weight 800,
 *   tracking -0.02em, line height 0.9, accent text colour, 1.25rem above it.
 * - Number and dates as a row of three columns (label above value), 1rem under the title.
 * - Table: header row filled with the accent, on-accent text, 2px radius. Item rows: hairlines.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: right column 20rem; invoice "Total" row semibold with a hairline above; the emphasised figure
 *   in a bar filled with the accent (on-accent text, 2px radius, 0.625rem × 0.75rem padding), label
 *   0.875rem bold, figure 1.5rem extra bold.
 * - Payment details and contact, notes and terms under a 2px accent rule; footer line with a
 *   hairline.
 */
export function Bold({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={`border-l-8 ${className ?? ""}`} style={{ borderLeftColor: v.accent }}>
      <header className="mb-6">
        <div className="flex items-start gap-3">
          <Logo v={v} className="shrink-0" />
          <div className="min-w-0">
            <BusinessName v={v} sizePt={27} weight={800} lineHeight={1.05} className="tracking-[-0.02em]" />
            <LetterLine v={v} />
          </div>
        </div>
        <p
          className={`${headingFont} mt-4 text-[2.75rem] leading-[0.9] font-extrabold tracking-[-0.02em] break-words @md:text-[3rem]`}
          style={{ color: v.accentText }}
        >
          {v.title}
        </p>
        <Meta v={v} layout="row" className="mt-4" />
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable v={v} headStyle={{ background: v.accent, color: v.onAccent }} headClass="rounded-[2px]" />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} totalClass="mt-1.5 border-t border-rule-soft pt-2 font-semibold text-graphite" />
          <div
            className="mt-2 flex items-baseline justify-between gap-4 rounded-[2px] px-3 py-2.5"
            style={{ background: v.accent, color: v.onAccent }}
          >
            <dt className="text-[0.875rem] font-bold">{v.grand.label}</dt>
            <dd className="amount text-[1.5rem] leading-none font-extrabold">{v.grand.value}</dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t-2 pt-3.5" style={{ borderColor: v.accent }} />
    </Sheet>
  );
}
