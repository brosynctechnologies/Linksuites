import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense, type CSSProperties } from "react";
import { Rating } from "@/components/badges";
import { LearningPath } from "@/components/learning-path";
import { Markdown } from "@/components/markdown";
import { LoadProblem, SiteFooter, SiteHeader } from "@/components/site-chrome";
import { CheckIcon } from "@/components/icons";
import { brandVars, initials } from "@/lib/color";
import { formatCount, formatDay, formatLength, levelLabel, plural } from "@/lib/format";
import { first, serverGet } from "@/lib/server-api";
import type { CourseDetailDto, Paged, PublicReviewDto } from "@/lib/types";
import { BuyPanel } from "./buy-panel";
import { MoreReviews } from "./more-reviews";
import { PreviewButton } from "./preview";
import { ReviewItem } from "./review-item";

function detailPath(academySlug: string, courseSlug: string) {
  return `/api/catalog/courses/${encodeURIComponent(academySlug)}/${encodeURIComponent(courseSlug)}`;
}

export async function generateMetadata({ params }: PageProps<"/courses/[academySlug]/[courseSlug]">): Promise<Metadata> {
  const { academySlug, courseSlug } = await params;
  const res = await serverGet<CourseDetailDto>(detailPath(academySlug, courseSlug));
  if (!res.ok) return { title: "Course" };
  return {
    title: res.data.title,
    description: res.data.subtitle ?? `A course by ${res.data.academyName}.`,
    openGraph: res.data.thumbnailUrl ? { images: [res.data.thumbnailUrl] } : undefined,
  };
}

export default function CoursePage({ params, searchParams }: PageProps<"/courses/[academySlug]/[courseSlug]">) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <main className="flex-1 pb-24 lg:pb-0">
        <Suspense fallback={<CourseSkeleton />}>
          {Promise.all([params, searchParams]).then(([p, sp]) => (
            <Course academySlug={p.academySlug} courseSlug={p.courseSlug} viaAcademy={first(sp.via) === "academy"} />
          ))}
        </Suspense>
      </main>
      <SiteFooter />
    </div>
  );
}

function CourseSkeleton() {
  return (
    <div className="mx-auto max-w-6xl px-4 py-8 sm:px-6" aria-hidden="true">
      <div className="h-4 w-40 animate-pulse rounded bg-mist-2" />
      <div className="mt-4 h-10 w-3/4 animate-pulse rounded bg-mist-2" />
      <div className="mt-3 h-5 w-1/2 animate-pulse rounded bg-mist-2" />
    </div>
  );
}

