import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink, StaffLoginForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";

export const metadata: Metadata = { title: "Staff sign in" };

export default function LoginPage() {
  return (
    <AuthShell
      title="Staff sign in"
      subtitle="For the medical director, admins, doctors, nurses, front desk and cashiers."
      footer={
        <Suspense>
          <div className="space-y-1">
            <p>
              New clinic? <AuthSwitchLink href="/signup">Set up your clinic free</AuthSwitchLink>
            </p>
            <p>Patient? Open the portal from the link your clinic sent you.</p>
          </div>
        </Suspense>
      }
    >
      <StaffLoginForm />
    </AuthShell>
  );
}
