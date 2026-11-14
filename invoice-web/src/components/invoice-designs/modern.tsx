import { tint } from "@/lib/templates";
import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Modern (free): confident, with a colour band.
 * - Header: a full-width band filled with the accent, bleeding to the sheet's top, left and right edges
 *   (top corners follow the sheet's 4px radius), padded like the page (1.5rem top and bottom).
 *   All text in the band uses the on-accent colour (white or ink #111827, whichever contrasts more).
 *   Left: logo on a white chip (4px radius, 0.375rem padding), name 1.75rem bold, line under the name.
 *   Right, right-aligned: title 2rem semibold, number and dates (labels in the full on-accent colour, for contrast).
 * - Directly under the band, a 0.25rem strip in the second accent, also full width.
 * - Table: header row filled with ink #1e2430, white text; zebra rows (#f6f7f9 on every second row),
 *   hairlines #e8ebf0.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: right column 20rem; invoice "Total" row semibold with a hairline above; the emphasised figure
 *   in a block filled with the accent at 12%, 2px accent rule on top, 0.5rem side padding; label
 *   0.875rem semibold ink, figure 1.375rem bold in accent text colour.
 * - Payment details and contact, notes and terms under a hairline; footer line with a hairline.
 */
export function Modern({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={className}>
      <header className="mx-[calc(var(--pad)*-1)] mt-[calc(var(--pad)*-1)] mb-6">
        <div
          className="flex flex-col gap-4 rounded-t-doc px-(--pad) py-6 @md:flex-row @md:items-start @md:justify-between"
          style={{ background: v.accent, color: v.onAccent }}
        >
          <div className="min-w-0 @md:max-w-[60%]">
            {v.logoUrl && (
              <div className="mb-3 inline-block rounded-[4px] bg-white p-1.5">
                <Logo v={v} />
              </div>
            )}
            <BusinessName v={v} color={v.onAccent} />
            <LetterLine v={v} color={v.onAccent} />
          </div>
          <div className="shrink-0 @md:text-right">
            <p className={`${headingFont} mb-2 text-[2rem] leading-none font-semibold`}>{v.title}</p>
            <Meta v={v} labelClass="" />
          </div>
        </div>
        <div className="h-1" style={{ background: v.second }} />
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable v={v} headStyle={{ background: "#1e2430", color: "#ffffff" }} headClass="rounded-[2px]" rowClass={(i) => i % 2 === 1 && "bg-slate-50"} />

      <TotalsWithSignature v={v}>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} totalClass="mt-1.5 border-t border-rule-soft pt-2 font-semibold text-graphite" />
          <div
            className="mt-2 flex items-baseline justify-between gap-4 border-t-2 px-2 py-2.5"
            style={{ borderColor: v.accent, background: tint(v.accent, 0.12) }}
          >
            <dt className="text-[0.875rem] font-semibold text-graphite">{v.grand.label}</dt>
            <dd className="amount text-[1.375rem] leading-none font-bold" style={{ color: v.accentText }}>
              {v.grand.value}
            </dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
