import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { AcceptInvite } from "./accept-invite";

export const metadata: Metadata = { title: "Accept your invitation", robots: { index: false } };

/** One link shape for both invitations the school sends: parents (guardian invites) and staff (team invites). */
export default function InvitePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <AcceptInvite />
    </Suspense>
  );
}
