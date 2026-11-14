import { BillTo, BusinessName, headingFont, InfoBlocks, LetterLine, LineTable, Logo, Meta, Sheet, TotalsRows, TotalsWithSignature, type DesignProps } from "./blocks";

/*
 * Elegant (free): formal and centred. Headings (name, title, emphasised figure) always use the serif
 * family; body text follows the business's font style.
 * - Letterhead, centred: logo (centred, 0.75rem below), name 1.75rem serif semibold, line under the name.
 * - Title framed by a fine double accent rule above and below (3px double = two 1px lines with a 1px
 *   gap; 0.5rem padding inside), 1.25rem above and below the frame: title 1.75rem serif italic regular in
 *   accent text colour, centred.
 * - Number and dates centred as a row of three columns (label above value).
 * - Table: header row without fill, accent text colour labels, a 1px accent underline. Item rows: hairlines.
 * - Signature (when switched on) beside the totals, on the left (TotalsWithSignature).
 * - Totals: a centred block 20rem wide (on the right, beside the signature, when the signature is on); invoice "Total" row semibold; the emphasised figure under a 3px
 *   double accent rule, label serif italic 1rem, figure serif 1.375rem semibold, both ink.
 * - Payment details and contact, notes and terms under a hairline; footer line centred in
 *   italic, with a 1px accent rule above.
 */
export function Elegant({ v, className, animateStamp }: DesignProps) {
  return (
    <Sheet v={v} className={className} footerRule={v.accent} footerClass="italic">
      <header className="mb-6 text-center">
        {v.logoUrl && (
          <div className="mb-3 flex justify-center">
            <Logo v={v} />
          </div>
        )}
        <BusinessName v={v} weight={600} />
        <LetterLine v={v} />
        <div className="my-5 border-y-[3px] border-double py-2" style={{ borderColor: v.accent }}>
          <p className={`${headingFont} text-[1.75rem] leading-tight font-normal italic`} style={{ color: v.accentText }}>
            {v.title}
          </p>
        </div>
        <Meta v={v} layout="row" className="justify-center" />
      </header>

      <BillTo v={v} animateStamp={animateStamp} />

      <LineTable v={v} headClass="border-b" headStyle={{ borderColor: v.accent, color: v.accentText }} />

      <TotalsWithSignature v={v} center>
        <dl className="w-full max-w-[20rem] space-y-1">
          <TotalsRows v={v} totalClass="mt-1.5 border-t border-rule-soft pt-2 font-semibold text-graphite" />
          <div className="mt-2 flex items-baseline justify-between gap-4 border-t-[3px] border-double pt-2.5" style={{ borderColor: v.accent }}>
            <dt className={`${headingFont} text-base text-graphite italic`}>{v.grand.label}</dt>
            <dd className={`${headingFont} amount text-[1.375rem] leading-none font-semibold text-graphite`}>{v.grand.value}</dd>
          </div>
        </dl>
      </TotalsWithSignature>

      <InfoBlocks v={v} className="border-t border-rule-soft pt-3.5" />
    </Sheet>
  );
}
