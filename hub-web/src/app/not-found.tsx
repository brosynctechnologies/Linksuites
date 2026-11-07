import type { Metadata } from "next";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import Link from "next/link";
import { lsButton } from "@/components/suite/ui";

export const metadata: Metadata = {
  title: "Page not found",
  robots: { index: false, follow: true },
};

export default function NotFound() {
  return (
    <>
      <SuiteHeader nav={[{ href: "/#apps", label: "Apps" }, { href: "/#plans", label: "Plans" }]} />
      <main id="main" tabIndex={-1} className="ls-container ls-section-y flex-1 outline-none">
        <h1 className="ls-h1 max-w-[14ch] text-ls-ink">This page isn&apos;t here.</h1>
        <p className="ls-lead mt-4 max-w-[32rem] text-ls-ink-2">
          The link may be old. Plans now live on the home page, and each app&apos;s own pages are on its own site.
        </p>
        <Link href="/#pick" className={lsButton("app", "lg", "mt-8")}>
          Find your app
        </Link>
      </main>
      <SuiteFooter links={[{ href: "/#plans", label: "Plans" }, { href: "/#faq", label: "Questions" }]} />
    </>
  );
}
