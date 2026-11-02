import type { Metadata } from "next";
import { Suspense } from "react";
import { RequireSession } from "@/components/require-session";
import { SiteFooter, SiteHeader } from "@/components/site-chrome";
import { PageLoader } from "@/components/ui";
import { SeekerNav } from "./seeker-nav";

export const metadata: Metadata = { title: { default: "My PropertyLink", template: "%s | PropertyLink" }, robots: { index: false } };

/** Seeker pages are client-rendered behind the session guard (the token lives in localStorage). */
export default function SeekerLayout({ children }: LayoutProps<"/">) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 sm:px-6 sm:py-8">
        <Suspense fallback={<PageLoader />}>
          <RequireSession mode="any">
            <SeekerNav />
            <div className="mt-6">{children}</div>
          </RequireSession>
        </Suspense>
      </main>
      <SiteFooter />
    </div>
  );
}
