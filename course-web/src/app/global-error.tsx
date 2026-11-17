"use client";

import { useEffect } from "react";
import { reportError } from "@/lib/report-error";

/** The last-resort error page, used when the root layout itself fails: plain HTML, no app styles. */
export default function GlobalError({ error, reset }: { error: Error & { digest?: string }; reset: () => void }) {
  useEffect(() => reportError(error), [error]);
  return (
    <html lang="en">
      <body style={{ fontFamily: "system-ui, sans-serif", margin: 0, padding: "48px 16px", color: "#1a1a1a", background: "#fff" }}>
        <main style={{ maxWidth: 480, margin: "0 auto" }}>
          <h1 style={{ fontSize: 28, margin: "0 0 12px" }}>Something went wrong.</h1>
          <p style={{ fontSize: 16, lineHeight: 1.5, margin: "0 0 24px" }}>
            We&apos;ve been told about it. Try again, and if it keeps happening, come back in a few minutes.
          </p>
          <button
            type="button"
            onClick={reset}
            style={{ minHeight: 44, padding: "0 20px", fontSize: 16, borderRadius: 8, border: "1px solid #1a1a1a", background: "#1a1a1a", color: "#fff" }}
          >
            Try again
          </button>
        </main>
      </body>
    </html>
  );
}
