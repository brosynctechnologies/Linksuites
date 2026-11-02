"use client";

import Link from "next/link";
import { useHydrated } from "@/lib/auth";
import { carryQuery, wantsImport } from "./auth-shell";
import { Notice } from "./ui";

/** Link to /login or /signup that keeps `?next=` and `?import=1`. */
export function AuthSwitchLink({ href, children }: { href: "/login" | "/signup"; children: React.ReactNode }) {
  const hydrated = useHydrated();
  return (
    <Link href={hydrated ? `${href}${carryQuery()}` : href} className="font-semibold text-ink underline-offset-2 hover:underline">
      {children}
    </Link>
  );
}

export function ImportNotice() {
  const hydrated = useHydrated();
  if (!hydrated || !wantsImport()) return null;
  return (
    <div className="mb-4">
      <Notice tone="info">Your invoice draft will be saved to your account so you can send it and track payment.</Notice>
    </div>
  );
}
