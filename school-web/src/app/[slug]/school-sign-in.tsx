"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { ParentSignInForm, StaffLoginForm, StudentSignInForm } from "@/components/auth-forms";
import { PartOfLinkSuite } from "@/components/auth-shell";
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

/** A page with one message in place of the sign-in (an unknown, closed or unreachable school). */
export function SchoolMessage({ title, children }: { title: string; children: ReactNode }) {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-screen flex-col items-center justify-center bg-ruled px-6 text-center outline-none">
      <Link href="/" className="mb-8 inline-flex min-h-11 items-center rounded-control" aria-label="SchoolLink home">
        <Wordmark />
      </Link>
      <h1 className="slab text-2xl font-bold text-ink">{title}</h1>
      <div className="mt-2 max-w-sm text-sm text-muted">{children}</div>
      <Link href="/login" className={buttonClass("secondary", "mt-6")}>
        Staff sign in
      </Link>
    </main>
  );
}

export function SchoolUnavailable() {
  return <SchoolMessage title="This school's portal is unavailable">Please contact the school office.</SchoolMessage>;
}

/**
 * Parent, student and staff sign-in for one school. The server passes the school when it could look it up; otherwise it is
 * loaded here.
 */
export function SchoolSignIn({ slug, school: initial }: { slug: string; school?: SchoolPublicDto }) {
  const { data, error } = useApi<SchoolPublicDto>(initial ? null : `/api/school/${encodeURIComponent(slug)}/public`, { auth: false });
  const school = initial ?? data;
  const [who, setWho] = useState<Who>("parent");

  if (error) {
    if (error.status === 403) return <SchoolUnavailable />;
    return (
      <SchoolMessage title={error.status === 404 ? "We can't find this school" : "We couldn't open this page"}>
        {error.status === 404 ? "Check the address the school sent you, or ask the school office for the right link." : error.message}
      </SchoolMessage>
    );
  }
  if (!school) return <PageLoader />;

  return (
    <div className="flex min-h-screen flex-col bg-ruled">
      <main id="main" tabIndex={-1} className="flex-1 outline-none">
        <div className="on-blazer bg-blazer-deep text-white">
          <div className="mx-auto w-full max-w-[28rem] px-4 pt-8 pb-7">
            {school.logoUrl && (
              <img src={school.logoUrl} alt="" width={56} height={56} className="mb-3 h-14 w-14 rounded-control bg-white object-contain p-1" />
            )}
            <h1 className="slab text-[1.75rem] leading-tight font-bold">{school.name}</h1>
            {school.motto && <p className="mt-1 text-sm text-on-blazer">{school.motto}</p>}
          </div>
        </div>
        <div className="mx-auto w-full max-w-[28rem] px-4 pt-5 pb-8">
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
        </div>
      </main>
      <footer className="mx-auto flex w-full max-w-[28rem] flex-wrap items-center justify-between gap-x-4 px-4 pb-10 text-sm text-muted">
        <Link href="/" className="inline-flex min-h-11 items-center gap-1 rounded-control">
          Powered by <Wordmark className="text-sm" />
        </Link>
        <PartOfLinkSuite />
      </footer>
    </div>
  );
}
