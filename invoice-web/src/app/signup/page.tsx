import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink, ImportNotice } from "@/components/auth-links";
import { baseOpenGraph, baseTwitter } from "@/lib/site";
import { SignupForm } from "./signup-form";

const TITLE = "Create your free InvoiceLink account";
const DESCRIPTION = "Start sending invoices and quotes your clients can pay online. 10 free invoices every month, no card needed.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/signup" },
  openGraph: { ...baseOpenGraph, url: "/signup", title: TITLE, description: DESCRIPTION },
  twitter: { ...baseTwitter, title: TITLE, description: DESCRIPTION },
};

export default function SignupPage() {
  return (
    <AuthShell
      title="Create your free account"
      subtitle="10 free invoices every month. No card needed."
      footer={
        <>
          Already have an account? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
        </>
      }
    >
      <ImportNotice />
      <SignupForm />
    </AuthShell>
  );
}
