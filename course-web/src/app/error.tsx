"use client";

import Link from "next/link";
import { useEffect } from "react";
import { Button } from "@/components/ui";
import { reportError } from "@/lib/report-error";

/** Shown when a page crashes. The crash is reported to the admin console's error list. */
export default function ErrorPage({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => reportError(error), [error]);
  return (
    <main id="main" className="mx-auto flex w-full max-w-lg flex-1 flex-col px-4 py-16">
      <h1 className="text-2xl font-bold">Something went wrong on this page.</h1>
      <p className="mt-3 text-muted">We&apos;ve been told about it. Try again, and if it keeps happening, come back in a few minutes.</p>
      <div className="mt-6 flex flex-wrap gap-3">
        <Button onClick={reset}>Try again</Button>
        <Link href="/" className="inline-flex min-h-11 items-center px-2 font-medium underline underline-offset-4">
          Go to the home page
        </Link>
      </div>
    </main>
  );
}
