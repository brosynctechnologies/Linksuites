import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink, StaffLoginForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = { title: "Staff sign in" };

export default function LoginPage() {
  return (
    <AuthShell
      title="Staff sign in"
      subtitle="For proprietors, principals, bursars and teachers."
      footer={
        <Suspense>
          <div className="space-y-1">
            <p>
              New school? <AuthSwitchLink href="/signup">Set up your school free</AuthSwitchLink>
            </p>
            <p>Parent or student? Sign in on your school&apos;s page, from the link the school sent you.</p>
          </div>
        </Suspense>
      }
    >
      <StaffLoginForm />
    </AuthShell>
  );
}
