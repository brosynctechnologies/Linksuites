import type { Metadata } from "next";
import { Suspense, type ReactNode } from "react";
import { RequireSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";
import { AppShell } from "./app-shell";

export const metadata: Metadata = { title: { default: "Clinic", template: "%s | CareLink" }, robots: { index: false }, alternates: {} };

/** The staff app is fully client-rendered (the token lives in localStorage), so it streams behind Suspense. */
export default function ClinicAppLayout({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequireSession>
        <AppShell>{children}</AppShell>
      </RequireSession>
    </Suspense>
  );
}
