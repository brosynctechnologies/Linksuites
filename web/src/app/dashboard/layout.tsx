import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { DashboardShell } from "./dashboard-shell";

export const metadata: Metadata = { title: "Dashboard", robots: { index: false }, alternates: {} };

/** The dashboard is fully client-rendered (token lives in localStorage), so it streams behind Suspense. */
export default function DashboardLayout({ children }: LayoutProps<"/dashboard">) {
  return (
    <Suspense fallback={<PageLoader />}>
      <DashboardShell>{children}</DashboardShell>
    </Suspense>
  );
}
