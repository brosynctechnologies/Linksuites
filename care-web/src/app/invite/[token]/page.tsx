import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { AcceptInvite } from "./accept-invite";

export const metadata: Metadata = { title: "Accept your invitation", robots: { index: false }, alternates: {} };

/** Staff invitations (team invites). Patient portal invitations use /portal/invite/{token} (Task 20). */
export default function InvitePage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <AcceptInvite />
    </Suspense>
  );
}
