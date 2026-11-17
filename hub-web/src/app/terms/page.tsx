import type { Metadata } from "next";
import { COMPANY, ContactLine, LegalPage, LegalSection } from "@/components/hub/legal-page";

export const metadata: Metadata = {
  title: "Terms of use",
  description: "The terms for using LinkSuite's apps: accounts, plans and payments, acceptable use, your content and our responsibilities.",
  alternates: { canonical: "/terms" },
};

export default function TermsPage() {
  return (
    <LegalPage
      title="Terms of use"
      intro={
        <>
          These terms are the agreement between your business and {COMPANY} for using LinkSuite. By creating an account or using any
          LinkSuite app, you agree to them. Our privacy policy explains how we handle personal data.
        </>
      }
    >
      <LegalSection title="Your account">
        <ul>
          <li>You must be at least 18 and able to act for the business you sign up.</li>
          <li>Give true details and keep them up to date.</li>
          <li>
            Keep your password and sign-in codes private. You are responsible for what happens in your account, including what the
            people you invite do.
          </li>
        </ul>
      </LegalSection>

      <LegalSection title="Plans and payments">
        <ul>
          <li>Each app has a free plan. Paid plans are shown in naira inside the app before you pay.</li>
          <li>A paid plan runs for the period you paid for. It doesn&apos;t renew by itself; you extend it from the app.</li>
          <li>
            You can pay online or, when shown, by bank transfer. A bank transfer starts your plan once we have confirmed the money
            arrived.
          </li>
          <li>
            Plan payments are not refunded for unused time, except where we failed to provide the service or the law requires a
            refund.
          </li>
          <li>We may change prices; a change never affects a period you have already paid for.</li>
        </ul>
      </LegalSection>

      <LegalSection title="Money your customers pay you">
        <p>
          Where an app takes payments for your business, the money is paid into your own payment account (for example your Paystack
          account). Your sale is between you and your customer: you are responsible for delivering what you sell, for refunds you owe,
          and for your own taxes. We are not a party to those sales.
        </p>
      </LegalSection>

      <LegalSection title="Acceptable use">
        <p>You must not use LinkSuite to:</p>
        <ul>
          <li>sell or advertise anything illegal in Nigeria, or list property you have no right to let or sell;</li>
          <li>mislead or defraud customers, or collect payments for things you won&apos;t provide;</li>
          <li>send spam or messages people did not ask for;</li>
          <li>upload content you don&apos;t have the right to use, or that is abusive or hateful;</li>
          <li>try to break, overload or get around the security of the service, or access other businesses&apos; data.</li>
        </ul>
        <p>
          We may remove content, hide listings or suspend an account that breaks these rules, and we will tell you why unless the law
          or an investigation stops us.
        </p>
      </LegalSection>

      <LegalSection title="Your content">
        <p>
          What you put into LinkSuite stays yours. You allow us to store, process and show it only so the apps work for you and your
          customers, for example showing your products on your store page. You can export or delete your records from the apps.
        </p>
      </LegalSection>

      <LegalSection title="Schools and clinics">
        <p>
          SchoolLink and CareLink are record-keeping tools. The school or clinic stays responsible for its teaching, its medical
          decisions and the accuracy of its records, and for having permission to keep pupils&apos; and patients&apos; data. CareLink does
          not give medical advice.
        </p>
      </LegalSection>

      <LegalSection title="Our service">
        <ul>
          <li>We work to keep LinkSuite running and your data safe, and we back it up daily.</li>
          <li>
            We may change or improve features. If we remove something you pay for, we will tell you and extend or refund your plan
            fairly.
          </li>
          <li>Short interruptions can happen, for example during updates.</li>
        </ul>
      </LegalSection>

      <LegalSection title="Responsibility">
        <p>
          To the extent the law allows, we are not responsible for indirect losses such as lost profit, and our total responsibility
          to you for any claim is limited to what you paid us in the 12 months before it. Nothing here limits rights you have under
          Nigerian consumer law that can&apos;t be limited.
        </p>
      </LegalSection>

      <LegalSection title="Ending">
        <p>
          You can stop using LinkSuite at any time. We may close an account that seriously or repeatedly breaks these terms. When an
          account closes, we handle its data as our privacy policy describes.
        </p>
      </LegalSection>

      <LegalSection title="Law and contact">
        <p>
          These terms are governed by the laws of the Federal Republic of Nigeria, and the courts of Lagos State have jurisdiction. If
          we change them in a way that matters, we will tell account owners by email first. Questions? <ContactLine />.
        </p>
      </LegalSection>
    </LegalPage>
  );
}