async function Course({ academySlug, courseSlug, viaAcademy }: { academySlug: string; courseSlug: string; viaAcademy: boolean }) {
  const path = detailPath(academySlug, courseSlug);
  const [res, reviews] = await Promise.all([serverGet<CourseDetailDto>(path), serverGet<Paged<PublicReviewDto>>(`${path}/reviews?page=1`)]);
  if (!res.ok && res.status === 404) notFound();
  if (!res.ok) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <LoadProblem status={res.status} what="this course" />
      </div>
    );
  }
  const c = res.data;
  const sections = c.sections ?? [];
  const lessonsTotal = sections.reduce((n, s) => n + s.lessons.length, 0) || c.lessonsCount;
  const length = formatLength(c.durationSec);
  const reviewList = reviews.ok ? (reviews.data.items ?? []) : [];
  const reviewTotal = reviews.ok ? reviews.data.total : 0;
  const style = viaAcademy ? (brandVars(c.brandColor) as CSSProperties) : undefined;

  return (
    <div style={style}>
      <div className="mx-auto grid max-w-6xl gap-x-12 gap-y-6 px-4 py-6 sm:px-6 sm:py-10 lg:grid-cols-[minmax(0,1fr)_22rem] lg:grid-rows-[auto_1fr]">
        <div className="min-w-0 lg:col-start-1 lg:row-start-1">
          {/* Heading */}
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href={viaAcademy ? `/a/${c.academySlug}` : "/courses"} className="hover:text-ink hover:underline">
              {viaAcademy ? c.academyName : "Courses"}
            </Link>
            {c.categoryName && !viaAcademy && (
              <>
                <span aria-hidden="true"> / </span>
                <span>{c.categoryName}</span>
              </>
            )}
          </nav>
          <h1 className="mt-2 font-serif text-[1.875rem] leading-[1.15] font-semibold tracking-[-0.015em] text-ink sm:text-3xl">{c.title}</h1>
          {c.subtitle && <p className="mt-3 max-w-2xl text-lg text-slate-700">{c.subtitle}</p>}
          <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
            <Link href={`/a/${c.academySlug}`} className="flex items-center gap-2 font-semibold text-ink hover:underline">
              <TutorAvatar name={c.tutor?.name ?? c.academyName} logoUrl={c.tutor?.logoUrl} size="sm" />
              {c.tutor?.name || c.academyName}
            </Link>
            <Rating average={c.ratingAverage} count={c.ratingCount} />
            <span className="text-slate-700">{plural(c.studentsCount, "learner")}</span>
          </div>
          <dl className="mt-5 grid grid-cols-2 gap-x-6 gap-y-3 border-y border-line py-4 text-sm sm:grid-cols-4">
            <Fact label="Lessons" value={formatCount(lessonsTotal)} />
            <Fact label="Length" value={length || "Self-paced"} />
            <Fact label="Level" value={levelLabel(c.level) || "All levels"} />
            <Fact label="Updated" value={formatDay(c.updatedAt)} />
          </dl>

        </div>

        {/* One buy panel: after the heading on phones, a sticky column on desktop. */}
        <div className="lg:col-start-2 lg:row-span-2 lg:row-start-1">
          <div className="lg:sticky lg:top-6">
            <BuyPanelFor c={c} viaAcademy={viaAcademy} />
          </div>
        </div>

        <div className="min-w-0 lg:col-start-1 lg:row-start-2">

          {c.outcomes?.length > 0 && (
            <section className="mt-3 lg:mt-0" aria-labelledby="outcomes">
              <h2 id="outcomes" className="text-xl font-semibold text-ink">
                What you&apos;ll learn
              </h2>
              <ul className="mt-3 grid gap-x-8 gap-y-2.5 sm:grid-cols-2">
                {c.outcomes.map((o, i) => (
                  <li key={i} className="flex gap-2.5 text-[0.9375rem] text-slate-700">
                    <CheckIcon className="mt-1 h-4 w-4 shrink-0 text-lagoon-text" />
                    {o}
                  </li>
                ))}
              </ul>
            </section>
          )}

          <section className="mt-10" aria-labelledby="path">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 id="path" className="text-xl font-semibold text-ink">
                The path through this course
              </h2>
              <p className="text-sm text-muted">
                {plural(sections.length, "section")}, {plural(lessonsTotal, "lesson")}
                {length ? `, ${length}` : ""}
              </p>
            </div>
            <p className="mt-1 text-sm text-muted">Lessons marked Preview are free to watch.</p>
            <div className="mt-5 rounded-panel border border-line bg-surface px-4 py-5 sm:px-6">
              {sections.length ? (
                <LearningPath
                  sections={sections.map((s) => ({
                    id: s.id,
                    title: s.title,
                    lessons: s.lessons.map((l) => ({ ...l, isQuiz: l.type === "Quiz" })),
                  }))}
                  locked
                  renderAction={(l) => (l.isPreview ? <PreviewButton lessonId={l.id} title={l.title} /> : null)}
                  certificate={{ earned: false, label: "Course complete", note: "Finish every lesson and pass each quiz." }}
                />
              ) : (
                <p className="text-sm text-muted">The tutor hasn&apos;t added lessons yet.</p>
              )}
            </div>
          </section>

          {c.requirements?.length > 0 && (
            <section className="mt-10" aria-labelledby="requirements">
              <h2 id="requirements" className="text-xl font-semibold text-ink">
                Before you start
              </h2>
              <ul className="mt-3 list-disc space-y-1.5 pl-5 text-[0.9375rem] text-slate-700">
                {c.requirements.map((r, i) => (
                  <li key={i}>{r}</li>
                ))}
              </ul>
            </section>
          )}

          {c.description && (
            <section className="mt-10" aria-labelledby="about">
              <h2 id="about" className="text-xl font-semibold text-ink">
                About this course
              </h2>
              <Markdown source={c.description} className="mt-3 max-w-[68ch]" />
            </section>
          )}

          {c.tutor && (
            <section className="mt-10 border-t border-line pt-8" aria-labelledby="tutor">
              <h2 id="tutor" className="text-xl font-semibold text-ink">
                Your tutor
              </h2>
              <div className="mt-4 flex items-start gap-4">
                <TutorAvatar name={c.tutor.name} logoUrl={c.tutor.logoUrl} size="lg" />
                <div className="min-w-0">
                  <Link href={`/a/${c.academySlug}`} className="font-serif text-lg font-semibold text-ink hover:underline">
                    {c.tutor.name}
                  </Link>
                  {c.tutor.headline && <p className="text-sm text-muted">{c.tutor.headline}</p>}
                </div>
              </div>
              {c.tutor.bio && <Markdown source={c.tutor.bio} className="mt-4 max-w-[68ch]" />}
            </section>
          )}

          <section className="mt-10 border-t border-line pt-8" aria-labelledby="reviews">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <h2 id="reviews" className="text-xl font-semibold text-ink">
                Reviews
              </h2>
              <Rating average={c.ratingAverage} count={c.ratingCount} />
            </div>
            {reviewList.length === 0 ? (
              <p className="mt-3 text-sm text-muted">No reviews yet. Learners can review a course once they&apos;ve started it.</p>
            ) : (
              <>
                <ul className="mt-2 divide-y divide-line-soft">
                  {reviewList.map((r, i) => (
                    <ReviewItem key={`${r.createdAt}-${i}`} review={r} />
                  ))}
                </ul>
                <MoreReviews path={`${detailPath(c.academySlug, c.slug)}/reviews`} total={reviewTotal} firstPageCount={reviewList.length} pageSize={reviews.ok ? reviews.data.pageSize : 0} />
              </>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

function BuyPanelFor({ c, viaAcademy }: { c: CourseDetailDto; viaAcademy: boolean }) {
  return (
    <BuyPanel
      courseId={c.id}
      academySlug={c.academySlug}
      courseSlug={c.slug}
      priceMinor={c.priceMinor}
      currency={c.currency}
      isFree={c.isFree}
      lessonsCount={c.lessonsCount}
      durationSec={c.durationSec}
      language={c.language}
      channel={viaAcademy ? "Academy" : "Marketplace"}
    />
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="tnum font-semibold text-ink">{value}</dd>
    </div>
  );
}

function TutorAvatar({ name, logoUrl, size }: { name: string; logoUrl?: string | null; size: "sm" | "lg" }) {
  const cls = size === "sm" ? "h-7 w-7 text-[0.7rem]" : "h-14 w-14 text-base";
  return logoUrl ? (
    <img src={logoUrl} alt="" loading="lazy" className={`${cls} shrink-0 rounded-full border border-line bg-surface object-cover`} />
  ) : (
    <span className={`${cls} flex shrink-0 items-center justify-center rounded-full bg-lagoon font-bold text-on-lagoon`} aria-hidden="true">
      {initials(name)}
    </span>
  );
}
