import type { ReactNode } from "react";
import { suiteFonts } from "@/lib/suite-fonts";

/** The LinkSuite frame for PropertyLink's own pages: chalk ground and the two suite faces. URLs don't change. */
export default function SiteLayout({ children }: { children: ReactNode }) {
  return <div className={`${suiteFonts} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`}>{children}</div>;
}
