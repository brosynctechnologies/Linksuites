"use client";

import { useRouter } from "next/navigation";
import { logout } from "@/lib/api";
import { firstName, lastSchoolSlug, useAuth } from "@/lib/auth";
import { AuthShell } from "./auth-shell";
import { Button } from "./ui";

/** Placeholder home for parents and students until their portal ships: who is signed in, and a way out. */
export function FamilyPlaceholder({ who }: { who: "parent" | "student" }) {
  const auth = useAuth();
  const router = useRouter();
  return (
    <AuthShell title={`Hello, ${firstName(auth?.user.fullName)}`} subtitle={who === "parent" ? "Your parent account is ready." : "You're signed in."}>
      <p className="text-sm text-slate-700">
        {who === "parent"
          ? "Results, attendance and fees for your children will appear here soon. The school will let you know when it's ready."
          : "Your results and timetable will appear here soon. Your school will let you know when it's ready."}
      </p>
      <Button
        variant="secondary"
        className="mt-5 w-full"
        onClick={async () => {
          const slug = lastSchoolSlug();
          await logout();
          router.replace(slug ? `/${slug}` : "/");
        }}
      >
        Sign out
      </Button>
    </AuthShell>
  );
}
