"use client";

import Link from "next/link";
import { useState } from "react";
import { PatientSignInForm, PatientSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { Segmented, linkClass } from "@/components/ui";

/**
 * Patient portal sign in (`/api/auth/patient`): an emailed code by default, or a password; or a new account. Only the portal's own
 * sign in leads into the portal: a staff session opening `/portal` is asked to sign out first.
 */
export function PortalSignIn() {
  const [mode, setMode] = useState<"in" | "new">("in");
  return (
    <AuthShell
      title="Patient portal"
      subtitle="Book appointments, read signed notes and results, and pay bills, for yourself or your children."
      footer={
        <div className="space-y-1">
          <p>Your clinic sends you an invitation to link your record. Open its link to get started.</p>
          <p>
            Clinic staff? <Link href="/login" className={linkClass}>Staff sign in</Link>
          </p>
        </div>
      }
    >
      <Segmented
        label="Do you have a portal account?"
        items={[
          { value: "in", label: "Sign in" },
          { value: "new", label: "I'm new here" },
        ]}
        value={mode}
        onChange={setMode}
        className="mb-5"
      />
      {mode === "in" ? <PatientSignInForm key="in" /> : <PatientSignupForm key="new" />}
    </AuthShell>
  );
}
