import type { Metadata } from "next";
import { AuthSwitchLink } from "@/components/auth-links";
import { LearnerLoginForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = { title: "Log in" };

export default function LoginPage() {
  return (
    <AuthShell
      title="Welcome back"
      subtitle="Log in to pick up where you stopped."
      footer={
        <div className="space-y-2">
          <p>
            New to LearnLink? <AuthSwitchLink href="/signup">Create a free account</AuthSwitchLink>
          </p>
          <p>
            Teaching? <AuthSwitchLink href="/teach/login">Log in to the studio</AuthSwitchLink>
          </p>
        </div>
      }
    >
      <LearnerLoginForm />
    </AuthShell>
  );
}
