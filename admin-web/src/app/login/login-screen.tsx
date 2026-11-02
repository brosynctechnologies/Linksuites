"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { SignIn } from "@/components/sign-in";
import { Loading } from "@/components/ui";
import { useHydrated, useSession } from "@/lib/session";

/** Only same-site paths are allowed as `next`. */
function safeNext(next: string | null): string {
  return next && next.startsWith("/") && !next.startsWith("//") ? next : "/";
}

export function LoginScreen() {
  const hydrated = useHydrated();
  const session = useSession();
  const router = useRouter();
  const next = safeNext(useSearchParams().get("next"));

  useEffect(() => {
    if (hydrated && session) router.replace(next);
  }, [hydrated, session, next, router]);

  if (!hydrated || session) return <Loading label="Opening the console…" />;
  return <SignIn />;
}
