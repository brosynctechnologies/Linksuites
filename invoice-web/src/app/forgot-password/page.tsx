import type { Metadata } from "next";
import { AuthShell } from "@/components/auth-shell";
import { AuthSwitchLink } from "@/components/auth-links";
import { ForgotPasswordForm } from "./forgot-password-form";

export const metadata: Metadata = { title: "Reset your password", alternates: { canonical: "/forgot-password" }, robots: { index: false } };

export default function ForgotPasswordPage() {
  return (
    <AuthShell
      title="Reset your password"
      subtitle="Enter the email you log in with and we'll send you a link to choose a new password."
      footer={
        <>
          Remembered it? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
        </>
      }
    >
      <ForgotPasswordForm />
    </AuthShell>
  );
}
