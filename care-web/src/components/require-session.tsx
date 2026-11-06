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
 * Client-side guard for the staff app (the token lives in localStorage, so the server can't check it): a session with a CareLink
 * clinic (tenant with product "care"). A patient portal session (no clinic in the token) never reaches these pages; the portal has its
 * own guard, `RequirePortalSession`.
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
          <Link href="/portal" className={buttonClass("secondary", "w-full")}>
            Back to my patient portal
          </Link>
        )}
      </div>
    </AuthShell>
  );
}

/**
 * Client-side guard for the patient portal (Task 20), as school-web's family pages: a customer session with no clinic in the token. A
 * staff session (or a support view) never shows portal pages: it is asked to sign out and sign in with a personal account. The API
 * refuses business tokens on every portal route too (PatientPortalAccess.CustomerUserId).
 */
export function RequirePortalSession({ children }: { children: ReactNode }) {
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
  if (auth.tenant) {
    return (
      <AuthShell title="You're signed in as staff" subtitle={auth.tenant.name}>
        <p className="text-sm text-slate-700">
          The patient portal is for patients and their families, and staff accounts can&apos;t open it. If you are also a patient, sign out
          and sign in with your personal account.
        </p>
        <div className="mt-5 space-y-2">
          {!auth.support && (
            <Button
              className="w-full"
              onClick={async () => {
                await logout();
                router.push(`/portal/sign-in?next=${encodeURIComponent(pathname)}`);
              }}
            >
              Sign out and open the portal
            </Button>
          )}
          <Link href="/app" className={buttonClass("secondary", "w-full")}>
            Open the staff app
          </Link>
        </div>
      </AuthShell>
    );
  }
  return <>{children}</>;
}
