"use client";

import Link from "next/link";
import { useHydrated } from "@/lib/auth";
import { carryQuery } from "./auth-shell";

/** Link to /login or /signup that keeps `?next=`. */
export function AuthSwitchLink({ href, children }: { href: "/login" | "/signup"; children: React.ReactNode }) {
  const hydrated = useHydrated();
  return (
    <Link href={hydrated ? `${href}${carryQuery()}` : href} className="font-semibold text-rose underline-offset-2 hover:underline">
      {children}
    </Link>
  );
}
