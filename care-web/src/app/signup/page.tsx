import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink, ClinicSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { baseOpenGraph, baseTwitter } from "@/lib/site";

const TITLE = "Set up your clinic free";
const DESCRIPTION =
  "Start CareLink free for your clinic in Nigeria: register patients, run the queue and bill visits with up to 3 staff logins. No card needed.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/signup" },
  openGraph: { ...baseOpenGraph, url: "/signup", title: TITLE, description: DESCRIPTION },
  twitter: { ...baseTwitter, title: TITLE, description: DESCRIPTION },
};

/** What the setup checklist asks for, in the order a clinic usually does it. */
const STEPS = [
  "Complete the clinic profile: address, phone and your patient number prefix.",
  "Set up the price list, or import it from Excel.",
  "Invite your doctors, nurses, front desk and cashier.",
  "Register or import your patients, then add wards and beds when you admit.",
];

export default function SignupPage() {
  return (
    <AuthShell
      title="Set up your clinic"
      subtitle="Free for outpatients with up to 3 staff. No card needed."
      footer={
        <Suspense>
          <p>
            Already using CareLink? <AuthSwitchLink href="/login">Sign in</AuthSwitchLink>
          </p>
        </Suspense>
      }
      aside={
        <div className="case-sheet">
          <p className="case-pad display border-b border-rule bg-slate-50 py-3 text-lg font-bold text-ink">After you sign up</p>
          <ol className="case-rows">
            {STEPS.map((s, i) => (
              <li key={s} className="case-pad flex gap-3 py-3 text-sm text-slate-700">
                <span className="display tnum font-bold text-scrub">{i + 1}.</span>
                <span>{s}</span>
              </li>
            ))}
          </ol>
        </div>
      }
    >
      <ClinicSignupForm />
    </AuthShell>
  );
}
