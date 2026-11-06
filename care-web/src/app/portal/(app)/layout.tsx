import type { Metadata } from "next";
import { Suspense, type ReactNode } from "react";
import { RequirePortalSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";
import { PortalPatientProvider } from "@/lib/portal";
import { PortalShell } from "./portal-shell";

export const metadata: Metadata = { title: { default: "Patient portal", template: "%s | CareLink" }, robots: { index: false } };

/** The patient portal (Task 20): a customer session only (never a staff one), fully client-rendered like the staff app. */
export default function PortalLayout({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequirePortalSession>
        <PortalPatientProvider>
          <PortalShell>{children}</PortalShell>
        </PortalPatientProvider>
      </RequirePortalSession>
    </Suspense>
  );
}
