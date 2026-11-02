import type { Metadata } from "next";
import { Suspense } from "react";
import { PageLoader } from "@/components/ui";
import { SchoolSignIn } from "./school-sign-in";

export const metadata: Metadata = { title: "Sign in to your school" };

/** The school's own sign-in page (`/{slug}`): staff, parents and students. Client-rendered; the school's name comes from the public API. */
export default function SchoolSignInPage() {
  return (
    <Suspense fallback={<PageLoader />}>
      <SchoolSignIn />
    </Suspense>
  );
}
