"use client";

import { useState } from "react";
import { api, toApiError, type ApiError } from "@/lib/api";
import type { Paged, PublicReviewDto } from "@/lib/types";
import { Button, ErrorBox } from "@/components/ui";
import { ReviewItem } from "./review-item";

/** Loads further pages of reviews below the server-rendered first page. */
export function MoreReviews({ path, total, firstPageCount, pageSize }: { path: string; total: number; firstPageCount: number; pageSize: number }) {
  const [items, setItems] = useState<PublicReviewDto[]>([]);
  const [page, setPage] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const shown = firstPageCount + items.length;
  if (total <= firstPageCount && items.length === 0) return null;

  async function more() {
    setBusy(true);
    setError(null);
    try {
      const next = page + 1;
      const res = await api<Paged<PublicReviewDto>>(`${path}?page=${next}`, { auth: false });
      setItems((xs) => [...xs, ...(res.items ?? [])]);
      setPage(next);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      {items.length > 0 && (
        <ul className="divide-y divide-line-soft border-t border-line-soft">
          {items.map((r, i) => (
            <ReviewItem key={`${r.createdAt}-${i}`} review={r} />
          ))}
        </ul>
      )}
      {error && <ErrorBox error={error} />}
      {shown < total && pageSize > 0 && (
        <Button variant="secondary" className="mt-4" loading={busy} onClick={more}>
          Show more reviews
        </Button>
      )}
    </>
  );
}
