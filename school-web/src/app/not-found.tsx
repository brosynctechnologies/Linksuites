import type { Metadata } from "next";
import Link from "next/link";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton } from "@/components/suite/ui";
import { suiteFrame } from "@/lib/suite-fonts";
import { productByKey } from "@/lib/suite";

const app = productByKey("school");

export const metadata: Metadata = {
  title: "Page not found",
  robots: { index: false, follow: true },
  alternates: {},
};

export default function NotFound() {
  return (
    <div className={suiteFrame}>
      <SuiteHeader app={app} nav={[{ href: "/#plans", label: "Plans", wide: true }, { href: "/login", label: "Log in" }]} action={{ href: app.startPath, label: "Start free" }} />
      <main id="main" tabIndex={-1} className="ls-container ls-section-y flex-1 outline-none">
        <h1 className="ls-h1 max-w-[14ch] text-ls-ink">This page isn&apos;t here.</h1>
        <p className="ls-lead mt-4 max-w-[34rem] text-ls-ink-2">
          The link may be old or mistyped. If you were looking for your school&apos;s sign-in page, check the address the school sent you.
          Plans are now on the home page.
        </p>
        <div className="mt-8 flex flex-wrap gap-3">
          <Link href="/login" className={lsButton("app", "lg")}>
            Staff sign in
          </Link>
          <Link href="/" className={lsButton("secondary", "lg")}>
            Go to the home page
          </Link>
        </div>
      </main>
      <SuiteFooter app={app} links={[{ href: "/#plans", label: "Plans" }, { href: "/demo", label: "Book a demo" }, { href: "/login", label: "Staff log in" }]} />
    </div>
  );
}
