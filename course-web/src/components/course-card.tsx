import Link from "next/link";
import { coursePath, formatLength, levelLabel, plural } from "@/lib/format";
import { formatMoney } from "@/lib/money";
import type { CourseCardDto } from "@/lib/types";
import { Rating } from "./badges";
import { GraduationIcon } from "./icons";
import { cx } from "./styles";

/** Price as learners see it: "Free" in lime-dark (it's an invitation), paid in ink. */
export function Price({ priceMinor, currency, isFree, className }: { priceMinor: number; currency?: string; isFree: boolean; className?: string }) {
  if (isFree || priceMinor <= 0) return <span className={cx("font-bold text-lime-dark", className)}>Free</span>;
  return <span className={cx("tnum font-bold text-ink", className)}>{formatMoney(priceMinor, currency || "NGN")}</span>;
}

/** A small, lazy thumbnail; a quiet ink block with the course initial when there is none. */
export function Thumb({ src, title, className }: { src: string | null | undefined; title: string; className?: string }) {
  return (
    <div className={cx("relative aspect-video w-full overflow-hidden bg-ink-2", className)}>
      {src ? (
        <img src={src} alt="" loading="lazy" decoding="async" className="h-full w-full object-cover" />
      ) : (
        <div className="flex h-full w-full items-end p-3" aria-hidden="true">
          <GraduationIcon className="absolute top-3 right-3 h-5 w-5 text-white/25" />
          <span className="line-clamp-2 font-serif text-[0.95rem] leading-snug font-semibold text-white/85">{title}</span>
        </div>
      )}
    </div>
  );
}

/**
 * Course card: the one place a card grid is right (people scan thumbnails).
 * `viaAcademy` keeps learners in the academy's colours and credits the academy channel.
 */
export function CourseCard({ course, viaAcademy, className }: { course: CourseCardDto; viaAcademy?: boolean; className?: string }) {
  const length = formatLength(course.durationSec);
  return (
    <article className={cx("group relative flex flex-col overflow-hidden rounded-card bg-surface shadow-card has-[a:focus-visible]:outline-[3px] has-[a:focus-visible]:outline-offset-2 has-[a:focus-visible]:outline-lagoon has-[a:focus-visible]:outline-solid", className)}>
      <Thumb src={course.thumbnailUrl} title={course.title} />
      <div className="flex flex-1 flex-col p-3.5">
        <h3 className="line-clamp-2 font-serif text-[1.0625rem] leading-snug font-semibold text-ink">
          <Link href={coursePath(course.academySlug, course.slug, viaAcademy)} className="course-card-link after:absolute after:inset-0 after:rounded-card hover:underline">
            {course.title}
          </Link>
        </h3>
        <p className="mt-1 truncate text-sm text-muted">{course.academyName}</p>
        <div className="mt-1.5">
          <Rating average={course.ratingAverage} count={course.ratingCount} />
        </div>
        <div className="mt-auto flex items-end justify-between gap-2 pt-3">
          <Price priceMinor={course.priceMinor} currency={course.currency} isFree={course.isFree} className="text-base" />
          <span className="truncate text-right text-xs text-muted">
            {[plural(course.lessonsCount, "lesson"), length, levelLabel(course.level)].filter(Boolean).slice(0, 2).join(", ")}
          </span>
        </div>
      </div>
    </article>
  );
}

/** A horizontal row of course cards that scrolls on phones and becomes a grid on larger screens. */
export function CourseRow({ courses, viaAcademy }: { courses: CourseCardDto[]; viaAcademy?: boolean }) {
  return (
    <ul className="no-scrollbar -mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-2 sm:mx-0 sm:grid sm:grid-cols-2 sm:gap-4 sm:overflow-visible sm:px-0 md:grid-cols-3 lg:grid-cols-4">
      {courses.map((c) => (
        <li key={c.id} className="w-[72%] shrink-0 snap-start sm:w-auto">
          <CourseCard course={c} viaAcademy={viaAcademy} className="h-full" />
        </li>
      ))}
    </ul>
  );
}

/** Course grid for catalogue and academy pages. */
export function CourseGrid({ courses, viaAcademy }: { courses: CourseCardDto[]; viaAcademy?: boolean }) {
  return (
    <ul className="grid grid-cols-1 gap-4 min-[420px]:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
      {courses.map((c) => (
        <li key={c.id}>
          <CourseCard course={c} viaAcademy={viaAcademy} className="h-full" />
        </li>
      ))}
    </ul>
  );
}

/** Placeholder cards while a row streams in. */
export function CourseRowSkeleton({ count = 4 }: { count?: number }) {
  return (
    <ul className="no-scrollbar -mx-4 flex gap-3 overflow-hidden px-4 sm:mx-0 sm:grid sm:grid-cols-2 sm:gap-4 sm:px-0 md:grid-cols-3 lg:grid-cols-4" aria-hidden="true">
      {Array.from({ length: count }, (_, i) => (
        <li key={i} className="w-[72%] shrink-0 overflow-hidden rounded-card bg-surface sm:w-auto">
          <div className="aspect-video animate-pulse bg-mist-2" />
          <div className="space-y-2 p-3.5">
            <div className="h-4 w-4/5 animate-pulse rounded bg-mist-2" />
            <div className="h-3 w-1/2 animate-pulse rounded bg-mist-2" />
            <div className="h-4 w-1/4 animate-pulse rounded bg-mist-2" />
          </div>
        </li>
      ))}
    </ul>
  );
}
