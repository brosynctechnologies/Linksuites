import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { PortalSignIn } from "./portal-sign-in";

export const metadata: Metadata = { title: "Patient portal sign in" };

export default function PortalSignInPage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PortalSignIn />
    </Suspense>
  );
}
