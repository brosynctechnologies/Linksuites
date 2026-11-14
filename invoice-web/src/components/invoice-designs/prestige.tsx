import { initials } from "./model";
import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Prestige (premium): luxury and heritage. Headings and all figures use the serif family whatever the
 * font style; body text follows the font style. Generous spacing throughout.
 * - Letterhead, centred: the logo inside a fine frame (1px accent border, 0.375rem gap, then a second
 *   1px accent border inset: two concentric 1px lines). Without a logo the frame holds a monogram: the
 *   business's initials (up to two letters), serif 1.5rem semibold in accent text colour, in a 3.5rem
 *   square frame. Then the name 1.75rem serif semibold, tracking 0.02em, 1rem below the frame; the line
 *   under the name in serif italic #4a5262.
 * - A 1px second-accent rule, 40% of the content width, centred, 1.25rem above and below.
 * - Title: serif 1.5rem regular, tracking 0.08em, accent text colour, centred; number and dates centred
 *   as a row of three columns below it (0.75rem gap).
 * - Bill to with 2rem of space above the table.
 * - Table: header labels muted 0.75rem, 1px second-accent rule under them; item rows with hairlines and
 *   0.5rem vertical padding; figures in the serif.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature); its rule in the second accent.
 * - Totals: right column 20rem, rows 0.25rem apart, serif figures; invoice "Total" row semibold; the
 *   emphasised figure between two 1px second-accent rules (0.625rem padding), label serif 1rem italic ink,
 *   figure serif 1.5rem semibold in accent text colour.
 * - Payment details and contact, notes and terms under a hairline (the signature rule is in the
 *   second accent); footer line centred in serif italic with a 1px second-accent rule above.
 */
export function Prestige({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={className} footerRule={v.second} footerClass={`${headingFont} italic`}>
      <header className="mb-8 flex flex-col items-center text-center">
        <div className="mb-4 border p-1.5" style={{ borderColor: v.accent }}>
          <div className="flex min-h-[3.5rem] min-w-[3.5rem] items-center justify-center border p-1.5" style={{ borderColor: v.accent }}>
            {v.logoUrl ? (
              <Logo v={v} />
            ) : (
              <span className={`${headingFont} text-[1.5rem] leading-none font-semibold`} style={{ color: v.accentText }}>
                {initials(v.name)}
              </span>
            )}
          </div>
        </div>
        <BusinessName v={v} weight={600} className="tracking-[0.02em]" />
        <LetterLine v={v} className={`${headingFont} italic`} />
        <span className="my-5 block h-px w-2/5" style={{ background: v.second }} />
        <p className={`${headingFont} text-[1.5rem] leading-tight font-normal tracking-[0.08em]`} style={{ color: v.accentText }}>
          {v.title}
        </p>
        <Meta v={v} layout="row" className="mt-3 justify-center" />
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable
        v={v}
        className="mt-2"
        headClass="border-b text-muted"
        headStyle={{ borderColor: v.second }}
        rowStyle={{ paddingTop: "0.5rem", paddingBottom: "0.5rem" }}
        figureClass={headingFont}
      />

      <TotalsWithSignature v={v} ruleColor={v.second}>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} figureClass={headingFont} totalClass="mt-1.5 border-t border-rule-soft pt-2 font-semibold text-graphite" />
          <div className="mt-3 flex items-baseline justify-between gap-4 border-y py-2.5" style={{ borderColor: v.second }}>
            <dt className={`${headingFont} text-base text-graphite italic`}>{v.grand.label}</dt>
            <dd className={`${headingFont} amount text-[1.5rem] leading-none font-semibold`} style={{ color: v.accentText }}>
              {v.grand.value}
            </dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
