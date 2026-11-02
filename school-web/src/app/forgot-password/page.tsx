import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { ForgotPasswordForm } from "./forgot-password-form";

export const metadata: Metadata = { title: "Reset your password" };

export default function ForgotPasswordPage() {
  return (
    <AuthShell
      title="Reset your password"
      subtitle="Enter the email you sign in with and we'll send you a link to choose a new password. Parents can also sign in with an emailed code instead."
      footer={
        <Suspense>
          <p>
            Remembered it? <AuthSwitchLink href="/login">Sign in</AuthSwitchLink>
          </p>
        </Suspense>
      }
    >
      <ForgotPasswordForm />
    </AuthShell>
  );
}
