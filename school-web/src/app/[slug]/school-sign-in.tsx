"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { ParentSignInForm, StaffLoginForm, StudentSignInForm } from "@/components/auth-forms";
import { Wordmark } from "@/components/site-chrome";
import { PageLoader, Tabs, buttonClass } from "@/components/ui";
import { useApi } from "@/lib/use-api";
import type { SchoolPublicDto } from "@/lib/types";

type Who = "parent" | "student" | "staff";

const TABS: { value: Who; label: string }[] = [
  { value: "parent", label: "Parent" },
  { value: "student", label: "Student" },
  { value: "staff", label: "Staff" },
];

const HINTS: Record<Who, string> = {
  parent: "Use the email the school has for you. We'll send a code, so there's no password to remember.",
  student: "For primary and secondary students, with the password the school gave you.",
  staff: "Proprietors, principals, bursars and teachers.",
};

export function SchoolSignIn() {
  const { slug } = useParams<{ slug: string }>();
  const { data: school, error } = useApi<SchoolPublicDto>(`/api/school/${encodeURIComponent(slug)}/public`, { auth: false });
  const [who, setWho] = useState<Who>("parent");

  if (error) {
    return (
      <main className="flex min-h-screen flex-col items-center justify-center bg-ruled px-6 text-center">
        <Link href="/" className="mb-8 inline-flex min-h-11 items-center rounded-control">
          <Wordmark />
        </Link>
        <h1 className="slab text-2xl font-bold text-ink">
          {error.status === 404 ? "We can't find this school" : error.status === 403 ? "This school's portal is unavailable." : "We couldn't open this page"}
</h1>
        <p className="mt-2 max-w-sm text-sm text-muted">
          {error.status === 404
            ? "Check the address the school sent you, or ask the school office for the right link."
            : error.status === 403
              ? "Please contact the school office."
              : error.message}
        </p>
        <Link href="/login" className={buttonClass("secondary", "mt-6")}>
          Staff sign in
        </Link>
      </main>
    );
  }
  if (!school) return <PageLoader />;

  return (
    <div className="flex min-h-screen flex-col bg-ruled">
      <header className="on-blazer bg-blazer-deep text-white">
        <div className="mx-auto w-full max-w-[28rem] px-4 pt-8 pb-7">
          {school.logoUrl && <img src={school.logoUrl} alt="" className="mb-3 h-14 w-14 rounded-control bg-white object-contain p-1" />}
          <h1 className="slab text-[1.75rem] leading-tight font-bold">{school.name}</h1>
          {school.motto && <p className="mt-1 text-sm text-on-blazer">{school.motto}</p>}
        </div>
      </header>
      <main className="mx-auto w-full max-w-[28rem] flex-1 px-4 pt-5 pb-14">
        <h2 className="sr-only">Sign in</h2>
        <div className="rounded-panel border border-rule bg-surface">
          <Tabs items={TABS} value={who} onChange={setWho} label="I am a" idBase="who" className="mx-0 px-2 sm:px-2" />
          <div id={`who-panel-${who}`} role="tabpanel" aria-labelledby={`who-tab-${who}`} className="p-5">
            <p className="mb-4 text-sm text-slate-600">{HINTS[who]}</p>
            {who === "parent" && <ParentSignInForm key="parent" schoolSlug={slug} />}
            {who === "student" && <StudentSignInForm key="student" schoolSlug={slug} />}
            {who === "staff" && <StaffLoginForm key="staff" schoolSlug={slug} />}
          </div>
        </div>
        <p className="mt-6 text-center text-xs text-muted">
          <Link href="/" className="inline-flex min-h-11 items-center">
            Powered by <Wordmark className="ml-1 text-sm" />
          </Link>
        </p>
      </main>
    </div>
  );
}
