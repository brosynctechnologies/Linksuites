import type { Metadata } from "next";
import { Suspense } from "react";
import { AuthSwitchLink, SeekerSignupForm } from "@/components/auth-forms";
import { AuthShell } from "@/components/auth-shell";
import { shareMetadata } from "@/lib/site";

const TITLE = "Create a free account to save homes and book viewings";
const DESCRIPTION = "A free PropertyLink account saves homes and searches, emails you new listings, and books viewings and short-lets in Lagos.";

export const metadata: Metadata = {
  title: TITLE,
  description: DESCRIPTION,
  alternates: { canonical: "/signup" },
  ...shareMetadata({ title: TITLE, description: DESCRIPTION, url: "/signup" }),
};

export default function SignupPage() {
  return (
    <AuthShell
      title="Create your free account"
      subtitle="Save homes, get alerts for new listings, book viewings and short-lets."
      footer={
        <Suspense>
          <div className="space-y-1">
            <p>
              Already have an account? <AuthSwitchLink href="/login">Log in</AuthSwitchLink>
            </p>
            <p>
              Listing property? <AuthSwitchLink href="/studio/signup">Create a studio</AuthSwitchLink>
            </p>
          </div>
        </Suspense>
      }
    >
      <SeekerSignupForm />
    </AuthShell>
  );
}
