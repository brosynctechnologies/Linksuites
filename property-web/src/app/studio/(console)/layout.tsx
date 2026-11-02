import type { Metadata } from "next";
import { Suspense } from "react";
import { RequireSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";
import { StudioShell } from "./studio-shell";

export const metadata: Metadata = { title: { default: "Studio", template: "%s | Studio" }, robots: { index: false } };

/** The studio is fully client-rendered (the token lives in localStorage), so it streams behind Suspense. */
export default function StudioLayout({ children }: LayoutProps<"/studio">) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequireSession mode="agency">
        <StudioShell>{children}</StudioShell>
      </RequireSession>
    </Suspense>
  );
}
