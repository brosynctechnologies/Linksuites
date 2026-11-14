import type { ComponentType } from "react";
import type { DesignProps } from "./blocks";
import { Bold } from "./bold";
import { Classic } from "./classic";
import { Corporate } from "./corporate";
import { Elegant } from "./elegant";
import { Executive } from "./executive";
import { Ledger } from "./ledger";
import { Minimal } from "./minimal";
import { Modern } from "./modern";
import { resolveDoc, type DesignKey, type PreviewDoc } from "./model";
import { Prestige } from "./prestige";
import { Studio } from "./studio";

export const DESIGNS: Record<DesignKey, ComponentType<DesignProps>> = {
  classic: Classic,
  modern: Modern,
  minimal: Minimal,
  bold: Bold,
  elegant: Elegant,
  executive: Executive,
  corporate: Corporate,
  ledger: Ledger,
  studio: Studio,
  prestige: Prestige,
};

/** Renders a document in its design (unknown template keys fall back to Classic). */
export function InvoiceDesign({ doc, className, animateStamp }: { doc: PreviewDoc; className?: string; animateStamp?: boolean }) {
  const v = resolveDoc(doc);
  const Design = DESIGNS[v.design];
  return <Design v={v} className={className} animateStamp={animateStamp} />;
}
