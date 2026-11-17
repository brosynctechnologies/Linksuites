"use client";

import { useEffect } from "react";
import { lsButton } from "@/components/suite/ui";
import { reportError } from "@/lib/report-error";

/** Shown when a page crashes. The crash is reported to the admin console's error list. */
export default function ErrorPage({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => reportError(error), [error]);
  return (
    <main id="main" className="ls-container ls-section-y flex-1">
      <h1 className="ls-h1 max-w-[16ch] text-ls-ink">Something went wrong.</h1>
      <p className="ls-lead mt-4 max-w-[32rem] text-ls-ink-2">We&apos;ve been told about it. Try again in a moment.</p>
      <button type="button" onClick={reset} className={lsButton("app", "lg", "mt-8")}>
        Try again
      </button>
    </main>
  );
}
