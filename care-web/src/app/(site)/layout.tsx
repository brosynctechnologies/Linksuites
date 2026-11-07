import type { ReactNode } from "react";
import { siteFrame } from "../site-fonts";

export default function SiteLayout({ children }: { children: ReactNode }) {
  return <div className={siteFrame}>{children}</div>;
}
