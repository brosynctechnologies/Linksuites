import type { Metadata } from "next";
import { Generator } from "@/components/generator";
import { JsonLd } from "@/components/suite/sections";
import { SuiteFooter } from "@/components/suite/suite-footer";
import { SuiteHeader } from "@/components/suite/suite-header";
import { SITE_URL } from "@/lib/config";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { hubUrl, productByKey } from "@/lib/suite";

const app = productByKey("invoices");

const TITLE = "Free invoice generator: make a PDF invoice in naira";
const DESCRIPTION = "Fill in the form, watch the invoice draw itself and download a PDF. No sign-up needed. VAT, discounts and your logo, in naira or any currency.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/create" },
  openGraph: { ...baseOpenGraph, url: "/create", title: TITLE, description: DESCRIPTION },
  twitter: { ...baseTwitter, title: TITLE, description: DESCRIPTION },
};

const jsonLd = {
  "@context": "https://schema.org",
  "@type": "WebApplication",
  name: "InvoiceLink free invoice generator",
  url: `${SITE_URL}/create`,
  description: DESCRIPTION,
  applicationCategory: "BusinessApplication",
  operatingSystem: "Web",
  isAccessibleForFree: true,
  offers: { "@type": "Offer", price: "0", priceCurrency: "NGN" },
  publisher: { "@type": "Organization", name: "LinkSuite", url: hubUrl },
};

export default function CreatePage() {
  return (
    <>
      <JsonLd data={jsonLd} />
      <SuiteHeader
        app={app}
        nav={[
          { href: "/#plans", label: "Plans", wide: true },
          { href: "/login", label: "Log in" },
        ]}
        action={{ href: app.startPath, label: "Start free" }}
      />
      <main id="main" tabIndex={-1} className="ls-container flex-1 pt-8 pb-14 outline-none sm:pt-12">
        <Generator
          intro={
            <div className="pb-2">
              <h1 className="ls-h2 text-ls-ink">Free invoice generator</h1>
              <p className="ls-lead mt-3 max-w-[32rem] text-ls-ink-2">
                Fill in the details, watch the invoice update, then download your PDF. No account needed.
              </p>
            </div>
          }
        />
      </main>
      <SuiteFooter
        app={app}
        links={[
          { href: "/", label: "About InvoiceLink" },
          { href: "/#plans", label: "Plans" },
          { href: "/login", label: "Log in" },
          { href: "/signup", label: "Create an account" },
        ]}
      />
    </>
  );
}
