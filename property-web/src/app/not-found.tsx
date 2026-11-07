import type { Metadata } from "next";
import Link from "next/link";
import { suiteFonts } from "@/lib/suite-fonts";
import { SitePage } from "@/components/site-chrome";
import { lsButton } from "@/components/suite/ui";

export const metadata: Metadata = { title: "Page not found", robots: { index: false, follow: true } };

export default function NotFound() {
  return (
    <div className={`${suiteFonts} flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink`}>
      <SitePage className="ls-container ls-section-y">
        <h1 className="ls-h1 max-w-[14ch] text-ls-ink">This page isn&apos;t on the market.</h1>
        <p className="ls-lead mt-4 max-w-[34rem] text-ls-ink-2">
          The link may be old, or the home may have been rented, sold or taken down. Search for others like it.
        </p>
        <div className="mt-8 flex flex-wrap gap-3">
          <Link href="/rent" className={lsButton("app")}>
            Homes for rent
          </Link>
          <Link href="/" className={lsButton("secondary")}>
            Search all homes
          </Link>
        </div>
      </SitePage>
    </div>
  );
}
