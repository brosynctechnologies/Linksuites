"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useState } from "react";
import { periodSuffix } from "@/lib/money";
import { searchHref, type SearchState } from "@/lib/search";
import type { ListingCardDto, MapPin, PricePeriod } from "@/lib/types";
import { ListIcon, MapIcon } from "../icons";
import { ListingCard } from "../listing-card";
import { SearchMap } from "../maps";
import { buttonClass, cx } from "../styles";

/**
 * Results: the list on the left and a sticky map on the right (desktop). On phones the list shows first and a
 * floating "Map" button switches to a full-screen map. A pin opens its listing; hovering a pin highlights its card,
 * and hovering a card highlights its pin.
 */
export function SearchView({
  state,
  items,
  pins,
  total,
  page,
  pageSize,
  center,
}: {
  state: SearchState;
  items: ListingCardDto[];
  pins: MapPin[];
  total: number;
  page: number;
  pageSize: number;
  center: [number, number] | null;
}) {
  const router = useRouter();
  const [view, setView] = useState<"list" | "map">("list");
  const [selected, setSelected] = useState<string | null>(null);
  const period: PricePeriod = state.purpose === "Sale" ? "Total" : state.purpose === "ShortLet" ? "Night" : "Year";
  const selectedCard = items.find((i) => i.id === selected) ?? null;

  const onSelect = useCallback(
    (id: string) => {
      setSelected(id);
      const el = document.getElementById(`listing-${id}`);
      if (el && window.matchMedia("(min-width: 1024px)").matches) {
        el.scrollIntoView({ block: "nearest", behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
      }
    },
    [],
  );
  const onSearchArea = useCallback(
    (bbox: string) => {
      // The map area replaces the named place, so moving outside it still finds homes.
      router.push(searchHref({ ...state, location: undefined, bbox, page: 1 }));
    },
    [router, state],
  );

  const pages = Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
  const link = (p: number) => searchHref({ ...state, page: p });

  return (
    <div className="lg:grid lg:grid-cols-[minmax(0,1fr)_minmax(22rem,44%)]">
      <div className="min-w-0 px-4 pb-28 sm:px-6 lg:pb-10">
        {items.length === 0 ? (
          <div className="rounded-panel border border-dashed border-slate-300 bg-surface px-6 py-12 text-center">
            <p className="font-semibold text-asphalt">No homes match these filters yet.</p>
            <p className="mt-1 text-sm text-muted">Try a wider area, fewer filters or a higher budget. Save the search and we&apos;ll email you when one appears.</p>
          </div>
        ) : (
          <ul className="space-y-3">
            {items.map((l) => (
              <li
                key={l.id}
                id={`listing-${l.id}`}
                onMouseEnter={() => setSelected(l.id)}
                onFocus={() => setSelected(l.id)}
                className={cx("rounded-panel", selected === l.id && "ring-2 ring-adire ring-offset-2 ring-offset-plaster")}
              >
                <ListingCard listing={l} layout="row" />
              </li>
            ))}
          </ul>
        )}
        {pages > 1 && (
          <nav className="mt-6 flex items-center justify-between gap-3 text-sm" aria-label="Pages">
            <Link
              href={link(page - 1)}
              className={cx(buttonClass("secondary"), page <= 1 && "pointer-events-none opacity-45")}
              aria-disabled={page <= 1}
              tabIndex={page <= 1 ? -1 : undefined}
            >
              Previous
            </Link>
            <span className="text-muted">
              Page {page} of {pages}
            </span>
            <Link
              href={link(page + 1)}
              className={cx(buttonClass("secondary"), page >= pages && "pointer-events-none opacity-45")}
              aria-disabled={page >= pages}
              tabIndex={page >= pages ? -1 : undefined}
            >
              Next
            </Link>
          </nav>
        )}
      </div>

      <div
        className={cx(
          "lg:sticky lg:top-0 lg:block lg:h-dvh lg:border-l lg:border-line",
          view === "map" ? "fixed inset-0 top-[var(--support-bar,0px)] z-40 block bg-plaster" : "hidden",
        )}
      >
        <SearchMap
          pins={pins}
          selectedId={selected}
          onSelect={onSelect}
          onSearchArea={onSearchArea}
          bbox={state.bbox}
          center={center}
          periodSuffix={periodSuffix(period)}
        />
        {view === "map" && selectedCard && (
          <div className="absolute inset-x-3 bottom-24 z-10 lg:hidden">
            <ListingCard listing={selectedCard} layout="row" className="shadow-float" />
          </div>
        )}
      </div>

      {/* Phones: list/map toggle */}
      <button
        type="button"
        onClick={() => setView((v) => (v === "list" ? "map" : "list"))}
        className="fixed bottom-[max(1.25rem,env(safe-area-inset-bottom))] left-1/2 z-50 inline-flex min-h-12 -translate-x-1/2 items-center gap-2 rounded-full bg-asphalt px-5 text-sm font-semibold text-white shadow-float lg:hidden"
      >
        {view === "list" ? (
          <>
            <MapIcon className="h-4 w-4" /> Map
          </>
        ) : (
          <>
            <ListIcon className="h-4 w-4" /> List
          </>
        )}
      </button>
    </div>
  );
}
