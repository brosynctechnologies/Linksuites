"use client";

import Link from "next/link";
import { useState } from "react";
import { api, asList, toApiError, type ApiError } from "@/lib/api";
import { useApi } from "@/lib/use-api";
import type { ListingCardDto } from "@/lib/types";
import { HeartIcon } from "@/components/icons";
import { ListingCard } from "@/components/listing-card";
import { ApiErrorView, EmptyState, ErrorBox, PageHeader, PageLoader, buttonClass } from "@/components/ui";

export default function SavedHomesPage() {
  const { data, error, reload, setData } = useApi<ListingCardDto[]>("/api/me/saved-listings");
  const [actionError, setActionError] = useState<ApiError | null>(null);
  const list = asList(data);

  async function remove(id: string) {
    setActionError(null);
    try {
      await api(`/api/me/saved-listings/${id}`, { method: "DELETE" });
      setData((xs) => asList(xs).filter((x) => x.id !== id));
    } catch (e) {
      setActionError(toApiError(e));
    }
  }

  return (
    <div>
      <PageHeader title="Saved homes" subtitle="Homes you saved that are still on the market." />
      {actionError && <ErrorBox error={actionError} />}
      {!data && error && <ApiErrorView error={error} onRetry={reload} />}
      {!data && !error && <PageLoader />}
      {data && list.length === 0 && (
        <EmptyState
          icon={<HeartIcon />}
          title="No saved homes yet"
          text="Tap the heart on any listing to keep it here."
          action={
            <Link href="/rent" className={buttonClass("primary")}>
              Find a home
            </Link>
          }
        />
      )}
      {list.length > 0 && (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {list.map((l) => (
            <li key={l.id} className="flex flex-col gap-1.5">
              <ListingCard listing={l} className="flex-1" />
              <button type="button" onClick={() => remove(l.id)} className="min-h-11 self-start text-sm font-medium text-brick hover:underline">
                Remove from saved
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
