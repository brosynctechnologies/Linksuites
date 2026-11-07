import type { Metadata } from "next";
import { Suspense, type ReactNode } from "react";
import { RequireSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";
import { StudentShell } from "./student-shell";

export const metadata: Metadata = { title: { default: "Student", template: "%s | SchoolLink" }, robots: { index: false }, alternates: {} };

export default function StudentLayout({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequireSession mode="family">
        <StudentShell>{children}</StudentShell>
      </RequireSession>
    </Suspense>
  );
}
