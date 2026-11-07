import type { ReactNode } from "react";
import { suiteFrame } from "@/lib/suite-fonts";

/** Public pages in the LinkSuite frame. URLs don't change: the group only scopes the suite faces to these pages. */
export default function SiteLayout({ children }: { children: ReactNode }) {
  return <div className={suiteFrame}>{children}</div>;
}
