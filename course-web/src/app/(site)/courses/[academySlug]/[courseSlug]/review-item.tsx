import { Stars } from "@/components/badges";
import { formatDay } from "@/lib/format";
import type { PublicReviewDto } from "@/lib/types";

export function ReviewItem({ review }: { review: PublicReviewDto }) {
  return (
    <li className="py-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-semibold text-ink">{review.studentName}</p>
        <p className="text-xs text-muted">{formatDay(review.createdAt)}</p>
      </div>
      <Stars value={review.rating} className="mt-1" />
      {review.comment && <p className="mt-2 max-w-[68ch] text-[0.9375rem] leading-relaxed text-slate-700">{review.comment}</p>}
    </li>
  );
}
