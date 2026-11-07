import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { PortalSignIn } from "./portal-sign-in";

export const metadata: Metadata = {
  title: "Patient portal sign in",
  description: "Sign in to your clinic's CareLink patient portal to book appointments, read signed notes and results, and pay bills.",
  robots: { index: false, follow: true },
  alternates: {},
};

export default function PortalSignInPage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PortalSignIn />
    </Suspense>
  );
}
