import type { Metadata } from "next";
import { COMPANY, ContactLine, LegalPage, LegalSection } from "@/components/hub/legal-page";

export const metadata: Metadata = {
  title: "Privacy policy",
  description: "What LinkSuite collects, why, who sees it, how long we keep it and your rights under Nigerian data protection law.",
  alternates: { canonical: "/privacy" },
};

export default function PrivacyPage() {
  return (
    <LegalPage
      title="Privacy policy"
      intro={
        <>
          This explains what LinkSuite collects when you or your customers use our apps, why we collect it, who can see it and what
          you can ask us to do with it. It applies to all seven LinkSuite apps and to this site.
        </>
      }
    >
      <LegalSection title="Who we are">
        <p>
          LinkSuite is a service of {COMPANY}, a Nigerian company. We are the data controller for the account details of the
          businesses that sign up. For the records a business keeps about its own customers, pupils or patients, that business
          is the controller and we process the records on its behalf, only to run the app for it.
        </p>
      </LegalSection>

      <LegalSection title="What we collect">
        <ul>
          <li>Account details: your name, email address, phone number, password (stored only as a secure hash) and business details.</li>
          <li>
            What you put into an app: products, orders, invoices, bookings, courses, listings, school records or clinic records,
            and the files you upload.
          </li>
          <li>
            Details of your customers that you or they enter, such as names, phone numbers, addresses, and for schools and clinics,
            pupil and patient records.
          </li>
          <li>
            Payment records: amounts, dates and references. Card details are entered on the payment provider&apos;s page (Paystack or
            OPay) and never reach us.
          </li>
          <li>
            Technical details needed to keep the service safe: sign-in times, IP address, device and browser type, and an activity
            log of changes made in your account.
          </li>
        </ul>
      </LegalSection>

      <LegalSection title="Why we use it">
        <ul>
          <li>To run the apps you signed up for and show your records to the people you allow.</li>
          <li>To send emails the service needs: sign-in codes, receipts, reminders, invites and notices about your account.</li>
          <li>To take and confirm payments for plans, and to keep the accounting records the law requires.</li>
          <li>To keep the service secure, stop abuse and fraud, and fix problems.</li>
        </ul>
        <p>
          We rely on our contract with you, on our legal duties, and on our legitimate interest in keeping the service safe. We
          don&apos;t sell personal data, and we don&apos;t use your customers&apos; data for our own marketing.
        </p>
      </LegalSection>

      <LegalSection title="Who can see it">
        <ul>
          <li>The people in your business you invite, with the role you give them.</li>
          <li>
            Your customers, parents, pupils or patients, only for their own records (for example a parent sees only their own
            children&apos;s results).
          </li>
          <li>
            A small LinkSuite platform team, only to give support, check payments or deal with abuse. Every time they view an
            account it is logged.
          </li>
          <li>
            Service providers who help us run LinkSuite: our hosting and storage providers, our email provider, and Paystack or OPay
            for payments. They may only use the data to provide their service to us.
          </li>
          <li>Authorities, when Nigerian law requires us to share it.</li>
        </ul>
      </LegalSection>

      <LegalSection title="How long we keep it">
        <p>
          We keep your data while your account is open. When a business closes its account, we delete or anonymise its records
          within 90 days, except payment and invoice records we must keep for tax and accounting (usually six years), and backups,
          which are overwritten within 30 days.
        </p>
      </LegalSection>

      <LegalSection title="Your rights">
        <p>
          Under the Nigeria Data Protection Act 2023 you can ask to see the personal data we hold about you, correct it, have it
          deleted, object to how we use it, or get a copy to take elsewhere. If your data was entered by a business (for example your
          school or clinic), ask that business first; we will help them answer you. To make a request, <ContactLine />. We reply within
          30 days. You can also complain to the Nigeria Data Protection Commission.
        </p>
      </LegalSection>

      <LegalSection title="Security">
        <p>
          Connections are encrypted, passwords are hashed, staff access is limited by role, and private files such as ID documents are
          never shown publicly. No system is perfectly secure; if a breach affects you, we will tell you and the regulator as the law
          requires.
        </p>
      </LegalSection>

      <LegalSection title="Cookies">
        <p>
          We use only the cookies and browser storage needed to keep you signed in and remember your choices. We don&apos;t use
          advertising cookies.
        </p>
      </LegalSection>

      <LegalSection title="Children">
        <p>
          SchoolLink holds pupils&apos; records for their school. Pupils&apos; accounts are created by the school, and parents see their own
          children&apos;s records. We don&apos;t knowingly let children under 13 sign up a business.
        </p>
      </LegalSection>

      <LegalSection title="Changes">
        <p>
          If we change this policy in a way that matters, we will tell account owners by email before the change takes effect.
        </p>
      </LegalSection>
    </LegalPage>
  );
}
