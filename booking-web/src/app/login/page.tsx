import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink } from "@/components/auth-links";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Log in" };

export default function LoginPage() {
  return (
    <AuthShell
      title="Welcome back"
      subtitle="Log in to see today’s chairs and bookings."
      footer={
        <>
          New here? <AuthSwitchLink href="/signup">Create a free account</AuthSwitchLink>
        </>
      }
    >
      <LoginForm />
    </AuthShell>
  );
}
