import type { Metadata } from "next";
import Link from "next/link";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton } from "@/components/suite/ui";
import { productByKey } from "@/lib/suite";
import { siteFrame } from "./site-fonts";

const app = productByKey("care");

export const metadata: Metadata = {
  title: "Page not found",
  robots: { index: false, follow: true },
  alternates: {},
};

export default function NotFound() {
  return (
    <div className={siteFrame}>
      <SuiteHeader app={app} nav={[{ href: "/#plans", label: "Plans", wide: true }, { href: "/login", label: "Log in" }]} />
      <main id="main" tabIndex={-1} className="ls-container ls-section-y flex-1 outline-none">
        <h1 className="ls-h1 max-w-[14ch] text-ls-ink">We can&apos;t find this page.</h1>
        <p className="ls-lead mt-4 max-w-[32rem] text-ls-ink-2">
          The link may be old or mistyped. Staff can sign in and find the page from the menu; patients can open the portal.
        </p>
        <div className="mt-8 flex flex-wrap gap-3">
          <Link href="/login" className={lsButton("app")}>
            Staff sign in
          </Link>
          <Link href="/portal/sign-in" className={lsButton("secondary")}>
            Patient portal
          </Link>
        </div>
      </main>
      <SuiteFooter app={app} links={[{ href: "/", label: "CareLink home" }, { href: "/#plans", label: "Plans" }]} />
    </div>
  );
}
