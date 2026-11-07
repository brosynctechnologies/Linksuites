import type { Metadata } from "next";
import Link from "next/link";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { lsButton } from "@/components/suite/ui";
import { productByKey } from "@/lib/suite";

const app = productByKey("orders");

export const metadata: Metadata = {
  title: "Page not found",
  robots: { index: false, follow: true },
  alternates: {},
};

export default function NotFound() {
  return (
    <div className="ls-body flex min-h-dvh flex-col bg-ls-chalk font-ls-text text-ls-ink">
      <SuiteHeader app={app} nav={[{ href: "/login", label: "Log in" }]} action={{ href: app.startPath, label: "Start free" }} />
      <main id="main" tabIndex={-1} className="ls-container ls-section-y flex-1 outline-none">
        <h1 className="ls-h1 max-w-[14ch] text-ls-ink">This page isn&apos;t here.</h1>
        <p className="ls-lead mt-4 max-w-[32rem] text-ls-ink-2">
          Check the link you were sent. If you were looking for a store, its name may have changed. Plans are now on the home page.
        </p>
        <Link href="/" className={lsButton("app", "lg", "mt-8")}>
          Go to the OrderLink home page
        </Link>
      </main>
      <SuiteFooter
        app={app}
        links={[
          { href: "/#plans", label: "Plans" },
          { href: "/#faq", label: "Questions" },
          { href: "/login", label: "Log in" },
        ]}
      />
    </div>
  );
}
