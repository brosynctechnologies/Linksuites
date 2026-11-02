import type { Metadata } from "next";
import { AuthSwitchLink } from "@/components/auth-links";
import { AuthShell } from "@/components/auth-shell";
import { ForgotPasswordForm } from "./forgot-password-form";

export const metadata: Metadata = { title: "Reset your password" };

export default function ForgotPasswordPage() {
  return (
    <AuthShell
      title="Reset your password"
      subtitle="Enter the email you log in with and we'll send you a link to choose a new password."
      footer={
        <div className="space-y-2">
          <p>
            Remembered it? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
          </p>
          <p>
            Teaching? <AuthSwitchLink href="/teach/login">Log in to the studio</AuthSwitchLink>
          </p>
        </div>
      }
    >
      <ForgotPasswordForm />
    </AuthShell>
  );
}
