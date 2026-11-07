import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink, SchoolSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { baseOpenGraph, baseTwitter } from "@/lib/site";

const TITLE = "Set up your school on SchoolLink free";
const DESCRIPTION =
  "Create your school's SchoolLink account: free for up to 50 students, no card needed. Add your classes, import students from Excel and invite your staff.";

export const metadata: Metadata = {
  title: "Set up your school free",
  description: DESCRIPTION,
  alternates: { canonical: "/signup" },
  openGraph: { ...baseOpenGraph, url: "/signup", title: TITLE, description: DESCRIPTION },
  twitter: { ...baseTwitter, title: TITLE, description: DESCRIPTION },
};

const STEPS = [
  "Add your levels and classes from the Nigerian template (Creche to SSS 3), then edit them.",
  "Import your students from Excel, with their parents' phone numbers.",
  "Invite your principal, bursar and teachers.",
  "Send parents their link by email or WhatsApp.",
];

export default function SignupPage() {
  return (
    <AuthShell
      title="Set up your school"
      subtitle="Free for up to 50 students. No card needed."
      footer={
        <Suspense>
          <p>
            Already using SchoolLink? <AuthSwitchLink href="/login">Sign in</AuthSwitchLink>
          </p>
        </Suspense>
      }
      aside={
        <div className="ruled-sheet">
          <p className="ruled-pad slab border-b border-rule py-3 text-lg font-bold text-ink">After you sign up</p>
          <ol className="ruled-rows">
            {STEPS.map((s, i) => (
              <li key={s} className="ruled-pad flex gap-3 py-3 text-sm text-slate-700">
                <span className="slab tnum font-bold text-blazer">{i + 1}.</span>
                <span>{s}</span>
              </li>
            ))}
          </ol>
        </div>
      }
    >
      <SchoolSignupForm />
    </AuthShell>
  );
}
