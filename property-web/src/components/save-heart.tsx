"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { api, toApiError } from "@/lib/api";
import { useAuth, useHydrated } from "@/lib/auth";
import type { ListingCardDto } from "@/lib/types";
import { HeartIcon } from "./icons";
import { cx } from "./styles";

// One shared read of the saved list per session user, so several hearts on a page cost one request.
let savedFor: string | null = null;
let savedIds: Promise<Set<string>> | null = null;

function loadSaved(userId: string): Promise<Set<string>> {
  if (savedFor !== userId || !savedIds) {
    savedFor = userId;
    savedIds = api<ListingCardDto[]>("/api/me/saved-listings")
      .then((list) => new Set((Array.isArray(list) ? list : []).map((l) => l.id)))
      .catch(() => new Set<string>());
  }
  return savedIds;
}

/** Save heart: toggles the listing in Saved homes; signed-out visitors are sent to log in and come back. */
export function SaveHeart({ listingId, className, withLabel }: { listingId: string; className?: string; withLabel?: boolean }) {
  const auth = useAuth();
  const hydrated = useHydrated();
  const pathname = usePathname();
  const [saved, setSaved] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const userId = auth?.user.id;

  useEffect(() => {
    if (!userId) return;
    let live = true;
    loadSaved(userId).then((ids) => live && setSaved(ids.has(listingId)));
    return () => {
      live = false;
    };
  }, [userId, listingId]);

  const base = cx(
    "inline-flex min-h-11 min-w-11 shrink-0 items-center justify-center gap-2 rounded-control border border-line bg-surface px-3 text-sm font-semibold text-asphalt hover:bg-slate-50",
    className,
  );

  if (!hydrated) return <span className={base} aria-hidden="true" />;
  if (!auth) {
    return (
      <Link href={`/login?next=${encodeURIComponent(pathname)}`} className={base} aria-label="Log in to save this home">
        <HeartIcon className="h-5 w-5" />
        {withLabel && "Save"}
      </Link>
    );
  }

  async function toggle() {
    const next = !saved;
    setBusy(true);
    setError(null);
    setSaved(next);
    try {
      await api(`/api/me/saved-listings/${listingId}`, { method: next ? "POST" : "DELETE" });
      const ids = await loadSaved(userId!);
      if (next) ids.add(listingId);
      else ids.delete(listingId);
    } catch (e) {
      setSaved(!next);
      setError(toApiError(e).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <span className="inline-flex flex-col items-end">
      <button type="button" onClick={toggle} disabled={busy || saved === null} aria-pressed={!!saved} className={base} aria-label={saved ? "Remove from saved homes" : "Save this home"}>
        <HeartIcon filled={!!saved} className={cx("h-5 w-5", saved && "text-brick")} />
        {withLabel && (saved ? "Saved" : "Save")}
      </button>
      {error && (
        <span role="alert" className="mt-1 text-xs text-brick">
          {error}
        </span>
      )}
    </span>
  );
}
