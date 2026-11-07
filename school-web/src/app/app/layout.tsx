import type { Metadata } from "next";
import { Suspense } from "react";
import { RequireSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";
import { AppShell } from "./app-shell";

export const metadata: Metadata = { title: { default: "School", template: "%s | SchoolLink" }, robots: { index: false }, alternates: {} };

/** The staff app is fully client-rendered (the token lives in localStorage), so it streams behind Suspense. */
export default function SchoolAppLayout({ children }: LayoutProps<"/app">) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequireSession mode="staff">
        <AppShell>{children}</AppShell>
      </RequireSession>
    </Suspense>
  );
}
