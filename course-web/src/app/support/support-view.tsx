"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { AuthShell } from "@/components/auth-shell";
import { Spinner } from "@/components/ui";
import { startSupportView } from "@/lib/support";

/** Opens an admin's read-only view from `/support#token=…`. There is deliberately no login form here. */
export function SupportView() {
  const router = useRouter();
  const token = useRef<string | null | undefined>(undefined);
  const [expired, setExpired] = useState(false);

  useEffect(() => {
    if (token.current === undefined) {
      token.current = new URLSearchParams(window.location.hash.slice(1)).get("token");
      // Keep the token out of the address bar, history and referrers.
      window.history.replaceState(null, "", window.location.pathname + window.location.search);
    }
    startSupportView(token.current).then(
      () => router.replace("/studio"),
      () => setExpired(true),
    );
  }, [router]);

  if (expired) {
    return (
      <AuthShell title="This support link has expired.">
        <p className="text-sm text-slate-700">Open the academy again from the admin console to get a new link.</p>
      </AuthShell>
    );
  }

  return (
    <AuthShell title="Opening support view">
      <p role="status" className="flex items-center gap-2.5 text-sm text-muted">
        <Spinner className="h-4 w-4" />
        Checking the support link…
      </p>
    </AuthShell>
  );
}
