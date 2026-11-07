import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { ForgotPasswordForm } from "./forgot-password-form";

export const metadata: Metadata = { title: "Reset your password", robots: { index: false } };

export default function ForgotPasswordPage() {
  return (
    <AuthShell
      title="Reset your password"
      subtitle="Enter the email you log in with and we'll send you a link to choose a new password."
      footer={
        <Suspense>
          <div className="space-y-1">
            <p>
              Remembered it? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
            </p>
            <p>
              Agent or landlord? <AuthSwitchLink href="/studio/login">Log in to the studio</AuthSwitchLink>
            </p>
          </div>
        </Suspense>
      }
    >
      <ForgotPasswordForm />
    </AuthShell>
  );
}
