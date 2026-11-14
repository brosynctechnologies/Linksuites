import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Executive (premium): boardroom.
 * - Header: bleeds to the sheet's top, left and right edges. From the @md width it is two columns:
 *   a sidebar filled with the accent taking the left 40% (on-accent text, padded 1.5rem × page padding),
 *   holding the logo on a white chip (4px radius, 0.375rem padding), the name 1.5rem bold and the line
 *   under the name (full on-accent colour); and on the right 60% (white, padded 1.5rem × page padding,
 *   content bottom-aligned) the title 2rem semibold in accent text colour over number and dates.
 *   A 0.25rem second-accent rule runs under the whole header. On narrow screens the sidebar becomes a
 *   full-width band above the title.
 * - Table: header row filled with the second accent, on-second text. Item rows: hairlines.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: a boxed panel 20rem wide on the right, 1px #d9dee6 border, 4px radius, #f6f7f9 fill,
 *   1rem padding; invoice "Total" row semibold with a hairline above; the emphasised figure under a 2px
 *   second-accent rule, label 0.875rem semibold ink, figure 1.375rem bold in second-accent text colour.
 * - Payment details and contact, notes and terms under a hairline; footer line with a hairline.
 */
export function Executive({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={className}>
      <header className="mx-[calc(var(--pad)*-1)] mt-[calc(var(--pad)*-1)] mb-6">
        <div className="flex flex-col @md:flex-row">
          <div className="rounded-t-doc px-(--pad) py-6 @md:w-2/5 @md:shrink-0 @md:rounded-tr-none" style={{ background: v.accent, color: v.onAccent }}>
            {v.logoUrl && (
              <div className="mb-3 inline-block max-w-full rounded-[4px] bg-white p-1.5">
                <Logo v={v} style={{ maxWidth: "100%" }} />
              </div>
            )}
            <BusinessName v={v} sizePt={18} color={v.onAccent} />
            <LetterLine v={v} color={v.onAccent} />
          </div>
          <div className="flex min-w-0 flex-1 flex-col justify-end px-(--pad) pt-6 @md:pb-6">
            <p className={`${headingFont} mb-2 text-[2rem] leading-none font-semibold`} style={{ color: v.accentText }}>
              {v.title}
            </p>
            <div>
              <Meta v={v} />
            </div>
          </div>
        </div>
        <div className="mt-6 h-1 @md:mt-0" style={{ background: v.second }} />
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable v={v} headStyle={{ background: v.second, color: v.onSecond }} headClass="rounded-[2px]" />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] space-y-1 rounded-[4px] border border-rule bg-slate-50 p-4">
          <TotalsRows v={v} totalClass="mt-1.5 border-t border-rule pt-2 font-semibold text-graphite" />
          <div className="mt-2 flex items-baseline justify-between gap-4 border-t-2 pt-2.5" style={{ borderColor: v.second }}>
            <dt className="text-[0.875rem] font-semibold text-graphite">{v.grand.label}</dt>
            <dd className="amount text-[1.375rem] leading-none font-bold" style={{ color: v.secondText }}>
              {v.grand.value}
            </dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
