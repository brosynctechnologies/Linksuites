import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink, SeekerLoginForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = { title: "Log in" };

export default function LoginPage() {
  return (
    <AuthShell
      title="Welcome back"
      subtitle="Log in to see your saved homes, viewings and trips."
      footer={
        <Suspense>
          <div className="space-y-1">
            <p>
              New to PropertyLink? <AuthSwitchLink href="/signup">Create a free account</AuthSwitchLink>
            </p>
            <p>
              Agent or landlord? <AuthSwitchLink href="/studio/login">Log in to the studio</AuthSwitchLink>
            </p>
          </div>
        </Suspense>
      }
    >
      <SeekerLoginForm />
    </AuthShell>
  );
}
