import type { ReactNode } from "react";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { CONTACT_EMAIL } from "@/lib/site";

export const LEGAL_UPDATED = "17 November 2026";
export const COMPANY = "Brosync Technologies";

/** How to reach us: the public contact address when it is set, otherwise the in-app route. */
export function ContactLine() {
  return CONTACT_EMAIL ? (
    <>
      email us at{" "}
      <a href={`mailto:${CONTACT_EMAIL}`} className="font-semibold text-ls-ink underline underline-offset-4">
        {CONTACT_EMAIL}
      </a>
    </>
  ) : (
    <>reply to any email you have had from LinkSuite</>
  );
}

export function LegalSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-10">
      <h2 className="ls-h3 text-ls-ink">{title}</h2>
      <div className="ls-body mt-3 space-y-3 text-ls-ink-2 [&_li]:mt-1.5 [&_ul]:list-disc [&_ul]:pl-6">{children}</div>
    </section>
  );
}

/** The frame for the privacy policy and the terms: one readable column under the suite header. */
export function LegalPage({ title, intro, children }: { title: string; intro: ReactNode; children: ReactNode }) {
  return (
    <>
      <SuiteHeader nav={[{ href: "/#apps", label: "Apps" }, { href: "/#plans", label: "Plans" }]} />
      <main id="main" tabIndex={-1} className="ls-container ls-section-y flex-1 outline-none">
        <article className="max-w-[42rem]">
          <h1 className="ls-h1 text-ls-ink">{title}</h1>
          <p className="ls-small mt-3 text-ls-ink-2">
            Last updated {LEGAL_UPDATED}. LinkSuite is run by {COMPANY}, Lagos, Nigeria.
          </p>
          <p className="ls-lead mt-6 text-ls-ink-2">{intro}</p>
          {children}
        </article>
      </main>
      <SuiteFooter
        links={[
          { href: "/privacy", label: "Privacy policy" },
          { href: "/terms", label: "Terms of use" },
          { href: "/#faq", label: "Questions" },
        ]}
      />
    </>
  );
}
