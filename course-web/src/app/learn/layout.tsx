import type { Metadata } from "next";
import { Suspense } from "react";
import { RequireSession } from "@/components/require-session";
import { PageLoader } from "@/components/ui";

export const metadata: Metadata = { title: "My learning", robots: { index: false } };

/** Learner pages are client-rendered (the session lives in localStorage), so they stream behind Suspense. */
export default function LearnLayout({ children }: LayoutProps<"/learn">) {
  return (
    <Suspense fallback={<PageLoader />}>
      <RequireSession mode="any">{children}</RequireSession>
    </Suspense>
  );
}
