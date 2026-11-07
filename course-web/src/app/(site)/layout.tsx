import type { ReactNode } from "react";
import { suiteFontVars } from "@/lib/suite-fonts";

/** Public pages: the marketplace, the tutors' landing, course and certificate pages. They load the LinkSuite faces. */
export default function SiteLayout({ children }: { children: ReactNode }) {
  return <div className={`${suiteFontVars} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`}>{children}</div>;
}
