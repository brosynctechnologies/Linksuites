import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { Suspense, type CSSProperties } from "react";
import { Rating } from "@/components/badges";
import { LearningPath } from "@/components/learning-path";
import { Markdown } from "@/components/markdown";
import { LoadProblem, SiteFooter, SiteHeader, SiteMain } from "@/components/site-chrome";
import { CheckIcon } from "@/components/icons";
import { JsonLd } from "@/components/suite/sections";
import { brandVars, initials } from "@/lib/color";
import { coursePath, formatCount, formatDay, formatLength, levelLabel, plural } from "@/lib/format";
import { first, serverGet } from "@/lib/server-api";
import { absolute, pageOpenGraph } from "@/lib/site";
import type { CourseDetailDto, Paged, PublicReviewDto } from "@/lib/types";
import { BuyPanel } from "./buy-panel";
import { MoreReviews } from "./more-reviews";
import { PreviewButton } from "./preview";
import { ReviewItem } from "./review-item";

function detailPath(academySlug: string, courseSlug: string) {
  return `/api/catalog/courses/${encodeURIComponent(academySlug)}/${encodeURIComponent(courseSlug)}`;
}

function describe(c: CourseDetailDto) {
  return c.subtitle || `An online course from ${c.academyName} on LearnLink.`;
}

export async function generateMetadata({ params }: PageProps<"/courses/[academySlug]/[courseSlug]">): Promise<Metadata> {
  const { academySlug, courseSlug } = await params;
  const res = await serverGet<CourseDetailDto>(detailPath(academySlug, courseSlug));
  if (!res.ok) return { title: "Course" };
  const c = res.data;
  const url = coursePath(c.academySlug, c.slug);
  return {
    title: `${c.title}, from ${c.academyName}`,
    description: describe(c),
    alternates: { canonical: url },
    openGraph: pageOpenGraph({ title: c.title, description: describe(c), url, images: c.thumbnailUrl ? [c.thumbnailUrl] : undefined }),
  };
}

/** schema.org Course: the academy's own course and its own price, as learners see it. */
function courseJsonLd(c: CourseDetailDto) {
  const url = absolute(coursePath(c.academySlug, c.slug));
  return {
    "@context": "https://schema.org",
    "@type": "Course",
    name: c.title,
    description: describe(c),
    url,
    ...(c.thumbnailUrl ? { image: c.thumbnailUrl } : {}),
    ...(c.language ? { inLanguage: c.language } : {}),
    ...(c.categoryName ? { about: c.categoryName } : {}),
    educationalLevel: levelLabel(c.level) || "All levels",
    provider: { "@type": "Organization", name: c.academyName, url: absolute(`/a/${c.academySlug}`) },
    ...(c.ratingAverage && c.ratingCount > 0
      ? { aggregateRating: { "@type": "AggregateRating", ratingValue: c.ratingAverage.toFixed(1), ratingCount: c.ratingCount, bestRating: 5 } }
      : {}),
    offers: {
      "@type": "Offer",
      category: c.isFree ? "Free" : "Paid",
      price: c.isFree ? "0" : (c.priceMinor / 100).toFixed(2),
      priceCurrency: c.currency || "NGN",
      availability: "https://schema.org/InStock",
      url,
    },
    hasCourseInstance: {
      "@type": "CourseInstance",
      courseMode: "online",
      courseWorkload: c.durationSec ? `PT${Math.max(1, Math.round(c.durationSec / 60))}M` : undefined,
      instructor: c.tutor ? { "@type": "Person", name: c.tutor.name } : undefined,
    },
  };
}

export default function CoursePage({ params, searchParams }: PageProps<"/courses/[academySlug]/[courseSlug]">) {
  return (
    <div className="flex min-h-screen flex-col">
      <SiteHeader />
      <SiteMain className="pb-24 lg:pb-0">
        <Suspense fallback={<CourseSkeleton />}>
          {Promise.all([params, searchParams]).then(([p, sp]) => (
            <Course academySlug={p.academySlug} courseSlug={p.courseSlug} viaAcademy={first(sp.via) === "academy"} />
          ))}
        </Suspense>
      </SiteMain>
      <SiteFooter />
    </div>
  );
}

function CourseSkeleton() {
  return (
    <div className="ls-container py-8" aria-hidden="true">
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
      <div className="ls-container py-10">
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
      <JsonLd data={courseJsonLd(c)} />
      <div className="ls-container grid gap-x-12 gap-y-6 py-6 sm:py-10 lg:grid-cols-[minmax(0,1fr)_22rem] lg:grid-rows-[auto_1fr]">
        <div className="min-w-0 lg:col-start-1 lg:row-start-1">
          {/* Heading */}
          <nav aria-label="Breadcrumb" className="text-sm text-muted">
            <Link href={viaAcademy ? `/a/${c.academySlug}` : "/courses"} className="inline-flex min-h-11 items-center hover:text-ink hover:underline">
              {viaAcademy ? c.academyName : "Courses"}
            </Link>
            {c.categoryName && !viaAcademy && (
              <>
                <span aria-hidden="true"> / </span>
                <span>{c.categoryName}</span>
              </>
            )}
          </nav>
          <h1 className="ls-h2 mt-1 max-w-[24ch] text-ls-ink">{c.title}</h1>
          {c.subtitle && <p className="ls-lead mt-3 max-w-[38rem] text-ls-ink-2">{c.subtitle}</p>}
          <div className="mt-4 flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
            <Link href={`/a/${c.academySlug}`} className="flex min-h-11 items-center gap-2 font-semibold text-ink hover:underline">
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
              <h2 id="outcomes" className="ls-h3 text-ls-ink">
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
              <h2 id="path" className="ls-h3 text-ls-ink">
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
              <h2 id="requirements" className="ls-h3 text-ls-ink">
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
              <h2 id="about" className="ls-h3 text-ls-ink">
                About this course
              </h2>
              <Markdown source={c.description} className="mt-3 max-w-[68ch]" />
            </section>
          )}

          {c.tutor && (
            <section className="mt-10 border-t border-line pt-8" aria-labelledby="tutor">
              <h2 id="tutor" className="ls-h3 text-ls-ink">
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
              <h2 id="reviews" className="ls-h3 text-ls-ink">
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
  const px = size === "sm" ? 28 : 56;
  return logoUrl ? (
    <img src={logoUrl} alt="" width={px} height={px} loading="lazy" className={`${cls} shrink-0 rounded-full border border-line bg-surface object-cover`} />
  ) : (
    <span className={`${cls} flex shrink-0 items-center justify-center rounded-full bg-lagoon font-bold text-on-lagoon`} aria-hidden="true">
      {initials(name)}
    </span>
  );
}
