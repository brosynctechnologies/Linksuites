import type { Metadata } from "next";
import Link from "next/link";
import { SitePage } from "@/components/site-page";
import { lsButton } from "@/components/suite/ui";

export const metadata: Metadata = {
  title: "Page not found",
  robots: { index: false, follow: true },
};

export default function NotFound() {
  return (
    <SitePage hideSearch>
      <div className="ls-container ls-section-y bg-ls-chalk font-ls-text text-ls-ink">
        {/* A path that stops short: the one recurring motif, used quietly. */}
        <div className="mb-8 flex items-center gap-2" aria-hidden="true">
          <span className="h-3 w-3 rounded-full bg-lime" />
          <span className="h-[2px] w-10 bg-lime" />
          <span className="h-3 w-3 rounded-full bg-lime" />
          <span className="h-[2px] w-10 bg-line" />
          <span className="h-3 w-3 rounded-full border-2 border-dashed border-slate-400" />
        </div>
        <h1 className="ls-h1 max-w-[14ch]">This page isn&apos;t on the path.</h1>
        <p className="ls-lead mt-4 max-w-[32rem] text-ls-ink-2">
          The link may be old or mistyped. Find a course on the marketplace, or pick up where you stopped in My learning.
        </p>
        <div className="mt-8 flex flex-wrap gap-3">
          <Link href="/courses" className={lsButton("app", "lg")}>
            Browse courses
          </Link>
          <Link href="/learn" className={lsButton("secondary", "lg")}>
            My learning
          </Link>
        </div>
      </div>
    </SitePage>
  );
}
