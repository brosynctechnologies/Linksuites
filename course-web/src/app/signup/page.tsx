import type { Metadata } from "next";
import { AuthSwitchLink } from "@/components/auth-links";
import { LearnerSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = {
  title: "Create a free learner account",
  description: "One LearnLink account for every course you take, from WAEC and JAMB prep to digital skills. Learn on your phone and earn certificates.",
  alternates: { canonical: "/signup" },
};

export default function SignupPage() {
  return (
    <AuthShell
      title="Start learning"
      subtitle="One account for every course you take, on any device."
      footer={
        <div className="space-y-2">
          <p>
            Already have an account? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
          </p>
          <p>
            Want to teach? <AuthSwitchLink href="/teach/signup">Create your academy</AuthSwitchLink>
          </p>
        </div>
      }
    >
      <LearnerSignupForm />
    </AuthShell>
  );
}
