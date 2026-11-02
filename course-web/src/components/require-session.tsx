"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, type ReactNode } from "react";
import { loginPathFor, logout } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import { AuthShell } from "./auth-shell";
import { Button, PageLoader, buttonClass } from "./ui";

/**
 * Client-side guard (the token lives in localStorage, so the server can't check it).
 * - "any": any signed-in session (learner or tutor).
 * - "tutor": a session with an academy (tenant); a learner session gets "Create your academy".
 */
export function RequireSession({ mode, children }: { mode: "any" | "tutor"; children: ReactNode }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    if (hydrated && !auth) {
      router.replace(`${loginPathFor(pathname)}?next=${encodeURIComponent(pathname)}`);
    }
  }, [hydrated, auth, pathname, router]);

  if (!hydrated || !auth) return <PageLoader />;

  if (mode === "tutor" && !auth.tenant) return <CreateAcademyPrompt name={auth.user.fullName} />;

  return <>{children}</>;
}

/** A learner opened the studio: explain, and offer the tutor sign-up. */
function CreateAcademyPrompt({ name }: { name: string }) {
  const router = useRouter();
  return (
    <AuthShell title="Create your academy" subtitle="The studio is where tutors build courses and see their earnings.">
      <p className="text-sm text-slate-700">
        You&apos;re signed in as a learner{name ? `, ${name}` : ""}. To teach, create an academy: use the same email and password, and your courses
        as a learner stay where they are.
      </p>
      <div className="mt-5 space-y-2">
        <Button
          className="w-full"
          onClick={async () => {
            await logout();
            router.push("/teach/signup?next=/studio");
          }}
        >
          Create your academy
        </Button>
        <Button
          variant="secondary"
          className="w-full"
          onClick={async () => {
            await logout();
            router.push("/teach/login?next=/studio");
          }}
        >
          I already have one: log in to the studio
        </Button>
        <Link href="/learn" className={buttonClass("ghost", "w-full")}>
          Back to My learning
        </Link>
      </div>
    </AuthShell>
  );
}
