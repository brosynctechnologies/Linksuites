import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { PortalInvite } from "./portal-invite";

export const metadata: Metadata = { title: "Patient portal invitation", robots: { index: false } };

export default function PortalInvitePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <PortalInvite />
    </Suspense>
  );
}
