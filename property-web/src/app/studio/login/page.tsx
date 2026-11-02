import type { Metadata } from "next";
import { Suspense } from "react";
import { AgencyLoginForm, AuthSwitchLink } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = { title: "Log in to the studio" };

export default function StudioLoginPage() {
  return (
    <AuthShell
      title="Log in to the studio"
      subtitle="For agencies, agents and landlords."
      footer={
        <Suspense>
          <div className="space-y-1">
            <p>
              New here? <AuthSwitchLink href="/studio/signup">List your property</AuthSwitchLink>
            </p>
            <p>
              Looking for a home? <AuthSwitchLink href="/login">Log in as a home seeker</AuthSwitchLink>
            </p>
          </div>
        </Suspense>
      }
    >
      <AgencyLoginForm />
    </AuthShell>
  );
}
