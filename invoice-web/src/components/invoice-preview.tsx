import { InvoiceDesign } from "./invoice-designs";
import type { PreviewDoc } from "./invoice-designs/model";

export { DocStamp } from "./invoice-designs/blocks";
export {
  LETTERHEAD_DEFAULTS,
  linesFromDto,
  type DocumentTitle,
  type HeaderLine,
  type LogoSize,
  type PreviewBusiness,
  type PreviewDoc,
  type PreviewLetterhead,
  type PreviewLine,
  type PreviewParty,
  type StampKind,
} from "./invoice-designs/model";

/**
 * The paper document: the HTML twin of the server-rendered PDF, drawn in its design (one component per
 * template key in ./invoice-designs) as a white sheet on the desk (A4 proportion from the sm breakpoint up).
 * Pure component (no hooks) so it works in both server and client components.
 * Uses container queries, so it adapts to the width of whatever column it sits in.
 */
export function InvoicePreview({ doc, className, animateStamp }: { doc: PreviewDoc; className?: string; animateStamp?: boolean }) {
  return <InvoiceDesign doc={doc} className={className} animateStamp={animateStamp} />;
}
