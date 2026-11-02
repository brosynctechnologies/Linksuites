"use client";

import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { loginPathFor, logout } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { PRODUCT } from "@/lib/config";
import { AuthShell } from "./auth-shell";
import { Button, PageLoader } from "./ui";

/**
 * Client-side guard for the staff app (the token lives in localStorage, so the server can't check it): a session with a CareLink
 * clinic (tenant with product "care"). The patient portal (Task 20) gets its own guard.
 */
export function RequireSession({ children }: { children: ReactNode }) {
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
  if (!isStaff) return <NotStaff hasOtherBusiness={!!auth.tenant} />;
  return <>{children}</>;
}

/** A patient or another product's account opened the staff app. */
function NotStaff({ hasOtherBusiness }: { hasOtherBusiness: boolean }) {
  const router = useRouter();
  return (
    <AuthShell title="This is the staff app" subtitle="It is for the medical director, admins, doctors, nurses, front desk and cashiers.">
      <p className="text-sm text-slate-700">
        {hasOtherBusiness
          ? "You're signed in to a business that isn't a clinic. Sign in with the email your clinic invited."
          : "You're signed in with a patient account. Staff sign in with the email their clinic invited."}
      </p>
      <Button
        className="mt-5 w-full"
        onClick={async () => {
          await logout();
          router.push("/login?next=/app");
        }}
      >
        Sign in as staff
      </Button>
    </AuthShell>
  );
}
