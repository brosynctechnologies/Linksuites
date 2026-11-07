import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink, ImportNotice } from "@/components/auth-links";
import { LoginForm } from "./login-form";

export const metadata: Metadata = {
  title: "Log in",
  description: "Log in to InvoiceLink to send invoices and quotes, and see who has paid.",
  alternates: { canonical: "/login" },
  robots: { index: false },
};

export default function LoginPage() {
  return (
    <AuthShell
      title="Welcome back"
      subtitle="Log in to manage your invoices and clients."
      footer={
        <>
          New here? <AuthSwitchLink href="/signup">Create a free account</AuthSwitchLink>
        </>
      }
    >
      <ImportNotice />
      <LoginForm />
    </AuthShell>
  );
}
