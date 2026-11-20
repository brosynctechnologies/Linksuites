import type { Metadata } from "next";
import { Suspense } from "react";
import { StartRedirect } from "@/components/pwa";
import { PageLoader } from "@/components/ui";

export const metadata: Metadata = { title: "CareLink", robots: { index: false }, alternates: {} };

/** The installed app's start page (manifest start_url): goes to the part of the app this person uses, or to sign in. */
export default function StartPage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <StartRedirect />
    </Suspense>
  );
}
