import type { Metadata } from "next";
import { Suspense, type ReactNode } from "react";
import { RequireSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";
import { ParentShell } from "./parent-shell";

export const metadata: Metadata = { title: { default: "Parent", template: "%s | SchoolLink" }, robots: { index: false } };

export default function ParentLayout({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequireSession mode="family">
        <ParentShell>{children}</ParentShell>
      </RequireSession>
    </Suspense>
  );
}
