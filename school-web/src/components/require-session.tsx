"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { loginPathFor, logout } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { PRODUCT } from "@/lib/config";
import { AuthShell } from "./auth-shell";
import { Button, PageLoader, buttonClass } from "./ui";

/**
 * Client-side guard (the token lives in localStorage, so the server can't check it).
 * - "staff": a session with a SchoolLink school (tenant with product "school").
 * - "family": a parent or student session (no school in the token).
 */
export function RequireSession({ mode, children }: { mode: "staff" | "family"; children: ReactNode }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (hydrated && !auth) {
      router.replace(`${loginPathFor(pathname)}?next=${encodeURIComponent(pathname + window.location.search)}`);
    }
  }, [hydrated, auth, pathname, router]);

  if (!hydrated || !auth) return <PageLoader />;

  const isStaff = !!auth.tenant && (!auth.tenant.product || auth.tenant.product === PRODUCT);
  if (mode === "staff" && !isStaff) return <NotStaff hasOtherBusiness={!!auth.tenant} />;
  if (mode === "family" && auth.tenant) {
    return (
      <AuthShell title="You're signed in as staff" subtitle={auth.tenant.name}>
        <p className="text-sm text-slate-700">The parent and student pages are for family accounts. Your school&apos;s pages are in the staff app.</p>
        <Link href="/app" className={buttonClass("primary", "mt-5 w-full")}>
          Open the staff app
        </Link>
      </AuthShell>
    );
  }
  return <>{children}</>;
}

/** A parent, student or another product's account opened the staff app. */
function NotStaff({ hasOtherBusiness }: { hasOtherBusiness: boolean }) {
  const router = useRouter();
  return (
    <AuthShell title="This is the staff app" subtitle="It is for proprietors, principals, bursars and teachers.">
      <p className="text-sm text-slate-700">
        {hasOtherBusiness
          ? "You're signed in to a business that isn't a school. Sign in with the email your school invited."
          : "You're signed in with a parent or student account. Staff sign in with the email their school invited."}
      </p>
      <div className="mt-5 space-y-2">
        <Button
          className="w-full"
          onClick={async () => {
            await logout();
            router.push("/login?next=/app");
          }}
        >
          Sign in as staff
        </Button>
        {!hasOtherBusiness && (
          <Link href="/parent" className={buttonClass("secondary", "w-full")}>
            Back to my family page
          </Link>
        )}
      </div>
    </AuthShell>
  );
}
