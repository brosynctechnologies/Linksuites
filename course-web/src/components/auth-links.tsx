"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { useHydrated } from "@/lib/auth";
import { carryQuery } from "./auth-shell";

/** Link between login and signup pages that keeps `?next=`. */
export function AuthSwitchLink({ href, children }: { href: string; children: ReactNode }) {
  const hydrated = useHydrated();
  return (
    <Link href={hydrated ? `${href}${carryQuery()}` : href} className="font-semibold text-lagoon-text underline-offset-2 hover:underline">
      {children}
    </Link>
  );
}
