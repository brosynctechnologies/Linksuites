import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink, ImportNotice } from "@/components/auth-links";
import { SignupForm } from "./signup-form";

export const metadata: Metadata = { title: "Create your account" };

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
