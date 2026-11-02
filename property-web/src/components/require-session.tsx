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
 * - "any": any signed-in session (seeker or agency).
 * - "agency": a session with a PropertyLink business (tenant with product "property"); a seeker gets "Create your studio".
 */
export function RequireSession({ mode, children }: { mode: "any" | "agency"; children: ReactNode }) {
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

  const hasStudio = !!auth.tenant && (!auth.tenant.product || auth.tenant.product === PRODUCT);
  if (mode === "agency" && !hasStudio) return <CreateStudioPrompt name={auth.user.fullName} />;

  return <>{children}</>;
}

/** A seeker opened the studio: explain, and offer the agency sign-up. */
function CreateStudioPrompt({ name }: { name: string }) {
  const router = useRouter();
  return (
    <AuthShell title="List your property" subtitle="The studio is where agencies, agents and landlords post listings and answer leads.">
      <p className="text-sm text-slate-700">
        You&apos;re signed in as a home seeker{name ? `, ${name}` : ""}. To list property, create a studio with the same email and password. Your saved
        homes stay where they are.
      </p>
      <div className="mt-5 space-y-2">
        <Button
          className="w-full"
          onClick={async () => {
            await logout();
            router.push("/studio/signup?next=/studio");
          }}
        >
          Create my studio
        </Button>
        <Button
          variant="secondary"
          className="w-full"
          onClick={async () => {
            await logout();
            router.push("/studio/login?next=/studio");
          }}
        >
          I already have one: log in to the studio
        </Button>
        <Link href="/me" className={buttonClass("ghost", "w-full")}>
          Back to saved homes
        </Link>
      </div>
    </AuthShell>
  );
}
