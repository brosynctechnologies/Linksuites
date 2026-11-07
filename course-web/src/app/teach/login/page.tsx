import type { Metadata } from "next";
import { AuthSwitchLink } from "@/components/auth-links";
import { TutorLoginForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = { title: "Log in to the studio", robots: { index: false, follow: true } };

export default function TutorLoginPage() {
  return (
    <AuthShell
      title="Log in to your studio"
      subtitle="Build courses, answer learners and see your earnings."
      footer={
        <div className="space-y-2">
          <p>
            No academy yet? <AuthSwitchLink href="/teach/signup">Create one for free</AuthSwitchLink>
          </p>
          <p>
            Here to learn? <AuthSwitchLink href="/login">Learner log in</AuthSwitchLink>
          </p>
        </div>
      }
    >
      <TutorLoginForm />
    </AuthShell>
  );
}
