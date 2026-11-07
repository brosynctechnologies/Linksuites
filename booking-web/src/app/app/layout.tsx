import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { AppShell } from "./app-shell";

export const metadata: Metadata = { title: "Today", robots: { index: false }, alternates: {} };

/** The signed-in app is fully client-rendered (token lives in localStorage), so it streams behind Suspense. */
export default function AppLayout({ children }: LayoutProps<"/app">) {
  return (
    <Suspense fallback={<PageLoader />}>
      <AppShell>{children}</AppShell>
    </Suspense>
  );
}
