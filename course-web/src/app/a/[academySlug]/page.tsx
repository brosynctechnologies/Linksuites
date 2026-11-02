import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense, type CSSProperties } from "react";
import { AccountLinks } from "@/components/account-links";
import { CourseGrid, CourseRowSkeleton } from "@/components/course-card";
import { Markdown } from "@/components/markdown";
import { LoadProblem, Wordmark } from "@/components/site-chrome";
import { brandVars, initials, normalizeHex, readableOn } from "@/lib/color";
import { APP_NAME } from "@/lib/config";
import { plural } from "@/lib/format";
import { serverGet } from "@/lib/server-api";
import type { AcademyPageDto } from "@/lib/types";

function academyPath(slug: string) {
  return `/api/catalog/academies/${encodeURIComponent(slug)}`;
}

export async function generateMetadata({ params }: PageProps<"/a/[academySlug]">): Promise<Metadata> {
  const { academySlug } = await params;
  const res = await serverGet<AcademyPageDto>(academyPath(academySlug));
  if (!res.ok) return { title: "Academy" };
  return { title: res.data.name, description: res.data.headline ?? `Courses from ${res.data.name}.` };
}

/** A tutor's own academy, in the academy's brand colour. Course links carry ?via=academy. */
export default function AcademyPage({ params }: PageProps<"/a/[academySlug]">) {
  return (
    <Suspense
      fallback={
        <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
          <div className="h-10 w-1/2 animate-pulse rounded bg-mist-2" />
          <div className="mt-8">
            <CourseRowSkeleton />
          </div>
        </div>
      }
    >
      {params.then(({ academySlug }) => (
        <Academy slug={academySlug} />
      ))}
    </Suspense>
  );
}

async function Academy({ slug }: { slug: string }) {
  const res = await serverGet<AcademyPageDto>(academyPath(slug));
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="this academy" />
      </div>
    );
  }
  const a = res.data;
  const brand = normalizeHex(a.brandColor);
  const onBrand = readableOn(brand);
  const courses = a.courses ?? [];

  return (
    <div className="flex min-h-screen flex-col" style={brandVars(brand) as CSSProperties}>
      <header style={{ background: brand, color: onBrand }}>
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3 sm:px-6">
          <span className="text-sm font-semibold opacity-90">{a.name}</span>
          <AccountLinks band />
        </div>
        <div className="mx-auto max-w-6xl px-4 pt-6 pb-10 sm:px-6 sm:pt-10 sm:pb-14">
          <div className="flex items-center gap-4">
            {a.logoUrl ? (
              <img src={a.logoUrl} alt="" className="h-16 w-16 shrink-0 rounded-full border-2 border-white/40 bg-surface object-cover sm:h-20 sm:w-20" />
            ) : (
              <span
                className="flex h-16 w-16 shrink-0 items-center justify-center rounded-full border-2 text-xl font-bold sm:h-20 sm:w-20"
                style={{ borderColor: onBrand, color: onBrand }}
                aria-hidden="true"
              >
                {initials(a.name)}
              </span>
            )}
            <div className="min-w-0">
              <h1 className="font-serif text-[1.875rem] leading-[1.1] font-semibold tracking-[-0.015em] sm:text-3xl">{a.name}</h1>
              <p className="mt-1 text-sm opacity-85">{plural(courses.length, "course")}</p>
            </div>
          </div>
          {a.headline && <p className="mt-5 max-w-2xl text-lg leading-snug opacity-95">{a.headline}</p>}
        </div>
      </header>

      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6 sm:py-10">
        {a.bio && (
          <section aria-labelledby="about" className="mb-10 max-w-[68ch]">
            <h2 id="about" className="text-xl font-semibold text-ink">
              About
            </h2>
            <Markdown source={a.bio} className="mt-3" />
          </section>
        )}
        <section aria-labelledby="courses">
          <h2 id="courses" className="mb-4 text-xl font-semibold text-ink">
            Courses
          </h2>
          {courses.length ? (
            <CourseGrid courses={courses} viaAcademy />
          ) : (
            <p className="rounded-panel border border-dashed border-slate-300 px-4 py-8 text-center text-sm text-muted">No courses published yet. Check back soon.</p>
          )}
        </section>
      </main>

      {a.showBranding && (
        <footer className="border-t border-line px-4 py-6 text-center text-xs text-muted">
          Courses hosted on{" "}
          <Link href="/" className="align-middle hover:underline">
            <Wordmark className="text-sm" />
          </Link>
          <span className="sr-only"> ({APP_NAME})</span>
        </footer>
      )}
    </div>
  );
}
