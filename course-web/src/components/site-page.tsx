import type { ReactNode } from "react";
import { suiteFontVars } from "@/lib/suite-fonts";
import { SiteFooter, SiteHeader, SiteMain } from "./site-chrome";

/** Standard public page frame. Loads the suite faces itself, so My learning and checkout get the same header and footer. */
export function SitePage({ children, query, hideSearch }: { children: ReactNode; query?: string; hideSearch?: boolean }) {
  return (
    <div className={`${suiteFontVars} flex min-h-screen flex-col`}>
      <SiteHeader query={query} hideSearch={hideSearch} />
      <SiteMain>{children}</SiteMain>
      <SiteFooter />
    </div>
  );
}
