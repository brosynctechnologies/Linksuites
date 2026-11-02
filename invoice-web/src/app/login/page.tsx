import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink, ImportNotice } from "@/components/auth-links";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Log in" };

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
